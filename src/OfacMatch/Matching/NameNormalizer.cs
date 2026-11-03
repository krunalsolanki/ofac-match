using System.Globalization;
using System.Text;

namespace OfacMatch.Matching;

/// <summary>Turns a raw name into comparable tokens.</summary>
public static class NameNormalizer
{
    private static readonly HashSet<string> Honorifics = new(StringComparer.Ordinal)
    {
        "DR", "MR", "MRS", "MS", "MISS",
    };

    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal)
    {
        "CO", "COMPANY", "CORP", "CORPORATION", "INC", "INCORPORATED", "LLC", "LLP", "LP", "LTD", "LIMITED",
        "PLC", "SA", "SAS", "SARL", "SPA", "SRL", "GMBH", "AG", "BV", "NV", "JSC", "PJSC", "OJSC", "CJSC",
        "OOO", "ZAO", "OAO", "FZE", "FZCO", "DMCC", "PTE", "PVT", "BHD", "SDN",
    };

    /// <summary>
    /// Normalizes a name into upper-case ASCII tokens: accents removed, apostrophes dropped,
    /// other punctuation treated as spaces, and honorifics such as "Dr." removed.
    /// </summary>
    /// <param name="name">The raw name.</param>
    /// <param name="removeLegalSuffixes">Also drop company suffixes such as LLC, LTD or S.A.</param>
    public static IReadOnlyList<string> Tokenize(string name, bool removeLegalSuffixes = false)
    {
        var text = FoldToAscii(name);
        var tokens = new List<string>();
        var current = new StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                current.Append(char.ToUpperInvariant(ch));
            }
            else if (ch == '\'' || ch == '`' || ch == '’' || ch == '‘')
            {
                // 'ABBAS and ABBAS are the same name; O'NEIL and ONEIL too.
            }
            else
            {
                Flush(current, tokens);
            }
        }

        Flush(current, tokens);

        var filtered = tokens.Where(t => !Honorifics.Contains(t)).ToList();
        if (removeLegalSuffixes)
        {
            // Join "S A" style abbreviations before matching suffixes.
            filtered = JoinSingleLetters(filtered).Where(t => !IsLegalForm(t)).ToList();
        }

        return filtered.Count > 0 ? filtered : tokens;
    }

    /// <summary>
    /// Converts OFAC's "LAST, First Middle" order for individuals into "First Middle LAST".
    /// Names without a comma are returned unchanged.
    /// </summary>
    public static string ToNaturalOrder(string name)
    {
        var comma = name.IndexOf(',');
        if (comma <= 0 || comma == name.Length - 1)
        {
            return name;
        }

        var last = name.Substring(0, comma).Trim();
        var first = name.Substring(comma + 1).Trim();
        return first.Length == 0 ? last : $"{first} {last}";
    }

    // Long legal-form words are also recognized when misspelled ("COMPALY" for COMPANY),
    // so a typo in a company's legal form doesn't count against the match. Same first
    // letter and length within one keep real words such as UNLIMITED or CORPORATE.
    private static readonly string[] LongLegalForms =
        LegalSuffixes.Where(s => s.Length >= 6).ToArray();

    private static bool IsLegalForm(string token) =>
        LegalSuffixes.Contains(token)
        || (token.Length >= 6 && LongLegalForms.Any(form =>
            form[0] == token[0]
            && Math.Abs(form.Length - token.Length) <= 1
            && JaroWinkler.Similarity(token, form) >= 0.92));

    private static string FoldToAscii(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private static void Flush(StringBuilder current, List<string> tokens)
    {
        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
            current.Clear();
        }
    }

    private static List<string> JoinSingleLetters(List<string> tokens)
    {
        var result = new List<string>();
        var run = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token.Length == 1)
            {
                run.Append(token);
                continue;
            }

            if (run.Length > 0)
            {
                result.Add(run.ToString());
                run.Clear();
            }

            result.Add(token);
        }

        if (run.Length > 0)
        {
            result.Add(run.ToString());
        }

        return result;
    }
}
