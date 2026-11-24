using FluentValidation;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.Application.Projects;

public sealed record MemberDto(Guid UserId, string FullName, string Email, string JobTitle, ProjectRole Role);

public sealed record ColumnDto(Guid Id, string Name, int Position, int? WipLimit);

public sealed record TaskCardDto(
    Guid Id,
    string Key,
    string Title,
    Guid ColumnId,
    int Position,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    int CommentCount,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt);

public sealed record BoardDto(
    Guid Id,
    string Name,
    string Key,
    string? Description,
    Guid OwnerId,
    ProjectRole MyRole,
    IReadOnlyList<ColumnDto> Columns,
    IReadOnlyList<MemberDto> Members,
    IReadOnlyList<TaskCardDto> Tasks);

public sealed record ProjectSummaryDto(
    Guid Id,
    string Name,
    string Key,
    string? Description,
    ProjectRole MyRole,
    int MemberCount,
    int OpenTasks,
    int DoneTasks,
    DateTimeOffset CreatedAt);

public sealed record CommentDto(Guid Id, Guid AuthorId, string AuthorName, string Body, DateTimeOffset CreatedAt);

public sealed record TaskDetailDto(TaskCardDto Card, string? Description, Guid CreatedById, IReadOnlyList<CommentDto> Comments);

public sealed record TaskPositionDto(Guid TaskId, Guid ColumnId, int Position, DateTimeOffset? CompletedAt);

public sealed record MyTaskDto(TaskCardDto Card, Guid ProjectId, string ProjectName, string ColumnName);

public sealed record CreateProjectRequest(string Name, string Key, string? Description);

public sealed record UpdateProjectRequest(string Name, string? Description);

public sealed record AddMemberRequest(string Email);

public sealed record SaveColumnRequest(string Name, int? WipLimit);

public sealed record SaveTaskRequest(
    string Title,
    string? Description,
    TaskPriority Priority,
    Guid? AssigneeId,
    DateOnly? DueDate,
    Guid? ColumnId = null);

public sealed record MoveTaskRequest(Guid ColumnId, int Index);

public sealed record CommentRequest(string Body);

public sealed class CreateProjectRequestValidator : AbstractValidator<CreateProjectRequest>
{
    public CreateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Project.NameMaxLength);
        RuleFor(x => x.Key)
            .Must(key => Project.IsValidKey(key?.Trim().ToUpperInvariant()))
            .WithMessage("Key must be 2-10 characters: a letter followed by letters or digits (e.g. WEB).");
        RuleFor(x => x.Description).MaximumLength(Project.DescriptionMaxLength);
    }
}

public sealed class UpdateProjectRequestValidator : AbstractValidator<UpdateProjectRequest>
{
    public UpdateProjectRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Project.NameMaxLength);
        RuleFor(x => x.Description).MaximumLength(Project.DescriptionMaxLength);
    }
}

public sealed class AddMemberRequestValidator : AbstractValidator<AddMemberRequest>
{
    public AddMemberRequestValidator() => RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
}

public sealed class SaveColumnRequestValidator : AbstractValidator<SaveColumnRequest>
{
    public SaveColumnRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(BoardColumn.NameMaxLength);
        RuleFor(x => x.WipLimit).InclusiveBetween(1, BoardColumn.MaxWipLimit).When(x => x.WipLimit is not null);
    }
}

public sealed class SaveTaskRequestValidator : AbstractValidator<SaveTaskRequest>
{
    public SaveTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(TaskItem.TitleMaxLength);
        RuleFor(x => x.Description).MaximumLength(TaskItem.DescriptionMaxLength);
        RuleFor(x => x.Priority).IsInEnum();
        RuleFor(x => x.DueDate)
            .InclusiveBetween(new DateOnly(2000, 1, 1), new DateOnly(2100, 12, 31))
            .When(x => x.DueDate is not null);
    }
}

public sealed class MoveTaskRequestValidator : AbstractValidator<MoveTaskRequest>
{
    public MoveTaskRequestValidator()
    {
        RuleFor(x => x.ColumnId).NotEmpty();
        RuleFor(x => x.Index).GreaterThanOrEqualTo(0);
    }
}

public sealed class CommentRequestValidator : AbstractValidator<CommentRequest>
{
    public CommentRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(TaskComment.BodyMaxLength);
}
