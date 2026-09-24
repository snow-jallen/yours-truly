using YoursTruly.Core.Domain;
using YoursTruly.Core.Import;
using YoursTruly.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Data;

/// <summary>One detail somebody typed in that no imported file carries — a row of the
/// "changes since import" list.</summary>
public sealed record Correction(
    Guid ContactPointId, Guid PersonId, string PersonName,
    ContactKind Kind, string Value, DateOnly AddedOn)
{
    public string KindLabel => Kind == ContactKind.Email ? "Email" : "Phone";
}

/// <summary>Reading and editing the directory. The screens talk to this; nothing in
/// the user interface touches a DbContext.</summary>
public sealed class DirectoryService(AppDbContext db)
{
    public async Task<IReadOnlyList<Recipient>> RecipientsAsync(CancellationToken cancellation = default)
    {
        var people = await db.People
            .Include(p => p.ContactPoints)
            .AsNoTracking()
            .ToListAsync(cancellation);

        var groups = (await db.GroupMembers.AsNoTracking()
                .Select(m => new { m.PersonId, m.GroupId })
                .ToListAsync(cancellation))
            .GroupBy(m => m.PersonId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<Guid>)g.Select(m => m.GroupId).ToHashSet());

        return people
            .Select(p => groups.TryGetValue(p.Id, out var ids) ? ToRecipient(p) with { Groups = ids } : ToRecipient(p))
            .ToList();
    }

    /// <summary>The address the app would actually use, preferring one someone typed in
    /// over whatever the last import happened to print.</summary>
    private static Recipient ToRecipient(Person p)
    {
        var email = Best(p, ContactKind.Email)?.Value ?? p.ImportedEmail;
        var phone = Best(p, ContactKind.Phone)?.Normalized;

        return new Recipient(
            p.Id, p.LastName, p.FirstName, p.DisplayName, p.Age,
            p.BirthMonth, p.BirthDay, p.PreferredChannels, email, phone, p.IsActive)
        {
            Notes = p.Notes,
        };
    }

    private static ContactPoint? Best(Person p, ContactKind kind) =>
        p.ContactPoints.Where(c => c.Kind == kind)
            .OrderByDescending(c => c.IsPreferred)
            .ThenByDescending(c => c.AddedOn)
            .FirstOrDefault();

    /// <summary>The app's own field. No import may write it, so it is saved on its own.</summary>
    public async Task SetPreferredChannelsAsync(
        Guid personId, ChannelSet channels, CancellationToken cancellation = default)
    {
        var person = await db.People.FirstAsync(p => p.Id == personId, cancellation);
        person.PreferredChannels = channels;
        person.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellation);
    }

    public async Task AddContactAsync(
        Guid personId, ContactKind kind, string value, string? normalized,
        DateOnly today, bool areaCodeAssumed = false, CancellationToken cancellation = default)
    {
        var person = await db.People.Include(p => p.ContactPoints)
            .FirstAsync(p => p.Id == personId, cancellation);

        var already = person.ContactPoints.FirstOrDefault(c =>
            c.Kind == kind
            && string.Equals(c.Normalized ?? c.Value, normalized ?? value, StringComparison.OrdinalIgnoreCase));
        if (already is not null) return;

        person.ContactPoints.Add(new ContactPoint
        {
            PersonId = person.Id,
            Kind = kind,
            Source = ContactSource.Local,
            Value = value,
            Normalized = normalized,
            AreaCodeAssumed = areaCodeAssumed,
            AddedOn = today,
            IsPreferred = !person.ContactPoints.Any(c => c.Kind == kind && c.IsPreferred),
        });
        person.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellation);
    }

    /// <summary>The stored people as the import planner sees them. One definition, used
    /// by the importer and by anything else that needs it — having two is how a person
    /// added by hand quietly became a person the import was willing to deactivate.</summary>
    public static IQueryable<ExistingPerson> ExistingPeople(AppDbContext db) =>
        db.People.Select(p => new ExistingPerson(
            p.Id, p.LastName, p.FirstName, p.BirthMonth, p.BirthDay,
            p.Age, p.ImportedEmail, p.ImportedPhone, p.IsActive)
        {
            AddedByHand = p.Source == PersonSource.Local,
        });

    /// <summary>Adds somebody who is not in the imported file — a spouse, a visitor,
    /// anyone the list does not carry. They are marked as added by hand, which is what
    /// stops the next import deciding they have left.</summary>
    public async Task<Guid> AddPersonAsync(
        string lastName,
        string firstName,
        string? email,
        string? phone,
        string? notes,
        DateOnly today,
        string defaultAreaCode = Normalizer.DefaultAreaCode,
        CancellationToken cancellation = default)
    {
        var last = (lastName ?? "").Trim();
        var first = (firstName ?? "").Trim();
        if (last.Length == 0) throw new ArgumentException("A person needs a surname.", nameof(lastName));

        var person = new Person
        {
            LastName = last,
            FirstName = first,
            DisplayName = first.Length > 0 ? $"{last}, {first}" : last,
            Source = PersonSource.Local,
            Notes = Clean(notes),
            FirstSeenOn = today,
            LastSeenOn = today,
        };
        db.People.Add(person);

        Record(person, ContactKind.Email, Clean(email), Clean(email)?.ToLowerInvariant(), false, today);
        var (rawPhone, e164, assumed) = Normalizer.ParsePhone(phone, defaultAreaCode);
        Record(person, ContactKind.Phone, rawPhone, e164, assumed, today);

        await db.SaveChangesAsync(cancellation);
        return person.Id;
    }

    /// <summary>Corrects what the app holds for someone.
    ///
    /// An edit is never written over the fields an import owns. It is recorded as a new
    /// contact detail supplied by hand, which is what keeps it through the next import.
    /// Passing a value unchanged does nothing at all, so saving a form twice does not
    /// create two of anything.</summary>
    public async Task<int> UpdateDetailsAsync(
        Guid personId,
        string? email,
        string? phone,
        string? notes,
        DateOnly today,
        string defaultAreaCode = Normalizer.DefaultAreaCode,
        CancellationToken cancellation = default)
    {
        var person = await db.People.Include(p => p.ContactPoints)
            .FirstAsync(p => p.Id == personId, cancellation);

        var added = 0;
        added += Record(person, ContactKind.Email, Clean(email), Clean(email)?.ToLowerInvariant(), false, today);

        var (rawPhone, e164, assumed) = Normalizer.ParsePhone(phone, defaultAreaCode);
        added += Record(person, ContactKind.Phone, rawPhone, e164, assumed, today);

        person.Notes = Clean(notes);
        person.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellation);
        return added;
    }

    /// <summary>Returns 1 when this was a detail the app did not already hold.</summary>
    private static int Record(
        Person person, ContactKind kind, string? value, string? normalized,
        bool areaCodeAssumed, DateOnly today)
    {
        if (value is null) return 0;

        var key = normalized ?? value;
        var existing = person.ContactPoints.FirstOrDefault(c =>
            c.Kind == kind && string.Equals(c.Normalized ?? c.Value, key, StringComparison.OrdinalIgnoreCase));

        // Already the one in use, whether it came from a file or from an earlier edit.
        if (existing is not null && existing.IsPreferred) return 0;

        foreach (var other in person.ContactPoints.Where(c => c.Kind == kind)) other.IsPreferred = false;

        if (existing is not null)
        {
            existing.IsPreferred = true;
            return 0;
        }

        person.ContactPoints.Add(new ContactPoint
        {
            PersonId = person.Id,
            Kind = kind,
            Source = ContactSource.Local,
            Value = value,
            Normalized = normalized,
            AreaCodeAssumed = areaCodeAssumed,
            AddedOn = today,
            IsPreferred = true,
        });
        return 1;
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Everything typed in by hand that no imported file has carried and
    /// nobody has ticked off yet — what has changed here since the last import, and
    /// therefore what the file it came from does not know.</summary>
    public async Task<IReadOnlyList<Correction>> CorrectionsAsync(
        CancellationToken cancellation = default)
    {
        return await db.ContactPoints
            .Where(c => c.Source == ContactSource.Local
                     && c.LastSeenInImportOn == null
                     && c.CopiedBackOn == null)
            .Include(c => c.Person)
            .OrderBy(c => c.Person!.LastName).ThenBy(c => c.Person!.FirstName)
            .AsNoTracking()
            .Select(c => new Correction(
                c.Id, c.PersonId, c.Person!.DisplayName, c.Kind, c.Value, c.AddedOn))
            .ToListAsync(cancellation);
    }

    /// <summary>Ticks one off by hand, for somebody who has put it back into wherever
    /// their list comes from. An import carrying the same value clears it on its own,
    /// so this is only for closing the loop sooner — or at all, when the list is a
    /// spreadsheet nobody is going to re-export.</summary>
    public async Task MarkCopiedBackAsync(
        Guid contactPointId, DateOnly on, CancellationToken cancellation = default)
    {
        var contact = await db.ContactPoints.FirstAsync(c => c.Id == contactPointId, cancellation);
        contact.CopiedBackOn = on;
        await db.SaveChangesAsync(cancellation);
    }

    /// <summary>Whether anybody has ever been imported or added, including people who
    /// have since fallen out of the list. False only for a brand-new directory.</summary>
    public Task<bool> AnyPeopleAsync(CancellationToken cancellation = default) =>
        db.People.AnyAsync(cancellation);

    public Task<int> ActiveCountAsync(CancellationToken cancellation = default) =>
        db.People.CountAsync(p => p.IsActive, cancellation);

    public async Task<DateTimeOffset?> LastImportAtAsync(CancellationToken cancellation = default) =>
        await db.ImportRuns.OrderByDescending(r => r.ImportedAt)
            .Select(r => (DateTimeOffset?)r.ImportedAt).FirstOrDefaultAsync(cancellation);
}
