using System.Globalization;
using OfacMatch.Data;
using OfacMatch.Screening;

namespace OfacMatch.Api;

/// <summary>Where the SDN list comes from. Read from configuration, which includes environment variables.</summary>
public sealed class ListSettings
{
    /// <summary>OFACMATCH_DATA_DIR: a folder with SDN.CSV, ALT.CSV and ADD.CSV to use offline. Never downloads when set.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>OFACMATCH_CACHE_DIR: where downloaded files are kept.</summary>
    public string CacheDirectory { get; init; } = DefaultCacheDirectory();

    /// <summary>OFACMATCH_REFRESH_HOURS: how old the downloaded list may get before it is downloaded again. Default 24.</summary>
    public double RefreshHours { get; init; } = 24;

    public static ListSettings From(IConfiguration configuration)
    {
        var refresh = configuration["OFACMATCH_REFRESH_HOURS"];
        return new ListSettings
        {
            DataDirectory = NullIfEmpty(configuration["OFACMATCH_DATA_DIR"]),
            CacheDirectory = NullIfEmpty(configuration["OFACMATCH_CACHE_DIR"]) ?? DefaultCacheDirectory(),
            // Capped at a year so a huge value can't overflow TimeSpan.FromHours.
            RefreshHours = double.TryParse(refresh, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) && hours > 0 && hours <= 24 * 365 ? hours : 24,
        };
    }

    private static string DefaultCacheDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfacMatch", "sdn");

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>Thrown when the SDN list can't be loaded. The API answers 503 and never screens against a missing list.</summary>
public sealed class ListUnavailableException(string message, Exception inner) : Exception(message, inner);

/// <summary>Loads the SDN list, keeps it in memory and reloads it when it gets stale.</summary>
public sealed class ListProvider(ListSettings settings, ILogger<ListProvider> logger) : IDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Loaded? _current;

    /// <summary>The list in use, or null before the first load.</summary>
    public Loaded? Current => _current;

    /// <summary>Returns the current list, loading or refreshing it as needed.</summary>
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
                try
                {
                    _current = await LoadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (_current is not null && IsLoadFailure(ex, cancellationToken))
                {
                    // Keep screening against the previous copy rather than failing; try again next time.
                    logger.LogWarning(ex, "Could not refresh the SDN list; still using the copy from {Date:u}", _current.FilesDate);
                    return _current;
                }
            }

            return _current;
        }
        catch (Exception ex) when (IsLoadFailure(ex, cancellationToken))
        {
            logger.LogError(ex, "Could not load the SDN list");
            throw new ListUnavailableException($"Could not load the OFAC SDN list: {ex.Message}", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() => _lock.Dispose();

    private static bool IsLoadFailure(Exception ex, CancellationToken cancellationToken) =>
        ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private bool IsStale(Loaded loaded) =>
        settings.DataDirectory is null
        && DateTime.UtcNow - loaded.FilesDate > TimeSpan.FromHours(settings.RefreshHours);

    private async Task<Loaded> LoadAsync(CancellationToken cancellationToken)
    {
        if (settings.DataDirectory is string folder)
        {
            logger.LogInformation("Loading the SDN list from {Folder}", folder);
            return Index(SanctionsList.LoadFromDirectory(folder), FileDate(folder), "local folder");
        }

        var cache = settings.CacheDirectory;
        if (File.Exists(Path.Combine(cache, "SDN.CSV")) && DateTime.UtcNow - FileDate(cache) < TimeSpan.FromHours(settings.RefreshHours))
        {
            logger.LogInformation("Loading the downloaded SDN list from {Folder}", cache);
            return Index(SanctionsList.LoadFromDirectory(cache), FileDate(cache), "OFAC Sanctions List Service (cached)");
        }

        logger.LogInformation("Downloading the SDN list from OFAC into {Folder}", cache);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var list = await SanctionsList.DownloadAsync(http, cache, cancellationToken: cancellationToken).ConfigureAwait(false);
        return Index(list, DateTime.UtcNow, "OFAC Sanctions List Service");
    }

    private static Loaded Index(SanctionsList list, DateTime filesDate, string source) =>
        new(list, new SanctionsScreener(list), list.Entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First()), filesDate, source);

    private static DateTime FileDate(string folder) => File.GetLastWriteTimeUtc(Path.Combine(folder, "SDN.CSV"));

    /// <summary>A loaded list with its screener. Each request sets its own threshold.</summary>
    public sealed record Loaded(SanctionsList List, SanctionsScreener Screener, IReadOnlyDictionary<int, SanctionsEntry> ById, DateTime FilesDate, string Source);
}

/// <summary>Loads the list at start-up, so the first request doesn't wait, and checks for a newer list every hour.</summary>
public sealed class ListRefresher(ListProvider provider, ILogger<ListRefresher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await provider.GetAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (ListUnavailableException)
            {
                // Already logged; requests answer 503 until a load succeeds.
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                await Task.Delay(provider.Current is null ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        logger.LogDebug("List refresher stopped");
    }
}
