using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Accounts;

public sealed class AccountRegistrationServiceTests
{
    private readonly FakeAccountStore _accounts = new();
    private readonly FakeSessions _sessions = new();

    private AccountRegistrationService Service => new(new RegisterAccountRequestValidator(), _accounts, _sessions);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_request_creates_a_normalized_player_account_and_starts_its_session()
    {
        var result = await Service.RegisterPlayerAsync(new RegisterAccountRequest(" Ana ", " ana@exemplo.pt ", "palavra-passe", " 1.0 ", "912 345 678"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _accounts.Created.ShouldHaveSingleItem().ShouldBe((new NewAccount("Ana", "ana@exemplo.pt", "palavra-passe", "912345678", "1.0"), Profiles.Player));
        result.Value.Account.Profile.ShouldBe(Profiles.Player);
        _sessions.Started.ShouldHaveSingleItem().ShouldBe((result.Value.Account.Id, Profiles.Player));
        result.Value.Session.ShouldBeSameAs(FakeSessions.Tokens);
    }

    [Fact]
    public async Task Owner_registration_creates_the_same_normalized_account_with_the_owner_profile()
    {
        var result = await Service.RegisterOwnerAsync(new RegisterAccountRequest(" Rui ", " rui@exemplo.pt ", "palavra-passe", "1.0", "912 345 678"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _accounts.Created.ShouldHaveSingleItem().ShouldBe((new NewAccount("Rui", "rui@exemplo.pt", "palavra-passe", "912345678", "1.0"), Profiles.Owner));
        result.Value.Account.Profile.ShouldBe(Profiles.Owner);
        _sessions.Started.ShouldHaveSingleItem().ShouldBe((result.Value.Account.Id, Profiles.Owner));
    }

    [Fact]
    public async Task Owner_registration_applies_the_same_validation_and_the_same_taken_email_error()
    {
        var invalid = await Service.RegisterOwnerAsync(new RegisterAccountRequest("", "rui", "curta", "1.0"), Ct);
        invalid.Error.ShouldNotBeNull().FieldErrors.ShouldNotBeNull().Keys.ShouldBe(["name", "email", "password"], ignoreOrder: true);

        _accounts.EmailTaken = true;
        var taken = await Service.RegisterOwnerAsync(new RegisterAccountRequest("Rui", "rui@exemplo.pt", "palavra-passe", "1.0"), Ct);

        taken.Error.ShouldBe(AccountErrors.EmailTaken);
        _accounts.Created.ShouldBeEmpty();
        _sessions.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_request_returns_field_errors_and_touches_nothing()
    {
        var result = await Service.RegisterPlayerAsync(new RegisterAccountRequest("", "ana", "curta", "1.0"), Ct);

        result.IsSuccess.ShouldBeFalse();
        result.Error.ShouldNotBeNull().Code.ShouldBe(ErrorCodes.ValidationFailed);
        result.Error.Kind.ShouldBe(ErrorKind.Validation);
        result.Error.FieldErrors.ShouldNotBeNull().Keys.ShouldBe(["name", "email", "password"], ignoreOrder: true);
        _accounts.Created.ShouldBeEmpty();
        _sessions.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task Taken_email_is_a_conflict_and_starts_no_session()
    {
        _accounts.EmailTaken = true;

        var result = await Service.RegisterPlayerAsync(new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", "1.0"), Ct);

        result.Error.ShouldBe(AccountErrors.EmailTaken);
        result.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("email_taken");
        _sessions.Started.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Without_the_accepted_version_no_account_is_created(string? version)
    {
        var player = await Service.RegisterPlayerAsync(new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", version!), Ct);
        var owner = await Service.RegisterOwnerAsync(new RegisterAccountRequest("Rui", "rui@exemplo.pt", "palavra-passe", version!), Ct);

        player.Error.ShouldBe(AccountErrors.TermsNotAccepted);
        owner.Error.ShouldBe(AccountErrors.TermsNotAccepted);
        player.Error!.Kind.ShouldBe(ErrorKind.Validation);
        player.Error.Code.ShouldBe("terms_not_accepted");
        _accounts.Created.ShouldBeEmpty();
        _sessions.Started.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_longer_than_the_column_is_not_an_acceptance()
    {
        var version = new string('1', TermsAcceptancePolicy.MaximumVersionLength + 1);

        var result = await Service.RegisterPlayerAsync(new RegisterAccountRequest("Ana", "ana@exemplo.pt", "palavra-passe", version), Ct);

        result.Error.ShouldBe(AccountErrors.TermsNotAccepted);
        _accounts.Created.ShouldBeEmpty();
    }

    [Fact]
    public async Task Invalid_fields_are_reported_before_the_missing_acceptance()
    {
        var result = await Service.RegisterPlayerAsync(new RegisterAccountRequest("", "ana", "curta", ""), Ct);

        result.Error.ShouldNotBeNull().Code.ShouldBe(ErrorCodes.ValidationFailed);
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        public Task<bool> IsActiveAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> IsDeletedAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> ChangePreferredLanguageAsync(Guid userId, string code, CancellationToken cancellationToken) => Task.FromResult(true);

        public bool EmailTaken { get; set; }

        public List<(NewAccount Account, string Profile)> Created { get; } = [];

        public Task<AccountResponse?> CreateAsync(NewAccount account, string profile, CancellationToken cancellationToken)
        {
            if (EmailTaken)
            {
                return Task.FromResult<AccountResponse?>(null);
            }

            Created.Add((account, profile));
            return Task.FromResult<AccountResponse?>(new AccountResponse(Guid.CreateVersion7(), account.Name, account.Email, profile));
        }

        public Task<AccountResponse?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken) =>
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
