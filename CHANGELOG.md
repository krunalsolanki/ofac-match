# Changelog

All notable changes are listed here. The project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- Try-it web page (`samples/OfacMatch.Web`) that screens names in the browser, published to GitHub Pages with a daily copy of the SDN list.
- One-click Claude Desktop extensions (`.mcpb`) for Windows and Mac, built and attached to each GitHub release. They are self-contained, so .NET isn't needed.

## [0.1.0] - Unreleased

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
