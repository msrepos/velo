namespace Velo.Modules.Identity.Contracts;
public interface IVerifiedUsers
{
    Task<bool> ExistsAsync(Guid userId, CancellationToken ct);
}
