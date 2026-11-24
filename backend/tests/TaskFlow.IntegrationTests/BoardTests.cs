using System.Net;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using TaskFlow.Application.Projects;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;
using TaskFlow.IntegrationTests.Infrastructure;

namespace TaskFlow.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class BoardTests(ApiFactory factory)
{
    private static readonly TimeSpan RealtimeTimeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Creating_a_project_sets_up_a_kanban_board()
    {
        var owner = await factory.CreateUserAsync();

        var board = await CreateProject(owner, "web");

        Assert.Equal("WEB", board.Key);
        Assert.Equal(ProjectRole.Owner, board.MyRole);
        Assert.Equal(5, board.Columns.Count);
        Assert.Equal(owner.User.Id, Assert.Single(board.Members).UserId);

        var duplicate = await owner.Client.PostJsonAsync("/api/projects", new CreateProjectRequest("Other", "WEB", null));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task Boards_are_visible_to_members_only()
    {
        var owner = await factory.CreateUserAsync();
        var outsider = await factory.CreateUserAsync();
        var board = await CreateProject(owner, "PRIV");

        Assert.Equal(HttpStatusCode.NotFound, (await outsider.Client.GetAsync($"/api/projects/{board.Id}")).StatusCode);

        await (await owner.Client.PostJsonAsync($"/api/projects/{board.Id}/members", new AddMemberRequest(outsider.User.Email))).ReadAsync<BoardDto>();

        var projects = await (await outsider.Client.GetAsync("/api/projects")).ReadAsync<List<ProjectSummaryDto>>();
        Assert.Contains(projects, p => p.Id == board.Id && p.MyRole == ProjectRole.Member);

        // Members work with tasks, but only the owner changes the board structure.
        var addColumn = await outsider.Client.PostJsonAsync($"/api/projects/{board.Id}/columns", new SaveColumnRequest("QA", null));
        Assert.Equal(HttpStatusCode.Forbidden, addColumn.StatusCode);
    }

    [Fact]
    public async Task Tasks_get_sequential_keys_and_assignees_must_be_members()
    {
        var owner = await factory.CreateUserAsync();
        var outsider = await factory.CreateUserAsync();
        var board = await CreateProject(owner, "KEY");

        var first = await CreateTask(owner, board, "First");
        var second = await CreateTask(owner, board, "Second");
        Assert.Equal(["KEY-1", "KEY-2"], new[] { first.Key, second.Key });
        Assert.Equal(board.Columns[0].Id, first.ColumnId);
        Assert.Equal([0, 1], new[] { first.Position, second.Position });

        var invalid = await owner.Client.PostJsonAsync($"/api/projects/{board.Id}/tasks",
            new SaveTaskRequest("Nope", null, TaskPriority.Low, outsider.User.Id, null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Moving_tasks_reorders_columns_and_tracks_completion()
    {
        var owner = await factory.CreateUserAsync();
        var board = await CreateProject(owner, "MOVE");
        var a = await CreateTask(owner, board, "A");
        var b = await CreateTask(owner, board, "B");
        var c = await CreateTask(owner, board, "C");
        var done = board.Columns[^1];

        var moved = await (await owner.Client.PostJsonAsync($"/api/tasks/{a.Id}/move", new MoveTaskRequest(done.Id, 0))).ReadAsync<List<TaskPositionDto>>();
        Assert.Contains(moved, p => p.TaskId == a.Id && p.ColumnId == done.Id && p.CompletedAt is not null);

        var reordered = await (await owner.Client.PostJsonAsync($"/api/tasks/{c.Id}/move", new MoveTaskRequest(board.Columns[0].Id, 0))).ReadAsync<List<TaskPositionDto>>();
        Assert.Equal(2, reordered.Count);

        var refreshed = await (await owner.Client.GetAsync($"/api/projects/{board.Id}")).ReadAsync<BoardDto>();
        var backlog = refreshed.Tasks.Where(t => t.ColumnId == board.Columns[0].Id).OrderBy(t => t.Position).Select(t => t.Title);
        Assert.Equal(["C", "B"], backlog);
        Assert.Equal(b.Id, refreshed.Tasks.Single(t => t.Title == "B").Id);
    }

    [Fact]
    public async Task Wip_limits_are_enforced()
    {
        var owner = await factory.CreateUserAsync();
        var board = await CreateProject(owner, "WIP");
        var review = board.Columns.Single(c => c.Name == "Review");
        await (await owner.Client.PutJsonAsync($"/api/projects/{board.Id}/columns/{review.Id}", new SaveColumnRequest("Review", 1))).ReadAsync<BoardDto>();

        var first = await CreateTask(owner, board, "First");
        var second = await CreateTask(owner, board, "Second");
        (await owner.Client.PostJsonAsync($"/api/tasks/{first.Id}/move", new MoveTaskRequest(review.Id, 0))).EnsureSuccessStatusCode();

        var blocked = await owner.Client.PostJsonAsync($"/api/tasks/{second.Id}/move", new MoveTaskRequest(review.Id, 0));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, blocked.StatusCode);
        Assert.Contains("WIP limit", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Comments_assignments_and_my_tasks_work_together()
    {
        var owner = await factory.CreateUserAsync("Owner");
        var dev = await factory.CreateUserAsync("Developer");
        var board = await CreateProject(owner, "MINE");
        await (await owner.Client.PostJsonAsync($"/api/projects/{board.Id}/members", new AddMemberRequest(dev.User.Email))).ReadAsync<BoardDto>();

        var task = await (await owner.Client.PostJsonAsync($"/api/projects/{board.Id}/tasks",
            new SaveTaskRequest("Build API", "Endpoints for orders", TaskPriority.High, dev.User.Id, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3)))).ReadAsync<TaskCardDto>();

        var comment = await (await dev.Client.PostJsonAsync($"/api/tasks/{task.Id}/comments", new CommentRequest("On it!"))).ReadAsync<CommentDto>();
        Assert.Equal("Developer", comment.AuthorName);

        var detail = await (await owner.Client.GetAsync($"/api/tasks/{task.Id}")).ReadAsync<TaskDetailDto>();
        Assert.Equal("Endpoints for orders", detail.Description);
        Assert.Equal(1, detail.Card.CommentCount);

        var mine = await (await dev.Client.GetAsync("/api/tasks/mine")).ReadAsync<List<MyTaskDto>>();
        var mineTask = Assert.Single(mine);
        Assert.Equal("MINE-1", mineTask.Card.Key);
        Assert.Equal("Backlog", mineTask.ColumnName);

        // Removing the member unassigns their tasks.
        var afterRemoval = await (await owner.Client.DeleteAsync($"/api/projects/{board.Id}/members/{dev.User.Id}")).ReadAsync<BoardDto>();
        Assert.Null(afterRemoval.Tasks.Single().AssigneeId);
        Assert.Equal(HttpStatusCode.NotFound, (await dev.Client.GetAsync($"/api/tasks/{task.Id}")).StatusCode);
    }

    [Fact]
    public async Task Deleting_a_task_closes_the_gap_in_its_column()
    {
        var owner = await factory.CreateUserAsync();
        var board = await CreateProject(owner, "DEL");
        await CreateTask(owner, board, "A");
        var b = await CreateTask(owner, board, "B");
        await CreateTask(owner, board, "C");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.Client.DeleteAsync($"/api/tasks/{b.Id}")).StatusCode);

        var refreshed = await (await owner.Client.GetAsync($"/api/projects/{board.Id}")).ReadAsync<BoardDto>();
        Assert.Equal([("A", 0), ("C", 1)], refreshed.Tasks.OrderBy(t => t.Position).Select(t => (t.Title, t.Position)));
    }

    [Fact]
    public async Task Board_changes_are_pushed_to_other_members_in_real_time()
    {
        var owner = await factory.CreateUserAsync();
        var teammate = await factory.CreateUserAsync();
        var board = await CreateProject(owner, "LIVE");
        await (await owner.Client.PostJsonAsync($"/api/projects/{board.Id}/members", new AddMemberRequest(teammate.User.Email))).ReadAsync<BoardDto>();

        await using var connection = await factory.ConnectToBoardAsync(teammate.AccessToken);
        var created = new TaskCompletionSource<TaskCardDto>(TaskCreationOptions.RunContinuationsAsynchronously);
        var moved = new TaskCompletionSource<IReadOnlyList<TaskPositionDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.On<TaskCardDto>("TaskUpserted", card => created.TrySetResult(card));
        connection.On<IReadOnlyList<TaskPositionDto>>("TasksMoved", positions => moved.TrySetResult(positions));
        await connection.InvokeAsync("JoinProject", board.Id);

        var task = await CreateTask(owner, board, "Pushed live");
        var received = await created.Task.WaitAsync(RealtimeTimeout);
        Assert.Equal(task.Id, received.Id);
        Assert.Equal("LIVE-1", received.Key);

        (await owner.Client.PostJsonAsync($"/api/tasks/{task.Id}/move", new MoveTaskRequest(board.Columns[2].Id, 0))).EnsureSuccessStatusCode();
        var positions = await moved.Task.WaitAsync(RealtimeTimeout);
        Assert.Contains(positions, p => p.TaskId == task.Id && p.ColumnId == board.Columns[2].Id);
    }

    [Fact]
    public async Task Hub_refuses_to_join_projects_the_user_is_not_part_of()
    {
        var owner = await factory.CreateUserAsync();
        var outsider = await factory.CreateUserAsync();
        var board = await CreateProject(owner, "SECRET");

        await using var connection = await factory.ConnectToBoardAsync(outsider.AccessToken);

        await Assert.ThrowsAsync<HubException>(() => connection.InvokeAsync("JoinProject", board.Id));
    }

    [Fact]
    public async Task Seeded_demo_user_has_two_boards()
    {
        var demo = await factory.LoginAsync(ApiFactory.DemoEmail, ApiFactory.Password);

        var projects = await (await demo.Client.GetAsync("/api/projects")).ReadAsync<List<ProjectSummaryDto>>();

        Assert.Equal(["Mobile Banking App", "Website Redesign"], projects.Select(p => p.Name));
        Assert.Contains(projects, p => p.Key == "MOB" && p.MyRole == ProjectRole.Owner);
    }

    [Fact]
    public async Task Health_endpoint_reports_healthy()
    {
        Assert.Equal("Healthy", await factory.CreateClient().GetStringAsync("/health"));
    }

    private static async Task<BoardDto> CreateProject(TestUser owner, string key) =>
        await (await owner.Client.PostJsonAsync("/api/projects", new CreateProjectRequest($"Project {key}", key, "Test project"))).ReadAsync<BoardDto>();

    private static async Task<TaskCardDto> CreateTask(TestUser user, BoardDto board, string title) =>
        await (await user.Client.PostJsonAsync($"/api/projects/{board.Id}/tasks",
            new SaveTaskRequest(title, null, TaskPriority.Medium, null, null))).ReadAsync<TaskCardDto>();
}
