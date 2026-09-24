using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

/// <summary>Telling a name from everything else printed beside it, which is the one
/// piece of a record that has no syntax to recognise it by. See
/// <see cref="PersonName"/>.</summary>
public sealed class PersonNameTests
{
    [Theory]
    [InlineData("Luke Skywalker", "Luke Skywalker")]
    [InlineData("Obi-Wan Kenobi", "Obi-Wan Kenobi")]
    [InlineData("Ashgrove, Adelaide", "Ashgrove, Adelaide")]
    public void Reads_a_name_as_the_file_wrote_it(string text, string expected) =>
        Assert.Equal(expected, PersonName.LeadingName(text));

    [Theory]
    // A run usually carries the name and then something else, and asking whether the
    // whole run is a name would throw every one of these away.
    [InlineData("Padme Amidala SW-011", "Padme Amidala")]
    [InlineData("Luke Skywalker Tatooine Human Pilot", "Luke Skywalker Tatooine Human")]
    [InlineData("Verity Winslade, v.winslade@example.com", "Verity Winslade")]
    [InlineData("Han Solo (435) 555-0101", "Han Solo")]
    public void Takes_the_name_off_the_front_of_a_longer_run(string text, string expected) =>
        Assert.Equal(expected, PersonName.LeadingName(text));

    [Theory]
    // The things a page prints that are not people. All of them were imported as
    // people by the reader this replaced.
    [InlineData("COMLINK MESSAGE ADDRESS")]
    [InlineData("SPECIES=HUMAN")]
    [InlineData("435-555-0101")]
    [InlineData("luke.skywalker@example.com")]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_what_is_not_a_name(string text) =>
        Assert.Null(PersonName.LeadingName(text));
}
