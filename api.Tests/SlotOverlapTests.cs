using GameSwap.Functions.Storage;
using Xunit;

namespace GameSwap.Tests;

public class SlotOverlapTests
{
    [Fact]
    public void ParseMinutes_ParsesValidAndInvalid()
    {
        Assert.Equal(450, SlotOverlap.ParseMinutes("07:30"));
        Assert.Equal(-1, SlotOverlap.ParseMinutes("bad"));
    }

    [Fact]
    public void TryParseMinutesRange_ValidatesOrder()
    {
        Assert.True(SlotOverlap.TryParseMinutesRange("18:00", "19:00", out var start, out var end));
        Assert.Equal(1080, start);
        Assert.Equal(1140, end);
        Assert.False(SlotOverlap.TryParseMinutesRange("19:00", "19:00", out _, out _));
        Assert.False(SlotOverlap.TryParseMinutesRange("19:00", "18:00", out _, out _));
    }

    [Fact]
    public void AddRange_RejectsOverlaps()
    {
        var ranges = new Dictionary<string, List<(int startMin, int endMin)>>();
        var key = SlotOverlap.BuildRangeKey("park/field", "2026-04-01");

        Assert.True(SlotOverlap.AddRange(ranges, key, 600, 660)); // 10:00-11:00
        Assert.True(SlotOverlap.AddRange(ranges, key, 660, 720)); // 11:00-12:00
        Assert.False(SlotOverlap.AddRange(ranges, key, 659, 700)); // overlaps
        Assert.True(SlotOverlap.HasOverlap(ranges, key, 630, 645));
        Assert.False(SlotOverlap.HasOverlap(ranges, key, 720, 780));
    }

    [Fact]
    public void BuildRangeKey_UsesDateOnly()
    {
        var key = SlotOverlap.BuildRangeKey("park/field", new DateOnly(2026, 4, 1));
        Assert.Equal("park/field|2026-04-01", key);
    }
}
