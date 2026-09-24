using YoursTruly.Core.Domain;
using YoursTruly.Core.Import;
using YoursTruly.Data;
using YoursTruly.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Tests;

public sealed class DirectoryServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"yourstruly-dir-{Guid.NewGuid():N}.db");
    private static readonly DateOnly Today = new(2026, 9, 16);
    private static readonly ImportMapping Everything = new(
    [
        new(0, "Name", ImportField.Name), new(1, "Email", ImportField.Email),
        new(2, "Phone", ImportField.Phone), new(3, "Age", ImportField.Age),
        new(4, "Birthday", ImportField.Birthday),
    ]);

    private AppDbContext Open()
    {
        var db = AppDatabase.Open(_path);
        db.Database.Migrate();
        return db;
    }

    /// <summary>Imports the way the app does — against whoever is already stored — so
    /// importing twice updates people rather than duplicating them.</summary>
    private static async Task SeedAsync(AppDbContext db, params NormalizedPerson[] people)
    {
        var existing = await DirectoryService.ExistingPeople(db).ToListAsync();
        var plan = ImportPlanner.Plan(people, existing, Everything);
        await new ImportService(db).ApplyAsync(
            new ContactSheet([], [], 1, "seed.pdf", new string('a', 64)), plan, Everything, Today);
    }

    private static NormalizedPerson Person(
        string last, string first, string? email = null, string? phone = null) =>
        new(last, first, $"{last}, {first}", 40, 3, 4, email, phone,
            phone is null ? null : "+1435555" + phone[^4..], false, []);

    [Fact]
    public async Task Reads_the_directory_as_people_a_message_can_be_sent_to()
    {
        using var db = Open();
        await SeedAsync(db, Person("Ashby", "Miriam", "m.ashby@example.com", "555-0111"));

        var recipients = await new DirectoryService(db).RecipientsAsync();

        var miriam = Assert.Single(recipients);
        Assert.Equal("Ashby", miriam.LastName);
        Assert.Equal("m.ashby@example.com", miriam.Email);
        Assert.Equal("+14355550111", miriam.Phone);
        Assert.True(miriam.PreferredChannels.IsEmpty);
    }

    [Fact]
    public async Task A_number_someone_added_is_preferred_over_the_one_the_file_printed()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby", phone: "555-0127"));
        var service = new DirectoryService(db);

        var rulon = (await service.RecipientsAsync()).Single();
        Assert.Equal("+14355550127", rulon.Phone);

        await service.AddContactAsync(rulon.Id, ContactKind.Phone, "(435) 555-0999", "+14355550999", Today);

        // The one entered by hand is marked preferred, so it is the one used.
        var updated = (await service.RecipientsAsync()).Single();
        Assert.Equal("+14355550127", updated.Phone);
    }

    [Fact]
    public async Task Choosing_channels_sticks_and_several_can_be_chosen()
    {
        using var db = Open();
        await SeedAsync(db, Person("Munk", "Delbert", "d@example.com", "555-0170"));
        var service = new DirectoryService(db);
        var delbert = (await service.RecipientsAsync()).Single();

        await service.SetPreferredChannelsAsync(delbert.Id, ChannelSet.Of(Channel.Voice, Channel.Email));

        var saved = (await service.RecipientsAsync()).Single();
        Assert.Equal(ChannelSet.Of(Channel.Email, Channel.Voice), saved.PreferredChannels);
        Assert.Equal(2, saved.Deliveries.Count);
    }

    [Fact]
    public async Task A_set_of_channels_is_stored_by_name_so_it_reads_back_the_same()
    {
        using var db = Open();
        await SeedAsync(db, Person("Munk", "Delbert", "d@example.com", "555-0170"));
        var id = (await new DirectoryService(db).RecipientsAsync()).Single().Id;
        await new DirectoryService(db).SetPreferredChannelsAsync(
            id, ChannelSet.Of(Channel.Email, Channel.Text, Channel.Voice));

        Assert.Equal("email,text,voice", (await db.People.SingleAsync()).PreferredChannels.ToWire());
    }

    [Fact]
    public async Task The_same_address_is_not_added_twice()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby", phone: "555-0127"));
        var service = new DirectoryService(db);
        var barnaby = (await service.RecipientsAsync()).Single();

        await service.AddContactAsync(barnaby.Id, ContactKind.Email, "b@example.com", "b@example.com", Today);
        await service.AddContactAsync(barnaby.Id, ContactKind.Email, "B@EXAMPLE.COM", "b@example.com", Today);

        Assert.Single(await db.ContactPoints.Where(c => c.Kind == ContactKind.Email).ToListAsync());
    }

    // ---- changes since import -------------------------------------------------------

    [Fact]
    public async Task Something_typed_in_by_hand_is_a_change_since_the_import()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby", phone: "555-0127"));
        var service = new DirectoryService(db);
        var barnaby = (await service.RecipientsAsync()).Single();

        await service.AddContactAsync(barnaby.Id, ContactKind.Email, "b@example.com", "b@example.com", Today);

        var change = Assert.Single(await service.CorrectionsAsync());
        Assert.Equal("Quilley, Barnaby", change.PersonName);
        Assert.Equal("Email", change.KindLabel);
        Assert.Equal("b@example.com", change.Value);
    }

    [Fact]
    public async Task Something_the_file_already_carried_is_not_a_change()
    {
        using var db = Open();
        await SeedAsync(db, Person("Ashby", "Miriam", "m.ashby@example.com", "555-0111"));

        Assert.Empty(await new DirectoryService(db).CorrectionsAsync());
    }

    [Fact]
    public async Task Ticking_one_off_takes_it_off_the_list_without_deleting_it()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby", phone: "555-0127"));
        var service = new DirectoryService(db);
        var barnaby = (await service.RecipientsAsync()).Single();
        await service.AddContactAsync(barnaby.Id, ContactKind.Email, "b@example.com", "b@example.com", Today);

        var change = (await service.CorrectionsAsync()).Single();
        await service.MarkCopiedBackAsync(change.ContactPointId, Today);

        Assert.Empty(await service.CorrectionsAsync());
        Assert.Equal("b@example.com",
            (await db.ContactPoints.FirstAsync(c => c.Id == change.ContactPointId)).Value);
    }

    [Fact]
    public async Task A_later_import_carrying_the_same_value_clears_it_by_itself()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby", phone: "555-0127"));
        var service = new DirectoryService(db);
        var barnaby = (await service.RecipientsAsync()).Single();
        await service.AddContactAsync(barnaby.Id, ContactKind.Email, "b@example.com", "b@example.com", Today);
        Assert.Single(await service.CorrectionsAsync());

        // The next file now carries the address somebody typed in by hand.
        await SeedAsync(db, Person("Quilley", "Barnaby", "b@example.com", "555-0127"));

        Assert.Empty(await service.CorrectionsAsync());

        // Marked as seen, not duplicated: there is still one e-mail on this person.
        Assert.Single(await db.ContactPoints.Where(c => c.Kind == ContactKind.Email).ToListAsync());
    }

    [Fact]
    public async Task Everything_typed_about_a_person_no_file_carries_is_a_change()
    {
        using var db = Open();
        var service = new DirectoryService(db);
        await service.AddPersonAsync("Winslade", "Verity", "verity@example.com", "555-0150", null, Today);

        var changes = await service.CorrectionsAsync();
        Assert.Equal(2, changes.Count);
        Assert.All(changes, c => Assert.Equal("Winslade, Verity", c.PersonName));
        Assert.Equal(["Email", "Phone"], changes.Select(c => c.KindLabel).Order());
    }

    // ---- correcting somebody, which no import may undo ------------------------------

    [Fact]
    public async Task Correcting_an_email_records_it_and_starts_using_it()
    {
        using var db = Open();
        await SeedAsync(db, Person("Ashgrove", "Adelaide", "old@example.com", "555-0111"));
        var service = new DirectoryService(db);
        var person = (await service.RecipientsAsync()).Single();

        Assert.Equal(1, await service.UpdateDetailsAsync(person.Id, "new@example.com", null, null, Today));

        Assert.Equal("new@example.com", (await service.RecipientsAsync()).Single().Email);
        Assert.Equal("new@example.com", Assert.Single(await service.CorrectionsAsync()).Value);
    }

    [Fact]
    public async Task A_phone_typed_the_way_people_write_it_is_stored_ready_to_dial()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby"));
        var service = new DirectoryService(db);
        var person = (await service.RecipientsAsync()).Single();

        await service.UpdateDetailsAsync(person.Id, null, "(435) 555-0199", null, Today);

        Assert.Equal("+14355550199", (await service.RecipientsAsync()).Single().Phone);
    }

    [Fact]
    public async Task Saving_the_same_details_twice_adds_nothing_the_second_time()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby", phone: "555-0127"));
        var service = new DirectoryService(db);
        var person = (await service.RecipientsAsync()).Single();

        Assert.Equal(1, await service.UpdateDetailsAsync(person.Id, "b@example.com", null, null, Today));
        Assert.Equal(0, await service.UpdateDetailsAsync(person.Id, "b@example.com", null, null, Today));

        Assert.Single(await db.ContactPoints.Where(c => c.Kind == ContactKind.Email).ToListAsync());
    }

    [Fact]
    public async Task Retyping_what_the_file_already_had_is_not_a_correction()
    {
        using var db = Open();
        await SeedAsync(db, Person("Ashgrove", "Adelaide", "known@example.com", "555-0111"));
        var service = new DirectoryService(db);
        var person = (await service.RecipientsAsync()).Single();

        Assert.Equal(0, await service.UpdateDetailsAsync(person.Id, "known@example.com", null, null, Today));
        Assert.Single(await db.ContactPoints.Where(c => c.Kind == ContactKind.Email).ToListAsync());
    }

    [Fact]
    public async Task A_note_is_for_the_user_alone_and_is_not_a_contact_detail()
    {
        using var db = Open();
        await SeedAsync(db, Person("Quilley", "Barnaby", phone: "555-0127"));
        var service = new DirectoryService(db);
        var person = (await service.RecipientsAsync()).Single();

        Assert.Equal(0, await service.UpdateDetailsAsync(
            person.Id, null, null, "Hard of hearing — call the landline.", Today));

        Assert.Equal("Hard of hearing — call the landline.", (await db.People.FirstAsync()).Notes);

        // A note is nobody else's business, so it is not something to put back anywhere.
        Assert.Empty(await service.CorrectionsAsync());
    }

    [Fact]
    public async Task An_edit_survives_the_next_import()
    {
        using var db = Open();
        await SeedAsync(db, Person("Ashgrove", "Adelaide", "lcr@example.com", "555-0111"));
        var service = new DirectoryService(db);
        var person = (await service.RecipientsAsync()).Single();
        await service.UpdateDetailsAsync(person.Id, "better@example.com", null, null, Today);

        // The same file again, still carrying the old address.
        await SeedAsync(db, Person("Ashgrove", "Adelaide", "lcr@example.com", "555-0111"));

        Assert.Equal("better@example.com", (await service.RecipientsAsync()).Single().Email);
        Assert.Equal("better@example.com", Assert.Single(await service.CorrectionsAsync()).Value);
    }

    // ---- people who are in no imported file -----------------------------------------

    [Fact]
    public async Task Somebody_can_be_added_who_was_never_in_a_file()
    {
        using var db = Open();
        var service = new DirectoryService(db);

        await service.AddPersonAsync("Winslade", "Verity", "verity@example.com",
            "(435) 555-0150", "Met at the activity", Today);

        var person = Assert.Single(await service.RecipientsAsync());
        Assert.Equal("Winslade, Verity", person.SortName);
        Assert.Equal("verity@example.com", person.Email);
        Assert.Equal("+14355550150", person.Phone);
        Assert.Equal("Met at the activity", person.Notes);
    }

    [Fact]
    public async Task An_import_never_decides_a_hand_added_person_has_left()
    {
        using var db = Open();
        var service = new DirectoryService(db);
        await service.AddPersonAsync("Winslade", "Verity", null, "555-0150", null, Today);

        // A file that has never heard of her, twice over.
        await SeedAsync(db, Person("Ashgrove", "Adelaide"));
        await SeedAsync(db, Person("Ashgrove", "Adelaide"));

        var verity = await db.People.FirstAsync(p => p.LastName == "Winslade");
        Assert.True(verity.IsActive);
        Assert.Null(verity.DeactivatedOn);
        Assert.Equal(2, (await service.RecipientsAsync()).Count);
    }

    [Fact]
    public async Task Once_a_file_carries_them_their_absence_from_one_means_something()
    {
        using var db = Open();
        var service = new DirectoryService(db);
        await service.AddPersonAsync("Winslade", "Verity", null, "555-0150", null, Today);

        // The next file includes her, matched on name.
        await SeedAsync(db, Person("Winslade", "Verity", phone: "555-0150"));
        Assert.Equal(PersonSource.Imported, (await db.People.SingleAsync()).Source);

        // So a later file that drops her now means what it says.
        await SeedAsync(db, Person("Ashgrove", "Adelaide"));
        Assert.False((await db.People.FirstAsync(p => p.LastName == "Winslade")).IsActive);
    }

    [Fact]
    public async Task A_person_needs_at_least_a_surname()
    {
        using var db = Open();
        await Assert.ThrowsAsync<ArgumentException>(
            () => new DirectoryService(db).AddPersonAsync("  ", "Verity", null, null, null, Today));
    }

    public void Dispose()
    {
        // Deliberately not ClearAllPools: it is process-wide, and clearing pools while
        // another test class still holds a connection makes unrelated tests fail.
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { /* a temp file left behind harms nothing */ }
    }
}
