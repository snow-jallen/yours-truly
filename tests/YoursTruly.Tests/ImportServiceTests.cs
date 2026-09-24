using YoursTruly.Core.Domain;
using YoursTruly.Core.Import;
using YoursTruly.Data;
using YoursTruly.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Tests;

public sealed class ImportServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"yourstruly-{Guid.NewGuid():N}.db");
    private static readonly DateOnly Today = new(2026, 9, 16);
    private static readonly DateOnly Later = new(2026, 10, 14);

    private static ImportMapping Carrying(params ImportField[] fields) =>
        new([.. fields.Select((f, i) => new ColumnMapping(i, f.Label(), f))]);

    private static readonly ImportMapping Everything = Carrying(
        ImportField.Name, ImportField.Email, ImportField.Phone,
        ImportField.Age, ImportField.Birthday, ImportField.Groups);

    /// <summary>A file with nothing in it, for the cases that turn on what the mapping
    /// carries rather than on what the rows say.</summary>
    private static ContactSheet Sheet() => new([], [], 9, "print.pdf", new string('b', 64));

    private AppDbContext Open()
    {
        var db = AppDatabase.Open(_path);
        db.Database.Migrate();
        return db;
    }

    private static NormalizedPerson Person(
        string last, string first, string? email = null, string? phone = null,
        params string[] groups) =>
        new(last, first, $"{last}, {first}", 40, 3, 4,
            email, phone, phone is null ? null : "+1435555" + phone[^4..], false, groups);

    private static async Task<ImportRun> ImportAsync(
        AppDbContext db, IReadOnlyList<NormalizedPerson> people, DateOnly on,
        ImportMapping? mapping = null)
    {
        var used = mapping ?? Everything;
        var existing = await DirectoryService.ExistingPeople(db).ToListAsync();
        var plan = ImportPlanner.Plan(people, existing, used);
        return await new ImportService(db).ApplyAsync(Sheet(), plan, used, on);
    }

    [Fact]
    public async Task First_import_stores_everyone_with_their_contact_details()
    {
        using var db = Open();
        var run = await ImportAsync(db, [
            Person("Ashby", "Miriam", "m.ashby@example.com", "555-0111"),
            Person("Quilley", "Barnaby", phone: "555-0127"),
        ], Today);

        Assert.Equal(2, run.AddedCount);
        Assert.Equal(2, await db.People.CountAsync());

        var miriam = await db.People.Include(p => p.ContactPoints)
            .FirstAsync(p => p.LastName == "Ashby");
        Assert.Equal(2, miriam.ContactPoints.Count);
        Assert.All(miriam.ContactPoints, c => Assert.Equal(ContactSource.Imported, c.Source));
        Assert.All(miriam.ContactPoints, c => Assert.Equal(Today, c.LastSeenInImportOn));
        Assert.Equal(Today, miriam.FirstSeenOn);
    }

    [Fact]
    public async Task Someone_dropped_from_the_file_is_deactivated_not_deleted()
    {
        using var db = Open();
        await ImportAsync(db, [Person("Ashby", "Miriam"), Person("Peacock", "Alvin")], Today);
        var run = await ImportAsync(db, [Person("Ashby", "Miriam")], Later);

        Assert.Equal(1, run.DeactivatedCount);
        Assert.Equal(2, await db.People.CountAsync());

        var alvin = await db.People.FirstAsync(p => p.LastName == "Peacock");
        Assert.False(alvin.IsActive);
        Assert.Equal(Later, alvin.DeactivatedOn);
    }

    [Fact]
    public async Task An_import_never_changes_the_channels_somebody_chose_or_a_note()
    {
        using var db = Open();
        await ImportAsync(db, [Person("Quilley", "Barnaby", phone: "555-0127")], Today);

        var before = await db.People.FirstAsync();
        before.PreferredChannels = ChannelSet.Of(Channel.Voice, Channel.Text);
        before.Notes = "Hard of hearing — call the landline.";
        await db.SaveChangesAsync();

        await ImportAsync(db, [Person("Quilley", "Barnaby", phone: "555-0999")], Later);

        var after = await db.People.FirstAsync();
        Assert.Equal(ChannelSet.Of(Channel.Voice, Channel.Text), after.PreferredChannels);
        Assert.Equal("Hard of hearing — call the landline.", after.Notes);
        Assert.Equal("555-0999", after.ImportedPhone);
    }

    [Fact]
    public async Task A_detail_typed_in_by_hand_survives_the_next_import()
    {
        using var db = Open();
        await ImportAsync(db, [Person("Quilley", "Barnaby", phone: "555-0127")], Today);

        var barnaby = await db.People.Include(p => p.ContactPoints).FirstAsync();
        barnaby.ContactPoints.Add(new ContactPoint
        {
            PersonId = barnaby.Id,
            Kind = ContactKind.Email,
            Source = ContactSource.Local,
            Value = "b.quilley@example.com",
            Normalized = "b.quilley@example.com",
            IsPreferred = true,
            AddedOn = Today,
        });
        await db.SaveChangesAsync();

        // A later file carrying the same address marks it as seen rather than adding
        // a second copy of it.
        await ImportAsync(db, [Person("Quilley", "Barnaby", "b.quilley@example.com", "555-0127")], Later);

        var emails = await db.ContactPoints.Where(c => c.Kind == ContactKind.Email).ToListAsync();
        var email = Assert.Single(emails);
        Assert.Equal(Later, email.LastSeenInImportOn);
        Assert.Equal(ContactSource.Local, email.Source);
    }

    [Fact]
    public async Task Every_change_is_recorded_against_the_import_that_made_it()
    {
        using var db = Open();
        await ImportAsync(db, [Person("Winslade", "Verity", phone: "555-0142")], Today);
        var run = await ImportAsync(db, [Person("Winslade", "Verity", phone: "555-0199")], Later);

        var change = Assert.Single(await db.PersonChanges.Where(c => c.ImportRunId == run.Id).ToListAsync());
        Assert.Equal(ChangeKind.Updated, change.Kind);
        Assert.Equal("Phone", change.Field);
        Assert.Equal("555-0142", change.OldValue);
        Assert.Equal("555-0199", change.NewValue);
    }

    [Fact]
    public async Task Someone_who_comes_back_keeps_the_history_they_had()
    {
        using var db = Open();
        await ImportAsync(db, [Person("Peacock", "Alvin")], Today);
        var id = (await db.People.FirstAsync()).Id;

        await ImportAsync(db, [], Later);
        await ImportAsync(db, [Person("Peacock", "Alvin")], Later.AddDays(30));

        var alvin = await db.People.FirstAsync();
        Assert.Equal(id, alvin.Id);
        Assert.True(alvin.IsActive);
        Assert.Null(alvin.DeactivatedOn);
        Assert.Equal(Today, alvin.FirstSeenOn);
    }

    [Fact]
    public async Task A_file_with_no_phone_column_leaves_the_stored_number_alone()
    {
        using var db = Open();
        await ImportAsync(db, [Person("Quilley", "Barnaby", phone: "555-0127")], Today);

        // A file carrying a name, an e-mail and nothing else. The e-mail changes, so
        // this person is updated and the write runs — which makes this a test of the
        // guard rather than of doing nothing.
        var emailOnly = Carrying(ImportField.Name, ImportField.Email);
        await ImportAsync(db, [Person("Quilley", "Barnaby", email: "b@example.com")], Later, emailOnly);

        var person = await db.People.Include(p => p.ContactPoints).SingleAsync();
        Assert.Equal("b@example.com", person.ImportedEmail);
        Assert.Equal("555-0127", person.ImportedPhone);

        // The phone was not touched, so it was not re-stamped as seen either.
        var phone = Assert.Single(person.ContactPoints, c => c.Kind == ContactKind.Phone);
        Assert.Equal(Today, phone.LastSeenInImportOn);
    }

    [Fact]
    public async Task A_column_of_teams_puts_everybody_into_a_group()
    {
        using var db = Open();
        await ImportAsync(db, [
            Person("Ashgrove", "Adelaide", groups: "U12 Blue"),
            Person("Quilley", "Barnaby", groups: "U12 Blue"),
            Person("Winslade", "Verity", groups: "U14 Red"),
        ], Today);

        var groups = await new GroupService(db).ListAsync();
        Assert.Equal(["U12 Blue", "U14 Red"], groups.Select(g => g.Name));
        Assert.Equal(2, groups.Single(g => g.Name == "U12 Blue").Members);
    }

    [Fact]
    public async Task An_import_adds_people_to_a_group_and_never_takes_one_out()
    {
        using var db = Open();
        await ImportAsync(db, [Person("Ashgrove", "Adelaide", groups: "Committee")], Today);

        // Somebody put in that group by hand, who the file has never heard of.
        var byHand = await new DirectoryService(db).AddPersonAsync(
            "Peacock", "Alvin", "a@example.com", null, null, Today);
        await new GroupService(db).SaveMembersAsync("Committee", [byHand]);

        // The file comes round again without him in it. The group is not the file's
        // to empty, and he stays in it.
        await ImportAsync(db, [Person("Ashgrove", "Adelaide", groups: "Committee")], Later);

        var committee = (await new GroupService(db).ListAsync()).Single();
        Assert.Equal(2, committee.Members);
    }

    public void Dispose()
    {
        // Deliberately not ClearAllPools: it is process-wide, and clearing pools while
        // another test class still holds a connection makes unrelated tests fail.
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { /* a temp file left behind harms nothing */ }
    }
}
