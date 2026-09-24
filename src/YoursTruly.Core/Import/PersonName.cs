namespace YoursTruly.Core.Import;

/// <summary>Telling a person's name from everything else printed near it.
///
/// Names have no syntax. An e-mail address announces itself and a phone number very
/// nearly does; a name is just words, and the only thing separating "Padme Amidala"
/// from "Galactic Senate" is that one of them is a person. So this is mostly about what
/// a name is definitely not.</summary>
public static class PersonName
{
    /// <summary>At most this many words. Past four it is a sentence, not a name.</summary>
    private const int MostWords = 4;

    /// <summary>The name a run <em>begins</em> with, or null when it does not begin with
    /// one.
    ///
    /// Deliberately not "is this run a name". A run very often carries the name and then
    /// something else — "Padme Amidala SW-011" in a directory, "Luke Skywalker Tatooine
    /// Human" in a table row — and asking whether the whole run is a name throws those
    /// away entirely. Taking the leading run of name-shaped words gets the name out of
    /// all of them and still refuses "COMLINK MESSAGE ADDRESS" and "SPECIES=HUMAN",
    /// whose words carry no lower case at all.</summary>
    public static string? LeadingName(string? text)
    {
        var taken = new List<string>();
        foreach (var word in (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var comma = word.EndsWith(',');
            var bare = comma ? word[..^1] : word;
            if (!CouldBelongToAName(bare)) break;

            // A comma after the first word is how a surname-first file writes a name —
            // "Ashgrove, Adelaide" — and it is kept, because it is the thing that later
            // tells the importer which half is the surname. A comma anywhere after that
            // is punctuation ending the name, as in "Verity Winslade, v.winslade@…":
            // the word belongs to the name and the comma does not.
            taken.Add(taken.Count == 0 && comma ? word : bare);
            if (comma && taken.Count > 1) break;
            if (taken.Count == MostWords) break;
        }
        return taken.Count == 0 ? null : string.Join(' ', taken);
    }

    /// <summary>A capital, at least one lower-case letter after it, no digits, and
    /// nothing in it but letters and the punctuation names actually use.
    ///
    /// The lower-case letter is what does most of the work: it rejects an id, a heading
    /// and a KEY=VALUE without knowing anything about any of them.</summary>
    private static bool CouldBelongToAName(string word) =>
        word.Length >= 2
        && char.IsUpper(word[0])
        && word.Any(char.IsLower)
        && !word.Any(char.IsDigit)
        && word.All(c => char.IsLetter(c) || c is '-' or '\'' or '.');
}
