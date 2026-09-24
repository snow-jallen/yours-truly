using YoursTruly.Core.Domain;
using YoursTruly.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Data;

/// <summary>A group as the lists show it. <see cref="Members"/> counts only people
/// still in the directory, because those are the ones a message would reach.</summary>
public sealed record GroupSummary(Guid Id, string Name, int Members);

/// <summary>Why a name was refused, so the screen can say so in words.</summary>
public enum GroupNameProblem { None, Blank, Taken }

/// <summary>What saving people into a group did: whether the group is new, and how
/// many of the people were not already in it.</summary>
public sealed record GroupSaved(GroupSummary Group, bool Created, int Added);

/// <summary>Making, naming and filling groups. Groups are the app's own, like the note
/// and the preferred channel: an import never touches them.</summary>
public sealed class GroupService(AppDbContext db)
{
    public async Task<IReadOnlyList<GroupSummary>> ListAsync(CancellationToken cancellation = default)
    {
        var groups = await db.Groups
            .AsNoTracking()
            .Select(g => new GroupSummary(g.Id, g.Name, g.Members.Count(m => m.Person!.IsActive)))
            .ToListAsync(cancellation);
        return [.. groups.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Puts these people in the group with this name, making it first if there
    /// is none. Saving the same people twice adds nobody the second time.</summary>
    public async Task<GroupSaved> SaveMembersAsync(
        string name, IEnumerable<Guid> personIds, CancellationToken cancellation = default)
    {
        var clean = GroupName.Clean(name) ?? throw new ArgumentException("A group needs a name.", nameof(name));

        // The column compares without case, so this finds "Choir" when "choir" is typed.
        var group = await db.Groups.Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Name == clean, cancellation);
        var created = group is null;
        if (group is null)
        {
            group = new Group { Name = clean };
            db.Groups.Add(group);
        }

        var already = group.Members.Select(m => m.PersonId).ToHashSet();
        var added = 0;
        foreach (var id in personIds.Distinct().Where(id => !already.Contains(id)))
        {
            group.Members.Add(new GroupMember { GroupId = group.Id, PersonId = id });
            added++;
        }

        await db.SaveChangesAsync(cancellation);
        var summary = (await ListAsync(cancellation)).Single(g => g.Id == group.Id);
        return new GroupSaved(summary, created, added);
    }

    /// <summary>Adds these people to a group; anyone already in it is left alone.
    /// Returns how many were new to it.</summary>
    public async Task<int> AddMembersAsync(
        Guid groupId, IEnumerable<Guid> personIds, CancellationToken cancellation = default)
    {
        var already = (await db.GroupMembers.Where(m => m.GroupId == groupId)
            .Select(m => m.PersonId).ToListAsync(cancellation)).ToHashSet();

        var added = 0;
        foreach (var id in personIds.Distinct().Where(id => !already.Contains(id)))
        {
            db.GroupMembers.Add(new GroupMember { GroupId = groupId, PersonId = id });
            added++;
        }

        await db.SaveChangesAsync(cancellation);
        return added;
    }

    public async Task RemoveMembersAsync(
        Guid groupId, IEnumerable<Guid> personIds, CancellationToken cancellation = default)
    {
        var ids = personIds.ToHashSet();
        await db.GroupMembers
            .Where(m => m.GroupId == groupId && ids.Contains(m.PersonId))
            .ExecuteDeleteAsync(cancellation);
    }

    /// <summary>Makes this person a member of exactly these groups — what the person
    /// editor's ticks say.</summary>
    public async Task SetGroupsForPersonAsync(
        Guid personId, IReadOnlySet<Guid> groupIds, CancellationToken cancellation = default)
    {
        var current = await db.GroupMembers.Where(m => m.PersonId == personId).ToListAsync(cancellation);

        db.GroupMembers.RemoveRange(current.Where(m => !groupIds.Contains(m.GroupId)));

        var have = current.Select(m => m.GroupId).ToHashSet();
        var exists = await db.Groups.Where(g => groupIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(cancellation);
        foreach (var id in exists.Where(id => !have.Contains(id)))
            db.GroupMembers.Add(new GroupMember { GroupId = id, PersonId = personId });

        await db.SaveChangesAsync(cancellation);
    }

    public async Task<GroupNameProblem> RenameAsync(
        Guid groupId, string name, CancellationToken cancellation = default)
    {
        var clean = GroupName.Clean(name);
        if (clean is null) return GroupNameProblem.Blank;
        if (await db.Groups.AnyAsync(g => g.Id != groupId && g.Name == clean, cancellation))
            return GroupNameProblem.Taken;

        var group = await db.Groups.FirstAsync(g => g.Id == groupId, cancellation);
        group.Name = clean;
        await db.SaveChangesAsync(cancellation);
        return GroupNameProblem.None;
    }

    /// <summary>Removes the group and nothing else: the people in it stay, and messages
    /// already sent to it keep the name they were sent under.</summary>
    public async Task DeleteAsync(Guid groupId, CancellationToken cancellation = default)
    {
        var group = await db.Groups.FirstOrDefaultAsync(g => g.Id == groupId, cancellation);
        if (group is null) return;
        db.Groups.Remove(group);
        await db.SaveChangesAsync(cancellation);
    }
}
