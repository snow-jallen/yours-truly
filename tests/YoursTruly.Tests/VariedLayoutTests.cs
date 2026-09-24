using System.Text.RegularExpressions;
using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>Fifteen people printed six different ways: a table with no headings, profile
/// cards, a two-up directory grouped by affiliation, intake forms, a mainframe dump of
/// KEY=VALUE stanzas, and a nine-column report that does have a heading row.
///
/// These are the files that showed the importer was wrong. Read by hunting line by line
/// for addresses — which is what it used to do — they came back as fifteen people called
/// "COMLINK MESSAGE ADDRESS", and two of them quietly lost a third to two-thirds of their
/// people while reporting no problem at all.
///
/// They live in samples/ because they are also what somebody is invited to try the app
/// on. None of these layouts is taught to the reader. If a seventh turns up and fails,
/// the answer is not a seventh reader.</summary>
public sealed class VariedLayoutTests
{
    /// <summary>The five that name nothing about themselves.</summary>
    private static readonly string[] NoHeadings =
    [
        "01-operations-table.pdf",
        "02-profile-cards.pdf",
        "03-affiliation-directory.pdf",
        "04-intake-forms.pdf",
        "05-mainframe-export.pdf",
    ];

    /// <summary>The one file that does name its own columns.</summary>
    private const string WithHeadings = "06-system-report.pdf";

    public static TheoryData<string> Headless() => [.. NoHeadings];

    /// <summary>All six. Whatever else differs between them, every one has to give up
    /// the same fifteen people.</summary>
    public static TheoryData<string> Layouts() => [.. NoHeadings, WithHeadings];

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Finds_every_person_however_the_page_is_laid_out(string file)
    {
        var sheet = ContactSheetReader.Read(TestPaths.Layout(file));
        Assert.Equal(15, sheet.Rows.Count);
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Reads_the_name_that_belongs_to_each_address(string file)
    {
        // Every one of the fifteen is named in their own email address, so the check is
        // against the file rather than against a list typed out here: luke.skywalker@
        // belongs to Luke Skywalker and nobody else.
        var sheet = ContactSheetReader.Read(TestPaths.Layout(file));
        var people = sheet.People();

        // Against the sort name rather than the display name, so that the given name
        // and the surname having been told apart is checked too.
        var wrong = people
            .Where(p => Bare(p.SortName) != Bare(FromAddress(p.Email)))
            .Select(p => $"{p.Email} → “{p.SortName}”")
            .ToList();

        Assert.True(wrong.Count == 0,
            $"{wrong.Count} of {people.Count} names do not match their address:"
            + Environment.NewLine + string.Join(Environment.NewLine, wrong.Take(5)));
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void Reads_a_usable_phone_number_for_everybody(string file)
    {
        var people = ContactSheetReader.Read(TestPaths.Layout(file)).People();

        Assert.Equal(15, people.Count(p => p.PhoneE164 is not null));

        // And each person's own, not the first one on the page — which is what a reader
        // that looks near an address rather than inside a record comes back with.
        Assert.Equal(15, people.Select(p => p.PhoneE164).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Headless))]
    public void Says_the_columns_are_its_own_invention(string file)
    {
        // None of these five names its columns, so the import screen has to say the
        // headings came from the app rather than from the page.
        var sheet = ContactSheetReader.Read(TestPaths.Layout(file));
        Assert.NotEqual(SheetShape.Table, sheet.Shape);
        Assert.False(sheet.HeadingsFound);
    }

    [Fact]
    public void Uses_a_heading_row_where_the_file_has_one()
    {
        // The sixth file is the easy case and has to stay easy. A heading row is the
        // only thing that says what a column *means* — geometry cannot tell an interest
        // from a department — so a file carrying one must never fall through to the
        // record reader, which would find the same people and throw the other six
        // columns away.
        var sheet = ContactSheetReader.Read(TestPaths.Layout(WithHeadings));

        Assert.Equal(SheetShape.Table, sheet.Shape);
        Assert.True(sheet.HeadingsFound);
        Assert.Equal(
            ["RECORD", "NAME", "PHONE", "EMAIL", "DEPARTMENT", "LOCATION", "START DATE",
             "INTEREST", "STATUS"],
            sheet.Columns);

        // And the columns it offers are the file's own words, which is what the user
        // then maps on the import screen.
        Assert.Contains("Operations", sheet.Sample(4));
    }

    [Fact]
    public void Says_how_many_people_it_could_reach_but_could_not_name()
    {
        // The whole point of the rewrite. The old reader never failed: it took whatever
        // fell out of a page it had misread and offered it behind a green Import button,
        // and the people it had lost were simply not mentioned. Names are the thing a
        // misread loses first — an address announces itself and a name does not — so the
        // count of nameless rows is the honest measure of a bad read.
        var sheet = SheetHelp.Read(SyntheticRoster.Nameless(), "refs.pdf");
        var mapping = ImportMapping.Guess(sheet);

        Assert.Equal(5, sheet.Rows.Count);
        Assert.Empty(Normalizer.Normalize(sheet, mapping));
        Assert.Equal(5, Normalizer.Unnamed(sheet, mapping));
    }

    /// <summary>"luke.skywalker@example.com" → "Skywalker, Luke", as the app stores it.</summary>
    private static string FromAddress(string? email)
    {
        var parts = (email ?? "").Split('@')[0].Split('.');
        var words = parts.Select(p => p.Length == 0 ? p : char.ToUpperInvariant(p[0]) + p[1..]).ToList();
        return words.Count == 1 ? words[0] : $"{words[^1]}, {string.Join(' ', words.Take(words.Count - 1))}";
    }

    /// <summary>Compared without punctuation or case: an address cannot carry the hyphen
    /// in Obi-Wan, and the reader is right to keep it.</summary>
    private static string Bare(string? t) =>
        new((t ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
