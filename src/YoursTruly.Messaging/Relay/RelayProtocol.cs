using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace YoursTruly.Messaging.Relay;

/// <summary>What Yours Truly on one computer says to Yours Truly on a Mac when it wants a
/// text sent from the Mac's Messages app. Small on purpose: one way to check the Mac is
/// there, one way to send a text, and nothing that reads anything back. The relay can be
/// asked to send and that is all — so somebody who got hold of the key could send texts
/// as you, but could never read a word of your messages.</summary>
public static class RelayProtocol
{
    /// <summary>Picked to be out of the way of anything else likely to be running.</summary>
    public const int DefaultPort = 47474;

    public const string PingPath = "/yours-truly/ping";
    public const string TextPath = "/yours-truly/text";

    /// <summary>Longer than any text anybody means to send, short enough that a request
    /// cannot be used to fill the Mac's memory.</summary>
    public const int LongestText = 5_000;
    public const int LargestBody = 64 * 1024;
}

public sealed record RelayTextRequest(
    [property: JsonPropertyName("to")] string? To,
    [property: JsonPropertyName("text")] string? Text);

/// <summary>Every answer the relay gives. <see cref="Reason"/> is a fixed word the
/// sending side turns into a sentence; <see cref="Error"/> is already a sentence, written
/// by the Mac's own Messages sender, and passed on as it stands.</summary>
public sealed record RelayAnswer
{
    [JsonPropertyName("sent")] public bool? Sent { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("error")] public string? Error { get; init; }
    [JsonPropertyName("limit")] public int? Limit { get; init; }
    [JsonPropertyName("app")] public string? App { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("paused")] public bool? Paused { get; init; }
}

public static class RelayReasons
{
    public const string Paused = "paused";
    public const string Hour = "hour";
    public const string Day = "day";
    public const string LockedOut = "locked";
    public const string NotPrivate = "not-private";
    public const string BadRequest = "bad-request";
    public const string WrongKey = "wrong-key";
}

/// <summary>The key a Mac hands out to the computers allowed to send through it.
///
/// Twenty-five characters in five groups, from an alphabet with no 0/O or 1/I/L to
/// confuse, so it survives being read out over the phone as well as pasted. That is
/// 125 random bits, which is far beyond guessing — and the relay locks a computer out
/// after a handful of wrong ones anyway.</summary>
public static class RelayKey
{
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public static string New()
    {
        Span<char> chars = stackalloc char[25];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        var raw = new string(chars);
        return string.Join('-', Enumerable.Range(0, 5).Select(g => raw.Substring(g * 5, 5)));
    }

    /// <summary>Upper case, with the dashes and spaces gone, so a key typed in lower case
    /// or pasted with a space on the end still matches.</summary>
    public static string Normalise(string? key) =>
        new((key ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Compared in constant time, so how long a refusal takes says nothing
    /// about how much of a guess was right.</summary>
    public static bool Matches(string? offered, string? expected)
    {
        var a = Normalise(offered);
        var b = Normalise(expected);
        if (b.Length == 0) return false;
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));
    }
}

/// <summary>Who the relay is willing to talk to at all: this Mac itself, the network it
/// is on, and a Tailscale network. Never the open internet — even if somebody forwarded
/// the port on their router by mistake, a request from outside is refused before the key
/// is even looked at.</summary>
public static class NetworkScope
{
    public static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return b[0] == 10                                  // 10.0.0.0/8
                || (b[0] == 172 && b[1] is >= 16 and <= 31)    // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254)                // link-local
                || (b[0] == 100 && b[1] is >= 64 and <= 127);  // 100.64.0.0/10: Tailscale
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var b = address.GetAddressBytes();
            return (b[0] & 0xFE) == 0xFC                       // fc00::/7, unique local — Tailscale's fd7a:…
                || address.IsIPv6LinkLocal;
        }

        return false;
    }

    /// <summary>Private, or on the same subnet as one of this Mac's own network
    /// connections. The second matters on home networks with IPv6, where a computer on
    /// the same Wi-Fi often arrives from a global address — public in form, but plainly
    /// next door.</summary>
    public static bool IsNearby(IPAddress address) =>
        IsPrivate(address) || OwnSubnets().Any(s => SameSubnet(address, s.Address, s.PrefixLength));

    /// <summary>Whether two addresses share their first <paramref name="prefixLength"/> bits.</summary>
    public static bool SameSubnet(IPAddress a, IPAddress b, int prefixLength)
    {
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (b.IsIPv4MappedToIPv6) b = b.MapToIPv4();
        if (a.AddressFamily != b.AddressFamily || prefixLength <= 0) return false;

        var x = a.GetAddressBytes();
        var y = b.GetAddressBytes();
        prefixLength = Math.Min(prefixLength, x.Length * 8);

        var whole = prefixLength / 8;
        for (var i = 0; i < whole; i++)
            if (x[i] != y[i]) return false;

        var rest = prefixLength % 8;
        if (rest == 0) return true;
        var mask = (byte)(0xFF << (8 - rest));
        return (x[whole] & mask) == (y[whole] & mask);
    }

    private static IEnumerable<(IPAddress Address, int PrefixLength)> OwnSubnets()
    {
        System.Net.NetworkInformation.NetworkInterface[] nics;
        try
        {
            nics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (System.Net.NetworkInformation.NetworkInformationException)
        {
            yield break;
        }

        foreach (var nic in nics)
        {
            if (nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                if (!IPAddress.IsLoopback(unicast.Address) && unicast.PrefixLength > 0)
                    yield return (unicast.Address, unicast.PrefixLength);
        }
    }
}

/// <summary>How many texts the relay will send in an hour and in a day.
///
/// Two reasons. Carriers watch personal numbers for bursts, and the computers asking
/// already space their texts out — this is the backstop for when two of them ask at
/// once. And if a key ever did leak, this is the most anybody could send before the
/// owner noticed.</summary>
public sealed class SendAllowance(int perHour = 60, int perDay = 200, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Queue<DateTimeOffset> _sent = new();
    private readonly Lock _gate = new();

    public int PerHour => perHour;
    public int PerDay => perDay;

    /// <summary>Takes one text from the allowance, or says which limit is spent.</summary>
    public bool TryTake(out string? reason, out int limit)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            while (_sent.Count > 0 && now - _sent.Peek() >= TimeSpan.FromDays(1)) _sent.Dequeue();

            if (_sent.Count >= perDay)
            {
                (reason, limit) = (RelayReasons.Day, perDay);
                return false;
            }

            if (_sent.Count(t => now - t < TimeSpan.FromHours(1)) >= perHour)
            {
                (reason, limit) = (RelayReasons.Hour, perHour);
                return false;
            }

            _sent.Enqueue(now);
            (reason, limit) = (null, 0);
            return true;
        }
    }

    /// <summary>Texts sent since midnight on this Mac, for the menu-bar icon.</summary>
    public int SentToday
    {
        get
        {
            lock (_gate)
            {
                var midnight = new DateTimeOffset(_clock.GetLocalNow().Date, _clock.GetLocalNow().Offset);
                return _sent.Count(t => t >= midnight);
            }
        }
    }
}
