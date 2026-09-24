namespace YoursTruly.Core.Domain;

/// <summary>A way of reaching somebody. Stored as a string in the database so adding a
/// channel later never renumbers the existing rows. A person can choose more than one;
/// see <see cref="ChannelSet"/>.</summary>
public enum Channel
{
    /// <summary>No channel. Only ever means "none of them" — a person's choice is a
    /// <see cref="ChannelSet"/>, and an empty one is how "not decided yet" is said.</summary>
    None = 0,
    Email = 1,
    Text = 2,
    Voice = 3,
    WhatsApp = 4,
}

public static class Channels
{
    /// <summary>Channels the app can actually deliver on today, in the order the UI shows them.</summary>
    public static readonly IReadOnlyList<Channel> Deliverable = [Channel.Email, Channel.Text, Channel.Voice];

    /// <summary>Channels a row may hold that nothing can send on yet. Kept so a set
    /// read off disk comes back whole rather than quietly losing a member.</summary>
    public static readonly IReadOnlyList<Channel> Other = [Channel.WhatsApp];

    public static string ToWire(this Channel c) => c switch
    {
        Channel.Email => "email",
        Channel.Text => "text",
        Channel.Voice => "voice",
        Channel.WhatsApp => "whatsapp",
        _ => "none",
    };

    public static Channel FromWire(string? s) => s switch
    {
        "email" => Channel.Email,
        "text" => Channel.Text,
        "voice" => Channel.Voice,
        "whatsapp" => Channel.WhatsApp,
        _ => Channel.None,
    };
}
