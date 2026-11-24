using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Projects;
using TaskFlow.Infrastructure.Security;

namespace TaskFlow.Api.Realtime;

/// <summary>Messages the server pushes to browsers that have a board open.</summary>
public interface IBoardClient
{
    Task TaskUpserted(TaskCardDto task);

    Task TasksMoved(IReadOnlyList<TaskPositionDto> positions);

    Task TaskDeleted(Guid taskId);

    Task CommentAdded(Guid taskId, CommentDto comment);

    Task BoardChanged();
}

/// <summary>
/// Real-time channel for boards. A connection joins one SignalR group per open project, and only
/// after the server has verified that the user is a member of that project.
/// </summary>
[Authorize]
public sealed class BoardHub(IAppDbContext db) : Hub<IBoardClient>
{
    public const string Path = "/hubs/board";

    public static string Group(Guid projectId) => $"project:{projectId:N}";

    public async Task JoinProject(Guid projectId)
    {
        var userId = Guid.Parse(Context.User!.FindFirstValue(TaskFlowClaims.Subject)!);
        var isMember = await db.ProjectMembers.AnyAsync(m => m.ProjectId == projectId && m.UserId == userId);
        if (!isMember)
        {
            throw new HubException("Project not found.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, Group(projectId));
    }

    public Task LeaveProject(Guid projectId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(projectId));
}

/// <summary>Broadcasts committed board changes to the project's SignalR group.</summary>
internal sealed class SignalRBoardNotifier(IHubContext<BoardHub, IBoardClient> hub) : IBoardNotifier
{
    public Task TaskUpsertedAsync(Guid projectId, TaskCardDto task, CancellationToken ct) =>
        Board(projectId).TaskUpserted(task);

    public Task TasksMovedAsync(Guid projectId, IReadOnlyList<TaskPositionDto> positions, CancellationToken ct) =>
        Board(projectId).TasksMoved(positions);

    public Task TaskDeletedAsync(Guid projectId, Guid taskId, CancellationToken ct) =>
        Board(projectId).TaskDeleted(taskId);

    public Task CommentAddedAsync(Guid projectId, Guid taskId, CommentDto comment, CancellationToken ct) =>
        Board(projectId).CommentAdded(taskId, comment);

    public Task BoardChangedAsync(Guid projectId, CancellationToken ct) =>
        Board(projectId).BoardChanged();

    private IBoardClient Board(Guid projectId) => hub.Clients.Group(BoardHub.Group(projectId));
}
