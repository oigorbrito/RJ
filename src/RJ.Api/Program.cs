using System.Security.Claims;
using Microsoft.AspNetCore.Routing;
using Npgsql;
using RJ.Api;
using RJ.Api.Operations;
using RJ.Api.Security;
using RJ.Application.Generation;
using RJ.Application.Ingestion;
using RJ.Application.Operations;
using RJ.Application.Retrieval;
using RJ.Application.Sources;
using RJ.Infrastructure.Operations;
using RJ.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi("v1");

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
builder.Services.AddSingleton<IProcessSummaryTelemetry, ActivityMeterProcessSummaryTelemetry>();
builder.Services.AddSingleton<IProcessSummaryAuditSink, NoopProcessSummaryAuditSink>();
builder.Services.AddSingleton<IProcessSummaryStructuredLogger, AspNetProcessSummaryStructuredLogger>();
builder.Services.AddSingleton<IProcessCallerIdentity, ClaimsPrincipalProcessCallerIdentity>();
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
builder.Services.AddSingleton<IProcessSummaryJobStore, InMemoryProcessSummaryJobStore>();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<ApiExceptionBoundaryMiddleware>();
app.UseMiddleware<HttpAuthenticationBoundaryMiddleware>();

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

app.MapGet("/health", HealthEndpoint.Live).WithName("HealthLiveCompatibility");
app.MapGet("/health/live", HealthEndpoint.Live).WithName("HealthLive");
app.MapGet("/health/ready", HealthEndpoint.ReadyAsync).WithName("HealthReady");

app.MapPost("/api/legal-documents", async (
    HttpContext context,
    IngestLegalDocumentRequest request,
    IngestLegalDocumentHandler handler,
    IProcessCallerIdentity identity,
    CancellationToken cancellationToken) => await RequireCaseAsync(
        context.User,
        request.CaseId,
        identity,
        () => IngestionEndpoint.HandleAsync(request, handler, cancellationToken)))
    .WithName("IngestLegalDocument");

app.MapGet("/api/cases/{caseId}/documents", async (
    HttpContext context,
    string caseId,
    int? page,
    int? pageSize,
    LegalDocumentQueryService service,
    IProcessCallerIdentity identity,
    CancellationToken cancellationToken) => await RequireCaseAsync(
        context.User,
        caseId,
        identity,
        () => ReadEndpoint.ListDocumentsAsync(caseId, page, pageSize, service, cancellationToken)))
    .WithName("ListCaseDocuments");

app.MapGet("/api/cases/{caseId}/documents/{documentId}", async (
    HttpContext context,
    string caseId,
    string documentId,
    LegalDocumentQueryService service,
    IProcessCallerIdentity identity,
    CancellationToken cancellationToken) => await RequireCaseAsync(
        context.User,
        caseId,
        identity,
        () => ReadEndpoint.GetDocumentAsync(caseId, documentId, service, cancellationToken)))
    .WithName("GetCaseDocument");

app.MapGet("/api/cases/{caseId}/search", async (
    HttpContext context,
    string caseId,
    string? q,
    int? limit,
    LegalDocumentQueryService service,
    IProcessCallerIdentity identity,
    CancellationToken cancellationToken) => await RequireCaseAsync(
        context.User,
        caseId,
        identity,
        () => ReadEndpoint.SearchAsync(caseId, q, limit, service, cancellationToken)))
    .WithName("SearchCaseDocuments");

app.MapGet("/api/cases/{caseId}/evidence", async (
    HttpContext context,
    string caseId,
    string? q,
    int? limit,
    LegalDocumentQueryService service,
    IProcessCallerIdentity identity,
    CancellationToken cancellationToken) => await RequireCaseAsync(
        context.User,
        caseId,
        identity,
        () => ReadEndpoint.RetrieveEvidenceAsync(caseId, q, limit, service, cancellationToken)))
    .WithName("RetrieveCaseEvidence");

app.MapGet("/api/cases/{caseId}/generation-context", async (
    HttpContext context,
    string caseId,
    string? q,
    int? limit,
    int? budget,
    GenerationContextService service,
    IProcessCallerIdentity identity,
    CancellationToken cancellationToken) => await RequireCaseAsync(
        context.User,
        caseId,
        identity,
        () => GenerationContextEndpoint.HandleAsync(caseId, q, limit, budget, service, cancellationToken)))
    .WithName("BuildCaseGenerationContext");

app.MapPost("/api/process-summaries/jobs", async (
    HttpContext context,
    ProcessSummaryRequest request,
    ProcessSummaryJobService service,
    IProcessCallerIdentity identity,
    CancellationToken cancellationToken) =>
{
    try
    {
        var caller = identity.Require(context.User);
        return await ProcessSummaryEndpoint.SubmitAuthenticatedAsync(
            request,
            caller,
            service,
            cancellationToken);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
})
    .WithName("SubmitProcessSummaryJob");
app.MapGet("/api/process-summaries/jobs/{jobId}", (
    HttpContext context,
    string jobId,
    ProcessSummaryJobService service,
    IProcessCallerIdentity identity) =>
{
    try
    {
        return ProcessSummaryEndpoint.GetJobAuthenticated(
            jobId,
            identity.Require(context.User),
            service);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
})
    .WithName("GetProcessSummaryJob");

app.MapGet("/api/process-summaries/jobs/{jobId}/validated-summary", (
    HttpContext context,
    string jobId,
    ProcessSummaryJobService service,
    IProcessCallerIdentity identity) =>
{
    try
    {
        return ProcessSummaryEndpoint.GetValidatedSummaryAuthenticated(
            jobId,
            identity.Require(context.User),
            service);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
})
    .WithName("GetValidatedProcessSummary");

app.MapPost("/api/process-summaries/jobs/{jobId}/refresh-plan", (
    HttpContext context,
    string jobId,
    ProcessSummaryRefreshPlanRequest request,
    ProcessSummaryJobService service,
    IProcessCallerIdentity identity) =>
{
    try
    {
        return ProcessSummaryEndpoint.GetRefreshPlanAuthenticated(
            jobId,
            request,
            identity.Require(context.User),
            service);
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
})
    .WithName("GetProcessSummaryRefreshPlan");

app.MapOpenApi("/openapi/{documentName}.json");

static async Task<IResult> RequireCaseAsync(
    ClaimsPrincipal principal,
    string caseId,
    IProcessCallerIdentity identity,
    Func<Task<IResult>> next)
{
    try
    {
        identity.RequireCase(principal, caseId);
        return await next();
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
    catch (ForbiddenAccessException)
    {
        return Results.Forbid();
    }
    catch (ArgumentException)
    {
        return Results.BadRequest(new ApiError("invalid_request", "Invalid case identifier."));
    }
}

app.Run();

public partial class Program;
