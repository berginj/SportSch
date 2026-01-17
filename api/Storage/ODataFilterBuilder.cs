namespace GameSwap.Functions.Storage;

/// <summary>
/// Builds OData filter strings for Azure Table Storage queries.
/// </summary>
public static class ODataFilterBuilder
{
    /// <summary>
    /// Creates a filter for partition keys starting with a prefix (range query).
    /// Example: PartitionKey >= 'SLOT|league|' AND PartitionKey < 'SLOT|league|\uffff'
    /// </summary>
    public static string PartitionKeyPrefix(string prefix)
    {
        var next = prefix + "\uffff";
        return $"PartitionKey ge '{ApiGuards.EscapeOData(prefix)}' and PartitionKey lt '{ApiGuards.EscapeOData(next)}'";
    }

    /// <summary>
    /// Creates an exact partition key filter.
    /// </summary>
    public static string PartitionKeyExact(string pk)
    {
        return $"PartitionKey eq '{ApiGuards.EscapeOData(pk)}'";
    }

    /// <summary>
    /// Creates a row key filter.
    /// </summary>
    public static string RowKeyEquals(string rk)
    {
        return $"RowKey eq '{ApiGuards.EscapeOData(rk)}'";
    }

    /// <summary>
    /// Creates an equality filter for a property.
    /// </summary>
    public static string PropertyEquals(string propertyName, string value)
    {
        return $"{propertyName} eq '{ApiGuards.EscapeOData(value)}'";
    }

    /// <summary>
    /// Creates a date range filter (inclusive).
    /// </summary>
    public static string DateRange(string dateField, string? fromDate, string? toDate)
    {
        var filters = new List<string>();

        if (!string.IsNullOrWhiteSpace(fromDate))
            filters.Add($"{dateField} ge '{ApiGuards.EscapeOData(fromDate)}'");

        if (!string.IsNullOrWhiteSpace(toDate))
            filters.Add($"{dateField} le '{ApiGuards.EscapeOData(toDate)}'");

        return And(filters.ToArray());
    }

    /// <summary>
    /// Creates a status filter.
    /// </summary>
    public static string StatusEquals(string status)
    {
        return $"Status eq '{ApiGuards.EscapeOData(status)}'";
    }

    /// <summary>
    /// Combines multiple filters with AND operator.
    /// </summary>
    public static string And(params string[] filters)
    {
        var nonEmpty = filters.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (nonEmpty.Count == 0) return "";
        if (nonEmpty.Count == 1) return nonEmpty[0];
        return string.Join(" and ", nonEmpty.Select(f => $"({f})"));
    }

    /// <summary>
    /// Combines multiple filters with OR operator.
    /// </summary>
    public static string Or(params string[] filters)
    {
        var nonEmpty = filters.Where(f => !string.IsNullOrWhiteSpace(f)).ToList();
        if (nonEmpty.Count == 0) return "";
        if (nonEmpty.Count == 1) return nonEmpty[0];
        return string.Join(" or ", nonEmpty.Select(f => $"({f})"));
    }
}
