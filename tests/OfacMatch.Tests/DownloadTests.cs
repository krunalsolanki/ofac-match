using System.Net;
using System.Net.Http;
using System.Text;
using OfacMatch.Data;

namespace OfacMatch.Tests;

public class DownloadTests
{
    [Fact]
    public async Task Download_sends_a_user_agent_and_loads_all_three_files()
    {
        var handler = new FakeOfac();
        using var http = new HttpClient(handler);

        var list = await SanctionsList.DownloadAsync(http, baseUrl: "https://example.test/exports/");

        Assert.Equal(5, list.Entries.Count);
        Assert.Single(list.Entries.Single(e => e.Id == 100).Aliases);
        Assert.Equal(new[] { "SDN.CSV", "ALT.CSV", "ADD.CSV" }, handler.Requested);
        Assert.All(handler.UserAgents, ua => Assert.StartsWith("OfacMatch/", ua));
    }

    [Fact]
    public async Task Download_keeps_the_callers_own_user_agent()
    {
        var handler = new FakeOfac();
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MyBank/2.0");

        await SanctionsList.DownloadAsync(http, baseUrl: "https://example.test/exports/");

        Assert.All(handler.UserAgents, ua => Assert.Equal("MyBank/2.0", ua));
    }

    /// <summary>Behaves like OFAC's service: 403 without a User-Agent, otherwise the file.</summary>
    private sealed class FakeOfac : HttpMessageHandler
    {
        public List<string> Requested { get; } = new();

        public List<string> UserAgents { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var userAgent = request.Headers.UserAgent.ToString();
            if (userAgent.Length == 0)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            var file = request.RequestUri!.Segments.Last();
            Requested.Add(file);
            UserAgents.Add(userAgent);

            var body = file switch
            {
                "SDN.CSV" => TestList.Sdn,
                "ALT.CSV" => TestList.Alt,
                _ => TestList.Add,
            };

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/csv"),
            });
        }
    }
}
