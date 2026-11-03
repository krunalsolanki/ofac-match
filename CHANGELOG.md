# Changelog

All notable changes are listed here. The project follows [Semantic Versioning](https://semver.org/).

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
- Documentation: scoring walkthrough, API reference, responsible-use notes, benefits and comparison, publishing guide.
- `OfacMatch.Mcp`: MCP server with `screen_name`, `screen_names`, `get_sanctions_entry`, `compare_names` and `get_list_info` tools, run with `dnx OfacMatch.Mcp`.
- Per-request `MinScore` and `MaxResults` on `ScreeningRequest`.

### Changed

- Cleaned up project credits and evaluation wording.

### Fixed

- Downloads from OFAC failed with HTTP 403 because no User-Agent was sent; one is now added when the `HttpClient` has none.
