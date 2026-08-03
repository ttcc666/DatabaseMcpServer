import { readonly, shallowRef, toValue, watch, type MaybeRefOrGetter } from "vue"
import { fetchJson } from "@/api/http"
import type { ConnectionStringProfile, ConnectionStringProfileResponse } from "@/types/connections"

export function useConnectionStringWizard(dbType: MaybeRefOrGetter<string>) {
  const profile = shallowRef<ConnectionStringProfile | null>(null)
  const isLoading = shallowRef(false)
  const error = shallowRef<string | null>(null)

  watch(
    () => toValue(dbType),
    async (value, _previous, onCleanup) => {
      profile.value = null
      error.value = null
      if (!value) {
        return
      }

      const controller = new AbortController()
      onCleanup(() => controller.abort())
      isLoading.value = true
      try {
        const response = await fetchJson<ConnectionStringProfileResponse>(
          `/api/connection-string-profiles/${encodeURIComponent(value)}`,
          { signal: controller.signal },
        )
        profile.value = response.profile
      }
      catch (cause) {
        if (cause instanceof DOMException && cause.name === "AbortError") {
          return
        }
        error.value = cause instanceof Error ? cause.message : String(cause)
      }
      finally {
        if (!controller.signal.aborted) {
          isLoading.value = false
        }
      }
    },
    { immediate: true },
  )

  return {
    profile: readonly(profile),
    isLoading: readonly(isLoading),
    error: readonly(error),
  }
}
