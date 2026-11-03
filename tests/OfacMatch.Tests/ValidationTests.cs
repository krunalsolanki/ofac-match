using System.Net;
using System.Net.Http;
using OfacMatch.Data;
using OfacMatch.Matching;
using OfacMatch.Screening;

namespace OfacMatch.Tests;

public class ValidationTests
{
    [Fact]
    public void Null_name_is_rejected()
    {
        Assert.Throws<ArgumentNullException>(() => new ScreeningRequest(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("--- !!! ...")]
    public void Names_without_letters_or_digits_are_rejected(string name)
    {
        // These would otherwise match nothing and look like a clean result.
        Assert.Throws<ArgumentException>(() => new ScreeningRequest(name));
    }

    [Fact]
    public void Very_long_names_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ScreeningRequest(new string('A', ScreeningRequest.MaxNameLength + 1)));
        Assert.NotNull(new ScreeningRequest(new string('A', ScreeningRequest.MaxNameLength)));
    }

    [Fact]
    public void Screen_rejects_a_blank_name()
    {
        var screener = new SanctionsScreener(TestList.Load());

        Assert.Throws<ArgumentException>(() => screener.Screen(" "));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100.5)]
    [InlineData(double.NaN)]
    public void Min_score_must_be_0_to_100(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningOptions { MinScore = score });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningRequest("John Doe") { MinScore = score });
    }

    [Fact]
    public void Max_results_must_be_at_least_1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningOptions { MaxResults = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningRequest("John Doe") { MaxResults = -5 });
    }

    [Theory]
    [InlineData(1799)]
    [InlineData(0)]
    [InlineData(-1970)]
    public void Year_of_birth_must_be_realistic(int year)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningRequest("John Doe") { YearOfBirth = year });
    }

    [Fact]
    public void Year_of_birth_in_the_future_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningRequest("John Doe") { YearOfBirth = DateTime.UtcNow.Year + 1 });
    }

    [Fact]
    public void Undefined_entry_type_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningRequest("John Doe") { Type = (EntryType)42 });
    }

    [Fact]
    public void Overlong_country_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ScreeningRequest("John Doe") { Country = new string('X', ScreeningRequest.MaxCountryLength + 1) });
    }

    [Fact]
    public void Options_reject_bad_values()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreeningOptions { YearOfBirthTolerance = -1 });
        Assert.Throws<ArgumentNullException>(() => new ScreeningOptions { Scoring = null! });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringOptions { CoverageWeight = 1.5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringOptions { PrefixBoostThreshold = -0.1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScoringOptions { MinPartScoreForFullName = double.NaN });
    }

    [Fact]
    public void Matching_helpers_reject_null()
    {
        Assert.Throws<ArgumentNullException>(() => JaroWinkler.Similarity(null!, "A"));
        Assert.Throws<ArgumentNullException>(() => Soundex.Encode(null!));
        Assert.Throws<ArgumentNullException>(() => NameNormalizer.Tokenize(null!));
        Assert.Throws<ArgumentNullException>(() => NameNormalizer.ToNaturalOrder(null!));
        Assert.Throws<ArgumentNullException>(() => new NameScorer().Score((string)null!, "A"));
        Assert.Throws<ArgumentException>(() => new NameScorer().Score(new[] { "JOHN", "" }, new[] { "JOHN" }));
    }

    [Fact]
    public void Jaro_winkler_prefix_scale_is_limited_so_scores_stay_within_1()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => JaroWinkler.Similarity("MARTHA", "MARHTA", prefixScale: 0.3));
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(1970, 13, null)]
    [InlineData(1970, 2, 30)]
    public void Partial_date_rejects_impossible_dates(int year, int? month, int? day)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PartialDate(year, month, day));
    }

    [Fact]
    public void Partial_date_needs_a_month_for_a_day()
    {
        Assert.Throws<ArgumentException>(() => new PartialDate(1970, null, 5));
    }

    [Fact]
    public void A_mistyped_day_in_remarks_keeps_the_year_and_month()
    {
        var entry = new SanctionsEntry(1, "DOE, John", EntryType.Individual, Array.Empty<string>(), null, "DOB 31 Feb 1970.");

        Assert.Equal("1970-02", Assert.Single(entry.DatesOfBirth).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><body>Service unavailable</body></html>")]
    public void Loading_data_with_no_entries_fails_instead_of_clearing_everyone(string sdn)
    {
        Assert.Throws<InvalidDataException>(() => SanctionsList.Load(new StringReader(sdn)));
    }

    [Fact]
    public void Load_from_directory_checks_the_folder()
    {
        Assert.Throws<ArgumentException>(() => SanctionsList.LoadFromDirectory(" "));
        Assert.Throws<DirectoryNotFoundException>(() => SanctionsList.LoadFromDirectory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://example.test/exports/")]
    [InlineData("/exports/")]
    public async Task Download_rejects_a_bad_base_url(string baseUrl)
    {
        using var http = new HttpClient();

        await Assert.ThrowsAsync<ArgumentException>(() => SanctionsList.DownloadAsync(http, baseUrl: baseUrl));
    }

    [Fact]
    public async Task A_bad_download_does_not_overwrite_saved_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "SDN.CSV"), TestList.Sdn);
        try
        {
            using var http = new HttpClient(new ErrorPage());

            await Assert.ThrowsAsync<InvalidDataException>(() => SanctionsList.DownloadAsync(http, folder, "https://example.test/exports/"));
            Assert.Equal(TestList.Sdn, File.ReadAllText(Path.Combine(folder, "SDN.CSV")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>Answers 200 OK with an HTML page, as a proxy or outage page might.</summary>
    private sealed class ErrorPage : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Maintenance</html>") });
    }
}
