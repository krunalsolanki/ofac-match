using OfacMatch.Data;
using OfacMatch.Screening;

namespace OfacMatch.Tests;

public class ScreenerTests
{
    private readonly SanctionsScreener _screener = new(TestList.Load());

    [Theory]
    [InlineData("John Michael Doe")]
    [InlineData("DOE, John Michael")]
    [InlineData("Doe John Michael")]
    public void Exact_names_score_100_in_any_order(string name)
    {
        var match = Assert.Single(_screener.Screen(name));

        Assert.Equal(100, match.Score);
        Assert.Equal(100, match.Entry.Id);
    }

    [Fact]
    public void Misspelled_names_still_match()
    {
        var match = Assert.Single(_screener.Screen("Jon Michel Doe"));

        Assert.Equal(100, match.Entry.Id);
        Assert.InRange(match.Score, 85, 99.9);
    }

    [Fact]
    public void Aliases_are_screened_and_reported()
    {
        var match = Assert.Single(_screener.Screen("Johnny Doe"));

        Assert.Equal(NameKind.Aka, match.NameKind);
        Assert.Equal("DOE, Johnny", match.MatchedName);
    }

    [Fact]
    public void Transliterated_names_match()
    {
        var match = Assert.Single(_screener.Screen("Ahmed Mohammed"));

        Assert.Equal(102, match.Entry.Id);
    }

    [Fact]
    public void Company_suffixes_do_not_block_a_match()
    {
        var match = Assert.Single(_screener.Screen("Example Trading Company"));

        Assert.Equal(101, match.Entry.Id);
        Assert.Equal(100, match.Score);
    }

    [Fact]
    public void Former_names_are_matched()
    {
        var match = Assert.Single(_screener.Screen("Example Traders"));

        Assert.Equal(NameKind.Fka, match.NameKind);
    }

    [Theory]
    [InlineData("Jane Smith")]
    [InlineData("Krunal Solanki")]
    [InlineData("John Smith")]
    public void Unrelated_names_do_not_match(string name)
    {
        Assert.Empty(_screener.Screen(name));
    }

    [Fact]
    public void Type_filter_limits_results()
    {
        var request = new ScreeningRequest("Sea Star") { Type = EntryType.Individual };

        Assert.Empty(_screener.Screen(request));
        Assert.Single(_screener.Screen("Sea Star"));
    }

    [Fact]
    public void Date_of_birth_and_country_conflicts_are_flagged_not_hidden()
    {
        var request = new ScreeningRequest("John Michael Doe") { YearOfBirth = 1990, Country = "Sylvania" };

        var match = Assert.Single(_screener.Screen(request));

        Assert.True(match.DateOfBirthConflict);
        Assert.True(match.CountryConflict);
    }

    [Fact]
    public void Matching_date_of_birth_is_not_a_conflict()
    {
        var match = Assert.Single(_screener.Screen(new ScreeningRequest("John Michael Doe") { YearOfBirth = 1971 }));

        Assert.False(match.DateOfBirthConflict);
    }

    [Fact]
    public void Explain_describes_the_evidence()
    {
        var text = _screener.Screen("Jon Michel Doe").Single().Explain();

        Assert.Contains("Entry 100", text);
        Assert.Contains("JON -> JOHN", text);
        Assert.Contains("sounds alike", text);
    }

    [Fact]
    public void Lower_threshold_returns_more_candidates()
    {
        var broad = new SanctionsScreener(TestList.Load(), new ScreeningOptions { MinScore = 50 });

        Assert.True(broad.Screen("John Doe").Count >= _screener.Screen("John Doe").Count);
    }
}
