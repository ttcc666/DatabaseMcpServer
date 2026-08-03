import type { ApiResponse } from "@/types"

export class ApiError extends Error {
  readonly status: number

  constructor(message: string, status: number) {
    super(message)
    this.name = "ApiError"
    this.status = status
  }
}

export async function fetchJson<T>(input: RequestInfo | URL, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers)
  const method = (init.method ?? "GET").toUpperCase()
  const hasBody = init.body !== undefined && init.body !== null

  if (hasBody && !(init.body instanceof FormData) && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json")
  }

  if (method !== "GET" && method !== "HEAD" && headers.get("Content-Type")?.includes("application/json")) {
    headers.set("X-DatabaseMcp-Web", "1")
  }

  const response = await fetch(input, { ...init, headers })
  if (!response.ok) {
    throw new ApiError(await readErrorMessage(response), response.status)
  }

  return await response.json() as T
}

export async function postJson<T>(url: string, payload: unknown, signal?: AbortSignal): Promise<T> {
  const response = await fetchJson<T>(url, {
    method: "POST",
    body: JSON.stringify(payload),
    signal,
  })
  throwIfFailure(response as ApiResponse)
  return response
}

export async function putJson<T>(url: string, payload: unknown): Promise<T> {
  const response = await fetchJson<T>(url, {
    method: "PUT",
    body: JSON.stringify(payload),
  })
  throwIfFailure(response as ApiResponse)
  return response
}

export async function deleteJson<T>(url: string): Promise<T> {
  const response = await fetchJson<T>(url, { method: "DELETE" })
  throwIfFailure(response as ApiResponse)
  return response
}

export function throwIfFailure(response: ApiResponse) {
  if (!response.success) {
    throw new Error(response.message ?? "请求失败。")
  }
}

async function readErrorMessage(response: Response) {
  try {
    const payload = await response.json() as ApiResponse
    return payload.message ?? `HTTP ${response.status}`
  }
  catch {
    return `HTTP ${response.status}`
  }
}
