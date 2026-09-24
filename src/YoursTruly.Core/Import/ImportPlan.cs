namespace YoursTruly.Core.Import;

/// <summary>A person already in the database, reduced to the fields an import can
/// touch. Chosen channels, notes and locally-added contact details are deliberately
/// absent: an import must never be able to change them.</summary>
public sealed record ExistingPerson(
    Guid Id,
    string LastName,
    string FirstName,
    int? BirthMonth,
    int? BirthDay,
    int? Age,
    string? Email,
    string? Phone,
    bool IsActive)
{
    /// <summary>True for somebody added by hand, who will never be in an imported file.
    /// Their absence from one says nothing, so an import must leave them alone.</summary>
    public bool AddedByHand { get; init; }
}

public sealed record FieldChange(string Field, string? From, string? To);

public sealed record PersonUpdate(
    ExistingPerson Existing, NormalizedPerson Incoming, IReadOnlyList<FieldChange> Changes);

public sealed record PersonReturn(ExistingPerson Existing, NormalizedPerson Incoming);

/// <summary>What an import would do, worked out before anything is written.</summary>
public sealed record ImportPlan(
    IReadOnlyList<NormalizedPerson> Added,
    IReadOnlyList<PersonUpdate> Updated,
    IReadOnlyList<ExistingPerson> Deactivated,
    IReadOnlyList<PersonReturn> Reactivated,
    int Unchanged,
    IReadOnlyList<string> Groups)
{
    public int TotalInFile => Added.Count + Updated.Count + Reactivated.Count + Unchanged;

    /// <summary>People the file can reach but could not name, who are therefore not in
    /// any of the lists above. Almost always a sign the file was read wrongly rather
    /// than a sign about the file — see <see cref="Normalizer.Unnamed"/>.</summary>
    public int Unnamed { get; init; }
}
