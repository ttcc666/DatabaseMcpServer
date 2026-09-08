import { computed, ref, shallowRef } from "vue"
import { useI18n } from "vue-i18n"
import { toast } from "vue-sonner"
import { deleteJson, fetchJson, postJson, putJson } from "@/api/http"
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
    connectionMode: "wizard",
    connectionString: "",
    connectionFields: {},
    description: "",
    clearDescription: false,
    setDefault: false,
    enableDangerousOperations: false,
  }
}

export function useConfigWorkbench() {
  const { t } = useI18n()
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
      notifyError(t("workbench.bootstrapFailed"), error)
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
      lastMessage.value = t("workbench.refreshedMessage")
      toast.success(t("workbench.refreshedToast"))
    }
    catch (error) {
      notifyError(t("workbench.refreshFailed"), error)
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
          enableDangerousOperations: database.enableDangerousOperations,
          optimizationSettings: database.optimizationSettings ?? null,
        }
        return
      }

      const response = await fetchJson<DatabaseDetailResponse>(`/api/databases/${encodeURIComponent(name)}`)
      if (!response.success || !response.database) {
        throw new Error(response.message ?? t("workbench.cannotReadDetail"))
      }

      selectedDatabase.value = response.database
    }
    catch (error) {
      selectedDatabase.value = null
      notifyError(t("workbench.loadDetailFailed"), error)
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
      toast.error(t("workbench.selectFirst"))
      return
    }

    editorDraft.value = {
      mode: "edit",
      sourceName: target.name,
      presetDbType: null,
      maskedConnectionHint: target.connectionString,
      name: target.name,
      dbType: target.dbType,
      connectionMode: "unchanged",
      connectionString: "",
      connectionFields: {},
      description: target.description ?? "",
      clearDescription: false,
      setDefault: target.isDefault,
      enableDangerousOperations: target.enableDangerousOperations,
    }
    editorOpen.value = true
  }

  async function startClone(name?: string) {
    const target = await ensureSelectedDatabase(name)
    if (!target) {
      toast.error(t("workbench.selectFirst"))
      return
    }

    editorDraft.value = {
      mode: "clone",
      sourceName: target.name,
      presetDbType: null,
      maskedConnectionHint: target.connectionString,
      name: `${target.name}-copy`,
      dbType: target.dbType,
      connectionMode: "unchanged",
      connectionString: "",
      connectionFields: {},
      description: target.description ?? "",
      clearDescription: false,
      setDefault: false,
      enableDangerousOperations: target.enableDangerousOperations,
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
        throw new Error(response.message ?? t("workbench.presetNotFound"))
      }

      editorDraft.value = {
        ...editorDraft.value,
        mode: "preset",
        presetDbType: response.preset.dbType,
        dbType: response.preset.dbType,
        name: response.preset.exampleName,
        connectionMode: "wizard",
        connectionString: response.preset.exampleConnectionString,
        connectionFields: {},
        description: response.preset.description,
        clearDescription: false,
      }
    }
    catch (error) {
      notifyError(t("workbench.loadPresetFailed"), error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function submitEditor(submittedDraft?: EditorDraft) {
    if (!submittedDraft && !editorDraft.value) {
      return
    }

    const draft = submittedDraft ?? editorDraft.value!
    editorDraft.value = draft
    busyAction.value = `save:${draft.mode}`

    const connectionPayload = buildConnectionPayload(draft)

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
          ...connectionPayload,
          description: draft.description,
          clearDescription: draft.clearDescription,
          setDefault: draft.setDefault,
          applyDbType: true,
          applyConnectionString: draft.connectionMode !== "unchanged",
          applyDescription: !draft.clearDescription,
          applyClearDescription: draft.clearDescription,
          applySetDefault: true,
          enableDangerousOperations: draft.enableDangerousOperations,
          applyEnableDangerousOperations: true,
        })
      }
      else if (draft.mode === "preset") {
        await postJson<ApiResponse>("/api/databases/from-preset", {
          dbType: draft.presetDbType ?? draft.dbType,
          name: draft.name,
          ...connectionPayload,
          description: draft.description,
          setDefault: draft.setDefault,
          enableDangerousOperations: draft.enableDangerousOperations,
          printOnly: false,
        })
      }
      else {
        await postJson<ApiResponse>("/api/databases", {
          name: draft.name,
          dbType: draft.dbType,
          ...connectionPayload,
          description: draft.description,
          setDefault: draft.setDefault,
          enableDangerousOperations: draft.enableDangerousOperations,
        })
      }

      lastMessage.value = t("workbench.savedMessage")
      toast.success(t("workbench.savedToast"))
      diagnostics.value = null
      resetEditor()
      await loadDashboard(false)
    }
    catch (error) {
      notifyError(t("workbench.saveFailed"), error)
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
      lastMessage.value = force ? t("workbench.initForcedMessage") : t("workbench.initMessage")
      toast.success(force ? t("workbench.initForcedToast") : t("workbench.initToast"))
    }
    catch (error) {
      notifyError(t("workbench.initFailed"), error)
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
      lastMessage.value = t("workbench.deletedMessage", { name: deleteTarget.value })
      toast.success(t("workbench.deletedToast", { name: deleteTarget.value }))
      diagnostics.value = null
      selectedDatabase.value = null
      deleteOpen.value = false
      deleteTarget.value = null
      await loadDashboard(false)
    }
    catch (error) {
      notifyError(t("workbench.deleteFailed"), error)
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
      lastMessage.value = t("workbench.defaultSwitchedMessage", { name })
      toast.success(t("workbench.defaultSwitchedToast", { name }))
      await loadDashboard()
    }
    catch (error) {
      notifyError(t("workbench.setDefaultFailed"), error)
    }
    finally {
      busyAction.value = null
    }
  }

  async function switchCurrent(name: string) {
    busyAction.value = `current:${name}`
    try {
      await postJson<ApiResponse>("/api/current-database/switch", { databaseName: name })
      lastMessage.value = t("workbench.currentSwitchedMessage", { name })
      toast.success(t("workbench.currentSwitchedToast", { name }))
      await loadDashboard()
    }
    catch (error) {
      notifyError(t("workbench.switchCurrentFailed"), error)
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
      lastMessage.value = response.message ?? t("workbench.testCompletedMessage", { name })
      response.success ? toast.success(t("workbench.testCompletedToast", { name })) : toast.error(response.message ?? t("workbench.testFailedToast"))
    }
    catch (error) {
      notifyError(t("workbench.testFailed"), error)
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
      lastMessage.value = response.message ?? t("workbench.validateCompleted")
      response.success ? toast.success(t("workbench.validatePassed")) : toast.error(response.message ?? t("workbench.validateFailedToast"))
    }
    catch (error) {
      notifyError(t("workbench.validateFailed"), error)
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
      lastMessage.value = response.message ?? t("workbench.doctorCompleted")
      response.success ? toast.success(t("workbench.doctorCompletedToast")) : toast.error(response.message ?? t("workbench.doctorFailedToast"))
    }
    catch (error) {
      notifyError(t("workbench.doctorFailed"), error)
    }
    finally {
      busyAction.value = null
    }
  }

  function exportConfig() {
    busyAction.value = "export"
    window.location.href = "/api/config/export"
    lastMessage.value = t("workbench.exportStarted")
    toast.success(t("workbench.exportToast"))
    busyAction.value = null
  }

  function requestImport(file: File | null) {
    if (!file) {
      toast.error(t("workbench.noFileSelected"))
      return
    }

    if (!file.name.toLowerCase().endsWith(".json") && file.type !== "application/json") {
      toast.error(t("workbench.selectJsonFile"))
      return
    }

    pendingImportFile.value = file
    importOpen.value = true
  }

  async function confirmImport() {
    // Capture the file first. AlertDialogAction closes the dialog immediately and
    // that can fire cancelImport(); without a local capture the import would no-op.
    const file = pendingImportFile.value
    if (!file) {
      toast.error(t("workbench.selectImportFile"))
      return
    }

    busyAction.value = "import"
    importOpen.value = false
    try {
      const formData = new FormData()
      formData.append("file", file)
      formData.append("force", "true")

      const response = await fetch("/api/config/import", {
        method: "POST",
        body: formData,
        headers: {
          "X-DatabaseMcp-Web": "1",
        },
      })

      let payload: ApiResponse | null = null
      try {
        payload = await response.json() as ApiResponse
      }
      catch {
        payload = null
      }

      if (!response.ok) {
        throw new Error(payload?.message ?? t("workbench.importFailedHttp", { status: response.status }))
      }

      if (!payload?.success) {
        throw new Error(payload?.message ?? t("workbench.importFailed"))
      }

      await loadContextAndPresets()
      await loadDashboard(false)
      lastMessage.value = payload.message ?? t("workbench.importedMessage", { name: file.name })
      toast.success(payload.message ?? t("workbench.importSuccess"))
    }
    catch (error) {
      notifyError(t("workbench.importError"), error)
    }
    finally {
      pendingImportFile.value = null
      busyAction.value = null
    }
  }

  function cancelImport() {
    // Do not clear the selected file while an import is in flight; dialog close
    // from AlertDialogAction would otherwise cancel the pending operation.
    if (busyAction.value === "import") {
      importOpen.value = false
      return
    }

    importOpen.value = false
    pendingImportFile.value = null
  }

  function notifyError(prefix: string, error: unknown) {
    const message = `${prefix}: ${formatError(error)}`
    lastMessage.value = message
    toast.error(message, {
      duration: 6000,
    })
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

export function buildConnectionPayload(draft: EditorDraft) {
  if (draft.connectionMode === "wizard") {
    return {
      connectionString: null,
      connectionFields: draft.connectionFields,
    }
  }

  if (draft.connectionMode === "raw") {
    return {
      connectionString: draft.connectionString,
      connectionFields: null,
    }
  }

  return {
    connectionString: null,
    connectionFields: null,
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : String(error)
}
