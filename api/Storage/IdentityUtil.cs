using System.Text;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using System.Linq;

namespace GameSwap.Functions.Storage;

public static class IdentityUtil
{
    public sealed record Me(string UserId, string Email);

    public static Me GetMe(HttpRequestData req)
    {
        // 1) Static Web Apps / EasyAuth principal header (best)
        // Support both common casings.
        if (TryGetHeader(req, "x-ms-client-principal", out var encoded) ||
            TryGetHeader(req, "X-MS-CLIENT-PRINCIPAL", out encoded))
        {
            if (!string.IsNullOrWhiteSpace(encoded))
            {
                try
                {
                    var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    // Prefer canonical SWA root fields first (most reliable)
                    var userId = TryGetString(root, "userId");
                    var userDetails = TryGetString(root, "userDetails"); // often email/username

                    // Then fall back to claims if needed
                    var claims = root.TryGetProperty("claims", out var c) ? c : default;

                    string? FindClaim(params string[] types)
                    {
                        if (claims.ValueKind != JsonValueKind.Array) return null;
                        foreach (var item in claims.EnumerateArray())
                        {
                            var typ = item.TryGetProperty("typ", out var t) ? t.GetString() : null;
                            var val = item.TryGetProperty("val", out var v) ? v.GetString() : null;
                            if (typ != null && val != null && types.Contains(typ)) return val;
                        }
                        return null;
                    }

                    userId ??=
                        FindClaim(
                            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier",
                            "nameidentifier",
                            "sub"
                        );

                    var email = userDetails;
                    email ??=
                        FindClaim(
                            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
                            "emails",
                            "email",
                            "preferred_username",
                            "upn"
                        );

                    return new Me(
                        string.IsNullOrWhiteSpace(userId) ? "UNKNOWN" : userId!,
                        string.IsNullOrWhiteSpace(email) ? "UNKNOWN" : email!
                    );
                }
                catch
                {
                    // fall through to dev headers
                }
            }
        }

        // 2) Fallback headers (useful for local/dev/testing)
        var userIdFallback = req.Headers.TryGetValues("x-user-id", out var ids) ? ids.FirstOrDefault() : null;
        var emailFallback = req.Headers.TryGetValues("x-user-email", out var emails) ? emails.FirstOrDefault() : null;

        return new Me(userIdFallback ?? "UNKNOWN", emailFallback ?? "UNKNOWN");
    }

    private static bool TryGetHeader(HttpRequestData req, string name, out string? value)
    {
        value = null;
        if (req.Headers.TryGetValues(name, out var values))
        {
            value = values.FirstOrDefault();
            return true;
        }
        return false;
    }

    private static string? TryGetString(JsonElement root, string prop)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        if (!root.TryGetProperty(prop, out var el)) return null;
        if (el.ValueKind != JsonValueKind.String) return null;
        return el.GetString();
    }
}
