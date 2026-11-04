using System.Diagnostics;
using System.Globalization;
using System.Text;
using OfacMatch.Data;
using OfacMatch.Matching;
using OfacMatch.Screening;

namespace OfacMatch.Evaluation;

/// <summary>
/// Measures the pipeline stages around matching: blocking, conflict flags and explanations.
/// What is measured, and every setting, is fixed in docs/evaluation-protocol.md#pipeline-measurements.
/// </summary>
internal static partial class Baselines
{
    private static readonly string[] BlockingNames =
    {
        "None (every listed name)", "First letter (OfacMatch's pre-filter)", "Soundex", "Trigram index, k = 2", "Trigram index, k = 3",
    };

    private const int TimedSearches = 1000;

    public static string RunPipeline(SanctionsList list, IReadOnlyList<TestCase> cases, IReadOnlyList<TestCase> hardCases,
        IReadOnlyList<string> twoPart, IReadOnlyList<string> threePart, IReadOnlyList<string> nearMisses, int seed, string header)
    {
        var positives = cases.Concat(hardCases).ToList();
        var r = new StringBuilder();
        r.AppendLine("# Pipeline stages: blocking, conflict flags and explanations");
        r.AppendLine();
        r.AppendLine(header);
        r.AppendLine();
        r.AppendLine("What is measured, and every setting, was written down before the first run: see [the evaluation protocol](evaluation-protocol.md#pipeline-measurements). Matching is the frozen default of release 2.0.0 throughout.");
        Blocking(list, positives, twoPart.Concat(threePart).ToList(), r);
        Flags(list, positives, twoPart, threePart, nearMisses, seed, r);
        Explanations(list, positives, twoPart.Concat(threePart).Concat(nearMisses).ToList(), r);
        return r.ToString();
    }

    private static void Blocking(SanctionsList list, List<TestCase> positives, List<string> ordinary, StringBuilder r)
    {
        var listed = new List<NameData>();
        foreach (var entry in list.Entries)
        {
            Add(listed, entry, entry.Name);
            foreach (var alias in entry.Aliases)
            {
                Add(listed, entry, alias.Name);
            }
        }

        var gramIds = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in listed)
        {
            name.Grams = GramIds(name.Tokens, gramIds, add: true);
        }

        var score = Parts(new PartsOptions());
        var count = BlockingNames.Length;

        (bool[] TargetKept, double[] Target, double[] Other, long[] Kept, long Total) Evaluate(string name, EntryType type, int targetId)
        {
            var query = Query.From(name, null);
            var plainGrams = GramIds(query.Plain.Tokens, gramIds, add: false);
            var entityGrams = GramIds(query.Entity.Tokens, gramIds, add: false);
            var targetKept = new bool[count];
            var target = new double[count];
            var other = new double[count];
            var kept = new long[count];
            var keep = new bool[count];
            long total = 0;
            foreach (var l in listed)
            {
                if (l.Type != type)
                {
                    continue;
                }

                total++;
                var individual = l.Type == EntryType.Individual;
                var q = individual ? query.Plain : query.Entity;
                var shared = SharedCount(individual ? plainGrams : entityGrams, l.Grams!);
                keep[0] = true;
                keep[1] = (query.Initials & l.Initials) != 0;
                keep[2] = SharesCode(q.Codes, l.Codes);
                keep[3] = shared >= 2;
                keep[4] = shared >= 3;
                var s = Round(score(q, l));
                for (var m = 0; m < count; m++)
                {
                    if (!keep[m])
                    {
                        continue;
                    }

                    kept[m]++;
                    if (l.EntryId == targetId)
                    {
                        targetKept[m] = true;
                        target[m] = Math.Max(target[m], s);
                    }
                    else
                    {
                        other[m] = Math.Max(other[m], s);
                    }
                }
            }

            return (targetKept, target, other, kept, total);
        }

        var pos = positives.AsParallel().AsOrdered().Select(c => (Case: c, Result: Evaluate(c.Query, c.Type, c.EntryId))).ToList();
        var neg = ordinary.AsParallel().AsOrdered().Select(n => Evaluate(n, EntryType.Individual, -1)).ToList();
        Console.WriteLine($"Pipeline: blocking measured on {pos.Count + neg.Count:N0} searches");

        var timings = TimeBlocking(listed, gramIds, positives.Take(TimedSearches).ToList(), score);

        var original = pos.Where(p => !p.Case.IsHarder).ToList();
        var harder = pos.Where(p => p.Case.IsHarder).ToList();
        r.AppendLine();
        r.AppendLine("## Blocking");
        r.AppendLine();
        r.AppendLine("Blocking decides which listed names are compared with a search at all; every name it keeps is then scored with OfacMatch's default scoring. *Pairs completeness* is the share of positive cases whose listed entry still has at least one name kept. *Reduction ratio* is the share of name comparisons skipped, over all searches. Recall and alerts are at a threshold of 85.");
        r.AppendLine();
        r.AppendLine($"| Blocking | Pairs completeness ({pos.Count:N0}) | Recall, original ({original.Count:N0}) | Recall, harder ({harder.Count:N0}) | Reduction ratio | Names compared per search | Ordinary names that alert ({neg.Count:N0}) | Time per search |");
        r.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        var allSearches = pos.Select(p => p.Result).Concat(neg).ToList();
        var totalPairs = allSearches.Sum(x => x.Total);
        for (var m = 0; m < count; m++)
        {
            var keptPairs = allSearches.Sum(x => x.Kept[m]);
            r.AppendLine($"| {BlockingNames[m]} | {Percent(pos.Count(p => p.Result.TargetKept[m]), pos.Count)} | {Percent(original.Count(p => p.Result.Target[m] >= 85), original.Count)} | {Percent(harder.Count(p => p.Result.Target[m] >= 85), harder.Count)} | {(100.0 * (1 - (double)keptPairs / totalPairs)).ToString("0.0", CultureInfo.InvariantCulture)}% | {((double)keptPairs / allSearches.Count).ToString("N0", CultureInfo.InvariantCulture)} | {Percent(neg.Count(n => n.Other[m] >= 85), neg.Count)} | {timings[m].ToString("0.0", CultureInfo.InvariantCulture)} ms |");
        }

        r.AppendLine();
        r.AppendLine($"Time per search: single thread, the first {Math.Min(TimedSearches, positives.Count):N0} positive searches, including finding candidates (a linear scan for none and first letter, an inverted index for Soundex and trigrams) and scoring them with the evaluation's copy of OfacMatch's scorer. Absolute times depend on the machine; compare the rows with each other.");

        var lost = pos.Where(p => !p.Result.TargetKept[1]).Take(5).ToList();
        if (lost.Count > 0)
        {
            r.AppendLine();
            r.AppendLine("Cases the first-letter pre-filter loses: " + string.Join("; ", lost.Select(p => $"\"{p.Case.Query}\" for \"{p.Case.Original}\"")) + ".");
        }
    }

    private static double[] TimeBlocking(List<NameData> listed, Dictionary<string, int> gramIds, List<TestCase> searches, Func<NameData, NameData, double> score)
    {
        var codeIndex = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var postings = new List<int>[gramIds.Count];
        for (var i = 0; i < listed.Count; i++)
        {
            foreach (var code in listed[i].Codes.Distinct())
            {
                if (!codeIndex.TryGetValue(code, out var names))
                {
                    codeIndex[code] = names = new List<int>();
                }

                names.Add(i);
            }

            foreach (var g in listed[i].Grams!)
            {
                (postings[g] ??= new List<int>()).Add(i);
            }
        }

        var queries = searches.Select(c => (c.Type, Query: Query.From(c.Query, null))).ToList();
        var times = new double[BlockingNames.Length];
        var stamp = new int[listed.Count];
        var counts = new int[listed.Count];
        var touched = new List<int>();
        var round = 0;
        double sink = 0;

        void Score(int i, Query query)
        {
            var l = listed[i];
            sink += score(l.Type == EntryType.Individual ? query.Plain : query.Entity, l);
        }

        for (var m = 0; m < BlockingNames.Length; m++)
        {
            var stopwatch = Stopwatch.StartNew();
            foreach (var (type, query) in queries)
            {
                switch (m)
                {
                    case 0:
                    case 1:
                        for (var i = 0; i < listed.Count; i++)
                        {
                            if (listed[i].Type == type && (m == 0 || (query.Initials & listed[i].Initials) != 0))
                            {
                                Score(i, query);
                            }
                        }

                        break;
                    case 2:
                        round++;
                        foreach (var code in query.Plain.Codes.Concat(query.Entity.Codes).Distinct())
                        {
                            if (!codeIndex.TryGetValue(code, out var names))
                            {
                                continue;
                            }

                            foreach (var i in names)
                            {
                                if (stamp[i] != round && listed[i].Type == type)
                                {
                                    stamp[i] = round;
                                    var q = listed[i].Type == EntryType.Individual ? query.Plain : query.Entity;
                                    if (SharesCode(q.Codes, listed[i].Codes))
                                    {
                                        Score(i, query);
                                    }
                                }
                            }
                        }

                        break;
                    default:
                        var k = m == 3 ? 2 : 3;
                        var grams = GramIds(type == EntryType.Individual ? query.Plain.Tokens : query.Entity.Tokens, gramIds, add: false);
                        touched.Clear();
                        foreach (var g in grams)
                        {
                            foreach (var i in postings[g] ?? (IEnumerable<int>)Array.Empty<int>())
                            {
                                if (counts[i]++ == 0)
                                {
                                    touched.Add(i);
                                }
                            }
                        }

                        foreach (var i in touched)
                        {
                            if (counts[i] >= k && listed[i].Type == type)
                            {
                                Score(i, query);
                            }

                            counts[i] = 0;
                        }

                        break;
                }
            }

            stopwatch.Stop();
            times[m] = stopwatch.Elapsed.TotalMilliseconds / Math.Max(1, queries.Count);
        }

        Console.WriteLine($"Pipeline: blocking timed ({sink:0})");
        return times;
    }

    private static void Flags(SanctionsList list, List<TestCase> positives, IReadOnlyList<string> twoPart, IReadOnlyList<string> threePart, IReadOnlyList<string> nearMisses, int seed, StringBuilder r)
    {
        const string country = "United States";
        var screener = new SanctionsScreener(list, new ScreeningOptions { MinScore = 85, MaxResults = 1000 });
        var thresholds = new[] { 85.0, 90.0 };

        var individuals = list.Entries.Where(e => e.Type == EntryType.Individual).ToList();
        var entities = list.Entries.Where(e => e.Type != EntryType.Individual).ToList();
        r.AppendLine();
        r.AppendLine("## Conflict flags");
        r.AppendLine();
        r.AppendLine($"Flags can only help when the listed entry has the information. On this list, {Percent(individuals.Count(e => e.DatesOfBirth.Count > 0), individuals.Count)} of {individuals.Count:N0} individuals have a date of birth and {Percent(individuals.Count(e => e.Countries.Any()), individuals.Count)} have a country in an address; {Percent(entities.Count(e => e.Countries.Any()), entities.Count)} of {entities.Count:N0} other entries have a country.");
        r.AppendLine();
        r.AppendLine($"Each ordinary and near-miss name was given a year of birth drawn uniformly from 1940 to 2006 and the country \"{country}\", with the default tolerance of one year. An alert is *cleared by* a flag when every match at or above the threshold carries it, so a reviewer can close the alert from the flags alone (after checking them).");
        r.AppendLine();
        r.AppendLine("| Names | Threshold | Alerts | Cleared by date of birth | Cleared by country | Cleared by either | Not cleared |");
        r.AppendLine("|---|---:|---:|---:|---:|---:|---:|");

        var random = new Random(seed + 2000);
        var groups = new (string Label, IReadOnlyList<string> Names)[]
        {
            ("Two-part ordinary names", twoPart), ("Three-part ordinary names", threePart), ("Near-miss names", nearMisses),
        };
        foreach (var (label, names) in groups)
        {
            var requests = names.Select(n => (Name: n, Year: random.Next(1940, 2007))).ToList();
            var results = requests.AsParallel().AsOrdered()
                .Select(x => screener.Screen(new ScreeningRequest(x.Name) { Type = EntryType.Individual, YearOfBirth = x.Year, Country = country }))
                .ToList();
            foreach (var t in thresholds)
            {
                var alerts = results.Select(ms => ms.Where(m => m.Score >= t).ToList()).Where(ms => ms.Count > 0).ToList();
                var dob = alerts.Count(ms => ms.All(m => m.DateOfBirthConflict));
                var place = alerts.Count(ms => ms.All(m => m.CountryConflict));
                var either = alerts.Count(ms => ms.All(m => m.DateOfBirthConflict || m.CountryConflict));
                r.AppendLine($"| {label} ({names.Count:N0}) | {t:0} | {alerts.Count:N0} ({Percent(alerts.Count, names.Count)}) | {Percent(dob, alerts.Count)} | {Percent(place, alerts.Count)} | {Percent(either, alerts.Count)} | {Percent(alerts.Count - either, alerts.Count)} |");
            }
        }

        // Check: the listed person, searched with their own listed year of birth and country, must never be flagged.
        var own = positives
            .Select(c => (Case: c, Entry: list.Entries.First(e => e.Id == c.EntryId)))
            .Where(x => x.Entry.Type == EntryType.Individual && x.Entry.DatesOfBirth.Count > 0 && x.Entry.Countries.Any())
            .ToList();
        var checkedMatches = own.AsParallel()
            .Select(x => screener.Screen(new ScreeningRequest(x.Case.Query) { Type = EntryType.Individual, YearOfBirth = x.Entry.DatesOfBirth[0].Year, Country = x.Entry.Countries.First() })
                .FirstOrDefault(m => m.Entry.Id == x.Entry.Id))
            .Where(m => m is not null)
            .ToList();
        var wrong = checkedMatches.Count(m => m!.DateOfBirthConflict || m.CountryConflict);
        r.AppendLine();
        r.AppendLine($"Check: {own.Count:N0} positive individual cases whose entry has both a date of birth and a country were searched with that year of birth and country. {checkedMatches.Count:N0} were found at 85 or above, and {wrong:N0} of those carried a flag (a flag on the listed person's own details would be an error).");
        Console.WriteLine("Pipeline: flags measured");
    }

    private static void Explanations(SanctionsList list, List<TestCase> positives, List<string> ordinary, StringBuilder r)
    {
        var screener = new SanctionsScreener(list, new ScreeningOptions { MinScore = 80, MaxResults = 1000 });
        var matches = positives.AsParallel().SelectMany(c => screener.Screen(new ScreeningRequest(c.Query) { Type = c.Type }))
            .Concat(ordinary.AsParallel().SelectMany(n => screener.Screen(new ScreeningRequest(n) { Type = EntryType.Individual })))
            .ToList();
        var failures = matches.Select(m => (Match: m, Recomputed: Recompute(m))).Where(x => Math.Abs(x.Recomputed - x.Match.Score) > 0.1 + 1e-9).ToList();
        var fromListed = matches.Count(m => m.NameScore.ScoredFromListedName);

        r.AppendLine();
        r.AppendLine("## Explanations");
        r.AppendLine();
        r.AppendLine($"Every match at 80 or above, on every search in the set ({matches.Count:N0} matches, {fromListed:N0} of them scored from the listed name), was re-scored only from what `match.Explain()` prints: the listed name, each searched part with its partner, the similarity to three decimals and whether the parts sound alike, the whole-name score, and whether it counts and whether the listed name's side was used. The whole-name score is taken as printed; everything else is recomputed with the formulas in [how scoring works](how-scoring-works.md).");
        r.AppendLine();
        r.AppendLine($"**Reproduced within 0.1 points: {Percent(matches.Count - failures.Count, matches.Count)}** ({matches.Count - failures.Count:N0} of {matches.Count:N0}).");
        foreach (var (m, recomputed) in failures.Take(5))
        {
            r.AppendLine($"- Not reproduced: \"{string.Join(" ", m.NameScore.Parts.Select(p => p.QueryPart))}\" against \"{m.MatchedName}\": reported {m.Score:0.0}, recomputed {recomputed:0.0}.");
        }

        Console.WriteLine("Pipeline: explanations checked");
    }

    // Re-scores a match from the numbers its explanation prints.
    private static double Recompute(ScreeningMatch match)
    {
        var individual = match.Entry.Type == EntryType.Individual;
        var listedTokens = NameNormalizer.Tokenize(individual ? NameNormalizer.ToNaturalOrder(match.MatchedName) : match.MatchedName, removeLegalSuffixes: !individual);
        var listedTotal = listedTokens.Sum(t => t.Length);
        var score = match.NameScore;
        double weighted = 0, total = 0, covered = 0, listedWeighted = 0, unpaired = 0;
        foreach (var part in score.Parts)
        {
            var similarity = Math.Round(part.Similarity, 3, MidpointRounding.AwayFromZero);
            var pair = part.ListedPart is null ? 0 : part.SoundsAlike ? Math.Max(similarity, (similarity + 1) / 2) : similarity;
            weighted += pair * part.QueryPart.Length;
            total += part.QueryPart.Length;
            if (part.ListedPart is null)
            {
                unpaired += part.QueryPart.Length;
            }
            else
            {
                covered += part.ListedPart.Length;
                listedWeighted += pair * part.ListedPart.Length;
            }
        }

        var parts = (weighted / total) * (0.95 + (0.05 * covered / listedTotal));
        if (score.ScoredFromListedName)
        {
            parts = (listedWeighted / listedTotal) * (1 - (0.1 * unpaired / total));
        }

        var partsScore = Math.Round(parts * 100, 1, MidpointRounding.AwayFromZero);
        return score.FullNameCounts ? Math.Max(score.FullNameScore, partsScore) : partsScore;
    }

    // Distinct trigram ids of the name parts, each padded with '#'. Unknown trigrams are skipped unless added.
    private static int[] GramIds(IReadOnlyList<string> tokens, Dictionary<string, int> ids, bool add)
    {
        var result = new HashSet<int>();
        foreach (var token in tokens)
        {
            var padded = "#" + token + "#";
            for (var i = 0; i + 3 <= padded.Length; i++)
            {
                var g = padded.Substring(i, 3);
                if (ids.TryGetValue(g, out var id))
                {
                    result.Add(id);
                }
                else if (add)
                {
                    id = ids.Count;
                    ids[g] = id;
                    result.Add(id);
                }
            }
        }

        var array = result.ToArray();
        Array.Sort(array);
        return array;
    }

    private static int SharedCount(int[] a, int[] b)
    {
        int i = 0, j = 0, shared = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j])
            {
                shared++;
                i++;
                j++;
            }
            else if (a[i] < b[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return shared;
    }

    private static bool SharesCode(string[] a, string[] b)
    {
        foreach (var x in a)
        {
            foreach (var y in b)
            {
                if (x == y)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
