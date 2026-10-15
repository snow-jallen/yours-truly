namespace YoursTruly.Core.Domain;

/// <summary>What a group may be called. One rule, used by every screen that names one,
/// so "Choir" typed on the Send screen and "choir " typed on People are the same group
/// rather than two that look alike in a list.</summary>
public static class GroupName
{
    public const int MaxLength = 60;

    /// <summary>The name as it will be stored, or null when there is nothing to store.
    /// Inner runs of spaces collapse, because nobody means two spaces.</summary>
    public static string? Clean(string? name)
    {
        var words = (name ?? "").Split((char[])[' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        var joined = string.Join(' ', words);
        return joined.Length <= MaxLength ? joined : joined[..MaxLength].TrimEnd();
    }

    public static bool Same(string? a, string? b) =>
        string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The groups somebody is in, as one line for under their name:
    /// "Choir · Youth leaders". Alphabetical, so the same person reads the same way
    /// every time; a group that has gone since the person was loaded is left out
    /// rather than shown as an id. Empty when they are in none.</summary>
    public static string Line(IEnumerable<Guid> memberOf, IReadOnlyDictionary<Guid, string> names) =>
        string.Join(" · ", memberOf
            .Select(id => names.TryGetValue(id, out var name) ? name : null)
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase));

    public const string AnyGroup = "Any group";

    /// <summary>What a picker of several groups says on its face: "Any group" when none
    /// is ticked, the name when one is, both names when two fit, and otherwise how many
    /// — "3 groups" — rather than a label cut off part way through a name.</summary>
    public static string Summary(IReadOnlyList<string> ticked, int room = 28)
    {
        if (ticked.Count == 0) return AnyGroup;
        if (ticked.Count == 1) return ticked[0];

        var both = $"{ticked[0]}, {ticked[1]}";
        return ticked.Count == 2 && both.Length <= room ? both : $"{ticked.Count} groups";
    }

    /// <summary>The ticked groups as words for the record of who a message went to:
    /// "Choir", "Choir or Youth leaders", "Band, Choir or Youth leaders". "Or" because
    /// that is what ticking several means — anyone in any of them. Every name is kept;
    /// this is a record, not a label, and "3 groups" would not say which.</summary>
    public static string Describe(IReadOnlyList<string> ticked) => ticked.Count switch
    {
        0 => "",
        1 => ticked[0],
        _ => $"{string.Join(", ", ticked.Take(ticked.Count - 1))} or {ticked[^1]}",
    };
}
