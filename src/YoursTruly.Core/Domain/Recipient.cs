namespace YoursTruly.Core.Domain;

/// <summary>Why a message cannot reach someone. The list above the Send button shows
/// these rather than quietly sending to fewer people than it counted.</summary>
public enum UnreachableReason
{
    /// <summary>Nothing is wrong; the message can be delivered.</summary>
    None = 0,

    /// <summary>Nobody has chosen how to contact this person yet.</summary>
    NoChannelChosen = 1,

    /// <summary>A channel is chosen, but there is no address or number to use with it.</summary>
    MissingAddress = 2,

    /// <summary>A channel is chosen that the app cannot deliver on yet.</summary>
    ChannelNotSupported = 3,

    /// <summary>The person has fallen out of the imported list. The address may still
    /// work, which is exactly why this has to be said out loud rather than assumed.</summary>
    NotInDirectory = 4,
}

/// <summary>Whether a message would reach one person on one channel, and at which
/// address — and when it would not, why. A bool would lose the reason, and the reason
/// is the only part the user can act on.</summary>
public sealed record Reachability(Channel Channel, string? Address, UnreachableReason Reason)
{
    public bool CanReceive => Reason is UnreachableReason.None;
}

/// <summary>One person as the send list shows them and as the sender consumes them.
/// Deliberately the same record for both: the count above the Send button and the
/// addresses actually used come from one calculation, so the screen cannot promise
/// something the send does not keep.</summary>
public sealed record Recipient(
    Guid Id,
    string LastName,
    string FirstName,
    string DisplayName,
    int? Age,
    int? BirthMonth,
    int? BirthDay,
    ChannelSet PreferredChannels,
    string? Email,
    string? Phone,
    bool IsActive)
{
    /// <summary>Whatever the user has written about this person. Theirs alone: never
    /// sent anywhere.</summary>
    public string? Notes { get; init; }

    /// <summary>The groups this person is in — ticked by hand, or filled from a column
    /// of an imported file.</summary>
    public IReadOnlySet<Guid> Groups { get; init; } = NoGroups;

    private static readonly IReadOnlySet<Guid> NoGroups = new HashSet<Guid>();

    /// <summary>"Ashgrove, Adelaide", as the list is ordered.</summary>
    public string SortName => $"{LastName}, {FirstName}";

    /// <summary>The phone as people read it, rather than as a service dials it.</summary>
    public string PhoneForDisplay => PhoneFormat.ForDisplay(Phone);

    public bool HasNote => !string.IsNullOrWhiteSpace(Notes);

    /// <summary>"Adelaide Ashgrove", for sentences written to the user.</summary>
    public string FullName =>
        string.IsNullOrWhiteSpace(FirstName) ? LastName.Trim() : $"{FirstName.Trim()} {LastName.Trim()}";

    /// <summary>The address this channel would use, or null when there is none.
    /// WhatsApp answers with the phone number even though the app cannot send on it
    /// yet: whether a channel can be delivered is <see cref="Reachability"/>'s
    /// question, not this one's.</summary>
    public string? AddressFor(Channel channel) => channel switch
    {
        Channel.Email => Blank(Email),
        Channel.Text or Channel.Voice or Channel.WhatsApp => Blank(Phone),
        _ => null,
    };

    /// <summary>What sending to this person would do, one answer per channel they
    /// chose — because they all happen. Somebody who ticked Text and Email is two
    /// deliveries, and a set with nothing in it is one answer saying so.</summary>
    public IReadOnlyList<Reachability> Reachabilities
    {
        get
        {
            if (!IsActive)
                return [new Reachability(PreferredChannels.First, null, UnreachableReason.NotInDirectory)];
            if (PreferredChannels.IsEmpty)
                return [new Reachability(Channel.None, null, UnreachableReason.NoChannelChosen)];
            return [.. PreferredChannels.Ordered.Select(ReachabilityVia)];
        }
    }

    /// <summary>The channels that would actually carry a message to this person.</summary>
    public IReadOnlyList<Reachability> Deliveries =>
        [.. Reachabilities.Where(r => r.CanReceive)];

    /// <summary>One answer standing for the whole set: the first channel that works, or
    /// — when none does — the first reason it does not. What a single line of the
    /// screen shows and what the unreachable list explains.</summary>
    public Reachability Reachability =>
        Reachabilities.FirstOrDefault(r => r.CanReceive) ?? Reachabilities[0];

    /// <summary>The same question for one channel, whether or not this person chose it —
    /// used when a send overrides everyone's preference, for instance because something
    /// is urgent enough to text the people who would normally be e-mailed.
    ///
    /// Being out of the directory is checked first: the address may be perfectly good,
    /// and that is the case most easily sent to by mistake.</summary>
    public Reachability ReachabilityVia(Channel channel)
    {
        if (!IsActive)
            return new Reachability(channel, null, UnreachableReason.NotInDirectory);
        if (channel is Channel.None)
            return new Reachability(Channel.None, null, UnreachableReason.NoChannelChosen);
        if (!Channels.Deliverable.Contains(channel))
            return new Reachability(channel, null, UnreachableReason.ChannelNotSupported);

        var address = AddressFor(channel);
        return address is null
            ? new Reachability(channel, null, UnreachableReason.MissingAddress)
            : new Reachability(channel, address, UnreachableReason.None);
    }

    public bool CanReceive => Reachabilities.Any(r => r.CanReceive);

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
