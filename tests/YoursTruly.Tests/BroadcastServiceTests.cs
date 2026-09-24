using YoursTruly.Core.Domain;
using YoursTruly.Data;
using YoursTruly.Data.Entities;
using YoursTruly.Messaging;
using YoursTruly.Messaging.Settings;
using Microsoft.EntityFrameworkCore;

namespace YoursTruly.Tests;

public sealed class BroadcastServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"yourstruly-send-{Guid.NewGuid():N}.db");

    private sealed class FakeSender(Channel channel, SendOutcome? outcome = null) : IMessageSender
    {
        public List<string> SentTo { get; } = [];
        public Channel Channel { get; } = channel;
        public bool IsConfigured => true;

        public Task<SendOutcome> SendAsync(string address, OutgoingMessage message, CancellationToken ct)
        {
            SentTo.Add(address);
            return Task.FromResult(outcome ?? SendOutcome.Sent("ID" + SentTo.Count));
        }

        public Task<CredentialCheck> TestAsync(string address, CancellationToken ct) =>
            Task.FromResult(CredentialCheck.Working("fine"));
    }

    /// <summary>For tests that are not about pacing: texts go straight out, so the
    /// suite does not sit through real pauses.</summary>
    private static Task NoWait(TimeSpan gap, CancellationToken ct) => Task.CompletedTask;

    private AppDbContext Open()
    {
        var db = AppDatabase.Open(_path);
        db.Database.Migrate();
        return db;
    }

    /// <summary>A delivery hangs off a real person, so the people have to be in the
    /// database before anything can be sent to them.</summary>
    private static Task<Recipient> PersonAsync(
        AppDbContext db, string last, Channel channel = Channel.Email,
        string? email = "someone@example.com", string? phone = "+14355550100") =>
        PersonAsync(db, last, ChannelSet.Of(channel), email, phone);

    private static async Task<Recipient> PersonAsync(
        AppDbContext db, string last, ChannelSet channels,
        string? email = "someone@example.com", string? phone = "+14355550100")
    {
        var person = new YoursTruly.Data.Entities.Person
        {
            LastName = last,
            FirstName = "Test",
            DisplayName = $"{last}, Test",
            Age = 40,
            BirthMonth = 3,
            BirthDay = 4,
            PreferredChannels = channels,
            FirstSeenOn = new DateOnly(2026, 9, 16),
            LastSeenOn = new DateOnly(2026, 9, 16),
        };
        db.People.Add(person);
        await db.SaveChangesAsync();

        return new Recipient(person.Id, last, "Test", person.DisplayName, 40, 3, 4,
            channels, email, phone, true);
    }

    [Fact]
    public async Task Each_person_is_sent_to_on_the_channel_they_chose()
    {
        using var db = Open();
        var email = new FakeSender(Channel.Email);
        var text = new FakeSender(Channel.Text);
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Email] = email, [Channel.Text] = text }, NoWait);

        await service.SendAsync("Dinner", "Friday at 6:30", "Everyone (2)",
            [await PersonAsync(db, "Mail", Channel.Email), await PersonAsync(db, "Text", Channel.Text)]);

        Assert.Equal(["someone@example.com"], email.SentTo);
        Assert.Equal(["+14355550100"], text.SentTo);
    }

    [Fact]
    public async Task An_override_sends_everyone_the_same_way_whatever_they_chose()
    {
        using var db = Open();
        var email = new FakeSender(Channel.Email);
        var text = new FakeSender(Channel.Text);
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Email] = email, [Channel.Text] = text }, NoWait);

        await service.SendAsync("Urgent", "Moved to Thursday", "Everyone, all by text (2)",
            [await PersonAsync(db, "Mail", Channel.Email), await PersonAsync(db, "None", Channel.None)],
            via: Channel.Text);

        Assert.Empty(email.SentTo);
        Assert.Equal(2, text.SentTo.Count);
        Assert.All(await db.MessageDeliveries.ToListAsync(),
            d => Assert.Equal(Channel.Text, d.Channel));
    }

    [Fact]
    public async Task Someone_unreachable_is_recorded_as_skipped_with_the_reason()
    {
        using var db = Open();
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Email] = new FakeSender(Channel.Email) }, NoWait);

        await service.SendAsync("Dinner", "Friday", "Everyone (1)",
            [await PersonAsync(db, "NoEmail", Channel.Email, email: null)]);

        var delivery = await db.MessageDeliveries.SingleAsync();
        Assert.Equal(DeliveryStatus.Skipped, delivery.Status);
        Assert.Contains("no address", delivery.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_failure_is_written_down_in_the_words_the_user_will_read()
    {
        using var db = Open();
        var failing = new FakeSender(Channel.Email, SendOutcome.Failed("Gmail needs an app password."));
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Email] = failing }, NoWait);

        await service.SendAsync("Dinner", "Friday", "Everyone (1)", [await PersonAsync(db, "Mail")]);

        var delivery = await db.MessageDeliveries.SingleAsync();
        Assert.Equal(DeliveryStatus.Failed, delivery.Status);
        Assert.Equal("Gmail needs an app password.", delivery.Error);
        Assert.Null(delivery.SentAt);
    }

    [Fact]
    public async Task What_already_went_out_survives_a_send_that_is_stopped_half_way()
    {
        using var db = Open();
        using var stop = new CancellationTokenSource();
        var sender = new FakeSender(Channel.Email);
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Email] = sender }, NoWait);

        var people = new[]
        {
            await PersonAsync(db, "One"), await PersonAsync(db, "Two"), await PersonAsync(db, "Three"),
        };
        var progress = new Progress<BroadcastProgress>(p => { if (p.Done == 2) stop.Cancel(); });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.SendAsync("Dinner", "Friday", "Everyone (3)", people, progress, cancellation: stop.Token));

        // Two were really sent, and the record says so. Pretending otherwise would have
        // the user send to them twice.
        Assert.Equal(2, await db.MessageDeliveries.CountAsync(d => d.Status == DeliveryStatus.Sent));
    }

    [Fact]
    public async Task The_batch_records_what_was_written_and_who_it_went_to()
    {
        using var db = Open();
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Email] = new FakeSender(Channel.Email) }, NoWait);

        await service.SendAsync("Stake dinner", "Friday at 6:30", "Manti 2nd Ward (1)",
            [await PersonAsync(db, "Mail")]);

        var batch = await db.MessageBatches.SingleAsync();
        Assert.Equal("Stake dinner", batch.Subject);
        Assert.Equal("Manti 2nd Ward (1)", batch.AudienceDescription);
        Assert.NotNull(batch.SentAt);
    }

    [Fact]
    public async Task A_channel_with_no_sender_is_skipped_rather_than_crashing_the_send()
    {
        using var db = Open();
        var service = new BroadcastService(db, new Dictionary<Channel, IMessageSender>(), NoWait);

        await service.SendAsync("Dinner", "Friday", "Everyone (1)", [await PersonAsync(db, "Mail")]);

        var delivery = await db.MessageDeliveries.SingleAsync();
        Assert.Equal(DeliveryStatus.Skipped, delivery.Status);
    }

    /// <summary>Stands in for Task.Delay: records each gap instead of sleeping through it.</summary>
    private sealed class Stopwatch
    {
        public List<TimeSpan> Gaps { get; } = [];
        public Task Wait(TimeSpan gap, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Gaps.Add(gap);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Texts_go_out_two_to_eleven_seconds_apart_with_no_wait_before_the_first()
    {
        using var db = Open();
        var text = new FakeSender(Channel.Text);
        var clock = new Stopwatch();
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Text] = text }, clock.Wait, new Random(7));

        var people = new List<Recipient>();
        for (var i = 0; i < 6; i++) people.Add(await PersonAsync(db, $"Text{i}", Channel.Text));
        await service.SendAsync("", "Friday", "Everyone (6)", people);

        Assert.Equal(6, text.SentTo.Count);
        Assert.Equal(5, clock.Gaps.Count);
        Assert.All(clock.Gaps, g => Assert.InRange(g, TextPacing.Shortest, TextPacing.Longest));
        Assert.True(clock.Gaps.Distinct().Count() > 1, "every gap was the same length, which is the pattern to avoid");
    }

    [Fact]
    public async Task Twilio_sends_every_text_at_once()
    {
        // The gaps exist to keep a carrier from flagging the user's own phone number.
        // A Twilio number is rented for the purpose and Twilio paces its own sending,
        // so waiting buys nothing and costs the user the whole send: six texts is under
        // a minute of waiting, but a directory of four hundred is three quarters of an
        // hour with the window pinned open.
        using var db = Open();
        var text = new FakeSender(Channel.Text);
        var clock = new Stopwatch();
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Text] = text },
            clock.Wait, paceTexts: false);

        var people = new List<Recipient>();
        for (var i = 0; i < 6; i++) people.Add(await PersonAsync(db, $"Text{i}", Channel.Text));
        await service.SendAsync("", "Friday", "Everyone (6)", people);

        Assert.Equal(6, text.SentTo.Count);
        Assert.Empty(clock.Gaps);
    }

    [Fact]
    public void Pacing_is_a_property_of_the_route_and_defaults_to_on()
    {
        // Twilio is the only route that does not need it; the other two send from the
        // user's real number. A route added later gets pacing until somebody decides
        // otherwise, which is the safe way round.
        Assert.False(TextTransport.Twilio.NeedsPacing());
        Assert.True(TextTransport.MacMessages.NeedsPacing());
        Assert.True(TextTransport.AndroidGateway.NeedsPacing());
    }

    [Fact]
    public async Task Emails_calls_and_skipped_people_are_not_waited_for()
    {
        using var db = Open();
        var clock = new Stopwatch();
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender>
            {
                [Channel.Email] = new FakeSender(Channel.Email),
                [Channel.Text] = new FakeSender(Channel.Text),
                [Channel.Voice] = new FakeSender(Channel.Voice),
            }, clock.Wait);

        await service.SendAsync("Dinner", "Friday", "Everyone (5)",
        [
            await PersonAsync(db, "Mail1", Channel.Email),
            await PersonAsync(db, "Text1", Channel.Text),
            await PersonAsync(db, "NoPhone", Channel.Text, phone: null),
            await PersonAsync(db, "Call", Channel.Voice),
            await PersonAsync(db, "Mail2", Channel.Email),
        ]);

        // Only one text actually went out, so there was nothing to space it from.
        Assert.Empty(clock.Gaps);
    }

    [Fact]
    public async Task The_screen_is_told_how_long_until_the_next_text()
    {
        using var db = Open();
        var clock = new Stopwatch();
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Text] = new FakeSender(Channel.Text) }, clock.Wait);
        var reports = new List<BroadcastProgress>();

        await service.SendAsync("", "Friday", "Everyone (2)",
            [await PersonAsync(db, "A", Channel.Text), await PersonAsync(db, "B", Channel.Text)],
            new SynchronousProgress(reports));

        var pause = Assert.Single(reports, r => r.NextTextIn is not null);
        Assert.Equal(1, pause.Done);
        Assert.Equal(clock.Gaps.Single(), pause.NextTextIn);
    }

    [Fact]
    public async Task Stopping_during_a_wait_leaves_the_texts_already_sent_on_record()
    {
        using var db = Open();
        using var stop = new CancellationTokenSource();
        var text = new FakeSender(Channel.Text);
        var service = new BroadcastService(db,
            new Dictionary<Channel, IMessageSender> { [Channel.Text] = text },
            (_, ct) => { stop.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; });

        List<Recipient> people = [await PersonAsync(db, "A", Channel.Text), await PersonAsync(db, "B", Channel.Text)];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.SendAsync("", "Friday", "Everyone (2)", people, cancellation: stop.Token));

        Assert.Single(text.SentTo);
        Assert.Equal(DeliveryStatus.Sent, Assert.Single(db.MessageDeliveries.AsNoTracking()).Status);
    }

    /// <summary>Progress&lt;T&gt; posts to a synchronization context, so its reports can
    /// arrive after the send has returned. This one records them as they happen.</summary>
    private sealed class SynchronousProgress(List<BroadcastProgress> into) : IProgress<BroadcastProgress>
    {
        public void Report(BroadcastProgress value) => into.Add(value);
    }

    public void Dispose()
    {
        // Deliberately not ClearAllPools: it is process-wide, and clearing pools while
        // another test class still holds a connection makes unrelated tests fail.
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (IOException) { /* a temp file left behind harms nothing */ }
    }
}
