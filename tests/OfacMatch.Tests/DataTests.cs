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
