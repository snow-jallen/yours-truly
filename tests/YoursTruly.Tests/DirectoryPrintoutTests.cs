using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>A printed directory: no table anywhere, people side by side in bands, and
/// several of them sharing every line.
///
/// Read as lines this comes apart into nonsense — the two adults' names, callings and
/// numbers all run together and one of them vanishes. Read loosely, which is what used
/// to happen, a 32-page printout of 436 people came back as 161 rows, each one a whole
/// household mashed into a single name.</summary>
public sealed class DirectoryPrintoutTests
{
    private static ContactSheet Sheet() =>
        SheetHelp.Read(SyntheticDirectory.Build(), "ward-directory.pdf");

    [Fact]
    public void Recognises_a_printout_that_is_not_a_table()
    {
        var sheet = Sheet();
        Assert.Equal(SheetShape.Directory, sheet.Shape);
        Assert.Equal(["Name", "Email", "Phone"], sheet.Columns);
    }

    [Fact]
    public void Finds_every_individual_and_not_one_row_per_household()
    {
        // Four in the first household, one in the second, six in the third.
        var people = Sheet().People();
        Assert.Equal(11, people.Count);
    }

    [Fact]
    public void Gives_each_person_the_household_surname_and_their_own_given_name()
    {
        var people = Sheet().People();
        Assert.Contains(people, p => p is { LastName: "Ashgrove", FirstName: "Tyler" });
        Assert.Contains(people, p => p is { LastName: "Ashgrove", FirstName: "Whitney Leigh" });
        Assert.Contains(people, p => p is { LastName: "Quilley", FirstName: "Emma" });
        Assert.Contains(people, p => p is { LastName: "Winslade", FirstName: "Jonathan" });
    }

    [Fact]
    public void Keeps_two_people_who_share_a_line_apart()
    {
        // The whole difficulty: Tyler's details and Whitney's sit on the same lines,
        // told apart only by which band they start in.
        var people = Sheet().People();

        var tyler = Assert.Single(people, p => p.FirstName == "Tyler" && p.LastName == "Ashgrove");
        Assert.Equal("tyler.ashgrove@example.com", tyler.Email);
        Assert.Equal("+14358513732", tyler.PhoneE164);

        var whitney = Assert.Single(people, p => p.FirstName == "Whitney Leigh");
        Assert.Null(whitney.Email);
        Assert.Equal("+18019955902", whitney.PhoneE164);
    }

    [Fact]
    public void Reads_children_across_three_bands_and_two_lines()
    {
        var winslade = Sheet().People().Where(p => p.LastName == "Winslade").ToList();
        Assert.Equal(
            ["Cora Lucille", "James", "Jonathan", "Lydia Jean", "Melinda Jane", "Samantha"],
            winslade.Select(p => p.FirstName).Order());
    }

    [Fact]
    public void Somebody_with_no_way_of_being_reached_is_still_a_person()
    {
        // Children have a name and nothing else. They are individuals, the directory
        // lists them, and a phone number can be added later.
        var people = Sheet().People();
        var child = Assert.Single(people, p => p.FirstName == "Carson Tyler");
        Assert.Null(child.Email);
        Assert.Null(child.PhoneRaw);
    }

    [Fact]
    public void Leaves_out_the_address_the_page_header_and_the_footer()
    {
        var people = Sheet().People();
        Assert.DoesNotContain(people, p => p.DisplayName.Contains("Union", StringComparison.Ordinal));
        Assert.DoesNotContain(people, p => p.DisplayName.Contains("Ward", StringComparison.Ordinal));
        Assert.DoesNotContain(people, p => p.DisplayName.Contains("rights", StringComparison.Ordinal));

        // The address sits at a size between the names and the details, so it would be
        // read as somebody's detail if the sizes were taken on trust.
        Assert.DoesNotContain(people, p => p.Email?.Contains("Union", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void A_file_that_is_a_table_is_still_read_as_one()
    {
        // The printout reader runs before the loose one and after the table one, so it
        // must not claim anything the other two should have had.
        Assert.Equal(SheetShape.Table, SheetHelp.Read(SyntheticReport.Build(), "table.pdf").Shape);
        Assert.Equal(SheetShape.Table, SheetHelp.Read(SyntheticRoster.Table(), "roster.pdf").Shape);
        Assert.Equal(SheetShape.Records, SheetHelp.Read(SyntheticRoster.Loose(), "tree.pdf").Shape);
    }

    [RequiresRealDirectory]
    public void The_real_printed_directory_still_reads_as_a_directory()
    {
        // Thirty-two pages of households, the file this reader was written for. It is
        // read before the record reader underneath it and has to stay that way: it
        // finds the hundreds of people who have no email address and no phone number
        // at all, and anchoring on contact details would never see one of them.
        var sheet = ContactSheetReader.Read(TestPaths.RealDirectory!);
        Assert.Equal(SheetShape.Directory, sheet.Shape);

        var people = sheet.People();
        Assert.InRange(people.Count, 400, 500);

        // Individuals, not households. Read as one row per printed block it came back
        // with well under half this many.
        Assert.InRange(people.Select(p => p.LastName).Distinct().Count(), 100, 300);
        Assert.All(people, p => Assert.NotEqual("", p.LastName));
    }
}
