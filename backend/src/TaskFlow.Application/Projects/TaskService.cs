using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Projects;

public sealed class TaskService(
    IAppDbContext db,
    ICurrentUser currentUser,
    ProjectService projects,
    IBoardNotifier notifier,
    TimeProvider clock,
    IValidator<SaveTaskRequest> saveValidator,
    IValidator<MoveTaskRequest> moveValidator,
    IValidator<CommentRequest> commentValidator)
{
    public async Task<TaskCardDto> CreateAsync(Guid projectId, SaveTaskRequest request, CancellationToken ct)
    {
        await saveValidator.ValidateAndThrowAsync(request, ct);
        var project = await projects.LoadForMemberAsync(projectId, ct);
        EnsureAssigneeIsMember(project, request.AssigneeId);

        var column = request.ColumnId is { } columnId
            ? project.GetColumn(columnId)
            : project.Columns.OrderBy(c => c.Position).First();
        var columnTasks = await db.Tasks.Where(t => t.ColumnId == column.Id).ToListAsync(ct);

        var task = new TaskItem(
            project, project.AllocateTaskNumber(), request.Title, request.Description, request.Priority, request.AssigneeId, request.DueDate, currentUser.Id);
        BoardOrdering.Add(project, task, column, columnTasks, clock.GetUtcNow());

        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);

        var card = ProjectService.ToCard(task, project.Key, 0);
        await notifier.TaskUpsertedAsync(project.Id, card, ct);
        return card;
    }

    public async Task<TaskDetailDto> GetAsync(Guid taskId, CancellationToken ct)
    {
        var (task, project) = await LoadAsync(taskId, ct);
        var comments = await LoadCommentsAsync(taskId, ct);
        return new TaskDetailDto(ProjectService.ToCard(task, project.Key, comments.Count), task.Description, task.CreatedById, comments);
    }

    public async Task<TaskCardDto> UpdateAsync(Guid taskId, SaveTaskRequest request, CancellationToken ct)
    {
        await saveValidator.ValidateAndThrowAsync(request, ct);
        var (task, project) = await LoadAsync(taskId, ct);
        EnsureAssigneeIsMember(project, request.AssigneeId);

        task.Update(request.Title, request.Description, request.Priority, request.AssigneeId, request.DueDate);
        await db.SaveChangesAsync(ct);

        var card = ProjectService.ToCard(task, project.Key, await db.TaskComments.CountAsync(c => c.TaskId == taskId, ct));
        await notifier.TaskUpsertedAsync(project.Id, card, ct);
        return card;
    }

    /// <summary>Drags a task to a position in a column and broadcasts every position that changed.</summary>
    public async Task<IReadOnlyList<TaskPositionDto>> MoveAsync(Guid taskId, MoveTaskRequest request, CancellationToken ct)
    {
        await moveValidator.ValidateAndThrowAsync(request, ct);
        var (task, project) = await LoadAsync(taskId, ct);
        var target = project.GetColumn(request.ColumnId);

        var affected = await db.Tasks
            .Where(t => t.ColumnId == task.ColumnId || t.ColumnId == target.Id)
            .ToListAsync(ct);

        var changed = BoardOrdering.Move(
            project,
            task,
            target,
            request.Index,
            affected.Where(t => t.ColumnId == task.ColumnId).ToList(),
            affected.Where(t => t.ColumnId == target.Id).ToList(),
            clock.GetUtcNow());

        await db.SaveChangesAsync(ct);

        var positions = changed.Select(t => new TaskPositionDto(t.Id, t.ColumnId, t.Position, t.CompletedAt)).ToList();
        if (positions.Count > 0)
        {
            await notifier.TasksMovedAsync(project.Id, positions, ct);
        }

        return positions;
    }

    public async Task DeleteAsync(Guid taskId, CancellationToken ct)
    {
        var (task, project) = await LoadAsync(taskId, ct);
        var columnTasks = await db.Tasks.Where(t => t.ColumnId == task.ColumnId).ToListAsync(ct);

        db.Tasks.Remove(task);
        BoardOrdering.Remove(task, columnTasks);
        await db.SaveChangesAsync(ct);
        await notifier.TaskDeletedAsync(project.Id, taskId, ct);
    }

    public async Task<CommentDto> AddCommentAsync(Guid taskId, CommentRequest request, CancellationToken ct)
    {
        await commentValidator.ValidateAndThrowAsync(request, ct);
        var (_, project) = await LoadAsync(taskId, ct);

        var comment = new TaskComment(taskId, currentUser.Id, request.Body, clock.GetUtcNow());
        db.TaskComments.Add(comment);
        await db.SaveChangesAsync(ct);

        var author = await db.Users.Where(u => u.Id == currentUser.Id).Select(u => u.FullName).SingleAsync(ct);
        var dto = new CommentDto(comment.Id, comment.AuthorId, author, comment.Body, comment.CreatedAt);
        await notifier.CommentAddedAsync(project.Id, taskId, dto, ct);
        return dto;
    }

    /// <summary>Open tasks assigned to the caller across all their projects, most urgent first.</summary>
    public async Task<IReadOnlyList<MyTaskDto>> AssignedToMeAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        var rows = await db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId == me && t.CompletedAt == null)
            .Join(db.Projects, t => t.ProjectId, p => p.Id, (t, p) => new { Task = t, Project = p })
            .Where(x => x.Project.Members.Any(m => m.UserId == me))
            .Select(x => new
            {
                x.Task,
                x.Project.Key,
                ProjectName = x.Project.Name,
                ColumnName = x.Project.Columns.Where(c => c.Id == x.Task.ColumnId).Select(c => c.Name).First(),
                Comments = db.TaskComments.Count(c => c.TaskId == x.Task.Id),
            })
            .ToListAsync(ct);

        return rows
            .OrderBy(r => r.Task.DueDate ?? DateOnly.MaxValue)
            .ThenByDescending(r => r.Task.Priority)
            .Select(r => new MyTaskDto(ProjectService.ToCard(r.Task, r.Key, r.Comments), r.Task.ProjectId, r.ProjectName, r.ColumnName))
            .ToList();
    }

    private async Task<(TaskItem Task, Project Project)> LoadAsync(Guid taskId, CancellationToken ct)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException("Task", taskId);

        try
        {
            return (task, await projects.LoadForMemberAsync(task.ProjectId, ct));
        }
        catch (NotFoundException)
        {
            throw new NotFoundException("Task", taskId);
        }
    }

    private async Task<IReadOnlyList<CommentDto>> LoadCommentsAsync(Guid taskId, CancellationToken ct) =>
        await db.TaskComments.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderBy(c => c.CreatedAt)
            .Join(db.Users, c => c.AuthorId, u => u.Id, (c, u) => new CommentDto(c.Id, c.AuthorId, u.FullName, c.Body, c.CreatedAt))
            .ToListAsync(ct);

    private static void EnsureAssigneeIsMember(Project project, Guid? assigneeId)
    {
        if (assigneeId is { } id && !project.IsMember(id))
        {
            throw new ValidationException([new ValidationFailure("assigneeId", "The assignee must be a member of the project.")]);
        }
    }
}
