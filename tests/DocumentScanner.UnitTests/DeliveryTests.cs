using System.Net;
using System.Net.Http;
using System.Text.Json;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class DeliveryTests
{
    private sealed class Bridge : HttpMessageHandler
    {
        public int Uploads;public bool Committed,TimeoutAfterCommit,FailReceipt;public string? UploadedName;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct)
        {
            string action=req.RequestUri!.AbsolutePath.Split('.').Last();string body=await req.Content!.ReadAsStringAsync(ct);
            Assert.Equal("https",req.RequestUri.Scheme);Assert.Equal("token",req.Headers.Authorization?.Scheme);
            object message=action switch{"ping"=>new ErpConnection("test-user","1.1.0",["Sales Order"]),"document"=>new ErpDocument("Sales Order","SO-1","Party","Description","Company","2026-11-14","Submitted","100","r1"),_=>new ErpReceipt(Committed?"FILE-1":null)};
            if(action=="receipt"&&FailReceipt)return new(HttpStatusCode.Forbidden);
            if(action=="upload"){Uploads++;Committed=true;UploadedName=body;if(TimeoutAfterCommit)throw new TaskCanceledException("Response lost after server commit");message=new ErpReceipt("FILE-1");}
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{message}))};
        }
    }
    private static (UploadJournal Journal,string Root,ErpSite Site) Job()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string file=Path.Combine(root,"scan.png");File.WriteAllBytes(file,[137,80,78,71,13,10,26,10,1]);var site=new ErpSite{Url="https://erp.example.com"};
        return(new(){Target=new(Guid.NewGuid(),site.Id,"test-user","Sales Order","SO-1",true,"r1"),Items=[new(){File=file,Hash=LocalDelivery.Hash(file)}]},root,site);
    }
    [Fact]public async Task TimeoutAfterCommitIsReconciledWithoutSecondUpload()
    {var(j,root,site)=Job();try{var handler=new Bridge{TimeoutAfterCommit=true};using var c=new ErpClient(site,new("key","secret"),handler);int writes=0;await Assert.ThrowsAsync<TaskCanceledException>(()=>UploadRunner.Run(j,c,()=>writes++,default));Assert.Equal(UploadState.Unknown,j.Items[0].State);await UploadRunner.Run(j,c,()=>writes++,default);Assert.Equal(1,handler.Uploads);Assert.Equal(UploadState.Succeeded,j.Items[0].State);Assert.True(writes>=3);}finally{Directory.Delete(root,true);}}
    [Fact]public async Task FailedReconciliationNeverBlindlyUploads()
    {var(j,root,site)=Job();try{var handler=new Bridge{FailReceipt=true};using var c=new ErpClient(site,new("key","secret"),handler);await UploadRunner.Run(j,c,()=>{},default);Assert.Equal(0,handler.Uploads);Assert.Equal(UploadState.Unknown,j.Items[0].State);}finally{Directory.Delete(root,true);}}
    [Fact]public async Task SuccessfulArtifactsAreSkippedAndChangedBytesAreRejected()
    {var(j,root,site)=Job();try{var handler=new Bridge();using var c=new ErpClient(site,new("key","secret"),handler);await UploadRunner.Run(j,c,()=>{},default);await UploadRunner.Run(j,c,()=>{},default);Assert.Equal(1,handler.Uploads);File.AppendAllText(j.Items[0].File,"changed");await Assert.ThrowsAsync<InvalidDataException>(()=>c.Upload(j.Target,j.Items[0],default));}finally{Directory.Delete(root,true);}}
    [Fact]public void LocalSaveRetriesKeepExistingDifferentFilesAndDoNotDuplicateCompletedCopies()
    {var(j,root,_)=Job();try{string output=Path.Combine(root,"output");Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"scan.png"),"user file");j=j with{NeedsLocal=true,LocalFolder=output};Assert.True(LocalDelivery.Complete(j,()=>{}));Assert.Single(j.LocalFiles);Assert.Equal("scan (1).png",j.Items[0].Name);Assert.True(LocalDelivery.Complete(j,()=>{}));Assert.Equal(2,Directory.GetFiles(output).Length);Assert.Equal("user file",File.ReadAllText(Path.Combine(output,"scan.png")));}finally{Directory.Delete(root,true);}}
    [Fact]public void LocalFailureKeepsArtifactsForRetry()
    {var(j,root,_)=Job();try{string output=Path.Combine(root,"blocked");File.WriteAllText(output,"block");j=j with{NeedsLocal=true,LocalFolder=output};Assert.False(LocalDelivery.Complete(j,()=>{}));Assert.False(j.LocalComplete);Assert.True(File.Exists(j.Items[0].File));File.Delete(output);Assert.True(LocalDelivery.Complete(j,()=>{}));Assert.True(j.LocalComplete);}finally{Directory.Delete(root,true);}}
    [Theory][InlineData("http://erp.example.com")][InlineData("https://user:pass@erp.example.com")][InlineData("https://erp.example.com/?token=secret")]
    public void InsecureOrCredentialBearingSitesAreRejected(string url)=>Assert.Throws<ArgumentException>(()=>new ErpSite{Url=url}.Validate());
}
