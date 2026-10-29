namespace Velo.Modules.Inventory.Contracts;

public sealed record MobileWarehouse(Guid Id, string Code, string Name, string? Address,
    decimal? Latitude, decimal? Longitude, bool IsActive, byte[] Version);
public sealed record MobileWarehousePage(MobileWarehouse[] Items, int Page, int PageSize, int TotalCount);
public interface IMobileWarehouses
{
    Task<MobileWarehouse?> FindAsync(Guid id, CancellationToken ct);
    Task<MobileWarehousePage> ListAsync(int page, int pageSize, CancellationToken ct);
}
