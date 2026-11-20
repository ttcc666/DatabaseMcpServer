import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import { mount, type VueWrapper } from "@vue/test-utils"
import { defineComponent, h } from "vue"
import { ConfigOperationError, fetchJson, postJson, putJson } from "@/api/http"
import { buildConnectionPayload, useConfigWorkbench } from "@/composables/useConfigWorkbench"
import type { EditorDraft } from "@/types"

vi.mock("@/api/http", async importOriginal => {
  const actual = await importOriginal<typeof import("@/api/http")>()
  return { ...actual, fetchJson: vi.fn(), putJson: vi.fn(), postJson: vi.fn() }
})

describe("configuration edit retry", () => {
  let wrapper: VueWrapper
  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(fetchJson).mockResolvedValue({ success: true, configExists: true, databases: [] })
  })
  afterEach(() => wrapper?.unmount())

  it.each([false, true])("retries using the persisted identity when saved=%s", async (saved) => {
    let workbench!: ReturnType<typeof useConfigWorkbench>
    wrapper = mount(defineComponent({ setup() { workbench = useConfigWorkbench(); return () => h("div") } }))
    const draft = { ...createDraft(), mode: "edit" as const, sourceName: "old", name: "new" }
    vi.mocked(putJson).mockRejectedValueOnce(new ConfigOperationError({ success: false, saved, runtimeApplied: false, message: "failure" }))
    workbench.editorOpen.value = true
    await workbench.submitEditor(draft)
    expect(workbench.editorOpen.value).toBe(true)
    expect(workbench.editorDraft.value?.sourceName).toBe(saved ? "new" : "old")
    expect(postJson).not.toHaveBeenCalled()
    expect(putJson).toHaveBeenNthCalledWith(1, "/api/databases/old", expect.objectContaining({ newName: "new", dbType: "MySql" }))
    vi.mocked(putJson).mockResolvedValueOnce({ success: true })
    await workbench.submitEditor()
    expect(putJson).toHaveBeenNthCalledWith(2, `/api/databases/${saved ? "new" : "old"}`, expect.objectContaining({ newName: "new" }))
    expect(workbench.editorOpen.value).toBe(false)
  })

  it.each([false, true])("keeps a new editor when an earlier save finishes with success=%s", async (success) => {
    let workbench!: ReturnType<typeof useConfigWorkbench>
    wrapper = mount(defineComponent({ setup() { workbench = useConfigWorkbench(); return () => h("div") } }))
    let finish!: () => void
    vi.mocked(putJson).mockImplementationOnce(() => new Promise((resolve, reject) => {
      finish = () => success ? resolve({ success: true })
        : reject(new ConfigOperationError({ success: false, saved: true, databaseName: "new", message: "partial" }))
    }))
    const saving = workbench.submitEditor({ ...createDraft(), mode: "edit", sourceName: "old", name: "new" })
    workbench.startCreate()
    workbench.editorDraft.value!.name = "another"
    finish()
    await saving
    expect(workbench.editorOpen.value).toBe(true)
    expect(workbench.editorDraft.value?.name).toBe("another")
    expect(workbench.editorDraft.value?.sourceName).toBeNull()
  })
})

describe("buildConnectionPayload", () => {
  it("serializes wizard, raw and unchanged modes without mixing contracts", () => {
    const draft = createDraft()
    expect(buildConnectionPayload(draft)).toEqual({ connectionString: null, connectionFields: { Server: "localhost" } })

    draft.connectionMode = "raw"
    draft.connectionString = "Server=raw;"
    expect(buildConnectionPayload(draft)).toEqual({ connectionString: "Server=raw;", connectionFields: null })

    draft.connectionMode = "unchanged"
    expect(buildConnectionPayload(draft)).toEqual({ connectionString: null, connectionFields: null })
  })
})

function createDraft(): EditorDraft {
  return {
    mode: "create",
    name: "main",
    dbType: "MySql",
    connectionMode: "wizard",
    connectionString: "",
    connectionFields: { Server: "localhost" },
    description: "",
    clearDescription: false,
    setDefault: false,
    enableDangerousOperations: false,
  }
}
