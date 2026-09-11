using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using RJ.Api.Security;

namespace RJ.ApiTests;

public sealed class HttpAuthenticationBoundaryMiddlewareTests
{
    [Fact]
    public async Task Api_request_without_authenticated_principal_returns_401_without_invoking_next()
    {
        var invoked = false;
        var middleware = new HttpAuthenticationBoundaryMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/cases/case-1/documents";

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.False(invoked);
    }

    [Fact]
    public async Task Authenticated_api_request_reaches_next()
    {
        var invoked = false;
        var middleware = new HttpAuthenticationBoundaryMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/cases/case-1/documents";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([], "test"));

        await middleware.InvokeAsync(context);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Health_request_without_authenticated_principal_reaches_next()
    {
        var invoked = false;
        var middleware = new HttpAuthenticationBoundaryMiddleware(_ =>
        {
            invoked = true;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Path = "/health/live";

        await middleware.InvokeAsync(context);

        Assert.True(invoked);
    }

    [Fact]
    public async Task Every_protected_api_route_rejects_anonymous_requests_before_handler()
    {
        var routes = new[]
        {
            "/api/legal-documents",
            "/api/cases/case-1/documents",
            "/api/cases/case-1/documents/doc-1",
            "/api/cases/case-1/search",
            "/api/cases/case-1/evidence",
            "/api/cases/case-1/generation-context",
            "/api/process-summaries/jobs",
            "/api/process-summaries/jobs/job-1",
            "/api/process-summaries/jobs/job-1/validated-summary",
            "/api/process-summaries/jobs/job-1/refresh-plan"
        };

        foreach (var route in routes)
        {
            var invoked = false;
            var middleware = new HttpAuthenticationBoundaryMiddleware(_ =>
            {
                invoked = true;
                return Task.CompletedTask;
            });
            var context = new DefaultHttpContext();
            context.Request.Path = route;

            await middleware.InvokeAsync(context);

            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
            Assert.False(invoked, route);
        }
    }
}
