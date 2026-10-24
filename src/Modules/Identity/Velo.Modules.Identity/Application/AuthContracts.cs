using System.Text.Json.Serialization;

namespace Velo.Modules.Identity.Application;

public sealed record RequestOtpRequest(string PhoneNumber);
public sealed record RegisterRequest(string PhoneNumber, string Email, string Name);
public sealed record VerifyOtpRequest(Guid ChallengeId, string Code);
public sealed record RefreshRequest(string RefreshToken);
public sealed record OtpResponse(Guid ChallengeId, int ExpiresIn, int ResendAfter,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DevelopmentCode);
public sealed record TokenResponse(string TokenType, string AccessToken, long ExpiresIn,
    string RefreshToken, Guid SessionId);
public sealed record CurrentUserResponse(Guid Id, string PhoneNumber, string[] Roles,
    string? Name = null, string? Email = null);
public sealed record SessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, bool IsCurrent);

public sealed class AuthException(int statusCode, string code, int? retryAfter = null) : Exception(code)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public int? RetryAfter { get; } = retryAfter;
}

// The integration boundary for an actual SMS provider. Implementations must not log codes.
public interface IOtpSender
{
    Task SendAsync(string phoneNumber, string code, CancellationToken cancellationToken);
}
