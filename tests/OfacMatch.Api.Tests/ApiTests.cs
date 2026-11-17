using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OfacMatch.Api.Tests;

/// <summary>Runs the API in memory against a small fictional list in a temporary folder. Nothing is downloaded.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private const string Sdn =
        "100,\"DOE, John Michael\",\"individual\",\"SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"DOB 15 Mar 1970; POB Exampletown, Freedonia.\"\r\n" +
        "101,\"EXAMPLE TRADING CO., LTD.\",-0- ,\"IRAN] [SDGT\",-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,-0- ,\"a.k.a. 'ETC'.\"\r\n" +
        "103,\"SEA STAR\",\"vessel\",\"IRAN\",-0- ,\"9ABC1\",\"Crude Oil Tanker\",-0- ,-0- ,\"Freedonia\",-0- ,-0- \r\n" +
        "\u001a";

    private const string Alt = "100,1,\"aka\",\"DOE, Johnny\",-0- \r\n\u001a";

    private const string Add =
        "100,1,-0- ,\"Exampletown\",\"Freedonia\",-0- \r\n" +
        "101,2,\"1 Harbor Road\",\"Port City\",\"Sylvania\",-0- \r\n" +
        "\u001a";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ofacmatch-api-tests-" + Guid.NewGuid().ToString("N"));

    public ApiFactory()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "SDN.CSV"), Sdn);
        File.WriteAllText(Path.Combine(_folder, "ALT.CSV"), Alt);
        File.WriteAllText(Path.Combine(_folder, "ADD.CSV"), Add);
    }

    public string? ApiKey { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("OFACMATCH_DATA_DIR", _folder);
        if (ApiKey is not null)
        {
            builder.UseSetting("OFACMATCH_API_KEY", ApiKey);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Screen_FindsListedNameWithExplanation()
    {
        var response = await _client.PostAsJsonAsync("/v1/screen", new { name = "John Doe", yearOfBirth = 1970 });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("potentialMatches").GetInt32());
        var match = body.GetProperty("matches")[0];
        Assert.Equal(100, match.GetProperty("entryId").GetInt32());
        Assert.True(match.GetProperty("score").GetDouble() >= 85);
        Assert.False(match.GetProperty("dateOfBirthConflict").GetBoolean());
        Assert.NotEmpty(match.GetProperty("parts").EnumerateArray());
        Assert.Contains("Entry 100", match.GetProperty("explanation").GetString());
        Assert.Contains("SDN list only", body.GetProperty("notice").GetString());
    }

    [Fact]
    public async Task Screen_FlagsDifferentBirthYear()
    {
        var body = await (await _client.PostAsJsonAsync("/v1/screen", new { name = "John Doe", yearOfBirth = 1990 }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("matches")[0].GetProperty("dateOfBirthConflict").GetBoolean());
    }

    [Fact]
    public async Task Screen_TypeFilterAndOrdinaryName()
    {
        var vessel = await (await _client.PostAsJsonAsync("/v1/screen", new { name = "John Doe", type = "vessel" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, vessel.GetProperty("potentialMatches").GetInt32());

        var ordinary = await (await _client.PostAsJsonAsync("/v1/screen", new { name = "Emily Carter" }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, ordinary.GetProperty("potentialMatches").GetInt32());
    }

    [Theory]
    [InlineData("{\"name\":\"\"}", "name")]
    [InlineData("{\"name\":\"!!!\"}", "name")]
    [InlineData("{}", "name")]
    [InlineData("{\"name\":\"John Doe\",\"minScore\":101}", "minScore")]
    [InlineData("{\"name\":\"John Doe\",\"maxResults\":0}", "maxResults")]
    [InlineData("{\"name\":\"John Doe\",\"type\":\"planet\"}", "type")]
    [InlineData("{\"name\":\"John Doe\",\"yearOfBirth\":1500}", "yearOfBirth")]
    public async Task Screen_RejectsBadInput(string json, string field)
    {
        var response = await _client.PostAsync("/v1/screen", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _), await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Batch_KeepsOrderReferencesAndReportsBadItems()
    {
        var response = await _client.PostAsJsonAsync("/v1/screen/batch", new
        {
            items = new object[]
            {
                new { reference = "C-1", name = "John Doe", yearOfBirth = 1970 },
                new { reference = "C-2", name = "Emily Carter" },
                new { reference = "C-3", name = "" },
                new { reference = "C-4", name = "John Doe", yearOfBirth = 1990 },
                new { reference = "C-5", name = "Example Trading Company Limited", type = "entity" },
            },
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var results = body.GetProperty("results");

        Assert.Equal(["C-1", "C-2", "C-3", "C-4", "C-5"], results.EnumerateArray().Select(r => r.GetProperty("reference").GetString()));
        Assert.Equal("review", results[0].GetProperty("status").GetString());
        Assert.Equal("no_match", results[1].GetProperty("status").GetString());
        Assert.Equal("error", results[2].GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(results[2].GetProperty("error").GetString()));
        Assert.Equal("likely_different_person", results[3].GetProperty("status").GetString());
        Assert.Equal("review", results[4].GetProperty("status").GetString());

        var summary = body.GetProperty("summary");
        Assert.Equal(5, summary.GetProperty("screened").GetInt32());
        Assert.Equal(2, summary.GetProperty("review").GetInt32());
        Assert.Equal(1, summary.GetProperty("errors").GetInt32());
    }

    [Fact]
    public async Task Batch_RejectsEmptyAndTooLarge()
    {
        var empty = await _client.PostAsJsonAsync("/v1/screen/batch", new { items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var tooMany = Enumerable.Range(0, Endpoints.MaxBatch + 1).Select(i => new { name = "John Doe" }).ToArray();
        var large = await _client.PostAsJsonAsync("/v1/screen/batch", new { items = tooMany });
        Assert.Equal(HttpStatusCode.BadRequest, large.StatusCode);
    }

    [Fact]
    public async Task Compare_ExplainsScore()
    {
        var body = await (await _client.PostAsJsonAsync("/v1/compare", new { name = "Jon Doe", otherName = "DOE, John" }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("score").GetDouble() > 85);
        Assert.Equal(2, body.GetProperty("parts").GetArrayLength());
    }

    [Fact]
    public async Task Entry_FoundAndNotFound()
    {
        var found = await (await _client.GetAsync("/v1/entries/100")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DOE, John Michael", found.GetProperty("name").GetString());
        Assert.Contains(found.GetProperty("aliases").EnumerateArray(), a => a.GetProperty("name").GetString() == "DOE, Johnny");

        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/v1/entries/999")).StatusCode);
    }

    [Fact]
    public async Task ListHealthAndOpenApi()
    {
        var list = await (await _client.GetAsync("/v1/list")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, list.GetProperty("entries").GetInt32());
        Assert.Equal("local folder", list.GetProperty("source").GetString());

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);

        var openApi = await _client.GetStringAsync("/openapi/v1.json");
        Assert.Contains("/v1/screen/batch", openApi);
    }
}

public sealed class ApiKeyTests : IDisposable
{
    private readonly ApiFactory _factory = new() { ApiKey = "test-key" };

    [Fact]
    public async Task RequiresKeyOnV1Only()
    {
        var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/screen", new { name = "John Doe" })).StatusCode);

        client.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/v1/list")).StatusCode);

        var good = _factory.CreateClient();
        good.DefaultRequestHeaders.Add("X-Api-Key", "test-key");
        Assert.Equal(HttpStatusCode.OK, (await good.PostAsJsonAsync("/v1/screen", new { name = "John Doe" })).StatusCode);

        // Health stays open for load balancers and container checks.
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/health")).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
