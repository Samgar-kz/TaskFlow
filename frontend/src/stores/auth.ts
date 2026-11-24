import { computed, ref } from 'vue'
import { defineStore } from 'pinia'
import { authApi } from '@/api'
import { configureAuth } from '@/api/http'
import type { AuthResponse, User } from '@/api/types'

// Non-secret flag that only says "a refresh cookie probably exists", so anonymous visitors
// don't trigger a pointless (401) refresh call on every page load.
const SESSION_HINT_KEY = 'taskflow.session'

function hasSessionHint(): boolean {
  try {
    return window.localStorage.getItem(SESSION_HINT_KEY) === '1'
  } catch {
    return true
  }
}

function setSessionHint(active: boolean) {
  try {
    if (active) window.localStorage.setItem(SESSION_HINT_KEY, '1')
    else window.localStorage.removeItem(SESSION_HINT_KEY)
  } catch {
    // Storage unavailable (private mode, tests): the refresh call simply always runs.
  }
}

/**
 * Session state. The access token lives only in memory (never localStorage), so it cannot be
 * stolen from storage by injected scripts. After a page reload the session is restored from the
 * HttpOnly refresh cookie.
 */
export const useAuthStore = defineStore('auth', () => {
  const accessToken = ref<string | null>(null)
  const user = ref<User | null>(null)
  const restored = ref(false)
  let refreshInFlight: Promise<string | null> | null = null

  const isAuthenticated = computed(() => accessToken.value !== null)
  const isAdmin = computed(() => user.value?.role === 'Admin')

  function apply(response: AuthResponse) {
    accessToken.value = response.accessToken
    user.value = response.user
    setSessionHint(true)
  }

  function clear() {
    accessToken.value = null
    user.value = null
    setSessionHint(false)
  }

  async function login(email: string, password: string) {
    apply(await authApi.login(email, password))
  }

  async function register(payload: { email: string; password: string; fullName: string; jobTitle: string }) {
    apply(await authApi.register(payload))
  }

  /** Exchanges the refresh cookie for a new access token. Concurrent callers share one request. */
  function refresh(): Promise<string | null> {
    refreshInFlight ??= authApi
      .refresh()
      .then((response) => {
        apply(response)
        return response.accessToken
      })
      .catch(() => {
        clear()
        return null
      })
      .finally(() => {
        refreshInFlight = null
      })
    return refreshInFlight
  }

  /** Restores the session after a page load. Skipped for visitors who never signed in. */
  async function restore() {
    if (!restored.value) {
      if (hasSessionHint()) await refresh()
      restored.value = true
    }
  }

  async function logout() {
    try {
      await authApi.logout()
    } finally {
      clear()
    }
  }

  function setUser(updated: User) {
    user.value = updated
  }

  return { accessToken, user, restored, isAuthenticated, isAdmin, login, register, refresh, restore, logout, setUser, clear }
})

/** Wires the HTTP client to the auth store (token injection and silent refresh). */
export function installAuth(onSessionExpired: () => void) {
  const auth = useAuthStore()
  configureAuth({
    getToken: () => auth.accessToken,
    refresh: () => auth.refresh(),
    onSessionExpired: () => {
      auth.clear()
      onSessionExpired()
    },
  })
}
