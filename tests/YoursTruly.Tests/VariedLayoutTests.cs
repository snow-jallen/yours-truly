using System.Text.RegularExpressions;
using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>The same fifteen people, printed five different ways: a table, profile
/// cards, a two-up directory grouped by affiliation, intake forms, and a mainframe dump
/// of KEY=VALUE stanzas.
///
/// These are the files that showed the importer was wrong. Read by hunting line by line
/// for addresses — which is what it used to do — they came back as fifteen people called
/// "COMLINK MESSAGE ADDRESS", and two of the five quietly lost a third to two-thirds of
/// their people while reporting no problem at all.
///
/// None of these layouts is taught to the reader. If a sixth turns up and fails, the
/// answer is not a sixth reader.</summary>
public sealed class VariedLayoutTests
{
    public static TheoryData<string> Layouts() =>
    [
        "01-operations-table.pdf",
        "02-profile-cards.pdf",
        "03-affiliation-directory.pdf",
        "04-intake-forms.pdf",
        "05-mainframe-export.pdf",
    ];

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
    [MemberData(nameof(Layouts))]
    public void Says_the_columns_are_its_own_invention(string file)
    {
        // None of these files names its columns, so the import screen has to say the
        // headings came from the app rather than from the page.
        var sheet = ContactSheetReader.Read(TestPaths.Layout(file));
        Assert.NotEqual(SheetShape.Table, sheet.Shape);
        Assert.False(sheet.HeadingsFound);
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
