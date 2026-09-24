using YoursTruly.Core.Import;

namespace YoursTruly.Tests;

public sealed class ImportPlannerTests
{
    private static NormalizedPerson Incoming(
        string last, string first,
        string? email = null, string? phone = null, int month = 3, int day = 4,
        int? age = 40, params string[] groups) =>
        new(last, first, $"{last}, {first}", age, month, day,
            email, phone, phone is null ? null : "+14355550100", false, groups);

    private static ExistingPerson Existing(
        string last, string first,
        string? email = null, string? phone = null, bool active = true, int month = 3, int day = 4,
        int? age = 40) =>
        new(Guid.NewGuid(), last, first, month, day, age, email, phone, active);

    /// <summary>A mapping over columns with these fields in them, which is all the
    /// planner ever asks of one: which fields the file is being imported for.</summary>
    private static ImportMapping Carrying(params ImportField[] fields) =>
        new([.. fields.Select((f, i) => new ColumnMapping(i, f.Label(), f))]);

    private static readonly ImportMapping Everything = Carrying(
        ImportField.Name, ImportField.Email, ImportField.Phone,
        ImportField.Age, ImportField.Birthday, ImportField.Groups);

    private static readonly ImportMapping NoAges = Carrying(
        ImportField.Name, ImportField.Email, ImportField.Phone, ImportField.Birthday);

    [Fact]
    public void A_file_with_no_age_column_does_not_clear_the_ages_on_record()
    {
        var existing = Existing("Ashby", "Miriam", age: 86);
        var incoming = Incoming("Ashby", "Miriam", age: null);

        var plan = ImportPlanner.Plan([incoming], [existing], NoAges);

        Assert.Empty(plan.Updated);
        Assert.Equal(1, plan.Unchanged);
    }

    [Fact]
    public void A_file_that_does_have_an_age_column_clears_one_it_leaves_blank()
    {
        // The field was reported, and reported as empty. That is a change.
        var existing = Existing("Ashby", "Miriam", age: 86);
        var incoming = Incoming("Ashby", "Miriam", age: null);

        var plan = ImportPlanner.Plan([incoming], [existing], Everything);

        var change = Assert.Single(Assert.Single(plan.Updated).Changes);
        Assert.Equal("Age", change.Field);
        Assert.Null(change.To);
    }

    [Fact]
    public void A_file_still_updates_the_fields_it_does_carry()
    {
        var existing = Existing("Ashby", "Miriam", age: 86, phone: "(435) 555-0142");
        var incoming = Incoming("Ashby", "Miriam", age: null, phone: "(435) 555-0199");

        var plan = ImportPlanner.Plan([incoming], [existing], NoAges);

        var change = Assert.Single(Assert.Single(plan.Updated).Changes);
        Assert.Equal("Phone", change.Field);
    }

    [Fact]
    public void Adds_someone_who_was_not_there_before()
    {
        var plan = ImportPlanner.Plan([Incoming("Ashby", "Miriam")], [], Everything);
        Assert.Single(plan.Added);
        Assert.Empty(plan.Updated);
        Assert.Empty(plan.Deactivated);
    }

    [Fact]
    public void Reports_the_fields_that_changed_and_nothing_else()
    {
        var plan = ImportPlanner.Plan(
            [Incoming("Winslade", "Verity", phone: "(435) 555-0199")],
            [Existing("Winslade", "Verity", phone: "(435) 555-0142")], Everything);

        var update = Assert.Single(plan.Updated);
        var change = Assert.Single(update.Changes);
        Assert.Equal("Phone", change.Field);
        Assert.Equal("(435) 555-0142", change.From);
        Assert.Equal("(435) 555-0199", change.To);
    }

    [Fact]
    public void Counts_an_unchanged_person_as_unchanged()
    {
        var plan = ImportPlanner.Plan(
            [Incoming("Quilley", "Barnaby", email: "r@example.com")],
            [Existing("Quilley", "Barnaby", email: "r@example.com")], Everything);

        Assert.Equal(1, plan.Unchanged);
        Assert.Empty(plan.Updated);
    }

    [Fact]
    public void Deactivates_someone_the_file_no_longer_lists()
    {
        var plan = ImportPlanner.Plan([], [Existing("Peacock", "Alvin")], Everything);
        Assert.Single(plan.Deactivated);
        Assert.Empty(plan.Added);
    }

    [Fact]
    public void Leaves_everyone_alone_when_the_file_is_not_the_whole_list()
    {
        // Adding a second list to the same file — a committee on top of a roster —
        // must not mark the roster as having left. The import screen asks.
        var plan = ImportPlanner.Plan(
            [Incoming("Ashby", "Miriam")],
            [Existing("Peacock", "Alvin")], Everything, deactivateMissing: false);

        Assert.Empty(plan.Deactivated);
        Assert.Single(plan.Added);
    }

    [Fact]
    public void Brings_back_someone_who_reappears_rather_than_adding_them_twice()
    {
        var plan = ImportPlanner.Plan(
            [Incoming("Peacock", "Alvin")],
            [Existing("Peacock", "Alvin", active: false)], Everything);

        Assert.Single(plan.Reactivated);
        Assert.Empty(plan.Added);
        Assert.Empty(plan.Deactivated);
    }

    [Fact]
    public void Matches_on_the_name_when_a_birthday_gets_filled_in()
    {
        var incoming = Incoming("Tuttle", "Marvin", month: 5, day: 14);
        var existing = new ExistingPerson(
            Guid.NewGuid(), "Tuttle", "Marvin", null, null, 40, null, null, true);

        var plan = ImportPlanner.Plan([incoming], [existing], Everything);

        Assert.Empty(plan.Added);
        Assert.Equal("Birthday", Assert.Single(Assert.Single(plan.Updated).Changes).Field);
    }

    [Fact]
    public void Tells_two_people_of_the_same_name_apart_by_birthday()
    {
        var plan = ImportPlanner.Plan(
            [Incoming("Denholm", "John", month: 1, day: 12), Incoming("Denholm", "John", month: 9, day: 3)],
            [Existing("Denholm", "John", month: 1, day: 12), Existing("Denholm", "John", month: 9, day: 3)],
            Everything);

        Assert.Equal(2, plan.Unchanged);
        Assert.Empty(plan.Added);
        Assert.Empty(plan.Deactivated);
    }

    [Fact]
    public void Names_every_group_the_file_would_make()
    {
        var plan = ImportPlanner.Plan(
        [
            Incoming("Ashgrove", "Adelaide", groups: "U12 Blue"),
            Incoming("Quilley", "Barnaby", groups: "U12 Blue"),
            Incoming("Winslade", "Verity", groups: "U14 Red"),
        ], [], Everything);

        Assert.Equal(["U12 Blue", "U14 Red"], plan.Groups);
    }

    [Fact]
    public void Somebody_added_by_hand_is_never_deactivated_by_a_file_that_omits_them()
    {
        var byHand = Existing("Peacock", "Alvin") with { AddedByHand = true };

        var plan = ImportPlanner.Plan([Incoming("Ashby", "Miriam")], [byHand], Everything);

        Assert.Empty(plan.Deactivated);
    }
}
