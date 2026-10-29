namespace Velo.Modules.Identity.Application;

public sealed record AdminUserResponse(Guid Id, string? Name, string? Email, string? PhoneNumber,
    bool IsActive, bool PhoneNumberConfirmed, string Version, string Role);
public sealed record AdminUserRequest(string? Name, string? Email, bool IsActive, string Version, string? Role = null);
public sealed record CreateAdminUserRequest(string? Name, string? Email, string? PhoneNumber, string Role = "Customer", bool IsActive = true);
public sealed record AdminUserPage(AdminUserResponse[] Items, int Page, int PageSize, int TotalCount);
public interface IAdminUsers
{
    Task<AdminUserPage> ListAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<AdminUserResponse> CreateAsync(Guid actor, CreateAdminUserRequest request, CancellationToken ct);
    Task<AdminUserResponse> UpdateAsync(Guid actor, Guid id, AdminUserRequest request, CancellationToken ct);
}
