using System.Globalization;
using OfacMatch.Data;
using OfacMatch.Screening;

// Usage: ofac-match "<name>" [--min 85] [--yob 1970] [--country "Freedonia"] [--individuals] [--data <folder>]
// Without --data, the latest list is downloaded from OFAC and cached in ./ofac-data.
if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.WriteLine("Usage: ofac-match \"<name>\" [--min 85] [--yob 1970] [--country <name>] [--individuals] [--data <folder>]");
    return 1;
}

var request = new ScreeningRequest(args[0]);
var options = new ScreeningOptions();
string? dataFolder = null;

for (var i = 1; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--min":
            options.MinScore = double.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--yob":
            request.YearOfBirth = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--country":
            request.Country = args[++i];
            break;
        case "--individuals":
            request.Type = EntryType.Individual;
            break;
        case "--data":
            dataFolder = args[++i];
            break;
    }
}

// --data <folder> uses the files there, downloading them first if the folder has none.
// Without --data, ./ofac-data is used as a cache and refreshed once a day.
var folder = Path.GetFullPath(dataFolder ?? "ofac-data");
var sdnFile = Path.Combine(folder, "SDN.CSV");
var fresh = File.Exists(sdnFile) && (dataFolder is not null || File.GetLastWriteTimeUtc(sdnFile) > DateTime.UtcNow.AddDays(-1));

SanctionsList list;
if (fresh)
{
    list = SanctionsList.LoadFromDirectory(folder);
}
else
{
    Console.WriteLine($"Downloading the SDN list from OFAC into {folder} ...");
    using var http = new HttpClient();
    list = await SanctionsList.DownloadAsync(http, folder);
}

var screener = new SanctionsScreener(list, options);
var stopwatch = System.Diagnostics.Stopwatch.StartNew();
var matches = screener.Screen(request);
stopwatch.Stop();

Console.WriteLine($"{list.Entries.Count:N0} entries loaded. {matches.Count} potential match(es) at score >= {options.MinScore} ({stopwatch.ElapsedMilliseconds} ms).");
Console.WriteLine();
foreach (var match in matches)
{
    Console.WriteLine(match.Explain());
}

return matches.Count > 0 ? 2 : 0;
