using System.Text;
using OfacMatch.Data;
using OfacMatch.Matching;

namespace OfacMatch.Screening;

/// <summary>What to screen.</summary>
public sealed class ScreeningRequest
{
    /// <summary>Longest name accepted. Real names are far shorter; the limit keeps one bad input from slowing a batch.</summary>
    public const int MaxNameLength = 500;

    /// <summary>Longest country name accepted.</summary>
    public const int MaxCountryLength = 100;

    /// <summary>Earliest year of birth accepted.</summary>
    public const int MinYearOfBirth = 1800;

    /// <summary>Longest email address, phone number, website or crypto address accepted.</summary>
    public const int MaxIdentifierLength = 500;

    private EntryType? _type;
    private int? _yearOfBirth;
    private string? _country;
    private double? _minScore;
    private int? _maxResults;
    private string? _email;
    private string? _phone;
    private string? _website;
    private string? _cryptoAddress;

    /// <summary>Creates a request for a name.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is longer than <see cref="MaxNameLength"/> or has no letters or digits.
    /// </exception>
    public ScreeningRequest(string name)
    {
        Guard.NotNull(name, nameof(name));
        if (name.Length > MaxNameLength)
        {
            throw new ArgumentException($"Name is longer than {MaxNameLength} characters.", nameof(name));
        }

        // A blank or punctuation-only name would match nothing and look like a clean result.
        if (!name.Any(char.IsLetterOrDigit))
        {
            throw new ArgumentException("Name must contain at least one letter or digit.", nameof(name));
        }

        Name = name;
    }

    /// <summary>The name to screen, in any order ("John Doe" or "DOE, John").</summary>
    public string Name { get; }

    /// <summary>Only screen entries of this type, e.g. individuals for a borrower.</summary>
    public EntryType? Type
    {
        get => _type;
        set
        {
            if (value is EntryType type && !Enum.IsDefined(typeof(EntryType), type))
            {
                throw new ArgumentOutOfRangeException(nameof(value), type, "Unknown entry type.");
            }

            _type = value;
        }
    }

    /// <summary>
    /// The party's year of birth, used to flag matches whose listed dates of birth disagree.
    /// Must be between <see cref="MinYearOfBirth"/> and the current year.
    /// </summary>
    public int? YearOfBirth
    {
        get => _yearOfBirth;
        set => _yearOfBirth = value is int year ? Guard.InRange(year, MinYearOfBirth, DateTime.UtcNow.Year, nameof(value)) : null;
    }

    /// <summary>The party's country, used to flag matches whose listed addresses are all elsewhere.</summary>
    public string? Country
    {
        get => _country;
        set
        {
            if (value is not null && value.Length > MaxCountryLength)
            {
                throw new ArgumentException($"Country is longer than {MaxCountryLength} characters.", nameof(value));
            }

            _country = value;
        }
    }

    /// <summary>Overrides <see cref="ScreeningOptions.MinScore"/> for this request. Must be 0-100.</summary>
    public double? MinScore
    {
        get => _minScore;
        set => _minScore = value is double score ? Guard.InRange(score, 0, 100, nameof(value)) : null;
    }

    /// <summary>Overrides <see cref="ScreeningOptions.MaxResults"/> for this request. Must be at least 1.</summary>
    public int? MaxResults
    {
        get => _maxResults;
        set => _maxResults = value is int max ? Guard.InRange(max, 1, int.MaxValue, nameof(value)) : null;
    }

    /// <summary>
    /// The party's email address. An entry that lists the same address, or a website on the same domain,
    /// is returned even when the name doesn't match.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not an email address.</exception>
    public string? Email
    {
        get => _email;
        set => _email = Validate(value, v => IdentifierNormalizer.Email(v) is not null, "an email address");
    }

    /// <summary>
    /// The party's phone number, in any format. An entry that lists the same number, or one ending in the same
    /// 9 digits, is returned even when the name doesn't match.
    /// </summary>
    /// <exception cref="ArgumentException">The value has fewer than 7 digits.</exception>
    public string? Phone
    {
        get => _phone;
        set => _phone = Validate(value, v => IdentifierNormalizer.Phone(v) is not null, "a phone number with at least 7 digits");
    }

    /// <summary>
    /// The party's website. An entry that lists the same site, or a site on the same domain,
    /// is returned even when the name doesn't match.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a website address.</exception>
    public string? Website
    {
        get => _website;
        set => _website = Validate(value, v => IdentifierNormalizer.Website(v) is not null, "a website address");
    }

    /// <summary>
    /// A digital currency (crypto) address. An entry that lists the same address is returned even when the name
    /// doesn't match.
    /// </summary>
    /// <exception cref="ArgumentException">The value is not a crypto address.</exception>
    public string? CryptoAddress
    {
        get => _cryptoAddress;
        set => _cryptoAddress = Validate(value, v => IdentifierNormalizer.Crypto(v) is not null, "a crypto address");
    }

    private static string? Validate(string? value, Func<string, bool> isValid, string what)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value!.Length > MaxIdentifierLength)
        {
            throw new ArgumentException($"Value is longer than {MaxIdentifierLength} characters.", nameof(value));
        }

        if (!isValid(value))
        {
            throw new ArgumentException($"\"{value}\" is not {what}.", nameof(value));
        }

        return value;
    }
}

/// <summary>Screening settings.</summary>
public sealed class ScreeningOptions
{
    private double _minScore = 85;
    private int _maxResults = 25;
    private int _yearOfBirthTolerance = 1;
    private ScoringOptions _scoring = new();

    /// <summary>
    /// Lowest score (0-100) to return. OFAC recommends no threshold; set one from your own risk
    /// assessment. Default 85.
    /// </summary>
    public double MinScore
    {
        get => _minScore;
        set => _minScore = Guard.InRange(value, 0, 100, nameof(value));
    }

    /// <summary>Maximum number of matches to return, at least 1. Default 25.</summary>
    public int MaxResults
    {
        get => _maxResults;
        set => _maxResults = Guard.InRange(value, 1, int.MaxValue, nameof(value));
    }

    /// <summary>Years of difference (0-100) allowed before a date of birth counts as a conflict. Default 1.</summary>
    public int YearOfBirthTolerance
    {
        get => _yearOfBirthTolerance;
        set => _yearOfBirthTolerance = Guard.InRange(value, 0, 100, nameof(value));
    }

    /// <summary>Name scoring settings.</summary>
    public ScoringOptions Scoring
    {
        get => _scoring;
        set => _scoring = Guard.NotNull(value, nameof(value));
    }
}

/// <summary>A potential match. A person must review it: a score measures name similarity, not identity.</summary>
public sealed class ScreeningMatch
{
    internal ScreeningMatch(
        SanctionsEntry entry,
        string matchedName,
        NameKind nameKind,
        NameScore nameScore,
        bool dobConflict,
        bool countryConflict,
        IReadOnlyList<IdentifierMatch> identifierMatches,
        MatchBasis matchedBy)
    {
        Entry = entry;
        MatchedName = matchedName;
        NameKind = nameKind;
        NameScore = nameScore;
        DateOfBirthConflict = dobConflict;
        CountryConflict = countryConflict;
        IdentifierMatches = identifierMatches;
        MatchedBy = matchedBy;
    }

    /// <summary>The listed entry.</summary>
    public SanctionsEntry Entry { get; }

    /// <summary>
    /// Name score on OFAC's 0-100 scale (100 = exact). For a match found by an identifier
    /// (<see cref="MatchedBy"/> is <see cref="MatchBasis.Identifier"/>) it is below the threshold.
    /// </summary>
    public double Score => NameScore.Score;

    /// <summary>Whether the name, an identifier (email, phone, website, crypto address) or both matched.</summary>
    public MatchBasis MatchedBy { get; }

    /// <summary>The screened identifiers that match ones listed for the entry. Empty when only the name matched.</summary>
    public IReadOnlyList<IdentifierMatch> IdentifierMatches { get; }

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
        sb.AppendLine(MatchedBy == MatchBasis.Identifier
            ? $"Identifier match; name score {Score:0.0} via {NameScore.Technique} against {NameKind} name \"{MatchedName}\""
            : $"Score {Score:0.0} via {NameScore.Technique} against {NameKind} name \"{MatchedName}\"");
        sb.AppendLine($"  Entry {Entry.Id}: {Entry.Name} ({Entry.Type}; programs: {string.Join(", ", Entry.Programs)})");
        foreach (var identifier in IdentifierMatches)
        {
            sb.AppendLine($"  {identifier.Describe()}");
        }

        sb.AppendLine($"  Full-name score {NameScore.FullNameScore:0.0}{(NameScore.FullNameCounts ? string.Empty : " (not counted: a name part has no close partner)")}; name-parts score {NameScore.NamePartsScore:0.0}{(NameScore.ScoredFromListedName ? " (scored from the listed name: the search has parts not on the list)" : string.Empty)}");
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
    private readonly List<IndexedName> _nameList = new();
    private readonly IndexedName[] _names;
    private readonly EntryType[] _types;
    private readonly long[] _initials;
    private readonly Dictionary<string, int> _vocabulary = new(StringComparer.Ordinal);
    private readonly Dictionary<int, List<int>> _namesByEntry = new();
    private readonly IdentifierIndex _identifiers;

    [ThreadStatic]
    private static TokenSimilarityCache? t_cache;

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

        // Arrays, so the screening loop reads plain values (much faster in a browser).
        _names = _nameList.ToArray();
        _types = _names.Select(n => n.Entry.Type).ToArray();
        _initials = _names.Select(n => n.Initials).ToArray();
        for (var i = 0; i < _names.Length; i++)
        {
            var id = _names[i].Entry.Id;
            if (!_namesByEntry.TryGetValue(id, out var indexes))
            {
                _namesByEntry[id] = indexes = new List<int>();
            }

            indexes.Add(i);
        }

        _identifiers = new IdentifierIndex(list.Entries);
    }

    /// <summary>
    /// Finds entries that list an email address, website, phone number or crypto address matching
    /// <paramref name="value"/>, without screening a name.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not a valid identifier of that type.</exception>
    public IReadOnlyList<IdentifierMatch> ScreenIdentifier(IdentifierType type, string value)
    {
        Guard.NotNull(value, nameof(value));
        var request = new ScreeningRequest("x");
        switch (type)
        {
            case IdentifierType.Email: request.Email = value; break;
            case IdentifierType.Phone: request.Phone = value; break;
            case IdentifierType.Website: request.Website = value; break;
            case IdentifierType.CryptoAddress: request.CryptoAddress = value; break;
            default: throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown identifier type.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be empty.", nameof(value));
        }

        return _identifiers.Find(type, value);
    }

    /// <summary>Screens a name. Throws <see cref="ArgumentException"/> for a name <see cref="ScreeningRequest"/> rejects.</summary>
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

        var cache = t_cache;
        if (cache is null || !ReferenceEquals(cache.Owner, this))
        {
            cache = t_cache = new TokenSimilarityCache(this, _vocabulary.Count);
        }

        cache.Begin();
        queryPlain.CacheRows = cache.RowsFor(queryPlain.Tokens);
        queryEntity.CacheRows = cache.RowsFor(queryEntity.Tokens);

        var best = new Dictionary<int, (IndexedName Name, NameScore Score)>();
        var hasType = request.Type is not null;
        var wantedType = request.Type ?? default;
        for (var i = 0; i < _names.Length; i++)
        {
            var entryType = _types[i];
            if (hasType && entryType != wantedType)
            {
                continue;
            }

            // Cheap pre-filter in the spirit of OFAC FAQ 249: at least one name part must
            // start with the same letter as a part of the searched name.
            if ((_initials[i] & queryInitials) == 0)
            {
                continue;
            }

            var indexed = _names[i];
            var query = entryType == EntryType.Individual ? queryPlain : queryEntity;
            var score = _scorer.Score(query, indexed.Tokens, minScore, cache);
            if (score is null || score.Score < minScore)
            {
                continue;
            }

            if (!best.TryGetValue(indexed.Entry.Id, out var current) || score.Score > current.Score.Score)
            {
                best[indexed.Entry.Id] = (indexed, score);
            }
        }

        // Identifiers are matched on every entry, whatever its type: a borrower's email on a listed company's
        // domain is worth a look. They never remove or lower a name match.
        var byIdentifier = new Dictionary<int, List<IdentifierMatch>>();
        foreach (var (type, value) in RequestIdentifiers(request))
        {
            foreach (var found in _identifiers.Find(type, value))
            {
                if (!byIdentifier.TryGetValue(found.Entry.Id, out var list))
                {
                    byIdentifier[found.Entry.Id] = list = new List<IdentifierMatch>();
                }

                list.Add(found);
            }
        }

        // An entry found only by an identifier still gets its best name score, so the analyst sees how the
        // names compare (often not at all, when the party has changed its name).
        foreach (var entryId in byIdentifier.Keys)
        {
            if (!best.ContainsKey(entryId) && _namesByEntry.TryGetValue(entryId, out var indexes))
            {
                foreach (var i in indexes)
                {
                    var indexed = _names[i];
                    var query = _types[i] == EntryType.Individual ? queryPlain : queryEntity;
                    var score = _scorer.Score(query, indexed.Tokens, double.NegativeInfinity, cache)!;
                    if (!best.TryGetValue(entryId, out var current) || score.Score > current.Score.Score)
                    {
                        best[entryId] = (indexed, score);
                    }
                }
            }
        }

        return best.Values
            .Select(m => (m.Name, m.Score, Identifiers: byIdentifier.TryGetValue(m.Name.Entry.Id, out var ids) ? ids : null))
            .OrderByDescending(m => m.Identifiers is not null)
            .ThenByDescending(m => m.Score.Score)
            .ThenBy(m => m.Name.Entry.Id)
            .Take(request.MaxResults ?? _options.MaxResults)
            .Select(m => new ScreeningMatch(
                m.Name.Entry,
                m.Name.Raw,
                m.Name.Kind,
                m.Score,
                HasDobConflict(m.Name.Entry, request),
                HasCountryConflict(m.Name.Entry, request),
                (IReadOnlyList<IdentifierMatch>?)m.Identifiers ?? Array.Empty<IdentifierMatch>(),
                m.Identifiers is null ? MatchBasis.Name
                    : m.Score.Score >= minScore ? MatchBasis.NameAndIdentifier
                    : MatchBasis.Identifier))
            .ToList();
    }

    private static IEnumerable<(IdentifierType Type, string Value)> RequestIdentifiers(ScreeningRequest request)
    {
        if (request.Email is string email)
        {
            yield return (IdentifierType.Email, email);
        }

        if (request.Website is string website)
        {
            yield return (IdentifierType.Website, website);
        }

        if (request.Phone is string phone)
        {
            yield return (IdentifierType.Phone, phone);
        }

        if (request.CryptoAddress is string crypto)
        {
            yield return (IdentifierType.CryptoAddress, crypto);
        }
    }

    private void Index(SanctionsEntry entry, string raw, NameKind kind)
    {
        var natural = entry.Type == EntryType.Individual ? NameNormalizer.ToNaturalOrder(raw) : raw;
        var tokens = NameNormalizer.Tokenize(natural, removeLegalSuffixes: entry.Type != EntryType.Individual);
        if (tokens.Count > 0)
        {
            var ids = new int[tokens.Count];
            for (var i = 0; i < tokens.Count; i++)
            {
                if (!_vocabulary.TryGetValue(tokens[i], out ids[i]))
                {
                    ids[i] = _vocabulary.Count;
                    _vocabulary[tokens[i]] = ids[i];
                }
            }

            _nameList.Add(new IndexedName(entry, raw, kind, new NameTokens(tokens) { Ids = ids }, Initials(tokens)));
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
