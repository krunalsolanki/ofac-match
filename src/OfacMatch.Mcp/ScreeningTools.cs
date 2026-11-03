using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OfacMatch.Data;
using OfacMatch.Matching;
using OfacMatch.Screening;

namespace OfacMatch.Mcp;

/// <summary>MCP tools for screening names against the OFAC SDN list.</summary>
[McpServerToolType]
public sealed class ScreeningTools
{
    private const string Notice =
        "Potential matches only. A score measures how similar two names are written, not whether they are the same party. " +
        "A person must review each match against the full record (date of birth, nationality, address, IDs) before any decision. Not legal advice.";

    private const int MaxBatch = 100;

    private const int MaxResultsLimit = 50;

    private readonly SanctionsListProvider _provider;

    public ScreeningTools(SanctionsListProvider provider)
    {
        _provider = provider;
    }

    [McpServerTool(Name = "screen_name", Title = "Screen a name against the OFAC SDN list", ReadOnly = true, Idempotent = true)]
    [Description("Screens one person, company, vessel or aircraft name against the U.S. Treasury OFAC SDN sanctions list and returns potential matches with scores (0-100, 100 = identical) and an explanation of each. Give year of birth and country when known so conflicts can be flagged.")]
    public async Task<ScreeningResult> ScreenName(
        [Description("The name to screen, in any order, e.g. \"John Doe\" or \"DOE, John\".")] string name,
        [Description("Only screen this kind of entry: individual, entity, vessel or aircraft. Omit to screen all.")] string? type = null,
        [Description("The party's year of birth, if known. Matches whose listed dates of birth differ by more than a year are flagged.")] int? yearOfBirth = null,
        [Description("The party's country, if known. Matches whose listed addresses are all in other countries are flagged.")] string? country = null,
        [Description("Lowest score to return, 0-100. Default 85.")] double minScore = 85,
        [Description("Maximum number of matches to return, 1-50. Default 10.")] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        // Check the input before loading the list, so a bad call fails fast and never triggers a download.
        var request = BuildRequest(name, type, yearOfBirth, country, minScore, maxResults);
        var loaded = await _provider.GetAsync(cancellationToken);
        var matches = loaded.Screener.Screen(request).Select(ToDto).ToList();
        return new ScreeningResult(request.Name, matches.Count, matches, ListInfo(loaded), minScore, Notice);
    }

    [McpServerTool(Name = "screen_names", Title = "Screen several names", ReadOnly = true, Idempotent = true)]
    [Description("Screens up to 100 names against the OFAC SDN list and returns, for each, the number of potential matches and the best one. Use screen_name for full details on any name that has matches.")]
    public async Task<BatchResult> ScreenNames(
        [Description("The names to screen (at most 100).")] string[] names,
        [Description("Lowest score to count as a potential match, 0-100. Default 85.")] double minScore = 85,
        CancellationToken cancellationToken = default)
    {
        if (names is null || names.Length == 0)
        {
            throw new McpException("Give at least one name to screen.");
        }

        if (names.Length > MaxBatch)
        {
            throw new McpException($"Screen at most {MaxBatch} names per call; got {names.Length}.");
        }

        CheckMinScore(minScore);
        var requests = names
            .Select((n, i) => Validated(() => new ScreeningRequest(n) { MinScore = minScore, MaxResults = 25 }, $"names[{i}]"))
            .ToList();

        var loaded = await _provider.GetAsync(cancellationToken);
        var results = requests
            .Select(request =>
            {
                var matches = loaded.Screener.Screen(request);
                var best = matches.FirstOrDefault();
                return new BatchItem(request.Name, matches.Count, best?.Score, best?.Entry.Id, best?.MatchedName, best?.Entry.Name);
            })
            .ToList();

        return new BatchResult(results.Count(r => r.PotentialMatches > 0), results, ListInfo(loaded), minScore, Notice);
    }

    [McpServerTool(Name = "get_sanctions_entry", Title = "Look up an SDN entry", ReadOnly = true, Idempotent = true)]
    [Description("Returns the full published record of one OFAC SDN entry by its entry number: names, aliases, type, programs, dates of birth, addresses and remarks. Use it to review a potential match.")]
    public async Task<EntryDetails?> GetSanctionsEntry(
        [Description("The SDN entry number (ent_num) shown in screening results.")] int entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId <= 0)
        {
            throw new McpException($"entryId must be a positive SDN entry number; got {entryId}.");
        }

        var loaded = await _provider.GetAsync(cancellationToken);
        var entry = loaded.List.Entries.FirstOrDefault(e => e.Id == entryId);
        return entry is null ? null : ToDetails(entry);
    }

    [McpServerTool(Name = "compare_names", Title = "Explain how two names score", ReadOnly = true, Idempotent = true)]
    [Description("Scores two names against each other with the same method used for screening, and shows the whole-name score, the name-parts score and how each part paired. Useful to understand or explain a score.")]
    public CompareResult CompareNames(
        [Description("The searched name.")] string name,
        [Description("The name to compare with, e.g. a listed name or alias.")] string otherName,
        [Description("Treat the names as company names, ignoring legal forms such as LLC or LTD.")] bool companies = false)
    {
        // ScreeningRequest applies the same rules to both names as screening does.
        name = Validated(() => new ScreeningRequest(name), nameof(name)).Name;
        otherName = Validated(() => new ScreeningRequest(otherName), nameof(otherName)).Name;

        var score = new NameScorer().Score(
            NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(name), companies),
            NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(otherName), companies));

        return new CompareResult(
            score.Score,
            score.Technique.ToString(),
            score.FullNameScore,
            score.NamePartsScore,
            score.FullNameCounts,
            score.Parts.Select(ToDto).ToList());
    }

    [McpServerTool(Name = "get_list_info", Title = "SDN list status", ReadOnly = true, Idempotent = true)]
    [Description("Reports which copy of the OFAC SDN list the server is using: number of entries, where it came from and when it was downloaded.")]
    public async Task<ListDetails> GetListInfo(CancellationToken cancellationToken = default) =>
        ListInfo(await _provider.GetAsync(cancellationToken));

    private static ScreeningRequest BuildRequest(string name, string? type, int? yearOfBirth, string? country, double minScore, int maxResults)
    {
        CheckMinScore(minScore);
        if (maxResults < 1 || maxResults > MaxResultsLimit)
        {
            throw new McpException($"maxResults must be between 1 and {MaxResultsLimit}; got {maxResults}.");
        }

        var request = Validated(() => new ScreeningRequest(name), nameof(name));
        request.Type = ParseType(type);
        request.MinScore = minScore;
        request.MaxResults = maxResults;
        Validated(() => request.YearOfBirth = yearOfBirth, nameof(yearOfBirth));
        Validated(() => request.Country = string.IsNullOrWhiteSpace(country) ? null : country!.Trim(), nameof(country));
        return request;
    }

    private static EntryType? ParseType(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        // Enum.TryParse also accepts numbers such as "7", so compare against the names only.
        foreach (EntryType value in Enum.GetValues(typeof(EntryType)))
        {
            if (string.Equals(value.ToString(), type!.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new McpException($"Unknown type \"{type}\". Use individual, entity, vessel or aircraft.");
    }

    private static void CheckMinScore(double minScore)
    {
        if (!(minScore >= 0 && minScore <= 100))
        {
            throw new McpException($"minScore must be between 0 and 100; got {minScore}.");
        }
    }

    /// <summary>Runs a library check and reports its failure to the client as a clear tool error.</summary>
    private static T Validated<T>(Func<T> create, string parameter)
    {
        try
        {
            return create();
        }
        catch (ArgumentException ex)
        {
            // Keep the first line and drop the " (Parameter 'x')" suffix .NET adds; the parameter is named in our own message.
            var message = ex.Message.Split('\n')[0].Trim();
            if (ex.ParamName is not null)
            {
                message = message.Replace($" (Parameter '{ex.ParamName}')", string.Empty);
            }

            throw new McpException($"Invalid {parameter}: {message}");
        }
    }

    private static ListDetails ListInfo(SanctionsListProvider.Loaded loaded) =>
        new(loaded.List.Entries.Count, loaded.List.Entries.Sum(e => e.Aliases.Count), loaded.Source, loaded.FilesDate.ToString("u"));

    private static MatchDto ToDto(ScreeningMatch match) =>
        new(
            match.Score,
            match.Entry.Id,
            match.Entry.Name,
            match.Entry.Type.ToString(),
            match.Entry.Programs.ToList(),
            match.MatchedName,
            match.NameKind.ToString(),
            match.DateOfBirthConflict,
            match.CountryConflict,
            match.Explain().TrimEnd());

    private static PartDto ToDto(PartMatch part) =>
        new(part.QueryPart, part.ListedPart, Math.Round(part.Similarity, 3), part.SoundsAlike, Math.Round(part.Score, 3));

    private static EntryDetails ToDetails(SanctionsEntry entry) =>
        new(
            entry.Id,
            entry.Name,
            entry.Type.ToString(),
            entry.Programs.ToList(),
            entry.Title,
            entry.Aliases.Select(a => new AliasDto(a.Kind.ToString(), a.Name)).ToList(),
            entry.DatesOfBirth.Select(d => d.ToString()).ToList(),
            entry.Addresses.Select(a => string.Join(", ", new[] { a.Street, a.CityStateZip, a.Country }.Where(s => !string.IsNullOrWhiteSpace(s)))).ToList(),
            entry.Remarks);
}

public sealed record ScreeningResult(string Name, int PotentialMatches, IReadOnlyList<MatchDto> Matches, ListDetails List, double MinScore, string Notice);

public sealed record MatchDto(
    double Score,
    int EntryId,
    string ListedName,
    string Type,
    IReadOnlyList<string> Programs,
    string MatchedName,
    string MatchedNameKind,
    bool DateOfBirthConflict,
    bool CountryConflict,
    string Explanation);

public sealed record BatchResult(int NamesWithMatches, IReadOnlyList<BatchItem> Results, ListDetails List, double MinScore, string Notice);

public sealed record BatchItem(string Name, int PotentialMatches, double? BestScore, int? BestEntryId, string? BestMatchedName, string? BestListedName);

public sealed record EntryDetails(
    int EntryId,
    string Name,
    string Type,
    IReadOnlyList<string> Programs,
    string? Title,
    IReadOnlyList<AliasDto> Aliases,
    IReadOnlyList<string> DatesOfBirth,
    IReadOnlyList<string> Addresses,
    string? Remarks);

public sealed record AliasDto(string Kind, string Name);

public sealed record CompareResult(double Score, string Technique, double FullNameScore, double NamePartsScore, bool FullNameCounts, IReadOnlyList<PartDto> Parts);

public sealed record PartDto(string QueryPart, string? MatchedPart, double Similarity, bool SoundsAlike, double Score);

public sealed record ListDetails(int Entries, int Aliases, string Source, string DownloadedUtc);
