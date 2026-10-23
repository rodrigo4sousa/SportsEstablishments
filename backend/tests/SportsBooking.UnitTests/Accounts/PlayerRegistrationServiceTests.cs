using SportsBooking.Application.Accounts;
using SportsBooking.Application.Auth;
using SportsBooking.Application.Common;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Accounts;

public sealed class PlayerRegistrationServiceTests
{
    private readonly FakeAccountStore _accounts = new();
    private readonly FakeSessions _sessions = new();

    private PlayerRegistrationService Service => new(new RegisterPlayerRequestValidator(), _accounts, _sessions);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Valid_request_creates_a_normalized_player_account_and_starts_its_session()
    {
        var result = await Service.RegisterAsync(new RegisterPlayerRequest(" Ana ", " ana@exemplo.pt ", "palavra-passe", "912 345 678"), Ct);

        result.IsSuccess.ShouldBeTrue();
        _accounts.Created.ShouldHaveSingleItem().ShouldBe(new NewAccount("Ana", "ana@exemplo.pt", "palavra-passe", "912345678"));
        _sessions.Started.ShouldHaveSingleItem().ShouldBe((result.Value.Account.Id, Profiles.Player));
        result.Value.Session.ShouldBeSameAs(FakeSessions.Tokens);
    }

    [Fact]
    public async Task Invalid_request_returns_field_errors_and_touches_nothing()
    {
        var result = await Service.RegisterAsync(new RegisterPlayerRequest("", "ana", "curta"), Ct);

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

        var result = await Service.RegisterAsync(new RegisterPlayerRequest("Ana", "ana@exemplo.pt", "palavra-passe"), Ct);

        result.Error.ShouldBe(AccountErrors.EmailTaken);
        result.Error!.Kind.ShouldBe(ErrorKind.Conflict);
        result.Error.Code.ShouldBe("email_taken");
        _sessions.Started.ShouldBeEmpty();
    }

    private sealed class FakeAccountStore : IAccountStore
    {
        public bool EmailTaken { get; set; }

        public List<NewAccount> Created { get; } = [];

        public Task<AccountResponse?> CreatePlayerAsync(NewAccount account, CancellationToken cancellationToken)
        {
            if (EmailTaken)
            {
                return Task.FromResult<AccountResponse?>(null);
            }

            Created.Add(account);
            return Task.FromResult<AccountResponse?>(new AccountResponse(Guid.CreateVersion7(), account.Name, account.Email, Profiles.Player));
        }

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
    }
}
