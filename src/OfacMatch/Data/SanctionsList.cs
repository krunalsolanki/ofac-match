using System.Globalization;
using System.Text;

namespace OfacMatch.Data;

/// <summary>
/// The OFAC SDN list loaded into memory. Load it from OFAC's legacy flat files
/// (SDN.CSV, ALT.CSV, ADD.CSV and SDN_COMMENTS.CSV) on disk, from streams, or straight from OFAC's
/// Sanctions List Service.
/// </summary>
public sealed class SanctionsList
{
    /// <summary>Base address of OFAC's Sanctions List Service exports.</summary>
    public const string DefaultBaseUrl = "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/";

    private const string CommentsFile = "SDN_COMMENTS.CSV";

    /// <summary>User-Agent sent with downloads when the <see cref="HttpClient"/> has none of its own.</summary>
    public const string UserAgent = "OfacMatch/0.1 (+https://github.com/krunalsolanki/ofac-match)";

    private SanctionsList(IReadOnlyList<SanctionsEntry> entries, DateTimeOffset loadedAt)
    {
        Entries = entries;
        LoadedAt = loadedAt;
    }

    /// <summary>All entries on the list.</summary>
    public IReadOnlyList<SanctionsEntry> Entries { get; }

    /// <summary>When this copy of the list was loaded. Screen against a current copy: OFAC updates the list often.</summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>Loads the list from the legacy flat files. Aliases and addresses are optional.</summary>
    /// <exception cref="InvalidDataException">The SDN data has no entries, so it is not an SDN file.</exception>
    public static SanctionsList Load(TextReader sdn, TextReader? alt = null, TextReader? add = null) =>
        Load(sdn, alt, add, comments: null);

    /// <summary>
    /// Loads the list from the legacy flat files, including SDN_COMMENTS.CSV. SDN.CSV cuts remarks off at
    /// 1,000 characters; SDN_COMMENTS.CSV holds the rest, which often includes identifiers such as email
    /// addresses and websites. Aliases, addresses and comments are optional.
    /// </summary>
    /// <exception cref="InvalidDataException">The SDN data has no entries, so it is not an SDN file.</exception>
    public static SanctionsList Load(TextReader sdn, TextReader? alt, TextReader? add, TextReader? comments)
    {
        Guard.NotNull(sdn, nameof(sdn));

        var overflow = ReadComments(comments);
        var entries = new List<SanctionsEntry>();
        var byId = new Dictionary<int, SanctionsEntry>();

        foreach (var row in CsvReader.Read(sdn))
        {
            if (!TryParseId(row, out var id) || Field(row, 1) is not string name)
            {
                continue;
            }

            var entry = new SanctionsEntry(
                id,
                name,
                ParseType(Field(row, 2)),
                SplitPrograms(Field(row, 3)),
                Field(row, 4),
                overflow.TryGetValue(id, out var rest) ? Field(row, 11) + rest : Field(row, 11));
            entries.Add(entry);
            byId[id] = entry;
        }

        // An empty list would clear every name, so treat it as bad data rather than a clean result.
        // This catches an empty file, a truncated download or an HTML error page saved as SDN.CSV.
        if (entries.Count == 0)
        {
            throw new InvalidDataException("The SDN data contains no entries. Check that it is OFAC's SDN.CSV file.");
        }

        if (alt is not null)
        {
            foreach (var row in CsvReader.Read(alt))
            {
                if (TryParseId(row, out var id)
                    && byId.TryGetValue(id, out var entry)
                    && Field(row, 3) is string altName)
                {
                    entry.Aliases.Add(new SanctionsAlias(ParseKind(Field(row, 2)), altName, Field(row, 4)));
                }
            }
        }

        if (add is not null)
        {
            foreach (var row in CsvReader.Read(add))
            {
                if (TryParseId(row, out var id) && byId.TryGetValue(id, out var entry))
                {
                    entry.Addresses.Add(new SanctionsAddress(Field(row, 2), Field(row, 3), Field(row, 4)));
                }
            }
        }

        return new SanctionsList(entries, DateTimeOffset.UtcNow);
    }

    /// <summary>Loads SDN.CSV, and ALT.CSV, ADD.CSV and SDN_COMMENTS.CSV when present, from a folder.</summary>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is empty.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    /// <exception cref="FileNotFoundException">The folder has no SDN.CSV.</exception>
    public static SanctionsList LoadFromDirectory(string directory)
    {
        Guard.NotNull(directory, nameof(directory));
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Directory must not be empty.", nameof(directory));
        }

        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Folder not found: {directory}");
        }

        using var sdn = OpenText(Path.Combine(directory, "SDN.CSV"))
            ?? throw new FileNotFoundException("SDN.CSV not found.", Path.Combine(directory, "SDN.CSV"));
        using var alt = OpenText(Path.Combine(directory, "ALT.CSV"));
        using var add = OpenText(Path.Combine(directory, "ADD.CSV"));
        using var comments = OpenText(Path.Combine(directory, CommentsFile));
        return Load(sdn, alt, add, comments);
    }

    /// <summary>
    /// Downloads SDN.CSV, ALT.CSV, ADD.CSV and SDN_COMMENTS.CSV from OFAC's Sanctions List Service and loads them.
    /// When <paramref name="saveToDirectory"/> is given, the files are also saved there for offline use,
    /// but only after they load successfully, so a bad download never replaces a good copy.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="baseUrl"/> is not an absolute http or https address.</exception>
    /// <exception cref="InvalidDataException">The downloaded SDN file has no entries.</exception>
    public static async Task<SanctionsList> DownloadAsync(
        HttpClient httpClient,
        string? saveToDirectory = null,
        string baseUrl = DefaultBaseUrl,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNull(httpClient, nameof(httpClient));
        Guard.NotNull(baseUrl, nameof(baseUrl));
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Base URL must be an absolute http or https address.", nameof(baseUrl));
        }

        if (saveToDirectory is not null && string.IsNullOrWhiteSpace(saveToDirectory))
        {
            throw new ArgumentException("Save directory must not be empty.", nameof(saveToDirectory));
        }

        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
        {
            baseUrl += "/";
        }

        var sdn = await DownloadTextAsync(httpClient, baseUrl + "SDN.CSV", cancellationToken).ConfigureAwait(false);
        var alt = await DownloadTextAsync(httpClient, baseUrl + "ALT.CSV", cancellationToken).ConfigureAwait(false);
        var add = await DownloadTextAsync(httpClient, baseUrl + "ADD.CSV", cancellationToken).ConfigureAwait(false);
        var comments = await DownloadOptionalTextAsync(httpClient, baseUrl + CommentsFile, cancellationToken).ConfigureAwait(false);

        var list = Load(
            new StringReader(sdn),
            new StringReader(alt),
            new StringReader(add),
            comments is null ? null : new StringReader(comments));

        if (saveToDirectory is not null)
        {
            Directory.CreateDirectory(saveToDirectory);
            File.WriteAllText(Path.Combine(saveToDirectory, "SDN.CSV"), sdn, Encoding.UTF8);
            File.WriteAllText(Path.Combine(saveToDirectory, "ALT.CSV"), alt, Encoding.UTF8);
            File.WriteAllText(Path.Combine(saveToDirectory, "ADD.CSV"), add, Encoding.UTF8);
            var commentsPath = Path.Combine(saveToDirectory, CommentsFile);
            if (comments is not null)
            {
                File.WriteAllText(commentsPath, comments, Encoding.UTF8);
            }
            else if (File.Exists(commentsPath))
            {
                // Don't pair an old comments file with a new SDN.CSV.
                File.Delete(commentsPath);
            }
        }

        return list;
    }

    private static async Task<string> DownloadTextAsync(HttpClient client, string url, CancellationToken ct) =>
        await DownloadTextAsync(client, url, optional: false, ct).ConfigureAwait(false)
        ?? throw new InvalidOperationException("Unreachable: a required download returned nothing.");

    // SDN_COMMENTS.CSV only adds detail, so for it a missing file (404) returns null. Other failures still throw.
    private static Task<string?> DownloadOptionalTextAsync(HttpClient client, string url, CancellationToken ct) =>
        DownloadTextAsync(client, url, optional: true, ct);

    private static async Task<string?> DownloadTextAsync(HttpClient client, string url, bool optional, CancellationToken ct)
    {
        // OFAC's service answers 403 Forbidden to requests without a User-Agent,
        // which HttpClient does not send by default.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.ParseAdd(UserAgent);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        if (optional && response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private static StreamReader? OpenText(string path) =>
        File.Exists(path) ? new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true) : null;

    private static string? Field(string?[] row, int index) => index < row.Length ? row[index] : null;

    // SDN_COMMENTS.CSV rows are (ent_num, the rest of the remarks). The text continues mid-word,
    // so it is appended as is, without a separator.
    private static Dictionary<int, string> ReadComments(TextReader? comments)
    {
        var overflow = new Dictionary<int, string>();
        if (comments is null)
        {
            return overflow;
        }

        foreach (var row in CsvReader.Read(comments))
        {
            if (TryParseId(row, out var id) && Field(row, 1) is string text)
            {
                overflow[id] = overflow.TryGetValue(id, out var earlier) ? earlier + text : text;
            }
        }

        return overflow;
    }

    private static bool TryParseId(string?[] row, out int id) =>
        int.TryParse(Field(row, 0), NumberStyles.Integer, CultureInfo.InvariantCulture, out id);

    private static EntryType ParseType(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "individual" => EntryType.Individual,
        "vessel" => EntryType.Vessel,
        "aircraft" => EntryType.Aircraft,
        _ => EntryType.Entity,
    };

    private static NameKind ParseKind(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "fka" => NameKind.Fka,
        "nka" => NameKind.Nka,
        _ => NameKind.Aka,
    };

    private static IReadOnlyList<string> SplitPrograms(string? value) =>
        value is null
            ? Array.Empty<string>()
            : value.Split(new[] { ']', '[', ' ' }, StringSplitOptions.RemoveEmptyEntries);
}
