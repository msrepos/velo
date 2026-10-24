namespace Velo.Modules.Identity.Domain;

public sealed class AuthThrottle
{
    public required string Key { get; set; }
    public DateTimeOffset WindowStartedAt { get; set; }
    public int Count { get; set; }
}
