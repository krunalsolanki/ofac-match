using System.Globalization;
using Microsoft.Extensions.Logging;
using OfacMatch.Data;
using OfacMatch.Screening;

namespace OfacMatch.Mcp;

/// <summary>Where the SDN list comes from, read from environment variables.</summary>
public sealed class ListSettings
{
    /// <summary>OFACMATCH_DATA_DIR: a folder with SDN.CSV, ALT.CSV, ADD.CSV to use offline. Never downloads when set.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>OFACMATCH_CACHE_DIR: where downloaded files are cached. Default: the user's local app data folder.</summary>
    public string CacheDirectory { get; init; } = DefaultCacheDirectory();

    /// <summary>OFACMATCH_REFRESH_HOURS: how old the cached list may get before it is downloaded again. Default 24.</summary>
    public double RefreshHours { get; init; } = 24;

    public static ListSettings FromEnvironment()
    {
        var refresh = Environment.GetEnvironmentVariable("OFACMATCH_REFRESH_HOURS");
        return new ListSettings
        {
            DataDirectory = NullIfEmpty(Environment.GetEnvironmentVariable("OFACMATCH_DATA_DIR")),
            CacheDirectory = NullIfEmpty(Environment.GetEnvironmentVariable("OFACMATCH_CACHE_DIR")) ?? DefaultCacheDirectory(),
            RefreshHours = double.TryParse(refresh, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0 ? hours : 24,
        };
    }

    private static string DefaultCacheDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfacMatch", "sdn");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Loads the SDN list on first use, keeps it in memory, and reloads it when it gets stale.</summary>
public sealed class SanctionsListProvider : IDisposable
{
    private readonly ListSettings _settings;
    private readonly ILogger<SanctionsListProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Loaded? _current;

    public SanctionsListProvider(ListSettings settings, ILogger<SanctionsListProvider> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Returns the current list and an index over it, loading or refreshing as needed.</summary>
    public async Task<Loaded> GetAsync(CancellationToken cancellationToken)
    {
        var current = _current;
        if (current is not null && !IsStale(current))
        {
            return current;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_current is null || IsStale(_current))
            {
                _current = await LoadAsync(cancellationToken).ConfigureAwait(false);
            }

            return _current;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private bool IsStale(Loaded loaded) =>
        _settings.DataDirectory is null
        && DateTime.UtcNow - loaded.FilesDate > TimeSpan.FromHours(_settings.RefreshHours);

    private async Task<Loaded> LoadAsync(CancellationToken cancellationToken)
    {
        if (_settings.DataDirectory is string folder)
        {
            _logger.LogInformation("Loading the SDN list from {Folder}", folder);
            return Index(SanctionsList.LoadFromDirectory(folder), FileDate(folder), $"local folder {folder}");
        }

        var cache = _settings.CacheDirectory;
        if (File.Exists(Path.Combine(cache, "SDN.CSV")) && DateTime.UtcNow - FileDate(cache) < TimeSpan.FromHours(_settings.RefreshHours))
        {
            _logger.LogInformation("Loading the cached SDN list from {Folder}", cache);
            return Index(SanctionsList.LoadFromDirectory(cache), FileDate(cache), "OFAC Sanctions List Service (cached)");
        }

        _logger.LogInformation("Downloading the SDN list from OFAC into {Folder}", cache);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var list = await SanctionsList.DownloadAsync(http, cache, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Index(list, DateTime.UtcNow, "OFAC Sanctions List Service");
    }

    private static Loaded Index(SanctionsList list, DateTime filesDate, string source) =>
        new(list, new SanctionsScreener(list, new ScreeningOptions()), filesDate, source);

    private static DateTime FileDate(string folder) => File.GetLastWriteTimeUtc(Path.Combine(folder, "SDN.CSV"));

    /// <summary>A loaded list with its screener; each tool call sets its own threshold.</summary>
    public sealed record Loaded(SanctionsList List, SanctionsScreener Screener, DateTime FilesDate, string Source);
}
