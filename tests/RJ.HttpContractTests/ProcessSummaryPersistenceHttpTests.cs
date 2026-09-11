using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RJ.Infrastructure.Persistence;

namespace RJ.HttpContractTests;

public sealed class ProcessSummaryPersistenceHttpTests
{
    private const string CaseId = "response_60031603620268160021_1";

    [Fact]
    public async Task Persisted_process_summary_survives_application_restart()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for HTTP persistence tests.");
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await PostgresSchema.MigrateAsync(dataSource);
        var fixturePath = Path.Combine(
            FindRepoRoot(),
            "tests",
            "fixtures",
            "rj",
            "response_60031603620268160021_1.json");
        var rawContent = await File.ReadAllTextAsync(fixturePath);
        var idempotencyKey = $"restart-{Guid.NewGuid():N}";
        string jobId;
        string snapshotSha256;

        await using (var firstFactory = CreateFactory())
        using (var firstClient = firstFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        {
            using var submitRequest = AuthorizedRequest(
                HttpMethod.Post,
                "/api/process-summaries/jobs",
                JsonContent.Create(new
                {
                    idempotencyKey,
                    sourceSystem = "Judit",
                    sourceName = "Judit",
                    sourceReference = "tests/fixtures/rj/response_60031603620268160021_1.json",
                    rawContent,
                    observedAt = "2026-09-02T18:51:04.800Z",
                    instruction = "Resuma o processo."
                }));
            var submit = await firstClient.SendAsync(submitRequest);

            Assert.Equal(HttpStatusCode.Accepted, submit.StatusCode);
            using var json = await ReadJsonAsync(submit);
            jobId = json.RootElement.GetProperty("jobId").GetString()!;
            snapshotSha256 = json.RootElement.GetProperty("snapshotSha256").GetString()!;
        }

        await using (var secondFactory = CreateFactory())
        using (var secondClient = secondFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        {
            using var getRequest = AuthorizedRequest(HttpMethod.Get, $"/api/process-summaries/jobs/{jobId}");
            var recovered = await secondClient.SendAsync(getRequest);

            Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
            using var json = await ReadJsonAsync(recovered);
            Assert.Equal(jobId, json.RootElement.GetProperty("jobId").GetString());
            Assert.Equal(snapshotSha256, json.RootElement.GetProperty("snapshotSha256").GetString());
            Assert.Equal("Validated", json.RootElement.GetProperty("status").GetString());
            Assert.True(json.RootElement.GetProperty("isValid").GetBoolean());
        }
    }

    [Fact]
    public async Task Persisted_idempotency_replay_survives_application_restart()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for HTTP persistence tests.");
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await PostgresSchema.MigrateAsync(dataSource);
        var rawContent = await File.ReadAllTextAsync(Path.Combine(
            FindRepoRoot(),
            "tests",
            "fixtures",
            "rj",
            "response_60031603620268160021_1.json"));
        var idempotencyKey = $"replay-{Guid.NewGuid():N}";
        string firstJobId;

        await using (var firstFactory = CreateFactory())
        using (var firstClient = firstFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        {
            var first = await SubmitAsync(firstClient, idempotencyKey, rawContent);
            Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
            using var json = await ReadJsonAsync(first);
            firstJobId = json.RootElement.GetProperty("jobId").GetString()!;
        }

        await using (var secondFactory = CreateFactory())
        using (var secondClient = secondFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false }))
        {
            var replay = await SubmitAsync(secondClient, idempotencyKey, rawContent);
            Assert.Equal(HttpStatusCode.Accepted, replay.StatusCode);
            using var json = await ReadJsonAsync(replay);
            Assert.Equal(firstJobId, json.RootElement.GetProperty("jobId").GetString());
        }
    }

    private static async Task<HttpResponseMessage> SubmitAsync(
        HttpClient client,
        string idempotencyKey,
        string rawContent)
    {
        using var request = AuthorizedRequest(
            HttpMethod.Post,
            "/api/process-summaries/jobs",
            JsonContent.Create(new
            {
                idempotencyKey,
                sourceSystem = "Judit",
                sourceName = "Judit",
                sourceReference = "tests/fixtures/rj/response_60031603620268160021_1.json",
                rawContent,
                observedAt = "2026-09-02T18:51:04.800Z",
                instruction = "Resuma o processo."
            }));
        return await client.SendAsync(request);
    }

    private static HttpRequestMessage AuthorizedRequest(
        HttpMethod method,
        string requestUri,
        HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, requestUri)
        {
            Content = content
        };
        request.Headers.Add(TestAuthenticatedPrincipalStartupFilter.CaseHeader, CaseId);
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter, TestAuthenticatedPrincipalStartupFilter>()));

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());

    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(current, "RJ.slnx")))
        {
            var parent = Directory.GetParent(current)
                ?? throw new InvalidOperationException("Repository root not found.");
            current = parent.FullName;
        }

        return current;
    }
}
