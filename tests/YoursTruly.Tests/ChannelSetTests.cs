using YoursTruly.Core.Domain;

namespace YoursTruly.Tests;

public sealed class ChannelSetTests
{
    [Fact]
    public void An_empty_set_is_how_not_decided_yet_is_said()
    {
        Assert.True(ChannelSet.None.IsEmpty);
        Assert.Equal(0, ChannelSet.None.Count);
        Assert.Equal(Channel.None, ChannelSet.None.First);
        Assert.False(ChannelSet.None.Has(Channel.Email));
    }

    [Fact]
    public void None_is_never_a_member_of_anything()
    {
        // It means "none of them", so putting it in a set is meaningless rather than
        // an error worth throwing over.
        var set = ChannelSet.Of(Channel.None);
        Assert.True(set.IsEmpty);
        Assert.False(set.Has(Channel.None));
    }

    [Fact]
    public void Ticking_a_channel_twice_unticks_it()
    {
        var set = ChannelSet.None.Toggle(Channel.Text);
        Assert.True(set.Has(Channel.Text));
        Assert.True(set.Toggle(Channel.Text).IsEmpty);
    }

    [Fact]
    public void The_same_pair_comes_out_in_the_same_order_however_it_went_in()
    {
        var one = ChannelSet.Of(Channel.Text, Channel.Email);
        var other = ChannelSet.Of(Channel.Email, Channel.Text);

        Assert.Equal(one, other);
        Assert.Equal([Channel.Email, Channel.Text], one.Ordered);
        Assert.Equal([Channel.Email, Channel.Text], other.Ordered);
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("email", 1)]
    [InlineData("email,text", 2)]
    [InlineData("voice,email,text", 3)]
    public void A_set_survives_being_written_down_and_read_back(string wire, int count)
    {
        var set = ChannelSet.FromWire(wire);
        Assert.Equal(count, set.Count);
        Assert.Equal(set, ChannelSet.FromWire(set.ToWire()));
    }

    [Fact]
    public void A_single_channel_name_reads_as_the_set_containing_it()
    {
        // Which is what every row held before a person could choose more than one.
        Assert.Equal(ChannelSet.Of(Channel.Text), ChannelSet.FromWire("text"));

        // And "none" was how "not decided yet" used to be written.
        Assert.True(ChannelSet.FromWire("none").IsEmpty);
    }

    [Fact]
    public void A_name_nobody_recognises_is_dropped_rather_than_read_as_something_else()
    {
        Assert.True(ChannelSet.FromWire("carrier-pigeon").IsEmpty);
        Assert.Equal(ChannelSet.Of(Channel.Email), ChannelSet.FromWire("email,carrier-pigeon"));
    }

    [Fact]
    public void A_channel_the_app_cannot_send_on_yet_still_reads_back_whole()
    {
        // Losing a member on the way through would quietly change what somebody chose.
        var set = ChannelSet.FromWire("email,whatsapp");
        Assert.Equal(2, set.Count);
        Assert.Equal("email,whatsapp", set.ToWire());
    }

    [Theory]
    [InlineData("", "No channel chosen")]
    [InlineData("email", "Email")]
    [InlineData("email,text", "Email and text")]
    [InlineData("email,text,voice", "Email, text and phone call")]
    public void A_set_says_itself_in_words(string wire, string expected) =>
        Assert.Equal(expected, ChannelSet.FromWire(wire).Label);
}
