# API and configuration reference

All public types live in two namespaces: `OfacMatch.Data` (the list) and `OfacMatch.Screening` (screening). `OfacMatch.Matching` exposes the building blocks (Jaro-Winkler, Soundex, normalization, the scorer) for advanced use.

## Loading the list: `SanctionsList`

| Member | Description |
|---|---|
| `DownloadAsync(HttpClient, saveToDirectory?, baseUrl?, cancellationToken?)` | Downloads SDN.CSV, ALT.CSV and ADD.CSV from OFAC's Sanctions List Service. Optionally saves them for offline use. |
| `LoadFromDirectory(string)` | Loads SDN.CSV, and ALT.CSV and ADD.CSV when present, from a folder. |
| `Load(TextReader sdn, TextReader? alt, TextReader? add)` | Loads from any readers, e.g. files from your own storage. |
| `Entries` | All entries (`SanctionsEntry`). |
| `LoadedAt` | When this copy was loaded. |
| `DefaultBaseUrl` | `https://sanctionslistservice.ofac.treas.gov/api/PublicationPreview/exports/` |
| `UserAgent` | Sent with downloads when your `HttpClient` has no User-Agent of its own. OFAC's service rejects requests without one (HTTP 403). |

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
| `Name` | required | Any order: "John Doe" or "DOE, John". |
| `Type` | all | Screen only one entry type, e.g. `EntryType.Individual` for a borrower. |
| `YearOfBirth` | none | Flags matches whose listed dates of birth are all more than `YearOfBirthTolerance` years away. |
| `Country` | none | Flags matches whose listed addresses are all in other countries. |
| `MinScore` | options | Overrides `ScreeningOptions.MinScore` for this request. |
| `MaxResults` | options | Overrides `ScreeningOptions.MaxResults` for this request. |

Flags never remove a match. They give the reviewer facts to clear or escalate it.

### `ScreeningOptions`

| Property | Default | Description |
|---|---|---|
| `MinScore` | 85 | Lowest score returned. OFAC recommends no threshold; choose one from your risk assessment and the [evaluation](evaluation.md). |
| `MaxResults` | 25 | Cap on matches returned. |
| `YearOfBirthTolerance` | 1 | Years of difference allowed before a date of birth is a conflict. |
| `Scoring` | see below | Name scoring settings. |

### `ScoringOptions`

| Property | Default | Description |
|---|---|---|
| `RequireAllPartsForFullName` | `true` | Whole-name score counts only when every searched part has a partner scoring at least `MinPartScoreForFullName`. `false` = always the higher technique, as OFAC's FAQ describes. |
| `MinPartScoreForFullName` | 0.70 | Part score needed for the guard above. |
| `CoverageWeight` | 0.05 | Share of the name-parts score that depends on how much of the listed name was covered. |
| `PrefixBoostThreshold` | 0.70 | Jaro score above which the Jaro-Winkler prefix bonus applies. 0 = always apply it. |

### `ScreeningMatch`

| Member | Description |
|---|---|
| `Score` | 0–100. |
| `Entry` | The listed entry. |
| `MatchedName`, `NameKind` | The name or alias that matched, and whether it is primary, aka, fka or nka. |
| `NameScore` | Evidence: `FullNameScore`, `NamePartsScore`, `FullNameCounts`, `Technique`, and `Parts` (each `PartMatch` with `QueryPart`, `ListedPart`, `Similarity`, `SoundsAlike`, `Score`). |
| `DateOfBirthConflict`, `CountryConflict` | Flags described above. |
| `Explain()` | Plain-text explanation for an analyst or the audit file. |

## Building blocks: `OfacMatch.Matching`

| Type | Use |
|---|---|
| `NameNormalizer.Tokenize(name, removeLegalSuffixes)` | The normalization used for screening. |
| `NameNormalizer.ToNaturalOrder(name)` | `LAST, First` → `First LAST`. |
| `JaroWinkler.Jaro(a, b)`, `JaroWinkler.Similarity(a, b, boostThreshold, prefixScale)` | String similarity. |
| `Soundex.Encode(value)` | American Soundex code. |
| `NameScorer.Score(query, listed)` | Score two names directly, with full evidence. |

## Command line

```bash
dotnet run --project samples/OfacMatch.Cli -- "<name>" [--min 85] [--yob 1970] [--country <name>] [--individuals] [--data <folder>]
```

Without `--data`, the list is downloaded from OFAC and cached in `./ofac-data` for a day. The exit code is 0 for no matches and 2 when there are potential matches, so scripts can act on it.
