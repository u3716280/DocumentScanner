using System.Security.Cryptography;
using DocumentScanner.Core;
namespace DocumentScanner.Infrastructure;

public static class FileHashes
{
    public static string Sha256(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(); }
    // MD5 is how Frappe identifies file content (File.content_hash). It is used for matching only, never for security.
    public static string Md5(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(MD5.HashData(stream)).ToLowerInvariant(); }
}

/// <summary>Builds a delivery from rendered files and gives every file its final name before anything is written.</summary>
public static class DeliveryPlanner
{
    public static DeliveryItem Item(string file, int? page) => new() { File = file, Sha256 = FileHashes.Sha256(file), Md5 = FileHashes.Md5(file), Size = new FileInfo(file).Length, Page = page };

    /// <summary>
    /// Names the files that have not reached any destination yet. A name counts as taken when it exists in the local
    /// folder (if the job saves locally) or on the ERPNext document (if it uploads), so both sides get the same name.
    /// Returns the files whose name had to be changed.
    /// </summary>
    public static IReadOnlyList<DeliveryItem> AssignNames(DeliveryJournal journal, string baseName, IReadOnlyCollection<string> serverNames)
    {
        var server = new HashSet<string>(serverNames, StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<string>(journal.Items.Where(Settled).Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        var renamed = new List<DeliveryItem>();
        foreach (var item in journal.Items.Where(i => !Settled(i)))
        {
            item.Name = AttachmentNaming.Resolve(baseName, journal.Format, item.Page, name =>
                used.Contains(name) || journal.NeedsUpload && server.Contains(name) || journal.NeedsLocal && journal.LocalFolder != null && File.Exists(Path.Combine(journal.LocalFolder, name)));
            item.LocalDestination = null; used.Add(item.Name);
            if (AttachmentNaming.WasRenamed(baseName, journal.Format, item.Page, item.Name)) renamed.Add(item);
        }
        return renamed;
        static bool Settled(DeliveryItem item) => item.LocalDone || item.State is UploadState.Succeeded or UploadState.Unknown;
    }
}

public static class LocalDelivery
{
    /// <summary>
    /// Copies the frozen files to the local folder under their assigned names. Never overwrites: a different file
    /// with the same name stops the save, and files already written are kept. Returns true when all are saved.
    /// </summary>
    public static bool Complete(DeliveryJournal journal, Action persist)
    {
        if (!journal.NeedsLocal) return true;
        try
        {
            string folder = Path.GetFullPath(journal.LocalFolder ?? throw new ArgumentException("No local folder was chosen."));
            Directory.CreateDirectory(folder);
            foreach (var item in journal.Items.Where(i => !i.LocalDone))
            {
                FilenameRules.Validate(item.Name);
                if (FileHashes.Sha256(item.File) != item.Sha256) throw new InvalidDataException("A prepared file changed on disk. Start the save again.");
                string destination = Path.Combine(folder, item.Name);
                // The name is a single validated segment, so this only guards against a journal edited by hand.
                if (!string.Equals(Path.TrimEndingDirectorySeparator(Path.GetDirectoryName(Path.GetFullPath(destination))!), Path.TrimEndingDirectorySeparator(folder), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    throw new InvalidDataException("Unsafe local file name.");
                if (File.Exists(destination))
                {
                    if (FileHashes.Sha256(destination) != item.Sha256) throw new IOException($"A different file named {item.Name} already exists in the folder. Nothing was overwritten.");
                }
                else
                {
                    string temporary = SafeOutput.Temporary(folder, Path.GetExtension(item.Name));
                    try { File.Copy(item.File, temporary); File.Move(temporary, destination, false); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                item.LocalDestination = destination; item.LocalDone = true; persist();
            }
            journal.LocalComplete = true; journal.LocalError = null; persist(); return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
        {
            journal.LocalComplete = false;
            journal.LocalError = (journal.Items.Any(i => i.LocalDone) ? "Saved some files, but not all. Files already saved are kept. " : "Nothing was saved to the folder. ") + e.Message;
            persist(); return false;
        }
    }
}

/// <summary>Raised when the destination can no longer accept files. Scanned pages are never touched by it.</summary>
public sealed class DestinationBlockedException(string message) : Exception(message);

public static class UploadRunner
{
    /// <summary>
    /// Sends the files that are not confirmed yet, one at a time. Before each file the document and its attachments
    /// are read again: a cancelled document stops the run, and a file whose content is already attached is recorded
    /// as delivered instead of being sent a second time. That check is how a timeout is reconciled; the standard
    /// API has no operation id, so this narrows the chance of a duplicate but cannot rule it out.
    /// </summary>
    public static async Task Run(DeliveryJournal journal, FrappeClient client, Action persist, CancellationToken ct)
    {
        if (!journal.NeedsUpload) return;
        var target = journal.Destination ?? throw new InvalidOperationException("This job has no ERPNext destination.");
        if (client.Site.Id != target.SiteId || !SiteOrigins.Same(client.Origin, target.SiteOrigin)) throw new DestinationBlockedException("The ERPNext site of this job is no longer configured the same way. Nothing was sent.");
        var candidates = journal.Items.Where(i => i.State != UploadState.Succeeded && (i.State != UploadState.Failed || i.Retryable)).ToArray();
        if (candidates.Length == 0) return;

        string account = await client.GetLoggedUser(ct);
        if (!string.Equals(account, target.Account, StringComparison.OrdinalIgnoreCase)) throw new DestinationBlockedException($"This job was prepared as {target.Account}, but the stored credentials now belong to {account}. Nothing was sent.");
        long limit = await client.GetMaxFileSize(ct);

        foreach (var item in candidates)
        {
            ct.ThrowIfCancellationRequested();
            // Before: nothing sent. Sending: the request is in flight. Verifying: ERPNext accepted it and the result is being confirmed.
            var phase = Phase.Before;
            bool wasUnknown = item.State == UploadState.Unknown;
            try
            {
                var fresh = await client.GetDocument(target.DocType, target.DocName, account, ct);
                journal.Destination = fresh; target = fresh;
                if (!DocStatusRules.AllowsUpload(fresh.DocStatus)) { persist(); throw new DestinationBlockedException(DocStatusRules.CancelledMessage); }

                var attached = await client.ListAttachments(target.DocType, target.DocName, ct);
                if (attached.FirstOrDefault(a => a.IsPrivate && a.ContentHash == item.Md5) is { } existing) { Confirm(item, existing); persist(); continue; }

                if (!File.Exists(item.File) || FileHashes.Sha256(item.File) != item.Sha256) { Reject(item, "The prepared file is missing or changed. Start a new send."); persist(); continue; }
                if (item.Size > limit) { Reject(item, $"The file is {Megabytes(item.Size)}; this ERPNext site accepts at most {Megabytes(limit)}. Send fewer pages or save it to a folder."); persist(); continue; }

                // From here the server may hold the file even if no answer arrives, so the state is Unknown until confirmed.
                item.State = UploadState.Unknown; item.Error = null; item.Attempts++; persist(); phase = Phase.Sending;
                var uploaded = await client.Upload(target.DocType, target.DocName, item.File, item.Name, ct);
                phase = Phase.Verifying;
                var after = await client.ListAttachments(target.DocType, target.DocName, ct);
                if (after.FirstOrDefault(a => a.Name == uploaded.Name) is not { } verified)
                { item.Error = "ERPNext accepted the file, but it does not appear on the document yet. Retry will check again before sending."; persist(); continue; }
                Confirm(item, verified); persist();
            }
            catch (OperationCanceledException) { if (phase != Phase.Before) item.Error = Unconfirmed("Interrupted."); persist(); throw; }
            catch (DestinationBlockedException) { throw; }
            catch (ErpException e)
            {
                // Was the file possibly stored? Only when the request was in flight and failed without a clear refusal, or while confirming.
                bool unknown = phase == Phase.Verifying || phase == Phase.Sending && e.Kind is ErpErrorKind.Network or ErpErrorKind.Timeout or ErpErrorKind.InvalidResponse or ErpErrorKind.Server
                    || phase == Phase.Before && wasUnknown;
                if (e.Kind == ErpErrorKind.FileTooLarge && phase != Phase.Verifying) Reject(item, e.Message);
                else if (unknown) { item.State = UploadState.Unknown; item.Retryable = true; item.Error = Unconfirmed(e.Message); }
                else { item.State = UploadState.Failed; item.Retryable = true; item.Error = e.Message; }
                journal.LastError = item.Error; persist();
                // These concern the whole job, not one file: stop instead of repeating the same failure for every file.
                if (e.Kind is ErpErrorKind.Authentication or ErpErrorKind.Permission or ErpErrorKind.NotFound or ErpErrorKind.Certificate or ErpErrorKind.Redirect) throw;
            }
        }
    }

    private enum Phase { Before, Sending, Verifying }
    private static string Unconfirmed(string reason) => reason + " The result is not known. Retry will check ERPNext before sending again.";
    private static void Confirm(DeliveryItem item, ErpAttachment attachment)
    {
        item.State = UploadState.Succeeded; item.Retryable = false; item.Error = null;
        item.ServerFile = attachment.Name; item.ServerFileName = attachment.FileName; item.ServerUrl = attachment.FileUrl;
    }
    private static void Reject(DeliveryItem item, string reason) { item.State = UploadState.Failed; item.Retryable = false; item.Error = reason; }
    public static string Megabytes(long bytes) => $"{bytes / 1024d / 1024d:0.0} MB";
}
