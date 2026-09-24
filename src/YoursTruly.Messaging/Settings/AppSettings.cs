using System.Text.Json.Serialization;

namespace YoursTruly.Messaging.Settings;

public sealed record EmailSettings
{
    public string Address { get; init; } = "";

    /// <summary>Not the account password — the 16-character one Google issues per app.</summary>
    public string AppPassword { get; init; } = "";

    public string DisplayName { get; init; } = "";
    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;

    public bool IsComplete => Address.Length > 0 && AppPassword.Length > 0 && Host.Length > 0;
}

public sealed record TwilioSettings
{
    public string AccountSid { get; init; } = "";
    public string AuthToken { get; init; } = "";

    /// <summary>The Twilio number texts and calls come from, in E.164.</summary>
    public string FromNumber { get; init; } = "";

    /// <summary>Where the Test buttons send to — the user's own phone, in E.164.</summary>
    public string TestNumber { get; init; } = "";

    /// <summary>A Twilio Messaging Service with an RCS sender attached, if there is one.
    ///
    /// Sending through the service rather than the bare number is what turns a plain
    /// text into RCS — a named, verified sender with real formatting — for the people
    /// whose phones support it. Twilio falls back to SMS from the same request for
    /// everyone else, so nobody receives less than they do today. Empty means texts go
    /// out from <see cref="FromNumber"/> as ordinary SMS.</summary>
    public string MessagingServiceSid { get; init; } = "";

    public bool SendsRichText => MessagingServiceSid.Trim().Length > 0;

    public bool IsComplete =>
        AccountSid.Length > 0 && AuthToken.Length > 0
        && (FromNumber.Length > 0 || MessagingServiceSid.Length > 0);
}

/// <summary>Where texts actually leave from.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TextTransport>))]
public enum TextTransport
{
    /// <summary>A messaging service. Reaches everybody, costs money, and arrives from a
    /// number nobody recognises.</summary>
    Twilio = 1,

    /// <summary>The Messages app on this Mac, which sends over the user's own line —
    /// their real number, replies in their own Messages app. Meant for a team or a
    /// committee rather than a whole directory. iPhone owners with a Mac.</summary>
    MacMessages = 2,

    /// <summary>A gateway app on the user's own Android phone, which the app asks over
    /// the local network. Same idea as the Mac route and the same limits, but it works
    /// from any computer and needs no Mac.</summary>
    AndroidGateway = 3,
}

/// <summary>The SMS Gateway app running on the user's Android phone, in local-server
/// mode — the phone answers on the home network and nothing leaves it for anyone
/// else's server, which matters when the payload is 427 people's phone numbers.</summary>
public sealed record AndroidGatewaySettings
{
    /// <summary>What the app shows as its local address, e.g. http://192.168.1.44:8080</summary>
    public string BaseUrl { get; init; } = "";

    public string Username { get; init; } = "";
    public string Password { get; init; } = "";

    public bool IsComplete => BaseUrl.Trim().Length > 0 && Username.Length > 0 && Password.Length > 0;
}

/// <summary>One list of people and where it is kept — "Soccer team", "Primary class",
/// "Neighbours". Each is its own database file, so nothing in one can leak into another
/// and an import that empties one leaves the rest alone.
///
/// Kept in the settings rather than in any of the databases, because a list cannot be
/// the thing that remembers where the other lists are.</summary>
public sealed record SavedList(string Name, string Path)
{
    /// <summary>What to call a file nobody has named: "soccer-team.db" reads as
    /// "Soccer team", which is nearly always what was meant.</summary>
    public static string NameFor(string path)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(path).Replace('-', ' ').Replace('_', ' ');
        var words = stem.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return "Untitled list";
        var joined = string.Join(' ', words);
        return char.ToUpperInvariant(joined[0]) + joined[1..];
    }
}

public sealed record AppSettings
{
    /// <summary>Where the open list is kept. Empty means the usual place. Stored here
    /// rather than in the database for the obvious reason.</summary>
    public string DatabasePath { get; init; } = "";

    /// <summary>Every list the app knows about, in the order the switcher shows them.
    /// Opening one puts it here, so it is one click away afterwards.</summary>
    public IReadOnlyList<SavedList> Lists { get; init; } = [];

    public TextTransport TextVia { get; init; } = TextTransport.Twilio;

    /// <summary>What goes at the bottom of every message. A message from a number
    /// nobody recognises gets ignored or reported; the same message signed gets
    /// answered, so this is the difference between the app working and not.</summary>
    public string Signature { get; init; } = "";

    public EmailSettings Email { get; init; } = new();

    /// <summary>The e-mail account with a name on it: whatever the user typed, or —
    /// when they typed nothing — the name from their signature, so mail arrives under
    /// the same name as everything else.
    ///
    /// Derived here rather than stored, because a value computed on the way into the
    /// settings is a value the Setup screen sees as an unsaved change the moment the
    /// screen opens, for something nobody typed.</summary>
    public EmailSettings EmailAs =>
        Email.DisplayName.Trim().Length > 0
            ? Email
            : Email with { DisplayName = YoursTruly.Core.Domain.Signature.SenderName(Signature) };
    public AndroidGatewaySettings AndroidGateway { get; init; } = new();
    public TwilioSettings Twilio { get; init; } = new();

    /// <summary>Supplied when a file prints a seven-digit number, with no way of
    /// knowing where it is.</summary>
    public string DefaultAreaCode { get; init; } = "435";

    /// <summary>Written out because a record compares a list by reference, and one read
    /// back off disk is never the same object as the one that was written. Without
    /// this, Setup would report unsaved changes the moment it opened, every time.</summary>
    public bool Equals(AppSettings? other) =>
        other is not null
        && DatabasePath == other.DatabasePath
        && Lists.SequenceEqual(other.Lists)
        && TextVia == other.TextVia
        && Signature == other.Signature
        && Email == other.Email
        && AndroidGateway == other.AndroidGateway
        && Twilio == other.Twilio
        && DefaultAreaCode == other.DefaultAreaCode;

    public override int GetHashCode() => HashCode.Combine(
        DatabasePath, Lists.Count, TextVia, Signature, Email, AndroidGateway, Twilio, DefaultAreaCode);
}

public interface ISettingsStore
{
    string Path { get; }
    AppSettings Load();
    void Save(AppSettings settings);
}
