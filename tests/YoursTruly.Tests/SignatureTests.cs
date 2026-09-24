using YoursTruly.Core.Domain;

namespace YoursTruly.Tests;

public sealed class SignatureTests
{
    private const string Jonathan = "Jonathan Allen, Thunder FC";

    [Fact]
    public void Every_message_says_who_it_is_from()
    {
        var text = Signature.Compose("Practice is Friday at 6:30.", Jonathan);
        Assert.Equal("Practice is Friday at 6:30.\n— Jonathan Allen, Thunder FC", text);
    }

    [Fact]
    public void A_signature_is_whatever_the_sender_writes_in_it()
    {
        // One box, not a name and a role: the app has no idea what somebody wants to
        // be called or what they want to say about themselves, and should not guess.
        var text = Signature.Compose("Practice is Friday.", "Jonathan\n435-555-0101");
        Assert.EndsWith("— Jonathan\n435-555-0101", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_is_appended_when_nobody_has_said_who_they_are()
    {
        Assert.Equal("Practice is Friday.", Signature.Compose("Practice is Friday.", ""));
        Assert.Equal("Practice is Friday.", Signature.Compose("Practice is Friday.", "   "));
    }

    [Fact]
    public void Signing_off_by_hand_is_not_doubled()
    {
        var typed = "Practice is Friday at 6:30.\nThanks, Jonathan Allen";
        Assert.Equal(typed, Signature.Compose(typed, Jonathan));
    }

    [Fact]
    public void Mentioning_yourself_in_passing_still_gets_a_signature()
    {
        var body = "Jonathan Allen is bringing the cones. Doors open at six.\nBring a ball if you can.\nSee you there.";
        Assert.EndsWith("— Jonathan Allen, Thunder FC",
            Signature.Compose(body, Jonathan), StringComparison.Ordinal);
    }

    [Fact]
    public void Trailing_blank_lines_do_not_push_the_signature_away()
    {
        Assert.Equal("Practice is Friday.\n— Jonathan Allen, Thunder FC",
            Signature.Compose("Practice is Friday.\n\n\n", Jonathan));
    }

    [Fact]
    public void A_signature_longer_than_the_limit_is_cut_rather_than_refused()
    {
        var long_ = new string('x', Signature.MaxLength + 50);
        Assert.Equal(Signature.MaxLength, Signature.Clean(long_)!.Length);
    }

    [Fact]
    public void The_name_to_show_in_a_corner_is_the_first_line_up_to_a_comma()
    {
        Assert.Equal("Jonathan Allen", Signature.SenderName(Jonathan));
        Assert.Equal("Jonathan Allen", Signature.SenderName("Jonathan Allen\nThunder FC"));
        Assert.Equal("", Signature.SenderName(null));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("short", 1)]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Segments_are_counted_the_way_the_carrier_bills_them(object input, int expected)
    {
        var text = input is int length ? new string('x', length) : (string)input;
        Assert.Equal(expected, Signature.TextSegments(text));
    }
}
