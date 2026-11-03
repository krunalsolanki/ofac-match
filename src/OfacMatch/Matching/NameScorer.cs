namespace OfacMatch.Matching;

/// <summary>Which of the two comparison techniques produced a score.</summary>
public enum MatchTechnique
{
    /// <summary>The whole name compared as one string.</summary>
    FullName,
    /// <summary>Name parts compared one by one with Jaro-Winkler and Soundex.</summary>
    NameParts,
}

/// <summary>How one part of the searched name matched a part of the listed name.</summary>
public sealed class PartMatch
{
    internal PartMatch(string queryPart, string? listedPart, double similarity, bool soundsAlike, double score)
    {
        QueryPart = queryPart;
        ListedPart = listedPart;
        Similarity = similarity;
        SoundsAlike = soundsAlike;
        Score = score;
    }

    /// <summary>The part of the searched name.</summary>
    public string QueryPart { get; }

    /// <summary>The listed name part it was paired with, or null when nothing paired.</summary>
    public string? ListedPart { get; }

    /// <summary>Jaro-Winkler similarity of the two parts (0-1).</summary>
    public double Similarity { get; }

    /// <summary>Whether the parts share a Soundex code.</summary>
    public bool SoundsAlike { get; }

    /// <summary>The part's score (0-1) after combining similarity and sound.</summary>
    public double Score { get; }
}

/// <summary>The result of comparing two names, with the evidence behind the score.</summary>
public sealed class NameScore
{
    internal NameScore(double fullNameScore, double namePartsScore, bool fullNameCounts, IReadOnlyList<PartMatch> parts)
    {
        FullNameScore = fullNameScore;
        NamePartsScore = namePartsScore;
        FullNameCounts = fullNameCounts;
        Parts = parts;
    }

    /// <summary>Whole-name Jaro-Winkler similarity on a 0-100 scale.</summary>
    public double FullNameScore { get; }

    /// <summary>Composite name-parts score on a 0-100 scale.</summary>
    public double NamePartsScore { get; }

    /// <summary>
    /// Whether <see cref="FullNameScore"/> was allowed to set the score. It is not when a part of
    /// the searched name has no plausible partner (see <see cref="ScoringOptions.RequireAllPartsForFullName"/>).
    /// </summary>
    public bool FullNameCounts { get; }

    /// <summary>The final score on a 0-100 scale (100 = exact).</summary>
    public double Score => FullNameCounts ? Math.Max(FullNameScore, NamePartsScore) : NamePartsScore;

    /// <summary>The technique that produced <see cref="Score"/>.</summary>
    public MatchTechnique Technique =>
        FullNameCounts && FullNameScore >= NamePartsScore ? MatchTechnique.FullName : MatchTechnique.NameParts;

    /// <summary>How each part of the searched name was paired.</summary>
    public IReadOnlyList<PartMatch> Parts { get; }
}

/// <summary>
/// Scores how similar two names are, following the approach OFAC describes for its
/// Sanctions List Search tool (FAQ 249): compare the full names, compare the name parts
/// with Jaro-Winkler and Soundex, and report the higher score.
/// </summary>
public sealed class NameScorer
{
    private readonly ScoringOptions _options;

    /// <summary>Creates a scorer.</summary>
    public NameScorer(ScoringOptions? options = null)
    {
        _options = options ?? new ScoringOptions();
    }

    /// <summary>Compares two already-tokenized names.</summary>
    public NameScore Score(IReadOnlyList<string> query, IReadOnlyList<string> listed) =>
        Score(new NameTokens(query), new NameTokens(listed));

    /// <summary>Compares two raw names.</summary>
    public NameScore Score(string query, string listed) =>
        Score(NameNormalizer.Tokenize(query), NameNormalizer.Tokenize(listed));

    internal NameScore Score(NameTokens query, NameTokens listed)
    {
        if (query.Count == 0 || listed.Count == 0)
        {
            return new NameScore(0, 0, false, Array.Empty<PartMatch>());
        }

        var full = JaroWinkler.Similarity(query.Joined, listed.Joined, _options.PrefixBoostThreshold);
        var (partsScore, parts) = ScoreParts(query, listed);

        // A shared first name gives two different people a high whole-name score
        // ("JOHN SMITH" vs "JOHN NUMBI"), so by default the whole-name score only
        // counts when every searched part has a plausible partner.
        var fullNameCounts = !_options.RequireAllPartsForFullName
            || parts.All(p => p.Score >= _options.MinPartScoreForFullName);

        return new NameScore(Round(full), Round(partsScore), fullNameCounts, parts);
    }

    private (double Score, IReadOnlyList<PartMatch> Parts) ScoreParts(NameTokens query, NameTokens listed)
    {
        // Score every pair, then pair parts greedily from the best pair down,
        // so each listed part is used at most once.
        var pairs = new Pair[query.Count * listed.Count];
        var n = 0;
        for (var q = 0; q < query.Count; q++)
        {
            for (var l = 0; l < listed.Count; l++)
            {
                var similarity = JaroWinkler.Similarity(query.Tokens[q], listed.Tokens[l], _options.PrefixBoostThreshold);
                var soundsAlike = query.Tokens[q].Length > 1 && listed.Tokens[l].Length > 1
                    && query.Codes[q] == listed.Codes[l];
                var pairScore = soundsAlike ? Math.Max(similarity, (similarity + 1) / 2) : similarity;
                pairs[n++] = new Pair(q, l, similarity, soundsAlike, pairScore);
            }
        }

        Array.Sort(pairs, (x, y) => y.Score.CompareTo(x.Score));

        var assigned = new PartMatch?[query.Count];
        var usedListed = new bool[listed.Count];
        foreach (var pair in pairs)
        {
            if (assigned[pair.Q] is not null || usedListed[pair.L])
            {
                continue;
            }

            assigned[pair.Q] = new PartMatch(query.Tokens[pair.Q], listed.Tokens[pair.L], pair.Similarity, pair.SoundsAlike, pair.Score);
            usedListed[pair.L] = true;
        }

        var parts = new PartMatch[query.Count];
        double weighted = 0, totalWeight = 0;
        for (var q = 0; q < query.Count; q++)
        {
            parts[q] = assigned[q] ?? new PartMatch(query.Tokens[q], null, 0, false, 0);
            double weight = query.Tokens[q].Length;
            weighted += parts[q].Score * weight;
            totalWeight += weight;
        }

        var composite = weighted / totalWeight;

        // Small nudge toward listed names that are fully covered, so "John Michael Doe"
        // outranks "John Doe" when both are on the list. Never more than CoverageWeight.
        double coveredChars = 0;
        for (var l = 0; l < listed.Count; l++)
        {
            if (usedListed[l])
            {
                coveredChars += listed.Tokens[l].Length;
            }
        }

        var coverage = coveredChars / listed.TotalLength;
        var score = composite * ((1 - _options.CoverageWeight) + (_options.CoverageWeight * coverage));
        return (score, parts);
    }

    private readonly struct Pair
    {
        public Pair(int q, int l, double similarity, bool soundsAlike, double score)
        {
            Q = q;
            L = l;
            Similarity = similarity;
            SoundsAlike = soundsAlike;
            Score = score;
        }

        public int Q { get; }

        public int L { get; }

        public double Similarity { get; }

        public bool SoundsAlike { get; }

        public double Score { get; }
    }

    private static double Round(double unitScore) => Math.Round(unitScore * 100, 1, MidpointRounding.AwayFromZero);
}

/// <summary>Tuning for <see cref="NameScorer"/>.</summary>
public sealed class ScoringOptions
{
    /// <summary>
    /// Jaro score above which the Jaro-Winkler prefix bonus applies. Winkler's original
    /// definition uses 0.7 (the default). OpenSanctions reported that OFAC's scores are
    /// reproduced more closely when the bonus is always applied; set 0 to try that.
    /// </summary>
    public double PrefixBoostThreshold { get; set; } = 0.7;

    /// <summary>
    /// Share of the name-parts score (0-1) that depends on how much of the listed name the
    /// search covered. Default 0.05: a partial search such as "Ayman Zawahiri" still scores high.
    /// </summary>
    public double CoverageWeight { get; set; } = 0.05;

    /// <summary>
    /// When true (default), the whole-name score can raise the result only if every part of the
    /// searched name pairs with a listed part scoring at least <see cref="MinPartScoreForFullName"/>.
    /// Set false to always report the higher of the two techniques, as OFAC's FAQ 249 describes.
    /// </summary>
    public bool RequireAllPartsForFullName { get; set; } = true;

    /// <summary>Minimum part score (0-1) used by <see cref="RequireAllPartsForFullName"/>. Default 0.7.</summary>
    public double MinPartScoreForFullName { get; set; } = 0.7;
}

/// <summary>A tokenized name with its Soundex codes, computed once and reused for every comparison.</summary>
internal sealed class NameTokens
{
    public NameTokens(IReadOnlyList<string> tokens)
    {
        Tokens = tokens;
        Codes = tokens.Select(Soundex.Encode).ToArray();
        Joined = string.Join(" ", tokens);
        TotalLength = tokens.Sum(t => t.Length);
    }

    public IReadOnlyList<string> Tokens { get; }

    public string[] Codes { get; }

    public string Joined { get; }

    public int TotalLength { get; }

    public int Count => Tokens.Count;
}
