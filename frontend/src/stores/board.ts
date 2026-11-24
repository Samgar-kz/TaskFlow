import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { projectsApi, tasksApi } from '@/api'
import type { Board, Comment, TaskCard, TaskPosition } from '@/api/types'
import { useAuthStore } from '@/stores/auth'
import { applyMove, applyPositions, removeCard, upsertCard } from '@/utils/boardState'

export type LiveStatus = 'offline' | 'connecting' | 'live' | 'reconnecting'

/**
 * The open board and its real-time connection. Local changes are applied optimistically; the
 * SignalR hub pushes everyone else's changes, so all viewers stay in sync without refreshing.
 */
export const useBoardStore = defineStore('board', () => {
  const board = ref<Board | null>(null)
  const status = ref<LiveStatus>('offline')
  const lastComment = ref<{ taskId: string; comment: Comment } | null>(null)
  let connection: HubConnection | null = null
  let joinedProjectId: string | null = null

  const membersById = computed(() => new Map(board.value?.members.map((m) => [m.userId, m]) ?? []))
  const columns = computed(() => [...(board.value?.columns ?? [])].sort((a, b) => a.position - b.position))

  function setTasks(update: (tasks: TaskCard[]) => TaskCard[]) {
    if (board.value) board.value = { ...board.value, tasks: update(board.value.tasks) }
  }

  async function load(projectId: string) {
    board.value = await projectsApi.board(projectId)
    await connect(projectId)
  }

  async function reload() {
    if (board.value) board.value = await projectsApi.board(board.value.id)
  }

  async function connect(projectId: string) {
    const auth = useAuthStore()
    if (!connection) {
      connection = new HubConnectionBuilder()
        .withUrl('/hubs/board', { accessTokenFactory: async () => auth.accessToken ?? (await auth.refresh()) ?? '' })
        .withAutomaticReconnect()
        .configureLogging(LogLevel.Warning)
        .build()

      connection.on('TaskUpserted', (card: TaskCard) => setTasks((tasks) => upsertCard(tasks, card)))
      connection.on('TasksMoved', (positions: TaskPosition[]) => setTasks((tasks) => applyPositions(tasks, positions)))
      connection.on('TaskDeleted', (taskId: string) => setTasks((tasks) => removeCard(tasks, taskId)))
      connection.on('CommentAdded', (taskId: string, comment: Comment) => {
        setTasks((tasks) => tasks.map((t) => (t.id === taskId ? { ...t, commentCount: t.commentCount + 1 } : t)))
        lastComment.value = { taskId, comment }
      })
      connection.on('BoardChanged', () => void reload().catch(() => (board.value = null)))
      connection.onreconnecting(() => (status.value = 'reconnecting'))
      connection.onreconnected(async () => {
        status.value = 'live'
        if (joinedProjectId) {
          await connection!.invoke('JoinProject', joinedProjectId)
          await reload()
        }
      })
      connection.onclose(() => (status.value = 'offline'))
    }

    try {
      if (connection.state === HubConnectionState.Disconnected) {
        status.value = 'connecting'
        await connection.start()
      }
      if (joinedProjectId && joinedProjectId !== projectId) {
        await connection.invoke('LeaveProject', joinedProjectId)
      }
      await connection.invoke('JoinProject', projectId)
      joinedProjectId = projectId
      status.value = 'live'
    } catch {
      status.value = 'offline'
    }
  }

  async function disconnect() {
    joinedProjectId = null
    board.value = null
    if (connection) {
      await connection.stop()
      connection = null
    }
    status.value = 'offline'
  }

  /** Moves a card optimistically, then reconciles with the server (or reloads if it refused). */
  async function moveTask(taskId: string, columnId: string, index: number) {
    const previous = board.value?.tasks ?? []
    setTasks((tasks) => applyMove(tasks, taskId, columnId, index))
    try {
      const positions = await tasksApi.move(taskId, columnId, index)
      setTasks((tasks) => applyPositions(tasks, positions))
    } catch (err) {
      setTasks(() => previous)
      throw err
    }
  }

  function upsert(card: TaskCard) {
    setTasks((tasks) => upsertCard(tasks, card))
  }

  function remove(taskId: string) {
    setTasks((tasks) => removeCard(tasks, taskId))
  }

  return { board, status, lastComment, membersById, columns, load, reload, disconnect, moveTask, upsert, remove }
})
