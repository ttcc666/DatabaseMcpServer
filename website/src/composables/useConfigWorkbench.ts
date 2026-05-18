import { computed, ref, shallowRef } from "vue"
import { toast } from "vue-sonner"
import type {
  ApiResponse,
  ConfigContext,
  DashboardResponse,
  DatabaseDetail,
  DatabaseDetailResponse,
  EditorDraft,
  PresetDetailResponse,
  PresetListResponse,
} from "@/types"

function createEmptyDraft(): EditorDraft {
  return {
    mode: "create",
    sourceName: null,
    presetDbType: null,
    maskedConnectionHint: null,
    name: "",
    dbType: "",
    connectionString: "",
    description: "",
    clearDescription: false,
    setDefault: false,
  }
}

async function fetchJson<T>(input: RequestInfo, init?: RequestInit): Promise<T> {
  const response = await fetch(input, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(init?.headers ?? {}),
    },
  })

  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`)
  }

  return await response.json() as T
}

export function useConfigWorkbench() {
  const context = shallowRef<ConfigContext | null>(null)
  const dashboard = shallowRef<DashboardResponse | null>(null)
  const presets = shallowRef<PresetListResponse["presets"]>([])
  const selectedDatabase = shallowRef<DatabaseDetail | null>(null)
  const diagnostics = shallowRef<string | null>(null)
  const lastMessage = shallowRef<string | null>(null)
  const busyAction = shallowRef<string | null>(null)
  const editorDraft = ref<EditorDraft | null>(null)
  const editorOpen = shallowRef(false)
  const deleteTarget = shallowRef<string | null>(null)
  const deleteOpen = shallowRef(false)
  const pendingImportFile = shallowRef<File | null>(null)
  const importOpen = shallowRef(false)

  const hasConfig = computed(() => dashboard.value?.configExists ?? false)
  const hasDatabases = computed(() => (dashboard.value?.databases.length ?? 0) > 0)
  const dbTypeOptions = computed(() => presets.value.map(item => item.dbType))
  const selectedName = computed(() => selectedDatabase.value?.name ?? null)
  const isBootstrapping = computed(() => busyAction.value === "bootstrap")

  async function loadContextAndPresets() {
    const [contextResponse, presetsResponse] = await Promise.all([
      fetchJson<ConfigContext>("/api/context"),
      fetchJson<PresetListResponse>("/api/presets"),
    ])

    context.value = contextResponse
    presets.value = presetsResponse.presets
  }

  async function loadDashboard(preserveSelection = true) {
    const dashboardResponse = await fetchJson<DashboardResponse>("/api/dashboard")
    dashboard.value = dashboardResponse

    if (!dashboardResponse.success) {
      selectedDatabase.value = null
      return
    }

    const candidateName = preserveSelection ? selectedName.value : null
    const fallbackName
      = candidateName && dashboardResponse.databases.some(item => item.name === candidateName)
        ? candidateName
        : dashboardResponse.currentDatabase
          ?? dashboardResponse.currentDefaultDatabase
          ?? dashboardResponse.databases[0]?.name
          ?? null

    if (fallbackName) {
      await selectDatabase(fallbackName)
    }
    else {
      selectedDatabase.value = null
    }
  }

  async function bootstrap() {
    busyAction.value = "bootstrap"
    try {
      await loadContextAndPresets()
      await loadDashboard(false)
    }
    catch (error) {
      notifyError("初始化失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function refresh() {
    busyAction.value = "refresh"
    try {
      await loadContextAndPresets()
      await loadDashboard()
      lastMessage.value = "已刷新配置状态。"
      toast.success("配置状态已刷新")
    }
    catch (error) {
      notifyError("刷新失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function selectDatabase(name: string) {
    try {
      const database = dashboard.value?.databases.find(item => item.name === name)
      if (database) {
        selectedDatabase.value = {
          name: database.name,
          dbType: database.dbType,
          description: database.description ?? null,
          connectionString: database.connectionString,
          isDefault: database.isDefault,
          optimizationSettings: database.optimizationSettings ?? null,
        }
        return
      }

      const response = await fetchJson<DatabaseDetailResponse>(`/api/databases/${encodeURIComponent(name)}`)
      if (!response.success || !response.database) {
        throw new Error(response.message ?? "无法读取数据库详情。")
      }

      selectedDatabase.value = response.database
    }
    catch (error) {
      selectedDatabase.value = null
      notifyError("读取数据库详情失败", error)
    }
  }

  function startCreate() {
    editorDraft.value = createEmptyDraft()
    editorOpen.value = true
  }

  async function startPresetCreate(dbType?: string) {
    const nextDbType = dbType ?? presets.value[0]?.dbType ?? ""
    editorDraft.value = {
      ...createEmptyDraft(),
      mode: "preset",
      dbType: nextDbType,
      presetDbType: nextDbType,
    }
    editorOpen.value = true

    if (nextDbType) {
      await applyPreset(nextDbType)
    }
  }

  async function ensureSelectedDatabase(name?: string) {
    if (!name || selectedDatabase.value?.name === name) {
      return selectedDatabase.value
    }

    await selectDatabase(name)
    return selectedDatabase.value
  }

  async function startEdit(name?: string) {
    const target = await ensureSelectedDatabase(name)
    if (!target) {
      toast.error("请先选中一个数据库连接。")
      return
    }

    editorDraft.value = {
      mode: "edit",
      sourceName: target.name,
      presetDbType: null,
      maskedConnectionHint: target.connectionString,
      name: target.name,
      dbType: target.dbType,
      connectionString: "",
      description: target.description ?? "",
      clearDescription: false,
      setDefault: target.isDefault,
    }
    editorOpen.value = true
  }

  async function startClone(name?: string) {
    const target = await ensureSelectedDatabase(name)
    if (!target) {
      toast.error("请先选中一个数据库连接。")
      return
    }

    editorDraft.value = {
      mode: "clone",
      sourceName: target.name,
      presetDbType: null,
      maskedConnectionHint: target.connectionString,
      name: `${target.name}-copy`,
      dbType: target.dbType,
      connectionString: "",
      description: target.description ?? "",
      clearDescription: false,
      setDefault: false,
    }
    editorOpen.value = true
  }

  function closeEditor() {
    resetEditor()
  }

  function resetEditor() {
    editorDraft.value = null
    editorOpen.value = false
  }

  async function applyPreset(dbType: string) {
    if (!editorDraft.value) {
      return
    }

    busyAction.value = "preset"
    try {
      const response = await fetchJson<PresetDetailResponse>(`/api/presets/${encodeURIComponent(dbType)}`)
      if (!response.success || !response.preset) {
        throw new Error(response.message ?? "未找到模板。")
      }

      editorDraft.value = {
        ...editorDraft.value,
        mode: "preset",
        presetDbType: response.preset.dbType,
        dbType: response.preset.dbType,
        name: response.preset.exampleName,
        connectionString: response.preset.exampleConnectionString,
        description: response.preset.description,
        clearDescription: false,
      }
    }
    catch (error) {
      notifyError("读取模板失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function submitEditor() {
    if (!editorDraft.value) {
      return
    }

    const draft = editorDraft.value
    busyAction.value = `save:${draft.mode}`

    try {
      if (draft.mode === "clone") {
        await postJson<ApiResponse>(`/api/databases/${encodeURIComponent(draft.sourceName ?? "")}/clone`, {
          newName: draft.name,
          setDefault: draft.setDefault,
        })
      }
      else if (draft.mode === "edit") {
        const renamed = draft.sourceName && draft.sourceName !== draft.name
        const targetName = renamed ? draft.name : draft.sourceName ?? draft.name

        if (renamed) {
          await postJson<ApiResponse>(`/api/databases/${encodeURIComponent(draft.sourceName ?? "")}/rename`, {
            newName: draft.name,
          })
        }

        await putJson<ApiResponse>(`/api/databases/${encodeURIComponent(targetName)}`, {
          dbType: draft.dbType,
          connectionString: draft.connectionString,
          description: draft.description,
          clearDescription: draft.clearDescription,
          setDefault: draft.setDefault,
          applyDbType: true,
          applyConnectionString: draft.connectionString.trim().length > 0,
          applyDescription: !draft.clearDescription,
          applyClearDescription: draft.clearDescription,
          applySetDefault: true,
        })
      }
      else if (draft.mode === "preset") {
        await postJson<ApiResponse>("/api/databases/from-preset", {
          dbType: draft.presetDbType ?? draft.dbType,
          name: draft.name,
          connectionString: draft.connectionString,
          description: draft.description,
          setDefault: draft.setDefault,
          printOnly: false,
        })
      }
      else {
        await postJson<ApiResponse>("/api/databases", {
          name: draft.name,
          dbType: draft.dbType,
          connectionString: draft.connectionString,
          description: draft.description,
          setDefault: draft.setDefault,
        })
      }

      lastMessage.value = "配置已保存。"
      toast.success("配置已保存")
      diagnostics.value = null
      resetEditor()
      await loadDashboard(false)
    }
    catch (error) {
      notifyError("保存失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function initializeConfig(force = false) {
    busyAction.value = "init"
    try {
      await postJson<ApiResponse>("/api/config/init", { force })
      await refresh()
      lastMessage.value = force ? "已覆盖并重新初始化配置文件。" : "已初始化配置文件。"
      toast.success(force ? "配置文件已重置" : "配置文件已初始化")
    }
    catch (error) {
      notifyError("初始化失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  function requestDelete(name: string) {
    deleteTarget.value = name
    deleteOpen.value = true
  }

  async function confirmDelete() {
    if (!deleteTarget.value) {
      return
    }

    busyAction.value = "remove"
    try {
      await deleteJson<ApiResponse>(`/api/databases/${encodeURIComponent(deleteTarget.value)}`)
      lastMessage.value = `已删除 '${deleteTarget.value}'。`
      toast.success(`已删除 ${deleteTarget.value}`)
      diagnostics.value = null
      selectedDatabase.value = null
      deleteOpen.value = false
      deleteTarget.value = null
      await loadDashboard(false)
    }
    catch (error) {
      notifyError("删除失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  function cancelDelete() {
    deleteOpen.value = false
    deleteTarget.value = null
  }

  async function setDefault(name: string) {
    busyAction.value = `default:${name}`
    try {
      await postJson<ApiResponse>(`/api/databases/${encodeURIComponent(name)}/set-default`, {})
      lastMessage.value = `默认连接已切换为 '${name}'。`
      toast.success(`默认连接已切换为 ${name}`)
      await loadDashboard()
    }
    catch (error) {
      notifyError("设置默认连接失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function switchCurrent(name: string) {
    busyAction.value = `current:${name}`
    try {
      await postJson<ApiResponse>("/api/current-database/switch", { databaseName: name })
      lastMessage.value = `当前连接已切换为 '${name}'。`
      toast.success(`当前连接已切换为 ${name}`)
      await loadDashboard()
    }
    catch (error) {
      notifyError("切换当前连接失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function testSelected(name: string) {
    busyAction.value = `test:${name}`
    try {
      const response = await fetchJson<ApiResponse>(`/api/databases/${encodeURIComponent(name)}/test`, {
        method: "POST",
        body: JSON.stringify({}),
      })
      diagnostics.value = JSON.stringify(response, null, 2)
      lastMessage.value = response.message ?? `连接 '${name}' 测试完成。`
      response.success ? toast.success(`连接 ${name} 测试完成`) : toast.error(response.message ?? "连接测试失败")
    }
    catch (error) {
      notifyError("连接测试失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function validateConfig() {
    busyAction.value = "validate"
    try {
      const response = await fetchJson<ApiResponse>("/api/config/validate", {
        method: "POST",
        body: JSON.stringify({}),
      })
      diagnostics.value = JSON.stringify(response, null, 2)
      lastMessage.value = response.message ?? "校验已完成。"
      response.success ? toast.success("配置校验通过") : toast.error(response.message ?? "配置校验失败")
    }
    catch (error) {
      notifyError("校验失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function doctorConfig() {
    busyAction.value = "doctor"
    try {
      const response = await fetchJson<ApiResponse>("/api/config/doctor", {
        method: "POST",
        body: JSON.stringify({
          name: selectedName.value,
          testConnections: true,
          fixSuggestions: true,
          summaryOnly: false,
        }),
      })
      diagnostics.value = JSON.stringify(response, null, 2)
      lastMessage.value = response.message ?? "诊断已完成。"
      response.success ? toast.success("诊断已完成") : toast.error(response.message ?? "诊断失败")
    }
    catch (error) {
      notifyError("诊断失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  function exportConfig() {
    busyAction.value = "export"
    window.location.href = "/api/config/export"
    lastMessage.value = "浏览器开始下载当前配置文件。"
    toast.success("开始导出配置文件")
    busyAction.value = null
  }

  function requestImport(file: File | null) {
    if (!file) {
      return
    }

    pendingImportFile.value = file
    importOpen.value = true
  }

  async function confirmImport() {
    if (!pendingImportFile.value) {
      return
    }

    busyAction.value = "import"
    try {
      const formData = new FormData()
      formData.append("file", pendingImportFile.value)
      formData.append("force", "true")

      const response = await fetch("/api/config/import", {
        method: "POST",
        body: formData,
      })

      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`)
      }

      const payload = await response.json() as ApiResponse
      if (!payload.success) {
        throw new Error(payload.message ?? "导入失败。")
      }

      await loadDashboard(false)
      importOpen.value = false
      pendingImportFile.value = null
      lastMessage.value = "配置导入成功。"
      toast.success("配置导入成功")
    }
    catch (error) {
      notifyError("导入失败", error)
    }
    finally {
      busyAction.value = null
    }
  }

  function cancelImport() {
    importOpen.value = false
    pendingImportFile.value = null
  }

  function notifyError(prefix: string, error: unknown) {
    const message = `${prefix}: ${formatError(error)}`
    lastMessage.value = message
    toast.error(message)
  }

  return {
    context,
    dashboard,
    presets,
    selectedDatabase,
    diagnostics,
    lastMessage,
    busyAction,
    editorDraft,
    editorOpen,
    deleteTarget,
    deleteOpen,
    pendingImportFile,
    importOpen,
    hasConfig,
    hasDatabases,
    dbTypeOptions,
    selectedName,
    isBootstrapping,
    bootstrap,
    refresh,
    selectDatabase,
    startCreate,
    startPresetCreate,
    startEdit,
    startClone,
    closeEditor,
    resetEditor,
    applyPreset,
    submitEditor,
    initializeConfig,
    requestDelete,
    confirmDelete,
    cancelDelete,
    setDefault,
    switchCurrent,
    testSelected,
    validateConfig,
    doctorConfig,
    exportConfig,
    requestImport,
    confirmImport,
    cancelImport,
  }
}

async function postJson<T>(url: string, payload: unknown): Promise<T> {
  const response = await fetchJson<T>(url, {
    method: "POST",
    body: JSON.stringify(payload),
  })
  throwIfFailure(response as ApiResponse)
  return response
}

async function putJson<T>(url: string, payload: unknown): Promise<T> {
  const response = await fetchJson<T>(url, {
    method: "PUT",
    body: JSON.stringify(payload),
  })
  throwIfFailure(response as ApiResponse)
  return response
}

async function deleteJson<T>(url: string): Promise<T> {
  const response = await fetchJson<T>(url, {
    method: "DELETE",
  })
  throwIfFailure(response as ApiResponse)
  return response
}

function throwIfFailure(response: ApiResponse) {
  if (!response.success) {
    throw new Error(response.message ?? "请求失败。")
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : String(error)
}
