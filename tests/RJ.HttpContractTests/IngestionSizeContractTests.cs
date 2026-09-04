using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using RJ.Api;
using RJ.Infrastructure.Persistence;

namespace RJ.HttpContractTests;

public sealed class IngestionSizeContractTests
{
    [Fact]
    public async Task Oversized_raw_content_returns_413_with_stable_error_code()
    {
        var connectionString = Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Assert.Skip("RJ_POSTGRES_CONNECTION is required for HTTP contract tests.");
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await PostgresSchema.MigrateAsync(dataSource);
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsJsonAsync("/api/legal-documents", new
        {
            caseId = $"case-{Guid.NewGuid():N}",
            documentId = "doc-large",
            sourceName = "large.txt",
            rawContent = new string('x', IngestionLimits.MaxRawContentBytes + 1)
        });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        Assert.Equal("payload_too_large", json.RootElement.GetProperty("code").GetString());
    }
}
