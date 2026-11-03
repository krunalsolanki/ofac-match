# Contributing to OfacMatch

Thanks for helping. Bug reports, name variations that score badly, documentation fixes and code are all welcome.

## Reporting a matching problem

Open an issue with:

- the name you searched,
- the listed name you expected (or did not expect) to match, with its SDN entry number,
- the score you got and the score you expected,
- the output of `match.Explain()` if there was a match.

Please do not include real customer data. Use the listed name and a made-up variation of it.

## Making a change

1. Fork the repository and create a branch.
2. Build and test:

   ```bash
   dotnet build
   dotnet test
   ```

   On Windows the tests also run on .NET Framework 4.8. Elsewhere, run `dotnet test -f net10.0`.
3. Add a test for every behavior change. Test data must be fictional; see `tests/OfacMatch.Tests/TestList.cs`.
4. Keep the library dependency-free and compatible with .NET Standard 2.0.
5. Open a pull request describing what changed and why.

## Scoring changes

Scoring changes affect compliance teams' alert volumes, so they need extra care:

- explain the change in the pull request, with before-and-after scores for a few examples,
- keep the old behavior reachable through an option when practical,
- note the change in `CHANGELOG.md`.

## Code of conduct

This project follows the [Contributor Covenant](CODE_OF_CONDUCT.md).
