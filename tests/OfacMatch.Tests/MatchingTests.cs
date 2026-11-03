using OfacMatch.Matching;

namespace OfacMatch.Tests;

public class MatchingTests
{
    [Theory]
    [InlineData("MARTHA", "MARHTA", 0.9444)]
    [InlineData("DWAYNE", "DUANE", 0.8222)]
    [InlineData("DIXON", "DICKSONX", 0.7667)]
    public void Jaro_matches_published_values(string a, string b, double expected)
    {
        Assert.Equal(expected, JaroWinkler.Jaro(a, b), 4);
    }

    [Theory]
    [InlineData("MARTHA", "MARHTA", 0.9611)]
    [InlineData("DWAYNE", "DUANE", 0.8400)]
    [InlineData("DIXON", "DICKSONX", 0.8133)]
    public void JaroWinkler_matches_published_values(string a, string b, double expected)
    {
        Assert.Equal(expected, JaroWinkler.Similarity(a, b), 4);
    }

    [Fact]
    public void JaroWinkler_skips_prefix_bonus_below_threshold_unless_disabled()
    {
        var jaro = JaroWinkler.Jaro("ABCXYZ", "ABQRST");

        Assert.True(jaro <= 0.7);
        Assert.Equal(jaro, JaroWinkler.Similarity("ABCXYZ", "ABQRST"));
        Assert.True(JaroWinkler.Similarity("ABCXYZ", "ABQRST", boostThreshold: 0) > jaro);
    }

    [Theory]
    [InlineData("Robert", "R163")]
    [InlineData("Rupert", "R163")]
    [InlineData("Rubin", "R150")]
    [InlineData("Ashcraft", "A261")]
    [InlineData("Tymczak", "T522")]
    [InlineData("Pfister", "P236")]
    [InlineData("Lee", "L000")]
    [InlineData("", "")]
    public void Soundex_matches_standard_codes(string value, string expected)
    {
        Assert.Equal(expected, Soundex.Encode(value));
    }

    [Theory]
    [InlineData("José Müller", "JOSE MULLER")]
    [InlineData("AL-ZAWAHIRI, Dr. Ayman", "AL ZAWAHIRI AYMAN")]
    [InlineData("'ABBAS, Yasir", "ABBAS YASIR")]
    [InlineData("O'Neil", "ONEIL")]
    public void Tokenize_normalizes_names(string raw, string expected)
    {
        Assert.Equal(expected, string.Join(" ", NameNormalizer.Tokenize(raw)));
    }

    [Theory]
    [InlineData("EXAMPLE TRADING CO., LTD.", "EXAMPLE TRADING")]
    [InlineData("Acme Holdings S.A.", "ACME HOLDINGS")]
    [InlineData("Acme Holdings Compaly", "ACME HOLDINGS")]
    [InlineData("Acme Limted", "ACME")]
    [InlineData("Unlimited Corporate Services LLC", "UNLIMITED CORPORATE SERVICES")]
    public void Tokenize_can_drop_legal_suffixes(string raw, string expected)
    {
        Assert.Equal(expected, string.Join(" ", NameNormalizer.Tokenize(raw, removeLegalSuffixes: true)));
    }

    [Theory]
    [InlineData("DOE, John Michael", "John Michael DOE")]
    [InlineData("SEA STAR", "SEA STAR")]
    public void ToNaturalOrder_moves_last_name_to_the_end(string raw, string expected)
    {
        Assert.Equal(expected, NameNormalizer.ToNaturalOrder(raw));
    }

    [Fact]
    public void Scorer_reports_100_for_identical_names()
    {
        var score = new NameScorer().Score("John Doe", "John Doe");

        Assert.Equal(100, score.Score);
    }

    [Fact]
    public void Scorer_ignores_word_order_through_name_parts()
    {
        var score = new NameScorer().Score("Doe John", "John Doe");

        Assert.Equal(MatchTechnique.NameParts, score.Technique);
        Assert.True(score.Score >= 99, $"score {score.Score}");
    }

    [Fact]
    public void Scorer_credits_names_that_sound_alike()
    {
        var score = new NameScorer().Score("Mohammed Ahmed", "Muhammad Ahmad");

        Assert.All(score.Parts, p => Assert.True(p.SoundsAlike));
        Assert.True(score.Score >= 85, $"score {score.Score}");
    }

    [Fact]
    public void Scorer_does_not_let_a_shared_first_name_carry_the_score()
    {
        var strict = new NameScorer().Score("John Smith", "John Numbi");
        var ofacStyle = new NameScorer(new ScoringOptions { RequireAllPartsForFullName = false }).Score("John Smith", "John Numbi");

        Assert.False(strict.FullNameCounts);
        Assert.True(strict.Score < 85, $"strict score {strict.Score}");
        Assert.Equal(Math.Max(ofacStyle.FullNameScore, ofacStyle.NamePartsScore), ofacStyle.Score);
    }
}
