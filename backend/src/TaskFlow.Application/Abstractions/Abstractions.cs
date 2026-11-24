using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Users;

namespace TaskFlow.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<Project> Projects { get; }

    DbSet<ProjectMember> ProjectMembers { get; }

    DbSet<TaskItem> Tasks { get; }

    DbSet<TaskComment> TaskComments { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Pushes board changes to everyone viewing the project, in real time. Called only after the
/// change has been committed. Implemented with SignalR in the API layer.
/// </summary>
public interface IBoardNotifier
{
    Task TaskUpsertedAsync(Guid projectId, TaskCardDto task, CancellationToken ct);

    Task TasksMovedAsync(Guid projectId, IReadOnlyList<TaskPositionDto> positions, CancellationToken ct);

    Task TaskDeletedAsync(Guid projectId, Guid taskId, CancellationToken ct);

    Task CommentAddedAsync(Guid projectId, Guid taskId, CommentDto comment, CancellationToken ct);

    /// <summary>Columns, members or project details changed; clients reload the board.</summary>
    Task BoardChangedAsync(Guid projectId, CancellationToken ct);
}

public interface ICurrentUser
{
    Guid Id { get; }

    bool IsAdmin { get; }
}

public interface IPasswordHasher
{
    string Hash(User user, string password);

    PasswordCheck Verify(User user, string password);
}

public enum PasswordCheck
{
    Failed,
    Success,
    SuccessRehashNeeded,
}

public interface ITokenService
{
    AccessToken CreateAccessToken(User user);

    IssuedRefreshToken CreateRefreshToken();

    string HashRefreshToken(string refreshToken);
}

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public sealed record IssuedRefreshToken(string Token, string Hash, DateTimeOffset ExpiresAt);
