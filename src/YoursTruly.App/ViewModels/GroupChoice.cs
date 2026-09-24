using YoursTruly.Data;

namespace YoursTruly.App.ViewModels;

/// <summary>One entry in a group picker. Chosen by id rather than by the words shown,
/// so a group somebody happens to call "Any group" is still a group.</summary>
public sealed record GroupChoice(Guid? Id, string Label)
{
    public const string AnyLabel = "Any group";
    public static readonly GroupChoice Any = new(null, AnyLabel);

    public static IReadOnlyList<GroupChoice> Options(IEnumerable<GroupSummary> groups) =>
        [Any, .. groups.Select(g => new GroupChoice(g.Id, $"{g.Name} ({g.Members})") { Name = g.Name })];

    /// <summary>The group's own name, without the count shown beside it.</summary>
    public string Name { get; init; } = Label;

    /// <summary>The same group in a freshly loaded list, or "any" when it has gone.</summary>
    public static GroupChoice Find(IReadOnlyList<GroupChoice> options, GroupChoice? wanted) =>
        options.FirstOrDefault(o => o.Id == wanted?.Id) ?? Any;

    public override string ToString() => Label;
}
