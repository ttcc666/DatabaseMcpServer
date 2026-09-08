import { describe, expect, it } from "vitest"
import { ref } from "vue"
import { getStatusRank, useConnectionTable } from "@/composables/useConnectionTable"
import type { DatabaseSummary } from "@/types"
import type { ConnectionHealthResult, ConnectionTableItem } from "@/types/connections"

const connections: DatabaseSummary[] = [
  { name: "zeta", dbType: "Sqlite", description: "local", connectionString: "masked", isDefault: false, enableDangerousOperations: false, isCurrent: false },
  { name: "alpha", dbType: "MySql", description: "analytics", connectionString: "masked", isDefault: true, enableDangerousOperations: false, isCurrent: false },
  { name: "beta", dbType: "MySql", description: "primary", connectionString: "masked", isDefault: false, enableDangerousOperations: true, isCurrent: true },
]

describe("useConnectionTable", () => {
  it("filters normalized text, type and status, then resets", () => {
    const health = new Map<string, ConnectionHealthResult>([
      ["alpha", healthResult("alpha", true, 8)],
      ["beta", healthResult("beta", false, 15)],
    ])
    const table = useConnectionTable(ref(connections), ref(health))

    expect(table.rows.value.map(item => item.name)).toEqual(["alpha", "beta", "zeta"])
    table.query.value = "  ANALYTICS "
    expect(table.rows.value.map(item => item.name)).toEqual(["alpha"])
    table.query.value = ""
    table.typeFilter.value = "MySql"
    table.statusFilter.value = "unhealthy"
    expect(table.rows.value.map(item => item.name)).toEqual(["beta"])

    table.resetFilters()
    expect(table.rows.value).toHaveLength(3)
  })

  it("uses the documented status precedence", () => {
    const items: ConnectionTableItem[] = [
      { ...connections[0]!, health: healthResult("zeta", false, 1) },
      { ...connections[0]!, health: healthResult("zeta", true, 1) },
      { ...connections[0]!, isCurrent: true },
      { ...connections[0]!, isDefault: true },
      { ...connections[0]!, enableDangerousOperations: true },
      { ...connections[0]! },
    ]

    expect(items.map(getStatusRank)).toEqual([0, 1, 2, 3, 4, 5])
  })
})

function healthResult(name: string, isHealthy: boolean, responseTimeMs: number): ConnectionHealthResult {
  return { name, dbType: "Sqlite", isHealthy, responseTimeMs, errorMessage: isHealthy ? "" : "offline", checkedAt: "2026-08-03T12:00:00Z" }
}
