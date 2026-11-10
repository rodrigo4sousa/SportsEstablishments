using SportsBooking.Application.Accounts;
using SportsBooking.Domain.Accounts;

namespace SportsBooking.UnitTests.Accounts;

/// <summary>US30: the address left on a deleted account, and the password field of the request.</summary>
public sealed class DeletedAccountTests
{
    [Fact]
    public void The_tombstone_is_unique_says_nothing_about_the_person_and_cannot_receive_mail()
    {
        var id = Guid.CreateVersion7();

        var email = DeletedAccount.TombstoneEmail(id);

        email.ShouldBe($"deleted-{id:N}@deleted.invalid");
        email.ShouldNotBe(DeletedAccount.TombstoneEmail(Guid.CreateVersion7()));
        EmailAddressPolicy.IsValid(email).ShouldBeTrue();
    }

    [Theory]
    [InlineData("deleted-abc@deleted.invalid", true)]
    [InlineData("DELETED-ABC@DELETED.INVALID", true)]
    [InlineData("rita@exemplo.test", false)]
    [InlineData("rita@deleted.invalid.example.test", false)]
    public void A_tombstone_is_recognised_by_its_domain(string email, bool expected) =>
        DeletedAccount.IsTombstone(email).ShouldBe(expected);

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", true)]
    [InlineData("a-password", true)]
    public void The_password_only_has_to_be_filled_in(string password, bool valid) =>
        new DeleteAccountRequestValidator().Validate(new DeleteAccountRequest(password)).IsValid.ShouldBe(valid);

    [Fact]
    public void A_missing_password_counts_as_empty() =>
        new DeleteAccountRequest(null!).Normalized().Password.ShouldBe(string.Empty);
}
