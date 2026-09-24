using YoursTruly.Core.Domain;

namespace YoursTruly.Tests;

/// <summary>Gmail's daily sending limit, and how near a send is to it.</summary>
public sealed class GmailQuotaTests
{
    [Fact]
    public void Knows_the_two_numbers_Google_publishes()
    {
        // Pinned deliberately. These come from Google and from nowhere else — a free
        // account is 500 a day (support.google.com/mail/answer/22839) and Workspace is
        // 2,000 (knowledge.workspace.google.com). If either changes, this test is where
        // to change it, and the doc comment on GmailQuota is where the links are.
        Assert.Equal(500, GmailQuota.FreeAccount);
        Assert.Equal(2_000, GmailQuota.Workspace);

        // A rolling 24 hours, not a calendar day. Google is explicit that the limit is
        // "applied over a rolling 24-hour period, not a set time of day", and counting
        // from midnight would under-report all morning after a big evening send.
        Assert.Equal(TimeSpan.FromHours(24), GmailQuota.Window);
    }

    [Theory]
    [InlineData("smtp.gmail.com", "jonathan@gmail.com", 500)]
    [InlineData("smtp.gmail.com", "someone@googlemail.com", 500)]
    [InlineData("SMTP.GMAIL.COM", "SOMEONE@GMAIL.COM", 500)]
    public void A_gmail_address_is_a_free_account(string host, string address, int limit) =>
        Assert.Equal(limit, GmailQuota.For(host, address)!.Limit);

    [Theory]
    // Workspace runs on the same SMTP host, and the only thing that tells the two apart
    // is the address: a free account is always at gmail.com and a Workspace account is
    // always at a domain of its own.
    [InlineData("smtp.gmail.com", "jonathan@snow.edu")]
    [InlineData("smtp.gmail.com", "someone@example.org")]
    public void A_domain_of_its_own_on_gmail_is_workspace(string host, string address) =>
        Assert.Equal(2_000, GmailQuota.For(host, address)!.Limit);

    [Theory]
    [InlineData("smtp.fastmail.com", "someone@fastmail.com")]
    [InlineData("smtp.office365.com", "someone@outlook.com")]
    [InlineData("", "someone@gmail.com")]
    // Nothing filled in yet. The host defaults to smtp.gmail.com on a fresh install, so
    // without the address there is no account to have a limit.
    [InlineData("smtp.gmail.com", "")]
    [InlineData("smtp.gmail.com", "notanaddress")]
    public void Says_nothing_about_a_provider_it_does_not_know(string host, string address)
    {
        // The bar is hidden rather than guessed at. A made-up limit shown confidently is
        // worse than no limit at all: it would either nag about a cap that does not
        // exist or promise room that is not there.
        Assert.Null(GmailQuota.For(host, address));
    }

    [Fact]
    public void Counts_what_is_about_to_go_out_as_well_as_what_has()
    {
        // The question on the Send screen is not "how much have I used" but "will this
        // send fit", so the pending messages are part of the reading.
        var quota = GmailQuota.For("smtp.gmail.com", "me@gmail.com")!;

        var use = quota.After(sent: 100, adding: 40);
        Assert.Equal(140 / 500.0, use.Fill, 3);
        Assert.Equal(QuotaPressure.Fine, use.Pressure);
        Assert.Equal("100 of 500", use.Count());
        Assert.Equal("sent in the last 24 hours, and this send adds 40", use.Describe());
        Assert.Equal("", use.Advise());
    }

    [Fact]
    public void Speaks_up_when_a_send_would_not_fit()
    {
        var quota = GmailQuota.For("smtp.gmail.com", "me@gmail.com")!;
        var use = quota.After(sent: 430, adding: 200);

        Assert.Equal(QuotaPressure.Over, use.Pressure);
        Assert.Equal(70, use.Remaining);

        // And says the thing that actually helps: how many will fit, and that waiting
        // works because the window rolls.
        Assert.Contains("room for 70 more", use.Advise(), StringComparison.Ordinal);
        Assert.Contains("rolling 24 hours", use.Advise(), StringComparison.Ordinal);
    }

    [Fact]
    public void Warns_before_the_limit_rather_than_at_it()
    {
        var quota = GmailQuota.For("smtp.gmail.com", "me@gmail.com")!;
        Assert.Equal(QuotaPressure.Fine, quota.After(399).Pressure);
        Assert.Equal(QuotaPressure.Close, quota.After(400).Pressure);
        Assert.Equal(QuotaPressure.Over, quota.After(501).Pressure);
    }

    [Fact]
    public void The_bar_never_runs_past_its_own_end()
    {
        // Over the limit is said in words. A bar drawn past 100% just looks broken.
        var quota = GmailQuota.For("smtp.gmail.com", "me@gmail.com")!;
        Assert.Equal(1, quota.After(sent: 900, adding: 300).Fill);
        Assert.Equal(0, quota.After(sent: 0).Fill);
    }
}
