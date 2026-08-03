import { computed, readonly, shallowRef } from "vue"
import { useI18n } from "vue-i18n"
import { toast } from "vue-sonner"
import { postJson } from "@/api/http"
import type { ConnectionHealthResponse } from "@/types/connections"

export function useConnectionHealth() {
  const { t } = useI18n()
  const response = shallowRef<ConnectionHealthResponse | null>(null)
  const isChecking = shallowRef(false)

  const resultMap = computed(() => new Map(
    (response.value?.results ?? []).map(result => [result.name, result]),
  ))

  async function checkAll() {
    if (isChecking.value) {
      return
    }

    isChecking.value = true
    try {
      const payload = await postJson<ConnectionHealthResponse>("/api/databases/health-check", {})
      response.value = payload
      if (payload.overallHealth) {
        toast.success(t("healthCheck.allHealthy", { count: payload.totalConnections }))
      }
      else {
        toast.error(t("healthCheck.someUnhealthy", { count: payload.unhealthyConnections }))
      }
    }
    catch (error) {
      toast.error(t("healthCheck.failed", { error: formatError(error) }))
    }
    finally {
      isChecking.value = false
    }
  }

  function prune(validNames: readonly string[]) {
    if (!response.value) {
      return
    }

    const valid = new Set(validNames)
    const results = response.value.results.filter(result => valid.has(result.name))
    if (results.length === response.value.results.length) {
      return
    }

    const healthyConnections = results.filter(result => result.isHealthy).length
    response.value = {
      ...response.value,
      totalConnections: results.length,
      healthyConnections,
      unhealthyConnections: results.length - healthyConnections,
      overallHealth: results.length > 0 && healthyConnections === results.length,
      results,
    }
  }

  return {
    response: readonly(response),
    isChecking: readonly(isChecking),
    resultMap,
    checkAll,
    prune,
  }
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : String(error)
}
