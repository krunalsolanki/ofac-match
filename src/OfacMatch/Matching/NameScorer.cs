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
    internal NameScore(double fullNameScore, double namePartsScore, bool fullNameCounts, IReadOnlyList<PartMatch> parts, bool scoredFromListedName = false)
    {
        FullNameScore = fullNameScore;
        NamePartsScore = namePartsScore;
        FullNameCounts = fullNameCounts;
        Parts = parts;
        ScoredFromListedName = scoredFromListedName;
    }

    /// <summary>
    /// True when the searched name has parts that are not on the listed name (an extra middle name,
    /// for example) and the name-parts score was therefore taken from the listed name's side,
    /// reduced for the extra parts (see <see cref="ScoringOptions.AllowExtraSearchedParts"/>).
    /// </summary>
    public bool ScoredFromListedName { get; }

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
    public NameScore Score(IReadOnlyList<string> query, IReadOnlyList<string> listed)
    {
        Guard.NotNull(query, nameof(query));
        Guard.NotNull(listed, nameof(listed));
        if (query.Any(string.IsNullOrEmpty) || listed.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("Name parts must not be null or empty. Use NameNormalizer.Tokenize to split a name.");
        }

        return Score(new NameTokens(query), new NameTokens(listed));
    }

    /// <summary>Compares two raw names.</summary>
    public NameScore Score(string query, string listed) =>
        Score(NameNormalizer.Tokenize(Guard.NotNull(query, nameof(query))), NameNormalizer.Tokenize(Guard.NotNull(listed, nameof(listed))));

    internal NameScore Score(NameTokens query, NameTokens listed)
    {
        if (query.Count == 0 || listed.Count == 0)
        {
            return new NameScore(0, 0, false, Array.Empty<PartMatch>());
        }

        var full = JaroWinkler.Similarity(query.Joined, listed.Joined, _options.PrefixBoostThreshold);
        var (partsScore, parts, fromListed) = ScoreParts(query, listed);

        // A shared first name gives two different people a high whole-name score
        // ("JOHN SMITH" vs "JOHN NUMBI"), so by default the whole-name score only
        // counts when every searched part has a plausible partner.
        var fullNameCounts = !_options.RequireAllPartsForFullName
            || parts.All(p => p.Score >= _options.MinPartScoreForFullName);

        return new NameScore(Round(full), Round(partsScore), fullNameCounts, parts, fromListed);
    }

    private (double Score, IReadOnlyList<PartMatch> Parts, bool FromListed) ScoreParts(NameTokens query, NameTokens listed)
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
        double listedWeighted = 0;
        foreach (var pair in pairs)
        {
            if (assigned[pair.Q] is not null || usedListed[pair.L])
            {
                continue;
            }

            assigned[pair.Q] = new PartMatch(query.Tokens[pair.Q], listed.Tokens[pair.L], pair.Similarity, pair.SoundsAlike, pair.Score);
            usedListed[pair.L] = true;
            listedWeighted += pair.Score * listed.Tokens[pair.L].Length;
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

        // A customer often gives a fuller name than the list has ("Ayman James al-Zawahiri").
        // Weighting by the searched parts counts the extra part as a miss, so also score from the
        // listed name's side, reduced by the share of searched letters that found no partner.
        // Only when the search has more parts than a listed name of two or more parts, so a
        // two-part search is never matched on one shared part.
        if (_options.AllowExtraSearchedParts && listed.Count >= 2 && query.Count > listed.Count)
        {
            double unpaired = 0;
            for (var q = 0; q < query.Count; q++)
            {
                if (assigned[q] is null)
                {
                    unpaired += query.Tokens[q].Length;
                }
            }

            var fromListed = (listedWeighted / listed.TotalLength) * (1 - (_options.ExtraPartPenalty * unpaired / totalWeight));
            if (fromListed > score)
            {
                return (fromListed, parts, true);
            }
        }

        return (score, parts, false);
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
    private double _prefixBoostThreshold = 0.7;
    private double _coverageWeight = 0.05;
    private double _minPartScoreForFullName = 0.7;
    private double _extraPartPenalty = 0.1;

    /// <summary>
    /// Jaro score (0-1) above which the Jaro-Winkler prefix bonus applies. Winkler's original
    /// definition uses 0.7 (the default). OpenSanctions reported that OFAC's scores are
    /// reproduced more closely when the bonus is always applied; set 0 to try that.
    /// </summary>
    public double PrefixBoostThreshold
    {
        get => _prefixBoostThreshold;
        set => _prefixBoostThreshold = Guard.InRange(value, 0, 1, nameof(value));
    }

    /// <summary>
    /// Share of the name-parts score (0-1) that depends on how much of the listed name the
    /// search covered. Default 0.05: a partial search such as "Ayman Zawahiri" still scores high.
    /// </summary>
    public double CoverageWeight
    {
        get => _coverageWeight;
        set => _coverageWeight = Guard.InRange(value, 0, 1, nameof(value));
    }

    /// <summary>
    /// When true (default), the whole-name score can raise the result only if every part of the
    /// searched name pairs with a listed part scoring at least <see cref="MinPartScoreForFullName"/>.
    /// Set false to always report the higher of the two techniques, as OFAC's FAQ 249 describes.
    /// </summary>
    public bool RequireAllPartsForFullName { get; set; } = true;

    /// <summary>
    /// When true (default), a searched name with more parts than a listed name of two or more parts
    /// (an extra middle name, for example) is also scored from the listed name's side: how well each
    /// listed part was matched, weighted by its length, reduced by <see cref="ExtraPartPenalty"/> times the share of searched letters
    /// in parts with no partner. Set false for the behaviour of version 0.1.2 and earlier.
    /// </summary>
    public bool AllowExtraSearchedParts { get; set; } = true;

    /// <summary>
    /// Used by <see cref="AllowExtraSearchedParts"/>: how much each letter of a searched part with no
    /// listed partner reduces the score taken from the listed name's side, relative to a letter that
    /// failed to match (0-1). 0 ignores extra parts entirely; 1 counts them as fully missed. Default 0.1,
    /// chosen on the development set by the rule in docs/evaluation-protocol.md.
    /// </summary>
    public double ExtraPartPenalty
    {
        get => _extraPartPenalty;
        set => _extraPartPenalty = Guard.InRange(value, 0, 1, nameof(value));
    }

    /// <summary>Minimum part score (0-1) used by <see cref="RequireAllPartsForFullName"/>. Default 0.7.</summary>
    public double MinPartScoreForFullName
    {
        get => _minPartScoreForFullName;
        set => _minPartScoreForFullName = Guard.InRange(value, 0, 1, nameof(value));
    }
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
