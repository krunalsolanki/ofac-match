# Changelog

All notable changes are listed here. The project follows [Semantic Versioning](https://semver.org/).

## [2.1.1] - 2026-11-24

### Fixed

- MCP server: the Claude Desktop extension failed with "Folder not found: ${user_config.data_dir}" when the optional offline-list folder was left empty. An unfilled setting now counts as not set, so the list is downloaded from OFAC.

## [2.1.0] - 2026-11-24

### Added

- Web page: screen a whole list. Upload a CSV with a Name column (or First and Last name columns) and optional Year of birth or date of birth, Country and Type columns, up to 10,000 rows. The file is read in the browser and never uploaded. Results show which rows need review, and an audit file (CSV) has one line per potential match with the score, the explanation, the list date and empty columns for the reviewer's decision. Cells that a spreadsheet would run as a formula are stored as text.
- Web page: a "Try a sample list" button. Rows whose birth year differs from every match are shown as "likely a different person" and listed after the other potential matches; a different country alone no longer counts, because OFAC often lists only some of a party's addresses.

- `OfacMatch.Api`: a self-hosted REST API in Docker (`ghcr.io/krunalsolanki/ofacmatch-api`). Screen one name or up to 1,000 per request with your own references, compare two names, look up an SDN entry, and read the OpenAPI description. It loads the SDN list at start-up and refreshes it daily, answers 503 rather than "no match" when no list is loaded, can require an API key, and never logs request bodies.
- Python client for the REST API (`clients/python`, package `ofacmatch`): screen one name or any number (sent 1,000 at a time) with your own references, compare names, look up entries, and an `ofacmatch csv in.csv out.csv` command that writes a results file with reviewer columns. Standard library only.
- Web page: optional email, phone, website and crypto address on the one-name form ("More details"), and Email, Phone, Website and Crypto address columns in an uploaded list. Results show what matched (name, an identifier or both); the audit file adds the four input columns plus `matched_by` and `identifier_match`. The sample list includes a renamed supplier found only by its email.
- REST API, Python client 0.2.0 and MCP server: screen by email, phone, website and crypto address. `/v1/screen` and batch items take optional `email`, `phone`, `website` and `cryptoAddress`; each match has `matchedBy` and `identifierMatches`. A batch item matched by an identifier stays `review` even when the birth year differs. Python: `screen(..., email=, phone=, website=, crypto_address=)`, the same keys in batch items, Email, Phone, Website and Crypto address CSV columns, and `matched_by` and `identifier_match` result columns. MCP: the same optional parameters on `screen_name`, and a new `screen_identifier` tool that checks one value without a name.

- `SanctionsEntry.Identifiers`: the email addresses, websites, phone and fax numbers, and digital currency addresses that OFAC lists in each entry's remarks (on the current list: 370 emails, 1,306 websites, 185 phone and fax numbers, 1,075 crypto addresses).
- Screen by email, phone, website and crypto address: new `ScreeningRequest.Email`, `Phone`, `Website` and `CryptoAddress`, and `SanctionsScreener.ScreenIdentifier(type, value)`. An entry whose listed identifier matches is returned even when the name doesn't match, which catches a party that changed its name but kept its contact details (as in OFAC's 2026 Pegasus Worldwide Logistics settlement). `ScreeningMatch.MatchedBy` says whether the name, an identifier or both matched, `IdentifierMatches` lists them, and `Explain()` describes them. Matching is exact after normalization, plus same domain (only for listed websites, not on shared platforms) and same last 9 phone digits. On the current list every listed identifier finds its own entry, and 5,000 made-up emails, websites and phone numbers each raised no alerts.

### Changed

- The list loader also reads `SDN_COMMENTS.CSV`. SDN.CSV cuts an entry's remarks off at 1,000 characters, and OFAC publishes the rest in that file; 36 entries are affected, most of them listing email addresses, websites or crypto addresses near the end. `DownloadAsync` fetches it (a missing file is skipped), `LoadFromDirectory` reads it when present, and the web page uses it. `SanctionsList.Load` has a new overload that takes it.

- Screening is about 4 times faster with the same scores and matches. Each distinct listed name part is compared once per search, names that cannot reach the minimum score are dropped before pairing, and the whole-name comparison is skipped when the guard means it would not count.

## [2.0.0] - 2026-11-04

### Changed

- A search with more name parts than a listed name of two or more parts (an extra middle name, for example) is also scored from the listed name's side, reduced slightly for the extra parts (`ScoringOptions.ExtraPartPenalty`, default 0.1, chosen on a development set; see `docs/evaluation-protocol.md`). On the development set, searches with an extra middle name are found at a threshold of 85 in 100% of cases, against 19.3% before. Two-part searches score as before; three-part ordinary names alert more often at 85 (22.5% against 6.8%). Set `ScoringOptions.AllowExtraSearchedParts = false` for the previous behaviour.

### Added

- `NameScore.ScoredFromListedName`, shown in explanations and in the MCP `compare_names` result.
- Evaluation: harder variations (wrong first letter, two typos, nicknames, first-name initial, extra middle name), near-miss names, and a comparison with standard baselines and ablations (`--baselines`, `docs/baselines.md`).

## [0.1.2] - 2026-11-03

### Added

- Citation metadata (`CITATION.cff`, `.zenodo.json`) so each release is archived on Zenodo with a DOI.
- Privacy policy, Claude Code plugin and marketplace, Dockerfile and Glama metadata.

## [0.1.1] - 2026-11-03

### Added

- Try-it web page (`samples/OfacMatch.Web`) that screens names in the browser, published to GitHub Pages with a daily copy of the SDN list.
- One-click Claude Desktop extensions (`.mcpb`) for Windows and Mac, built and attached to each GitHub release. They are self-contained, so .NET isn't needed.

## [0.1.0] - 2026-11-03

### Added

- Load the SDN list from OFAC's legacy flat files (SDN.CSV, ALT.CSV, ADD.CSV), from disk or straight from OFAC's Sanctions List Service.
- Name screening with whole-name and name-part comparison (Jaro-Winkler and Soundex) on a 0-100 scale.
- Guard so a shared first name cannot carry the whole-name score (`ScoringOptions.RequireAllPartsForFullName`, on by default).
- Company legal forms are ignored when comparing companies, including one-letter typos in long forms such as "COMPALY".
- Explanations for every match: technique, matched alias and part-by-part pairing.
- Date-of-birth and country conflict flags, and filtering by entry type.
- Command-line sample.
- Evaluation harness (`eval/OfacMatch.Evaluation`) measuring recall over ten kinds of name variation and alert rates on ordinary names, with results in `docs/evaluation.md`.
- Documentation: scoring walkthrough, API reference, responsible-use notes, benefits and comparison.
- `OfacMatch.Mcp`: MCP server with `screen_name`, `screen_names`, `get_sanctions_entry`, `compare_names` and `get_list_info` tools, run with `dnx OfacMatch.Mcp`.
- Per-request `MinScore` and `MaxResults` on `ScreeningRequest`.
- Input validation across the public API. Blank, punctuation-only or overlong names, out-of-range scores, result counts, years of birth and scoring options now throw `ArgumentException` instead of failing later or silently returning no matches.
- Loading SDN data with no entries throws `InvalidDataException`, and `DownloadAsync` saves files only after they load, so a bad download can't replace a good copy or clear every name.
- The MCP tools reject invalid arguments with a clear message instead of clamping them, and report list-loading failures instead of a generic error.
- The command-line sample reports missing or invalid option values and unknown options instead of crashing.

### Changed

- Cleaned up project credits and evaluation wording.

### Fixed

- Downloads from OFAC failed with HTTP 403 because no User-Agent was sent; one is now added when the `HttpClient` has none.
