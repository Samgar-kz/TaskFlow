// Types mirroring the TaskFlow API contracts (enums are serialised as strings).

export type ProjectRole = 'Owner' | 'Member'
export type TaskPriority = 'Low' | 'Medium' | 'High' | 'Urgent'

export const PRIORITIES: TaskPriority[] = ['Low', 'Medium', 'High', 'Urgent']

export interface User {
  id: string
  email: string
  fullName: string
  jobTitle: string
  role: 'Member' | 'Admin'
}

export interface AuthResponse {
  accessToken: string
  expiresAt: string
  user: User
}

export interface Member {
  userId: string
  fullName: string
  email: string
  jobTitle: string
  role: ProjectRole
}

export interface Column {
  id: string
  name: string
  position: number
  wipLimit: number | null
}

export interface TaskCard {
  id: string
  key: string
  title: string
  columnId: string
  position: number
  priority: TaskPriority
  assigneeId: string | null
  dueDate: string | null
  commentCount: number
  completedAt: string | null
  createdAt: string
}

export interface Board {
  id: string
  name: string
  key: string
  description: string | null
  ownerId: string
  myRole: ProjectRole
  columns: Column[]
  members: Member[]
  tasks: TaskCard[]
}

export interface ProjectSummary {
  id: string
  name: string
  key: string
  description: string | null
  myRole: ProjectRole
  memberCount: number
  openTasks: number
  doneTasks: number
  createdAt: string
}

export interface Comment {
  id: string
  authorId: string
  authorName: string
  body: string
  createdAt: string
}

export interface TaskDetail {
  card: TaskCard
  description: string | null
  createdById: string
  comments: Comment[]
}

export interface TaskPosition {
  taskId: string
  columnId: string
  position: number
  completedAt: string | null
}

export interface MyTask {
  card: TaskCard
  projectId: string
  projectName: string
  columnName: string
}

export interface SaveTask {
  title: string
  description: string | null
  priority: TaskPriority
  assigneeId: string | null
  dueDate: string | null
  columnId?: string | null
}
