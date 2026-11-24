using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Tasks;

namespace TaskFlow.UnitTests;

public sealed class ProjectTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.CreateVersion7();
    private static readonly Guid Member = Guid.CreateVersion7();

    [Fact]
    public void New_project_has_a_kanban_layout_and_the_owner_as_member()
    {
        var project = Project.Create(Owner, "Website", "web", null, Now);

        Assert.Equal("WEB", project.Key);
        Assert.Equal(["Backlog", "To Do", "In Progress", "Review", "Done"], project.Columns.OrderBy(c => c.Position).Select(c => c.Name));
        Assert.Equal("Done", project.DoneColumn.Name);
        Assert.Equal(ProjectRole.Owner, Assert.Single(project.Members).Role);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("1WEB")]
    [InlineData("WEB-APP")]
    [InlineData("WAYTOOLONGKEY")]
    public void Invalid_keys_are_rejected(string key)
    {
        Assert.Throws<DomainException>(() => Project.Create(Owner, "Website", key, null, Now));
    }

    [Fact]
    public void Task_numbers_are_sequential()
    {
        var project = Project.Create(Owner, "Website", "WEB", null, Now);

        Assert.Equal([1, 2, 3], [project.AllocateTaskNumber(), project.AllocateTaskNumber(), project.AllocateTaskNumber()]);
    }

    [Fact]
    public void Only_the_owner_manages_membership_and_columns()
    {
        var project = Project.Create(Owner, "Website", "WEB", null, Now);
        project.AddMember(Owner, Member, Now);

        Assert.Throws<NotPermittedException>(() => project.AddMember(Member, Guid.CreateVersion7(), Now));
        Assert.Throws<NotPermittedException>(() => project.AddColumn(Member, "QA", null));
        Assert.Throws<DomainException>(() => project.AddMember(Owner, Member, Now));
        Assert.Throws<DomainException>(() => project.RemoveMember(Owner, Owner));
    }

    [Fact]
    public void Members_can_leave_on_their_own()
    {
        var project = Project.Create(Owner, "Website", "WEB", null, Now);
        project.AddMember(Owner, Member, Now);

        project.RemoveMember(Member, Member);

        Assert.False(project.IsMember(Member));
    }

    [Fact]
    public void New_columns_are_inserted_before_done()
    {
        var project = Project.Create(Owner, "Website", "WEB", null, Now);

        project.AddColumn(Owner, "QA", 2);

        Assert.Equal(["Backlog", "To Do", "In Progress", "Review", "QA", "Done"], project.Columns.OrderBy(c => c.Position).Select(c => c.Name));
    }

    [Fact]
    public void Columns_with_tasks_cannot_be_removed_and_wip_cannot_drop_below_current_load()
    {
        var project = Project.Create(Owner, "Website", "WEB", null, Now);
        var review = project.Columns.Single(c => c.Name == "Review");

        Assert.Throws<DomainException>(() => project.RemoveColumn(Owner, review.Id, taskCount: 1));
        Assert.Throws<DomainException>(() => project.UpdateColumn(Owner, review.Id, "Review", 2, taskCount: 3));

        project.RemoveColumn(Owner, review.Id, taskCount: 0);
        Assert.Equal([0, 1, 2, 3], project.Columns.OrderBy(c => c.Position).Select(c => c.Position));
    }
}

public sealed class BoardOrderingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Owner = Guid.CreateVersion7();

    private readonly Project _project = Project.Create(Owner, "Website", "WEB", null, Now);

    private BoardColumn Column(string name) => _project.Columns.Single(c => c.Name == name);

    private List<TaskItem> Fill(string column, int count)
    {
        var tasks = new List<TaskItem>();
        for (var i = 0; i < count; i++)
        {
            var task = new TaskItem(_project, _project.AllocateTaskNumber(), $"{column} {i}", null, TaskPriority.Medium, null, null, Owner);
            BoardOrdering.Add(_project, task, Column(column), tasks, Now);
            tasks.Add(task);
        }

        return tasks;
    }

    [Fact]
    public void Adding_appends_to_the_bottom_of_the_column()
    {
        var tasks = Fill("To Do", 3);

        Assert.Equal([0, 1, 2], tasks.Select(t => t.Position));
        Assert.All(tasks, t => Assert.Equal(Column("To Do").Id, t.ColumnId));
    }

    [Fact]
    public void Reordering_within_a_column_keeps_positions_dense()
    {
        var tasks = Fill("To Do", 4);
        var last = tasks[3];

        var changed = BoardOrdering.Move(_project, last, Column("To Do"), 0, tasks, tasks, Now);

        Assert.Equal(["To Do 3", "To Do 0", "To Do 1", "To Do 2"], tasks.OrderBy(t => t.Position).Select(t => t.Title));
        Assert.Equal(4, changed.Count);
    }

    [Fact]
    public void Moving_between_columns_renumbers_both_columns()
    {
        var todo = Fill("To Do", 3);
        var progress = Fill("In Progress", 2);
        var moving = todo[0];

        var changed = BoardOrdering.Move(_project, moving, Column("In Progress"), 1, todo, progress, Now);

        Assert.Equal(Column("In Progress").Id, moving.ColumnId);
        Assert.Equal(["In Progress 0", "To Do 0", "In Progress 1"], progress.Append(moving).OrderBy(t => t.Position).Select(t => t.Title));
        Assert.Equal([0, 1], todo.Where(t => t != moving).Select(t => t.Position));
        Assert.Contains(moving, changed);
        Assert.DoesNotContain(progress[0], changed);
    }

    [Fact]
    public void Index_beyond_the_end_appends()
    {
        var todo = Fill("To Do", 1);
        var review = Fill("Review", 1);

        BoardOrdering.Move(_project, todo[0], Column("Review"), 99, todo, review, Now);

        Assert.Equal(1, todo[0].Position);
    }

    [Fact]
    public void Wip_limit_blocks_moves_into_a_full_column_but_allows_reordering_inside_it()
    {
        var review = Fill("Review", 3);
        var todo = Fill("To Do", 1);

        var error = Assert.Throws<DomainException>(() => BoardOrdering.Move(_project, todo[0], Column("Review"), 0, todo, review, Now));
        Assert.Contains("WIP limit of 3", error.Message, StringComparison.Ordinal);

        BoardOrdering.Move(_project, review[2], Column("Review"), 0, review, review, Now);
        Assert.Equal(0, review[2].Position);
    }

    [Fact]
    public void Moving_into_done_marks_completion_and_moving_out_clears_it()
    {
        var todo = Fill("To Do", 1);
        var task = todo[0];

        BoardOrdering.Move(_project, task, Column("Done"), 0, todo, [], Now);
        Assert.Equal(Now, task.CompletedAt);

        BoardOrdering.Move(_project, task, Column("To Do"), 0, [task], [], Now.AddHours(1));
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Removing_a_task_closes_the_gap()
    {
        var todo = Fill("To Do", 3);

        BoardOrdering.Remove(todo[1], todo);

        Assert.Equal([0, 1], new[] { todo[0], todo[2] }.Select(t => t.Position));
    }
}
