import { afterEach, describe, expect, it, vi } from "vitest"
import { ConfigOperationError, deleteJson, fetchJson, postJson } from "@/api/http"

afterEach(() => vi.unstubAllGlobals())

describe("local web writes", () => {
  it("marks bodyless DELETE and multipart POST without forcing JSON", async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response('{"success":true}')))
    vi.stubGlobal("fetch", fetchMock)
    await deleteJson("/api/databases/example")
    await fetchJson("/api/config/import", { method: "POST", body: new FormData() })
    for (const [, request] of fetchMock.mock.calls) {
      expect(request.headers.get("X-DatabaseMcp-Web")).toBe("1")
      expect(request.headers.has("Content-Type")).toBe(false)
    }
  })

  it("retains partial-save details instead of treating a saved file as applied", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(new Response(JSON.stringify({
      success: false, saved: true, runtimeApplied: false, message: "已保存但未生效",
    }))))
    await expect(postJson("/api/config/init", {})).rejects.toMatchObject({
      message: "已保存但未生效", response: { saved: true, runtimeApplied: false },
    })
    expect(new ConfigOperationError({ success: false })).toBeInstanceOf(Error)
  })
})
