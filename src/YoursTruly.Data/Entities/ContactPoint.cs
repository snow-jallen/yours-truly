namespace YoursTruly.Data.Entities;

public enum ContactKind
{
    Email = 1,
    Phone = 2,
}

public enum ContactSource
{
    /// <summary>Came from an imported file.</summary>
    Imported = 1,

    /// <summary>Someone typed it into the app. These survive every import, because a
    /// correction made by hand is better information than the file it corrects.</summary>
    Local = 2,
}

/// <summary>One way of reaching a person. A person can have several: the number the
/// file printed, plus a mobile they gave you at an activity.</summary>
public class ContactPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PersonId { get; set; }
    public Person? Person { get; set; }

    public ContactKind Kind { get; set; }
    public ContactSource Source { get; set; }

    /// <summary>As written by whoever supplied it.</summary>
    public required string Value { get; set; }

    /// <summary>E.164 for a phone, lower-cased for an e-mail. What sending and
    /// duplicate-checking use.</summary>
    public string? Normalized { get; set; }

    /// <summary>True when the area code had to be assumed because the file printed
    /// only seven digits. The app asks before using one of these.</summary>
    public bool AreaCodeAssumed { get; set; }

    /// <summary>"mobile", "home", "work" — free text, optional.</summary>
    public string? Label { get; set; }

    /// <summary>The one the app uses when sending on this channel.</summary>
    public bool IsPreferred { get; set; }

    public DateOnly AddedOn { get; set; }

    /// <summary>Last import that still listed this value. Null for anything added by
    /// hand that no file has ever carried.</summary>
    public DateOnly? LastSeenInImportOn { get; set; }

    /// <summary>Ticked off once somebody has copied this back into wherever the list
    /// comes from. Clears it from the "changes since import" list without deleting
    /// anything, for the cases where a later import will never carry it.</summary>
    public DateOnly? CopiedBackOn { get; set; }

    /// <summary>Outstanding work for the "changes since import" list: typed in by hand,
    /// never seen in an imported file, not yet ticked off.</summary>
    public bool NeedsCopyingBack =>
        Source == ContactSource.Local && LastSeenInImportOn is null && CopiedBackOn is null;
}
