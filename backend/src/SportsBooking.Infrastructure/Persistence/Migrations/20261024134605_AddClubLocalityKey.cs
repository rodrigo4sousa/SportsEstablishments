using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SportsBooking.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClubLocalityKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "locality_key",
                table: "clubs",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Existing clubs get the key the application gives new ones (ClubRules.LocalityKey): no accents, lower case,
            // white space reduced to one space. The accent mapping is built here from the same Unicode decomposition,
            // for the Latin letters of U+00C0-U+017F, and written into the SQL, so this migration never changes even if
            // ClubRules.LocalityKey does.
            var accented = new StringBuilder();
            var plain = new StringBuilder();
            for (var code = 0x00C0; code <= 0x017F; code++)
            {
                var letter = ((char)code).ToString();
                var withoutAccent = new string(letter.Normalize(NormalizationForm.FormD)
                    .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                    .ToArray());
                if (withoutAccent.Length == 1 && withoutAccent != letter)
                {
                    accented.Append(letter);
                    plain.Append(withoutAccent);
                }
            }

            migrationBuilder.Sql(
                $@"UPDATE clubs SET locality_key = lower(btrim(regexp_replace(translate(locality, '{accented}', '{plain}'), '\s+', ' ', 'g')));");

            // The default only filled the existing rows; from now on every insert gives the key.
            migrationBuilder.Sql("ALTER TABLE clubs ALTER COLUMN locality_key DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "ix_clubs_locality_key",
                table: "clubs",
                column: "locality_key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_clubs_locality_key",
                table: "clubs");

            migrationBuilder.DropColumn(
                name: "locality_key",
                table: "clubs");
        }
    }
}
