using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tasks;

public enum TaskPriority
{
    Low = 0,
    Medium = 1,
    High = 2,
    Urgent = 3,
}

/// <summary>A card on a board. Its place is a column plus a zero-based position within that column.</summary>
public sealed class TaskItem : Entity, IAuditable
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 5000;

    private TaskItem()
    {
        // Required by EF Core.
    }

    public TaskItem(
        Project project,
        int number,
        string title,
        string? description,
        TaskPriority priority,
        Guid? assigneeId,
        DateOnly? dueDate,
        Guid createdById)
    {
        ProjectId = project.Id;
        Number = number;
        CreatedById = createdById;
        Update(title, description, priority, assigneeId, dueDate);
    }

    public Guid ProjectId { get; private set; }

    public int Number { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid ColumnId { get; private set; }

    public int Position { get; private set; }

    public TaskPriority Priority { get; private set; }

    public Guid? AssigneeId { get; private set; }

    public DateOnly? DueDate { get; private set; }

    public Guid CreatedById { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public void Update(string title, string? description, TaskPriority priority, Guid? assigneeId, DateOnly? dueDate)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > TitleMaxLength)
        {
            throw new DomainException($"Title is required and must be at most {TitleMaxLength} characters.");
        }

        if (description is not null && description.Length > DescriptionMaxLength)
        {
            throw new DomainException($"Description must be at most {DescriptionMaxLength} characters.");
        }

        if (!Enum.IsDefined(priority))
        {
            throw new DomainException("Unknown priority.");
        }

        Title = title.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Priority = priority;
        AssigneeId = assigneeId;
        DueDate = dueDate;
    }

    internal void Place(Guid columnId, int position, bool isDoneColumn, DateTimeOffset now)
    {
        ColumnId = columnId;
        Position = position;
        if (isDoneColumn)
        {
            CompletedAt ??= now;
        }
        else
        {
            CompletedAt = null;
        }
    }

    internal void Reposition(int position) => Position = position;
}

public sealed class TaskComment : Entity
{
    public const int BodyMaxLength = 2000;

    private TaskComment()
    {
        // Required by EF Core.
    }

    public TaskComment(Guid taskId, Guid authorId, string body, DateTimeOffset createdAt)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > BodyMaxLength)
        {
            throw new DomainException($"Comment is required and must be at most {BodyMaxLength} characters.");
        }

        TaskId = taskId;
        AuthorId = authorId;
        Body = body.Trim();
        CreatedAt = createdAt;
    }

    public Guid TaskId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Body { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }
}
