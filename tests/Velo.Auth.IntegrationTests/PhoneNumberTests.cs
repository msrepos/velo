using Velo.Modules.Identity.Application;
using Xunit;

namespace Velo.Auth.IntegrationTests;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData(" +20 (100) 123-4567 ", "+201001234567")]
    [InlineData("00201001234567", "+201001234567")]
    [InlineData("+966501234567", "+966501234567")]
    public void International_numbers_are_canonicalized(string value, string expected) =>
        Assert.Equal(expected, PhoneNumber.Normalize(value));

    [Theory]
    [InlineData("01001234567")]
    [InlineData("+00012345678")]
    [InlineData("+20abc123456")]
    [InlineData("+2012345678901234")]
    [InlineData("")]
    public void Ambiguous_or_invalid_numbers_are_rejected(string value) =>
        Assert.Throws<AuthException>(() => PhoneNumber.Normalize(value));
}
