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
}
