using YoursTruly.Core.Domain;
using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

public sealed class NormalizerTests
{
    [Theory]
    [InlineData("(435) 555-0142", "+14355550142", false)]
    [InlineData("435-555-0142", "+14355550142", false)]
    [InlineData("1 (435) 555-0142", "+14355550142", false)]
    [InlineData("555-0143", "+14355550143", true)]
    public void Turns_a_printed_number_into_one_that_can_be_dialled(string printed, string expected, bool assumed)
    {
        var (raw, e164, areaCodeAssumed) = Normalizer.ParsePhone(printed);
        Assert.Equal(printed, raw);
        Assert.Equal(expected, e164);
        Assert.Equal(assumed, areaCodeAssumed);
    }

    [Fact]
    public void Keeps_an_unusable_number_rather_than_discarding_it()
    {
        var (raw, e164, _) = Normalizer.ParsePhone("call the house");
        Assert.Equal("call the house", raw);
        Assert.Null(e164);
    }

    [Theory]
    [InlineData("17 Jan", 1, 17)]
    [InlineData("6 May", 5, 6)]
    [InlineData("31 Dec", 12, 31)]
    public void Reads_the_day_and_month_a_file_prints(string printed, int month, int day)
    {
        Assert.Equal((month, day), Normalizer.ParseBirthday(printed));
    }

    [Theory]
    [InlineData("9 Feb 1982", 2, 9)]
    [InlineData("12 Nov 2008", 11, 12)]
    [InlineData("9 Feb 82", 2, 9)]
    public void Reads_a_full_birth_date_and_throws_the_year_away(string printed, int month, int day)
    {
        // Files print the year on some rows and not others. the app stores only the
        // day and the month, and a date it could not read at all would be stored as no
        // birthday — which, on a file that carries birthdays, clears the one already
        // on record.
        Assert.Equal((month, day), Normalizer.ParseBirthday(printed));
    }

    [Theory]
    [InlineData("")]
    [InlineData("32 Jan")]
    [InlineData("17 Xyz")]
    public void Refuses_a_birthday_it_cannot_read(string printed)
    {
        Assert.Equal((null, null), Normalizer.ParseBirthday(printed));
    }

    [Theory]
    [InlineData("3/4/1982", 3, 4)]
    [InlineData("12-25", 12, 25)]
    public void Reads_a_date_written_in_numbers_the_way_the_United_States_writes_it(
        string printed, int month, int day)
    {
        // Ambiguous with the rest of the world and unavoidable: 03/04 is two guesses,
        // and this makes the local one.
        Assert.Equal((month, day), Normalizer.ParseBirthday(printed));
    }

    // ---- names, which most files do not write surname-first ------------------------

    private static ContactSheet Sheet(params string[] names) =>
        new(["Name", "Email"],
            [.. names.Select((n, i) => new SheetRow([n, $"p{i}@example.com"], 1))],
            1, "names.pdf", new string('a', 64));

    private static ImportMapping Mapping => new(
        [new(0, "Name", ImportField.Name), new(1, "Email", ImportField.Email)]);

    [Fact]
    public void Splits_a_name_printed_surname_first()
    {
        var person = Normalizer.Normalize(Sheet("Marchbank, Imogen Rose"), Mapping).Single();
        Assert.Equal("Marchbank", person.LastName);
        Assert.Equal("Imogen Rose", person.FirstName);
        Assert.Equal("Marchbank, Imogen Rose", person.DisplayName);
    }

    [Fact]
    public void Splits_a_name_printed_the_way_most_of_the_world_prints_it()
    {
        var person = Normalizer.Normalize(Sheet("Imogen Rose Marchbank"), Mapping).Single();
        Assert.Equal("Marchbank", person.LastName);
        Assert.Equal("Imogen Rose", person.FirstName);
    }

    [Fact]
    public void Which_way_round_the_names_are_is_read_off_the_file_not_assumed()
    {
        // Most rows have a comma, so the one that does not is surname-first too: the
        // person with two names is "Winslade Verity", not "Verity Winslade".
        var sheet = Sheet("Marchbank, Imogen", "Quilley, Barnaby", "Winslade Verity");
        var people = Normalizer.Normalize(sheet, Mapping);

        Assert.Equal(["Marchbank", "Quilley", "Winslade"], people.Select(p => p.LastName));
        Assert.Equal("Verity", people[2].FirstName);
    }

    [Fact]
    public void Two_columns_of_name_are_used_when_a_file_has_them()
    {
        var sheet = new ContactSheet(
            ["First", "Last"], [new SheetRow(["Imogen", "Marchbank"], 1)], 1, "n.pdf", new string('a', 64));
        var mapping = new ImportMapping(
            [new(0, "First", ImportField.FirstName), new(1, "Last", ImportField.LastName)]);

        var person = Normalizer.Normalize(sheet, mapping).Single();
        Assert.Equal("Marchbank", person.LastName);
        Assert.Equal("Imogen", person.FirstName);
        Assert.Equal("Marchbank, Imogen", person.DisplayName);
    }

    [Fact]
    public void A_row_with_no_name_in_it_is_not_a_person()
    {
        Assert.Single(Normalizer.Normalize(Sheet("Marchbank, Imogen", ""), Mapping));
    }
}

public sealed class PhoneFormatTests
{
    [Theory]
    [InlineData("+14355550100", "(435) 555-0100")]
    [InlineData("14355550100", "(435) 555-0100")]
    [InlineData("4355550100", "(435) 555-0100")]
    [InlineData("435-555-0100", "(435) 555-0100")]
    [InlineData("(435) 555-0100", "(435) 555-0100")]
    public void Numbers_are_shown_the_way_people_read_them(string stored, string shown)
    {
        Assert.Equal(shown, PhoneFormat.ForDisplay(stored));
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, "")]
    [InlineData("+447700900123", "+447700900123")]
    [InlineData("call the house", "call the house")]
    [InlineData("555-0100", "555-0100")]
    public void Anything_else_is_left_exactly_as_it_is(string? stored, string shown)
    {
        Assert.Equal(shown, PhoneFormat.ForDisplay(stored));
    }
}
