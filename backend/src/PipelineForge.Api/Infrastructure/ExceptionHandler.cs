using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PipelineForge.Infrastructure.Net;
using PipelineForge.Infrastructure.Scm;

namespace PipelineForge.Api.Infrastructure;

/// <summary>Maps known failures to ProblemDetails without leaking internals or stack traces.</summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger, IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            Services.ForbiddenException e => (StatusCodes.Status403Forbidden, e.Message),
            Services.NotFoundException e => (StatusCodes.Status404NotFound, e.Message),
            OutboundBlockedException e => (StatusCodes.Status400BadRequest, e.Message),
            ScmException e => (StatusCodes.Status502BadGateway, e.Message),
            HttpRequestException => (StatusCodes.Status502BadGateway, "Could not reach the external service."),
            TaskCanceledException when !ct.IsCancellationRequested => (StatusCodes.Status504GatewayTimeout, "The external service timed out."),
            JsonException => (StatusCodes.Status400BadRequest, "Invalid JSON."),
            ArgumentException e => (StatusCodes.Status400BadRequest, e.Message),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Not authenticated."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error."),
        };
        if (status >= 500) logger.LogError(exception, "Request failed: {Title}", title);
        context.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new ProblemDetails { Status = status, Title = title },
            Exception = exception,
        });
    }
}
