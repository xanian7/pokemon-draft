/**
 * Centralized API client.
 *
 * All stores and services should import API_BASE and the fetch helpers from here
 * rather than defining their own base URLs or raw fetch calls.
 */

export const API_BASE = import.meta.env.DEV ? 'http://localhost:5050/api' : '/api'

export type ApiResult<T> = { data: T; error: null } | { data: null; error: string }

async function apiFetch<T>(path: string, init?: RequestInit): Promise<ApiResult<T>> {
  try {
    const res = await fetch(`${API_BASE}${path}`, init)
    if (!res.ok) {
      const text = await res.text().catch(() => '')
      return { data: null, error: text || `Request failed (${res.status})` }
    }
    const contentType = res.headers.get('content-type') ?? ''
    const data = contentType.includes('application/json') ? await res.json() : (undefined as T)
    return { data, error: null }
  } catch {
    return { data: null, error: 'Could not connect to server.' }
  }
}

export function apiGet<T>(path: string, headers?: HeadersInit): Promise<ApiResult<T>> {
  return apiFetch<T>(path, { headers })
}

export function apiPost<T = void>(
  path: string,
  body?: unknown,
  headers?: HeadersInit,
): Promise<ApiResult<T>> {
  return apiFetch<T>(path, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
}

export function apiPatch<T = void>(
  path: string,
  body?: unknown,
  headers?: HeadersInit,
): Promise<ApiResult<T>> {
  return apiFetch<T>(path, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
}

export function apiPut<T = void>(
  path: string,
  body?: unknown,
  headers?: HeadersInit,
): Promise<ApiResult<T>> {
  return apiFetch<T>(path, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
}

export function apiDelete<T = void>(path: string, headers?: HeadersInit): Promise<ApiResult<T>> {
  return apiFetch<T>(path, { method: 'DELETE', headers })
}
