namespace YoursTruly.Core.Import;

/// <summary>What one column of a file is. The user says so on the import screen;
/// The app only guesses the first answer.</summary>
public enum ImportField
{
    /// <summary>Read and thrown away. Addresses land here, and so does anything else
    /// The app has no use for.</summary>
    Ignore = 0,

    /// <summary>The whole name in one column, either "Ashgrove, Adelaide" or
    /// "Adelaide Ashgrove" — which of the two is worked out from the file.</summary>
    Name = 1,

    FirstName = 2,
    LastName = 3,
    Email = 4,
    Phone = 5,
    Age = 6,
    Birthday = 7,

    /// <summary>Each distinct value in the column becomes a group, and everyone with
    /// that value joins it. A "Team" column makes a group per team; a "Class" column,
    /// one per class.</summary>
    Groups = 8,
}

public static class ImportFields
{
    /// <summary>Every answer the import screen offers, in the order it offers them.</summary>
    public static readonly IReadOnlyList<ImportField> All =
    [
        ImportField.Ignore, ImportField.Name, ImportField.FirstName, ImportField.LastName,
        ImportField.Email, ImportField.Phone, ImportField.Age, ImportField.Birthday,
        ImportField.Groups,
    ];

    public static string Label(this ImportField field) => field switch
    {
        ImportField.Name => "Name",
        ImportField.FirstName => "First name",
        ImportField.LastName => "Surname",
        ImportField.Email => "Email",
        ImportField.Phone => "Phone",
        ImportField.Age => "Age",
        ImportField.Birthday => "Birthday",
        ImportField.Groups => "Groups",
        _ => "Don't import",
    };

    /// <summary>A field no two columns may both claim. Two columns of groups is a
    /// sensible thing to ask for — team and year, say — and two of e-mail is not.</summary>
    public static bool OnlyOnce(this ImportField field) =>
        field is not (ImportField.Ignore or ImportField.Groups);
}
