/** Error raised for non-2xx responses, built from RFC 9457 problem details when available. */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
    public readonly fieldErrors: Record<string, string[]> = {},
  ) {
    super(message)
    this.name = 'ApiError'
  }

  /** First validation message for a field, if any. */
  field(name: string): string | undefined {
    return this.fieldErrors[name]?.[0]
  }
}

interface TokenProvider {
  getToken: () => string | null
  /** Obtains a fresh access token (via the refresh cookie). Returns null when the session is gone. */
  refresh: () => Promise<string | null>
  onSessionExpired: () => void
}

let tokens: TokenProvider = {
  getToken: () => null,
  refresh: async () => null,
  onSessionExpired: () => {},
}

export function configureAuth(provider: TokenProvider) {
  tokens = provider
}

type QueryValue = string | number | boolean | null | undefined

export function toQuery(params: object = {}): string {
  const search = new URLSearchParams()
  for (const [key, value] of Object.entries(params) as [string, QueryValue][]) {
    if (value !== undefined && value !== null && value !== '') {
      search.append(key, String(value))
    }
  }
  const query = search.toString()
  return query ? `?${query}` : ''
}

async function toApiError(response: Response): Promise<ApiError> {
  let message = response.statusText || 'Request failed'
  let errors: Record<string, string[]> = {}
  try {
    const problem = await response.json()
    message = problem.detail ?? problem.title ?? message
    errors = problem.errors ?? {}
  } catch {
    // Body was not JSON; keep the status text.
  }
  if (response.status === 429) {
    message = 'Too many attempts. Please wait a minute and try again.'
  }
  return new ApiError(response.status, message, errors)
}

/**
 * Sends a request to the API. Adds the bearer token and, if the token has expired,
 * refreshes it once and retries the request transparently.
 */
export async function request(path: string, init: RequestInit = {}, retry = true): Promise<Response> {
  const headers = new Headers(init.headers)
  const token = tokens.getToken()
  if (token) {
    headers.set('Authorization', `Bearer ${token}`)
  }
  if (init.body && !(init.body instanceof FormData) && !headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json')
  }

  const response = await fetch(`/api${path}`, { ...init, headers, credentials: 'same-origin' })

  if (response.status === 401 && retry && token && !path.startsWith('/auth/')) {
    const refreshed = await tokens.refresh()
    if (refreshed) {
      return request(path, init, false)
    }
    tokens.onSessionExpired()
  }

  if (!response.ok) {
    throw await toApiError(response)
  }
  return response
}

export async function http<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await request(path, init)
  if (response.status === 204) {
    return undefined as T
  }
  return (await response.json()) as T
}

export const get = <T>(path: string) => http<T>(path)
export const post = <T>(path: string, body?: unknown) =>
  http<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
export const put = <T>(path: string, body: unknown) => http<T>(path, { method: 'PUT', body: JSON.stringify(body) })
export const del = (path: string) => http<void>(path, { method: 'DELETE' })
