import { ref } from 'vue'
import { defineStore } from 'pinia'
import { ApiError } from '@/api/http'

export interface Toast {
  id: number
  kind: 'success' | 'error'
  message: string
}

export const useToastStore = defineStore('toast', () => {
  const toasts = ref<Toast[]>([])
  let nextId = 1

  function push(kind: Toast['kind'], message: string) {
    const id = nextId++
    toasts.value.push({ id, kind, message })
    setTimeout(() => dismiss(id), kind === 'error' ? 6000 : 3500)
  }

  function dismiss(id: number) {
    toasts.value = toasts.value.filter((t) => t.id !== id)
  }

  const success = (message: string) => push('success', message)
  const error = (err: unknown) =>
    push('error', err instanceof ApiError || err instanceof Error ? err.message : 'Something went wrong.')

  return { toasts, success, error, dismiss }
})
