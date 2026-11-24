using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Projects;

namespace TaskFlow.Api.Controllers;

[ApiController]
[Route("api/projects")]
public sealed class ProjectsController(ProjectService projects, TaskService tasks) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<ProjectSummaryDto>> List(CancellationToken ct) => projects.ListMineAsync(ct);

    [HttpPost]
    [ProducesResponseType<BoardDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(CreateProjectRequest request, CancellationToken ct)
    {
        var board = await projects.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Board), new { id = board.Id }, board);
    }

    /// <summary>The full board: columns, members and task cards.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<BoardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<BoardDto> Board(Guid id, CancellationToken ct) => projects.GetBoardAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<BoardDto> Update(Guid id, UpdateProjectRequest request, CancellationToken ct) => projects.UpdateAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await projects.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/members")]
    public Task<BoardDto> AddMember(Guid id, AddMemberRequest request, CancellationToken ct) => projects.AddMemberAsync(id, request, ct);

    /// <summary>Removes a member (owner), or leaves the project (when the id is the caller's).</summary>
    [HttpDelete("{id:guid}/members/{userId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid userId, CancellationToken ct)
    {
        var board = await projects.RemoveMemberAsync(id, userId, ct);
        return board is null ? NoContent() : Ok(board);
    }

    [HttpPost("{id:guid}/columns")]
    public Task<BoardDto> AddColumn(Guid id, SaveColumnRequest request, CancellationToken ct) => projects.AddColumnAsync(id, request, ct);

    [HttpPut("{id:guid}/columns/{columnId:guid}")]
    public Task<BoardDto> UpdateColumn(Guid id, Guid columnId, SaveColumnRequest request, CancellationToken ct) =>
        projects.UpdateColumnAsync(id, columnId, request, ct);

    [HttpDelete("{id:guid}/columns/{columnId:guid}")]
    public Task<BoardDto> RemoveColumn(Guid id, Guid columnId, CancellationToken ct) => projects.RemoveColumnAsync(id, columnId, ct);

    [HttpPost("{id:guid}/tasks")]
    [ProducesResponseType<TaskCardDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateTask(Guid id, SaveTaskRequest request, CancellationToken ct)
    {
        var card = await tasks.CreateAsync(id, request, ct);
        return Created($"/api/tasks/{card.Id}", card);
    }
}

[ApiController]
[Route("api/tasks")]
public sealed class TasksController(TaskService tasks) : ControllerBase
{
    /// <summary>Open tasks assigned to the caller across all projects.</summary>
    [HttpGet("mine")]
    public Task<IReadOnlyList<MyTaskDto>> Mine(CancellationToken ct) => tasks.AssignedToMeAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<TaskDetailDto> Get(Guid id, CancellationToken ct) => tasks.GetAsync(id, ct);

    [HttpPut("{id:guid}")]
    public Task<TaskCardDto> Update(Guid id, SaveTaskRequest request, CancellationToken ct) => tasks.UpdateAsync(id, request, ct);

    /// <summary>Moves a task to a zero-based index in a column; returns every position that changed.</summary>
    [HttpPost("{id:guid}/move")]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public Task<IReadOnlyList<TaskPositionDto>> Move(Guid id, MoveTaskRequest request, CancellationToken ct) =>
        tasks.MoveAsync(id, request, ct);

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await tasks.DeleteAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/comments")]
    public Task<CommentDto> Comment(Guid id, CommentRequest request, CancellationToken ct) => tasks.AddCommentAsync(id, request, ct);
}
