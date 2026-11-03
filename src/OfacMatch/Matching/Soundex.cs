using System.Text;

namespace OfacMatch.Matching;

/// <summary>American Soundex, the phonetic algorithm named in OFAC's FAQ.</summary>
public static class Soundex
{
    // Codes for A..Z. '0' = vowel-like (A E I O U Y), '-' = H and W (do not separate equal codes).
    private const string Codes = "01230120022455012623010202";

    /// <summary>Returns the four-character Soundex code, e.g. "ROBERT" → "R163", or "" when there are no letters.</summary>
    public static string Encode(string value)
    {
        Guard.NotNull(value, nameof(value));
        var letters = new StringBuilder();
        foreach (var ch in value)
        {
            var upper = char.ToUpperInvariant(ch);
            if (upper >= 'A' && upper <= 'Z')
            {
                letters.Append(upper);
            }
        }

        if (letters.Length == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder(4);
        result.Append(letters[0]);
        var previous = Code(letters[0]);

        for (var i = 1; i < letters.Length && result.Length < 4; i++)
        {
            var letter = letters[i];
            var code = Code(letter);

            if (letter == 'H' || letter == 'W')
            {
                continue;
            }

            if (code != '0' && code != previous)
            {
                result.Append(code);
            }

            previous = code;
        }

        return result.ToString().PadRight(4, '0');
    }

    private static char Code(char upperLetter) =>
        upperLetter == 'H' || upperLetter == 'W' ? '-' : Codes[upperLetter - 'A'];
}
