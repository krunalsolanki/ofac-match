using OfacMatch.Data;
using OfacMatch.Matching;
using OfacMatch.Screening;

namespace OfacMatch.Api;

/// <summary>The /v1 endpoints.</summary>
public static class Endpoints
{
    public const string Notice =
        "Potential matches only. A score measures how similar two names are written, not whether they are the same party. " +
        "A person must review each match against the full record before any decision. Checks the OFAC SDN list only. Not legal advice.";

    public const int MaxBatch = 1000;
    public const int MaxResultsLimit = 50;
    public const int MaxReferenceLength = 100;

    public static void Map(RouteGroupBuilder v1)
    {
        v1.MapPost("/screen", Screen)
            .WithSummary("Screen one name against the OFAC SDN list.")
            .WithDescription("Returns potential matches, best first, each with a score (0-100), the matched name or alias, conflict flags and an explanation.");

        v1.MapPost("/screen/batch", ScreenBatch)
            .WithSummary($"Screen up to {MaxBatch} names in one request.")
            .WithDescription("Each item can carry your own reference (for example a customer ID), which is returned unchanged. Items that fail validation are reported one by one; the rest are still screened.");

        v1.MapPost("/compare", Compare)
            .WithSummary("Explain how two names score against each other.");

        v1.MapGet("/entries/{id:int}", GetEntry)
            .WithSummary("The full published record of one SDN entry, to review a match.");

        v1.MapGet("/list", async (ListProvider provider, CancellationToken ct) =>
                await WithListAsync(provider, ct, loaded => Results.Ok(ListInfo(loaded))))
            .WithSummary("Which copy of the SDN list is in use.");
    }

    internal static async Task<IResult> Screen(ScreenRequest body, ListProvider provider, CancellationToken ct)
    {
        // Check the input before touching the list, so a bad request fails fast.
        if (!TryBuild(body, body.MinScore, body.MaxResults ?? 10, out var request, out var errors))
        {
            return Results.ValidationProblem(errors);
        }

        return await WithListAsync(provider, ct, loaded =>
        {
            var matches = loaded.Screener.Screen(request!).Select(ToDto).ToList();
            return Results.Ok(new ScreenResponse(request!.Name, matches.Count, matches, request.MinScore ?? 85, ListInfo(loaded), Notice));
        });
    }

    internal static async Task<IResult> ScreenBatch(BatchRequest body, ListProvider provider, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (body.Items is null || body.Items.Count == 0)
        {
            errors["items"] = ["Give at least one item to screen."];
        }
        else if (body.Items.Count > MaxBatch)
        {
            errors["items"] = [$"Send at most {MaxBatch} items per request; got {body.Items.Count}. Split the list into several requests."];
        }

        CheckMinScore(body.MinScore, errors);
        CheckMaxResults(body.MaxResults ?? 5, errors);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        // Build every request first; an invalid item is reported in its own result and doesn't stop the batch.
        var items = body.Items!;
        var requests = new ScreeningRequest?[items.Count];
        var itemErrors = new string?[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i] ?? new BatchItem();
            if (item.Reference is { Length: > MaxReferenceLength })
            {
                itemErrors[i] = $"reference is longer than {MaxReferenceLength} characters.";
            }
            else if (TryBuild(item.ToScreen(), body.MinScore, body.MaxResults ?? 5, out var request, out var problems))
            {
                requests[i] = request;
            }
            else
            {
                itemErrors[i] = string.Join(" ", problems.SelectMany(p => p.Value));
            }
        }

        return await WithListAsync(provider, ct, loaded =>
        {
            var results = new BatchItemResult[items.Count];
            Parallel.For(0, items.Count, new ParallelOptions { CancellationToken = ct }, i =>
            {
                var reference = items[i]?.Reference;
                var name = items[i]?.Name ?? string.Empty;
                if (requests[i] is not { } request)
                {
                    results[i] = new BatchItemResult(i, reference, name, "error", 0, [], itemErrors[i]);
                    return;
                }

                var matches = loaded.Screener.Screen(request).Select(ToDto).ToList();
                var status = matches.Count == 0 ? "no_match"
                    : matches.All(m => m.DateOfBirthConflict) ? "likely_different_person"
                    : "review";
                results[i] = new BatchItemResult(i, reference, request.Name, status, matches.Count, matches, null);
            });

            var summary = new BatchSummary(
                results.Length,
                results.Count(r => r.Status == "review"),
                results.Count(r => r.Status == "likely_different_person"),
                results.Count(r => r.Status == "no_match"),
                results.Count(r => r.Status == "error"));
            return Results.Ok(new BatchResponse(summary, results, body.MinScore ?? 85, ListInfo(loaded), Notice));
        });
    }

    internal static IResult Compare(CompareRequest body)
    {
        var errors = new Dictionary<string, string[]>();
        var name = Validated(() => new ScreeningRequest(body.Name!), "name", errors)?.Name;
        var other = Validated(() => new ScreeningRequest(body.OtherName!), "otherName", errors)?.Name;
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var companies = body.Companies ?? false;
        var score = new NameScorer().Score(
            NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(name!), companies),
            NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(other!), companies));

        return Results.Ok(new CompareResponse(
            score.Score,
            score.Technique.ToString(),
            score.FullNameScore,
            score.NamePartsScore,
            score.FullNameCounts,
            score.ScoredFromListedName,
            score.Parts.Select(ToDto).ToList()));
    }

    internal static async Task<IResult> GetEntry(int id, ListProvider provider, CancellationToken ct) =>
        await WithListAsync(provider, ct, loaded =>
            loaded.ById.TryGetValue(id, out var entry)
                ? Results.Ok(ToDetails(entry))
                : Results.Problem($"No SDN entry {id} in the list in use.", statusCode: StatusCodes.Status404NotFound));

    internal static ListDetails ListInfo(ListProvider.Loaded loaded) =>
        new(loaded.List.Entries.Count, loaded.List.Entries.Sum(e => e.Aliases.Count), loaded.Source, loaded.FilesDate.ToString("u"));

    private static async Task<IResult> WithListAsync(ListProvider provider, CancellationToken ct, Func<ListProvider.Loaded, IResult> handle)
    {
        try
        {
            return handle(await provider.GetAsync(ct));
        }
        catch (ListUnavailableException ex)
        {
            // Never answer "no match" when the list isn't there.
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable, title: "The SDN list is not available yet.");
        }
    }

    private static bool TryBuild(ScreenRequest body, double? minScore, int maxResults, out ScreeningRequest? request, out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();
        CheckMinScore(minScore, errors);
        CheckMaxResults(maxResults, errors);
        request = body.Name is null
            ? Fail<ScreeningRequest>("name", "Name is required.", errors)
            : Validated(() => new ScreeningRequest(body.Name), "name", errors);

        if (request is not null)
        {
            var built = request;
            if (body.Type is not null)
            {
                built.Type = ParseType(body.Type, errors);
            }

            Validated(() => built.YearOfBirth = body.YearOfBirth, "yearOfBirth", errors);
            Validated(() => built.Country = string.IsNullOrWhiteSpace(body.Country) ? null : body.Country.Trim(), "country", errors);
            if (minScore is double score && score is >= 0 and <= 100)
            {
                built.MinScore = score;
            }

            if (maxResults is >= 1 and <= MaxResultsLimit)
            {
                built.MaxResults = maxResults;
            }
        }

        if (errors.Count > 0)
        {
            request = null;
            return false;
        }

        return true;
    }

    private static void CheckMinScore(double? minScore, Dictionary<string, string[]> errors)
    {
        if (minScore is double score && !(score >= 0 && score <= 100))
        {
            errors["minScore"] = [$"minScore must be between 0 and 100; got {score}."];
        }
    }

    private static void CheckMaxResults(int maxResults, Dictionary<string, string[]> errors)
    {
        if (maxResults < 1 || maxResults > MaxResultsLimit)
        {
            errors["maxResults"] = [$"maxResults must be between 1 and {MaxResultsLimit}; got {maxResults}."];
        }
    }

    private static EntryType? ParseType(string type, Dictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(type) || string.Equals(type.Trim(), "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Enum.TryParse also accepts numbers such as "7", so compare against the names only.
        foreach (var value in Enum.GetValues<EntryType>())
        {
            if (string.Equals(value.ToString(), type.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        errors["type"] = [$"Unknown type \"{type}\". Use individual, entity, vessel or aircraft."];
        return null;
    }

    /// <summary>Runs a library check and turns its ArgumentException into a field error.</summary>
    private static T? Validated<T>(Func<T> create, string field, Dictionary<string, string[]> errors)
    {
        try
        {
            return create();
        }
        catch (ArgumentException ex)
        {
            var message = ex.Message.Split('\n')[0].Trim();
            if (ex.ParamName is not null)
            {
                message = message.Replace($" (Parameter '{ex.ParamName}')", string.Empty);
            }

            return Fail<T>(field, message, errors);
        }
    }

    private static T? Fail<T>(string field, string message, Dictionary<string, string[]> errors)
    {
        errors[field] = [message];
        return default;
    }

    private static MatchDto ToDto(ScreeningMatch match) =>
        new(
            match.Score,
            match.Entry.Id,
            match.Entry.Name,
            match.Entry.Type.ToString(),
            match.Entry.Programs.ToList(),
            match.MatchedName,
            match.NameKind.ToString(),
            match.NameScore.Technique.ToString(),
            match.DateOfBirthConflict,
            match.CountryConflict,
            match.NameScore.Parts.Select(ToDto).ToList(),
            // Same line breaks on every server, whatever its operating system.
            match.Explain().Replace("\r\n", "\n").TrimEnd());

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

/// <summary>One name to screen.</summary>
public sealed record ScreenRequest(string? Name, string? Type = null, int? YearOfBirth = null, string? Country = null, double? MinScore = null, int? MaxResults = null);

/// <summary>Up to 1,000 names to screen, with shared settings.</summary>
public sealed record BatchRequest(IReadOnlyList<BatchItem?>? Items, double? MinScore = null, int? MaxResults = null);

/// <summary>One item of a batch. Reference is yours (for example a customer ID) and is returned unchanged.</summary>
public sealed record BatchItem(string? Name = null, string? Reference = null, string? Type = null, int? YearOfBirth = null, string? Country = null)
{
    internal ScreenRequest ToScreen() => new(Name, Type, YearOfBirth, Country);
}

public sealed record CompareRequest(string? Name, string? OtherName, bool? Companies = null);

public sealed record ScreenResponse(string Name, int PotentialMatches, IReadOnlyList<MatchDto> Matches, double MinScore, ListDetails List, string Notice);

public sealed record BatchResponse(BatchSummary Summary, IReadOnlyList<BatchItemResult> Results, double MinScore, ListDetails List, string Notice);

/// <summary>Counts by status. "likely_different_person": every match has a birth year that differs; still review.</summary>
public sealed record BatchSummary(int Screened, int Review, int LikelyDifferentPerson, int NoMatch, int Errors);

/// <summary>Status is review, likely_different_person, no_match or error.</summary>
public sealed record BatchItemResult(int Index, string? Reference, string Name, string Status, int PotentialMatches, IReadOnlyList<MatchDto> Matches, string? Error);

public sealed record MatchDto(
    double Score,
    int EntryId,
    string ListedName,
    string Type,
    IReadOnlyList<string> Programs,
    string MatchedName,
    string MatchedNameKind,
    string Technique,
    bool DateOfBirthConflict,
    bool CountryConflict,
    IReadOnlyList<PartDto> Parts,
    string Explanation);

public sealed record PartDto(string QueryPart, string? MatchedPart, double Similarity, bool SoundsAlike, double Score);

public sealed record CompareResponse(double Score, string Technique, double FullNameScore, double NamePartsScore, bool FullNameCounts, bool ScoredFromListedName, IReadOnlyList<PartDto> Parts);

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

public sealed record ListDetails(int Entries, int Aliases, string Source, string DownloadedUtc);
