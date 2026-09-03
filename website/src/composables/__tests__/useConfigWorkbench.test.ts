import { describe, expect, it } from "vitest"
import { buildConnectionPayload } from "@/composables/useConfigWorkbench"
import type { EditorDraft } from "@/types"

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
