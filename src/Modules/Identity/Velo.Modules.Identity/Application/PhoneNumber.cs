using System.Text.RegularExpressions;

namespace Velo.Modules.Identity.Application;

public static partial class PhoneNumber
{
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 40)
            throw new AuthException(400, "invalid_phone_number");

        var value = input.Trim().Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
        if (value.StartsWith("00", StringComparison.Ordinal))
            value = "+" + value[2..];
        if (!InternationalPhone().IsMatch(value))
            throw new AuthException(400, "invalid_phone_number");
        return value;
    }

    [GeneratedRegex(@"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant)]
    private static partial Regex InternationalPhone();
}
