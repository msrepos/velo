using Microsoft.Extensions.Hosting;
using Velo.Modules.Identity.Application;

namespace Velo.Modules.Identity.Infrastructure;

// A deliberately non-delivering adapter. Codes are returned only when explicitly enabled
// in Development; no code or phone number is written to application logs.
public sealed class DevelopmentOtpSender(IHostEnvironment environment) : IOtpSender
{
    public Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException("The development OTP adapter cannot run outside Development.");
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
