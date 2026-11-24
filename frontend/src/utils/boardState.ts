import type { TaskCard, TaskPosition } from '@/api/types'

/** Tasks of one column in display order. */
export function columnTasks(tasks: TaskCard[], columnId: string): TaskCard[] {
  return tasks.filter((t) => t.columnId === columnId).sort((a, b) => a.position - b.position)
}

/**
 * Applies a drag-and-drop move locally, mirroring the server's ordering rules, so the board
 * updates instantly. The server's response (or a real-time event) confirms the final positions.
 */
export function applyMove(tasks: TaskCard[], taskId: string, targetColumnId: string, index: number): TaskCard[] {
  const moving = tasks.find((t) => t.id === taskId)
  if (!moving) return tasks

  const sourceColumnId = moving.columnId
  const target = columnTasks(tasks, targetColumnId).filter((t) => t.id !== taskId)
  const clamped = Math.max(0, Math.min(index, target.length))
  target.splice(clamped, 0, { ...moving, columnId: targetColumnId })

  const positions = new Map<string, { columnId: string; position: number }>()
  target.forEach((t, i) => positions.set(t.id, { columnId: targetColumnId, position: i }))
  if (sourceColumnId !== targetColumnId) {
    columnTasks(tasks, sourceColumnId)
      .filter((t) => t.id !== taskId)
      .forEach((t, i) => positions.set(t.id, { columnId: sourceColumnId, position: i }))
  }

  return tasks.map((t) => {
    const next = positions.get(t.id)
    return next ? { ...t, ...next } : t
  })
}

/** Applies authoritative positions pushed by the server. */
export function applyPositions(tasks: TaskCard[], positions: TaskPosition[]): TaskCard[] {
  const byId = new Map(positions.map((p) => [p.taskId, p]))
  return tasks.map((t) => {
    const p = byId.get(t.id)
    return p ? { ...t, columnId: p.columnId, position: p.position, completedAt: p.completedAt } : t
  })
}

/** Inserts a new card or replaces an existing one. */
export function upsertCard(tasks: TaskCard[], card: TaskCard): TaskCard[] {
  return tasks.some((t) => t.id === card.id) ? tasks.map((t) => (t.id === card.id ? card : t)) : [...tasks, card]
}

/** Removes a card and closes the gap in its column. */
export function removeCard(tasks: TaskCard[], taskId: string): TaskCard[] {
  const removed = tasks.find((t) => t.id === taskId)
  if (!removed) return tasks
  return tasks
    .filter((t) => t.id !== taskId)
    .map((t) => (t.columnId === removed.columnId && t.position > removed.position ? { ...t, position: t.position - 1 } : t))
}
