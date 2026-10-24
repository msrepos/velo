using System.Text.RegularExpressions;

namespace Velo.Modules.Catalog.Application;

public static partial class CatalogValidation
{
    public static string Name(string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsControl))
            throw new CatalogException(400, "invalid_name");
        return value;
    }

    public static string Sku(string? value)
    {
        value = value?.Trim().ToUpperInvariant();
        if (value is null || !SkuPattern().IsMatch(value))
            throw new CatalogException(400, "invalid_sku");
        return value;
    }

    public static string? Barcode(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value is not null && (value.Length > 64 || value.Any(char.IsWhiteSpace) || value.Any(char.IsControl)))
            throw new CatalogException(400, "invalid_barcode");
        return value;
    }

    public static decimal Price(decimal value)
    {
        if (value <= 0 || value > 9999999999999999.99m || decimal.Round(value, 2) != value)
            throw new CatalogException(400, "invalid_price");
        return value;
    }

    public static string? ImageUrl(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value is not null && (value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !string.IsNullOrEmpty(uri.UserInfo)))
            throw new CatalogException(400, "invalid_image_url");
        return value;
    }

    public static string? Description(string? value)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value?.Length > 2000) throw new CatalogException(400, "invalid_description");
        return value;
    }

    public static void Pagination(int page, int pageSize)
    {
        if (page < 1 || page > 1_000_000 || pageSize is < 1 or > 100)
            throw new CatalogException(400, "invalid_pagination");
    }

    public static byte[] Version(byte[]? value)
    {
        if (value is null || value.Length != 8) throw new CatalogException(400, "invalid_version");
        return value;
    }

    [GeneratedRegex(@"^[A-Z0-9][A-Z0-9._-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SkuPattern();
}
