using System.Net;
using Microsoft.Extensions.Options;
using Velo.Modules.Identity.Application;
using Velo.Modules.Identity.Infrastructure;
using Xunit;
namespace Velo.Auth.IntegrationTests;
public sealed class SmsGatewayTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(302)]
    [InlineData(503)]
    public async Task Gateway_requires_successful_https_delivery_without_following_redirects(int status)
    {
        var handler = new GatewayHandler((HttpStatusCode)status); using var client = new HttpClient(handler);
        var options = Options.Create(new AuthOptions { SmsProvider = "HttpGateway", GatewayUrl = "https://sms.example.invalid/otp", GatewayApiKey = "test-gateway-secret" });
        var sender = new HttpGatewayOtpSender(client, options);
        if(status == 200) await sender.SendAsync("+201012345678", "123456", TestContext.Current.CancellationToken);
        else await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync("+201012345678", "123456", TestContext.Current.CancellationToken));
        Assert.Equal("Bearer test-gateway-secret", handler.Authorization);
        Assert.Contains("123456", handler.Body);
    }
    [Theory]
    [InlineData("http://sms.example.invalid/otp")]
    [InlineData("https://user:pass@sms.example.invalid/otp")]
    [InlineData("https://sms.example.invalid/otp?apiKey=secret")]
    public void Gateway_rejects_insecure_or_credential_bearing_urls(string url) =>
        Assert.False(AuthOptions.HasValidGateway(new AuthOptions { SmsProvider = "HttpGateway", GatewayUrl = url, GatewayApiKey = "test-gateway-secret" }));
    private sealed class GatewayHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Authorization = request.Headers.Authorization?.ToString(); Body = await request.Content!.ReadAsStringAsync(ct); return new HttpResponseMessage(status); }
    }
}
