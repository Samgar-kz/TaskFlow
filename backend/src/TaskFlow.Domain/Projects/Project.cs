using System.Text.RegularExpressions;
using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Projects;

public enum ProjectRole
{
    Owner = 0,
    Member = 1,
}

/// <summary>A board column. Columns are ordered by <see cref="Position"/>; the last one means "done".</summary>
public sealed class BoardColumn : Entity
{
    public const int NameMaxLength = 40;
    public const int MaxWipLimit = 100;

    private BoardColumn()
    {
        // Required by EF Core.
    }

    internal BoardColumn(Guid projectId, string name, int position, int? wipLimit)
    {
        ProjectId = projectId;
        Position = position;
        Update(name, wipLimit);
    }

    public Guid ProjectId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Position { get; private set; }

    /// <summary>Optional work-in-progress limit: the maximum number of tasks the column may hold.</summary>
    public int? WipLimit { get; private set; }

    internal void Update(string name, int? wipLimit)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Column name is required and must be at most {NameMaxLength} characters.");
        }

        if (wipLimit is < 1 or > MaxWipLimit)
        {
            throw new DomainException($"WIP limit must be between 1 and {MaxWipLimit}.");
        }

        Name = name.Trim();
        WipLimit = wipLimit;
    }

    internal void MoveTo(int position) => Position = position;
}

public sealed class ProjectMember : Entity
{
    private ProjectMember()
    {
        // Required by EF Core.
    }

    internal ProjectMember(Guid projectId, Guid userId, ProjectRole role, DateTimeOffset joinedAt)
    {
        ProjectId = projectId;
        UserId = userId;
        Role = role;
        JoinedAt = joinedAt;
    }

    public Guid ProjectId { get; private set; }

    public Guid UserId { get; private set; }

    public ProjectRole Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }
}

/// <summary>
/// A project board: its columns, members and task numbering. Structural changes (columns,
/// membership, settings) are reserved for the owner; any member can work with tasks.
/// </summary>
public sealed partial class Project : Entity, IAuditable
{
    public const int NameMaxLength = 80;
    public const int DescriptionMaxLength = 1000;
    public const int MaxColumns = 10;
    public const int MaxMembers = 50;

    private readonly List<BoardColumn> _columns = [];
    private readonly List<ProjectMember> _members = [];

    private Project()
    {
        // Required by EF Core.
    }

    private Project(Guid ownerId, string name, string key, string? description)
    {
        if (!IsValidKey(key))
        {
            throw new DomainException("Key must be 2-10 characters: an uppercase letter followed by uppercase letters or digits.");
        }

        OwnerId = ownerId;
        Key = key;
        NextTaskNumber = 1;
        SetDetails(name, description);
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Short prefix of task keys, e.g. <c>WEB</c> in <c>WEB-42</c>.</summary>
    public string Key { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid OwnerId { get; private set; }

    public int NextTaskNumber { get; private set; }

    /// <summary>Concurrency token (PostgreSQL <c>xmin</c>): task numbers are never handed out twice.</summary>
    public uint Version { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public IReadOnlyList<BoardColumn> Columns => _columns;

    public IReadOnlyList<ProjectMember> Members => _members;

    /// <summary>The right-most column; tasks there count as completed.</summary>
    public BoardColumn DoneColumn => _columns.MaxBy(c => c.Position)!;

    public static bool IsValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);

    /// <summary>Creates a project with a conventional Kanban layout and the creator as owner.</summary>
    public static Project Create(Guid ownerId, string name, string key, string? description, DateTimeOffset now)
    {
        var project = new Project(ownerId, name, key.Trim().ToUpperInvariant(), description);
        project._members.Add(new ProjectMember(project.Id, ownerId, ProjectRole.Owner, now));

        (string Name, int? Wip)[] layout = [("Backlog", null), ("To Do", null), ("In Progress", 5), ("Review", 3), ("Done", null)];
        for (var i = 0; i < layout.Length; i++)
        {
            project._columns.Add(new BoardColumn(project.Id, layout[i].Name, i, layout[i].Wip));
        }

        return project;
    }

    public bool IsMember(Guid userId) => _members.Any(m => m.UserId == userId);

    public bool IsOwner(Guid userId) => OwnerId == userId;

    public BoardColumn GetColumn(Guid columnId) =>
        _columns.SingleOrDefault(c => c.Id == columnId) ?? throw new DomainException("The column does not belong to this project.");

    public void Update(Guid actorId, string name, string? description)
    {
        EnsureOwner(actorId);
        SetDetails(name, description);
    }

    public void AddMember(Guid actorId, Guid userId, DateTimeOffset now)
    {
        EnsureOwner(actorId);
        if (IsMember(userId))
        {
            throw new DomainException("This person is already a member of the project.");
        }

        if (_members.Count >= MaxMembers)
        {
            throw new DomainException($"A project can have at most {MaxMembers} members.");
        }

        _members.Add(new ProjectMember(Id, userId, ProjectRole.Member, now));
    }

    public void RemoveMember(Guid actorId, Guid userId)
    {
        if (actorId != userId)
        {
            EnsureOwner(actorId);
        }

        if (userId == OwnerId)
        {
            throw new DomainException("The project owner cannot be removed.");
        }

        var member = _members.SingleOrDefault(m => m.UserId == userId)
            ?? throw new DomainException("This person is not a member of the project.");
        _members.Remove(member);
    }

    public BoardColumn AddColumn(Guid actorId, string name, int? wipLimit)
    {
        EnsureOwner(actorId);
        if (_columns.Count >= MaxColumns)
        {
            throw new DomainException($"A board can have at most {MaxColumns} columns.");
        }

        // New columns are inserted before "Done" so the last column keeps meaning "completed".
        var done = DoneColumn;
        var column = new BoardColumn(Id, name, done.Position, wipLimit);
        done.MoveTo(done.Position + 1);
        _columns.Add(column);
        return column;
    }

    public void UpdateColumn(Guid actorId, Guid columnId, string name, int? wipLimit, int taskCount)
    {
        EnsureOwner(actorId);
        if (wipLimit is { } limit && taskCount > limit)
        {
            throw new DomainException($"The column already holds {taskCount} tasks; the WIP limit cannot be lower than that.");
        }

        GetColumn(columnId).Update(name, wipLimit);
    }

    public void RemoveColumn(Guid actorId, Guid columnId, int taskCount)
    {
        EnsureOwner(actorId);
        var column = GetColumn(columnId);
        if (taskCount > 0)
        {
            throw new DomainException("Move or delete the tasks in this column before removing it.");
        }

        if (_columns.Count <= 2)
        {
            throw new DomainException("A board needs at least two columns.");
        }

        _columns.Remove(column);
        var ordered = _columns.OrderBy(c => c.Position).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].MoveTo(i);
        }
    }

    /// <summary>Reserves the next sequential task number (WEB-1, WEB-2, ...).</summary>
    public int AllocateTaskNumber() => NextTaskNumber++;

    private void EnsureOwner(Guid actorId)
    {
        if (actorId != OwnerId)
        {
            throw new NotPermittedException("Only the project owner can change the board settings or membership.");
        }
    }

    private void SetDetails(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Project name is required and must be at most {NameMaxLength} characters.");
        }

        if (description is not null && description.Length > DescriptionMaxLength)
        {
            throw new DomainException($"Description must be at most {DescriptionMaxLength} characters.");
        }

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }

    [GeneratedRegex("^[A-Z][A-Z0-9]{1,9}$")]
    private static partial Regex KeyPattern();
}
