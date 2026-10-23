using System.Text.RegularExpressions;

namespace SportsBooking.UnitTests.Accounts;

/// <summary>
/// US04: no endpoint or page may create admin accounts or promote accounts to admin.
/// The startup seed is the only code allowed to assign the Admin profile.
/// </summary>
public sealed partial class AdminProfileGuardTests
{
    private const string AllowedFile = "AdminAccountSeeder.cs";

    [GeneratedRegex(@"\bAddToRoles?Async\b|\bAddClaims?Async\b")]
    private static partial Regex AssignsProfileOrClaim();

    [GeneratedRegex(@"Profiles\.Admin\b|""admin""", RegexOptions.IgnoreCase)]
    private static partial Regex MentionsAdmin();

    [Fact]
    public void Only_the_admin_seeder_assigns_the_admin_profile()
    {
        var sourceFiles = BackendSourceFiles();

        var offenders = sourceFiles
            .Where(file => Path.GetFileName(file) != AllowedFile)
            .Where(file =>
            {
                var code = File.ReadAllText(file);
                return AssignsProfileOrClaim().IsMatch(code) && MentionsAdmin().IsMatch(code);
            })
            .Select(Path.GetFileName)
            .ToList();

        offenders.ShouldBeEmpty();

        // Keeps the guard meaningful: the seeder must still exist and assign the profile.
        var seeder = sourceFiles.Where(file => Path.GetFileName(file) == AllowedFile).ShouldHaveSingleItem();
        var seederCode = File.ReadAllText(seeder);
        AssignsProfileOrClaim().IsMatch(seederCode).ShouldBeTrue();
        MentionsAdmin().IsMatch(seederCode).ShouldBeTrue();
    }

    private static List<string> BackendSourceFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("repository root (global.json) not found");
        var sourceRoot = Path.Combine(directory.FullName, "backend", "src");
        var separator = Path.DirectorySeparatorChar;

        return Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
                && !file.Contains($"{separator}bin{separator}", StringComparison.Ordinal))
            .ToList();
    }
}
