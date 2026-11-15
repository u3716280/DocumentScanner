using System.IO;
using System.Net;
using System.Net.Http;
using DocumentScanner.Core;
using DocumentScanner.Infrastructure;
using Xunit;
namespace DocumentScanner.UnitTests;
public sealed class ErpConnectionErrorsTests
{
    private sealed class Reply(HttpStatusCode status,string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(status){Content=new StringContent(content)});
    }
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized,"Authentication failed")]
    [InlineData(HttpStatusCode.Forbidden,"Access denied")]
    [InlineData(HttpStatusCode.NotFound,"Endpoint not found")]
    [InlineData(HttpStatusCode.InternalServerError,"request could not be completed")]
    public async Task ConnectionErrorsProvideActionableAdviceWithoutClaimingBridgeMissing(HttpStatusCode status,string expected)
    {
        using var client=new ErpClient(new ErpSite{Url="https://erp.example.com"},new("key","secret"),new Reply(status,""));
        var error=await Assert.ThrowsAsync<ErpRequestException>(()=>client.Connect(default));Assert.Equal(status,error.StatusCode);Assert.Contains(expected,error.Message);Assert.Contains("No scan data was discarded",error.Message);Assert.DoesNotContain("Bridge is not installed",error.Message);
    }
    [Fact]public async Task InvalidPingPayloadIsNotReportedAsConnected()
    {
        using var client=new ErpClient(new ErpSite{Url="https://erp.example.com"},new("key","secret"),new Reply(HttpStatusCode.OK,"{\"message\":{}}"));await Assert.ThrowsAsync<InvalidDataException>(()=>client.Connect(default));
    }
}
