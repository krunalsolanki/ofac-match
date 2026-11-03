using System.Net.Http.Json;
using OfacMatch.Data;
using OfacMatch.Screening;

namespace OfacMatch.Web;

/// <summary>
/// Loads the SDN list once from the copy published with the site (data/), and keeps the screener for the session.
/// Browsers can't download the list from OFAC directly, so a GitHub Action refreshes data/ every day.
/// </summary>
public sealed class SanctionsData(HttpClient http)
{
    private Task<SanctionsScreener>? _loading;

    public int Entries { get; private set; }

    public int Aliases { get; private set; }

    /// <summary>When the published copy was downloaded from OFAC, or null if unknown.</summary>
    public DateTimeOffset? DownloadedUtc { get; private set; }

    public Task<SanctionsScreener> GetAsync() => _loading ??= LoadAsync();

    private async Task<SanctionsScreener> LoadAsync()
    {
        var info = await http.GetFromJsonAsync<ListInfo>("data/info.json");
        DownloadedUtc = info?.DownloadedUtc;

        var files = await Task.WhenAll(
            http.GetStringAsync("data/SDN.CSV"),
            http.GetStringAsync("data/ALT.CSV"),
            http.GetStringAsync("data/ADD.CSV"));

        var list = SanctionsList.Load(new StringReader(files[0]), new StringReader(files[1]), new StringReader(files[2]));
        Entries = list.Entries.Count;
        Aliases = list.Entries.Sum(e => e.Aliases.Count);
        return new SanctionsScreener(list);
    }

    private sealed record ListInfo(DateTimeOffset? DownloadedUtc);
}
