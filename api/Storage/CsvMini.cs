namespace GameSwap.Functions.Storage;

using System.Text;

public static class CsvMini
{
    // Parses a CSV string into rows of columns.
    // Supports:
    // - comma separator
    // - quoted fields with escaped quotes ("")
    // - newlines inside quoted fields
    public static List<string[]> Parse(string csv)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrWhiteSpace(csv)) return rows;

        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < csv.Length; i++)
        {
            var c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // Escaped quote: ""
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\r')
                {
                    // ignore; handle newline on \n
                }
                else if (c == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row.ToArray());
                    row = new List<string>();
                }
                else
                {
                    field.Append(c);
                }
            }
        }

        // last field/row
        row.Add(field.ToString());
        rows.Add(row.ToArray());

        return rows;
    }

    // Creates a case-insensitive header index from the first row.
    // Example: "FieldName" becomes key "fieldname"
    public static Dictionary<string, int> HeaderIndex(string[] header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (header == null) return map;

        for (int i = 0; i < header.Length; i++)
        {
            var key = NormalizeHeaderKey(header[i]);
            if (string.IsNullOrWhiteSpace(key)) continue;

            if (!map.ContainsKey(key))
                map[key] = i;
        }

        return map;
    }

    // Gets a value from a row by header name (lowercase key).
    // Returns "" if missing.
    public static string Get(string[] row, Dictionary<string, int> headerIndex, string headerKeyLower)
    {
        if (row == null) return "";
        if (headerIndex == null) return "";
        if (string.IsNullOrWhiteSpace(headerKeyLower)) return "";

        var normalized = NormalizeHeaderKey(headerKeyLower);
        if (!headerIndex.TryGetValue(normalized, out var idx)) return "";
        if (idx < 0 || idx >= row.Length) return "";

        return row[idx] ?? "";
    }

    public static bool IsBlankRow(string[] row)
    {
        if (row == null) return true;
        foreach (var c in row)
        {
            if (!string.IsNullOrWhiteSpace(c)) return false;
        }
        return true;
    }

    private static string NormalizeHeaderKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "";
        var trimmed = key.Trim().ToLowerInvariant();
        return trimmed.Replace(" ", "").Replace("_", "").Replace("-", "");
    }
}
