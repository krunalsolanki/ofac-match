using System.Text;
using OfacMatch.Data;
using OfacMatch.Matching;

namespace OfacMatch.Screening;

/// <summary>What to screen.</summary>
public sealed class ScreeningRequest
{
    /// <summary>Creates a request for a name.</summary>
    public ScreeningRequest(string name)
    {
        Name = name;
    }

    /// <summary>The name to screen, in any order ("John Doe" or "DOE, John").</summary>
    public string Name { get; }

    /// <summary>Only screen entries of this type, e.g. individuals for a borrower.</summary>
    public EntryType? Type { get; set; }

    /// <summary>The party's year of birth, used to flag matches whose listed dates of birth disagree.</summary>
    public int? YearOfBirth { get; set; }

    /// <summary>The party's country, used to flag matches whose listed addresses are all elsewhere.</summary>
    public string? Country { get; set; }

    /// <summary>Overrides <see cref="ScreeningOptions.MinScore"/> for this request.</summary>
    public double? MinScore { get; set; }

    /// <summary>Overrides <see cref="ScreeningOptions.MaxResults"/> for this request.</summary>
    public int? MaxResults { get; set; }
}

/// <summary>Screening settings.</summary>
public sealed class ScreeningOptions
{
    /// <summary>
    /// Lowest score (0-100) to return. OFAC recommends no threshold; set one from your own risk
    /// assessment. Default 85.
    /// </summary>
    public double MinScore { get; set; } = 85;

    /// <summary>Maximum number of matches to return. Default 25.</summary>
    public int MaxResults { get; set; } = 25;

    /// <summary>Years of difference allowed before a date of birth counts as a conflict. Default 1.</summary>
    public int YearOfBirthTolerance { get; set; } = 1;

    /// <summary>Name scoring settings.</summary>
    public ScoringOptions Scoring { get; set; } = new();
}

/// <summary>A potential match. A person must review it: a score measures name similarity, not identity.</summary>
public sealed class ScreeningMatch
{
    internal ScreeningMatch(SanctionsEntry entry, string matchedName, NameKind nameKind, NameScore nameScore, bool dobConflict, bool countryConflict)
    {
        Entry = entry;
        MatchedName = matchedName;
        NameKind = nameKind;
        NameScore = nameScore;
        DateOfBirthConflict = dobConflict;
        CountryConflict = countryConflict;
    }

    /// <summary>The listed entry.</summary>
    public SanctionsEntry Entry { get; }

    /// <summary>Score on OFAC's 0-100 scale (100 = exact).</summary>
    public double Score => NameScore.Score;

    /// <summary>The listed name (primary or alias) that matched best.</summary>
    public string MatchedName { get; }

    /// <summary>Whether the matched name is the primary name or an alias.</summary>
    public NameKind NameKind { get; }

    /// <summary>The scoring evidence.</summary>
    public NameScore NameScore { get; }

    /// <summary>True when the request gave a year of birth and none of the listed dates of birth is close to it.</summary>
    public bool DateOfBirthConflict { get; }

    /// <summary>True when the request gave a country and none of the listed addresses is in it.</summary>
    public bool CountryConflict { get; }

    /// <summary>A plain-text explanation of the match for an analyst or an audit file.</summary>
    public string Explain()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Score {Score:0.0} via {NameScore.Technique} against {NameKind} name \"{MatchedName}\"");
        sb.AppendLine($"  Entry {Entry.Id}: {Entry.Name} ({Entry.Type}; programs: {string.Join(", ", Entry.Programs)})");
        sb.AppendLine($"  Full-name score {NameScore.FullNameScore:0.0}{(NameScore.FullNameCounts ? string.Empty : " (not counted: a name part has no close partner)")}; name-parts score {NameScore.NamePartsScore:0.0}");
        foreach (var part in NameScore.Parts)
        {
            sb.AppendLine(part.ListedPart is null
                ? $"  {part.QueryPart} -> (no match)"
                : $"  {part.QueryPart} -> {part.ListedPart}: similarity {part.Similarity:0.000}{(part.SoundsAlike ? ", sounds alike" : string.Empty)}");
        }

        if (DateOfBirthConflict)
        {
            sb.AppendLine($"  Date of birth conflict: listed {string.Join(", ", Entry.DatesOfBirth)}");
        }

        if (CountryConflict)
        {
            sb.AppendLine($"  Country conflict: listed {string.Join(", ", Entry.Countries)}");
        }

        return sb.ToString();
    }
}

/// <summary>Screens names against a loaded <see cref="SanctionsList"/>.</summary>
public sealed class SanctionsScreener
{
    private readonly ScreeningOptions _options;
    private readonly NameScorer _scorer;
    private readonly List<IndexedName> _names = new();

    /// <summary>Creates a screener and indexes every primary name and alias on the list.</summary>
    public SanctionsScreener(SanctionsList list, ScreeningOptions? options = null)
    {
        if (list is null)
        {
            throw new ArgumentNullException(nameof(list));
        }

        _options = options ?? new ScreeningOptions();
        _scorer = new NameScorer(_options.Scoring);

        foreach (var entry in list.Entries)
        {
            Index(entry, entry.Name, NameKind.Primary);
            foreach (var alias in entry.Aliases)
            {
                Index(entry, alias.Name, alias.Kind);
            }
        }
    }

    /// <summary>Screens a name.</summary>
    public IReadOnlyList<ScreeningMatch> Screen(string name) => Screen(new ScreeningRequest(name));

    /// <summary>Screens a request and returns potential matches, best first, one per entry.</summary>
    public IReadOnlyList<ScreeningMatch> Screen(ScreeningRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var queryPlain = new NameTokens(NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(request.Name)));
        var queryEntity = new NameTokens(NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(request.Name), removeLegalSuffixes: true));
        var queryInitials = Initials(queryPlain.Tokens) | Initials(queryEntity.Tokens);
        var minScore = request.MinScore ?? _options.MinScore;

        var best = new Dictionary<int, (IndexedName Name, NameScore Score)>();
        foreach (var indexed in _names)
        {
            if (request.Type is EntryType type && indexed.Entry.Type != type)
            {
                continue;
            }

            // Cheap pre-filter in the spirit of OFAC FAQ 249: at least one name part must
            // start with the same letter as a part of the searched name.
            if ((indexed.Initials & queryInitials) == 0)
            {
                continue;
            }

            var query = indexed.Entry.Type == EntryType.Individual ? queryPlain : queryEntity;
            var score = _scorer.Score(query, indexed.Tokens);
            if (score.Score < minScore)
            {
                continue;
            }

            if (!best.TryGetValue(indexed.Entry.Id, out var current) || score.Score > current.Score.Score)
            {
                best[indexed.Entry.Id] = (indexed, score);
            }
        }

        return best.Values
            .OrderByDescending(m => m.Score.Score)
            .ThenBy(m => m.Name.Entry.Id)
            .Take(request.MaxResults ?? _options.MaxResults)
            .Select(m => new ScreeningMatch(
                m.Name.Entry,
                m.Name.Raw,
                m.Name.Kind,
                m.Score,
                HasDobConflict(m.Name.Entry, request),
                HasCountryConflict(m.Name.Entry, request)))
            .ToList();
    }

    private void Index(SanctionsEntry entry, string raw, NameKind kind)
    {
        var natural = entry.Type == EntryType.Individual ? NameNormalizer.ToNaturalOrder(raw) : raw;
        var tokens = NameNormalizer.Tokenize(natural, removeLegalSuffixes: entry.Type != EntryType.Individual);
        if (tokens.Count > 0)
        {
            _names.Add(new IndexedName(entry, raw, kind, new NameTokens(tokens), Initials(tokens)));
        }
    }

    private bool HasDobConflict(SanctionsEntry entry, ScreeningRequest request) =>
        request.YearOfBirth is int year
        && entry.DatesOfBirth.Count > 0
        && entry.DatesOfBirth.All(d => Math.Abs(d.Year - year) > _options.YearOfBirthTolerance);

    private static bool HasCountryConflict(SanctionsEntry entry, ScreeningRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Country))
        {
            return false;
        }

        var countries = entry.Countries.ToList();
        return countries.Count > 0
            && !countries.Any(c => string.Equals(c.Trim(), request.Country!.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static long Initials(IReadOnlyList<string> tokens)
    {
        long mask = 0;
        foreach (var token in tokens)
        {
            var ch = token[0];
            var bit = ch >= 'A' && ch <= 'Z' ? ch - 'A' : ch >= '0' && ch <= '9' ? 26 + (ch - '0') : 36;
            mask |= 1L << bit;
        }

        return mask;
    }

    private sealed class IndexedName
    {
        public IndexedName(SanctionsEntry entry, string raw, NameKind kind, NameTokens tokens, long initials)
        {
            Entry = entry;
            Raw = raw;
            Kind = kind;
            Tokens = tokens;
            Initials = initials;
        }

        public SanctionsEntry Entry { get; }

        public string Raw { get; }

        public NameKind Kind { get; }

        public NameTokens Tokens { get; }

        public long Initials { get; }
    }
}
