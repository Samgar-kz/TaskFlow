namespace TaskFlow.Application.Common;

/// <summary>The requested resource does not exist or does not belong to the caller.</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} '{key}' was not found.");

/// <summary>The request conflicts with the current state (duplicate, still in use, ...).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>Credentials or tokens are missing, invalid or expired.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public static class Paging
{
    public const int DefaultPageSize = 25;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int? page, int? pageSize) =>
        (Math.Max(page ?? 1, 1), Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize));
}
