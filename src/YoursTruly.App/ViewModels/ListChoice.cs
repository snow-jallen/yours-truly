using YoursTruly.Messaging.Settings;

namespace YoursTruly.App.ViewModels;

/// <summary>One entry in the list switcher on the rail. A record so the combo box can
/// compare two of them by value, and <see cref="ToString"/> so it can show the name
/// without a template.</summary>
public sealed record ListChoice(string Name, string Path)
{
    public override string ToString() => Name;

    public static ListChoice Of(SavedList list) => new(list.Name, list.Path);

    /// <summary>The saved lists, with the open one included whether it was saved or
    /// not — the switcher has to be able to show where you are. Null means no list is
    /// open, which is the ordinary state before the first import.</summary>
    public static IReadOnlyList<ListChoice> Options(AppSettings settings, string? openPath)
    {
        var options = settings.Lists.Select(Of).ToList();
        if (openPath is not null && !options.Any(o => Same(o.Path, openPath)))
            options.Insert(0, new ListChoice(SavedList.NameFor(openPath), openPath));
        return options;
    }

    public static ListChoice? Find(IReadOnlyList<ListChoice> options, string? path) =>
        path is null ? null : options.FirstOrDefault(o => Same(o.Path, path));

    /// <summary>Paths are compared exactly. Two spellings of the same file would show
    /// as two lists, which is a smaller problem than two files treated as one.</summary>
    public static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
}
