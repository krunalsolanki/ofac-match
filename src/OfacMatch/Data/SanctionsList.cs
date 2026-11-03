using System.Globalization;
using System.Text;

namespace OfacMatch.Data;

/// <summary>
/// The OFAC SDN list loaded into memory. Load it from OFAC's legacy flat files
/// (SDN.CSV, ALT.CSV, ADD.CSV) on disk, from streams, or straight from OFAC's
/// Sanctions List Service.
/// </summary>
public sealed class SanctionsList
{
    /// <summary>Base address of OFAC's Sanctions List Service exports.</summary>
    public const string DefaultBaseUrl = "https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/";

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

    /// <summary>Loads the list from the three legacy flat files. Aliases and addresses are optional.</summary>
    /// <exception cref="InvalidDataException">The SDN data has no entries, so it is not an SDN file.</exception>
    public static SanctionsList Load(TextReader sdn, TextReader? alt = null, TextReader? add = null)
    {
        Guard.NotNull(sdn, nameof(sdn));

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
                Field(row, 11));
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

    /// <summary>Loads SDN.CSV, and ALT.CSV and ADD.CSV when present, from a folder.</summary>
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
        return Load(sdn, alt, add);
    }

    /// <summary>
    /// Downloads SDN.CSV, ALT.CSV and ADD.CSV from OFAC's Sanctions List Service and loads them.
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

        var list = Load(new StringReader(sdn), new StringReader(alt), new StringReader(add));

        if (saveToDirectory is not null)
        {
            Directory.CreateDirectory(saveToDirectory);
            File.WriteAllText(Path.Combine(saveToDirectory, "SDN.CSV"), sdn, Encoding.UTF8);
            File.WriteAllText(Path.Combine(saveToDirectory, "ALT.CSV"), alt, Encoding.UTF8);
            File.WriteAllText(Path.Combine(saveToDirectory, "ADD.CSV"), add, Encoding.UTF8);
        }

        return list;
    }

    private static async Task<string> DownloadTextAsync(HttpClient client, string url, CancellationToken ct)
    {
        // OFAC's service answers 403 Forbidden to requests without a User-Agent,
        // which HttpClient does not send by default.
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.ParseAdd(UserAgent);
        }

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return Encoding.UTF8.GetString(bytes);
    }

    private static StreamReader? OpenText(string path) =>
        File.Exists(path) ? new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true) : null;

    private static string? Field(string?[] row, int index) => index < row.Length ? row[index] : null;

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
