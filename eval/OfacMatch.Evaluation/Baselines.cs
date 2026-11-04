using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using OfacMatch.Data;
using OfacMatch.Matching;

namespace OfacMatch.Evaluation;

/// <summary>
/// Compares OfacMatch with standard name-matching baselines and with versions of itself that
/// have one component removed (ablations), on the same cases as the main evaluation.
/// Every method sees the same normalized name parts and keeps the best-scoring name per entry,
/// so only the comparison itself differs. Baselines compare against every listed name (no pre-filter).
/// </summary>
internal static class Baselines
{
    private static readonly double[] Thresholds = { 80, 85, 90, 95 };
    private static readonly double[] AlertBudgets = { 0.01, 0.05, 0.10, 0.20 };

    public static string Run(SanctionsList list, IReadOnlyList<TestCase> cases, IReadOnlyList<string> negatives,
        IReadOnlyList<string> threePartNames, IReadOnlyList<TestCase> hardCases, IReadOnlyList<string> nearMisses, string header)
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

        var trigrams = new TrigramModel(listed);
        foreach (var name in listed)
        {
            name.Vector = trigrams.Vector(name.Joined);
        }

        var methods = Methods();
        CheckReimplementation(listed, cases.Concat(hardCases).ToList(), methods.First(m => m.Name == DefaultName));

        var stopwatch = Stopwatch.StartNew();
        var positive = new ConcurrentBag<(TestCase Case, double[] Target, double[] Other)>();
        Parallel.ForEach(cases, c =>
        {
            var (target, other) = ScoreQuery(Query.From(c.Query, trigrams), c.Type, c.EntryId, listed, methods);
            positive.Add((c, target, other));
        });

        var negative = new ConcurrentBag<double[]>();
        Parallel.ForEach(negatives, n =>
        {
            var (_, other) = ScoreQuery(Query.From(n, trigrams), EntryType.Individual, -1, listed, methods);
            negative.Add(other);
        });
        stopwatch.Stop();
        Console.WriteLine($"Baselines: {cases.Count + negatives.Count:N0} searches x {methods.Count} methods in {stopwatch.Elapsed.TotalSeconds:0.0} s");

        var hard = hardCases.AsParallel()
            .Select(c => (Case: c, Target: ScoreQuery(Query.From(c.Query, trigrams), c.Type, c.EntryId, listed, methods).Target))
            .ToList();
        var near = nearMisses.AsParallel()
            .Select(n => ScoreQuery(Query.From(n, trigrams), EntryType.Individual, -1, listed, methods).Other)
            .ToList();
        var three = threePartNames.AsParallel()
            .Select(n => ScoreQuery(Query.From(n, trigrams), EntryType.Individual, -1, listed, methods).Other)
            .ToList();
        Console.WriteLine($"Baselines: {hard.Count:N0} harder variations, {near.Count:N0} near misses and {three.Count:N0} three-part names scored");

        var pos = positive.OrderBy(p => p.Case.EntryId).ThenBy(p => p.Case.Query, StringComparer.Ordinal).ToList();
        var neg = negative.ToList();
        var r = new StringBuilder();
        r.AppendLine("# Baselines and ablations");
        r.AppendLine();
        r.AppendLine(header);
        r.AppendLine();
        r.AppendLine("Every method below sees the same normalized name parts (accents removed, upper case, list order turned into natural order, legal forms removed for companies), screens against every primary name and alias of the searched type, and keeps the best-scoring name per entry. Only the comparison differs. Scores are on a 0-100 scale, rounded to one decimal. Baselines compare against every listed name; OfacMatch rows use its first-letter pre-filter unless stated.");
        r.AppendLine();
        r.AppendLine("Methods:");
        r.AppendLine();
        foreach (var m in methods)
        {
            r.AppendLine($"- **{m.Name}:** {m.Description}");
        }

        r.AppendLine();
        r.AppendLine("## Recall and alert rate at fixed thresholds");
        r.AppendLine();
        r.AppendLine("Recall: share of the " + pos.Count.ToString("N0", CultureInfo.InvariantCulture) + " variations whose original entry scores at or above the threshold. Alerts: share of the " + neg.Count.ToString("N0", CultureInfo.InvariantCulture) + " two-part ordinary names with any match at or above the threshold (three-part names are in the next table). Ranked first: the original entry scores strictly higher than every other entry.");
        r.AppendLine();
        r.AppendLine("| Method | " + string.Join(" | ", Thresholds.Select(t => $"Recall ≥ {t:0} | Alerts ≥ {t:0}")) + " | Ranked first |");
        r.AppendLine("|---|" + string.Concat(Thresholds.Select(_ => "---:|---:|")) + "---:|");
        for (var m = 0; m < methods.Count; m++)
        {
            r.Append($"| {methods[m].Name} |");
            foreach (var t in Thresholds)
            {
                r.Append($" {Percent(pos.Count(p => p.Target[m] >= t), pos.Count)} | {Percent(neg.Count(n => n[m] >= t), neg.Count)} |");
            }

            r.AppendLine($" {Percent(pos.Count(p => p.Target[m] > 0 && p.Target[m] > p.Other[m]), pos.Count)} |");
        }

        r.AppendLine();
        r.AppendLine("## Alert rate on three-part ordinary names");
        r.AppendLine();
        r.AppendLine($"Share of the {three.Count:N0} three-part ordinary names (a middle name, or two surnames) with any match at or above the threshold. These are where a search has more parts than many listed names.");
        r.AppendLine();
        r.AppendLine("| Method | " + string.Join(" | ", Thresholds.Select(t => $"Alerts ≥ {t:0}")) + " |");
        r.AppendLine("|---|" + string.Concat(Thresholds.Select(_ => "---:|")));
        for (var m = 0; m < methods.Count; m++)
        {
            r.AppendLine($"| {methods[m].Name} |" + string.Concat(Thresholds.Select(t => $" {Percent(three.Count(n => n[m] >= t), three.Count)} |")));
        }

        // Thresholds for a fixed alert rate are set on all ordinary names, two-part and three-part.
        var ordinary = neg.Concat(three).ToList();
        r.AppendLine();
        r.AppendLine("## Recall at a fixed alert rate");
        r.AppendLine();
        r.AppendLine($"Each method's scores mean different things, so the same threshold is not a fair comparison. Here each method gets the lowest threshold (in steps of 0.1) at which no more than the stated share of all {ordinary.Count:N0} ordinary names (two-part and three-part) alerts, and the table reports the recall it then reaches, with that threshold in brackets. Ordinary names spelled exactly like a listed name alert under every method, so no method can go below that floor.");
        r.AppendLine();
        r.AppendLine("| Method | " + string.Join(" | ", AlertBudgets.Select(a => $"Alerts ≤ {a * 100:0}%")) + " |");
        r.AppendLine("|---|" + string.Concat(AlertBudgets.Select(_ => "---:|")));
        var budgetThresholds = new double[methods.Count, AlertBudgets.Length];
        for (var m = 0; m < methods.Count; m++)
        {
            var negScores = ordinary.Select(n => n[m]).OrderBy(s => s).ToArray();
            r.Append($"| {methods[m].Name} |");
            for (var a = 0; a < AlertBudgets.Length; a++)
            {
                var t = ThresholdFor(negScores, AlertBudgets[a]);
                budgetThresholds[m, a] = t;
                r.Append(double.IsNaN(t)
                    ? " – |"
                    : $" {Percent(pos.Count(p => p.Target[m] >= t), pos.Count)} ({t.ToString("0.0", CultureInfo.InvariantCulture)}) |");
            }

            r.AppendLine();
        }

        var tenPercent = Array.IndexOf(AlertBudgets, 0.10);
        r.AppendLine();
        r.AppendLine("## Recall by variation at alerts ≤ 10%");
        r.AppendLine();
        r.AppendLine("| Variation | Cases | " + string.Join(" | ", methods.Select((m, i) => $"M{i + 1}")) + " |");
        r.AppendLine("|---|---:|" + string.Concat(methods.Select(_ => "---:|")));
        foreach (var group in pos.GroupBy(p => p.Case.Variation).OrderBy(g => g.Key))
        {
            r.Append($"| {Variations.Describe(group.Key)} | {group.Count()} |");
            for (var m = 0; m < methods.Count; m++)
            {
                var t = budgetThresholds[m, tenPercent];
                r.Append(double.IsNaN(t) ? " – |" : $" {Percent(group.Count(p => p.Target[m] >= t), group.Count())} |");
            }

            r.AppendLine();
        }

        r.AppendLine();
        r.AppendLine("Columns: " + string.Join("; ", methods.Select((m, i) => $"M{i + 1} = {m.Name}")) + ".");

        var hardGroups = hard.GroupBy(h => h.Case.Variation).OrderBy(g => g.Key).ToList();
        r.AppendLine();
        r.AppendLine("## Harder variations and near-miss names");
        r.AppendLine();
        r.AppendLine($"Harder variations ({hard.Count:N0} cases) and near-miss names ({near.Count:N0} ordinary names that share a given name with a listed individual, such as \"AYMAN SMITH\") are generated separately, so the cases above are unchanged. Each method uses the threshold set above on all ordinary names, so these columns show how the methods behave on cases they were not tuned for. Nickname cases are drawn from the whole list.");
        r.AppendLine();
        r.AppendLine("Variations: " + string.Join("; ", hardGroups.Select((g, i) => $"H{i + 1} = {Variations.Describe(g.Key)} ({g.Count()})")) + ".");
        foreach (var budget in new[] { 0.05, 0.10 })
        {
            var b = Array.IndexOf(AlertBudgets, budget);
            r.AppendLine();
            r.AppendLine($"### At the threshold where ordinary names alert ≤ {budget * 100:0}%");
            r.AppendLine();
            r.AppendLine("| Method | Threshold | " + string.Join(" | ", hardGroups.Select((_, i) => $"H{i + 1}")) + " | All harder | Near-miss alerts |");
            r.AppendLine("|---|---:|" + string.Concat(hardGroups.Select(_ => "---:|")) + "---:|---:|");
            for (var m = 0; m < methods.Count; m++)
            {
                var t = budgetThresholds[m, b];
                if (double.IsNaN(t))
                {
                    r.AppendLine($"| {methods[m].Name} | – |" + string.Concat(hardGroups.Select(_ => " – |")) + " – | – |");
                    continue;
                }

                r.AppendLine($"| {methods[m].Name} | {t.ToString("0.0", CultureInfo.InvariantCulture)} |"
                    + string.Concat(hardGroups.Select(g => $" {Percent(g.Count(h => h.Target[m] >= t), g.Count())} |"))
                    + $" {Percent(hard.Count(h => h.Target[m] >= t), hard.Count)} | {Percent(near.Count(n => n[m] >= t), near.Count)} |");
            }
        }
        r.AppendLine();
        r.AppendLine($"Runtime: {cases.Count + negatives.Count:N0} searches, each scored by all {methods.Count} methods against every listed name of the searched type, took {stopwatch.Elapsed.TotalSeconds:0.0} s on {Environment.ProcessorCount} threads. This harness is written for comparison, not speed; see the main evaluation for OfacMatch's own throughput.");
        return r.ToString();
    }

    private const string DefaultName = "OfacMatch (default)";

    // Follows the library's default, so the copy is checked against what ships.
    private static readonly double ExtraPenalty = new ScoringOptions().ExtraPartPenalty;

    private static List<Method> Methods()
    {
        var all = new PartsOptions();
        return new List<Method>
        {
            new("Exact match", "100 if the normalized names are identical, otherwise 0.", (q, l) => q.Joined == l.Joined ? 100 : 0),
            new("Levenshtein", "1 minus edit distance divided by the longer length, whole name.", (q, l) => Levenshtein(q.Joined, l.Joined)),
            new("Jaro-Winkler", "Jaro-Winkler similarity of the whole names (OFAC's Technique 1 alone).", (q, l) => JaroWinkler.Similarity(q.Joined, l.Joined)),
            new("Token sort ratio", "Indel similarity after sorting the name parts alphabetically, as in RapidFuzz's token_sort_ratio.", (q, l) => Indel(q.Sorted, l.Sorted)),
            new("Token set ratio", "RapidFuzz's token_set_ratio: 100 when one name's parts are a subset of the other's, otherwise the best Indel similarity of the shared and differing parts.", TokenSet),
            new("Soundex parts", "Share of searched parts that pair one-to-one with a listed part of the same Soundex code.", SoundexParts),
            new("Monge-Elkan (Jaro-Winkler)", "Mean, over searched parts, of the best Jaro-Winkler similarity with any listed part.", MongeElkan),
            new("Trigram TF-IDF cosine", "Cosine similarity of character-trigram TF-IDF vectors, with IDF from all listed names.", (q, l) => Cosine(q.Vector!.Value, l.Vector!.Value)),
            new("OfacMatch, OFAC FAQ style", "Higher of whole-name and name-parts score, without the guard, with the pre-filter.", Parts(all with { Guard = false }), Prefilter: true),
            new(DefaultName, "Name parts with Soundex, length weights, coverage, extra searched parts allowed, whole-name score behind the guard, and the first-letter pre-filter.", Parts(all), Prefilter: true),
            new("– without extra-part rule (0.1.2)", "Default, but extra searched parts count as misses, as in version 0.1.2.", Parts(all with { ExtraParts = false }), Prefilter: true),
            new("– without pre-filter", "Default, but every listed name is compared.", Parts(all)),
            new("– without Soundex", "Default, but parts that sound alike get no boost.", Parts(all with { Soundex = false }), Prefilter: true),
            new("– equal part weights", "Default, but every searched part counts the same regardless of length.", Parts(all with { LengthWeights = false }), Prefilter: true),
            new("– without coverage", "Default, but no adjustment for how much of the listed name was covered.", Parts(all with { Coverage = false }), Prefilter: true),
            new("– name parts only", "Default, but the whole-name score is never used.", Parts(all with { FullName = false }), Prefilter: true),
        };
    }

    private static (double[] Target, double[] Other) ScoreQuery(Query query, EntryType type, int targetId, List<NameData> listed, List<Method> methods)
    {
        var target = new double[methods.Count];
        var other = new double[methods.Count];
        foreach (var l in listed)
        {
            if (l.Type != type)
            {
                continue;
            }

            var q = l.Type == EntryType.Individual ? query.Plain : query.Entity;
            var passes = (query.Initials & l.Initials) != 0;
            var best = l.EntryId == targetId ? target : other;
            for (var m = 0; m < methods.Count; m++)
            {
                if (methods[m].Prefilter && !passes)
                {
                    continue;
                }

                var s = Round(methods[m].Score(q, l));
                if (s > best[m])
                {
                    best[m] = s;
                }
            }
        }

        return (target, other);
    }

    // The OfacMatch rows use a copy of the library's scorer with switches, so that components can
    // be turned off. Before running, the copy is checked against the library on real pairs.
    private static void CheckReimplementation(List<NameData> listed, IReadOnlyList<TestCase> cases, Method copy)
    {
        var scorer = new NameScorer();
        var random = new Random(7);
        var checkedPairs = 0;
        // Every 16th case, against random listed names and against all names of its own entry,
        // which is where the closer and less common scoring paths (such as extra parts) show up.
        foreach (var c in cases.Where((_, index) => index % 16 == 0))
        {
            var query = Query.From(c.Query, null);
            var own = listed.Where(l => l.EntryId == c.EntryId).ToList();
            for (var i = 0; i < 400 + own.Count; i++)
            {
                var l = i < own.Count ? own[i] : listed[random.Next(listed.Count)];
                var q = l.Type == EntryType.Individual ? query.Plain : query.Entity;
                if (q.Tokens.Length == 0)
                {
                    continue;
                }

                var expected = scorer.Score(q.Tokens, l.Tokens).Score;
                var actual = Round(copy.Score(q, l));
                if (Math.Abs(expected - actual) > 1e-9)
                {
                    throw new InvalidOperationException($"Scorer copy differs from the library: \"{q.Joined}\" vs \"{l.Joined}\": {actual} vs {expected}.");
                }

                checkedPairs++;
            }
        }

        Console.WriteLine($"Scorer copy matches the library on {checkedPairs:N0} pairs.");
    }

    private static Func<NameData, NameData, double> Parts(PartsOptions o) => (q, l) =>
    {
        if (q.Tokens.Length == 0 || l.Tokens.Length == 0)
        {
            return 0;
        }

        var pairs = new List<(int Q, int L, double Score)>(q.Tokens.Length * l.Tokens.Length);
        for (var i = 0; i < q.Tokens.Length; i++)
        {
            for (var k = 0; k < l.Tokens.Length; k++)
            {
                var sim = JaroWinkler.Similarity(q.Tokens[i], l.Tokens[k]);
                var alike = o.Soundex && q.Tokens[i].Length > 1 && l.Tokens[k].Length > 1 && q.Codes[i] == l.Codes[k];
                pairs.Add((i, k, alike ? Math.Max(sim, (sim + 1) / 2) : sim));
            }
        }

        // Same order as the library: a stable sort by score, highest first.
        var ordered = pairs.ToArray();
        Array.Sort(ordered, (x, y) => y.Score.CompareTo(x.Score));
        var assigned = new double?[q.Tokens.Length];
        var used = new bool[l.Tokens.Length];
        double listedWeighted = 0;
        foreach (var p in ordered)
        {
            if (assigned[p.Q] is null && !used[p.L])
            {
                assigned[p.Q] = p.Score;
                used[p.L] = true;
                listedWeighted += p.Score * l.Tokens[p.L].Length;
            }
        }

        double weighted = 0, total = 0, minPart = double.MaxValue;
        for (var i = 0; i < q.Tokens.Length; i++)
        {
            var s = assigned[i] ?? 0;
            double w = o.LengthWeights ? q.Tokens[i].Length : 1;
            weighted += s * w;
            total += w;
            minPart = Math.Min(minPart, s);
        }

        var composite = weighted / total;
        double covered = 0;
        for (var k = 0; k < l.Tokens.Length; k++)
        {
            if (used[k])
            {
                covered += l.Tokens[k].Length;
            }
        }

        var coverage = covered / l.TotalLength;
        var weight = o.Coverage ? 0.05 : 0;
        var partsUnit = composite * ((1 - weight) + weight * coverage);
        if (o.ExtraParts && l.Tokens.Length >= 2 && q.Tokens.Length > l.Tokens.Length)
        {
            double unpaired = 0, queryLength = 0;
            for (var i = 0; i < q.Tokens.Length; i++)
            {
                queryLength += q.Tokens[i].Length;
                if (assigned[i] is null)
                {
                    unpaired += q.Tokens[i].Length;
                }
            }

            // The library divides by the total part weight, which is the searched length when
            // parts are weighted by length; keep that so the ablation changes one thing only.
            var fromListed = (listedWeighted / l.TotalLength) * (1 - (ExtraPenalty * unpaired / (o.LengthWeights ? queryLength : total)));
            partsUnit = Math.Max(partsUnit, fromListed);
        }

        var parts = Round(partsUnit);
        if (!o.FullName)
        {
            return parts / 100;
        }

        var full = Round(JaroWinkler.Similarity(q.Joined, l.Joined));
        var fullCounts = !o.Guard || minPart >= 0.7;
        return (fullCounts ? Math.Max(full, parts) : parts) / 100;
    };

    private static double Levenshtein(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1;
        }

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(prev[j] + 1, cur[j - 1] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return 1 - (double)prev[b.Length] / Math.Max(a.Length, b.Length);
    }

    // Normalized Indel similarity (insertions and deletions only), as RapidFuzz's ratio.
    private static double Indel(string a, string b)
    {
        if (a.Length + b.Length == 0)
        {
            return 1;
        }

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                cur[j] = a[i - 1] == b[j - 1] ? prev[j - 1] + 1 : Math.Max(prev[j], cur[j - 1]);
            }

            (prev, cur) = (cur, prev);
        }

        return 2.0 * prev[b.Length] / (a.Length + b.Length);
    }

    private static double TokenSet(NameData q, NameData l)
    {
        var a = q.Tokens.ToHashSet(StringComparer.Ordinal);
        var b = l.Tokens.ToHashSet(StringComparer.Ordinal);
        var sect = a.Intersect(b).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var diffAb = a.Except(b).OrderBy(t => t, StringComparer.Ordinal).ToList();
        var diffBa = b.Except(a).OrderBy(t => t, StringComparer.Ordinal).ToList();
        if (sect.Count > 0 && (diffAb.Count == 0 || diffBa.Count == 0))
        {
            return 1;
        }

        var s = string.Join(" ", sect);
        var ab = string.Join(" ", diffAb);
        var ba = string.Join(" ", diffBa);
        var result = Indel(ab, ba);
        if (sect.Count > 0)
        {
            var sab = s + " " + ab;
            var sba = s + " " + ba;
            result = Math.Max(result, Math.Max(Indel(s, sab), Math.Max(Indel(s, sba), Indel(sab, sba))));
        }

        return result;
    }

    private static double SoundexParts(NameData q, NameData l)
    {
        if (q.Tokens.Length == 0)
        {
            return 0;
        }

        var used = new bool[l.Codes.Length];
        var matched = 0;
        foreach (var code in q.Codes)
        {
            for (var k = 0; k < l.Codes.Length; k++)
            {
                if (!used[k] && l.Codes[k] == code)
                {
                    used[k] = true;
                    matched++;
                    break;
                }
            }
        }

        return (double)matched / q.Codes.Length;
    }

    private static double MongeElkan(NameData q, NameData l)
    {
        if (q.Tokens.Length == 0 || l.Tokens.Length == 0)
        {
            return 0;
        }

        double sum = 0;
        foreach (var a in q.Tokens)
        {
            double best = 0;
            foreach (var b in l.Tokens)
            {
                best = Math.Max(best, JaroWinkler.Similarity(a, b));
            }

            sum += best;
        }

        return sum / q.Tokens.Length;
    }

    private static double Cosine((int[] Ids, double[] Weights) a, (int[] Ids, double[] Weights) b)
    {
        double dot = 0;
        int i = 0, j = 0;
        while (i < a.Ids.Length && j < b.Ids.Length)
        {
            if (a.Ids[i] == b.Ids[j])
            {
                dot += a.Weights[i++] * b.Weights[j++];
            }
            else if (a.Ids[i] < b.Ids[j])
            {
                i++;
            }
            else
            {
                j++;
            }
        }

        return Math.Min(1, dot);
    }

    internal static double ThresholdFor(double[] sortedNegatives, double budget)
    {
        for (var step = 1; step <= 1000; step++)
        {
            var t = step / 10.0;
            var alerts = sortedNegatives.Length - LowerBound(sortedNegatives, t - 1e-9);
            if (alerts <= budget * sortedNegatives.Length)
            {
                return t;
            }
        }

        return double.NaN;
    }

    private static int LowerBound(double[] sorted, double value)
    {
        int lo = 0, hi = sorted.Length;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (sorted[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static void Add(List<NameData> listed, SanctionsEntry entry, string raw)
    {
        var natural = entry.Type == EntryType.Individual ? NameNormalizer.ToNaturalOrder(raw) : raw;
        var tokens = NameNormalizer.Tokenize(natural, removeLegalSuffixes: entry.Type != EntryType.Individual);
        if (tokens.Count > 0)
        {
            listed.Add(new NameData(tokens) { EntryId = entry.Id, Type = entry.Type });
        }
    }

    private static double Round(double unit) => Math.Round(unit * 100, 1, MidpointRounding.AwayFromZero);

    private static string Percent(int part, int whole) =>
        whole == 0 ? "–" : (100.0 * part / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";

    private sealed record Method(string Name, string Description, Func<NameData, NameData, double> Score, bool Prefilter = false);

    private sealed record PartsOptions
    {
        public bool Soundex { get; init; } = true;

        public bool LengthWeights { get; init; } = true;

        public bool Coverage { get; init; } = true;

        public bool FullName { get; init; } = true;

        public bool Guard { get; init; } = true;

        public bool ExtraParts { get; init; } = true;
    }

    private sealed class NameData
    {
        public NameData(IReadOnlyList<string> tokens)
        {
            Tokens = tokens.ToArray();
            Codes = Tokens.Select(Soundex.Encode).ToArray();
            Joined = string.Join(" ", Tokens);
            Sorted = string.Join(" ", Tokens.OrderBy(t => t, StringComparer.Ordinal));
            TotalLength = Tokens.Sum(t => t.Length);
            Initials = InitialsOf(Tokens);
        }

        public string[] Tokens { get; }

        public string[] Codes { get; }

        public string Joined { get; }

        public string Sorted { get; }

        public int TotalLength { get; }

        public long Initials { get; }

        public int EntryId { get; init; }

        public EntryType Type { get; init; }

        public (int[] Ids, double[] Weights)? Vector { get; set; }
    }

    private sealed class Query
    {
        public required NameData Plain { get; init; }

        public required NameData Entity { get; init; }

        public long Initials => Plain.Initials | Entity.Initials;

        public static Query From(string name, TrigramModel? model)
        {
            var natural = NameNormalizer.ToNaturalOrder(name);
            var plain = new NameData(NameNormalizer.Tokenize(natural));
            var entity = new NameData(NameNormalizer.Tokenize(natural, removeLegalSuffixes: true));
            if (model is not null)
            {
                plain.Vector = model.Vector(plain.Joined);
                entity.Vector = model.Vector(entity.Joined);
            }

            return new Query { Plain = plain, Entity = entity };
        }
    }

    private static long InitialsOf(IReadOnlyList<string> tokens)
    {
        long mask = 0;
        foreach (var token in tokens)
        {
            var ch = token[0];
            var bit = ch >= 'A' && ch <= 'Z' ? ch - 'A' : ch >= '0' && ch <= '9' ? 26 + (ch - '0') : 36;
            mask |= 1L << bit;
        }

        return mask;
    }

    private sealed class TrigramModel
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        private readonly List<int> _df = new();
        private readonly int _documents;

        public TrigramModel(IReadOnlyList<NameData> names)
        {
            _documents = names.Count;
            foreach (var name in names)
            {
                foreach (var g in Grams(name.Joined).Distinct())
                {
                    if (!_ids.TryGetValue(g, out var id))
                    {
                        id = _ids.Count;
                        _ids[g] = id;
                        _df.Add(0);
                    }

                    _df[id]++;
                }
            }
        }

        // Smoothed IDF as in scikit-learn: ln((1 + N) / (1 + df)) + 1. Trigrams that never occur
        // on the list count toward the searched name's length but cannot match.
        public (int[] Ids, double[] Weights) Vector(string text)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var g in Grams(text))
            {
                counts[g] = counts.TryGetValue(g, out var c) ? c + 1 : 1;
            }

            var known = new List<(int Id, double Weight)>();
            double norm = 0;
            foreach (var (g, c) in counts)
            {
                var df = _ids.TryGetValue(g, out var id) ? _df[id] : 0;
                var w = c * (Math.Log((1.0 + _documents) / (1.0 + df)) + 1);
                norm += w * w;
                if (df > 0)
                {
                    known.Add((id, w));
                }
            }

            norm = Math.Sqrt(norm);
            known.Sort((x, y) => x.Id.CompareTo(y.Id));
            return (known.Select(k => k.Id).ToArray(), known.Select(k => norm == 0 ? 0 : k.Weight / norm).ToArray());
        }

        private static IEnumerable<string> Grams(string text)
        {
            var padded = " " + text + " ";
            for (var i = 0; i + 3 <= padded.Length; i++)
            {
                yield return padded.Substring(i, 3);
            }
        }
    }
}
