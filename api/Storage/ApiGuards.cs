using System.Net;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker.Http;

namespace GameSwap.Functions.Storage;

public static class ApiGuards
{
    // ==== Tables (public because other funcs reference it) ====
    // Prefer Storage.Constants for any new work.
    public const string MembershipsTableName = Constants.Tables.Memberships;
    public const string GlobalAdminsTableName = Constants.Tables.GlobalAdmins; // PK="GLOBAL", RK=userId

    // ==== Exceptions ====
    public sealed class HttpError : Exception
    {
        public int Status { get; }

        public HttpError(int status, string message) : base(message)
        {
            Status = status;
        }
    }

    // ==== LeagueId parsing ====
    // League scoping is header-based. Do not put leagueId in path/query for league-scoped routes.
    public static string RequireLeagueId(HttpRequestData req)
    {
        var headerLeagueId = req.Headers.TryGetValues(Constants.LEAGUE_HEADER_NAME, out var vals)
            ? (vals.FirstOrDefault() ?? "").Trim()
            : "";

        if (string.IsNullOrWhiteSpace(headerLeagueId))
            throw new HttpError((int)HttpStatusCode.BadRequest,
                $"Missing league scope header. Send {Constants.LEAGUE_HEADER_NAME}: <leagueId>.");
        EnsureValidTableKeyPart("leagueId", headerLeagueId);

        return headerLeagueId;
    }

    // ==== Membership gates ====
    public static async Task RequireMemberAsync(TableServiceClient svc, string userId, string leagueId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN")
            throw new HttpError((int)HttpStatusCode.Unauthorized, "Not authenticated.");

        if (!await IsMemberAsync(svc, userId, leagueId))
            throw new HttpError((int)HttpStatusCode.Forbidden, "Forbidden");
    }

    public static async Task RequireMemberAsync(TableServiceClient svc, IdentityUtil.Me me, string leagueId)
    {
        if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            throw new HttpError((int)HttpStatusCode.Unauthorized, "Not authenticated.");

        if (!await IsMemberAsync(svc, me.UserId, leagueId))
            throw new HttpError((int)HttpStatusCode.Forbidden, "Forbidden");
    }

    public static async Task<bool> IsMemberAsync(TableServiceClient svc, string userId, string leagueId)
    {
        var mem = await GetMembershipAsync(svc, userId, leagueId);
        return mem is not null;
    }

    public static async Task<TableEntity?> GetMembershipAsync(TableServiceClient svc, string userId, string leagueId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN") return null;
        if (string.IsNullOrWhiteSpace(leagueId)) return null;

        var table = await TableClients.GetTableAsync(svc, MembershipsTableName);
        try
        {
            var resp = await table.GetEntityAsync<TableEntity>(userId, leagueId);
            return resp.Value;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return null;
        }
    }

    // ==== Role helpers ====
    public static string GetRole(TableEntity? membershipEntity)
        => (membershipEntity?.GetString("Role") ?? "").Trim();

    public static (string division, string teamId) GetCoachTeam(TableEntity? membershipEntity)
    {
        var division = (membershipEntity?.GetString("Division") ?? "").Trim();
        var teamId = (membershipEntity?.GetString("TeamId") ?? "").Trim();
        return (division, teamId);
    }

    public static async Task RequireNotViewerAsync(TableServiceClient svc, string userId, string leagueId)
    {
        if (await IsGlobalAdminAsync(svc, userId)) return;
        await RequireMemberAsync(svc, userId, leagueId);

        var mem = await GetMembershipAsync(svc, userId, leagueId);
        var role = GetRole(mem);
        if (string.Equals(role, Constants.Roles.Viewer, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(role))
            throw new HttpError((int)HttpStatusCode.Forbidden, "Forbidden");
    }

    public static async Task RequireLeagueAdminAsync(TableServiceClient svc, string userId, string leagueId)
    {
        if (await IsGlobalAdminAsync(svc, userId)) return;
        await RequireMemberAsync(svc, userId, leagueId);

        var mem = await GetMembershipAsync(svc, userId, leagueId);
        var role = GetRole(mem);
        if (!string.Equals(role, Constants.Roles.LeagueAdmin, StringComparison.OrdinalIgnoreCase))
            throw new HttpError((int)HttpStatusCode.Forbidden, "Forbidden");
    }

    // ==== Global admin gates ====
    public static async Task RequireGlobalAdminAsync(TableServiceClient svc, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN")
            throw new HttpError((int)HttpStatusCode.Unauthorized, "Not authenticated.");

        if (!await IsGlobalAdminAsync(svc, userId))
            throw new HttpError((int)HttpStatusCode.Forbidden, "Forbidden");
    }

    public static async Task RequireGlobalAdminAsync(TableServiceClient svc, IdentityUtil.Me me)
    {
        if (string.IsNullOrWhiteSpace(me.UserId) || me.UserId == "UNKNOWN")
            throw new HttpError((int)HttpStatusCode.Unauthorized, "Not authenticated.");

        if (!await IsGlobalAdminAsync(svc, me.UserId))
            throw new HttpError((int)HttpStatusCode.Forbidden, "Forbidden");
    }

    public static async Task<bool> IsGlobalAdminAsync(TableServiceClient svc, string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId == "UNKNOWN") return false;

        var table = await TableClients.GetTableAsync(svc, GlobalAdminsTableName);
        try
        {
            _ = await table.GetEntityAsync<TableEntity>(Constants.Pk.GlobalAdmins, userId);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    // ==== Query helpers ====
    public static string? GetQueryParam(HttpRequestData req, string key)
    {
        if (string.IsNullOrWhiteSpace(req?.Url?.Query)) return null;

        var q = req.Url.Query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(q)) return null;

        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 0) continue;

            var k = Uri.UnescapeDataString(kv[0] ?? "");
            if (!string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) continue;

            var v = kv.Length == 2 ? Uri.UnescapeDataString(kv[1] ?? "") : "";
            return v;
        }

        return null;
    }

    // ==== Table key validation ====
    public static bool HasInvalidTableKeyChars(string value)
        => !string.IsNullOrEmpty(value) && value.Any(c => c < 0x20 || c == '/' || c == '\\' || c == '#' || c == '?');

    public static string InvalidTableKeyCharsMessage => "/, \\\\, #, ?, or control characters";

    public static void EnsureValidTableKeyPart(string name, string value)
    {
        if (HasInvalidTableKeyChars(value))
            throw new HttpError((int)HttpStatusCode.BadRequest,
                $"{name} contains invalid characters. Table keys cannot contain: {InvalidTableKeyCharsMessage}.");
    }

    // Back-compat alias (some of your funcs referenced GetQueryValue earlier)
    public static string? GetQueryValue(HttpRequestData req, string key) => GetQueryParam(req, key);

    public static string EscapeOData(string s) => (s ?? "").Replace("'", "''");
}
