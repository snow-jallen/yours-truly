using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using YoursTruly.Core.Diagnostics;

namespace YoursTruly.App.Relay;

/// <summary>Switches "Let my other computers text through this Mac" on and off by adding
/// and removing a login item — so it starts when the Mac does, keeps going with the
/// window closed, and comes back if it ever stops unexpectedly.</summary>
public interface IRelayAgent
{
    /// <summary>Only a Mac has Messages to send through.</summary>
    bool IsSupported { get; }

    bool IsOn { get; }

    /// <summary>Adds the login item and starts it. Throws with a message fit to show.</summary>
    void TurnOn();

    void TurnOff();

    /// <summary>Restarts it, for a new key to take effect.</summary>
    void Restart();

    /// <summary>Starts it if it is switched on but not running — after a reinstall, which
    /// quits every copy of the app, or after somebody chose "Stop until I next log in"
    /// and then opened Yours Truly again. Never throws: it is a nudge, not a request.</summary>
    void EnsureRunning();
}

/// <summary>Everywhere that is not a Mac.</summary>
public sealed class NoRelayAgent : IRelayAgent
{
    public bool IsSupported => false;
    public bool IsOn => false;
    public void TurnOn() => throw new PlatformNotSupportedException("Only a Mac can text for other computers.");
    public void TurnOff() { }
    public void Restart() { }
    public void EnsureRunning() { }
}

/// <summary>A launchd LaunchAgent in the user's own Library: the Mac's ordinary way to
/// run something in the background for one person while they are logged in. It runs
/// this same app with <c>--relay</c>, so there is nothing else to install or keep up to
/// date, and macOS sees one app asking to control Messages — the one the user installed.</summary>
public sealed class LaunchAgentRelay : IRelayAgent
{
    public const string Label = "com.YoursTruly.YoursTruly.relay";
    public const string Argument = "--relay";

    private readonly string _executable;
    private readonly string _plist;

    public LaunchAgentRelay(string? executable = null, string? launchAgents = null)
    {
        _executable = executable ?? Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot tell where Yours Truly is installed.");
        _plist = Path.Combine(
            launchAgents ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents"),
            $"{Label}.plist");
    }

    public bool IsSupported => OperatingSystem.IsMacOS();

    public bool IsOn => File.Exists(_plist);

    public void TurnOn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_plist)!);
        File.WriteAllText(_plist, PlistFor(_executable));

        // Out first, in case an older one is still loaded from a previous install.
        Launchctl("bootout", $"{Domain}/{Label}", mayFail: true);
        Launchctl("bootstrap", Domain, _plist);
        Log.Record("relay.on");
    }

    public void TurnOff()
    {
        Launchctl("bootout", $"{Domain}/{Label}", mayFail: true);
        if (File.Exists(_plist)) File.Delete(_plist);
        Log.Record("relay.off");
    }

    public void Restart()
    {
        if (IsOn) Launchctl("kickstart", "-k", $"{Domain}/{Label}");
    }

    public void EnsureRunning()
    {
        if (!IsSupported || !IsOn) return;
        try
        {
            // Without -k: starts it if it is not running, leaves it alone if it is.
            Launchctl(["kickstart", $"{Domain}/{Label}"], mayFail: true);
        }
        catch (Exception e)
        {
            Log.Failure("relay.ensure", e);
        }
    }

    /// <summary>What goes in the login item. Started at login; restarted if it stops
    /// for any reason other than somebody choosing Quit from its menu, which exits
    /// cleanly and so stays stopped until the next login.</summary>
    public static string PlistFor(string executable) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
          <key>Label</key>
          <string>{Label}</string>
          <key>ProgramArguments</key>
          <array>
            <string>{SecurityElement.Escape(executable)}</string>
            <string>{Argument}</string>
          </array>
          <key>RunAtLoad</key>
          <true/>
          <key>KeepAlive</key>
          <dict>
            <key>SuccessfulExit</key>
            <false/>
          </dict>
          <key>ThrottleInterval</key>
          <integer>30</integer>
          <key>LimitLoadToSessionType</key>
          <string>Aqua</string>
          <key>ProcessType</key>
          <string>Interactive</string>
        </dict>
        </plist>
        """;

    private static string Domain => $"gui/{getuid()}";

    [DllImport("libc")]
    private static extern uint getuid();

    private static void Launchctl(params string[] arguments) => Launchctl(arguments, mayFail: false);

    private static void Launchctl(string verb, string target, bool mayFail) =>
        Launchctl([verb, target], mayFail);

    private static void Launchctl(string[] arguments, bool mayFail)
    {
        var start = new ProcessStartInfo("/bin/launchctl")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("macOS would not start launchctl.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !mayFail)
        {
            Log.Record("relay.launchctl", Log.Details(
                ("verb", arguments[0]), ("exit", process.ExitCode), ("said", error.Trim())));
            throw new InvalidOperationException(
                "macOS would not start the background part of Yours Truly. Turn it off and on again here; if it still will not start, restart the Mac and try once more.");
        }
    }
}
