using System.ComponentModel.DataAnnotations;

namespace Velo.Modules.Identity.Application;

public sealed class AuthOptions
{
    public const string Section = "Authentication";
    [Required] public string OtpHashKey { get; set; } = "";
    [Range(1, 10)] public int OtpLifetimeMinutes { get; set; } = 5;
    [Range(1, 10)] public int OtpMaxAttempts { get; set; } = 5;
    [Range(30, 600)] public int OtpResendSeconds { get; set; } = 60;
    [Range(1, 30)] public int OtpRequestsPerPhonePerHour { get; set; } = 5;
    [Range(1, 1000)] public int OtpRequestsPerClientPerHour { get; set; } = 30;
    [Range(1, 1000)] public int OtpVerificationsPerClientPerHour { get; set; } = 120;
    [Range(1, 30)] public int AccessTokenMinutes { get; set; } = 15;
    [Range(1, 90)] public int SessionDays { get; set; } = 30;
    public bool ExposeDevelopmentCodes { get; set; }
    public string SmsProvider { get; set; } = "Disabled";
    public string GatewayUrl { get; set; } = "";
    public string GatewayApiKey { get; set; } = "";
    public static bool HasValidGateway(AuthOptions x) => x.SmsProvider != "HttpGateway" ||
        (Uri.TryCreate(x.GatewayUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
         string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
         x.GatewayApiKey is { Length: >= 16 and <= 2048 } && !x.GatewayApiKey.Any(char.IsControl));

    public static bool HasValidHashKey(AuthOptions options)
    {
        try { return Convert.FromBase64String(options.OtpHashKey).Length >= 32; }
        catch (FormatException) { return false; }
    }
}
