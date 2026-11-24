using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;

namespace TaskFlow.Domain.Tasks;

/// <summary>
/// Keeps task positions dense (0..n-1) and consistent when cards are added or dragged between
/// columns, and enforces column WIP limits. Pure logic, so every ordering rule is unit-testable.
/// </summary>
public static class BoardOrdering
{
    /// <summary>Appends a new task to the bottom of a column.</summary>
    public static void Add(Project project, TaskItem task, BoardColumn column, IReadOnlyCollection<TaskItem> columnTasks, DateTimeOffset now)
    {
        EnsureCapacity(column, columnTasks.Count);
        task.Place(column.Id, columnTasks.Count, column.Id == project.DoneColumn.Id, now);
    }

    /// <summary>
    /// Moves <paramref name="task"/> to <paramref name="index"/> in <paramref name="target"/>. Returns every task
    /// whose column or position changed, so callers can persist and broadcast only those.
    /// </summary>
    public static IReadOnlyList<TaskItem> Move(
        Project project,
        TaskItem task,
        BoardColumn target,
        int index,
        IReadOnlyCollection<TaskItem> sourceColumnTasks,
        IReadOnlyCollection<TaskItem> targetColumnTasks,
        DateTimeOffset now)
    {
        if (target.ProjectId != task.ProjectId)
        {
            throw new DomainException("Tasks can only be moved within their project.");
        }

        var sameColumn = task.ColumnId == target.Id;
        var others = targetColumnTasks.Where(t => t.Id != task.Id).OrderBy(t => t.Position).ToList();
        if (!sameColumn)
        {
            EnsureCapacity(target, others.Count);
        }

        var before = Snapshot(sourceColumnTasks.Concat(targetColumnTasks).Append(task));

        others.Insert(Math.Clamp(index, 0, others.Count), task);
        task.Place(target.Id, others.IndexOf(task), target.Id == project.DoneColumn.Id, now);
        Renumber(others);

        if (!sameColumn)
        {
            Renumber(sourceColumnTasks.Where(t => t.Id != task.Id).OrderBy(t => t.Position).ToList());
        }

        return before
            .Where(entry => entry.Key.ColumnId != entry.Value.ColumnId || entry.Key.Position != entry.Value.Position)
            .Select(entry => entry.Key)
            .ToList();
    }

    /// <summary>Closes the gap a removed task leaves in its column.</summary>
    public static void Remove(TaskItem removed, IReadOnlyCollection<TaskItem> columnTasks) =>
        Renumber(columnTasks.Where(t => t.Id != removed.Id && t.ColumnId == removed.ColumnId).OrderBy(t => t.Position).ToList());

    private static void EnsureCapacity(BoardColumn column, int currentCount)
    {
        if (column.WipLimit is { } limit && currentCount >= limit)
        {
            throw new DomainException($"\"{column.Name}\" has reached its WIP limit of {limit}. Finish something first.");
        }
    }

    private static void Renumber(List<TaskItem> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].Reposition(i);
        }
    }

    private static Dictionary<TaskItem, (Guid ColumnId, int Position)> Snapshot(IEnumerable<TaskItem> tasks) =>
        tasks.DistinctBy(t => t.Id).ToDictionary(t => t, t => (t.ColumnId, t.Position));
}
