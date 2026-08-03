import { beforeEach, describe, expect, it, vi } from "vitest"
import { postJson } from "@/api/http"
import { useConnectionHealth } from "@/composables/useConnectionHealth"
import type { ConnectionHealthResponse } from "@/types/connections"

vi.mock("@/api/http", () => ({ postJson: vi.fn() }))
vi.mock("vue-sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }))

const response: ConnectionHealthResponse = {
  success: true,
  overallHealth: false,
  totalConnections: 2,
  healthyConnections: 1,
  unhealthyConnections: 1,
  results: [
    { name: "alpha", dbType: "Sqlite", isHealthy: true, responseTimeMs: 4, errorMessage: "", checkedAt: "2026-08-03T12:00:00Z" },
    { name: "beta", dbType: "MySql", isHealthy: false, responseTimeMs: 20, errorMessage: "offline", checkedAt: "2026-08-03T12:00:01Z" },
  ],
}

describe("useConnectionHealth", () => {
  beforeEach(() => vi.mocked(postJson).mockResolvedValue(response))

  it("normalizes health by exact connection name and removes stale entries", async () => {
    const health = useConnectionHealth()
    await health.checkAll()

    expect(health.resultMap.value.get("beta")?.isHealthy).toBe(false)
    health.prune(["alpha"])
    expect(health.response.value?.totalConnections).toBe(1)
    expect(health.response.value?.overallHealth).toBe(true)
    expect(health.resultMap.value.has("beta")).toBe(false)
  })
})
