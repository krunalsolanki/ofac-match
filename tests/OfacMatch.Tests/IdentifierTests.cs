using OfacMatch.Data;
using OfacMatch.Screening;

namespace OfacMatch.Tests;

/// <summary>Screening by email, website, phone and crypto address. All entries and values are fictional.</summary>
public class IdentifierTests
{
    private const string Sdn =
        "300,\"EXAMPLE MACHINERY CO., LTD.\",-0- ,\"ILLICIT-DRUGS-EO14059\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"Website www.example-machinery.test; Email Address yk@example-machinery.test; Phone Number 8615800001234; a.k.a. 'EXAMPLE GROUP LTD.'.\"\r\n" +
        "301,\"DOE, Jane\",\"individual\",\"CYBER2\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"DOB 1970; Email Address jane.doe@webmail.test; Digital Currency Address - XBT 1ExampLeBitcoinAddr9xyz; alt. Digital Currency Address - ETH 0xABCDEF0000000000000000000000000000000001.\"\r\n" +
        "302,\"ROE, Richard\",\"individual\",\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"Website http://sample-university.edu.test/roe.\"\r\n" +
        "303,\"EXAMPLE PAGE TRADING\",-0- ,\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"Website www.facebook.com/examplepage; alt. Website t.me/ExampleChannel.\"\r\n" +
        "304,\"SAMPLE AIRLINES\",-0- ,\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"Telephone No. (218) (0)214770000; Fax: 0097200000099.\"\r\n" +
        "305,\"EXAMPLE MACHINE TOOLS\",-0- ,\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- \r\n" +
        "\u001a";

    private readonly SanctionsScreener _screener =
        new(SanctionsList.Load(new StringReader(Sdn)));

    [Fact]
    public void Renamed_company_is_found_by_an_email_on_its_listed_website_domain()
    {
        // The Pegasus case: the supplier traded under a new name, but its contact details were on the list.
        var request = new ScreeningRequest("Brand New Precision LLC") { Email = "sales@example-machinery.test" };

        var match = Assert.Single(_screener.Screen(request));

        Assert.Equal(300, match.Entry.Id);
        Assert.Equal(MatchBasis.Identifier, match.MatchedBy);
        Assert.True(match.Score < 85);
        var id = Assert.Single(match.IdentifierMatches);
        Assert.Equal(IdentifierMatchLevel.SameDomain, id.Level);
        Assert.Contains("Identifier match; name score", match.Explain());
        Assert.Contains(
            "Email sales@example-machinery.test has the same domain as listed Website www.example-machinery.test",
            match.Explain());
    }

    [Theory]
    [InlineData("YK@Example-Machinery.test")]
    [InlineData("mailto:yk@example-machinery.test")]
    public void Listed_email_matches_exactly_ignoring_case(string email)
    {
        var found = _screener.ScreenIdentifier(IdentifierType.Email, email);

        Assert.Contains(found, m => m.Entry.Id == 300 && m.Level == IdentifierMatchLevel.Exact && m.Listed.Type == IdentifierType.Email);
    }

    [Fact]
    public void Email_domain_of_a_listed_email_alone_does_not_match()
    {
        // Listed emails are often on webmail or ISP domains shared by millions of people.
        Assert.Empty(_screener.ScreenIdentifier(IdentifierType.Email, "someone.else@webmail.test"));
        Assert.Single(_screener.ScreenIdentifier(IdentifierType.Email, "jane.doe@webmail.test"));
    }

    [Theory]
    [InlineData("+86 158 0000 1234", IdentifierMatchLevel.Exact)]
    [InlineData("0086-158-0000-1234", IdentifierMatchLevel.Exact)]
    [InlineData("158 0000 1234", IdentifierMatchLevel.SameNumberEnding)]
    public void Phone_numbers_match_in_any_format(string phone, IdentifierMatchLevel level)
    {
        var match = Assert.Single(_screener.ScreenIdentifier(IdentifierType.Phone, phone));

        Assert.Equal(300, match.Entry.Id);
        Assert.Equal(level, match.Level);
    }

    [Theory]
    [InlineData("+218 21 4770000", "Telephone")]
    [InlineData("+972 0000 0099", "Fax")]
    public void Telephone_and_fax_numbers_match_without_trunk_or_international_prefix(string phone, string label)
    {
        var match = Assert.Single(_screener.ScreenIdentifier(IdentifierType.Phone, phone));

        Assert.Equal(304, match.Entry.Id);
        Assert.Equal(label, match.Listed.Label);
        Assert.Equal(IdentifierMatchLevel.Exact, match.Level);
    }

    [Theory]
    [InlineData("example-machinery.test", IdentifierMatchLevel.Exact)]
    [InlineData("HTTPS://WWW.Example-Machinery.test/", IdentifierMatchLevel.Exact)]
    [InlineData("https://example-machinery.test/contact", IdentifierMatchLevel.SameDomain)]
    [InlineData("shop.example-machinery.test", IdentifierMatchLevel.SameDomain)]
    public void Websites_match_by_page_or_by_domain(string website, IdentifierMatchLevel level)
    {
        var match = Assert.Single(_screener.ScreenIdentifier(IdentifierType.Website, website));

        Assert.Equal(300, match.Entry.Id);
        Assert.Equal(level, match.Level);
    }

    [Fact]
    public void Shared_platforms_match_only_the_listed_page()
    {
        Assert.Empty(_screener.ScreenIdentifier(IdentifierType.Website, "facebook.com/someotherpage"));
        Assert.Empty(_screener.ScreenIdentifier(IdentifierType.Website, "https://t.me/OtherChannel"));
        Assert.Empty(_screener.ScreenIdentifier(IdentifierType.Email, "anyone@facebook.com"));

        var match = Assert.Single(_screener.ScreenIdentifier(IdentifierType.Website, "https://www.facebook.com/examplepage/"));
        Assert.Equal(303, match.Entry.Id);
        Assert.Equal(IdentifierMatchLevel.Exact, match.Level);
    }

    [Fact]
    public void A_page_on_a_university_site_does_not_flag_the_whole_university()
    {
        Assert.Empty(_screener.ScreenIdentifier(IdentifierType.Email, "student@sample-university.edu.test"));
        Assert.Empty(_screener.ScreenIdentifier(IdentifierType.Website, "sample-university.edu.test/admissions"));
        Assert.Single(_screener.ScreenIdentifier(IdentifierType.Website, "http://sample-university.edu.test/roe/"));
    }

    [Fact]
    public void Crypto_addresses_match_exactly_and_hex_addresses_ignore_case()
    {
        Assert.Single(_screener.ScreenIdentifier(IdentifierType.CryptoAddress, "1ExampLeBitcoinAddr9xyz"));
        Assert.Empty(_screener.ScreenIdentifier(IdentifierType.CryptoAddress, "1examplebitcoinaddr9xyz"));
        Assert.Single(_screener.ScreenIdentifier(IdentifierType.CryptoAddress, "0xabcdef0000000000000000000000000000000001"));
    }

    [Fact]
    public void Name_and_identifier_together_are_reported_as_both()
    {
        var request = new ScreeningRequest("Example Machinery Co") { Phone = "+8615800001234" };

        var match = _screener.Screen(request).First();

        Assert.Equal(300, match.Entry.Id);
        Assert.Equal(MatchBasis.NameAndIdentifier, match.MatchedBy);
        Assert.Single(match.IdentifierMatches);
    }

    [Fact]
    public void Identifier_matches_come_first_and_ignore_the_type_filter()
    {
        // A person's email on a listed company's domain is worth a look even when screening individuals only.
        var request = new ScreeningRequest("Example Machine Tools")
        {
            Email = "a.buyer@example-machinery.test",
            Type = EntryType.Entity,
        };

        var matches = _screener.Screen(request);

        Assert.Equal(300, matches[0].Entry.Id);
        Assert.Contains(matches, m => m.Entry.Id == 305 && m.MatchedBy == MatchBasis.Name);

        var individuals = _screener.Screen(new ScreeningRequest("Jane Roe") { Email = "a.buyer@example-machinery.test", Type = EntryType.Individual });
        Assert.Contains(individuals, m => m.Entry.Id == 300 && m.MatchedBy == MatchBasis.Identifier);
    }

    [Fact]
    public void Without_identifiers_results_are_name_matches_only()
    {
        var match = _screener.Screen("Example Machinery Co").Single(m => m.Entry.Id == 300);

        Assert.Equal(MatchBasis.Name, match.MatchedBy);
        Assert.Empty(match.IdentifierMatches);
        Assert.DoesNotContain("Identifier", match.Explain());
    }

    [Fact]
    public void Blank_identifiers_are_treated_as_not_given()
    {
        var request = new ScreeningRequest("Nobody") { Email = " ", Phone = "", Website = null, CryptoAddress = "\t" };

        Assert.Null(request.Email);
        Assert.Null(request.Phone);
        Assert.Null(request.CryptoAddress);
    }

    [Theory]
    [InlineData(IdentifierType.Email, "not-an-email")]
    [InlineData(IdentifierType.Email, "two@@example.test")]
    [InlineData(IdentifierType.Phone, "123 456")]
    [InlineData(IdentifierType.Website, "localhost")]
    [InlineData(IdentifierType.CryptoAddress, "short")]
    [InlineData(IdentifierType.CryptoAddress, "has a space in it")]
    public void Invalid_identifiers_are_rejected(IdentifierType type, string value)
    {
        Assert.Throws<ArgumentException>(() => _screener.ScreenIdentifier(type, value));
    }

    [Fact]
    public void Overlong_identifier_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new ScreeningRequest("Nobody") { Website = new string('a', 501) + ".test" });
    }
}
