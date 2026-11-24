namespace TaskFlow.Domain.Common;

/// <summary>Base type for entities. Identifiers are time-ordered UUIDv7 values generated in the domain.</summary>
public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();
}

public interface IAuditable
{
    DateTimeOffset CreatedAt { get; set; }

    DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>Raised when an operation would violate a business rule (WIP limit, invalid key, ...).</summary>
public sealed class DomainException(string message) : Exception(message);

/// <summary>Raised when a project member tries an action reserved for the project owner.</summary>
public sealed class NotPermittedException(string message) : Exception(message);
