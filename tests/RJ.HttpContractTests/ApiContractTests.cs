using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RJ.Infrastructure.Persistence;

namespace RJ.HttpContractTests;

public sealed class ApiContractTests
{
    [Fact]
    public async Task Health_endpoints_report_live_and_ready()
    {
        await using var fixture = await HttpFixture.CreateAsync();

        var compatibilityLive = await fixture.Client.GetAsync("/health");
        var live = await fixture.Client.GetAsync("/health/live");
        var ready = await fixture.Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, compatibilityLive.StatusCode);
        Assert.Equal("live", (await ReadJsonAsync(compatibilityLive)).RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("live", (await ReadJsonAsync(live)).RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        Assert.Equal("ready", (await ReadJsonAsync(ready)).RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Startup_fails_closed_when_configured_postgres_is_unreachable()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
                builder.UseSetting(
                    "ConnectionStrings:Postgres",
                    "Host=127.0.0.1;Port=1;Database=rj_unreachable;Username=rj;Password=rj;Timeout=1"));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        }));
    }

    [Fact]
    public async Task Ingestion_returns_202_400_and_409_for_defined_contracts()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        var caseId = $"case-{Guid.NewGuid():N}";
        const string documentId = "doc-1";

        var accepted = await fixture.Client.PostAsJsonAsync("/api/legal-documents", new
        {
            caseId,
            documentId,
            sourceName = "source.txt",
            rawContent = "conteudo original"
        });

        var invalid = await fixture.Client.PostAsJsonAsync("/api/legal-documents", new
        {
            caseId = "",
            documentId = "doc-invalid",
            sourceName = "source.txt",
            rawContent = "conteudo"
        });

        var conflict = await fixture.Client.PostAsJsonAsync("/api/legal-documents", new
        {
            caseId,
            documentId,
            sourceName = "source.txt",
            rawContent = "conteudo diferente"
        });

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("invalid_request", (await ReadJsonAsync(invalid)).RootElement.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("evidence_conflict", (await ReadJsonAsync(conflict)).RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Ingestion_returns_stable_400_for_malformed_json()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var content = new StringContent(
            "{\"caseId\":\"case-1\",\"documentId\":",
            Encoding.UTF8,
            "application/json");

        var response = await fixture.Client.PostAsync("/api/legal-documents", content);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_json", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("Request body is not valid for the ingestion contract.", json.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Ingestion_returns_stable_400_for_incompatible_json_type()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var content = new StringContent(
            "{\"caseId\":123,\"documentId\":\"doc-1\",\"sourceName\":\"source.txt\",\"rawContent\":\"conteudo\"}",
            Encoding.UTF8,
            "application/json");

        var response = await fixture.Client.PostAsync("/api/legal-documents", content);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_json", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Ingestion_returns_stable_400_for_missing_required_field()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        using var content = new StringContent(
            "{\"caseId\":\"case-1\",\"documentId\":\"doc-1\",\"sourceName\":\"source.txt\"}",
            Encoding.UTF8,
            "application/json");

        var response = await fixture.Client.PostAsync("/api/legal-documents", content);
        var json = await ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", json.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Documents_are_deterministically_paginated_and_compact()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        var caseId = $"case-{Guid.NewGuid():N}";

        await PostDocumentAsync(fixture.Client, caseId, "doc-b", "b.txt", "conteudo b");
        await PostDocumentAsync(fixture.Client, caseId, "doc-a", "a.txt", "conteudo a");

        var response = await fixture.Client.GetAsync($"/api/cases/{caseId}/documents?page=1&pageSize=1");
        var json = await ReadJsonAsync(response);
        var item = json.RootElement.GetProperty("items")[0];

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, json.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal("doc-a", item.GetProperty("documentId").GetString());
        Assert.False(item.TryGetProperty("rawContent", out _));
        Assert.False(item.TryGetProperty("content", out _));
    }

    [Fact]
    public async Task Search_is_compact_while_evidence_remains_citable()
    {
        await using var fixture = await HttpFixture.CreateAsync();
        var caseId = $"case-{Guid.NewGuid():N}";
        const string raw = "cabecalho\r\nA tutela provisoria foi deferida pelo juizo.\r\nrodape";
        await PostDocumentAsync(fixture.Client, caseId, "doc-1", "decisao.txt", raw);

        var searchResponse = await fixture.Client.GetAsync($"/api/cases/{caseId}/search?q=tutela%20provisoria&limit=10");
        var searchJson = await ReadJsonAsync(searchResponse);
        var searchHit = searchJson.RootElement[0];

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        Assert.Equal("doc-1", searchHit.GetProperty("documentId").GetString());
        Assert.True(searchHit.TryGetProperty("rank", out _));
        Assert.False(searchHit.TryGetProperty("rawContent", out _));
        Assert.False(searchHit.TryGetProperty("content", out _));

        var evidenceResponse = await fixture.Client.GetAsync($"/api/cases/{caseId}/evidence?q=tutela%20provisoria&limit=10");
        var evidenceJson = await ReadJsonAsync(evidenceResponse);
        var evidence = evidenceJson.RootElement[0];
        var position = evidence.GetProperty("position");
        var excerpt = evidence.GetProperty("excerpt").GetString()!;
        var start = position.GetProperty("startOffset").GetInt32();
        var length = position.GetProperty("length").GetInt32();

        Assert.Equal(HttpStatusCode.OK, evidenceResponse.StatusCode);
        Assert.Equal("doc-1", evidence.GetProperty("documentId").GetString());
        Assert.Equal(raw.Substring(start, length), excerpt);
    }

    private static async Task PostDocumentAsync(
        HttpClient client,
        string caseId,
        string documentId,
        string sourceName,
        string rawContent)
    {
        var response = await client.PostAsJsonAsync("/api/legal-documents", new
        {
            caseId,
            documentId,
            sourceName,
            rawContent
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private sealed class HttpFixture : IAsyncDisposable
    {
        private readonly WebApplicationFactory<Program> _factory;
        private readonly NpgsqlDataSource _dataSource;

        private HttpFixture(
            WebApplicationFactory<Program> factory,
            NpgsqlDataSource dataSource,
            HttpClient client)
        {
            _factory = factory;
            _dataSource = dataSource;
            Client = client;
        }

        public HttpClient Client { get; }

        public static async Task<HttpFixture> CreateAsync()
        {
            var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Assert.Skip("RJ_POSTGRES_CONNECTION is required for HTTP contract tests.");
            }

            var dataSource = NpgsqlDataSource.Create(connectionString);
            await PostgresSchema.MigrateAsync(dataSource);

            var factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                    builder.ConfigureServices(services =>
                        services.AddSingleton<IStartupFilter, TestAuthenticatedPrincipalStartupFilter>()));
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false
            });

            return new HttpFixture(factory, dataSource, client);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await _factory.DisposeAsync();
            await _dataSource.DisposeAsync();
        }
    }
}
