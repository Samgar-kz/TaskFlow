using FluentValidation;
using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.Domain.Users;

namespace TaskFlow.Application.Projects;

public sealed class ProjectService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IBoardNotifier notifier,
    TimeProvider clock,
    IValidator<CreateProjectRequest> createValidator,
    IValidator<UpdateProjectRequest> updateValidator,
    IValidator<AddMemberRequest> memberValidator,
    IValidator<SaveColumnRequest> columnValidator)
{
    public async Task<IReadOnlyList<ProjectSummaryDto>> ListMineAsync(CancellationToken ct)
    {
        var me = currentUser.Id;
        return await db.Projects.AsNoTracking()
            .Where(p => p.Members.Any(m => m.UserId == me))
            .OrderBy(p => p.Name)
            .Select(p => new ProjectSummaryDto(
                p.Id,
                p.Name,
                p.Key,
                p.Description,
                p.Members.Where(m => m.UserId == me).Select(m => m.Role).First(),
                p.Members.Count,
                db.Tasks.Count(t => t.ProjectId == p.Id && t.CompletedAt == null),
                db.Tasks.Count(t => t.ProjectId == p.Id && t.CompletedAt != null),
                p.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<BoardDto> CreateAsync(CreateProjectRequest request, CancellationToken ct)
    {
        await createValidator.ValidateAndThrowAsync(request, ct);
        var key = request.Key.Trim().ToUpperInvariant();
        if (await db.Projects.AnyAsync(p => p.OwnerId == currentUser.Id && p.Key == key, ct))
        {
            throw new ConflictException($"You already have a project with the key {key}.");
        }

        var project = Project.Create(currentUser.Id, request.Name, key, request.Description, clock.GetUtcNow());
        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);
        return await GetBoardAsync(project.Id, ct);
    }

    /// <summary>Everything needed to render a board: columns, members and task cards.</summary>
    public async Task<BoardDto> GetBoardAsync(Guid projectId, CancellationToken ct)
    {
        var project = await LoadForMemberAsync(projectId, ct);
        var members = await LoadMembersAsync(project, ct);
        var tasks = await LoadCardsAsync(db.Tasks.Where(t => t.ProjectId == projectId), project.Key, ct);

        return new BoardDto(
            project.Id,
            project.Name,
            project.Key,
            project.Description,
            project.OwnerId,
            project.IsOwner(currentUser.Id) ? ProjectRole.Owner : ProjectRole.Member,
            project.Columns.OrderBy(c => c.Position).Select(c => new ColumnDto(c.Id, c.Name, c.Position, c.WipLimit)).ToList(),
            members,
            tasks);
    }

    public async Task<BoardDto> UpdateAsync(Guid projectId, UpdateProjectRequest request, CancellationToken ct)
    {
        await updateValidator.ValidateAndThrowAsync(request, ct);
        var project = await LoadForMemberAsync(projectId, ct);
        project.Update(currentUser.Id, request.Name, request.Description);
        return await SaveAndBroadcastAsync(project, ct);
    }

    public async Task DeleteAsync(Guid projectId, CancellationToken ct)
    {
        var project = await LoadForMemberAsync(projectId, ct);
        if (!project.IsOwner(currentUser.Id))
        {
            throw new Domain.Common.NotPermittedException("Only the project owner can delete the project.");
        }

        db.Projects.Remove(project);
        await db.SaveChangesAsync(ct);
        await notifier.BoardChangedAsync(projectId, ct);
    }

    public async Task<BoardDto> AddMemberAsync(Guid projectId, AddMemberRequest request, CancellationToken ct)
    {
        await memberValidator.ValidateAndThrowAsync(request, ct);
        var project = await LoadForMemberAsync(projectId, ct);
        var email = User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new NotFoundException("User", email);

        project.AddMember(currentUser.Id, user.Id, clock.GetUtcNow());
        return await SaveAndBroadcastAsync(project, ct);
    }

    /// <summary>Removes a member (or lets a member leave) and unassigns their open tasks in the project.</summary>
    public async Task<BoardDto?> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken ct)
    {
        var project = await LoadForMemberAsync(projectId, ct);
        project.RemoveMember(currentUser.Id, userId);

        var assigned = await db.Tasks.Where(t => t.ProjectId == projectId && t.AssigneeId == userId).ToListAsync(ct);
        foreach (var task in assigned)
        {
            task.Update(task.Title, task.Description, task.Priority, null, task.DueDate);
        }

        await db.SaveChangesAsync(ct);
        await notifier.BoardChangedAsync(projectId, ct);
        return userId == currentUser.Id ? null : await GetBoardAsync(projectId, ct);
    }

    public async Task<BoardDto> AddColumnAsync(Guid projectId, SaveColumnRequest request, CancellationToken ct)
    {
        await columnValidator.ValidateAndThrowAsync(request, ct);
        var project = await LoadForMemberAsync(projectId, ct);
        project.AddColumn(currentUser.Id, request.Name, request.WipLimit);
        return await SaveAndBroadcastAsync(project, ct);
    }

    public async Task<BoardDto> UpdateColumnAsync(Guid projectId, Guid columnId, SaveColumnRequest request, CancellationToken ct)
    {
        await columnValidator.ValidateAndThrowAsync(request, ct);
        var project = await LoadForMemberAsync(projectId, ct);
        var count = await db.Tasks.CountAsync(t => t.ColumnId == columnId, ct);
        project.UpdateColumn(currentUser.Id, columnId, request.Name, request.WipLimit, count);
        return await SaveAndBroadcastAsync(project, ct);
    }

    public async Task<BoardDto> RemoveColumnAsync(Guid projectId, Guid columnId, CancellationToken ct)
    {
        var project = await LoadForMemberAsync(projectId, ct);
        var count = await db.Tasks.CountAsync(t => t.ColumnId == columnId, ct);
        project.RemoveColumn(currentUser.Id, columnId, count);
        return await SaveAndBroadcastAsync(project, ct);
    }

    internal async Task<Project> LoadForMemberAsync(Guid projectId, CancellationToken ct)
    {
        var project = await db.Projects
            .Include(p => p.Columns)
            .Include(p => p.Members)
            .SingleOrDefaultAsync(p => p.Id == projectId, ct);

        // Non-members get 404 so they cannot discover which projects exist.
        if (project is null || !project.IsMember(currentUser.Id))
        {
            throw new NotFoundException("Project", projectId);
        }

        return project;
    }

    internal async Task<IReadOnlyList<TaskCardDto>> LoadCardsAsync(IQueryable<TaskItem> tasks, string key, CancellationToken ct)
    {
        var rows = await tasks.AsNoTracking()
            .OrderBy(t => t.Position)
            .Select(t => new { Task = t, Comments = db.TaskComments.Count(c => c.TaskId == t.Id) })
            .ToListAsync(ct);
        return rows.Select(r => ToCard(r.Task, key, r.Comments)).ToList();
    }

    internal static TaskCardDto ToCard(TaskItem t, string key, int commentCount) =>
        new(t.Id, $"{key}-{t.Number}", t.Title, t.ColumnId, t.Position, t.Priority, t.AssigneeId, t.DueDate, commentCount, t.CompletedAt, t.CreatedAt);

    private async Task<IReadOnlyList<MemberDto>> LoadMembersAsync(Project project, CancellationToken ct)
    {
        var ids = project.Members.Select(m => m.UserId).ToList();
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return project.Members
            .Where(m => users.ContainsKey(m.UserId))
            .OrderBy(m => m.Role)
            .ThenBy(m => users[m.UserId].FullName)
            .Select(m => new MemberDto(m.UserId, users[m.UserId].FullName, users[m.UserId].Email, users[m.UserId].JobTitle, m.Role))
            .ToList();
    }

    private async Task<BoardDto> SaveAndBroadcastAsync(Project project, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        await notifier.BoardChangedAsync(project.Id, ct);
        return await GetBoardAsync(project.Id, ct);
    }
}
