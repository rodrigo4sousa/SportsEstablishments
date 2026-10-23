using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Auth;

/// <summary>US03: login validation and the generic failure.</summary>
public sealed class LoginServiceTests
{
    private static readonly AccountResponse Owner = new(Guid.CreateVersion7(), "Dono", "dono@exemplo.pt", Profiles.Owner);

    private readonly FakeAccountStore _accounts = new();
    private readonly FakeSessions _sessions = new();

    private LoginService Service => new(new LoginRequestValidator(), _accounts, _sessions);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_credentials_start_a_session_with_the_account_profile()
    {
        _accounts.Account = Owner;

        var result = await Service.LoginAsync(new LoginRequest(" Dono@Exemplo.pt ", " palavra-passe "), Ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Account.ShouldBe(Owner);
        result.Value.Session.ShouldBeSameAs(FakeSessions.Tokens);
        _sessions.Started.ShouldHaveSingleItem().ShouldBe((Owner.Id, Profiles.Owner));
        // The email is trimmed (case is the store's job); the password is kept exactly as typed.
        _accounts.Checked.ShouldHaveSingleItem().ShouldBe(("Dono@Exemplo.pt", " palavra-passe "));
    }

    [Fact]
    public async Task Rejected_credentials_are_the_generic_401_and_start_no_session()
    {
        var result = await Service.LoginAsync(new LoginRequest("ana@exemplo.pt", "palavra-passe"), Ct);

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
        result.Error!.Kind.ShouldBe(ErrorKind.Unauthorized);
        result.Error.Code.ShouldBe("invalid_credentials");
        _sessions.Started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public async Task Empty_fields_are_validation_errors_per_field_and_check_nothing(string? email, string? password)
    {
        var result = await Service.LoginAsync(new LoginRequest(email!, password!), Ct);

        var error = result.Error.ShouldNotBeNull();
        error.Code.ShouldBe(ErrorCodes.ValidationFailed);
        error.FieldErrors.ShouldNotBeNull()["email"].ShouldBe(["Indica o teu email."]);
        error.FieldErrors["password"].ShouldBe(["Indica a palavra-passe."]);
        _accounts.Checked.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_malformed_email_is_not_a_validation_error_but_the_generic_401()
    {
        var result = await Service.LoginAsync(new LoginRequest("not-an-email", "palavra-passe"), Ct);

        result.Error.ShouldBe(AuthErrors.InvalidCredentials);
    }

    [Fact]
    public void Validator_rejects_only_empty_fields()
    {
        var validator = new LoginRequestValidator();

        validator.Validate(new LoginRequest("x", " ")).IsValid.ShouldBeTrue();
        validator.Validate(new LoginRequest("x", "")).ToFieldErrors().Keys.ShouldBe(["password"]);
        validator.Validate(new LoginRequest("", "x")).ToFieldErrors().Keys.ShouldBe(["email"]);
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        public AccountResponse? Account { get; set; }

        public List<(string Email, string Password)> Checked { get; } = [];

        public Task<AccountResponse?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
        {
            Checked.Add((email, password));
            return Task.FromResult(Account);
        }

        public Task<AccountResponse?> CreateAsync(NewAccount account, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AccountResponse?> FindAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class FakeSessions : ISessionService
    {
        public static readonly SessionTokens Tokens = new("access", DateTimeOffset.UnixEpoch, "refresh", DateTimeOffset.UnixEpoch);

        public List<(Guid UserId, string Profile)> Started { get; } = [];

        public Task<SessionTokens> StartAsync(Guid userId, string profile, CancellationToken cancellationToken)
        {
            Started.Add((userId, profile));
            return Task.FromResult(Tokens);
        }

        public Task<Result<SessionTokens>> RefreshAsync(string? refreshToken, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task EndAsync(string? refreshToken, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
