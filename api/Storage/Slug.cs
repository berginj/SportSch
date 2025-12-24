using System.Linq;
namespace GameSwap.Functions.Storage;

public static class Slug
{
    public static string Make(string s)
    {
        s = (s ?? "").Trim().ToLowerInvariant();
        var chars = s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray();
        var x = new string(chars);
        while (x.Contains("--")) x = x.Replace("--", "-");
        return x.Trim('-');
    }
}
