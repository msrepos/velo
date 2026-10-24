using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Velo.Modules.Inventory.Application;
using Velo.Modules.Inventory.Domain;
namespace Velo.Modules.Inventory.Infrastructure;

public sealed partial class InventoryService
{
    public async Task<InventoryPage<WarehouseResponse>> WarehousesAsync(int page, int size, CancellationToken ct)
    {
        Page(page, size);
        var query = db.Warehouses.AsNoTracking();
        var count = await query.CountAsync(ct);
        var items = await query.OrderBy(x => x.Code).Skip((page - 1) * size).Take(size).ToArrayAsync(ct);
        return new(items.Select(Map).ToArray(), page, size, count);
    }
    public async Task<WarehouseResponse> WarehouseAsync(Guid id, CancellationToken ct) =>
        Map(await db.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new InventoryException(404, "warehouse_not_found"));

    public async Task<WarehouseResponse> SaveWarehouseAsync(Guid? id, WarehouseRequest request, CancellationToken ct)
    {
        var code = Text(request.Code, 32, "invalid_warehouse_code").ToUpperInvariant();
        if (!Regex.IsMatch(code, "^[A-Z0-9][A-Z0-9_-]{0,31}$", RegexOptions.CultureInvariant))
            throw new InventoryException(400, "invalid_warehouse_code");
        var name = Text(request.Name, 160, "invalid_name");
        var type = request.Type?.Trim();
        if (type is not ("Fixed" or "Mobile")) throw new InventoryException(400, "invalid_warehouse_type");
        var address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        if (address?.Length > 500) throw new InventoryException(400, "invalid_address");
        if ((request.Latitude is null) != (request.Longitude is null) ||
            request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180 ||
            (request.Latitude is { } lat && decimal.Round(lat, 6) != lat) ||
            (request.Longitude is { } lon && decimal.Round(lon, 6) != lon))
            throw new InventoryException(400, "invalid_coordinates");
        if (id is not null && request.Version?.Length != 8)
            throw new InventoryException(400, "invalid_version");
        var warehouseId = id ?? Guid.NewGuid();
        return await transactions.RunAsync([WarehouseKey(warehouseId)], async () =>
        {
            var now = time.GetUtcNow();
            Warehouse item;
            if (id is null)
            {
                item = new Warehouse { Id = warehouseId, Code = code, Name = name, Type = type, CreatedAt = now };
                db.Warehouses.Add(item);
            }
            else
            {
                item = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == id, ct)
                    ?? throw new InventoryException(404, "warehouse_not_found");
                db.Entry(item).Property(x => x.RowVersion).OriginalValue = request.Version!;
            }
            item.Code = code; item.Name = name; item.Type = type;
            item.Address = address; item.Latitude = request.Latitude; item.Longitude = request.Longitude;
            item.IsActive = request.IsActive; item.UpdatedAt = now;
            if (id is not null) db.Entry(item).Property(x => x.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(ct); // Rowversion is needed in the response.
            return Map(item);
        }, ct);
    }
    private static WarehouseResponse Map(Warehouse x) => new(x.Id, x.Code, x.Name, x.Type,
        x.Address, x.Latitude, x.Longitude, x.IsActive, x.RowVersion);
}
