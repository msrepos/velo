using Microsoft.EntityFrameworkCore;
using Velo.Modules.Inventory.Contracts;
using Velo.Modules.Inventory.Infrastructure.Persistence;

namespace Velo.Modules.Inventory.Infrastructure;

public sealed class MobileWarehouses(InventoryDbContext db) : IMobileWarehouses
{
    public Task<MobileWarehouse?> FindAsync(Guid id, CancellationToken ct) =>
        db.Warehouses.AsNoTracking().Where(x => x.Id == id && x.Type == "Mobile")
            .Select(x => new MobileWarehouse(x.Id, x.Code, x.Name, x.Address, x.Latitude, x.Longitude, x.IsActive, x.RowVersion))
            .SingleOrDefaultAsync(ct);

    public async Task<MobileWarehousePage> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        var query = db.Warehouses.AsNoTracking().Where(x => x.Type == "Mobile");
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new MobileWarehouse(x.Id, x.Code, x.Name, x.Address, x.Latitude, x.Longitude, x.IsActive, x.RowVersion))
            .ToArrayAsync(ct);
        return new(rows, page, pageSize, count);
    }
}
