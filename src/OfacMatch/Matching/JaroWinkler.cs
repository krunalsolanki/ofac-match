namespace OfacMatch.Matching;

/// <summary>Jaro and Jaro-Winkler string similarity, the string-difference algorithm named in OFAC's FAQ.</summary>
public static class JaroWinkler
{
    /// <summary>Jaro similarity from 0 (nothing in common) to 1 (identical).</summary>
    public static double Jaro(string a, string b)
    {
        Guard.NotNull(a, nameof(a));
        Guard.NotNull(b, nameof(b));
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        var window = Math.Max(0, (Math.Max(a.Length, b.Length) / 2) - 1);
        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var start = Math.Max(0, i - window);
            var end = Math.Min(b.Length - 1, i + window);
            for (var j = start; j <= end; j++)
            {
                if (!bMatched[j] && a[i] == b[j])
                {
                    aMatched[i] = true;
                    bMatched[j] = true;
                    matches++;
                    break;
                }
            }
        }

        if (matches == 0)
        {
            return 0;
        }

        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatched[i])
            {
                continue;
            }

            while (!bMatched[k])
            {
                k++;
            }

            if (a[i] != b[k])
            {
                transpositions++;
            }

            k++;
        }

        double m = matches;
        return ((m / a.Length) + (m / b.Length) + ((m - (transpositions / 2.0)) / m)) / 3.0;
    }

    /// <summary>
    /// Jaro-Winkler similarity: Jaro plus a bonus for a shared prefix of up to four characters.
    /// </summary>
    /// <param name="a">First string.</param>
    /// <param name="b">Second string.</param>
    /// <param name="boostThreshold">
    /// The prefix bonus applies only when the Jaro score exceeds this value. Winkler's original
    /// definition uses 0.7; pass 0 to always apply the bonus.
    /// </param>
    /// <param name="prefixScale">
    /// Weight of each shared prefix character (standard: 0.1). At most 0.25, so the result stays within 0-1.
    /// </param>
    public static double Similarity(string a, string b, double boostThreshold = 0.7, double prefixScale = 0.1)
    {
        Guard.InRange(boostThreshold, 0, 1, nameof(boostThreshold));
        Guard.InRange(prefixScale, 0, 0.25, nameof(prefixScale));
        var jaro = Jaro(a, b);
        if (jaro <= boostThreshold)
        {
            return jaro;
        }

        var prefix = 0;
        var limit = Math.Min(4, Math.Min(a.Length, b.Length));
        while (prefix < limit && a[prefix] == b[prefix])
        {
            prefix++;
        }

        return jaro + (prefix * prefixScale * (1 - jaro));
    }
}
