using Microsoft.AspNetCore.Routing;
using Npgsql;
using RJ.Api;
using RJ.Application.Generation;
using RJ.Application.Ingestion;
using RJ.Application.Operations;
using RJ.Application.Retrieval;
using RJ.Application.Sources;
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
builder.Services.AddSingleton<IProcessSummaryClock, SystemProcessSummaryClock>();
builder.Services.AddSingleton<IProcessSummaryTelemetry, LoggingProcessSummaryTelemetry>();
builder.Services.AddSingleton<IProcessSummaryAuditSink, LoggingProcessSummaryAuditSink>();
builder.Services.AddSingleton<IProcessSummaryCallerContextResolver, ClaimsProcessSummaryCallerContextResolver>();
builder.Services.AddSingleton<IProcessSummaryJobAccessStore, InMemoryProcessSummaryJobAccessStore>();
builder.Services.AddSingleton<IProcessAttachmentContentStore, EmptyProcessAttachmentContentStore>();
builder.Services.AddSingleton<IngestLegalDocumentHandler>();
builder.Services.AddSingleton<LegalDocumentQueryService>();
builder.Services.AddSingleton<GenerationContextBuilder>();
builder.Services.AddSingleton<GenerationContextService>();
builder.Services.AddSingleton<IProcessSourceAdapter, JuditProcessSourceAdapter>();
builder.Services.AddSingleton<ProcessSourceCanonicalizationService>();
builder.Services.AddSingleton<ProcessGenerationContextComposer>();
builder.Services.AddSingleton<IGenerationModel, DeterministicProcessSummaryModel>();
builder.Services.AddSingleton<GenerationService>();
builder.Services.AddSingleton<ProcessSummaryJobService>();

var app = builder.Build();

app.UseMiddleware<JsonInputExceptionMiddleware>();
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method)
        && context.Request.Path.Equals("/api/legal-documents", StringComparison.Ordinal)
        && context.Request.ContentLength is long contentLength
        && contentLength > IngestionLimits.MaxRequestBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync("""{"code":"payload_too_large","error":"Request body exceeds the ingestion size limit."}""");
        return;
    }

    await next();
});

app.MapGet("/health", HealthEndpoint.Live);
app.MapGet("/health/live", HealthEndpoint.Live);
app.MapGet("/health/ready", HealthEndpoint.ReadyAsync);

app.MapPost("/api/legal-documents", IngestionEndpoint.HandleAsync);

app.MapGet("/api/cases/{caseId}/documents", ReadEndpoint.ListDocumentsAsync);
app.MapGet("/api/cases/{caseId}/documents/{documentId}", ReadEndpoint.GetDocumentAsync);
app.MapGet("/api/cases/{caseId}/search", ReadEndpoint.SearchAsync);
app.MapGet("/api/cases/{caseId}/evidence", ReadEndpoint.RetrieveEvidenceAsync);
app.MapGet("/api/cases/{caseId}/generation-context", GenerationContextEndpoint.HandleAsync);

app.MapPost("/api/process-summaries/jobs", ProcessSummaryEndpoint.SubmitAuthenticatedAsync);
app.MapGet("/api/process-summaries/jobs/{jobId}", ProcessSummaryEndpoint.GetJobAuthenticated);
app.MapGet("/api/process-summaries/jobs/{jobId}/validated-summary", ProcessSummaryEndpoint.GetValidatedSummaryAuthenticated);
app.MapPost("/api/process-summaries/jobs/{jobId}/refresh-plan", ProcessSummaryEndpoint.GetRefreshPlanAuthenticated);

app.Run();

public partial class Program;
