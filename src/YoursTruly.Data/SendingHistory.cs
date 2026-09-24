using Microsoft.EntityFrameworkCore;
using YoursTruly.Core.Domain;
using YoursTruly.Data.Entities;

namespace YoursTruly.Data;

/// <summary>What this list has already sent, for the quota on the Send screen.</summary>
public static class SendingHistory
{
    /// <summary>E-mails accepted by the provider in the last 24 hours.
    ///
    /// Counted from a rolling 24 hours rather than from midnight, because that is how
    /// Google applies the limit. Only <see cref="DeliveryStatus.Sent"/> counts: a
    /// message the server refused never reached Gmail's tally, and a skipped person was
    /// never sent anything at all.
    ///
    /// This knows only what this list sent. Mail sent from the same Gmail account by
    /// anything else — another list in another file, or Gmail itself in a browser —
    /// counts against the same quota and cannot be seen from here, so the number is a
    /// floor rather than the truth. The screen says so.</summary>
    public static Task<int> EmailsSentRecentlyAsync(
        AppDbContext db, DateTimeOffset now, CancellationToken cancellation = default)
    {
        var since = now - GmailQuota.Window;
        return db.MessageDeliveries.CountAsync(
            d => d.Channel == Channel.Email
                 && d.Status == DeliveryStatus.Sent
                 && d.SentAt != null && d.SentAt >= since,
            cancellation);
    }
}
