using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace YoursTruly.Data.Migrations
{
    /// <summary>The app stops being a reader of one organisation's reports.
    ///
    /// Wards and postal addresses go: the first was a list of ten units of one stake,
    /// and the second was never sent to. A person can now choose several channels
    /// rather than one, so PreferredChannel becomes PreferredChannels, holding their
    /// names joined by commas. Everything named after the system the files used to come
    /// from is renamed after what it actually is.
    ///
    /// Hand-written. Scaffolded, this came out as a rename of Ward to ImportedPhone and
    /// of LcrPhone to ImportedEmail — the columns match up that way by position, and
    /// every phone number in the directory would have become somebody's e-mail address.
    /// </summary>
    public partial class GenericImportAndSeveralChannels : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- what the fields are actually called ---------------------------------
            migrationBuilder.RenameColumn(
                name: "LcrEmail", table: "People", newName: "ImportedEmail");

            migrationBuilder.RenameColumn(
                name: "LcrPhone", table: "People", newName: "ImportedPhone");

            migrationBuilder.RenameColumn(
                name: "LastSeenInLcrOn", table: "ContactPoints", newName: "LastSeenInImportOn");

            // Still the same thing: a detail typed in by hand that somebody has since
            // copied back into wherever the list comes from.
            migrationBuilder.RenameColumn(
                name: "EnteredInLcrOn", table: "ContactPoints", newName: "CopiedBackOn");

            migrationBuilder.RenameIndex(
                name: "IX_ContactPoints_Source_EnteredInLcrOn_LastSeenInLcrOn",
                table: "ContactPoints",
                newName: "IX_ContactPoints_Source_CopiedBackOn_LastSeenInImportOn");

            // Enums are stored by name, so renaming one is a data change as well.
            migrationBuilder.Sql("UPDATE People SET Source = 'Imported' WHERE Source = 'Lcr';");
            migrationBuilder.Sql("UPDATE ContactPoints SET Source = 'Imported' WHERE Source = 'Lcr';");

            // --- several channels per person -----------------------------------------
            migrationBuilder.AddColumn<string>(
                name: "PreferredChannels",
                table: "People",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            // The one channel each person had is the set containing it. "none" was how
            // "not decided yet" used to be written; an empty set is how it is written now.
            migrationBuilder.Sql(
                "UPDATE People SET PreferredChannels = " +
                "CASE WHEN PreferredChannel IN ('email','text','voice','whatsapp') " +
                "THEN PreferredChannel ELSE '' END;");

            migrationBuilder.DropColumn(name: "PreferredChannel", table: "People");

            // --- things the app no longer holds ---------------------------------------
            migrationBuilder.DropIndex(name: "IX_People_Ward", table: "People");
            migrationBuilder.DropColumn(name: "Ward", table: "People");
            migrationBuilder.DropColumn(name: "Address", table: "People");

            // Postal addresses were kept only so they could be copied back into the
            // system the files came from. the app no longer posts anything, and no
            // longer knows anything about where the files came from.
            migrationBuilder.Sql("DELETE FROM ContactPoints WHERE Kind = 'Address';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredChannel",
                table: "People",
                type: "TEXT",
                maxLength: 16,
                nullable: false,
                defaultValue: "");

            // Somebody who chose several comes back with the first of them; there was
            // nowhere to put the rest before, and there is nowhere to put them now.
            migrationBuilder.Sql(
                "UPDATE People SET PreferredChannel = " +
                "CASE WHEN instr(PreferredChannels, ',') > 0 " +
                "THEN substr(PreferredChannels, 1, instr(PreferredChannels, ',') - 1) " +
                "ELSE PreferredChannels END;");
            migrationBuilder.Sql("UPDATE People SET PreferredChannel = 'none' WHERE PreferredChannel = '';");

            migrationBuilder.DropColumn(name: "PreferredChannels", table: "People");

            migrationBuilder.AddColumn<string>(
                name: "Ward", table: "People", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "Address", table: "People", type: "TEXT", nullable: true);

            migrationBuilder.RenameColumn(
                name: "ImportedEmail", table: "People", newName: "LcrEmail");
            migrationBuilder.RenameColumn(
                name: "ImportedPhone", table: "People", newName: "LcrPhone");
            migrationBuilder.RenameColumn(
                name: "LastSeenInImportOn", table: "ContactPoints", newName: "LastSeenInLcrOn");
            migrationBuilder.RenameColumn(
                name: "CopiedBackOn", table: "ContactPoints", newName: "EnteredInLcrOn");

            migrationBuilder.RenameIndex(
                name: "IX_ContactPoints_Source_CopiedBackOn_LastSeenInImportOn",
                table: "ContactPoints",
                newName: "IX_ContactPoints_Source_EnteredInLcrOn_LastSeenInLcrOn");

            migrationBuilder.Sql("UPDATE People SET Source = 'Lcr' WHERE Source = 'Imported';");
            migrationBuilder.Sql("UPDATE ContactPoints SET Source = 'Lcr' WHERE Source = 'Imported';");

            migrationBuilder.CreateIndex(name: "IX_People_Ward", table: "People", column: "Ward");
        }
    }
}
