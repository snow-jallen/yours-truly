using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace YoursTruly.Messaging.Relay;

/// <summary>One way another computer can reach this Mac, and what it is.</summary>
public sealed record RelayAddress(string Url, string Where);

/// <summary>The addresses to give the user's other computers. Every private address the
/// Mac has, labelled — a Tailscale one first, because it is the one that works from
/// anywhere and never changes — and the Mac's own name on the local network.</summary>
public static class RelayAddressBook
{
    public static IReadOnlyList<RelayAddress> ForThisMac(int port = RelayProtocol.DefaultPort)
    {
        var found = new List<RelayAddress>();

        foreach (var nic in SafeInterfaces())
        {
            foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip)) continue;
                if (!NetworkScope.IsPrivate(ip)) continue;
                found.Add(new RelayAddress($"http://{ip}:{port}", Describe(ip)));
            }
        }

        var name = Dns.GetHostName();
        if (name.Length > 0)
        {
            var local = name.Contains('.', StringComparison.Ordinal) ? name : $"{name}.local";
            found.Add(new RelayAddress($"http://{local}:{port}", "this Mac's name on its own network"));
        }

        return [.. found.OrderBy(a => a.Where.StartsWith("Tailscale", StringComparison.Ordinal) ? 0 : 1)
            .DistinctBy(a => a.Url)];
    }

    /// <summary>What an address is, in words: Tailscale's own range is recognisable, and
    /// everything else private is the network the Mac is plugged into.</summary>
    public static string Describe(IPAddress ip)
    {
        var b = ip.MapToIPv4().GetAddressBytes();
        return b[0] == 100 && b[1] is >= 64 and <= 127
            ? "Tailscale — works from anywhere on your Tailscale network"
            : "this network — only from the same Wi-Fi or office network";
    }

    private static IEnumerable<NetworkInterface> SafeInterfaces()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up);
        }
        catch (NetworkInformationException)
        {
            return [];
        }
    }
}
