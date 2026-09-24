using YoursTruly.Core.Domain;
using YoursTruly.Core.Import;
using YoursTruly.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Data;

/// <summary>Reads a file, works out what it would change, and — only when asked —
/// applies it in one transaction.</summary>
public sealed class ImportService(AppDbContext db)
{
    /// <summary>Opens a file and reads it. What its columns mean is not decided here:
    /// the import screen shows the app's guess and the user has the last word.</summary>
    public static ContactSheet Read(string path) => ContactSheetReader.Read(path);

    public async Task<ImportPlan> PlanAsync(
        ContactSheet sheet,
        ImportMapping mapping,
        bool deactivateMissing = true,
        string defaultAreaCode = Normalizer.DefaultAreaCode,
        CancellationToken cancellation = default)
    {
        var incoming = Normalizer.Normalize(sheet, mapping, defaultAreaCode);
        var existing = await DirectoryService.ExistingPeople(db).ToListAsync(cancellation);
        return ImportPlanner.Plan(incoming, existing, mapping, deactivateMissing) with
        {
            Unnamed = Normalizer.Unnamed(sheet, mapping, defaultAreaCode),
        };
    }

    /// <summary>Writes the plan. Everything lands or nothing does.</summary>
    public async Task<ImportRun> ApplyAsync(
        ContactSheet sheet, ImportPlan plan, ImportMapping mapping, DateOnly today,
        CancellationToken cancellation = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);

        var run = new ImportRun
        {
            FileName = sheet.FileName,
            Sha256 = sheet.Sha256,
            PageCount = sheet.PageCount,
            RowCount = sheet.Rows.Count,
            AddedCount = plan.Added.Count,
            UpdatedCount = plan.Updated.Count,
            DeactivatedCount = plan.Deactivated.Count,
            ReactivatedCount = plan.Reactivated.Count,
            UnchangedCount = plan.Unchanged,
        };
        db.ImportRuns.Add(run);

        // Group memberships the file asks for, collected as people are written and put
        // in at the end — a person added in this same transaction has no row to join to
        // until SaveChanges, and a group named by a hundred rows should be looked up once.
        var joining = new Dictionary<string, List<Guid>>(StringComparer.OrdinalIgnoreCase);
        void Join(Guid personId, NormalizedPerson incoming)
        {
            foreach (var name in incoming.Groups)
            {
                if (!joining.TryGetValue(name, out var members)) joining[name] = members = [];
                members.Add(personId);
            }
        }

        foreach (var incoming in plan.Added)
        {
            var person = new Person
            {
                LastName = incoming.LastName,
                FirstName = incoming.FirstName,
                DisplayName = incoming.DisplayName,
                FirstSeenOn = today,
                LastSeenOn = today,
            };
            ApplyFields(person, incoming, mapping, today);
            db.People.Add(person);
            run.Changes.Add(new PersonChange { PersonId = person.Id, Kind = ChangeKind.Added });
            await SyncContactAsync(person, incoming, mapping, today, cancellation);
            Join(person.Id, incoming);
        }

        foreach (var update in plan.Updated)
        {
            var person = await LoadAsync(update.Existing.Id, cancellation);
            ApplyFields(person, update.Incoming, mapping, today);
            person.LastSeenOn = today;
            foreach (var change in update.Changes)
                run.Changes.Add(new PersonChange
                {
                    PersonId = person.Id,
                    Kind = ChangeKind.Updated,
                    Field = change.Field,
                    OldValue = change.From,
                    NewValue = change.To,
                });
            await SyncContactAsync(person, update.Incoming, mapping, today, cancellation);
            Join(person.Id, update.Incoming);
        }

        foreach (var returning in plan.Reactivated)
        {
            var person = await LoadAsync(returning.Existing.Id, cancellation);
            person.IsActive = true;
            person.DeactivatedOn = null;
            person.LastSeenOn = today;
            ApplyFields(person, returning.Incoming, mapping, today);
            run.Changes.Add(new PersonChange { PersonId = person.Id, Kind = ChangeKind.Reactivated });
            await SyncContactAsync(person, returning.Incoming, mapping, today, cancellation);
            Join(person.Id, returning.Incoming);
        }

        foreach (var gone in plan.Deactivated)
        {
            var person = await LoadAsync(gone.Id, cancellation);
            person.IsActive = false;
            person.DeactivatedOn = today;
            person.UpdatedAt = DateTimeOffset.UtcNow;
            run.Changes.Add(new PersonChange { PersonId = person.Id, Kind = ChangeKind.Deactivated });
        }

        await db.SaveChangesAsync(cancellation);

        // An import only ever adds someone to a group. Taking anybody out is the one
        // thing it must not do: the groups are full of people put there by hand, and a
        // file that happens not to mention them is not an instruction to remove them.
        var groups = new GroupService(db);
        foreach (var (name, members) in joining)
            await groups.SaveMembersAsync(name, members, cancellation);

        await transaction.CommitAsync(cancellation);
        return run;
    }

    private Task<Person> LoadAsync(Guid id, CancellationToken cancellation) =>
        db.People.Include(p => p.ContactPoints).FirstAsync(p => p.Id == id, cancellation);

    /// <summary>Copies the fields this file is being imported for. Chosen channels and
    /// notes are untouched by design, and so is any field the mapping does not carry:
    /// the file has said nothing about them, and silence is not an instruction to blank
    /// them.</summary>
    private static void ApplyFields(
        Person person, NormalizedPerson incoming, ImportMapping mapping, DateOnly today)
    {
        person.DisplayName = incoming.DisplayName;
        if (mapping.Carries(ImportField.Age)) person.Age = incoming.Age;
        if (mapping.Carries(ImportField.Birthday))
        {
            person.BirthMonth = incoming.BirthMonth;
            person.BirthDay = incoming.BirthDay;
        }
        if (mapping.Carries(ImportField.Email)) person.ImportedEmail = incoming.Email;
        if (mapping.Carries(ImportField.Phone)) person.ImportedPhone = incoming.PhoneRaw;

        person.LastSeenOn = today;
        person.UpdatedAt = DateTimeOffset.UtcNow;

        // A file carries them now, so their absence from the next one means something.
        person.Source = PersonSource.Imported;
    }

    /// <summary>Keeps the contact points in step with what the file printed. A value
    /// somebody typed in that now appears in a file is marked as seen rather than
    /// duplicated.</summary>
    private async Task SyncContactAsync(
        Person person, NormalizedPerson incoming, ImportMapping mapping, DateOnly today,
        CancellationToken cancellation)
    {
        if (mapping.Carries(ImportField.Email))
            await UpsertAsync(person, ContactKind.Email, incoming.Email, Normalize(incoming.Email),
                false, today, cancellation);
        if (mapping.Carries(ImportField.Phone))
            await UpsertAsync(person, ContactKind.Phone, incoming.PhoneRaw, incoming.PhoneE164,
                incoming.AreaCodeAssumed, today, cancellation);
    }

    private async Task UpsertAsync(
        Person person, ContactKind kind, string? value, string? normalized,
        bool areaCodeAssumed, DateOnly today, CancellationToken cancellation)
    {
        if (value is null) return;

        if (db.Entry(person).State != EntityState.Added)
            await db.Entry(person).Collection(p => p.ContactPoints).LoadAsync(cancellation);

        var existing = person.ContactPoints.FirstOrDefault(c =>
            c.Kind == kind && string.Equals(c.Normalized ?? c.Value, normalized ?? value, StringComparison.OrdinalIgnoreCase));

        if (existing is not null)
        {
            existing.LastSeenInImportOn = today;
            existing.Value = value;
            existing.Normalized = normalized;
            existing.AreaCodeAssumed = areaCodeAssumed;
            return;
        }

        person.ContactPoints.Add(new ContactPoint
        {
            PersonId = person.Id,
            Kind = kind,
            Source = ContactSource.Imported,
            Value = value,
            Normalized = normalized,
            AreaCodeAssumed = areaCodeAssumed,
            AddedOn = today,
            LastSeenInImportOn = today,
            IsPreferred = !person.ContactPoints.Any(c => c.Kind == kind && c.IsPreferred),
        });
    }

    private static string? Normalize(string? email) => email?.Trim().ToLowerInvariant();
}
