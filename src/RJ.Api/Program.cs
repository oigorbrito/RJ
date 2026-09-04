using Microsoft.AspNetCore.Routing;
using Npgsql;
using RJ.Api;
using RJ.Application.Generation;
using RJ.Application.Ingestion;
using RJ.Application.Operations;
using RJ.Application.Retrieval;
using RJ.Infrastructure.Operations;
using RJ.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<RouteHandlerOptions>(options =>
{
    options.ThrowOnBadRequest = true;
});

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

app.UseMiddleware<JsonInputExceptionMiddleware>();

app.MapGet("/health", HealthEndpoint.Live);
app.MapGet("/health/live", HealthEndpoint.Live);
app.MapGet("/health/ready", HealthEndpoint.ReadyAsync);

app.MapPost("/api/legal-documents", IngestionEndpoint.HandleAsync)
    .WithMetadata(new Microsoft.AspNetCore.Http.Metadata.RequestSizeLimitAttribute(IngestionLimits.MaxRequestBodyBytes));

app.MapGet("/api/cases/{caseId}/documents", ReadEndpoint.ListDocumentsAsync);
app.MapGet("/api/cases/{caseId}/documents/{documentId}", ReadEndpoint.GetDocumentAsync);
app.MapGet("/api/cases/{caseId}/search", ReadEndpoint.SearchAsync);
app.MapGet("/api/cases/{caseId}/evidence", ReadEndpoint.RetrieveEvidenceAsync);
app.MapGet("/api/cases/{caseId}/generation-context", GenerationContextEndpoint.HandleAsync);

app.Run();

public partial class Program;
