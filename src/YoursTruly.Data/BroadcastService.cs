using YoursTruly.Core.Diagnostics;
using YoursTruly.Core.Domain;
using YoursTruly.Data.Entities;
using YoursTruly.Messaging;

namespace YoursTruly.Data;

/// <summary>How far a send has got. <see cref="NextTextIn"/> is set while it is
/// waiting before the next text, so the screen can say why nothing is happening.</summary>
public sealed record BroadcastProgress(int Done, int Total, string Who, TimeSpan? NextTextIn = null);

/// <summary>Sends one message to a chosen set of people and writes down what happened
/// to each of them, on each channel they asked for.
///
/// Texts are spaced out by <see cref="TextPacing"/>; e-mails and calls are not.
/// <paramref name="wait"/> and <paramref name="random"/> exist for the tests, which
/// should neither sleep nor depend on luck.</summary>
public sealed class BroadcastService(
    AppDbContext db,
    IReadOnlyDictionary<Channel, IMessageSender> senders,
    Func<TimeSpan, CancellationToken, Task>? wait = null,
    Random? random = null)
{
    private readonly Func<TimeSpan, CancellationToken, Task> _wait = wait ?? Task.Delay;
    private readonly Random _random = random ?? Random.Shared;

    /// <summary>Sends, recording every delivery as it goes.
    ///
    /// Each delivery is saved as its own row before the next is attempted, so a send
    /// that is interrupted half way leaves an honest record rather than nothing: the
    /// messages already sent cannot be unsent, and the user has to be able to see which
    /// those were.</summary>
    public async Task<MessageBatch> SendAsync(
        string subject,
        string body,
        string audienceDescription,
        IReadOnlyList<Recipient> chosen,
        IProgress<BroadcastProgress>? progress = null,
        Channel? via = null,
        string? voiceRecordingUrl = null,
        bool speakAloud = false,
        CancellationToken cancellation = default)
    {
        var batch = new MessageBatch
        {
            Subject = string.IsNullOrWhiteSpace(subject) ? null : subject,
            Body = body,
            VoiceRecordingPath = voiceRecordingUrl,
            AudienceDescription = audienceDescription,
        };
        db.MessageBatches.Add(batch);
        await db.SaveChangesAsync(cancellation);

        var done = 0;
        var textedAlready = false;
        foreach (var person in chosen)
        {
            cancellation.ThrowIfCancellationRequested();

            // Everything this person ticked, and all of it happens: somebody who asked
            // for a text and an e-mail gets both, as two deliveries with two records.
            var reaches = via is { } channel
                ? (IReadOnlyList<Reachability>)[person.ReachabilityVia(channel)]
                : person.Reachabilities;

            foreach (var reach in reaches)
            {
                var delivery = new MessageDelivery
                {
                    MessageBatchId = batch.Id,
                    PersonId = person.Id,
                    Channel = reach.Channel,
                    Address = reach.Address ?? "",
                };

                if (!reach.CanReceive)
                {
                    delivery.Status = DeliveryStatus.Skipped;
                    delivery.Error = Explain(reach.Reason);
                }
                else if (!senders.TryGetValue(reach.Channel, out var sender))
                {
                    delivery.Status = DeliveryStatus.Skipped;
                    delivery.Error = $"Yours Truly cannot send by {reach.Channel.ToWire()} yet.";
                }
                else
                {
                    // Only between texts that actually go to the provider: a skipped
                    // person sends nothing, so there is nothing to space out from.
                    if (reach.Channel is Channel.Text)
                    {
                        if (textedAlready)
                        {
                            var gap = TextPacing.NextGap(_random);
                            progress?.Report(new BroadcastProgress(done, chosen.Count, person.FullName, gap));
                            await _wait(gap, cancellation);
                        }
                        textedAlready = true;
                    }

                    var outcome = await sender.SendAsync(
                        reach.Address!,
                        new OutgoingMessage(subject, body, voiceRecordingUrl, speakAloud),
                        cancellation);

                    delivery.Status = outcome.Status switch
                    {
                        SendStatus.Sent => DeliveryStatus.Sent,
                        SendStatus.Skipped => DeliveryStatus.Skipped,
                        _ => DeliveryStatus.Failed,
                    };
                    delivery.ProviderMessageId = outcome.ProviderMessageId;
                    delivery.Error = outcome.Error;
                    delivery.CostMicros = outcome.CostMicros;
                    if (outcome.Status == SendStatus.Sent) delivery.SentAt = DateTimeOffset.UtcNow;
                }

                if (delivery.Status is not DeliveryStatus.Sent)
                    Log.Record("delivery.problem", Log.Details(
                        ("person", person.Id), ("channel", delivery.Channel.ToWire()),
                        ("address", Redact.Address(delivery.Address)),
                        ("status", delivery.Status.ToString()),
                        ("reason", Redact.Failure(delivery.Error))));

                db.MessageDeliveries.Add(delivery);
                await db.SaveChangesAsync(cancellation);
            }

            progress?.Report(new BroadcastProgress(++done, chosen.Count, person.FullName));
        }

        batch.SentAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellation);
        return batch;
    }

    private static string Explain(UnreachableReason reason) => reason switch
    {
        UnreachableReason.NoChannelChosen => "Nobody has chosen how to contact this person yet.",
        UnreachableReason.MissingAddress => "There is no address or number for the channel they prefer.",
        UnreachableReason.ChannelNotSupported => "Yours Truly cannot send on the channel they prefer yet.",
        UnreachableReason.NotInDirectory => "This person is no longer in the directory.",
        _ => "This person could not be reached.",
    };
}
