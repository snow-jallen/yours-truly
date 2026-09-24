using System.Collections;

namespace YoursTruly.Core.Domain;

/// <summary>The channels one person has asked to be reached on. Several are allowed
/// and they all happen: somebody who ticks Text and Email gets the message twice, once
/// each way. That is the point — a text is read now and an e-mail is still there
/// tomorrow, and for some people you want both.
///
/// Stored as its wire form, "email,text", so adding a channel later can never renumber
/// what is already on disk. <see cref="Channel.None"/> is not a member of anything: an
/// empty set is how "nobody has decided yet" is said.</summary>
public readonly record struct ChannelSet : IEnumerable<Channel>
{
    private readonly int _bits;

    private ChannelSet(int bits) => _bits = bits;

    /// <summary>Nobody has chosen how to reach this person yet.</summary>
    public static ChannelSet None => default;

    public static ChannelSet Of(params Channel[] channels) =>
        channels.Aggregate(None, (set, c) => set.With(c));

    public static ChannelSet Of(IEnumerable<Channel> channels) =>
        channels.Aggregate(None, (set, c) => set.With(c));

    public bool IsEmpty => _bits == 0;

    public int Count => System.Numerics.BitOperations.PopCount((uint)_bits);

    public bool Has(Channel channel) => channel is not Channel.None && (_bits & Bit(channel)) != 0;

    public ChannelSet With(Channel channel) =>
        channel is Channel.None ? this : new(_bits | Bit(channel));

    public ChannelSet Without(Channel channel) =>
        channel is Channel.None ? this : new(_bits & ~Bit(channel));

    /// <summary>Ticking a channel already ticked unticks it, which is how somebody gets
    /// back to having chosen nothing without a button for it.</summary>
    public ChannelSet Toggle(Channel channel) => Has(channel) ? Without(channel) : With(channel);

    /// <summary>The chosen channels in the order the screens show them, so two people
    /// who chose the same pair are described the same way.</summary>
    public IReadOnlyList<Channel> Ordered =>
        [.. Channels.Deliverable.Where(Has), .. Channels.Other.Where(Has)];

    /// <summary>The first chosen channel, used where one has to stand for the set —
    /// sorting a column, for instance. None when nothing is chosen.</summary>
    public Channel First => Ordered.Count > 0 ? Ordered[0] : Channel.None;

    /// <summary>"Email and text", for a sentence written to the user.</summary>
    public string Label
    {
        get
        {
            var names = Ordered.Select(Name).ToList();
            var joined = names.Count switch
            {
                0 => "no channel chosen",
                1 => names[0],
                _ => $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]}",
            };
            return char.ToUpperInvariant(joined[0]) + joined[1..].ToLowerInvariant();
        }
    }

    private static string Name(Channel channel) => channel switch
    {
        Channel.Email => "Email",
        Channel.Text => "Text",
        Channel.Voice => "Phone call",
        Channel.WhatsApp => "WhatsApp",
        _ => "None",
    };

    /// <summary>"email,text". Empty for a set with nothing in it.</summary>
    public string ToWire() => string.Join(',', Ordered.Select(c => c.ToWire()));

    /// <summary>Reads the stored form. Also accepts a single channel's name, which is
    /// what every row held before a person could choose more than one.</summary>
    public static ChannelSet FromWire(string? wire) =>
        Of((wire ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Channels.FromWire));

    public IEnumerator<Channel> GetEnumerator() => Ordered.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => ToWire();

    private static int Bit(Channel channel) => 1 << (int)channel;
}
