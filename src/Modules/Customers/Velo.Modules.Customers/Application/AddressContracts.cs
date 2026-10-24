using System.Text.Json.Serialization;

namespace Velo.Modules.Customers.Application;

public sealed record AddressRequest(string Label, string RecipientName, string PhoneNumber,
    string City, string Area, string Street, string Building,
    [property: JsonRequired] decimal Latitude, [property: JsonRequired] decimal Longitude,
    string? Floor = null, string? Apartment = null, string? Instructions = null,
    bool IsDefault = false, byte[]? Version = null);

public sealed record AddressResponse(Guid Id, string Label, string RecipientName, string PhoneNumber,
    string City, string Area, string Street, string Building, decimal Latitude, decimal Longitude,
    string? Floor, string? Apartment, string? Instructions, bool IsDefault, byte[] Version);

public sealed class CustomerException(int status, string code) : Exception(code)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
