using System.Buffers.Binary;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using SportsBooking.Domain.Accounts;
using SportsBooking.Infrastructure.Identity;
using SportsBooking.Infrastructure.Persistence;
using SportsBooking.IntegrationTests.Infrastructure;

namespace SportsBooking.IntegrationTests.Accounts;

public sealed class AccountStoreTests(ApiFactory factory)
{
    private static readonly DateTime RegisteredAt = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task The_three_profile_roles_exist_after_migrations()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

        foreach (var profile in Profiles.All)
        {
            (await roleManager.RoleExistsAsync(profile)).ShouldBeTrue(profile);
        }
    }

    [Fact]
    public async Task Passwords_are_stored_as_pbkdf2_hashes_and_ids_are_guid_v7()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        const string password = "palavra-passe-segura";
        var user = new AppUser(UniqueEmail("hash"), "Conta de teste", RegisteredAt);

        (await userManager.CreateAsync(user, password)).Succeeded.ShouldBeTrue();

        var stored = await userManager.FindByIdAsync(user.Id.ToString());
        stored.ShouldNotBeNull();
        stored.Id.Version.ShouldBe(7);
        stored.PasswordHash.ShouldNotBeNullOrWhiteSpace();
        stored.PasswordHash.ShouldNotContain(password);
        // Identity v3 format marker: PBKDF2 with HMAC-SHA512 and a random salt.
        Convert.FromBase64String(stored.PasswordHash)[0].ShouldBe((byte)0x01);
        (await userManager.CheckPasswordAsync(stored, password)).ShouldBeTrue();
        (await userManager.CheckPasswordAsync(stored, "outra-palavra-passe")).ShouldBeFalse();
    }

    [Fact]
    public async Task New_password_hashes_use_pbkdf2_hmac_sha512_with_210000_iterations()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = new AppUser(UniqueEmail("work-factor"), "Conta de teste", RegisteredAt);

        (await userManager.CreateAsync(user, "palavra-passe-segura")).Succeeded.ShouldBeTrue();

        // Identity v3 layout: format marker (1 byte), then PRF, iteration count and salt length (big-endian uint32 each).
        var hash = Convert.FromBase64String(user.PasswordHash.ShouldNotBeNull());
        hash[0].ShouldBe((byte)0x01);
        BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1, 4)).ShouldBe((uint)KeyDerivationPrf.HMACSHA512);
        BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5, 4)).ShouldBe(210_000u);
        BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(9, 4)).ShouldBe(16u);
    }

    [Fact]
    public async Task Hashes_with_fewer_iterations_are_upgraded_on_the_next_successful_check()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        const string password = "palavra-passe-antiga";
        var user = new AppUser(UniqueEmail("rehash"), "Conta de teste", RegisteredAt);
        (await userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();

        // A hash made with the previous default work factor (100,000 iterations).
        var legacyHasher = new PasswordHasher<AppUser>(Options.Create(new PasswordHasherOptions { IterationCount = 100_000 }));
        user.PasswordHash = legacyHasher.HashPassword(user, password);
        (await userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();

        (await userManager.CheckPasswordAsync(user, password)).ShouldBeTrue();

        var stored = (await userManager.FindByIdAsync(user.Id.ToString())).ShouldNotBeNull();
        BinaryPrimitives.ReadUInt32BigEndian(Convert.FromBase64String(stored.PasswordHash!).AsSpan(5, 4)).ShouldBe(210_000u);
    }

    [Fact]
    public async Task Duplicate_email_is_rejected_case_insensitively()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = UniqueEmail("dup");

        (await userManager.CreateAsync(new AppUser(email, "Conta de teste", RegisteredAt), "palavra-passe-1")).Succeeded.ShouldBeTrue();
        var duplicate = await userManager.CreateAsync(new AppUser(email.ToUpperInvariant(), "Conta de teste", RegisteredAt), "palavra-passe-2");

        duplicate.Succeeded.ShouldBeFalse();
        duplicate.Errors.Select(error => error.Code).ShouldContain(nameof(IdentityErrorDescriber.DuplicateEmail));
    }

    [Fact]
    public async Task Database_rejects_duplicate_email_even_without_user_manager()
    {
        var email = UniqueEmail("db-dup");
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.Users.Add(NewUserRow(email, userName: email));
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        dbContext.ChangeTracker.Clear();
        // Different user name, same normalized email.
        dbContext.Users.Add(NewUserRow(email.ToUpperInvariant(), userName: "other-" + email));

        var exception = await Should.ThrowAsync<DbUpdateException>(() => dbContext.SaveChangesAsync(TestContext.Current.CancellationToken));

        var postgres = exception.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("ix_users_normalized_email");
    }

    private static AppUser NewUserRow(string email, string userName) => new(email, "Conta de teste", RegisteredAt)
    {
        UserName = userName,
        NormalizedUserName = userName.ToUpperInvariant(),
        NormalizedEmail = email.ToUpperInvariant(),
        SecurityStamp = Guid.NewGuid().ToString(),
    };
}
