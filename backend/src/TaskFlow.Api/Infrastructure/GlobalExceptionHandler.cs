using TaskFlow.Application.Common;
using TaskFlow.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace TaskFlow.Api.Infrastructure;

/// <summary>Translates exceptions into RFC 9457 problem details with an appropriate status code.</summary>
internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => new HttpValidationProblemDetails(GroupErrors(validation))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Validation failed",
                Detail = "One or more fields are invalid.",
            },
            DomainException => Problem(StatusCodes.Status422UnprocessableEntity, "Business rule violated", exception.Message),
            NotPermittedException => Problem(StatusCodes.Status403Forbidden, "Forbidden", exception.Message),
            NotFoundException => Problem(StatusCodes.Status404NotFound, "Not found", exception.Message),
            ConflictException => Problem(StatusCodes.Status409Conflict, "Conflict", exception.Message),
            UnauthorizedException => Problem(StatusCodes.Status401Unauthorized, "Unauthorized", exception.Message),
            DbUpdateConcurrencyException => Problem(
                StatusCodes.Status409Conflict,
                "Concurrent update",
                "The record was changed by another request. Reload it and try again."),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } => Problem(
                StatusCodes.Status409Conflict,
                "Conflict",
                "A record with the same values already exists."),
            BadHttpRequestException badRequest => Problem(badRequest.StatusCode, "Bad request", badRequest.Message),
            _ => Problem(StatusCodes.Status500InternalServerError, "Server error", "An unexpected error occurred."),
        };

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    private static ProblemDetails Problem(int status, string title, string detail) =>
        new() { Status = status, Title = title, Detail = detail };

    private static Dictionary<string, string[]> GroupErrors(ValidationException exception) =>
        exception.Errors
            .GroupBy(e => ToCamelCase(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

    private static string ToCamelCase(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
