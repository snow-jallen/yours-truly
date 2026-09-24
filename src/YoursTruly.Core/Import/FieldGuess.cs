namespace YoursTruly.Core.Import;

/// <summary>What a column heading is probably for.
///
/// This is a guess and is treated as one: everything it decides is shown on the import
/// screen and can be changed before anything is written. That is what lets the reader
/// be generous — a heading it has never seen is offered as a group rather than
/// refused, and the user moves it if that is wrong.</summary>
public static class FieldGuess
{
    /// <summary>Phrases that name a field, tried in this order. The specific come
    /// first: "first name" has to beat "name", and "email address" has to beat
    /// "address", or half the columns of an ordinary file land somewhere silly.
    ///
    /// Each phrase is matched as whole words rather than as a substring, because the
    /// short ones are inside longer words that mean nothing like them — "age" is in
    /// "manager" and "village", and a Manager column is not an age.</summary>
    private static readonly (string[] Words, ImportField Field)[] Known =
    [
        (["e", "mail"], ImportField.Email),
        (["email"], ImportField.Email),

        (["first", "name"], ImportField.FirstName),
        (["given", "name"], ImportField.FirstName),
        (["forename"], ImportField.FirstName),

        (["last", "name"], ImportField.LastName),
        (["surname"], ImportField.LastName),
        (["family", "name"], ImportField.LastName),

        (["mobile"], ImportField.Phone),
        (["phone"], ImportField.Phone),
        (["telephone"], ImportField.Phone),
        (["cell"], ImportField.Phone),

        (["birth", "date"], ImportField.Birthday),
        (["birthdate"], ImportField.Birthday),
        (["birthday"], ImportField.Birthday),
        (["date", "of", "birth"], ImportField.Birthday),
        (["dob"], ImportField.Birthday),

        (["name"], ImportField.Name),
        (["member"], ImportField.Name),
        (["person"], ImportField.Name),
        (["contact"], ImportField.Name),

        (["age"], ImportField.Age),

        // Addresses are read like every other column and then thrown away. the app
        // posts nothing, so carrying where people live earns it nothing and costs a
        // directory's worth of home addresses sitting in a file on a desk.
        (["address"], ImportField.Ignore),
        (["street"], ImportField.Ignore),
        (["city"], ImportField.Ignore),
        (["postcode"], ImportField.Ignore),
        (["zip"], ImportField.Ignore),
        (["gender"], ImportField.Ignore),
        (["sex"], ImportField.Ignore),
    ];

    /// <summary>The field this heading names, or null when nothing recognises it.
    /// A null is not a failure — the import screen offers it as a group.</summary>
    public static ImportField? For(string? heading)
    {
        var words = Words(heading);
        if (words.Count == 0) return null;

        foreach (var (phrase, field) in Known)
            if (Contains(words, phrase))
                return field;

        return null;
    }

    /// <summary>What to start the import screen with: the guess, or a group column for
    /// anything unrecognised, since a column a file bothered to print is usually worth
    /// being able to gather people by.</summary>
    public static ImportField Default(string? heading) => For(heading) ?? ImportField.Groups;

    /// <summary>True for a heading naming one of the fields a list of people is built
    /// around. Finding two of these on one line is what says "this is the heading row".</summary>
    public static bool NamesAPerson(string? heading) =>
        For(heading) is ImportField.Name or ImportField.FirstName or ImportField.LastName
                      or ImportField.Email or ImportField.Phone;

    /// <summary>Lower-cased words, with punctuation treated as a space: "Individual
    /// E-mail" and "individual email" are the same heading, and "(1 Jan)" is two
    /// words rather than one unreadable one.</summary>
    private static IReadOnlyList<string> Words(string? heading) =>
        new string((heading ?? "").Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private static bool Contains(IReadOnlyList<string> words, string[] phrase)
    {
        for (var start = 0; start + phrase.Length <= words.Count; start++)
        {
            var matches = true;
            for (var i = 0; i < phrase.Length && matches; i++)
                matches = string.Equals(words[start + i], phrase[i], StringComparison.Ordinal);
            if (matches) return true;
        }
        return false;
    }
}
