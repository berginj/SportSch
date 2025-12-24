using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker.Http;

namespace GameSwap.Functions.Storage;

public static class CsvUpload
{
    public static async Task<string> ReadCsvTextAsync(HttpRequestData req, string preferredFormFieldName = "file")
    {
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        var bodyBytes = ms.ToArray();
        if (bodyBytes.Length == 0) return "";

        var contentType = GetHeader(req, "Content-Type");

        var looksMultipart =
            (!string.IsNullOrWhiteSpace(contentType) &&
             contentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase)) ||
            BodyLooksMultipart(bodyBytes);

        if (looksMultipart)
        {
            var bytes = await MultipartFormData.ReadFirstFileBytesAsync(bodyBytes, contentType, preferredFormFieldName);
            var csv = Encoding.UTF8.GetString(bytes ?? Array.Empty<byte>());
            return NormalizeNewlines(StripBom(csv));
        }

        return NormalizeNewlines(StripBom(Encoding.UTF8.GetString(bodyBytes)));
    }

    private static bool BodyLooksMultipart(byte[] body)
    {
        var s = Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 4096));
        return s.StartsWith("--") && s.Contains("Content-Disposition:", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetHeader(HttpRequestData req, string name)
        => req.Headers.TryGetValues(name, out var vals) ? (vals.FirstOrDefault() ?? "") : "";

    private static string StripBom(string s) => (s ?? "").TrimStart('\uFEFF');

    private static string NormalizeNewlines(string s)
        => (s ?? "").Replace("\r\n", "\n").Replace("\r", "\n");

    private static class MultipartFormData
    {
        public static Task<byte[]?> ReadFirstFileBytesAsync(byte[] body, string contentType, string preferName)
        {
            var boundary = ExtractBoundary(contentType);

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

            foreach (var part in parts)
            {
                var file = TryGetFilePart(part, preferNameOnly: true, preferName: preferName);
                if (file != null) return Task.FromResult<byte[]?>(file);
            }

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
