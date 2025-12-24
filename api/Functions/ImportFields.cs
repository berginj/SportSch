using System.Net;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using GameSwap.Functions.Storage;

namespace GameSwap.Functions.Functions;

public class ImportFields
{
    private readonly ILogger _log;
    private readonly TableServiceClient _svc;

    public ImportFields(ILoggerFactory lf, TableServiceClient tableServiceClient)
    {
        _log = lf.CreateLogger<ImportFields>();
        _svc = tableServiceClient;
    }

    [Function("ImportFields")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "import/fields")] HttpRequestData req)
    {
        try
        {
            var leagueId = ApiGuards.RequireLeagueId(req);
            var me = IdentityUtil.GetMe(req);

            await ApiGuards.RequireLeagueAdminAsync(_svc, me.UserId, leagueId);

            var csvText = await CsvUpload.ReadCsvTextAsync(req);
            if (string.IsNullOrWhiteSpace(csvText))
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "Empty CSV body.");

            var rows = CsvMini.Parse(csvText);
            if (rows.Count < 2)
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST", "No CSV rows found.");

            var header = rows[0];
            if (header.Length > 0 && header[0] != null)
                header[0] = header[0].TrimStart('\uFEFF'); // strip BOM

            var idx = CsvMini.HeaderIndex(header);

            if (!idx.ContainsKey("fieldkey") || !idx.ContainsKey("parkname") || !idx.ContainsKey("fieldname"))
            {
                // Helpful debug: show what the importer thought the header row was
                var headerPreview = string.Join(",", header.Select(x => (x ?? "").Trim()).Take(12));
                return ApiResponses.Error(req, HttpStatusCode.BadRequest, "BAD_REQUEST",
                    "Missing required columns. Required: fieldKey, parkName, fieldName. Optional: displayName, address, notes, status (Active/Inactive).",
                    new { headerPreview });
            }

            var table = await TableClients.GetTableAsync(_svc, Constants.Tables.Fields);

            int upserted = 0, rejected = 0, skipped = 0;
            var errors = new List<object>();
            var actions = new List<TableTransactionAction>();

            for (int i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (CsvMini.IsBlankRow(r)) { skipped++; continue; }

                var fieldKeyRaw = CsvMini.Get(r, idx, "fieldkey").Trim();
                var parkName = CsvMini.Get(r, idx, "parkname").Trim();
                var fieldName = CsvMini.Get(r, idx, "fieldname").Trim();

                var displayName = CsvMini.Get(r, idx, "displayname").Trim();
                var address = CsvMini.Get(r, idx, "address").Trim();
                var notes = CsvMini.Get(r, idx, "notes").Trim();

                var statusRaw = CsvMini.Get(r, idx, "status").Trim();
                var isActiveRaw = CsvMini.Get(r, idx, "isactive").Trim();

                if (string.IsNullOrWhiteSpace(fieldKeyRaw) || string.IsNullOrWhiteSpace(parkName) || string.IsNullOrWhiteSpace(fieldName))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, fieldKey = fieldKeyRaw, error = "fieldKey, parkName, fieldName are required." });
                    continue;
                }

                if (!TryParseFieldKeyFlexible(fieldKeyRaw, parkName, fieldName, out var parkCode, out var fieldCode, out var normalizedFieldKey))
                {
                    rejected++;
                    errors.Add(new { row = i + 1, fieldKey = fieldKeyRaw, error = "Invalid fieldKey. Use parkCode/fieldCode or parkCode_fieldCode, or valid parkName/fieldName." });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(displayName))
                    displayName = $"{parkName} > {fieldName}";

                var isActive = ParseIsActive(statusRaw, isActiveRaw);

                var pk = Constants.Pk.Fields(leagueId, parkCode);
                var rk = fieldCode;

                notes = AppendOptionalFieldNotes(notes, r, idx);

                var entity = new TableEntity(pk, rk)
                {
                    ["LeagueId"] = leagueId,
                    ["FieldKey"] = normalizedFieldKey,
                    ["ParkCode"] = parkCode,
                    ["FieldCode"] = fieldCode,
                    ["ParkName"] = parkName,
                    ["FieldName"] = fieldName,
                    ["DisplayName"] = displayName,
                    ["Address"] = address,
                    ["Notes"] = notes,
                    ["IsActive"] = isActive,
                    ["UpdatedUtc"] = DateTimeOffset.UtcNow
                };

                actions.Add(new TableTransactionAction(TableTransactionActionType.UpsertMerge, entity));

                if (actions.Count == 100)
                {
                    var result = await table.SubmitTransactionAsync(actions);
                    upserted += result.Value.Count;
                    actions.Clear();
                }
            }

            if (actions.Count > 0)
            {
                var result = await table.SubmitTransactionAsync(actions);
                upserted += result.Value.Count;
            }

            return ApiResponses.Ok(req, new { leagueId, upserted, rejected, skipped, errors });
        }
        catch (ApiGuards.HttpError ex) { return ApiResponses.FromHttpError(req, ex); }
        catch (RequestFailedException ex)
        {
            var requestId = req.FunctionContext.InvocationId.ToString();
            _log.LogError(ex, "ImportFields storage request failed. requestId={requestId}", requestId);
            return ApiResponses.Error(
                req,
                HttpStatusCode.BadGateway,
                "STORAGE_ERROR",
                "Storage request failed.",
                new { requestId, status = ex.Status, code = ex.ErrorCode });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ImportFields failed");
            return ApiResponses.Error(
                req,
                HttpStatusCode.InternalServerError,
                "INTERNAL",
                "Internal Server Error",
                new { exception = ex.GetType().Name, message = ex.Message });
        }
    }

    private static async Task<string> ReadCsvTextAsync(HttpRequestData req)
    {
        // Read body ONCE so we can sniff it even if Content-Type is wrong.
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        var bodyBytes = ms.ToArray();
        if (bodyBytes.Length == 0) return "";

        var ct = GetHeader(req, "Content-Type");

        // Detect multipart either by Content-Type OR by body signature
        var looksMultipart =
            (!string.IsNullOrWhiteSpace(ct) && ct.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) ||
            BodyLooksMultipart(bodyBytes);

        if (looksMultipart)
        {
            var bytes = await MultipartFormData.ReadFirstFileBytesAsync(bodyBytes, ct, preferName: "file");
            var csv = Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>());
            return NormalizeNewlines(StripBom(csv));
        }

        // Raw CSV text
        return NormalizeNewlines(StripBom(Encoding.UTF8.GetString(bodyBytes)));
    }

    private static bool BodyLooksMultipart(byte[] body)
    {
        // Cheap heuristic: multipart bodies contain Content-Disposition and boundary-like starts
        var s = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 4096));
        return s.StartsWith("--") && s.Contains("Content-Disposition:", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHeader(HttpRequestData req, string name)
        => req.Headers.TryGetValues(name, out var vals) ? (vals.FirstOrDefault() ?? "") : "";

    private static string StripBom(string s) => (s ?? "").TrimStart('\uFEFF');

    private static string NormalizeNewlines(string s)
        => (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

    private static bool TryParseFieldKeyFlexible(string raw, string parkName, string fieldName,
        out string parkCode, out string fieldCode, out string normalizedFieldKey)
    {
        parkCode = ""; fieldCode = ""; normalizedFieldKey = "";
        var v = (raw ?? "").Trim().Trim('/', '\\');

        var slashParts = v.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (slashParts.Length == 2)
        {
            parkCode = Slug.Make(slashParts[0]);
            fieldCode = Slug.Make(slashParts[1]);
            if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode)) return false;
            normalizedFieldKey = $"{parkCode}/{fieldCode}";
            return true;
        }

        var us = v.Split('_', 2, StringSplitOptions.TrimEntries);
        if (us.Length == 2)
        {
            parkCode = Slug.Make(us[0]);
            fieldCode = Slug.Make(us[1]);
            if (!string.IsNullOrWhiteSpace(parkCode) && !string.IsNullOrWhiteSpace(fieldCode))
            {
                normalizedFieldKey = $"{parkCode}/{fieldCode}";
                return true;
            }
        }

        parkCode = Slug.Make(parkName);
        fieldCode = Slug.Make(fieldName);
        if (string.IsNullOrWhiteSpace(parkCode) || string.IsNullOrWhiteSpace(fieldCode)) return false;

        normalizedFieldKey = $"{parkCode}/{fieldCode}";
        return true;
    }

    private static bool ParseIsActive(string statusRaw, string isActiveRaw)
    {
        if (!string.IsNullOrWhiteSpace(statusRaw))
        {
            var s = statusRaw.Trim();
            if (string.Equals(s, Constants.Status.FieldInactive, StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(s, Constants.Status.FieldActive, StringComparison.OrdinalIgnoreCase)) return true;
        }

        if (!string.IsNullOrWhiteSpace(isActiveRaw) && bool.TryParse(isActiveRaw, out var b))
            return b;

        return true;
    }

    private static string AppendOptionalFieldNotes(string existingNotes, string[] row, Dictionary<string, int> headerIndex)
    {
        var notes = (existingNotes ?? "").Trim();
        var extras = new List<string>();
        string GetOpt(string key) => CsvMini.Get(row, headerIndex, key).Trim();

        var lights = GetOpt("lights");
        if (!string.IsNullOrWhiteSpace(lights)) extras.Add($"Lights: {lights}");

        var cage = GetOpt("battingcage");
        if (!string.IsNullOrWhiteSpace(cage)) extras.Add($"Batting cage: {cage}");

        var mound = GetOpt("portablemound");
        if (!string.IsNullOrWhiteSpace(mound)) extras.Add($"Portable mound: {mound}");

        var lockCode = GetOpt("fieldlockcode");
        if (!string.IsNullOrWhiteSpace(lockCode)) extras.Add($"Lock code: {lockCode}");

        var fieldNotes = GetOpt("fieldnotes");
        if (!string.IsNullOrWhiteSpace(fieldNotes)) extras.Add(fieldNotes);

        if (extras.Count == 0) return notes;

        var extraText = string.Join(" | ", extras);
        if (string.IsNullOrWhiteSpace(notes)) return extraText;
        if (notes.Contains(extraText, StringComparison.OrdinalIgnoreCase)) return notes;
        return $"{notes} | {extraText}";
    }

    private static class MultipartFormData
    {
        public static Task<byte[]?> ReadFirstFileBytesAsync(byte[] body, string contentType, string preferName)
        {
            var boundary = ExtractBoundary(contentType);

            // If Content-Type was wrong, try to infer boundary from the first line: --BOUNDARY
            if (string.IsNullOrWhiteSpace(boundary))
            {
                var firstLineEnd = IndexOf(body, Encoding.UTF8.GetBytes("\r\n"), 0);
                if (firstLineEnd > 2)
                {
                    var firstLine = Encoding.UTF8.GetString(body, 0, firstLineEnd).Trim();
                    if (firstLine.StartsWith("--") && firstLine.Length > 4)
                        boundary = firstLine.Substring(2);
                }
            }

            if (string.IsNullOrWhiteSpace(boundary)) return Task.FromResult<byte[]?>(null);

            var boundaryBytes = Encoding.UTF8.GetBytes("--" + boundary);
            var endBoundaryBytes = Encoding.UTF8.GetBytes("--" + boundary + "--");
            var parts = SplitMultipart(body, boundaryBytes, endBoundaryBytes);

            // Preferred name first
            foreach (var part in parts)
            {
                var file = TryGetFilePart(part, preferNameOnly: true, preferName: preferName);
                if (file != null) return Task.FromResult<byte[]?>(file);
            }

            // Any file
            foreach (var part in parts)
            {
                var file = TryGetFilePart(part, preferNameOnly: false, preferName: preferName);
                if (file != null) return Task.FromResult<byte[]?>(file);
            }

            return Task.FromResult<byte[]?>(null);
        }

        private static byte[]? TryGetFilePart(byte[] part, bool preferNameOnly, string preferName)
        {
            var headerEnd = IndexOf(part, Encoding.UTF8.GetBytes("\r\n\r\n"), 0);
            if (headerEnd < 0) return null;

            var headerText = Encoding.UTF8.GetString(part, 0, headerEnd);
            var contentStart = headerEnd + 4;

            var cd = GetContentDisposition(headerText);
            if (cd == null) return null;

            var name = cd.Value.name ?? "";
            var filename = cd.Value.filename ?? "";
            if (string.IsNullOrWhiteSpace(filename)) return null;

            if (preferNameOnly && !string.Equals(name, preferName, StringComparison.OrdinalIgnoreCase))
                return null;

            var content = part.AsSpan(contentStart).ToArray();
            if (content.Length >= 2 && content[^2] == (byte)'\r' && content[^1] == (byte)'\n')
                content = content[..^2];
            return content;
        }

        private static string ExtractBoundary(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType)) return "";
            var m = Regex.Match(contentType, @"boundary=(?:""(?<b>[^""]+)""|(?<b>[^;]+))", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["b"].Value.Trim() : "";
        }

        private static (string? name, string? filename)? GetContentDisposition(string headers)
        {
            var lines = headers.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (!line.StartsWith("Content-Disposition:", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Regex.Match(line, @"name=(?:""(?<n>[^""]+)""|(?<n>[^;]+))", RegexOptions.IgnoreCase);
                var fn = Regex.Match(line, @"filename=(?:""(?<f>[^""]+)""|(?<f>[^;]+))", RegexOptions.IgnoreCase);
                return (name.Success ? name.Groups["n"].Value : null, fn.Success ? fn.Groups["f"].Value : null);
            }
            return null;
        }

        private static List<byte[]> SplitMultipart(byte[] body, byte[] boundary, byte[] endBoundary)
        {
            var parts = new List<byte[]>();
            int pos = IndexOf(body, boundary, 0);
            if (pos < 0) return parts;

            while (pos >= 0)
            {
                pos += boundary.Length;
                if (pos + 2 <= body.Length && body[pos] == (byte)'\r' && body[pos + 1] == (byte)'\n') pos += 2;

                if (pos - boundary.Length >= 0 && StartsWithAt(body, pos - boundary.Length, endBoundary))
                    break;

                var next = IndexOf(body, boundary, pos);
                var nextEnd = IndexOf(body, endBoundary, pos);

                int cut = (nextEnd >= 0 && (next < 0 || nextEnd < next)) ? nextEnd : next;
                if (cut < 0) break;

                var len = cut - pos;
                if (len > 0)
                {
                    var part = new byte[len];
                    Buffer.BlockCopy(body, pos, part, 0, len);
                    parts.Add(part);
                }
                pos = cut;
            }
            return parts;
        }

        private static bool StartsWithAt(byte[] haystack, int start, byte[] needle)
        {
            if (start < 0 || start + needle.Length > haystack.Length) return false;
            for (int i = 0; i < needle.Length; i++)
                if (haystack[start + i] != needle[i]) return false;
            return true;
        }

        private static int IndexOf(byte[] haystack, byte[] needle, int start)
        {
            if (needle.Length == 0) return -1;
            for (int i = start; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }
    }
}
