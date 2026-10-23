using SportsBooking.Api.Security;

namespace SportsBooking.UnitTests.Security;

public sealed class OriginCheckOptionsTests
{
    private static readonly OriginCheckOptionsValidator Validator = new();

    [Fact]
    public void At_least_one_origin_is_required()
    {
        Validator.Validate(null, new OriginCheckOptions()).Failed.ShouldBeTrue();
    }

    [Theory]
    [InlineData("https://reservas.pt")]
    [InlineData("http://localhost:3000")]
    public void Bare_http_and_https_origins_are_valid(string origin)
    {
        Validator.Validate(null, new OriginCheckOptions { AllowedOrigins = [origin] }).Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("reservas.pt")]
    [InlineData("https://reservas.pt/")]
    [InlineData("https://reservas.pt/app")]
    [InlineData("ftp://reservas.pt")]
    [InlineData("*")]
    public void Anything_but_a_bare_origin_is_invalid(string origin)
    {
        Validator.Validate(null, new OriginCheckOptions { AllowedOrigins = ["https://reservas.pt", origin] }).Failed.ShouldBeTrue();
    }
}
