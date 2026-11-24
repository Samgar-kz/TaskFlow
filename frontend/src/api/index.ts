import { del, get, post, put } from './http'
import type { AuthResponse, Board, Comment, MyTask, ProjectSummary, SaveTask, TaskCard, TaskDetail, TaskPosition, User } from './types'

export const authApi = {
  login: (email: string, password: string) => post<AuthResponse>('/auth/login', { email, password }),
  register: (body: { email: string; password: string; fullName: string; jobTitle: string }) =>
    post<AuthResponse>('/auth/register', body),
  refresh: () => post<AuthResponse>('/auth/refresh'),
  logout: () => post<void>('/auth/logout'),
  me: () => get<User>('/auth/me'),
}

export const projectsApi = {
  list: () => get<ProjectSummary[]>('/projects'),
  create: (body: { name: string; key: string; description: string | null }) => post<Board>('/projects', body),
  board: (id: string) => get<Board>(`/projects/${id}`),
  update: (id: string, body: { name: string; description: string | null }) => put<Board>(`/projects/${id}`, body),
  remove: (id: string) => del(`/projects/${id}`),
  addMember: (id: string, email: string) => post<Board>(`/projects/${id}/members`, { email }),
  removeMember: (id: string, userId: string) => del(`/projects/${id}/members/${userId}`),
  addColumn: (id: string, body: { name: string; wipLimit: number | null }) => post<Board>(`/projects/${id}/columns`, body),
  updateColumn: (id: string, columnId: string, body: { name: string; wipLimit: number | null }) =>
    put<Board>(`/projects/${id}/columns/${columnId}`, body),
  removeColumn: (id: string, columnId: string) => del(`/projects/${id}/columns/${columnId}`),
  createTask: (id: string, body: SaveTask) => post<TaskCard>(`/projects/${id}/tasks`, body),
}

export const tasksApi = {
  mine: () => get<MyTask[]>('/tasks/mine'),
  get: (id: string) => get<TaskDetail>(`/tasks/${id}`),
  update: (id: string, body: SaveTask) => put<TaskCard>(`/tasks/${id}`, body),
  move: (id: string, columnId: string, index: number) => post<TaskPosition[]>(`/tasks/${id}/move`, { columnId, index }),
  remove: (id: string) => del(`/tasks/${id}`),
  comment: (id: string, body: string) => post<Comment>(`/tasks/${id}/comments`, { body }),
}
