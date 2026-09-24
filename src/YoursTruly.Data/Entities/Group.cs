namespace YoursTruly.Data.Entities;

/// <summary>A list of people to send to together — "Activities committee", "U12 Blue".
/// Made by hand, or filled from a column of an imported file: a Team column makes a
/// group per team, a Ward column one per ward.
///
/// An import only ever adds to one. Somebody falling out of a file stays a member, so
/// they are back in the group if they reappear, and somebody put in by hand is never
/// taken out by a file that has not heard of them.</summary>
public class Group
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<GroupMember> Members { get; set; } = [];
}

/// <summary>One person in one group. Keyed on the pair, so nobody is in a group twice.</summary>
public class GroupMember
{
    public Guid GroupId { get; set; }
    public Group? Group { get; set; }

    public Guid PersonId { get; set; }
    public Person? Person { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;
}
