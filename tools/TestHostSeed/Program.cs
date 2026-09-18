using Azure.Data.Tables;
using GameSwap.Functions.Storage;

// Emulator only. Never accept storage credentials or a remote endpoint.
var service = new TableServiceClient("UseDevelopmentStorage=true");
foreach (var field in typeof(Constants.Tables).GetFields().Where(f => f.IsLiteral && f.FieldType == typeof(string)))
    await service.GetTableClient((string)field.GetRawConstantValue()!).CreateIfNotExistsAsync();

foreach (var league in new[] { "e2e-league-a", "e2e-league-b" })
{
    await Save(Constants.Tables.Leagues, new TableEntity(Constants.Pk.Leagues, league)
    {
        ["Name"] = league, ["IsActive"] = true, ["SpringStart"] = "2026-01-01", ["SpringEnd"] = "2026-12-31"
    });
    foreach (var role in new[] { "Coach", "LeagueAdmin", "Viewer" })
        await Save(Constants.Tables.Memberships, new TableEntity("e2e-" + role, league)
        {
            ["Role"] = role, ["Division"] = "10U", ["TeamId"] = "HOME", ["Email"] = role + "@example.test"
        });
    await Save(Constants.Tables.Divisions, new TableEntity("DIV|" + league, "10U")
        { ["Code"] = "10U", ["Name"] = "Under 10", ["IsActive"] = true });
    foreach (var team in new[] { "HOME", "AWAY" })
        await Save(Constants.Tables.Teams, new TableEntity("TEAM|" + league + "|10U", team)
            { ["TeamId"] = team, ["Division"] = "10U", ["Name"] = team, ["IsActive"] = true });
    await Save(Constants.Tables.Fields, new TableEntity("FIELD|" + league + "|PARK", "ONE")
        { ["ParkName"] = "Test park", ["FieldName"] = "Diamond 1", ["IsActive"] = true });
    await Save(Constants.Tables.Slots, new TableEntity(Constants.Pk.Slots(league, "10U"), "e2e-game")
    {
        ["LeagueId"] = league, ["Division"] = "10U", ["OfferingTeamId"] = "HOME", ["HomeTeamId"] = "HOME",
        ["ConfirmedTeamId"] = "AWAY", ["AwayTeamId"] = "AWAY", ["Status"] = "Confirmed", ["GameType"] = "Game",
        ["GameDate"] = "2026-10-10", ["StartTime"] = "18:00", ["EndTime"] = "19:30", ["StartMin"] = 1080,
        ["EndMin"] = 1170, ["IsAvailability"] = false, ["FieldKey"] = "PARK/ONE", ["FieldName"] = "Diamond 1"
    });
}
Console.WriteLine("Local E2E leagues seeded.");

async Task Save(string table, TableEntity row) => await service.GetTableClient(table).UpsertEntityAsync(row, TableUpdateMode.Replace);
