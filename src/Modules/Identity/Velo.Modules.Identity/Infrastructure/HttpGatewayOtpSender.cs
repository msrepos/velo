using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Velo.Modules.Identity.Application;
namespace Velo.Modules.Identity.Infrastructure;
public sealed class HttpGatewayOtpSender(HttpClient client, IOptions<AuthOptions> options) : IOtpSender
{
    public async Task SendAsync(string phoneNumber, string code, CancellationToken ct)
    {
        var settings = options.Value;
        if (!AuthOptions.HasValidGateway(settings)) throw new InvalidOperationException("Configure a valid HTTPS SMS gateway.");
        using var request = new HttpRequestMessage(HttpMethod.Post, settings.GatewayUrl)
        { Content = JsonContent.Create(new { phoneNumber, code }) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.GatewayApiKey);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }
}
