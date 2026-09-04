using Npgsql;
using RJ.Application.Generation;
using RJ.Application.Ingestion;
using RJ.Application.Retrieval;
using RJ.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("PostgreSQL connection string is required via ConnectionStrings:Postgres or RJ_POSTGRES_CONNECTION.");

var dataSource = NpgsqlDataSource.Create(connectionString);
await PostgresSchema.InitializeAsync(dataSource);

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<ILegalDocumentWriter, PostgresLegalDocumentWriter>();
builder.Services.AddSingleton<ILegalDocumentReader, PostgresLegalDocumentReader>();
builder.Services.AddSingleton<ILegalDocumentSearch, PostgresLegalDocumentSearch>();
builder.Services.AddSingleton<IngestLegalDocumentHandler>();
builder.Services.AddSingleton<LegalDocumentQueryService>();
builder.Services.AddSingleton<GenerationContextBuilder>();
builder.Services.AddSingleton<GenerationContextService>();

var app = builder.Build();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/legal-documents", async (
    IngestLegalDocumentRequest request,
    IngestLegalDocumentHandler handler,
    CancellationToken cancellationToken) =>
{
    var command = new IngestLegalDocumentCommand(
        request.CaseId,
        request.DocumentId,
        request.SourceName,
        request.RawContent);

    await handler.HandleAsync(command, cancellationToken);
    return Results.Accepted();
});

app.MapGet("/api/cases/{caseId}/documents", async (
    string caseId,
    LegalDocumentQueryService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var documents = await service.ListAsync(caseId, cancellationToken);
        return Results.Ok(documents);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

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

app.MapGet("/api/cases/{caseId}/search", async (
    string caseId,
    string? q,
    int? limit,
    LegalDocumentQueryService service,
    CancellationToken cancellationToken) =>
{
    try
    {
        var hits = await service.SearchAsync(caseId, q ?? string.Empty, limit ?? 20, cancellationToken);
        return Results.Ok(hits);
    }
    catch (ArgumentException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

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

public sealed record IngestLegalDocumentRequest(
    string CaseId,
    string DocumentId,
    string SourceName,
    string RawContent);
