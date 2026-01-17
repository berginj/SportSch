namespace GameSwap.Functions.Storage;

/// <summary>
/// Utilities for parsing and working with field keys in format "parkCode/fieldCode".
/// </summary>
public static class FieldKeyUtil
{
    /// <summary>
    /// Parses a field key in format "parkCode/fieldCode" into its components.
    /// </summary>
    /// <param name="raw">Field key string (e.g., "park-name/field-1")</param>
    /// <param name="parkCode">Output slugified park code</param>
    /// <param name="fieldCode">Output slugified field code</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParseFieldKey(string? raw, out string parkCode, out string fieldCode)
    {
        parkCode = "";
        fieldCode = "";
        var v = (raw ?? "").Trim().Trim('/');
        var parts = v.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2) return false;

        parkCode = Slug.Make(parts[0]);
        fieldCode = Slug.Make(parts[1]);
        return !string.IsNullOrWhiteSpace(parkCode) && !string.IsNullOrWhiteSpace(fieldCode);
    }

    /// <summary>
    /// Normalizes a field key by slugifying both parts and joining with "/".
    /// </summary>
    public static string NormalizeFieldKey(string parkCode, string fieldCode)
    {
        var normalizedPark = Slug.Make(parkCode);
        var normalizedField = Slug.Make(fieldCode);
        return $"{normalizedPark}/{normalizedField}";
    }

    /// <summary>
    /// Converts park and field codes to table storage partition key.
    /// </summary>
    public static string ToTableKey(string leagueId, string parkCode)
    {
        return Constants.Pk.Fields(leagueId, parkCode);
    }
}
