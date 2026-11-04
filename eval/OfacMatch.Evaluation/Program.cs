using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using OfacMatch.Data;
using OfacMatch.Evaluation;
using OfacMatch.Matching;
using OfacMatch.Screening;

// Measures how well OfacMatch finds listed parties under realistic name variations,
// and how often it alerts on ordinary names that are not on the list.
//
// Usage: dotnet run -c Release --project eval/OfacMatch.Evaluation -- [--data <folder>] [--out docs/evaluation.md]
//        [--individuals 400] [--entities 200] [--negatives 2000] [--seed 42] [--baselines] [--split dev|test --final]
// --baselines compares OfacMatch with standard methods and ablations instead (see Baselines.cs).
// --split dev (default) uses the development set. --split test uses the held-out test set: other
// listed entries and unseen ordinary names. It also needs --final; see docs/evaluation-protocol.md.
// --sets-only prints the size of each set and stops, without scoring anything.
// --tune compares candidate extra-part penalties on the development set (docs/evaluation-protocol.md).
// Runs that change --individuals, --entities, --negatives or --seed are not the published sets.
// --data defaults to ./ofac-data. If the folder has no SDN.CSV, the list is downloaded from OFAC first.
string data = "ofac-data";
string? output = null;
int individuals = 400, entities = 200, negatives = 2000, seed = 42;
var baselines = false;
var split = "dev";
var final = false;
var setsOnly = false;
var tune = false;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--data": data = Value(args, ref i); break;
        case "--out": output = Value(args, ref i); break;
        case "--individuals": individuals = Count(args, ref i); break;
        case "--entities": entities = Count(args, ref i); break;
        case "--negatives": negatives = Count(args, ref i); break;
        case "--seed": seed = Count(args, ref i); break;
        case "--baselines": baselines = true; break;
        case "--split": split = Value(args, ref i); break;
        case "--final": final = true; break;
        case "--sets-only": setsOnly = true; break;
        case "--tune": tune = true; break;
        default:
            Console.Error.WriteLine($"Unknown option {args[i]}.");
            return 1;
    }
}

if (split != "dev" && split != "test")
{
    Console.Error.WriteLine("--split must be dev or test.");
    return 1;
}

if (split == "test" && !final && !setsOnly)
{
    Console.Error.WriteLine("The held-out test set is run once, after the method is frozen. Add --final to confirm (see docs/evaluation-protocol.md).");
    return 1;
}

data = Path.GetFullPath(data);
SanctionsList list;
if (File.Exists(Path.Combine(data, "SDN.CSV")))
{
    list = SanctionsList.LoadFromDirectory(data);
}
else
{
    Console.WriteLine($"No SDN.CSV in {data}; downloading the list from OFAC...");
    using var http = new HttpClient();
    list = await SanctionsList.DownloadAsync(http, data);
}

Console.WriteLine($"Loaded {list.Entries.Count:N0} entries from {data}.");
var listDate = File.GetLastWriteTimeUtc(Path.Combine(data, "SDN.CSV"));
var random = new Random(seed);

var eligibleIndividuals = list.Entries.Where(e => e.Type == EntryType.Individual && NameNormalizer.Tokenize(e.Name).Count >= 2).ToList();
var eligibleEntities = list.Entries.Where(e => e.Type == EntryType.Entity && NameNormalizer.Tokenize(e.Name, true).Count >= 2).ToList();

// Development set. Always built, because the held-out set must avoid its entries.
// Order matters: each sample is drawn just before its variations, as in the published evaluation.
var cases = new List<TestCase>();
var sampledIndividuals = Sample(eligibleIndividuals, individuals, random);
cases.AddRange(Variations.ForIndividuals(sampledIndividuals, random));
var sampledEntities = Sample(eligibleEntities, entities, random);
cases.AddRange(Variations.ForEntities(sampledEntities, random));
var negativeNames = Variations.CommonNames(negatives, random);

// Harder cases, near-miss names and three-part names use their own generator, so the cases above are unchanged.
var hardRandom = new Random(seed + 1);
var hardCases = Variations.Harder(sampledIndividuals, sampledEntities, list, 200, hardRandom).ToList();
var nearMisses = Variations.NearMisses(sampledIndividuals, negatives / 2, hardRandom);
var threePartNames = Variations.ThreePartNames(negatives / 2, hardRandom, Variations.Development);
var setLabel = $"development set (seed {seed})";

if (split == "test")
{
    // Held-out test set: listed entries not used in development (nickname cases included),
    // a separate seed, and a vocabulary of ordinary names never used in development.
    var used = new HashSet<int>(sampledIndividuals.Concat(sampledEntities).Select(e => e.Id).Concat(hardCases.Select(c => c.EntryId)));
    var testRandom = new Random(seed + 1000);
    var testIndividuals = Sample(eligibleIndividuals.Where(e => !used.Contains(e.Id)), individuals, testRandom);
    var testEntities = Sample(eligibleEntities.Where(e => !used.Contains(e.Id)), entities, testRandom);
    cases = Variations.ForIndividuals(testIndividuals, testRandom).Concat(Variations.ForEntities(testEntities, testRandom)).ToList();
    negativeNames = Variations.CommonNames(negatives, testRandom, Variations.HeldOut);
    hardCases = Variations.Harder(testIndividuals, testEntities, list, 200, testRandom, used).ToList();
    nearMisses = Variations.NearMisses(testIndividuals, negatives / 2, testRandom, Variations.HeldOut);
    threePartNames = Variations.ThreePartNames(negatives / 2, testRandom, Variations.HeldOut);
    sampledIndividuals = testIndividuals;
    sampledEntities = testEntities;
    setLabel = $"held-out test set (seed {seed + 1000}, entries and ordinary names not used in development)";
}

Console.WriteLine($"Using the {setLabel}: {cases.Count:N0} variations, {hardCases.Count:N0} harder variations, {negativeNames.Count:N0} two-part and {threePartNames.Count:N0} three-part ordinary names, {nearMisses.Count:N0} near misses.");
if (setsOnly)
{
    // Describes the sets without scoring anything, so the held-out set can be checked without being used.
    foreach (var group in cases.Concat(hardCases).GroupBy(c => c.Variation).OrderBy(g => g.Key))
    {
        Console.WriteLine($"  {Variations.Describe(group.Key)}: {group.Count()}");
    }

    return 0;
}

if (tune)
{
    // Tries each candidate extra-part penalty on the development set, with the measures and the
    // selection rule written down in docs/evaluation-protocol.md before the first run.
    if (split != "dev")
    {
        Console.Error.WriteLine("Tuning uses the development set only.");
        return 1;
    }

    var allPositive = cases.Concat(hardCases).ToList();
    var ordinaryNames = negativeNames.Concat(threePartNames).ToList();
    var tuning = new StringBuilder();
    tuning.AppendLine("# Tuning the extra-part penalty (development set)");
    tuning.AppendLine();
    tuning.AppendLine($"Development set: {cases.Count:N0} original and {hardCases.Count:N0} harder variations, {negativeNames.Count:N0} two-part and {threePartNames.Count:N0} three-part ordinary names, {nearMisses.Count:N0} near misses. Thresholds for a fixed alert rate are set on all ordinary names. Selection rule: see [the evaluation protocol](evaluation-protocol.md).");
    tuning.AppendLine();
    tuning.AppendLine("| Penalty | All cases, alerts ≤ 5% (threshold) | All cases, alerts ≤ 10% | Original cases, alerts ≤ 1% | Extra middle name, alerts ≤ 5% | Extra middle name ≥ 85 | Three-part alerts ≥ 85 | Near-miss alerts, alerts ≤ 5% |");
    tuning.AppendLine("|---:|---:|---:|---:|---:|---:|---:|---:|");
    foreach (var penalty in new[] { 0.5, 0.4, 0.3, 0.2, 0.1, 0.0 })
    {
        var screener = new SanctionsScreener(list, new ScreeningOptions
        {
            MinScore = 50,
            MaxResults = 100_000,
            Scoring = new ScoringOptions { ExtraPartPenalty = penalty },
        });
        var positive = allPositive.AsParallel().AsOrdered()
            .Select(c => (Case: c, Score: screener.Screen(new ScreeningRequest(c.Query) { Type = c.Type }).FirstOrDefault(m => m.Entry.Id == c.EntryId)?.Score ?? 0.0))
            .ToList();
        double Top(string n) => screener.Screen(new ScreeningRequest(n) { Type = EntryType.Individual, MaxResults = 1 }).FirstOrDefault()?.Score ?? 0.0;
        var ordinary = ordinaryNames.AsParallel().Select(Top).OrderBy(s => s).ToArray();
        var three = threePartNames.AsParallel().Select(Top).ToList();
        var near = nearMisses.AsParallel().Select(Top).ToList();
        var extra = positive.Where(p => p.Case.Variation == Variation.ExtraMiddleName).ToList();
        var original = positive.Where(p => !p.Case.IsHarder).ToList();
        var t1 = Baselines.ThresholdFor(ordinary, 0.01);
        var t5 = Baselines.ThresholdFor(ordinary, 0.05);
        var t10 = Baselines.ThresholdFor(ordinary, 0.10);
        tuning.AppendLine($"| {penalty:0.0} | {Percent(positive.Count(p => p.Score >= t5), positive.Count)} ({t5:0.0}) | {Percent(positive.Count(p => p.Score >= t10), positive.Count)} ({t10:0.0}) | {Percent(original.Count(p => p.Score >= t1), original.Count)} ({t1:0.0}) | {Percent(extra.Count(p => p.Score >= t5), extra.Count)} | {Percent(extra.Count(p => p.Score >= 85), extra.Count)} | {Percent(three.Count(s => s >= 85), three.Count)} | {Percent(near.Count(s => s >= t5), near.Count)} |");
        Console.WriteLine($"Penalty {penalty:0.0}: done");
    }

    var tuningText = tuning.ToString();
    if (output is not null)
    {
        File.WriteAllText(output, tuningText, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {output}");
    }
    else
    {
        Console.WriteLine(tuningText);
    }

    return 0;
}

if (baselines)
{
    var baselineReport = Baselines.Run(list, cases, negativeNames, threePartNames, hardCases, nearMisses,
        $"Generated by `eval/OfacMatch.Evaluation --baselines` against the SDN list downloaded {listDate:yyyy-MM-dd} ({list.Entries.Count:N0} entries), on the {setLabel}: {cases.Count:N0} variations, {hardCases.Count:N0} harder variations, {negativeNames.Count:N0} two-part and {threePartNames.Count:N0} three-part ordinary names, and {nearMisses.Count:N0} near-miss names. See [the evaluation protocol](evaluation-protocol.md).");
    if (output is not null)
    {
        File.WriteAllText(output, baselineReport, new UTF8Encoding(false));
        Console.WriteLine($"Wrote {output}");
    }
    else
    {
        Console.WriteLine(baselineReport);
    }

    return 0;
}

var configurations = new (string Name, ScoringOptions Options)[]
{
    ("OfacMatch default", new ScoringOptions()),
    ("OFAC FAQ style (higher of the two techniques)", new ScoringOptions { RequireAllPartsForFullName = false }),
    ("Version 0.1.2 behaviour (extra searched parts count as misses)", new ScoringOptions { AllowExtraSearchedParts = false }),
};
var thresholds = new[] { 80.0, 85.0, 90.0, 95.0 };

var report = new StringBuilder();
report.AppendLine("# Evaluation");
report.AppendLine();
report.AppendLine($"Generated by `eval/OfacMatch.Evaluation` against the SDN list downloaded {listDate:yyyy-MM-dd} ({list.Entries.Count:N0} entries), on the {setLabel}. See [the evaluation protocol](evaluation-protocol.md).");
report.AppendLine();
report.AppendLine($"- **Positive cases:** {cases.Count:N0} variations of {individuals} individuals and {entities} entities sampled from the list. A case passes when the original entry is returned at or above the threshold.");
report.AppendLine($"- **Negative cases:** {negativeNames.Count:N0} two-part and {threePartNames.Count:N0} three-part ordinary names built from common first names and surnames across many cultures ({(split == "test" ? Variations.HeldOut.Label : Variations.Development.Label)} vocabulary). The alert rate is the share that return any match at or above the threshold. Some alerts are expected: a common name can genuinely resemble a listed name, and those still need a person's review.");
report.AppendLine();

foreach (var (configName, scoring) in configurations)
{
    var screener = new SanctionsScreener(list, new ScreeningOptions { MinScore = thresholds.Min(), MaxResults = 50, Scoring = scoring });
    var stopwatch = Stopwatch.StartNew();

    var positiveScores = new ConcurrentBag<(TestCase Case, double Score, bool Top)>();
    Parallel.ForEach(cases, c =>
    {
        var matches = screener.Screen(new ScreeningRequest(c.Query) { Type = c.Type });
        var hit = matches.FirstOrDefault(m => m.Entry.Id == c.EntryId);
        positiveScores.Add((c, hit?.Score ?? 0, matches.Count > 0 && matches[0].Entry.Id == c.EntryId));
    });

    var negativeScores = new ConcurrentBag<(string Name, double Score, string Matched)>();
    Parallel.ForEach(negativeNames, name =>
    {
        var best = screener.Screen(new ScreeningRequest(name) { Type = EntryType.Individual }).FirstOrDefault();
        negativeScores.Add((name, best?.Score ?? 0, best?.MatchedName ?? string.Empty));
    });

    stopwatch.Stop();
    var totalQueries = cases.Count + negativeNames.Count;

    // Scored after the timing, so the throughput below stays comparable with earlier runs.
    var threePartScores = threePartNames.AsParallel()
        .Select(n => screener.Screen(new ScreeningRequest(n) { Type = EntryType.Individual }).FirstOrDefault()?.Score ?? 0.0)
        .ToList();

    report.AppendLine($"## {configName}");
    report.AppendLine();
    report.AppendLine("### Recall by variation (share of listed parties found)");
    report.AppendLine();
    report.AppendLine("| Variation | Cases | " + string.Join(" | ", thresholds.Select(t => $"≥ {t:0}")) + " | Ranked first |");
    report.AppendLine("|---|---:|" + string.Concat(thresholds.Select(_ => "---:|")) + "---:|");
    foreach (var group in positiveScores.GroupBy(p => p.Case.Variation).OrderBy(g => g.Key))
    {
        report.AppendLine($"| {Variations.Describe(group.Key)} | {group.Count()} | "
            + string.Join(" | ", thresholds.Select(t => Percent(group.Count(p => p.Score >= t), group.Count())))
            + $" | {Percent(group.Count(p => p.Top), group.Count())} |");
    }

    report.AppendLine($"| **All** | **{positiveScores.Count}** | "
        + string.Join(" | ", thresholds.Select(t => $"**{Percent(positiveScores.Count(p => p.Score >= t), positiveScores.Count)}**"))
        + $" | **{Percent(positiveScores.Count(p => p.Top), positiveScores.Count)}** |");
    report.AppendLine();

    report.AppendLine("### Alert rate on ordinary names");
    report.AppendLine();
    report.AppendLine("| Threshold | Two-part names with an alert | Three-part names with an alert |");
    report.AppendLine("|---|---:|---:|");
    foreach (var t in thresholds)
    {
        report.AppendLine($"| ≥ {t:0} | {Percent(negativeScores.Count(n => n.Score >= t), negativeScores.Count)} | {Percent(threePartScores.Count(s => s >= t), threePartScores.Count)} |");
    }

    report.AppendLine();
    report.AppendLine($"{Percent(negativeScores.Count(n => n.Score >= 100), negativeScores.Count)} of these ordinary names are spelled exactly like a name on the list (score 100). No name-matching method can clear those without more information, such as a date of birth.");
    report.AppendLine();
    report.AppendLine("Highest-scoring ordinary names at ≥ 85, for review:");
    report.AppendLine();
    foreach (var n in negativeScores.Where(n => n.Score >= 85).OrderByDescending(n => n.Score).ThenBy(n => n.Name).Take(5))
    {
        report.AppendLine($"- {n.Name} → {n.Matched} ({n.Score:0.0})");
    }

    report.AppendLine();
    report.AppendLine($"Throughput: {totalQueries:N0} searches in {stopwatch.Elapsed.TotalSeconds:0.0} s on {Environment.ProcessorCount} threads ({stopwatch.Elapsed.TotalMilliseconds * Environment.ProcessorCount / totalQueries:0} ms of CPU per search).");
    report.AppendLine();

    // Harder cases are scored after the timing, so the throughput above stays comparable.
    var hardScores = hardCases.AsParallel().Select(c =>
    {
        var matches = screener.Screen(new ScreeningRequest(c.Query) { Type = c.Type });
        return (Case: c, Score: matches.FirstOrDefault(m => m.Entry.Id == c.EntryId)?.Score ?? 0.0, Top: matches.Count > 0 && matches[0].Entry.Id == c.EntryId);
    }).ToList();
    var nearMissScores = nearMisses.AsParallel()
        .Select(n => screener.Screen(new ScreeningRequest(n) { Type = EntryType.Individual }).FirstOrDefault()?.Score ?? 0.0)
        .ToList();

    report.AppendLine("### Harder variations");
    report.AppendLine();
    report.AppendLine("Variations that string similarity is expected to find hard, generated separately (seed + 1) so the cases above are unchanged. Nickname cases are drawn from the whole list, because few sampled names have a known nickname.");
    report.AppendLine();
    report.AppendLine("| Variation | Cases | " + string.Join(" | ", thresholds.Select(t => $"≥ {t:0}")) + " | Ranked first |");
    report.AppendLine("|---|---:|" + string.Concat(thresholds.Select(_ => "---:|")) + "---:|");
    foreach (var group in hardScores.GroupBy(p => p.Case.Variation).OrderBy(g => g.Key))
    {
        report.AppendLine($"| {Variations.Describe(group.Key)} | {group.Count()} | "
            + string.Join(" | ", thresholds.Select(t => Percent(group.Count(p => p.Score >= t), group.Count())))
            + $" | {Percent(group.Count(p => p.Top), group.Count())} |");
    }

    report.AppendLine($"| **All harder** | **{hardScores.Count}** | "
        + string.Join(" | ", thresholds.Select(t => $"**{Percent(hardScores.Count(p => p.Score >= t), hardScores.Count)}**"))
        + $" | **{Percent(hardScores.Count(p => p.Top), hardScores.Count)}** |");
    report.AppendLine();
    report.AppendLine($"Near-miss names ({nearMissScores.Count:N0} names that share a given name with a listed individual, such as \"AYMAN SMITH\"), share with an alert: "
        + string.Join(", ", thresholds.Select(t => $"≥ {t:0}: {Percent(nearMissScores.Count(s => s >= t), nearMissScores.Count)}")) + ".");
    report.AppendLine();

    Console.WriteLine($"{configName}: done in {stopwatch.Elapsed.TotalSeconds:0.0} s");
}

report.AppendLine("## Missed cases at ≥ 85 (default configuration)");
report.AppendLine();
report.AppendLine("Re-run with `--out` to regenerate. Misses show where matching can improve next.");

var finalScreener = new SanctionsScreener(list, new ScreeningOptions { MinScore = 0, MaxResults = int.MaxValue });
var misses = cases
    .AsParallel()
    .Select(c => (Case: c, Score: finalScreener.Screen(new ScreeningRequest(c.Query) { Type = c.Type }).FirstOrDefault(m => m.Entry.Id == c.EntryId)?.Score ?? 0))
    .Where(x => x.Score < 85)
    .ToList()
    .OrderBy(x => x.Case.EntryId)
    .ThenBy(x => x.Case.Query, StringComparer.Ordinal)
    .GroupBy(x => x.Case.Variation)
    .OrderBy(g => g.Key);
report.AppendLine();
foreach (var group in misses)
{
    report.AppendLine($"- **{Variations.Describe(group.Key)}:** " + string.Join("; ", group.Take(3).Select(x => $"\"{x.Case.Query}\" for \"{x.Case.Original}\" ({x.Score:0.0})")));
}

var text = report.ToString();
if (output is not null)
{
    File.WriteAllText(output, text, new UTF8Encoding(false));
    Console.WriteLine($"Wrote {output}");
}
else
{
    Console.WriteLine(text);
}

return 0;

static List<SanctionsEntry> Sample(IEnumerable<SanctionsEntry> source, int count, Random random) =>
    source.OrderBy(e => e.Id).Select(e => (Entry: e, Key: random.Next())).OrderBy(x => x.Key).Take(count).Select(x => x.Entry).ToList();

static string Percent(int part, int whole) =>
    whole == 0 ? "–" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";

static string Value(string[] args, ref int i)
{
    if (i + 1 >= args.Length)
    {
        throw new ArgumentException($"{args[i]} needs a value.");
    }

    return args[++i];
}

static int Count(string[] args, ref int i)
{
    var option = args[i];
    var text = Value(args, ref i);
    return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 0
        ? n
        : throw new ArgumentException($"{option} needs a whole number of 0 or more; got \"{text}\".");
}
