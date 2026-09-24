using YoursTruly.Core.Domain;

namespace YoursTruly.Data.Entities;

public enum PersonSource
{
    /// <summary>Came from an imported file.</summary>
    Imported = 1,

    /// <summary>Added by hand. Never appears in an imported file, so an import must not
    /// treat their absence from one as having left.</summary>
    Local = 2,
}

public class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // --- Identity as the file printed it --------------------------------------------
    public required string LastName { get; set; }
    public required string FirstName { get; set; }

    /// <summary>The name cell verbatim, e.g. "Ashgrove, Adelaide".</summary>
    public required string DisplayName { get; set; }

    // --- Fields an import owns. Overwritten on every import, but only by a file whose
    //     mapping actually carries them. ------------------------------------------------
    public int? Age { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }

    /// <summary>The e-mail and phone as the last import found them. Contact details
    /// added by hand live in <see cref="ContactPoints"/> and are never written here,
    /// so an import can replace these without touching anything a person typed in.</summary>
    public string? ImportedEmail { get; set; }
    public string? ImportedPhone { get; set; }

    // --- Fields the app owns. An import never touches these. ------------------------
    /// <summary>Every way this person has asked to be reached, and they all happen.
    /// Stored as its wire form, "email,text".</summary>
    public ChannelSet PreferredChannels { get; set; } = ChannelSet.None;

    public string? Notes { get; set; }

    /// <summary>Where this person came from. An import may only deactivate people it
    /// put there itself.</summary>
    public PersonSource Source { get; set; } = PersonSource.Imported;

    // --- Soft delete ----------------------------------------------------------------
    /// <summary>False once someone stops appearing in the imported file. Rows are never
    /// deleted: history, preferences and past deliveries all hang off this person.</summary>
    public bool IsActive { get; set; } = true;
    public DateOnly? DeactivatedOn { get; set; }

    // --- Provenance -----------------------------------------------------------------
    public DateOnly FirstSeenOn { get; set; }
    public DateOnly LastSeenOn { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<ContactPoint> ContactPoints { get; set; } = [];
}
