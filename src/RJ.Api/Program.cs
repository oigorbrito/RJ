using Microsoft.AspNetCore.Mvc;
using Npgsql;
using RJ.Api;
using RJ.Application.Generation;
using RJ.Application.Ingestion;
using RJ.Application.Operations;
using RJ.Application.Retrieval;
using RJ.Infrastructure.Operations;
using RJ.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("PostgreSQL connection string is required via ConnectionStrings:Postgres or RJ_POSTGRES_CONNECTION.");

var dataSource = NpgsqlDataSource.Create(connectionString);
await PostgresSchema.EnsureCurrentAsync(dataSource);

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<ILegalDocumentWriter, PostgresLegalDocumentWriter>();
builder.Services.AddSingleton<ILegalDocumentReader, PostgresLegalDocumentReader>();
builder.Services.AddSingleton<ILegalDocumentSearch, PostgresLegalDocumentSearch>();
builder.Services.AddSingleton<IReadinessProbe, PostgresReadinessProbe>();
builder.Services.AddSingleton<IngestLegalDocumentHandler>();
builder.Services.AddSingleton<LegalDocumentQueryService>();
builder.Services.AddSingleton<GenerationContextBuilder>();
builder.Services.AddSingleton<GenerationContextService>();

var app = builder.Build();

app.MapGet("/health", HealthEndpoint.Live);
app.MapGet("/health/live", HealthEndpoint.Live);
app.MapGet("/health/ready", HealthEndpoint.ReadyAsync);

app.MapPost("/api/legal-documents", IngestionEndpoint.HandleAsync)
    .WithMetadata(new RequestSizeLimitAttribute(IngestionLimits.MaxRequestBodyBytes));

app.MapGet("/api/cases/{caseId}/documents", ReadEndpoint.ListDocumentsAsync);

app.MapGet("/api/cases/{caseId}/documents/{documentId}", async (
    string caseId,
    string documentId,
    LegalDocumentQueryService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var document = await service.GetAsync(caseId, documentId, cancellationToken);
        return document is null ? Results.NotFound() : Results.Ok(document);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/cases/{caseId}/search", ReadEndpoint.SearchAsync);

app.MapGet("/api/cases/{caseId}/evidence", async (
    string caseId,
    string? q,
    int? limit,
    LegalDocumentQueryService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var evidence = await service.RetrieveEvidenceAsync(caseId, q ?? string.Empty, limit ?? 20, cancellationToken);
        return Results.Ok(evidence);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/cases/{caseId}/generation-context", async (
    string caseId,
    string? q,
    int? limit,
    int? budget,
    GenerationContextService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var context = await service.BuildAsync(
            caseId,
            q ?? string.Empty,
            limit ?? 20,
            budget ?? 12000,
            cancellationToken);
        return Results.Ok(context);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (InvalidOperationException exception)
    {
        return Results.UnprocessableEntity(new { error = exception.Message });
    }
});

app.Run();

public partial class Program;
