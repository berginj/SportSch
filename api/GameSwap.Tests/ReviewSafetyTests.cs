using System;
using System.Linq;
using System.Threading.Tasks;
using Azure.Data.Tables;
using GameSwap.Functions.Repositories;
using GameSwap.Functions.Services;
using GameSwap.Functions.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameSwap.Tests;

public class ReviewSafetyTests
{
    [Theory]
    [InlineData("Umpire")]
    [InlineData("Viewer")]
    [InlineData("FutureRole")]
    [InlineData("")]
    public async Task NonCoachRolesCannotCreateOrEditSlots(string role)
    {
        var memberships = new Mock<IMembershipRepository>();
        memberships.Setup(x => x.GetMembershipAsync("user", "league")).ReturnsAsync(
            new TableEntity("user", "league") { ["Role"] = role, ["TeamId"] = "team", ["Division"] = "10U" });
        var service = new AuthorizationService(memberships.Object, Mock.Of<ISlotRepository>(), NullLogger<AuthorizationService>.Instance);
        Assert.False(await service.CanCreateSlotAsync("user", "league", "10U", "team"));
        Assert.False(await service.CanUpdateSlotAsync("user", "league", "10U", "slot"));
        memberships.Verify(x => x.GetMembershipAsync("user", "league"), Times.Once);
    }

    [Fact]
    public async Task CorrectnessQueryIncludesLaterPages()
    {
        var repository = new Mock<ISlotRepository>();
        var filter = new SlotQueryFilter { LeagueId = "league", PageSize = 100 };
        repository.Setup(x => x.QuerySlotsAsync(filter, null)).ReturnsAsync(new PaginationResult<TableEntity>
            { Items = new() { new("SLOT|league|10U", "first") }, ContinuationToken = "second", PageSize = 100 });
        repository.Setup(x => x.QuerySlotsAsync(filter, "second")).ReturnsAsync(new PaginationResult<TableEntity>
            { Items = new() { new("SLOT|league|10U", "conflict") }, PageSize = 100 });
        Assert.Equal(new[] { "first", "conflict" }, (await repository.Object.QueryAllSlotsAsync(filter)).Select(x => x.RowKey));
    }

    [Theory]
    [InlineData("2026-07-10", "18:00", "2026-07-10T22:00:00Z")]
    [InlineData("2026-01-10", "18:00", "2026-01-10T23:00:00Z")]
    public void EasternTimeUsesSeasonalOffset(string date, string time, string expected)
    {
        Assert.True(ScheduleTime.TryGetUtc(date, time, out var instant));
        Assert.Equal(DateTimeOffset.Parse(expected), instant);
        ScheduleTime.RequireLeadTime(date, time, 72, instant.AddHours(-72));
        Assert.Throws<ApiGuards.HttpError>(() => ScheduleTime.RequireLeadTime(date, time, 72, instant.AddHours(-71)));
        Assert.Throws<ApiGuards.HttpError>(() => ScheduleTime.RequireLeadTime(date, time, 72, instant.AddHours(1)));
    }

    [Theory]
    [InlineData("2026-03-08", "02:30")]
    [InlineData("2026-11-01", "01:30")]
    [InlineData("not-a-date", "18:00")]
    public void InvalidOrAmbiguousTimesFailClosed(string date, string time)
        => Assert.False(ScheduleTime.TryGetUtc(date, time, out _));

    [Theory]
    [InlineData("leagueId")]
    [InlineData("division")]
    public void CompositeScopeRejectsReservedSeparator(string name)
        => Assert.Throws<ApiGuards.HttpError>(() => ApiGuards.EnsureValidTableKeyPart(name, "A|B"));
}
