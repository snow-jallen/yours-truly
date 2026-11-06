using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using YoursTruly.Core.Diagnostics;

namespace YoursTruly.Messaging.Relay;

/// <summary>Listens on the Mac for other copies of Yours Truly asking for a text to be
/// sent, and sends it through the Mac's Messages app.
///
/// Every request passes three checks before anything is sent: it comes from this
/// network or a Tailscale one, it carries the key, and the hourly and daily allowance
/// is not spent. Texts go out one at a time, because Messages drops them when pushed.
///
/// Plain HTTP rather than HTTPS, deliberately. Over Tailscale the connection is already
/// encrypted end to end; on a home or office network the key is the protection, and a
/// self-signed certificate would only teach people to click past a warning.</summary>
public sealed class RelayServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _key;
    private readonly IMessageSender _messages;
    private readonly SendAllowance _allowance;
    private readonly TimeProvider _clock;
    private readonly Func<IPAddress, bool> _allowed;
    private readonly string _version;
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _wrongKeys = new();
    private Task? _loop;

    /// <summary>Wrong keys allowed from one address in <see cref="LockoutWindow"/>
    /// before it is refused outright for the rest of that window.</summary>
    public const int WrongKeysAllowed = 5;
    public static readonly TimeSpan LockoutWindow = TimeSpan.FromMinutes(10);

    /// <param name="host">Which addresses to listen on: "*" for all of them, which is
    /// what the Mac does, or "localhost" in the tests.</param>
    public RelayServer(
        string key,
        IMessageSender messages,
        SendAllowance? allowance = null,
        int port = RelayProtocol.DefaultPort,
        string host = "*",
        TimeProvider? clock = null,
        Func<IPAddress, bool>? allowed = null,
        string version = "")
    {
        if (RelayKey.Normalise(key).Length == 0)
            throw new ArgumentException("The relay needs a key before it can start.", nameof(key));

        _key = key;
        _messages = messages;
        _allowance = allowance ?? new SendAllowance();
        _clock = clock ?? TimeProvider.System;
        _allowed = allowed ?? NetworkScope.IsNearby;
        _version = version;
        Port = port;
        _listener.Prefixes.Add($"http://{host}:{port}/");
    }

    public int Port { get; }

    /// <summary>Switched from the menu-bar icon. While paused every request to send is
    /// refused with a reason, so the computer asking can say so in words.</summary>
    public bool Paused { get; set; }

    public SendAllowance Allowance => _allowance;

    /// <summary>Raised after each text, sent or not, so the menu-bar icon can keep count.</summary>
    public event Action<bool>? TextHandled;

    public void Start()
    {
        _listener.Start();
        _loop = Task.Run(LoopAsync);
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception) when (!_listener.IsListening)
            {
                return;
            }
            catch (HttpListenerException e)
            {
                Log.Failure("relay.accept", e);
                continue;
            }

            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            var (status, answer) = await AnswerAsync(context.Request);
            await WriteAsync(context.Response, status, answer);
        }
        catch (Exception e)
        {
            Log.Failure("relay.request", e);
            try { context.Response.Abort(); } catch (Exception) { /* already gone */ }
        }
    }

    private async Task<(HttpStatusCode, RelayAnswer)> AnswerAsync(HttpListenerRequest request)
    {
        var from = request.RemoteEndPoint.Address;

        if (!_allowed(from))
        {
            Log.Record("relay.refused", Log.Details(("why", "not-private")));
            return (HttpStatusCode.Forbidden, new RelayAnswer { Reason = RelayReasons.NotPrivate });
        }

        var who = from.ToString();
        if (LockedOut(who))
            return (HttpStatusCode.TooManyRequests, new RelayAnswer { Reason = RelayReasons.LockedOut });

        if (!RelayKey.Matches(Bearer(request), _key))
        {
            RememberWrongKey(who);
            Log.Record("relay.refused", Log.Details(("why", "wrong-key")));
            return (HttpStatusCode.Unauthorized, new RelayAnswer { Reason = RelayReasons.WrongKey });
        }

        var path = request.Url?.AbsolutePath ?? "";
        if (request.HttpMethod == "GET" && path == RelayProtocol.PingPath)
            return (HttpStatusCode.OK, new RelayAnswer { App = "Yours Truly", Version = _version, Paused = Paused });

        if (request.HttpMethod == "POST" && path == RelayProtocol.TextPath)
            return await SendAsync(request);

        return (HttpStatusCode.NotFound, new RelayAnswer { Reason = RelayReasons.BadRequest });
    }

    private async Task<(HttpStatusCode, RelayAnswer)> SendAsync(HttpListenerRequest request)
    {
        var asked = await ReadAsync(request);
        if (asked is not { To: { Length: > 0 } to, Text: { Length: > 0 } text } || text.Length > RelayProtocol.LongestText)
            return (HttpStatusCode.BadRequest, new RelayAnswer { Reason = RelayReasons.BadRequest });

        if (Paused)
            return (HttpStatusCode.ServiceUnavailable, new RelayAnswer { Reason = RelayReasons.Paused });

        if (!_allowance.TryTake(out var spent, out var limit))
        {
            Log.Record("relay.refused", Log.Details(("why", spent)));
            return (HttpStatusCode.TooManyRequests, new RelayAnswer { Reason = spent, Limit = limit });
        }

        await _oneAtATime.WaitAsync();
        try
        {
            var outcome = await _messages.SendAsync(to, new OutgoingMessage("", text));
            var sent = outcome.Status == SendStatus.Sent;

            // Never the number or the words: see CLAUDE.md, Logging.
            Log.Record("relay.text", Log.Details(
                ("to", Redact.Address(to)), ("body", Redact.Text(text)), ("sent", sent),
                ("result", Redact.Failure(outcome.Error))));
            TextHandled?.Invoke(sent);

            return sent
                ? (HttpStatusCode.OK, new RelayAnswer { Sent = true })
                : (HttpStatusCode.UnprocessableEntity, new RelayAnswer { Sent = false, Error = outcome.Error });
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    // --- the key, and the computers that keep getting it wrong -----------------------

    private static string? Bearer(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        const string scheme = "Bearer ";
        return header is not null && header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)
            ? header[scheme.Length..]
            : null;
    }

    private bool LockedOut(string who)
    {
        if (!_wrongKeys.TryGetValue(who, out var times)) return false;
        lock (times)
        {
            Forget(times);
            return times.Count >= WrongKeysAllowed;
        }
    }

    private void RememberWrongKey(string who)
    {
        var times = _wrongKeys.GetOrAdd(who, _ => new Queue<DateTimeOffset>());
        lock (times)
        {
            Forget(times);
            times.Enqueue(_clock.GetUtcNow());
        }
    }

    private void Forget(Queue<DateTimeOffset> times)
    {
        var now = _clock.GetUtcNow();
        while (times.Count > 0 && now - times.Peek() >= LockoutWindow) times.Dequeue();
    }

    // --- reading and writing ------------------------------------------------------

    private static async Task<RelayTextRequest?> ReadAsync(HttpListenerRequest request)
    {
        if (request.ContentLength64 > RelayProtocol.LargestBody) return null;

        var buffer = new byte[RelayProtocol.LargestBody + 1];
        var read = 0;
        int got;
        while (read < buffer.Length && (got = await request.InputStream.ReadAsync(buffer.AsMemory(read))) > 0)
            read += got;
        if (read > RelayProtocol.LargestBody) return null;

        try
        {
            return JsonSerializer.Deserialize<RelayTextRequest>(buffer.AsSpan(0, read));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions Quiet = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static async Task WriteAsync(HttpListenerResponse response, HttpStatusCode status, RelayAnswer answer)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(answer, Quiet));
        response.StatusCode = (int)status;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (_listener.IsListening) _listener.Stop();
        if (_loop is not null) await _loop;
        _listener.Close();
        _oneAtATime.Dispose();
    }
}
