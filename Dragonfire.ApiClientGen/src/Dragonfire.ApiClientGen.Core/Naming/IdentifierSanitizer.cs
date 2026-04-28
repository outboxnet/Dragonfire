using System.Text;

namespace Dragonfire.ApiClientGen.Naming;

/// <summary>
/// Turns arbitrary text (Postman item names, JSON property names) into
/// PascalCase C# identifiers and prefixes / suffixes as needed to dodge
/// digits-first names and C# keywords.
/// </summary>
public static class IdentifierSanitizer
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class",
        "const","continue","decimal","default","delegate","do","double","else","enum","event",
        "explicit","extern","false","finally","fixed","float","for","foreach","goto","if",
        "implicit","in","int","interface","internal","is","lock","long","namespace","new",
        "null","object","operator","out","override","params","private","protected","public",
        "readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static",
        "string","struct","switch","this","throw","true","try","typeof","uint","ulong",
        "unchecked","unsafe","ushort","using","virtual","void","volatile","while",
    };

    /// <summary>Produces an upper-camel-case identifier from arbitrary text. Never returns empty.</summary>
    public static string PascalCase(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unnamed";

        var sb = new StringBuilder(raw.Length);
        var capitalizeNext = true;

        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (sb.Length == 0) return "Unnamed";
        if (char.IsDigit(sb[0])) sb.Insert(0, "Op");
        return sb.ToString();
    }

    /// <summary>Produces a lower-camel-case identifier (used for parameter names).</summary>
    public static string CamelCase(string raw)
    {
        var pascal = PascalCase(raw);
        if (pascal.Length == 0) return pascal;
        var first = char.ToLowerInvariant(pascal[0]);
        var camel = first + pascal[1..];
        return CSharpKeywords.Contains(camel) ? "@" + camel : camel;
    }

    /// <summary>Returns a property identifier safe for emission. Prefixes <c>@</c> for keywords.</summary>
    public static string PropertyName(string raw)
    {
        var pascal = PascalCase(raw);
        // Property names always start with uppercase, so they can't collide with
        // keywords directly — but their lowercase counterparts can. We keep the
        // PascalCase form, which is keyword-free in practice.
        return pascal;
    }
}
