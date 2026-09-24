using System.Text.RegularExpressions;
using YoursTruly.Core.Import;
using YoursTruly.Data;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace YoursTruly.Tests;

/// <summary>Measures the reader against genuine exports, if any are present. These are
/// the numbers that decide whether an import can be trusted, so they are asserted
/// rather than merely printed — and none of these files is in the repository, so on
/// most machines they skip.
///
/// They matter more than they used to. The reader is no longer told what these files
/// are; it works the columns out. That is a much better deal for every other file in
/// the world, and it has to be shown not to have cost anything on the three these
/// tests were written against.</summary>
public sealed class RealReportTests(ITestOutputHelper output)
{
    private static readonly Regex Phone = new(@"^(\(\d{3}\)\s*)?\d{3}-\d{4}$");
    private static readonly Regex Email = new(@"^[^@\s]+@[^@\s]+\.[A-Za-z]{2,}$");
    private static readonly Regex Birthday = new(@"^\d{1,2} [A-Z][a-z]{2}( \d{4})?$");

    private void Check(string path, int pages, int people)
    {
        var sheet = ContactSheetReader.Read(path);

        int Bad(string heading, Regex shape)
        {
            var column = sheet.Column(heading);
            return sheet.Rows.Count(r => r.Cell(column).Length > 0 && !shape.IsMatch(r.Cell(column)));
        }

        output.WriteLine($"pages       {sheet.PageCount}");
        output.WriteLine($"people      {sheet.Rows.Count}");
        output.WriteLine($"columns     {string.Join(" | ", sheet.Columns)}");
        output.WriteLine($"mapped as   {string.Join(" | ", ImportMapping.Guess(sheet).Columns.Select(c => c.Field))}");
        output.WriteLine($"bad phone   {Bad("Phone", Phone)}");
        output.WriteLine($"bad email   {Bad("Email", Email)}");
        output.WriteLine($"bad bday    {Bad("Birth", Birthday)}");

        Assert.True(sheet.HeadingsFound, "the heading row was not found, so the file was read loosely");
        Assert.Equal(pages, sheet.PageCount);

        // The export prints its own count in its footer. Reading exactly that many
        // people back is the strongest check available that no row was dropped.
        Assert.Equal(people, sheet.Rows.Count);
        Assert.Equal(0, Bad("Phone", Phone));
        Assert.Equal(0, Bad("Email", Email));
        Assert.Equal(0, Bad("Birth", Birthday));

        var mapping = ImportMapping.Guess(sheet);
        Assert.Null(mapping.Problem);

        // Every name comes back as two parts, and every number the app would dial is
        // in the form the provider accepts. A count rather than Assert.All, which would
        // print the offending person — name, number and all — into the log.
        var read = Normalizer.Normalize(sheet, mapping);
        Assert.Equal(people, read.Count);
        Assert.Equal(0, read.Count(p => p.LastName.Length == 0 || p.FirstName.Length == 0));
        Assert.Equal(0, read.Count(p => p.PhoneE164 is not null && !DialForm().IsMatch(p.PhoneE164)));
    }

    [RequiresRealReport]
    public void Reads_every_person_from_the_single_adults_export() =>
        Check(TestPaths.RealReport!, pages: 29, people: 427);

    [RequiresRealCallingsReport]
    public void Reads_every_person_from_the_callings_export() =>
        Check(TestPaths.RealCallingsReport!, pages: 9, people: 428);

    [RequiresRealMemberList]
    public void Reads_every_person_from_the_member_list_export() =>
        Check(TestPaths.RealMemberList!, pages: 5, people: 160);

    [RequiresRealCallingsReport]
    public void Leaves_out_the_callings_table_printed_above_the_people()
    {
        // Page 1 prints the stake's callings above the members table, including rows
        // reading "Calling Vacant" and names with no contact details.
        var sheet = ContactSheetReader.Read(TestPaths.RealCallingsReport!);
        Assert.Equal(428, sheet.Rows.Count);
        Assert.Equal(0, sheet.Rows.Count(r => r.Text.Contains("Vacant", StringComparison.Ordinal)));
    }

    [RequiresRealReport]
    public void Offers_a_column_of_units_as_groups_rather_than_refusing_it()
    {
        // The word "Unit" means nothing to the app now, and it is the most useful
        // column in the file: every one of its values becomes a group.
        var sheet = ContactSheetReader.Read(TestPaths.RealReport!);
        Assert.Equal(ImportField.Groups, sheet.GuessFor("Unit"));

        var groups = Normalizer.Normalize(sheet, ImportMapping.Guess(sheet))
            .SelectMany(p => p.Groups).Distinct().ToList();
        output.WriteLine($"groups: {string.Join(", ", groups.Order())}");
        Assert.InRange(groups.Count, 5, 30);
    }

    private static Regex DialForm() => new(@"^\+1\d{10}$");
}

/// <summary>Runs a genuine export all the way through the importer into a real
/// database, then imports it again to prove it settles — the path the app takes,
/// not just the reader the tests above exercise.</summary>
public sealed class RealImportTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"yourstruly-real-{Guid.NewGuid():N}.db");

    private AppDbContext Open()
    {
        var db = AppDatabase.Open(_path);
        db.Database.Migrate();
        return db;
    }

    private async Task ImportTwiceAsync(string file, int people, int leastByPhone, int leastByEmail)
    {
        using var db = Open();
        var service = new ImportService(db);

        var sheet = ImportService.Read(file);
        var mapping = ImportMapping.Guess(sheet);
        var plan = await service.PlanAsync(sheet, mapping);

        output.WriteLine($"first run: {plan.Added.Count} added, {plan.Groups.Count} groups");
        Assert.Equal(people, plan.Added.Count);
        Assert.True(plan.Deactivated.Count == 0, $"{plan.Deactivated.Count} deactivated when none were expected");

        var run = await service.ApplyAsync(sheet, plan, mapping, new DateOnly(2026, 9, 16));
        Assert.Equal(people, run.AddedCount);

        var stored = await new DirectoryService(db).RecipientsAsync();
        Assert.Equal(people, stored.Count);

        var withPhone = stored.Count(p => p.Phone is not null);
        var withEmail = stored.Count(p => p.Email is not null);
        output.WriteLine($"reachable by phone: {withPhone}, by email: {withEmail}");
        Assert.True(withPhone > leastByPhone, $"only {withPhone} people got a usable phone number");
        Assert.True(withEmail > leastByEmail, $"only {withEmail} people got an email address");

        // Every phone the app would dial must be in the form the provider accepts. A
        // count rather than Assert.All, which would print the offending person.
        var dialForm = new Regex(@"^\+1\d{10}$");
        Assert.Equal(0, stored.Count(p => p.Phone is not null && !dialForm.IsMatch(p.Phone)));

        // Importing the very same file again must change nothing at all. This is the
        // check that matters most: the app re-imports every week.
        var second = await service.PlanAsync(ImportService.Read(file), mapping);
        output.WriteLine($"second run: {second.Added.Count} added, {second.Updated.Count} updated, " +
                         $"{second.Deactivated.Count} deactivated, {second.Unchanged} unchanged");

        Assert.True(second.Added.Count == 0, $"{second.Added.Count} added on a re-import that should change nothing");
        Assert.True(second.Updated.Count == 0, $"{second.Updated.Count} updated on a re-import that should change nothing");
        Assert.True(second.Deactivated.Count == 0, $"{second.Deactivated.Count} deactivated on a re-import that should change nothing");
        Assert.Equal(people, second.Unchanged);
    }

    [RequiresRealReport]
    public Task The_whole_directory_imports_and_then_imports_again_unchanged() =>
        ImportTwiceAsync(TestPaths.RealReport!, 427, leastByPhone: 300, leastByEmail: 200);

    [RequiresRealCallingsReport]
    public Task The_callings_export_imports_and_then_imports_again_unchanged() =>
        ImportTwiceAsync(TestPaths.RealCallingsReport!, 428, leastByPhone: 300, leastByEmail: 200);

    [RequiresRealMemberList]
    public Task The_whole_ward_imports_and_then_imports_again_unchanged() =>
        ImportTwiceAsync(TestPaths.RealMemberList!, 160, leastByPhone: 100, leastByEmail: 100);

    public void Dispose()
    {
        // Deliberately not ClearAllPools: it is process-wide, and clearing pools while
        // another test class still holds a connection makes unrelated tests fail.
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { /* a temp file left behind harms nothing */ }
    }
}
