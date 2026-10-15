using System.Globalization;
using YoursTruly.Core.Domain;

namespace YoursTruly.Tests;

public sealed class FollowUpTests
{
    private const string Jonathan = "Jonathan Allen, Thunder FC";
    private static readonly DateTime Today = new(2026, 10, 14, 9, 0, 0);

    [Fact]
    public void Says_how_many_were_left_out_and_when_they_got_it()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            Assert.Equal("Left out 42 people who already got this on 3 Oct.",
                FollowUp.Describe(42, new DateTime(2026, 10, 3, 18, 42, 0), Today));
            Assert.Equal("Left out 1 person who already got this earlier today.",
                FollowUp.Describe(1, Today.AddHours(-2), Today));
            Assert.Equal("Left out 3 people who already got this on 20 Dec 2025.",
                FollowUp.Describe(3, new DateTime(2025, 12, 20), Today));
            Assert.StartsWith("Nobody who is still on your list got this",
                FollowUp.Describe(0, new DateTime(2026, 10, 3), Today), StringComparison.Ordinal);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void Sending_again_starts_from_what_was_typed_not_from_the_signed_copy()
    {
        var sent = Signature.Compose("Practice is Friday at 6:30.", Jonathan);
        Assert.Equal("Practice is Friday at 6:30.", FollowUp.Unsign(sent, Jonathan));

        // And signing it again gives back exactly what went out the first time.
        Assert.Equal(sent, Signature.Compose(FollowUp.Unsign(sent, Jonathan), Jonathan));
    }

    [Fact]
    public void A_message_signed_by_hand_or_under_an_old_signature_is_left_as_it_was()
    {
        var byHand = "Practice is Friday.\nThanks, Jonathan Allen";
        Assert.Equal(byHand, FollowUp.Unsign(Signature.Compose(byHand, Jonathan), Jonathan));

        var old = Signature.Compose("Practice is Friday.", "Coach Allen");
        Assert.Equal(old, FollowUp.Unsign(old, Jonathan));
        Assert.Equal("Practice is Friday.", FollowUp.Unsign("Practice is Friday.", null));
    }
}
