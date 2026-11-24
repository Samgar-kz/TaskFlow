import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { useAuthStore } from '@/stores/auth'
import { authApi } from '@/api'
import type { AuthResponse } from '@/api/types'

vi.mock('@/api', () => ({
  authApi: {
    login: vi.fn<() => Promise<AuthResponse>>(),
    register: vi.fn<() => Promise<AuthResponse>>(),
    refresh: vi.fn<() => Promise<AuthResponse>>(),
    logout: vi.fn<() => Promise<void>>(),
  },
}))

const response: AuthResponse = {
  accessToken: 'access',
  expiresAt: '2026-11-15T10:00:00Z',
  user: { id: '1', email: 'alex@example.com', fullName: 'Alex Morgan', role: 'Admin', jobTitle: 'Developer' },
}

describe('auth store', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
  })

  it('stores the token and user after login', async () => {
    vi.mocked(authApi.login).mockResolvedValue(response)
    const auth = useAuthStore()

    await auth.login('alex@example.com', 'secret')

    expect(auth.isAuthenticated).toBe(true)
    expect(auth.accessToken).toBe('access')
    expect(auth.isAdmin).toBe(true)
  })

  it('shares one refresh request between concurrent callers', async () => {
    vi.mocked(authApi.refresh).mockResolvedValue(response)
    const auth = useAuthStore()

    const [a, b] = await Promise.all([auth.refresh(), auth.refresh()])

    expect(a).toBe('access')
    expect(b).toBe('access')
    expect(authApi.refresh).toHaveBeenCalledTimes(1)
  })

  it('clears the session when refresh fails', async () => {
    vi.mocked(authApi.refresh).mockRejectedValue(new Error('401'))
    const auth = useAuthStore()

    await auth.restore()

    expect(auth.isAuthenticated).toBe(false)
    expect(auth.restored).toBe(true)
  })

  it('clears the session on logout even if the request fails', async () => {
    vi.mocked(authApi.login).mockResolvedValue(response)
    vi.mocked(authApi.logout).mockRejectedValue(new Error('network'))
    const auth = useAuthStore()
    await auth.login('alex@example.com', 'secret')

    await expect(auth.logout()).rejects.toThrow('network')

    expect(auth.user).toBeNull()
    expect(auth.accessToken).toBeNull()
  })
})
