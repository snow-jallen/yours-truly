namespace YoursTruly.Core.Import;

/// <summary>Works out what a file would change, without changing anything.
///
/// A file of people carries no stable identifier, so people are matched on name plus
/// birthday, then on name alone for anyone still unmatched. Where a birthday is printed
/// it is the strongest signal available: it does not change, and it is what tells two
/// people of the same name apart.</summary>
public static class ImportPlanner
{
    public static ImportPlan Plan(
        IReadOnlyList<NormalizedPerson> incoming,
        IReadOnlyList<ExistingPerson> existing,
        ImportMapping mapping,
        bool deactivateMissing = true)
    {
        var unmatched = existing.ToList();
        var added = new List<NormalizedPerson>();
        var updated = new List<PersonUpdate>();
        var reactivated = new List<PersonReturn>();
        var unchanged = 0;

        var stillIncoming = new List<NormalizedPerson>();

        // Pass one: name and birthday together.
        foreach (var person in incoming)
        {
            var match = unmatched.FirstOrDefault(e =>
                SameName(e, person) && e.BirthMonth == person.BirthMonth && e.BirthDay == person.BirthDay);
            if (match is null) { stillIncoming.Add(person); continue; }
            unmatched.Remove(match);
            Record(match, person);
        }

        // Pass two: name alone, for a corrected or newly-filled birthday.
        foreach (var person in stillIncoming)
        {
            var match = unmatched.FirstOrDefault(e => SameName(e, person));
            if (match is null) { added.Add(person); continue; }
            unmatched.Remove(match);
            Record(match, person);
        }

        void Record(ExistingPerson match, NormalizedPerson person)
        {
            var changes = Diff(match, person, mapping);
            if (!match.IsActive) reactivated.Add(new PersonReturn(match, person));
            else if (changes.Count > 0) updated.Add(new PersonUpdate(match, person, changes));
            else unchanged++;
        }

        // Somebody added by hand is not in the file and never will be, so their absence
        // from it is not evidence of anything.
        var deactivated = deactivateMissing
            ? unmatched.Where(e => e.IsActive && !e.AddedByHand).ToList()
            : [];

        var groups = incoming
            .SelectMany(p => p.Groups)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ImportPlan(added, updated, deactivated, reactivated, unchanged, groups);
    }

    private static bool SameName(ExistingPerson e, NormalizedPerson p) =>
        string.Equals(e.LastName, p.LastName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(e.FirstName, p.FirstName, StringComparison.OrdinalIgnoreCase);

    /// <summary>What this file would change about somebody already on record.
    ///
    /// Only the fields being imported are compared. A file with no e-mail column has
    /// not said anything about anyone's e-mail address, and an import from it must not
    /// blank the ones a file that did carry them recorded.</summary>
    private static IReadOnlyList<FieldChange> Diff(
        ExistingPerson e, NormalizedPerson p, ImportMapping mapping)
    {
        var changes = new List<FieldChange>();
        void Compare(ImportField field, string name, string? from, string? to)
        {
            if (!mapping.Carries(field)) return;
            if (!string.Equals(from ?? "", to ?? "", StringComparison.OrdinalIgnoreCase))
                changes.Add(new FieldChange(name, from, to));
        }

        Compare(ImportField.Age, "Age", e.Age?.ToString(), p.Age?.ToString());
        Compare(ImportField.Birthday, "Birthday",
            Birthday(e.BirthMonth, e.BirthDay), Birthday(p.BirthMonth, p.BirthDay));
        Compare(ImportField.Email, "Email", e.Email, p.Email);
        Compare(ImportField.Phone, "Phone", e.Phone, p.PhoneRaw);
        return changes;
    }

    private static string? Birthday(int? month, int? day) =>
        month is null || day is null ? null : $"{day} {Months[month.Value - 1]}";

    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];
}
