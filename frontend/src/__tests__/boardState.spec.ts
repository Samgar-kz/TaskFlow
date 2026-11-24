import { describe, expect, it } from 'vitest'
import type { TaskCard } from '@/api/types'
import { applyMove, applyPositions, columnTasks, removeCard, upsertCard } from '@/utils/boardState'
import { dueState } from '@/utils/format'

const card = (id: string, columnId: string, position: number): TaskCard => ({
  id,
  key: `WEB-${id}`,
  title: id,
  columnId,
  position,
  priority: 'Medium',
  assigneeId: null,
  dueDate: null,
  commentCount: 0,
  completedAt: null,
  createdAt: '2026-11-01T00:00:00Z',
})

const board = () => [card('a', 'todo', 0), card('b', 'todo', 1), card('c', 'todo', 2), card('x', 'doing', 0)]
const titles = (tasks: TaskCard[], column: string) => columnTasks(tasks, column).map((t) => t.title)

describe('applyMove', () => {
  it('reorders within a column', () => {
    const result = applyMove(board(), 'c', 'todo', 0)
    expect(titles(result, 'todo')).toEqual(['c', 'a', 'b'])
    expect(columnTasks(result, 'todo').map((t) => t.position)).toEqual([0, 1, 2])
  })

  it('moves across columns and closes the gap in the source', () => {
    const result = applyMove(board(), 'a', 'doing', 1)
    expect(titles(result, 'doing')).toEqual(['x', 'a'])
    expect(titles(result, 'todo')).toEqual(['b', 'c'])
    expect(columnTasks(result, 'todo').map((t) => t.position)).toEqual([0, 1])
  })

  it('clamps an out-of-range index to the end', () => {
    expect(titles(applyMove(board(), 'a', 'doing', 50), 'doing')).toEqual(['x', 'a'])
  })

  it('does not mutate the original array', () => {
    const original = board()
    applyMove(original, 'a', 'doing', 0)
    expect(original[0]!.columnId).toBe('todo')
  })
})

describe('server events', () => {
  it('applies authoritative positions', () => {
    const result = applyPositions(board(), [{ taskId: 'b', columnId: 'done', position: 0, completedAt: '2026-11-15T10:00:00Z' }])
    expect(result.find((t) => t.id === 'b')).toMatchObject({ columnId: 'done', completedAt: '2026-11-15T10:00:00Z' })
  })

  it('upserts new and existing cards', () => {
    const added = upsertCard(board(), card('d', 'todo', 3))
    expect(added).toHaveLength(5)
    const renamed = upsertCard(added, { ...card('d', 'todo', 3), title: 'renamed' })
    expect(renamed.find((t) => t.id === 'd')!.title).toBe('renamed')
    expect(renamed).toHaveLength(5)
  })

  it('removes a card and renumbers its column', () => {
    const result = removeCard(board(), 'a')
    expect(columnTasks(result, 'todo').map((t) => [t.title, t.position])).toEqual([
      ['b', 0],
      ['c', 1],
    ])
  })
})

describe('dueState', () => {
  const today = new Date(2026, 10, 15)
  it('classifies due dates', () => {
    expect(dueState('2026-11-14', today)).toBe('overdue')
    expect(dueState('2026-11-15', today)).toBe('today')
    expect(dueState('2026-11-17', today)).toBe('soon')
    expect(dueState('2026-12-01', today)).toBe('later')
  })
})
