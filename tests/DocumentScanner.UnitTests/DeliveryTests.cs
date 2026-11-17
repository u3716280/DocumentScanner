using System.Text.Json;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;

/// <summary>A temporary session folder with rendered files, as the application prepares them before a delivery.</summary>
internal sealed class Job : IDisposable
{
    public readonly string Root = Path.Combine(Path.GetTempPath(), "ds-" + Guid.NewGuid().ToString("N"));
    public string Outbox => Path.Combine(Root, "outbox");
    public string Local => Path.Combine(Root, "local");
    public int Persisted;
    public Job() { Directory.CreateDirectory(Outbox); Directory.CreateDirectory(Local); }
    public DeliveryJournal Journal(SaveAction action, ExportFormat format, int files, FakeErp? erp = null)
    {
        var items = new List<DeliveryItem>();
        for (int i = 0; i < files; i++)
        {
            string path = Path.Combine(Outbox, $"render-{i}.{AttachmentNaming.Extension(format)}");
            File.WriteAllBytes(path, Enumerable.Range(0, 300 + i).Select(b => (byte)(b + i * 7)).ToArray());
            items.Add(DeliveryPlanner.Item(path, format == ExportFormat.Pdf ? null : i + 1));
        }
        var journal = new DeliveryJournal
        {
            SessionId = Guid.NewGuid(), Action = action, Format = format, Revision = "rev-1", Items = items, LocalFolder = action == SaveAction.Erp ? null : Local,
            Destination = erp == null ? null : new() { SiteId = FakeErp.Site.Id, SiteOrigin = FakeErp.Origin, Account = erp.User, DocType = erp.DocType, DocName = erp.DocName },
        };
        DeliveryPlanner.AssignNames(journal, AttachmentNaming.BaseName(erp?.DocType ?? "Sales Invoice", erp?.DocName ?? "ACC-INV-2611-0001"), erp?.Files.Select(f => f.FileName).ToArray() ?? []);
        return journal;
    }
    public void Persist() => Persisted++;
    public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
}

public sealed class DeliveryJournalTests
{
    [Fact] public void OutcomeIsCompletedOnlyWhenEveryFileReachedEveryDestination()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.LocalAndErp, ExportFormat.Jpg, 2, erp);
        Assert.Equal(DeliveryOutcome.RetryRequired, DeliveryStatus.Outcome(journal)); Assert.True(DeliveryStatus.HasRetryableWork(journal));
        journal.LocalComplete = true; foreach (var i in journal.Items) i.LocalDone = true;
        Assert.Equal(DeliveryOutcome.PartiallyCompleted, DeliveryStatus.Outcome(journal));
        journal.Items[0].State = UploadState.Succeeded; journal.Items[1].State = UploadState.Unknown;
        Assert.Equal(DeliveryOutcome.PartiallyCompleted, DeliveryStatus.Outcome(journal)); Assert.True(DeliveryStatus.HasUnknown(journal)); Assert.False(DeliveryStatus.IsTerminal(journal));
        journal.Items[1].State = UploadState.Failed; journal.Items[1].Retryable = false;
        Assert.Equal(DeliveryOutcome.PartiallyCompleted, DeliveryStatus.Outcome(journal)); Assert.True(DeliveryStatus.IsTerminal(journal));
        journal.Items[1].State = UploadState.Succeeded;
        Assert.Equal(DeliveryOutcome.Completed, DeliveryStatus.Outcome(journal)); Assert.Equal("Completed", DeliveryStatus.Text(DeliveryOutcome.Completed));
        var failed = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp); failed.Items[0].State = UploadState.Failed; failed.Items[0].Retryable = false;
        Assert.Equal(DeliveryOutcome.Failed, DeliveryStatus.Outcome(failed));
    }

    [Fact] public void JournalRoundTripsThroughJsonWithoutSecrets()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.LocalAndErp, ExportFormat.Jpg, 2, erp);
        journal.Items[0].State = UploadState.Succeeded; journal.Items[0].ServerFile = "FILE-001"; journal.Items[1].Error = "ERPNext did not answer in time."; journal.RetryCount = 2;
        string path = Path.Combine(job.Root, "delivery.json"); JsonFiles.Write(path, journal);
        string text = File.ReadAllText(path); var restored = JsonFiles.Read<DeliveryJournal>(path);
        Assert.Equal(DeliveryJournal.CurrentSchema, restored.SchemaVersion); Assert.Equal(journal.OperationId, restored.OperationId); Assert.Equal(journal.Destination, restored.Destination);
        Assert.Equal(journal.Items.Select(i => (i.Name, i.Sha256, i.Md5, i.State, i.ServerFile, i.Page)), restored.Items.Select(i => (i.Name, i.Sha256, i.Md5, i.State, i.ServerFile, i.Page)));
        Assert.Equal(2, restored.RetryCount); Assert.True(restored.NeedsLocal); Assert.True(restored.NeedsUpload);
        foreach (string secret in new[] { FakeErp.Credential.Secret, FakeErp.Credential.Key, "Authorization", "api_secret", "password" }) Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0, true)][InlineData(1, true)][InlineData(2, false)][InlineData(3, false)][InlineData(-1, false)]
    public void OnlyDraftAndSubmittedDocumentsAcceptUploads(int docstatus, bool allowed) => Assert.Equal(allowed, DocStatusRules.AllowsUpload(docstatus));

    [Fact] public void SaveButtonsFollowTheLaunchContext()
    {
        Assert.True(SaveActionRules.Enabled(SaveAction.Local, false, true, false));
        Assert.False(SaveActionRules.Enabled(SaveAction.Erp, false, true, false)); Assert.False(SaveActionRules.Enabled(SaveAction.LocalAndErp, false, true, false));
        foreach (var action in Enum.GetValues<SaveAction>()) { Assert.True(SaveActionRules.Enabled(action, true, true, false)); Assert.False(SaveActionRules.Enabled(action, true, false, false)); Assert.False(SaveActionRules.Enabled(action, true, true, true)); }
        Assert.Equal(SaveAction.Erp, SaveActionRules.Primary(true)); Assert.Equal(SaveAction.Local, SaveActionRules.Primary(false));
    }

    [Fact] public void RevisionChangesWithPagesOrderEditsAndFormat()
    {
        var a = new PageData { OriginalFile = "a", ThumbnailFile = "a" }; var b = new PageData { OriginalFile = "b", ThumbnailFile = "b" };
        string Rev(IEnumerable<PageData> pages, ExportFormat f = ExportFormat.Pdf) => DeliveryRevision.Compute(pages, f, OutputPaper.Original, true, 90);
        string first = Rev([a, b]);
        Assert.Equal(first, Rev([a, b])); Assert.NotEqual(first, Rev([b, a])); Assert.NotEqual(first, Rev([a])); Assert.NotEqual(first, Rev([a with { Rotation = 90 }, b])); Assert.NotEqual(first, Rev([a, b], ExportFormat.Jpg));
    }

    [Fact] public void NamesAreChosenFreeOnBothSidesAndSettledFilesKeepTheirs()
    {
        using var job = new Job(); var erp = new FakeErp();
        erp.Files.Add(("FILE-X", "SI_ACC-INV-2611-0001_P002.jpg", "other", true));
        File.WriteAllText(Path.Combine(job.Local, "SI_ACC-INV-2611-0001_P001.jpg"), "someone else's file");
        var journal = job.Journal(SaveAction.LocalAndErp, ExportFormat.Jpg, 3, erp);
        Assert.Equal(new[] { "SI_ACC-INV-2611-0001_P001_001.jpg", "SI_ACC-INV-2611-0001_P002_001.jpg", "SI_ACC-INV-2611-0001_P003.jpg" }, journal.Items.Select(i => i.Name));
        journal.Items[0].LocalDone = true; journal.Items[2].State = UploadState.Unknown;
        var renamed = DeliveryPlanner.AssignNames(journal, "SI_ACC-INV-2611-0001", ["SI_ACC-INV-2611-0001_P002.jpg", "SI_ACC-INV-2611-0001_P002_001.jpg"]);
        Assert.Equal("SI_ACC-INV-2611-0001_P001_001.jpg", journal.Items[0].Name); Assert.Equal("SI_ACC-INV-2611-0001_P002_002.jpg", journal.Items[1].Name); Assert.Equal("SI_ACC-INV-2611-0001_P003.jpg", journal.Items[2].Name);
        Assert.Same(journal.Items[1], Assert.Single(renamed));
    }
}

public sealed class LocalDeliveryTests
{
    [Fact] public void SavesEveryFileUnderItsAssignedNameAndNeverOverwrites()
    {
        using var job = new Job();
        var journal = job.Journal(SaveAction.Local, ExportFormat.Png, 2);
        Assert.True(LocalDelivery.Complete(journal, job.Persist));
        Assert.True(journal.LocalComplete); Assert.All(journal.Items, i => { Assert.True(i.LocalDone); Assert.Equal(File.ReadAllBytes(i.File), File.ReadAllBytes(i.LocalDestination!)); });
        Assert.Equal(new[] { "SI_ACC-INV-2611-0001_P001.png", "SI_ACC-INV-2611-0001_P002.png" }, Directory.GetFiles(job.Local).Select(Path.GetFileName).Order());
        Assert.True(LocalDelivery.Complete(journal, job.Persist)); Assert.Equal(2, Directory.GetFiles(job.Local).Length);
    }

    [Fact] public void PartialSaveKeepsFinishedFilesAndRetryWritesOnlyTheRest()
    {
        using var job = new Job();
        var journal = job.Journal(SaveAction.LocalAndErp, ExportFormat.Jpg, 3);
        string blocked = Path.Combine(job.Local, journal.Items[1].Name); File.WriteAllText(blocked, "appeared after the name check");
        Assert.False(LocalDelivery.Complete(journal, job.Persist));
        Assert.False(journal.LocalComplete); Assert.True(journal.Items[0].LocalDone); Assert.False(journal.Items[1].LocalDone); Assert.False(journal.Items[2].LocalDone);
        Assert.Contains("Saved some files", journal.LocalError); Assert.Equal("appeared after the name check", File.ReadAllText(blocked));
        Assert.Equal(DeliveryOutcome.PartiallyCompleted, DeliveryStatus.Outcome(journal));
        DateTime firstWritten = File.GetLastWriteTimeUtc(journal.Items[0].LocalDestination!);
        DeliveryPlanner.AssignNames(journal, "SI_ACC-INV-2611-0001", []);
        Assert.Equal("SI_ACC-INV-2611-0001_P002_001.jpg", journal.Items[1].Name);
        Assert.True(LocalDelivery.Complete(journal, job.Persist)); Assert.Null(journal.LocalError);
        Assert.Equal(firstWritten, File.GetLastWriteTimeUtc(journal.Items[0].LocalDestination!)); Assert.Equal(4, Directory.GetFiles(job.Local).Length);
    }

    [Fact] public void ChangedArtifactOrUnsafeNameStopsTheSave()
    {
        using var job = new Job();
        var changed = job.Journal(SaveAction.Local, ExportFormat.Pdf, 1); File.AppendAllText(changed.Items[0].File, "x");
        Assert.False(LocalDelivery.Complete(changed, job.Persist)); Assert.Empty(Directory.GetFiles(job.Local));
        var unsafeName = job.Journal(SaveAction.Local, ExportFormat.Pdf, 1); unsafeName.Items[0].Name = "..\\..\\outside.pdf";
        Assert.False(LocalDelivery.Complete(unsafeName, job.Persist)); Assert.False(File.Exists(Path.Combine(job.Root, "outside.pdf")));
    }
}

public sealed class UploadRunnerTests
{
    private static async Task Run(DeliveryJournal journal, FakeErp erp, Job job, CancellationToken ct = default) { using var client = erp.Client(); await UploadRunner.Run(journal, client, job.Persist, ct); }

    [Fact] public async Task SendsEveryFilePrivatelyAndRecordsTheServerRecord()
    {
        using var job = new Job(); var erp = new FakeErp { DocStatus = 1 };
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Jpg, 3, erp);
        await Run(journal, erp, job);
        Assert.Equal(DeliveryOutcome.Completed, DeliveryStatus.Outcome(journal)); Assert.Equal(3, erp.Uploads.Count);
        Assert.Equal(journal.Items.Select(i => i.Name), erp.Uploads.Select(u => u.FileName));
        Assert.All(journal.Items, i => { Assert.NotNull(i.ServerFile); Assert.Equal(i.Name, i.ServerFileName); Assert.Equal(1, i.Attempts); Assert.Null(i.Error); });
        Assert.Equal(1, journal.Destination!.DocStatus); Assert.True(job.Persisted >= 6);
    }

    [Fact] public async Task CancelledDocumentStopsBeforeAnythingIsSent()
    {
        using var job = new Job(); var erp = new FakeErp { DocStatus = 2 };
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        var blocked = await Assert.ThrowsAsync<DestinationBlockedException>(() => Run(journal, erp, job));
        Assert.Equal(DocStatusRules.CancelledMessage, blocked.Message); Assert.Empty(erp.Uploads); Assert.Equal(UploadState.Pending, journal.Items[0].State);
        Assert.True(File.Exists(journal.Items[0].File)); Assert.Equal(2, journal.Destination!.DocStatus);
    }

    [Fact] public async Task DocumentCancelledDuringTheRunStopsTheRemainingFiles()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Jpg, 3, erp);
        erp.OnUpload = (n, _) => { if (n == 1) erp.DocStatus = 2; return null; };
        await Assert.ThrowsAsync<DestinationBlockedException>(() => Run(journal, erp, job));
        Assert.Single(erp.Uploads); Assert.Equal(new[] { UploadState.Succeeded, UploadState.Pending, UploadState.Pending }, journal.Items.Select(i => i.State));
        Assert.Equal(DeliveryOutcome.PartiallyCompleted, DeliveryStatus.Outcome(journal));
    }

    [Fact] public async Task MissingDocumentAndLostPermissionStopTheJobWithTheirOwnError()
    {
        using var job = new Job(); var erp = new FakeErp { DocumentExists = false };
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Jpg, 2, erp);
        Assert.Equal(ErpErrorKind.NotFound, (await Assert.ThrowsAsync<ErpException>(() => Run(journal, erp, job))).Kind); Assert.Empty(erp.Uploads);
        erp.DocumentExists = true; erp.CanWrite = false;
        Assert.Equal(ErpErrorKind.Permission, (await Assert.ThrowsAsync<ErpException>(() => Run(journal, erp, job))).Kind);
        Assert.Single(erp.Uploads); Assert.Equal(UploadState.Failed, journal.Items[0].State); Assert.True(journal.Items[0].Retryable); Assert.Equal(UploadState.Pending, journal.Items[1].State);
        erp.CanWrite = true; await Run(journal, erp, job); Assert.Equal(DeliveryOutcome.Completed, DeliveryStatus.Outcome(journal));
    }

    [Fact] public async Task AuthenticationFailureAndChangedAccountSendNothing()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        erp.Authenticated = false;
        Assert.Equal(ErpErrorKind.Authentication, (await Assert.ThrowsAsync<ErpException>(() => Run(journal, erp, job))).Kind);
        erp.Authenticated = true; erp.User = "someone.else@example.com";
        await Assert.ThrowsAsync<DestinationBlockedException>(() => Run(journal, erp, job));
        using var other = new FrappeClient(new ErpSite { Name = "Other", Url = FakeErp.Origin }, FakeErp.Credential, erp);
        await Assert.ThrowsAsync<DestinationBlockedException>(() => UploadRunner.Run(journal, other, job.Persist, default));
        Assert.Empty(erp.Uploads); Assert.Equal(UploadState.Pending, journal.Items[0].State);
    }

    [Fact] public async Task TimeoutAfterTheServerStoredTheFileIsReconciledWithoutASecondUpload()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        erp.OnUpload = (_, _) => FakeErp.UploadBehaviour.StoreThenTimeout;
        await Run(journal, erp, job);
        Assert.Equal(UploadState.Unknown, journal.Items[0].State); Assert.Contains("not known", journal.Items[0].Error);
        Assert.NotEqual(DeliveryOutcome.Completed, DeliveryStatus.Outcome(journal)); Assert.True(DeliveryStatus.HasUnknown(journal));
        erp.OnUpload = null; await Run(journal, erp, job);
        Assert.Single(erp.Uploads); Assert.Single(erp.Files); Assert.Equal(UploadState.Succeeded, journal.Items[0].State); Assert.Equal("FILE-001", journal.Items[0].ServerFile);
    }

    [Theory]
    [InlineData(FakeErp.UploadBehaviour.TimeoutBeforeStore)]
    [InlineData(FakeErp.UploadBehaviour.Disconnect)]
    [InlineData(FakeErp.UploadBehaviour.ServerError)]
    public async Task FailureWithoutAnAnswerIsUnknownAndRetrySendsBecauseNothingIsAttached(FakeErp.UploadBehaviour behaviour)
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        erp.OnUpload = (_, _) => behaviour;
        await Run(journal, erp, job);
        Assert.Equal(UploadState.Unknown, journal.Items[0].State); Assert.Empty(erp.Files);
        erp.OnUpload = null; await Run(journal, erp, job);
        Assert.Equal(2, erp.Uploads.Count); Assert.Single(erp.Files); Assert.Equal(UploadState.Succeeded, journal.Items[0].State); Assert.Equal(2, journal.Items[0].Attempts);
    }

    [Fact] public async Task UnreadableAnswerAfterStoringIsUnknownThenReconciled()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        erp.OnUpload = (_, _) => FakeErp.UploadBehaviour.StoreThenGarbage;
        await Run(journal, erp, job); Assert.Equal(UploadState.Unknown, journal.Items[0].State);
        await Run(journal, erp, job); Assert.Single(erp.Uploads); Assert.Equal(UploadState.Succeeded, journal.Items[0].State);
    }

    [Fact] public async Task PartialFailureRetriesOnlyTheFilesThatAreNotConfirmed()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Jpg, 4, erp);
        erp.OnUpload = (n, _) => n == 2 ? FakeErp.UploadBehaviour.TimeoutBeforeStore : n == 3 ? FakeErp.UploadBehaviour.StoreThenTimeout : null;
        await Run(journal, erp, job);
        Assert.Equal(new[] { UploadState.Succeeded, UploadState.Unknown, UploadState.Unknown, UploadState.Succeeded }, journal.Items.Select(i => i.State));
        Assert.Equal(DeliveryOutcome.PartiallyCompleted, DeliveryStatus.Outcome(journal));
        erp.OnUpload = null; erp.Uploads.Clear(); await Run(journal, erp, job);
        Assert.Equal(journal.Items[1].Name, Assert.Single(erp.Uploads).FileName); Assert.Equal(4, erp.Files.Count); Assert.Equal(DeliveryOutcome.Completed, DeliveryStatus.Outcome(journal));
        erp.Uploads.Clear(); await Run(journal, erp, job); Assert.Empty(erp.Uploads);
    }

    [Fact] public async Task ContentAlreadyAttachedCountsAsDelivered()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        erp.Files.Add(("FILE-OLD", "earlier-name.pdf", journal.Items[0].Md5, true));
        await Run(journal, erp, job);
        Assert.Empty(erp.Uploads); Assert.Equal(UploadState.Succeeded, journal.Items[0].State); Assert.Equal("FILE-OLD", journal.Items[0].ServerFile); Assert.Equal("earlier-name.pdf", journal.Items[0].ServerFileName);
        var publicCopy = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp); erp.Files.Clear(); erp.Files.Add(("FILE-PUB", "x.pdf", publicCopy.Items[0].Md5, false));
        await Run(publicCopy, erp, job); Assert.Single(erp.Uploads);
    }

    [Fact] public async Task ServerRenameIsReportedAsItIs()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        erp.OnUpload = (_, _) => FakeErp.UploadBehaviour.StoreUnderOtherName;
        await Run(journal, erp, job);
        Assert.Equal(UploadState.Succeeded, journal.Items[0].State); Assert.Equal("SI_ACC-INV-2611-0001.pdf", journal.Items[0].Name); Assert.Equal("SI_ACC-INV-2611-0001a1b2c3.pdf", journal.Items[0].ServerFileName);
    }

    [Fact] public async Task OversizedAndChangedFilesAreRefusedWithoutUploadAndAreNotRetryable()
    {
        using var job = new Job(); var erp = new FakeErp { MaxFileSize = 301 };
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Jpg, 3, erp);
        File.AppendAllText(journal.Items[0].File, "tampered");
        await Run(journal, erp, job);
        Assert.Equal(journal.Items[1].Name, Assert.Single(erp.Uploads).FileName);
        Assert.Equal(new[] { UploadState.Failed, UploadState.Succeeded, UploadState.Failed }, journal.Items.Select(i => i.State));
        Assert.False(journal.Items[0].Retryable); Assert.False(journal.Items[2].Retryable); Assert.Contains("accepts at most", journal.Items[2].Error);
        Assert.True(DeliveryStatus.IsTerminal(journal)); Assert.Equal(DeliveryOutcome.PartiallyCompleted, DeliveryStatus.Outcome(journal));
        erp.Uploads.Clear(); await Run(journal, erp, job); Assert.Empty(erp.Uploads);
    }

    [Fact] public async Task ServerSideSizeRejectionIsTerminalForThatFile()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Pdf, 1, erp);
        erp.OnUpload = (_, _) => FakeErp.UploadBehaviour.TooLarge;
        await Run(journal, erp, job);
        Assert.Equal(UploadState.Failed, journal.Items[0].State); Assert.False(journal.Items[0].Retryable); Assert.Equal(DeliveryOutcome.Failed, DeliveryStatus.Outcome(journal));
    }

    [Fact] public async Task CancellingMidUploadLeavesTheResultUnknownAndFilesIntact()
    {
        using var job = new Job(); var erp = new FakeErp(); using var cts = new CancellationTokenSource();
        var journal = job.Journal(SaveAction.Erp, ExportFormat.Jpg, 2, erp);
        erp.OnUpload = (_, _) => { cts.Cancel(); return FakeErp.UploadBehaviour.StoreThenTimeout; };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Run(journal, erp, job, cts.Token));
        Assert.Equal(UploadState.Unknown, journal.Items[0].State); Assert.Equal(UploadState.Pending, journal.Items[1].State); Assert.All(journal.Items, i => Assert.True(File.Exists(i.File)));
    }

    [Fact] public async Task LocalOnlyJobNeverContactsErpNext()
    {
        using var job = new Job(); var erp = new FakeErp();
        var journal = job.Journal(SaveAction.Local, ExportFormat.Pdf, 1, erp);
        await Run(journal, erp, job); Assert.Empty(erp.Requests);
    }
}

public sealed class SettingsUpgradeTests
{
    [Fact] public void VersionOneErpNextSettingsAreRemovedAndProfilesKept()
    {
        string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root); Directory.CreateDirectory(Path.GetDirectoryName(paths.Config)!);
            var siteId = Guid.NewGuid(); string folder = JsonSerializer.Serialize(root);
            File.WriteAllText(paths.Config, "{\"SchemaVersion\":1,\"OutputFolder\":" + folder + ",\"Output\":{\"Folder\":" + folder + ",\"Prefix\":\"POD\"},\"Erp\":{\"SiteId\":\"" + siteId + "\",\"DocType\":\"Delivery Note\",\"Private\":true},"
                + "\"Sites\":[{\"Id\":\"" + siteId + "\",\"Name\":\"Old\",\"Url\":\"https://old.example.com\"}],"
                + "\"Profiles\":[{\"SchemaVersion\":2,\"Id\":\"" + Guid.NewGuid() + "\",\"Name\":\"Invoices\",\"Device\":{\"Key\":\"d\",\"Name\":\"Scanner\",\"Driver\":\"Twain\",\"Id\":\"id\"},\"Settings\":{\"Dpi\":300},\"Output\":{\"Folder\":" + folder + "},\"Erp\":{\"DocType\":\"Sales Order\"}}]}");
            var store = new SettingsStore(paths); var value = store.Load();
            Assert.Equal(AppSettings.CurrentSchema, value.SchemaVersion); Assert.Empty(value.Sites); Assert.Equal(siteId, Assert.Single(store.RemovedSiteIds)); Assert.Contains("ERPNext", store.LoadWarning);
            Assert.Equal("Invoices", Assert.Single(value.Profiles).Name); Assert.Equal("POD", value.Output.Prefix); Assert.NotEmpty(value.AttachmentDescriptions);
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(paths.Config)!, "*.before-2.0-*"));
            string saved = File.ReadAllText(paths.Config); Assert.DoesNotContain("old.example.com", saved); Assert.DoesNotContain("\"Erp\"", saved);
            var again = new SettingsStore(paths); again.Load(); Assert.Null(again.LoadWarning); Assert.Empty(again.RemovedSiteIds);
            store.Save(value with { Sites = [new ErpSite { Name = "New", Url = "https://erp.example.com" }] }); Assert.Single(new SettingsStore(paths).Load().Sites);
        }
        finally { Directory.Delete(root, true); }
    }
}
