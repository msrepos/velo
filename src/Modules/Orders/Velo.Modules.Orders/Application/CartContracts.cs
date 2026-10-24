namespace Velo.Modules.Orders.Application;

public sealed record SetCartItemRequest(int Quantity);
public sealed record CartItemResponse(Guid VariantId, int Quantity, string? ProductName, string? VariantName,
    string? Sku, decimal? UnitPrice, decimal? LineTotal, bool IsAvailable);
public sealed record CartResponse(string Version, string Currency, decimal? Subtotal,
    bool HasUnavailableItems, CartItemResponse[] Items);

public sealed class CartException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
