import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { mount, type VueWrapper } from "@vue/test-utils"
import { defineComponent, h } from "vue"
import { fetchJson, postJson } from "@/api/http"
import { useToolPlayground } from "@/composables/useToolPlayground"
import type { ToolCatalogResponse, ToolInvocationResponse } from "@/types/tools"

vi.mock("@/api/http", () => ({ fetchJson: vi.fn(), postJson: vi.fn() }))

const catalog: ToolCatalogResponse = {
  success: true,
  tools: [
    { name: "query_rows", description: "query", category: "query", requiresConfirmation: false, parameters: [
      { name: "options", optionName: "options", description: "JSON options", type: "json", required: true, defaultValue: null },
    ] },
    { name: "drop_table", description: "drop", category: "schema", requiresConfirmation: true, parameters: [
      { name: "tableName", optionName: "table-name", description: "table", type: "string", required: true, defaultValue: null },
    ] },
  ],
}

describe("useToolPlayground", () => {
  let wrapper: VueWrapper

  beforeEach(() => {
    vi.mocked(fetchJson).mockResolvedValue(catalog)
    vi.mocked(postJson).mockResolvedValue({ success: true, toolName: "query_rows", durationMs: 3, toolSuccess: true, result: {} } satisfies ToolInvocationResponse)
  })
  afterEach(() => wrapper.unmount())

  it("filters risk correctly and parses JSON arguments before invocation", async () => {
    const playground = createPlayground()
    await playground.loadTools()

    playground.risk.value = "safe"
    expect(playground.filteredTools.value.map(tool => tool.name)).toEqual(["query_rows"])
    playground.risk.value = "protected"
    expect(playground.filteredTools.value.map(tool => tool.name)).toEqual(["drop_table"])

    playground.selectTool("query_rows")
    playground.argumentsState.options = "{\"limit\": 5}"
    await playground.requestInvocation()
    expect(postJson).toHaveBeenCalledWith(
      "/api/tools/query_rows/invoke",
      { arguments: { options: { limit: 5 } }, confirmation: null },
      expect.any(AbortSignal),
    )
  })

  it("pauses protected tools until an explicit confirmation is supplied", async () => {
    const playground = createPlayground()
    await playground.loadTools()
    playground.selectTool("drop_table")
    playground.argumentsState["table-name"] = "temp_table"

    await playground.requestInvocation()
    expect(playground.pendingConfirmation.value).toBe(true)
    expect(postJson).not.toHaveBeenCalled()

    await playground.confirmInvocation("drop_table")
    expect(postJson).toHaveBeenCalledWith(
      "/api/tools/drop_table/invoke",
      { arguments: { "table-name": "temp_table" }, confirmation: "drop_table" },
      expect.any(AbortSignal),
    )
  })

  function createPlayground() {
    let playground!: ReturnType<typeof useToolPlayground>
    wrapper = mount(defineComponent({
      setup() {
        playground = useToolPlayground()
        return () => h("div")
      },
    }))
    return playground
  }
})
