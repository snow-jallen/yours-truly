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
}
