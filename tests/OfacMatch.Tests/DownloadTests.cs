using System.Net;
using System.Net.Http;
using System.Text;
using OfacMatch.Data;

namespace OfacMatch.Tests;

public class DownloadTests
{
    [Fact]
    public async Task Download_sends_a_user_agent_and_loads_all_four_files()
    {
        var handler = new FakeOfac();
        using var http = new HttpClient(handler);

        var list = await SanctionsList.DownloadAsync(http, baseUrl: "https://example.test/exports/");

        Assert.Equal(5, list.Entries.Count);
        Assert.Single(list.Entries.Single(e => e.Id == 100).Aliases);
        Assert.Equal(new[] { "SDN.CSV", "ALT.CSV", "ADD.CSV", "SDN_COMMENTS.CSV" }, handler.Requested);
        Assert.All(handler.UserAgents, ua => Assert.StartsWith("OfacMatch/", ua));
        Assert.EndsWith(TestList.CommentsTail, list.Entries.Single(e => e.Id == 104).Remarks);
    }

    [Fact]
    public async Task Download_works_without_the_comments_file()
    {
        var handler = new FakeOfac { CommentsStatus = HttpStatusCode.NotFound };
        using var http = new HttpClient(handler);

        var list = await SanctionsList.DownloadAsync(http, baseUrl: "https://example.test/exports/");

        Assert.Equal("DOB Jun 1980.", list.Entries.Single(e => e.Id == 104).Remarks);
    }

    [Fact]
    public async Task Download_fails_when_the_comments_file_fails_for_another_reason()
    {
        var handler = new FakeOfac { CommentsStatus = HttpStatusCode.InternalServerError };
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => SanctionsList.DownloadAsync(http, baseUrl: "https://example.test/exports/"));
    }

    [Fact]
    public async Task Download_saves_the_comments_file_and_removes_a_stale_one()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ofacmatch-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var http = new HttpClient(new FakeOfac()))
            {
                await SanctionsList.DownloadAsync(http, folder, "https://example.test/exports/");
            }

            Assert.True(File.Exists(Path.Combine(folder, "SDN_COMMENTS.CSV")));
            Assert.EndsWith(TestList.CommentsTail, SanctionsList.LoadFromDirectory(folder).Entries.Single(e => e.Id == 104).Remarks);

            using (var http = new HttpClient(new FakeOfac { CommentsStatus = HttpStatusCode.NotFound }))
            {
                await SanctionsList.DownloadAsync(http, folder, "https://example.test/exports/");
            }

            Assert.False(File.Exists(Path.Combine(folder, "SDN_COMMENTS.CSV")));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
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

        public HttpStatusCode CommentsStatus { get; set; } = HttpStatusCode.OK;

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

            if (file == "SDN_COMMENTS.CSV" && CommentsStatus != HttpStatusCode.OK)
            {
                return Task.FromResult(new HttpResponseMessage(CommentsStatus));
            }

            var body = file switch
            {
                "SDN.CSV" => TestList.Sdn,
                "ALT.CSV" => TestList.Alt,
                "ADD.CSV" => TestList.Add,
                "SDN_COMMENTS.CSV" => TestList.Comments,
                _ => null,
            };

            if (body is null)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/csv"),
            });
        }
    }
}
