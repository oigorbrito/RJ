using Npgsql;
using RJ.Application.Ingestion;
using RJ.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? Environment.GetEnvironmentVariable("RJ_POSTGRES_CONNECTION")
    ?? throw new InvalidOperationException("PostgreSQL connection string is required via ConnectionStrings:Postgres or RJ_POSTGRES_CONNECTION.");

var dataSource = NpgsqlDataSource.Create(connectionString);
await PostgresSchema.InitializeAsync(dataSource);

builder.Services.AddSingleton(dataSource);
builder.Services.AddSingleton<ILegalDocumentWriter, PostgresLegalDocumentWriter>();
builder.Services.AddSingleton<IngestLegalDocumentHandler>();

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

app.Run();

public sealed record IngestLegalDocumentRequest(
    string CaseId,
    string DocumentId,
    string SourceName,
    string RawContent);
