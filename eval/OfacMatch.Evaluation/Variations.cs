using OfacMatch.Data;
using OfacMatch.Matching;

namespace OfacMatch.Evaluation;

internal enum Variation
{
    Exact,
    ListOrder,
    LowercaseNoPunctuation,
    Typo,
    SwappedLetters,
    MissingLetter,
    Transliteration,
    JoinedParticle,
    DroppedMiddleName,
    CompanySuffixChanged,

    // Harder variations, generated separately so the cases above stay exactly as published.
    FirstLetterError,
    TwoTypos,
    Nickname,
    FirstNameInitial,
    ExtraMiddleName,
}

internal sealed class TestCase
{
    public TestCase(int entryId, EntryType type, string original, string query, Variation variation)
    {
        EntryId = entryId;
        Type = type;
        Original = original;
        Query = query;
        Variation = variation;
    }

    public int EntryId { get; }

    public EntryType Type { get; }

    public string Original { get; }

    public string Query { get; }

    public Variation Variation { get; }

    public bool IsHarder => Variation >= Variation.FirstLetterError;
}

/// <summary>First names and surnames used to build ordinary names that are not on the list.</summary>
internal sealed class NameVocabulary
{
    public NameVocabulary(string label, string[] firstNames, string[] surnames)
    {
        Label = label;
        FirstNames = firstNames;
        Surnames = surnames;
    }

    public string Label { get; }

    public string[] FirstNames { get; }

    public string[] Surnames { get; }
}

/// <summary>Generates realistic name variations, the way names drift between documents, systems and alphabets.</summary>
internal static class Variations
{
    private static readonly string[] Particles = { "AL", "EL", "BIN", "BEN", "ABU", "ABD", "ABDUL", "VAN", "DE", "DA", "DOS", "DEL" };

    // Common transliteration differences: Mohammed/Muhammad, Yusuf/Yousef, Khalid/Halid, Qasim/Kasim.
    private static readonly (string From, string To)[] Transliterations =
    {
        ("OU", "U"), ("U", "OU"), ("U", "O"), ("O", "U"), ("EE", "I"), ("I", "EE"), ("A", "E"), ("E", "A"),
        ("KH", "H"), ("Q", "K"), ("Y", "I"), ("I", "Y"), ("MM", "M"), ("M", "MM"), ("DH", "D"), ("TH", "T"),
    };

    private static readonly HashSet<string> Suffixes = new(StringComparer.Ordinal)
    {
        "LTD", "LIMITED", "LLC", "INC", "CO", "COMPANY", "CORP", "CORPORATION", "SA", "PLC", "GMBH", "JSC",
    };

    private static readonly string[] FirstNames =
    {
        "James", "Mary", "Robert", "Patricia", "Michael", "Jennifer", "David", "Linda", "William", "Elizabeth",
        "Jose", "Maria", "Luis", "Carmen", "Juan", "Ana", "Carlos", "Sofia", "Wei", "Li", "Hiroshi", "Yuki",
        "Minjun", "Jiwoo", "Raj", "Priya", "Arjun", "Ananya", "Olga", "Ivan", "Dmitri", "Fatima", "Omar", "Aisha",
        "Hassan", "Layla", "Kwame", "Amara", "Chinedu", "Ngozi", "Pierre", "Camille", "Hans", "Greta", "Giulia",
        "Marco", "Lars", "Ingrid", "Thanh", "Mai",
    };

    private static readonly string[] Surnames =
    {
        "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Martinez",
        "Hernandez", "Lopez", "Gonzalez", "Wilson", "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin",
        "Lee", "Kim", "Park", "Chen", "Wang", "Zhang", "Liu", "Tanaka", "Sato", "Patel", "Shah", "Singh", "Kumar",
        "Gupta", "Ivanova", "Petrov", "Kowalski", "Nowak", "Mueller", "Schmidt", "Rossi", "Bianchi", "Dubois",
        "Moreau", "Okafor", "Mensah", "Haddad", "Nasser", "Khan", "Ahmed", "Rahman", "Nguyen", "Tran", "Silva",
        "Santos", "O'Brien", "Murphy",
    };

    /// <summary>The names used during development (and in the published evaluation).</summary>
    public static readonly NameVocabulary Development = new("development", FirstNames, Surnames);

    /// <summary>
    /// Names never used during development, from the same mix of cultures, so the held-out test
    /// measures ordinary names the method was not adjusted to.
    /// </summary>
    public static readonly NameVocabulary HeldOut = new(
        "held-out",
        new[]
        {
            "Henry", "Barbara", "Charles", "Susan", "Joseph", "Jessica", "Daniel", "Sarah", "Matthew", "Karen",
            "Miguel", "Lucia", "Pedro", "Isabel", "Diego", "Valentina", "Andres", "Gabriela", "Ming", "Xiu", "Kenji",
            "Haruka", "Seojun", "Hana", "Vikram", "Deepa", "Rohan", "Kavya", "Natalia", "Sergei", "Anastasia", "Yusuf",
            "Leila", "Karim", "Zainab", "Kofi", "Abena", "Emeka", "Chiamaka", "Louis", "Claire", "Klaus", "Heidi",
            "Francesca", "Luca", "Erik", "Astrid", "Minh", "Linh", "Tomasz",
        },
        new[]
        {
            "Clark", "Lewis", "Walker", "Hall", "Allen", "Young", "King", "Wright", "Scott", "Green", "Perez",
            "Sanchez", "Ramirez", "Torres", "Flores", "Rivera", "Gomez", "Diaz", "Choi", "Jung", "Huang", "Zhao",
            "Wu", "Yamamoto", "Suzuki", "Nakamura", "Reddy", "Iyer", "Mehta", "Joshi", "Kuznetsov", "Popov",
            "Volkova", "Wisniewski", "Novak", "Becker", "Fischer", "Weber", "Ferrari", "Romano", "Lefebvre",
            "Laurent", "Adeyemi", "Owusu", "Boateng", "Hamdan", "Saleh", "Aziz", "Hussain", "Chowdhury", "Islam",
            "Pham", "Le", "Pereira", "Oliveira", "Kelly", "Byrne",
        });

    public static string Describe(Variation variation) => variation switch
    {
        Variation.Exact => "Exact name",
        Variation.ListOrder => "Surname first (\"DOE John\")",
        Variation.LowercaseNoPunctuation => "Lower case, no punctuation",
        Variation.Typo => "One wrong letter",
        Variation.SwappedLetters => "Two letters swapped",
        Variation.MissingLetter => "One letter missing",
        Variation.Transliteration => "Different transliteration (Mohammed/Muhammad)",
        Variation.JoinedParticle => "Particle joined (\"AL ZAWAHIRI\" → \"ALZAWAHIRI\")",
        Variation.DroppedMiddleName => "Middle name left out",
        Variation.CompanySuffixChanged => "Company suffix changed (LTD → LLC)",
        Variation.FirstLetterError => "Wrong first letter (Kassim → Qassim)",
        Variation.TwoTypos => "Two wrong letters",
        Variation.Nickname => "Nickname or equivalent given name (Aleksandr → Sasha, Yusuf → Joseph)",
        Variation.FirstNameInitial => "First name as an initial (\"A ZAWAHIRI\")",
        Variation.ExtraMiddleName => "Extra middle name not on the list",
        _ => variation.ToString(),
    };

    public static IEnumerable<TestCase> ForIndividuals(IEnumerable<SanctionsEntry> entries, Random random)
    {
        foreach (var entry in entries)
        {
            var natural = NameNormalizer.ToNaturalOrder(entry.Name);
            var tokens = NameNormalizer.Tokenize(natural).ToList();
            var make = Maker(entry, random);

            yield return make(Variation.Exact, natural);
            yield return make(Variation.ListOrder, entry.Name.Replace(",", string.Empty));
            yield return make(Variation.LowercaseNoPunctuation, string.Join(" ", tokens).ToLowerInvariant());

            foreach (var c in Spelling(tokens, random, make))
            {
                yield return c;
            }

            if (Transliterate(tokens, random) is string transliterated)
            {
                yield return make(Variation.Transliteration, transliterated);
            }

            var particle = tokens.FindIndex(t => Particles.Contains(t));
            if (particle >= 0 && particle < tokens.Count - 1)
            {
                var joined = tokens.ToList();
                joined[particle] += joined[particle + 1];
                joined.RemoveAt(particle + 1);
                yield return make(Variation.JoinedParticle, string.Join(" ", joined));
            }

            if (tokens.Count >= 3)
            {
                var dropped = tokens.ToList();
                dropped.RemoveAt(1 + random.Next(tokens.Count - 2));
                yield return make(Variation.DroppedMiddleName, string.Join(" ", dropped));
            }
        }
    }

    public static IEnumerable<TestCase> ForEntities(IEnumerable<SanctionsEntry> entries, Random random)
    {
        foreach (var entry in entries)
        {
            var tokens = NameNormalizer.Tokenize(entry.Name).ToList();
            var make = Maker(entry, random);

            yield return make(Variation.Exact, entry.Name);
            yield return make(Variation.LowercaseNoPunctuation, string.Join(" ", tokens).ToLowerInvariant());

            foreach (var c in Spelling(tokens, random, make))
            {
                yield return c;
            }

            var changed = tokens.ToList();
            if (Suffixes.Contains(changed[changed.Count - 1]))
            {
                changed[changed.Count - 1] = changed[changed.Count - 1] == "LLC" ? "LIMITED" : "LLC";
            }
            else
            {
                changed.Add("LLC");
            }

            yield return make(Variation.CompanySuffixChanged, string.Join(" ", changed));
        }
    }

    public static List<string> CommonNames(int count, Random random) => CommonNames(count, random, Development);

    public static List<string> CommonNames(int count, Random random, NameVocabulary vocabulary)
    {
        var first = vocabulary.FirstNames;
        var last = vocabulary.Surnames;
        var names = new HashSet<string>();
        var limit = Math.Min(count, first.Length * last.Length);
        while (names.Count < limit)
        {
            names.Add($"{first[random.Next(first.Length)]} {last[random.Next(last.Length)]}");
        }

        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Ordinary names with three parts: half with a middle name ("Maria Wei Lopez"), half with two
    /// surnames as is common in Spanish and Portuguese ("Maria Lopez Silva"). The extra-part rule
    /// only applies to searches with more parts than a listed name, so it needs these to be measured.
    /// </summary>
    public static List<string> ThreePartNames(int count, Random random, NameVocabulary vocabulary)
    {
        var first = vocabulary.FirstNames;
        var last = vocabulary.Surnames;
        var names = new HashSet<string>();
        while (names.Count < count)
        {
            var given = first[random.Next(first.Length)];
            var surname = last[random.Next(last.Length)];
            if (random.Next(2) == 0)
            {
                var middle = first[random.Next(first.Length)];
                if (middle != given)
                {
                    names.Add($"{given} {middle} {surname}");
                }
            }
            else
            {
                var second = last[random.Next(last.Length)];
                if (second != surname)
                {
                    names.Add($"{given} {surname} {second}");
                }
            }
        }

        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    // Letters that are commonly confused at the start of a name, across transliterations.
    private static readonly Dictionary<char, char> FirstLetterSwaps = new()
    {
        ['C'] = 'K', ['K'] = 'Q', ['Q'] = 'K', ['G'] = 'J', ['J'] = 'Y', ['Y'] = 'J', ['I'] = 'Y', ['E'] = 'A',
        ['A'] = 'E', ['O'] = 'U', ['U'] = 'O', ['S'] = 'Z', ['Z'] = 'S', ['F'] = 'V', ['V'] = 'W', ['W'] = 'V',
        ['B'] = 'P', ['P'] = 'B', ['D'] = 'T', ['T'] = 'D', ['M'] = 'N', ['N'] = 'M', ['L'] = 'R', ['R'] = 'L',
        ['H'] = 'K', ['X'] = 'K',
    };

    // Given names as listed → a nickname or the same name in another language, as a customer may give it.
    private static readonly Dictionary<string, string> Nicknames = new(StringComparer.Ordinal)
    {
        ["WILLIAM"] = "BILL", ["ROBERT"] = "BOB", ["RICHARD"] = "DICK", ["JAMES"] = "JIM", ["JOHN"] = "JACK",
        ["MICHAEL"] = "MIKE", ["ALEXANDER"] = "ALEX", ["ALEKSANDR"] = "SASHA", ["ALEXANDR"] = "SASHA",
        ["VLADIMIR"] = "VOVA", ["DMITRY"] = "DIMA", ["DMITRIY"] = "DIMA", ["DMITRII"] = "DIMA",
        ["YEVGENIY"] = "ZHENYA", ["EVGENY"] = "ZHENYA", ["EVGENIY"] = "ZHENYA", ["NIKOLAY"] = "KOLYA",
        ["NIKOLAI"] = "KOLYA", ["SERGEY"] = "SERYOZHA", ["SERGEI"] = "SERYOZHA", ["MIKHAIL"] = "MISHA",
        ["IVAN"] = "VANYA", ["YURIY"] = "YURA", ["YURI"] = "YURA", ["ALEKSEY"] = "ALYOSHA", ["ALEXEY"] = "ALYOSHA",
        ["PAVEL"] = "PASHA", ["VIKTOR"] = "VITYA", ["JOSE"] = "PEPE", ["FRANCISCO"] = "PACO",
        ["ALEJANDRO"] = "ALEX", ["ANTONIO"] = "TONY", ["MANUEL"] = "MANOLO", ["JESUS"] = "CHUY",
        ["IGNACIO"] = "NACHO", ["GUILLERMO"] = "MEMO", ["ENRIQUE"] = "KIKE", ["ROBERTO"] = "BETO",
        ["ALBERTO"] = "BETO", ["EDUARDO"] = "LALO", ["RAFAEL"] = "RAFA", ["FERNANDO"] = "NANDO",
        ["MOHAMMED"] = "MOHD", ["MOHAMMAD"] = "MOHD", ["MUHAMMAD"] = "MOHD", ["MOHAMED"] = "MOHD",
        ["MUHAMMED"] = "MOHD", ["YUSUF"] = "JOSEPH", ["YOUSEF"] = "JOSEPH", ["IBRAHIM"] = "ABRAHAM",
        ["MUSA"] = "MOSES", ["SULEIMAN"] = "SOLOMON", ["SULAYMAN"] = "SOLOMON", ["DAWOOD"] = "DAVID",
        ["DAOUD"] = "DAVID", ["YAQUB"] = "JACOB", ["YAKUB"] = "JACOB",
    };

    /// <summary>
    /// Variations that string similarity is expected to find hard. Uses its own random generator,
    /// so adding them does not change the original cases.
    /// </summary>
    public static IEnumerable<TestCase> Harder(IEnumerable<SanctionsEntry> individuals, IEnumerable<SanctionsEntry> entities, SanctionsList list, int maxNicknames, Random random, ISet<int>? exclude = null)
    {
        foreach (var entry in individuals)
        {
            var tokens = NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(entry.Name)).ToList();
            var make = Maker(entry, random);
            foreach (var c in HardSpelling(tokens, random, make))
            {
                yield return c;
            }

            if (tokens[0].Length >= 3 && !Particles.Contains(tokens[0]))
            {
                yield return make(Variation.FirstNameInitial, Replace(tokens, 0, tokens[0].Substring(0, 1)));
            }

            var extra = tokens.ToList();
            extra.Insert(1, FirstNames[random.Next(FirstNames.Length)].ToUpperInvariant());
            yield return make(Variation.ExtraMiddleName, string.Join(" ", extra));
        }

        foreach (var entry in entities)
        {
            var tokens = NameNormalizer.Tokenize(entry.Name).ToList();
            foreach (var c in HardSpelling(tokens, random, Maker(entry, random)))
            {
                yield return c;
            }
        }

        // Few sampled names have a known nickname, so these are drawn from the whole list.
        var withNickname = new List<(SanctionsEntry Entry, List<string> Tokens, int Index)>();
        foreach (var entry in list.Entries.Where(e => e.Type == EntryType.Individual && (exclude is null || !exclude.Contains(e.Id))).OrderBy(e => e.Id))
        {
            var tokens = NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(entry.Name)).ToList();
            var index = tokens.FindIndex(t => Nicknames.ContainsKey(t));
            if (tokens.Count >= 2 && index >= 0 && index < tokens.Count - 1)
            {
                withNickname.Add((entry, tokens, index));
            }
        }

        var picked = withNickname.Select(x => (Item: x, Key: random.Next())).OrderBy(x => x.Key).Take(maxNicknames).Select(x => x.Item).ToList();
        foreach (var (entry, tokens, index) in picked)
        {
            yield return Maker(entry, random)(Variation.Nickname, Replace(tokens, index, Nicknames[tokens[index]]));
        }
    }

    /// <summary>
    /// Ordinary names that share a given name with a listed individual ("AYMAN SMITH"),
    /// the near misses that make up much of a real review queue.
    /// </summary>
    public static List<string> NearMisses(IEnumerable<SanctionsEntry> individuals, int count, Random random, NameVocabulary? vocabulary = null)
    {
        var surnames = (vocabulary ?? Development).Surnames;
        var given = individuals
            .Select(e => NameNormalizer.Tokenize(NameNormalizer.ToNaturalOrder(e.Name)))
            .Where(t => t.Count >= 2 && t[0].Length >= 3 && !Particles.Contains(t[0]))
            .Select(t => t[0])
            .Distinct()
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToList();
        var names = new HashSet<string>();
        var limit = Math.Min(count, given.Count * surnames.Length);
        while (names.Count < limit)
        {
            names.Add($"{given[random.Next(given.Count)]} {surnames[random.Next(surnames.Length)].ToUpperInvariant()}");
        }

        return names.OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<TestCase> HardSpelling(List<string> tokens, Random random, Func<Variation, string, TestCase> make)
    {
        var first = Enumerable.Range(0, tokens.Count).Where(i => tokens[i].Length >= 3 && !Particles.Contains(tokens[i]) && !Suffixes.Contains(tokens[i])).ToList();
        if (first.Count > 0)
        {
            var i = first[random.Next(first.Count)];
            var word = tokens[i];
            var letter = FirstLetterSwaps.TryGetValue(word[0], out var swap) ? swap : WrongLetter(word[0], random);
            yield return make(Variation.FirstLetterError, Replace(tokens, i, letter + word.Substring(1)));
        }

        var typo = Enumerable.Range(0, tokens.Count).Where(i => tokens[i].Length >= 4 && !Suffixes.Contains(tokens[i])).ToList();
        if (typo.Count >= 2)
        {
            var a = typo[random.Next(typo.Count)];
            var b = typo.Where(i => i != a).ElementAt(random.Next(typo.Count - 1));
            var changed = tokens.ToList();
            changed[a] = Mistype(changed[a], 1 + random.Next(changed[a].Length - 1), random);
            changed[b] = Mistype(changed[b], 1 + random.Next(changed[b].Length - 1), random);
            yield return make(Variation.TwoTypos, string.Join(" ", changed));
        }
        else if (typo.Count == 1 && tokens[typo[0]].Length >= 6)
        {
            var word = tokens[typo[0]];
            var p = 1 + random.Next(word.Length / 2 - 1);
            var q = word.Length / 2 + random.Next(word.Length - word.Length / 2);
            yield return make(Variation.TwoTypos, Replace(tokens, typo[0], Mistype(Mistype(word, p, random), q, random)));
        }
    }

    private static string Mistype(string word, int position, Random random) =>
        word.Substring(0, position) + WrongLetter(word[position], random) + word.Substring(position + 1);

    private static char WrongLetter(char current, Random random)
    {
        var replacement = (char)('A' + random.Next(26));
        return replacement == current ? (replacement == 'Z' ? 'A' : (char)(replacement + 1)) : replacement;
    }

    private static Func<Variation, string, TestCase> Maker(SanctionsEntry entry, Random random) =>
        (variation, query) => new TestCase(entry.Id, entry.Type, entry.Name, query, variation);

    private static IEnumerable<TestCase> Spelling(List<string> tokens, Random random, Func<Variation, string, TestCase> make)
    {
        var candidates = Enumerable.Range(0, tokens.Count).Where(i => tokens[i].Length >= 5).ToList();
        if (candidates.Count == 0)
        {
            yield break;
        }

        var index = candidates[random.Next(candidates.Count)];
        var word = tokens[index];

        var position = 1 + random.Next(word.Length - 1);
        var replacement = (char)('A' + random.Next(26));
        if (replacement == word[position])
        {
            replacement = replacement == 'Z' ? 'A' : (char)(replacement + 1);
        }

        yield return make(Variation.Typo, Replace(tokens, index, word.Substring(0, position) + replacement + word.Substring(position + 1)));

        var swap = 1 + random.Next(word.Length - 2);
        var swapped = word.ToCharArray();
        (swapped[swap], swapped[swap + 1]) = (swapped[swap + 1], swapped[swap]);
        if (new string(swapped) != word)
        {
            yield return make(Variation.SwappedLetters, Replace(tokens, index, new string(swapped)));
        }

        var missing = 1 + random.Next(word.Length - 1);
        yield return make(Variation.MissingLetter, Replace(tokens, index, word.Remove(missing, 1)));
    }

    private static string? Transliterate(List<string> tokens, Random random)
    {
        var options = new List<(int Token, int Position, string From, string To)>();
        for (var t = 0; t < tokens.Count; t++)
        {
            if (tokens[t].Length < 4)
            {
                continue;
            }

            foreach (var (from, to) in Transliterations)
            {
                var at = tokens[t].IndexOf(from, 1, StringComparison.Ordinal);
                if (at > 0)
                {
                    options.Add((t, at, from, to));
                }
            }
        }

        if (options.Count == 0)
        {
            return null;
        }

        var pick = options[random.Next(options.Count)];
        var word = tokens[pick.Token];
        return Replace(tokens, pick.Token, word.Substring(0, pick.Position) + pick.To + word.Substring(pick.Position + pick.From.Length));
    }

    private static string Replace(List<string> tokens, int index, string word)
    {
        var copy = tokens.ToList();
        copy[index] = word;
        return string.Join(" ", copy);
    }
}
