# API and configuration reference

All public types live in two namespaces: `OfacMatch.Data` (the list) and `OfacMatch.Screening` (screening). `OfacMatch.Matching` exposes the building blocks (Jaro-Winkler, Soundex, normalization, the scorer) for advanced use.

## Loading the list: `SanctionsList`

| Member | Description |
|---|---|
| `DownloadAsync(HttpClient, saveToDirectory?, baseUrl?, cancellationToken?)` | Downloads SDN.CSV, ALT.CSV and ADD.CSV from OFAC's Sanctions List Service. Optionally saves them for offline use. |
| `LoadFromDirectory(string)` | Loads SDN.CSV, and ALT.CSV and ADD.CSV when present, from a folder. |
| `Load(TextReader sdn, TextReader? alt, TextReader? add)` | Loads from any readers, e.g. files from your own storage. Throws `InvalidDataException` if the SDN data has no entries. |
| `Entries` | All entries (`SanctionsEntry`). |
| `LoadedAt` | When this copy was loaded. |
| `DefaultBaseUrl` | `https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/` |
| `UserAgent` | Sent with downloads when your `HttpClient` has no User-Agent of its own. OFAC's service rejects requests without one (HTTP 403). |

Loading never returns an empty list. An empty file, a truncated download or an HTML error page saved as SDN.CSV throws `InvalidDataException`, because screening against an empty list would clear every name. `DownloadAsync` saves files only after they load, so a bad download never replaces a good copy. It also checks that `baseUrl` is an absolute http or https address.

A loaded list and a `SanctionsScreener` built on it are read-only and safe to share across threads. Build them once and reuse them; reload when OFAC publishes an update.

## An entry: `SanctionsEntry`

| Member | Description |
|---|---|
| `Id` | OFAC's entry number (`ent_num`). |
| `Name` | Primary name as published, e.g. `DOE, John`. |
| `Type` | `Individual`, `Entity`, `Vessel` or `Aircraft`. |
| `Programs` | Program tags, e.g. `SDGT`, `IRAN`. |
| `Title`, `Remarks` | As published. |
| `DatesOfBirth` | `PartialDate` values parsed from remarks ("DOB 15 Mar 1970", "DOB 1965", "DOB circa 1950"). |
| `Aliases` | `SanctionsAlias` (aka, fka, nka). |
| `Addresses`, `Countries` | From ADD.CSV. |

## Screening: `SanctionsScreener`

```csharp
var screener = new SanctionsScreener(list, options);
IReadOnlyList<ScreeningMatch> matches = screener.Screen("name");
IReadOnlyList<ScreeningMatch> matches = screener.Screen(request);
```

Results are ordered best first, with at most one match per entry (its best-scoring name).

### `ScreeningRequest`

| Property | Default | Description |
|---|---|---|
| `Name` | required | Any order: "John Doe" or "DOE, John". Must have at least one letter or digit and at most 500 characters. |
| `Type` | all | Screen only one entry type, e.g. `EntryType.Individual` for a borrower. |
| `YearOfBirth` | none | 1800 to the current year. Flags matches whose listed dates of birth are all more than `YearOfBirthTolerance` years away. |
| `Country` | none | At most 100 characters. Flags matches whose listed addresses are all in other countries. |
| `MinScore` | options | 0–100. Overrides `ScreeningOptions.MinScore` for this request. |
| `MaxResults` | options | At least 1. Overrides `ScreeningOptions.MaxResults` for this request. |

Values outside these limits throw `ArgumentException` (or `ArgumentOutOfRangeException`) when you set them, not later during screening. A blank or punctuation-only name is rejected rather than returning no matches, so a data problem can't look like a clean result.

Flags never remove a match. They give the reviewer facts to clear or escalate it.

### `ScreeningOptions`

| Property | Default | Description |
|---|---|---|
| `MinScore` | 85 | Lowest score returned, 0–100. OFAC recommends no threshold; choose one from your risk assessment and the [evaluation](evaluation.md). |
| `MaxResults` | 25 | Cap on matches returned, at least 1. |
| `YearOfBirthTolerance` | 1 | Years of difference (0–100) allowed before a date of birth is a conflict. |
| `Scoring` | see below | Name scoring settings. Cannot be null. |

### `ScoringOptions`

| Property | Default | Description |
|---|---|---|
| `RequireAllPartsForFullName` | `true` | Whole-name score counts only when every searched part has a partner scoring at least `MinPartScoreForFullName`. `false` = always the higher technique, as OFAC's FAQ describes. |
| `MinPartScoreForFullName` | 0.70 | Part score needed for the guard above. |
| `CoverageWeight` | 0.05 | Share of the name-parts score that depends on how much of the listed name was covered. |
| `PrefixBoostThreshold` | 0.70 | Jaro score above which the Jaro-Winkler prefix bonus applies. 0 = always apply it. |
| `AllowExtraSearchedParts` | `true` | When the search has more parts than a listed name of at least two parts (an extra middle name), also score from the listed name's side. `false` = the behaviour of version 0.1.2. See [how scoring works](how-scoring-works.md#4-technique-2-name-parts). |
| `ExtraPartPenalty` | 0.10 | For the rule above: how much each searched letter with no listed partner reduces the score, relative to a letter that failed to match. 0 ignores extra parts; 1 counts them as fully missed. Chosen on the development set ([protocol](evaluation-protocol.md)). |

All five numbers must be between 0 and 1.

### `ScreeningMatch`

| Member | Description |
|---|---|
| `Score` | 0–100. |
| `Entry` | The listed entry. |
| `MatchedName`, `NameKind` | The name or alias that matched, and whether it is primary, aka, fka or nka. |
| `NameScore` | Evidence: `FullNameScore`, `NamePartsScore`, `FullNameCounts`, `ScoredFromListedName` (true when extra searched parts were allowed for), `Technique`, and `Parts` (each `PartMatch` with `QueryPart`, `ListedPart`, `Similarity`, `SoundsAlike`, `Score`). |
| `DateOfBirthConflict`, `CountryConflict` | Flags described above. |
| `Explain()` | Plain-text explanation for an analyst or the audit file. |

## Building blocks: `OfacMatch.Matching`

| Type | Use |
|---|---|
| `NameNormalizer.Tokenize(name, removeLegalSuffixes)` | The normalization used for screening. |
| `NameNormalizer.ToNaturalOrder(name)` | `LAST, First` → `First LAST`. |
| `JaroWinkler.Jaro(a, b)`, `JaroWinkler.Similarity(a, b, boostThreshold, prefixScale)` | String similarity. `boostThreshold` must be 0–1 and `prefixScale` 0–0.25, so the result stays within 0–1. |
| `Soundex.Encode(value)` | American Soundex code. |
| `NameScorer.Score(query, listed)` | Score two names directly, with full evidence. |

These methods throw `ArgumentNullException` for null input.

## Command line

```bash
dotnet run --project samples/OfacMatch.Cli -- "<name>" [--min 85] [--yob 1970] [--country <name>] [--individuals] [--data <folder>]
```

Without `--data`, the list is downloaded from OFAC and cached in `./ofac-data` for a day. The exit code is 0 for no matches and 2 when there are potential matches, so scripts can act on it.
