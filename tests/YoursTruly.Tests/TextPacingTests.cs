using YoursTruly.Core.Domain;

namespace YoursTruly.Tests;

public sealed class TextPacingTests
{
    [Fact]
    public void Every_gap_is_between_two_and_eleven_seconds_and_the_whole_range_gets_used()
    {
        var random = new Random(1);
        var gaps = Enumerable.Range(0, 5000).Select(_ => TextPacing.NextGap(random)).ToList();

        Assert.All(gaps, g => Assert.InRange(g.TotalMilliseconds, 2000, 11000));
        Assert.True(gaps.Min() < TimeSpan.FromSeconds(2.1), "never came close to the shortest gap");
        Assert.True(gaps.Max() > TimeSpan.FromSeconds(10.9), "never came close to the longest gap");
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 6.5)]
    [InlineData(41, 260)]
    public void The_estimate_is_an_average_gap_before_every_text_but_the_first(int texts, double seconds) =>
        Assert.Equal(TimeSpan.FromSeconds(seconds), TextPacing.Estimate(texts));

    [Theory]
    [InlineData(6.5, "about 5 seconds")]
    [InlineData(32.5, "about 30 seconds")]
    [InlineData(65, "about a minute")]
    [InlineData(260, "about 4 minutes")]
    public void The_estimate_is_said_the_way_people_say_it(double seconds, string said) =>
        Assert.Equal(said, TextPacing.Describe(TimeSpan.FromSeconds(seconds)));
}
