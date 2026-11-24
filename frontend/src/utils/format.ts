import type { TaskPriority } from '@/api/types'

export function initials(name: string): string {
  return name
    .split(' ')
    .filter(Boolean)
    .map((part) => part[0])
    .join('')
    .slice(0, 2)
    .toUpperCase()
}

/** Deterministic avatar colour per person. */
export function avatarColor(id: string): string {
  const palette = ['#7c3aed', '#0891b2', '#16a34a', '#ea580c', '#db2777', '#2563eb', '#ca8a04', '#475569']
  let hash = 0
  for (const char of id) hash = (hash * 31 + char.charCodeAt(0)) | 0
  return palette[Math.abs(hash) % palette.length]!
}

export type DueState = 'overdue' | 'today' | 'soon' | 'later'

/** Classifies a yyyy-MM-dd due date relative to today (local time). */
export function dueState(dueDate: string, today: Date = new Date()): DueState {
  const [y, m, d] = dueDate.split('-').map(Number)
  const due = new Date(y!, m! - 1, d)
  const start = new Date(today.getFullYear(), today.getMonth(), today.getDate())
  const days = Math.round((due.getTime() - start.getTime()) / 86_400_000)
  if (days < 0) return 'overdue'
  if (days === 0) return 'today'
  if (days <= 3) return 'soon'
  return 'later'
}

export function formatDue(dueDate: string): string {
  const [y, m, d] = dueDate.split('-').map(Number)
  return new Date(y!, m! - 1, d).toLocaleDateString('en-CA', { month: 'short', day: 'numeric' })
}

export function timeAgo(iso: string, now: Date = new Date()): string {
  const minutes = Math.round((now.getTime() - new Date(iso).getTime()) / 60_000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours} h ago`
  const days = Math.round(hours / 24)
  return days < 7 ? `${days} day${days === 1 ? '' : 's'} ago` : new Date(iso).toLocaleDateString('en-CA')
}

export const priorityStyle: Record<TaskPriority, string> = {
  Low: 'bg-slate-100 text-slate-600',
  Medium: 'bg-sky-50 text-sky-700',
  High: 'bg-amber-50 text-amber-700',
  Urgent: 'bg-rose-50 text-rose-700',
}
