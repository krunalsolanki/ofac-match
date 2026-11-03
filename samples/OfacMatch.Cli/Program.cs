using System.Globalization;
using OfacMatch.Data;
using OfacMatch.Screening;

// Usage: ofac-match "<name>" [--min 85] [--yob 1970] [--country "Freedonia"] [--individuals] [--data <folder>]
// Without --data, the latest list is downloaded from OFAC and cached in ./ofac-data.
const string Usage = "Usage: ofac-match \"<name>\" [--min 85] [--yob 1970] [--country <name>] [--individuals] [--data <folder>]";

if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
{
    Console.WriteLine(Usage);
    return 1;
}

ScreeningRequest request;
var options = new ScreeningOptions();
string? dataFolder = null;
var option = "name";

try
{
    request = new ScreeningRequest(args[0]);

    for (var i = 1; i < args.Length; i++)
    {
        option = args[i];
        switch (args[i])
        {
            case "--min":
                options.MinScore = ParseNumber(args, ref i);
                break;
            case "--yob":
                request.YearOfBirth = (int)ParseNumber(args, ref i, wholeNumber: true);
                break;
            case "--country":
                request.Country = Value(args, ref i);
                break;
            case "--individuals":
                request.Type = EntryType.Individual;
                break;
            case "--data":
                dataFolder = Value(args, ref i);
                break;
            default:
                throw new ArgumentException($"Unknown option {args[i]}.");
        }
    }
}
catch (ArgumentException ex)
{
    // Show "--min: Must be between 0 and 100." rather than .NET's "(Parameter 'value')" wording.
    var message = ex.Message.Split('\n')[0].Trim();
    if (ex.ParamName is not null)
    {
        message = $"{option}: {message.Replace($" (Parameter '{ex.ParamName}')", string.Empty)}";
    }

    Console.Error.WriteLine(message);
    Console.Error.WriteLine(Usage);
    return 1;
}

// --data <folder> uses the files there, downloading them first if the folder has none.
// Without --data, ./ofac-data is used as a cache and refreshed once a day.
var folder = Path.GetFullPath(dataFolder ?? "ofac-data");
var sdnFile = Path.Combine(folder, "SDN.CSV");
var fresh = File.Exists(sdnFile) && (dataFolder is not null || File.GetLastWriteTimeUtc(sdnFile) > DateTime.UtcNow.AddDays(-1));

SanctionsList list;
try
{
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
}
catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or UnauthorizedAccessException)
{
    // Exit code 3, not 0: a list that failed to load must never look like "no matches".
    Console.Error.WriteLine($"Could not load the SDN list: {ex.Message}");
    return 3;
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

static string Value(string[] args, ref int i)
{
    var option = args[i];
    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
    {
        throw new ArgumentException($"{option} needs a value.");
    }

    return args[++i];
}

static double ParseNumber(string[] args, ref int i, bool wholeNumber = false)
{
    var option = args[i];
    var text = Value(args, ref i);
    var styles = wholeNumber ? NumberStyles.Integer : NumberStyles.Float;
    if (!double.TryParse(text, styles, CultureInfo.InvariantCulture, out var number) || double.IsNaN(number) || double.IsInfinity(number))
    {
        throw new ArgumentException($"{option} needs a number; got \"{text}\".");
    }

    if (wholeNumber && (number < int.MinValue || number > int.MaxValue))
    {
        throw new ArgumentException($"{option} is out of range; got {text}.");
    }

    return number;
}
