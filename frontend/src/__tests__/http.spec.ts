import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError, configureAuth, get, toQuery } from '@/api/http'

function json(status: number, body: unknown) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

describe('http client', () => {
  const fetchMock = vi.fn<typeof fetch>()

  beforeEach(() => {
    vi.stubGlobal('fetch', fetchMock)
  })

  afterEach(() => {
    fetchMock.mockReset()
    vi.unstubAllGlobals()
  })

  it('sends the bearer token', async () => {
    configureAuth({ getToken: () => 'token-1', refresh: async () => null, onSessionExpired: () => {} })
    fetchMock.mockResolvedValueOnce(json(200, { ok: true }))

    await get('/accounts')

    const [url, init] = fetchMock.mock.calls[0]!
    expect(url).toBe('/api/accounts')
    expect(new Headers(init?.headers).get('Authorization')).toBe('Bearer token-1')
  })

  it('refreshes an expired token once and retries the request', async () => {
    let token = 'expired'
    const refresh = vi.fn<() => Promise<string | null>>(async () => {
      token = 'fresh'
      return token
    })
    configureAuth({ getToken: () => token, refresh, onSessionExpired: () => {} })
    fetchMock.mockResolvedValueOnce(new Response(null, { status: 401 })).mockResolvedValueOnce(json(200, [1, 2]))

    const result = await get<number[]>('/accounts')

    expect(result).toEqual([1, 2])
    expect(refresh).toHaveBeenCalledTimes(1)
    expect(new Headers(fetchMock.mock.calls[1]![1]?.headers).get('Authorization')).toBe('Bearer fresh')
  })

  it('reports an expired session when the refresh fails', async () => {
    const onSessionExpired = vi.fn<() => void>()
    configureAuth({ getToken: () => 'expired', refresh: async () => null, onSessionExpired })
    fetchMock.mockResolvedValueOnce(json(401, { title: 'Unauthorized' }))

    await expect(get('/accounts')).rejects.toBeInstanceOf(ApiError)
    expect(onSessionExpired).toHaveBeenCalledOnce()
  })

  it('turns validation problem details into field errors', async () => {
    configureAuth({ getToken: () => null, refresh: async () => null, onSessionExpired: () => {} })
    fetchMock.mockResolvedValueOnce(
      json(400, { title: 'Validation failed', detail: 'One or more fields are invalid.', errors: { amount: ['Too small'] } }),
    )

    const error = (await get('/transactions').catch((e: unknown) => e)) as ApiError

    expect(error.status).toBe(400)
    expect(error.message).toBe('One or more fields are invalid.')
    expect(error.field('amount')).toBe('Too small')
  })

  it('builds query strings without empty values', () => {
    expect(toQuery({ a: 1, b: '', c: undefined, d: null, e: 'x y' })).toBe('?a=1&e=x+y')
    expect(toQuery({})).toBe('')
  })
})
