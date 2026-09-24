using YoursTruly.Core.Domain;
using YoursTruly.Core.Import;
using YoursTruly.Data;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Tests;

public sealed class GroupServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"yourstruly-groups-{Guid.NewGuid():N}.db");
    private static readonly DateOnly Today = new(2026, 9, 23);
    private static readonly ImportMapping Everything = new(
    [
        new(0, "Name", ImportField.Name), new(1, "Email", ImportField.Email),
        new(2, "Phone", ImportField.Phone), new(3, "Birthday", ImportField.Birthday),
    ]);

    private AppDbContext Open()
    {
        var db = AppDatabase.Open(_path);
        db.Database.Migrate();
        return db;
    }

    private static NormalizedPerson Person(string last, string first) =>
        new(last, first, $"{last}, {first}", 40, 3, 4, null, null, null, false, []);

    private static async Task ImportAsync(AppDbContext db, params NormalizedPerson[] people)
    {
        var existing = await DirectoryService.ExistingPeople(db).ToListAsync();
        var plan = ImportPlanner.Plan(people, existing, Everything);
        await new ImportService(db).ApplyAsync(
            new ContactSheet([], [], 1, $"{Guid.NewGuid():N}.pdf",
                Guid.NewGuid().ToString("N") + new string('a', 32)),
            plan, Everything, Today);
    }

    private static async Task<Guid> IdOf(AppDbContext db, string last) =>
        (await db.People.FirstAsync(p => p.LastName == last)).Id;

    [Fact]
    public async Task Saving_people_under_a_new_name_makes_the_group_and_the_send_list_sees_it()
    {
        using var db = Open();
        await ImportAsync(db, Person("Ashby", "Miriam"), Person("Quilley", "Barnaby"), Person("Munk", "Delbert"));
        var groups = new GroupService(db);

        var saved = await groups.SaveMembersAsync(" Choir ", [await IdOf(db, "Ashby"), await IdOf(db, "Munk")]);

        Assert.True(saved.Created);
        Assert.Equal(2, saved.Added);
        Assert.Equal(new GroupSummary(saved.Group.Id, "Choir", 2), saved.Group);

        var recipients = await new DirectoryService(db).RecipientsAsync();
        var chosen = Audience.Select(recipients, new AudienceFilter { Group = saved.Group.Id });
        Assert.Equal(["Ashby", "Munk"], chosen.Select(p => p.LastName));
    }

    [Fact]
    public async Task Saving_under_an_existing_name_adds_to_that_group_and_nobody_twice()
    {
        using var db = Open();
        await ImportAsync(db, Person("Ashby", "Miriam"), Person("Quilley", "Barnaby"));
        var groups = new GroupService(db);
        var ashby = await IdOf(db, "Ashby");

        var first = await groups.SaveMembersAsync("Choir", [ashby]);
        var second = await groups.SaveMembersAsync("choir", [ashby, await IdOf(db, "Quilley")]);

        Assert.False(second.Created);
        Assert.Equal(first.Group.Id, second.Group.Id);
        Assert.Equal(1, second.Added);
        Assert.Equal(2, second.Group.Members);
        Assert.Single(await groups.ListAsync());
    }

    [Fact]
    public async Task The_person_editor_s_ticks_become_exactly_that_person_s_groups()
    {
        using var db = Open();
        await ImportAsync(db, Person("Ashby", "Miriam"));
        var groups = new GroupService(db);
        var ashby = await IdOf(db, "Ashby");
        var choir = (await groups.SaveMembersAsync("Choir", [ashby])).Group.Id;
        var reps = (await groups.SaveMembersAsync("Ward reps", [])).Group.Id;

        await groups.SetGroupsForPersonAsync(ashby, new HashSet<Guid> { reps });

        var miriam = (await new DirectoryService(db).RecipientsAsync()).Single();
        Assert.Equal(new HashSet<Guid> { reps }, miriam.Groups);
        Assert.DoesNotContain(choir, miriam.Groups);
    }

    [Fact]
    public async Task A_rename_is_refused_when_blank_or_already_taken()
    {
        using var db = Open();
        var groups = new GroupService(db);
        var choir = (await groups.SaveMembersAsync("Choir", [])).Group.Id;
        await groups.SaveMembersAsync("Ward reps", []);

        Assert.Equal(GroupNameProblem.Blank, await groups.RenameAsync(choir, "  "));
        Assert.Equal(GroupNameProblem.Taken, await groups.RenameAsync(choir, "WARD REPS"));
        Assert.Equal(GroupNameProblem.None, await groups.RenameAsync(choir, "Stake choir"));
        Assert.Equal(["Stake choir", "Ward reps"], (await groups.ListAsync()).Select(g => g.Name));
    }

    [Fact]
    public async Task Deleting_a_group_keeps_its_people_and_the_history_of_what_was_sent()
    {
        using var db = Open();
        await ImportAsync(db, Person("Ashby", "Miriam"));
        var groups = new GroupService(db);
        var choir = (await groups.SaveMembersAsync("Choir", [await IdOf(db, "Ashby")])).Group.Id;

        await groups.DeleteAsync(choir);

        Assert.Empty(await groups.ListAsync());
        Assert.Empty(db.GroupMembers);
        Assert.Empty(Assert.Single(await new DirectoryService(db).RecipientsAsync()).Groups);
    }

    [Fact]
    public async Task Removing_people_from_a_group_leaves_the_rest_in_it()
    {
        using var db = Open();
        await ImportAsync(db, Person("Ashby", "Miriam"), Person("Quilley", "Barnaby"));
        var groups = new GroupService(db);
        var ashby = await IdOf(db, "Ashby");
        var choir = (await groups.SaveMembersAsync("Choir", [ashby, await IdOf(db, "Quilley")])).Group.Id;

        await groups.RemoveMembersAsync(choir, [ashby]);

        Assert.Equal(1, Assert.Single(await groups.ListAsync()).Members);
    }

    [Fact]
    public async Task Falling_out_of_the_export_keeps_someone_in_their_groups_for_when_they_return()
    {
        using var db = Open();
        await ImportAsync(db, Person("Ashby", "Miriam"), Person("Quilley", "Barnaby"));
        var groups = new GroupService(db);
        var choir = (await groups.SaveMembersAsync("Choir", [await IdOf(db, "Ashby"), await IdOf(db, "Quilley")])).Group.Id;

        await ImportAsync(db, Person("Quilley", "Barnaby"));
        Assert.Equal(1, (await groups.ListAsync()).Single().Members);

        await ImportAsync(db, Person("Quilley", "Barnaby"), Person("Ashby", "Miriam"));
        Assert.Equal(2, (await groups.ListAsync()).Single().Members);
        Assert.Contains(choir, (await new DirectoryService(db).RecipientsAsync()).Single(p => p.LastName == "Ashby").Groups);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { /* a temp file left behind harms nothing */ }
    }
}
