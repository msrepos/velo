using System.Net;
using System.Net.Http.Json;
using Velo.Modules.Catalog.Application;
using Xunit;

namespace Velo.Catalog.IntegrationTests;

public sealed class CatalogTests(CatalogFixture fixture) : IClassFixture<CatalogFixture>
{
    private const string Admin = "/api/v1/admin/catalog";
    private const string Public = "/api/v1/catalog";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static string Unique(string prefix) => prefix + "-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Only_a_management_permission_can_write_or_list_admin_data()
    {
        using var anonymous = fixture.Client();
        using var customer = fixture.Client("customer");
        using var staff = fixture.Client("staff");
        var body = new CategoryRequest(Unique("Protected"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(Admin + "/categories", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.PostAsJsonAsync(Admin + "/categories", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync(Admin + "/products", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await staff.PostAsJsonAsync(Admin + "/categories", body, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(Public + "/categories", Ct)).StatusCode);
    }

    [Fact]
    public async Task Product_is_public_only_after_an_active_variant_and_returns_price_without_stock()
    {
        using var staff = fixture.Client("staff");
        using var client = fixture.Client();
        var category = await Category(staff);
        var product = await Product(staff, category.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Public + "/products/" + product.Id, Ct)).StatusCode);
        var variant = await Variant(staff, product.Id, "milk-" + Guid.NewGuid().ToString("N"), 42.50m);
        var visible = (await client.GetFromJsonAsync<ProductResponse>(Public + "/products/" + product.Id, Ct))!;
        Assert.Equal(product.Id, visible.Id);
        var item = Assert.Single(visible.Variants);
        Assert.Equal(variant.Id, item.Id);
        Assert.Equal(42.50m, item.Price);
        Assert.Equal("EGP", item.Currency);
        Assert.StartsWith("MILK-", item.Sku);
    }

    [Fact]
    public async Task Inactive_categories_products_and_variants_are_hidden_but_remain_available_to_admin()
    {
        using var staff = fixture.Client("staff");
        using var client = fixture.Client();
        var category = await Category(staff);
        var product = await Product(staff, category.Id);
        var active = await Variant(staff, product.Id);
        await Variant(staff, product.Id, isActive: false);
        var visible = (await client.GetFromJsonAsync<ProductResponse>(Public + "/products/" + product.Id, Ct))!;
        Assert.Single(visible.Variants);
        var all = (await staff.GetFromJsonAsync<ProductResponse>(Admin + "/products/" + product.Id, Ct))!;
        Assert.Equal(2, all.Variants.Length);

        var deactivate = await staff.PutAsJsonAsync(Admin + "/categories/" + category.Id,
            new CategoryRequest(category.Name, false, category.Version), Ct);
        deactivate.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Public + "/categories/" + category.Id, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Public + "/products/" + product.Id, Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Admin + "/products/" + product.Id, Ct)).StatusCode);

        var category2 = await Category(staff);
        var product2 = await Product(staff, category2.Id);
        var only = await Variant(staff, product2.Id);
        var stopVariant = await staff.PutAsJsonAsync($"{Admin}/products/{product2.Id}/variants/{only.Id}",
            new VariantRequest(only.Sku, only.Name, only.Price, IsActive: false, Version: only.Version), Ct);
        stopVariant.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Public + "/products/" + product2.Id, Ct)).StatusCode);

        var product3 = await Product(staff, category2.Id);
        await Variant(staff, product3.Id);
        var stopProduct = await staff.PutAsJsonAsync(Admin + "/products/" + product3.Id,
            new ProductRequest(category2.Id, product3.Name, IsActive: false, Version: product3.Version), Ct);
        stopProduct.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Public + "/products/" + product3.Id, Ct)).StatusCode);
    }

    [Fact]
    public async Task Category_names_skus_and_non_null_barcodes_are_unique()
    {
        using var staff = fixture.Client("staff");
        var category = await Category(staff);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync(Admin + "/categories",
            new CategoryRequest(" " + category.Name.ToLowerInvariant() + " "), Ct)).StatusCode);
        var first = await Product(staff, category.Id);
        var second = await Product(staff, category.Id);
        var original = await Variant(staff, first.Id);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"{Admin}/products/{second.Id}/variants",
            new VariantRequest(original.Sku.ToLowerInvariant(), "Duplicate", 10), Ct)).StatusCode);
        var barcode = "6221234567890";
        await Variant(staff, first.Id, barcode: barcode);
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PostAsJsonAsync($"{Admin}/products/{second.Id}/variants",
            new VariantRequest(Unique("SKU"), "Duplicate barcode", 10, barcode), Ct)).StatusCode);
        await Variant(staff, second.Id);
        await Variant(staff, second.Id); // Multiple absent barcodes are valid.
    }

    [Fact]
    public async Task Missing_versions_are_rejected_and_stale_versions_cannot_overwrite_changes()
    {
        using var staff = fixture.Client("staff");
        var category = await Category(staff);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PutAsJsonAsync(Admin + "/categories/" + category.Id,
            new CategoryRequest("Missing version"), Ct)).StatusCode);
        var updated = await staff.PutAsJsonAsync(Admin + "/categories/" + category.Id,
            new CategoryRequest(Unique("Renamed"), Version: category.Version), Ct);
        updated.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await staff.PutAsJsonAsync(Admin + "/categories/" + category.Id,
            new CategoryRequest(Unique("Stale"), Version: category.Version), Ct)).StatusCode);
    }

    [Fact]
    public async Task Concurrent_price_updates_have_exactly_one_winner()
    {
        using var first = fixture.Client("staff");
        using var second = fixture.Client("staff");
        var category = await Category(first);
        var product = await Product(first, category.Id);
        var variant = await Variant(first, product.Id);
        var path = $"{Admin}/products/{product.Id}/variants/{variant.Id}";
        var results = await Task.WhenAll(
            first.PutAsJsonAsync(path, new VariantRequest(variant.Sku, variant.Name, 51, Version: variant.Version), Ct),
            second.PutAsJsonAsync(path, new VariantRequest(variant.Sku, variant.Name, 52, Version: variant.Version), Ct));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
        var stored = (await first.GetFromJsonAsync<ProductResponse>(Admin + "/products/" + product.Id, Ct))!;
        Assert.Contains(stored.Variants[0].Price, new[] { 51m, 52m });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1.234)]
    public async Task Invalid_prices_are_rejected(decimal price)
    {
        using var staff = fixture.Client("staff");
        var category = await Category(staff);
        var product = await Product(staff, category.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"{Admin}/products/{product.Id}/variants",
            new VariantRequest(Unique("SKU"), "Test", price), Ct)).StatusCode);
    }

    [Fact]
    public async Task Invalid_references_images_and_variant_ownership_are_rejected()
    {
        using var staff = fixture.Client("staff");
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(Admin + "/categories",
            new CategoryRequest(" "), Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(Admin + "/products",
            new ProductRequest(Guid.NewGuid(), "Missing category"), Ct)).StatusCode);
        var category = await Category(staff);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync(Admin + "/products",
            new ProductRequest(category.Id, "Unsafe image", ImageUrl: "javascript:alert(1)"), Ct)).StatusCode);
        var product = await Product(staff, category.Id);
        var other = await Product(staff, category.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await staff.PostAsJsonAsync($"{Admin}/products/{product.Id}/variants",
            new VariantRequest("bad sku!", "Bad", 1), Ct)).StatusCode);
        var variant = await Variant(staff, product.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await staff.PutAsJsonAsync($"{Admin}/products/{other.Id}/variants/{variant.Id}",
            new VariantRequest(variant.Sku, variant.Name, 20, Version: variant.Version), Ct)).StatusCode);
    }

    [Fact]
    public async Task Public_search_category_filter_and_pagination_are_bounded_and_stable()
    {
        using var staff = fixture.Client("staff");
        using var client = fixture.Client();
        var category = await Category(staff);
        var prefix = Unique("Search");
        for (var i = 0; i < 3; i++)
        {
            var product = await Product(staff, category.Id, prefix + "-" + i);
            await Variant(staff, product.Id);
        }
        var url = $"{Public}/products?categoryId={category.Id}&search={prefix}&pageSize=2";
        var first = (await client.GetFromJsonAsync<PageResponse<ProductResponse>>(url, Ct))!;
        var second = (await client.GetFromJsonAsync<PageResponse<ProductResponse>>(url + "&page=2", Ct))!;
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.Items.Length);
        Assert.Single(second.Items);
        Assert.DoesNotContain(second.Items[0].Id, first.Items.Select(x => x.Id));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Public + "/products?pageSize=101", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Public + "/categories?page=0", Ct)).StatusCode);
    }

    private static async Task<CategoryResponse> Category(HttpClient client)
    {
        var response = await client.PostAsJsonAsync(Admin + "/categories", new CategoryRequest(Unique("Category")), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CategoryResponse>(Ct))!;
    }

    private static async Task<ProductResponse> Product(HttpClient client, Guid category, string? name = null)
    {
        var response = await client.PostAsJsonAsync(Admin + "/products", new ProductRequest(category, name ?? Unique("Product")), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>(Ct))!;
    }

    private static async Task<VariantResponse> Variant(HttpClient client, Guid product, string? sku = null,
        decimal price = 10, string? barcode = null, bool isActive = true)
    {
        var response = await client.PostAsJsonAsync($"{Admin}/products/{product}/variants",
            new VariantRequest(sku ?? Unique("SKU"), "1 litre", price, barcode, isActive), Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<VariantResponse>(Ct))!;
    }
}
