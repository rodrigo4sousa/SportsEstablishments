using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SportsBooking.Infrastructure;
using SportsBooking.Infrastructure.Identity;

namespace SportsBooking.UnitTests.Accounts;

/// <summary>US04: admin seed settings as registered by <c>AddInfrastructure</c>, validated when the API starts.</summary>
public sealed class AdminSeedOptionsTests
{
    private static AdminSeedOptions Resolve(string? email, string? password)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=unused",
                ["AdminSeed:Email"] = email,
                ["AdminSeed:Password"] = password,
            })
            .Build();

        using var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        return services.GetRequiredService<IOptions<AdminSeedOptions>>().Value;
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void Both_settings_absent_is_valid(string? email, string? password)
    {
        var options = Resolve(email, password);

        AdminSeedSettings.Evaluate(options.Email, options.Password).ShouldBe(AdminSeedConfiguration.NotConfigured);
    }

    [Fact]
    public void Valid_settings_are_accepted()
    {
        var options = Resolve("admin@reservas.pt", "palavra-passe");

        options.Email.ShouldBe("admin@reservas.pt");
        options.Password.ShouldBe("palavra-passe");
    }

    [Theory]
    [InlineData("admin@reservas.pt", null, "must be set together")]
    [InlineData(null, "palavra-passe", "must be set together")]
    [InlineData("admin@", "palavra-passe", "not a valid email address")]
    [InlineData("admin@reservas.pt", "1234567", "at least 8 characters")]
    public void Partial_or_invalid_settings_fail_validation_without_revealing_the_values(
        string? email, string? password, string expectedMessage)
    {
        var exception = Should.Throw<OptionsValidationException>(() => Resolve(email, password));

        exception.Message.ShouldContain(expectedMessage);
        foreach (var secret in new[] { email, password }.Where(secret => !string.IsNullOrEmpty(secret)))
        {
            exception.Message.ShouldNotContain(secret!);
        }
    }
}
