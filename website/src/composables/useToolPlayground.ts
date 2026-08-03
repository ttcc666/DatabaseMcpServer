import { computed, onUnmounted, reactive, readonly, shallowRef } from "vue"
import { fetchJson, postJson } from "@/api/http"
import type {
  ToolArgumentValue,
  ToolCatalogResponse,
  ToolCategory,
  ToolInvocationResponse,
  ToolMetadata,
} from "@/types/tools"

export function useToolPlayground() {
  const tools = shallowRef<ToolMetadata[]>([])
  const selectedName = shallowRef<string | null>(null)
  const query = shallowRef("")
  const category = shallowRef<ToolCategory>("all")
  const risk = shallowRef<"all" | "safe" | "protected">("all")
  const argumentsState = reactive<Record<string, ToolArgumentValue>>({})
  const errors = reactive<Record<string, string>>({})
  const result = shallowRef<ToolInvocationResponse | null>(null)
  const isLoadingCatalog = shallowRef(false)
  const isInvoking = shallowRef(false)
  const pendingConfirmation = shallowRef(false)
  const requestError = shallowRef<string | null>(null)
  let controller: AbortController | null = null

  const selectedTool = computed(() => tools.value.find(tool => tool.name === selectedName.value) ?? null)
  const filteredTools = computed(() => {
    const needle = query.value.trim().toLocaleLowerCase()
    return tools.value.filter(tool => {
      const matchesQuery = !needle || `${tool.name} ${tool.description}`.toLocaleLowerCase().includes(needle)
      const matchesCategory = category.value === "all" || tool.category === category.value
      const matchesRisk
        = risk.value === "all"
          || risk.value === "protected" && tool.requiresConfirmation
          || risk.value === "safe" && !tool.requiresConfirmation
      return matchesQuery && matchesCategory && matchesRisk
    })
  })
  const formattedResult = computed(() => result.value ? JSON.stringify(result.value.result, null, 2) : "")

  async function loadTools() {
    isLoadingCatalog.value = true
    requestError.value = null
    try {
      const response = await fetchJson<ToolCatalogResponse>("/api/tools")
      tools.value = response.tools
      if (!selectedName.value && response.tools.length > 0) {
        selectTool(response.tools[0]!.name)
      }
    }
    catch (error) {
      requestError.value = formatError(error)
    }
    finally {
      isLoadingCatalog.value = false
    }
  }

  function selectTool(name: string) {
    selectedName.value = name
    result.value = null
    requestError.value = null
    pendingConfirmation.value = false
    clearRecord(argumentsState)
    clearRecord(errors)

    const tool = tools.value.find(item => item.name === name)
    for (const parameter of tool?.parameters ?? []) {
      if (parameter.type === "bool") {
        argumentsState[parameter.optionName] = typeof parameter.defaultValue === "boolean" ? parameter.defaultValue : false
      }
      else if (parameter.type === "int") {
        argumentsState[parameter.optionName] = typeof parameter.defaultValue === "number" ? parameter.defaultValue : ""
      }
      else if (parameter.type === "json") {
        argumentsState[parameter.optionName] = parameter.defaultValue == null ? "" : JSON.stringify(parameter.defaultValue, null, 2)
      }
      else {
        argumentsState[parameter.optionName] = typeof parameter.defaultValue === "string" ? parameter.defaultValue : ""
      }
    }
  }

  async function requestInvocation() {
    if (!validate()) {
      return
    }

    if (selectedTool.value?.requiresConfirmation) {
      pendingConfirmation.value = true
      return
    }

    await invoke(null)
  }

  async function confirmInvocation(confirmation: string) {
    pendingConfirmation.value = false
    await invoke(confirmation)
  }

  async function invoke(confirmation: string | null) {
    const tool = selectedTool.value
    if (!tool || isInvoking.value) {
      return
    }

    controller = new AbortController()
    isInvoking.value = true
    requestError.value = null
    result.value = null
    try {
      result.value = await postJson<ToolInvocationResponse>(
        `/api/tools/${encodeURIComponent(tool.name)}/invoke`,
        { arguments: buildArguments(tool), confirmation },
        controller.signal,
      )
    }
    catch (error) {
      if (error instanceof DOMException && error.name === "AbortError") {
        requestError.value = "已停止等待响应；后端操作可能仍在执行。"
      }
      else {
        requestError.value = formatError(error)
      }
    }
    finally {
      isInvoking.value = false
      controller = null
    }
  }

  function validate() {
    const tool = selectedTool.value
    clearRecord(errors)
    if (!tool) {
      return false
    }

    for (const parameter of tool.parameters) {
      const value = argumentsState[parameter.optionName]
      if (parameter.required && (value === undefined || typeof value === "string" && !value.trim())) {
        errors[parameter.optionName] = "此参数必填。"
        continue
      }

      if (parameter.type === "json" && typeof value === "string" && value.trim()) {
        try {
          JSON.parse(value)
        }
        catch {
          errors[parameter.optionName] = "请输入有效 JSON。"
        }
      }
    }

    return Object.keys(errors).length === 0
  }

  function buildArguments(tool: ToolMetadata) {
    return Object.fromEntries(tool.parameters.flatMap(parameter => {
      const value = argumentsState[parameter.optionName]
      if (typeof value === "string" && !value.trim() && !parameter.required) {
        return []
      }
      if (parameter.type === "json" && typeof value === "string") {
        return [[parameter.optionName, JSON.parse(value)]]
      }
      if (parameter.type === "int" && typeof value === "string") {
        return [[parameter.optionName, Number.parseInt(value, 10)]]
      }
      return [[parameter.optionName, value]]
    }))
  }

  function abort() {
    controller?.abort()
  }

  function clearResult() {
    result.value = null
    requestError.value = null
  }

  function clearSensitiveState() {
    abort()
    clearRecord(argumentsState)
    clearRecord(errors)
    clearResult()
    selectedName.value = null
  }

  onUnmounted(clearSensitiveState)

  return {
    tools: readonly(tools),
    selectedName,
    selectedTool,
    query,
    category,
    risk,
    filteredTools,
    argumentsState,
    errors,
    result: readonly(result),
    formattedResult,
    isLoadingCatalog: readonly(isLoadingCatalog),
    isInvoking: readonly(isInvoking),
    pendingConfirmation,
    requestError: readonly(requestError),
    loadTools,
    selectTool,
    requestInvocation,
    confirmInvocation,
    abort,
    clearResult,
    clearSensitiveState,
  }
}

function clearRecord(record: Record<string, unknown>) {
  for (const key of Object.keys(record)) {
    delete record[key]
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : String(error)
}
