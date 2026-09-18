using System;

namespace GameSwap.Tests;

internal sealed class FixedClock : TimeProvider
{
    public static readonly DateTimeOffset Instant = new(2026, 7, 10, 16, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => Instant;
    public static DateTime EasternNow => TimeZoneInfo.ConvertTime(Instant, GameSwap.Functions.Storage.ScheduleTime.Eastern).DateTime;
}
