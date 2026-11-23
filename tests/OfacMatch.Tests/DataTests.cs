using OfacMatch.Data;

namespace OfacMatch.Tests;

public class DataTests
{
    [Fact]
    public void CsvReader_handles_quotes_nulls_and_end_of_file_marker()
    {
        var rows = CsvReader.Read(new StringReader("1,\"A, \"\"quoted\"\" name\",-0- ,plain\r\n\u001a")).ToList();

        var row = Assert.Single(rows);
        Assert.Equal(new string?[] { "1", "A, \"quoted\" name", null, "plain" }, row);
    }

    [Fact]
    public void CsvReader_keeps_line_breaks_inside_quotes()
    {
        var row = Assert.Single(CsvReader.Read(new StringReader("1,\"line one\nline two\"\n")));

        Assert.Equal("line one\nline two", row[1]);
    }

    [Fact]
    public void Load_appends_SDN_COMMENTS_to_cut_off_remarks()
    {
        // SDN.CSV stops mid-word at 1,000 characters; SDN_COMMENTS.CSV continues the same text.
        const string sdn = "200,\"EXAMPLE MACHINERY CO.\",-0- ,\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"Website www.exam\"\r\n";
        const string comments =
            "200,\"ple-machinery.test; Email Address\"\r\n" +
            "999,\"not on the list\"\r\n" +
            "200,\" sales@example-machinery.test.\"\r\n\u001a";

        var list = SanctionsList.Load(new StringReader(sdn), null, null, new StringReader(comments));

        Assert.Equal(
            "Website www.example-machinery.test; Email Address sales@example-machinery.test.",
            Assert.Single(list.Entries).Remarks);
    }

    [Fact]
    public void Load_without_comments_keeps_remarks_as_published()
    {
        Assert.Equal("DOB Jun 1980.", TestList.Load().Entries.Single(e => e.Id == 104).Remarks);
    }

    [Fact]
    public void Load_reads_entries_aliases_and_addresses()
    {
        var list = TestList.Load();

        Assert.Equal(5, list.Entries.Count);

        var doe = list.Entries.Single(e => e.Id == 100);
        Assert.Equal(EntryType.Individual, doe.Type);
        Assert.Equal(new[] { "SDGT" }, doe.Programs);
        Assert.Equal("DOE, Johnny", Assert.Single(doe.Aliases).Name);
        Assert.Equal(new[] { "Freedonia" }, doe.Countries);

        var company = list.Entries.Single(e => e.Id == 101);
        Assert.Equal(EntryType.Entity, company.Type);
        Assert.Equal(new[] { "IRAN", "SDGT" }, company.Programs);
        Assert.Equal(NameKind.Fka, Assert.Single(company.Aliases).Kind);

        Assert.Equal(EntryType.Vessel, list.Entries.Single(e => e.Id == 103).Type);
    }

    [Theory]
    [InlineData("DOB 15 Mar 1970; POB Exampletown.", "1970-03-15")]
    [InlineData("DOB Jun 1980.", "1980-06")]
    [InlineData("DOB 1965; alt. DOB 1966.", "1965|1966")]
    [InlineData("DOB circa 1950.", "1950")]
    [InlineData("Passport 12345.", "")]
    public void RemarksParser_extracts_dates_of_birth(string remarks, string expected)
    {
        var dates = RemarksParser.ParseDatesOfBirth(remarks);

        Assert.Equal(expected, string.Join("|", dates));
    }

    // Formats seen in OFAC's remarks. Values are fictional.
    [Theory]
    [InlineData("Email Address info@example.test.", "Email|Email Address|info@example.test|")]
    [InlineData("alt. Email Address Sales_Desk@Example.test", "Email|Email Address|Sales_Desk@Example.test|")]
    [InlineData("Website www.example.test", "Website|Website|www.example.test|")]
    [InlineData("Website:  WWW.EXAMPLE.TEST", "Website|Website|WWW.EXAMPLE.TEST|")]
    [InlineData("Website www.example.test. Offices worldwide.", "Website|Website|www.example.test|")]
    [InlineData("alt. Website http://www.example.test/index.html", "Website|Website|http://www.example.test/index.html|")]
    [InlineData("Phone Number 8612345678901", "Phone|Phone Number|8612345678901|")]
    [InlineData("Phone Number +52 686-383-0000.", "Phone|Phone Number|+52 686-383-0000|")]
    [InlineData("Telephone No. (218) (0)214770000", "Phone|Telephone|(218) (0)214770000|")]
    [InlineData("Telephone: 0097200000000", "Phone|Telephone|0097200000000|")]
    [InlineData("Fax No. (022) 7360000.", "Phone|Fax|(022) 7360000|")]
    [InlineData("Digital Currency Address - XBT 1ExampLeAddressXYZ123", "CryptoAddress|Digital Currency Address - XBT|1ExampLeAddressXYZ123|XBT")]
    [InlineData("alt. Digital Currency Address - ETH 0xAbC0000000000000000000000000000000000001", "CryptoAddress|Digital Currency Address - ETH|0xAbC0000000000000000000000000000000000001|ETH")]
    public void RemarksParser_extracts_identifiers(string remarks, string expected)
    {
        var id = Assert.Single(RemarksParser.ParseIdentifiers(remarks));

        Assert.Equal(expected, $"{id.Type}|{id.Label}|{id.Value}|{id.Currency}");
    }

    [Theory]
    [InlineData("Principal Director of Freedonia's National Telephone Company; DOB 1960.")]
    [InlineData("Passport 12345; Nationality Freedonia.")]
    [InlineData("Website")]
    [InlineData("")]
    [InlineData(null)]
    public void RemarksParser_ignores_parts_that_are_not_identifiers(string? remarks)
    {
        Assert.Empty(RemarksParser.ParseIdentifiers(remarks));
    }

    [Fact]
    public void Entry_lists_every_identifier_in_its_remarks()
    {
        // Shaped like a real entry: a company that later changed its name keeps the same contact details.
        var entry = new SanctionsEntry(
            300,
            "EXAMPLE MACHINERY CO., LTD.",
            EntryType.Entity,
            new[] { "ILLICIT-DRUGS-EO14059" },
            null,
            "Website www.example-machinery.test; Email Address sales@example-machinery.test; Phone Number 8615800000000; "
            + "Organization Established Date 01 Jan 2013; a.k.a. 'EXAMPLE GROUP LTD.'; alt. Email Address info@example-machinery.test.");

        Assert.Equal(
            new[]
            {
                "Website www.example-machinery.test",
                "Email Address sales@example-machinery.test",
                "Phone Number 8615800000000",
                "Email Address info@example-machinery.test",
            },
            entry.Identifiers.Select(i => i.ToString()));
    }
}
