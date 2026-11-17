using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Json;
using OfacMatch.Api;

// OfacMatch REST API: self-hosted, so the names you screen stay on your own server.
//
// Settings (environment variables):
//   OFACMATCH_DATA_DIR       folder with SDN.CSV, ALT.CSV, ADD.CSV to use offline (never downloads)
//   OFACMATCH_CACHE_DIR      where the downloaded list is kept
//   OFACMATCH_REFRESH_HOURS  how old the list may get before it is downloaded again (default 24)
//   OFACMATCH_API_KEY        if set, every /v1 request needs the header "X-Api-Key: <key>"
var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<JsonOptions>(o => o.SerializerOptions.PropertyNameCaseInsensitive = true);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddSingleton(ListSettings.From(builder.Configuration));
builder.Services.AddSingleton<ListProvider>();
builder.Services.AddHostedService<ListRefresher>();

// A batch of 1,000 names is well under 1 MB; refuse anything much bigger.
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2 * 1024 * 1024);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Optional shared key. Compared in constant time so the key can't be guessed from response times.
var apiKey = app.Configuration["OFACMATCH_API_KEY"];
if (!string.IsNullOrEmpty(apiKey))
{
    var expected = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
    app.Use(async (context, next) =>
    {
        if (context.Request.Path.StartsWithSegments("/v1"))
        {
            var given = SHA256.HashData(Encoding.UTF8.GetBytes(context.Request.Headers["X-Api-Key"].ToString()));
            if (!CryptographicOperations.FixedTimeEquals(given, expected))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new { title = "Missing or wrong X-Api-Key header.", status = 401 });
                return;
            }
        }

        await next();
    });
}

app.MapOpenApi();
app.MapGet("/", () => Results.Ok(new
{
    name = "OfacMatch API",
    version = typeof(Endpoints).Assembly.GetName().Version?.ToString(3),
    openApi = "/openapi/v1.json",
    endpoints = new[] { "POST /v1/screen", "POST /v1/screen/batch", "POST /v1/compare", "GET /v1/entries/{id}", "GET /v1/list", "GET /health" },
    notice = Endpoints.Notice,
})).ExcludeFromDescription();

app.MapGet("/health", (ListProvider provider) => provider.Current is null
        ? Results.Json(new { status = "loading", list = (object?)null }, statusCode: StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { status = "ok", list = Endpoints.ListInfo(provider.Current) }))
    .WithSummary("Whether the API is ready (the SDN list is loaded).");

Endpoints.Map(app.MapGroup("/v1"));

app.Run();

/// <summary>Visible to the tests.</summary>
public partial class Program;
