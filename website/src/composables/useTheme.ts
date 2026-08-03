import { useColorMode, usePreferredDark } from "@vueuse/core"
import { computed } from "vue"

export type ThemeMode = "light" | "dark" | "auto"

const mode = useColorMode({
  selector: "html",
  attribute: "class",
  initialValue: "auto",
  storageKey: "dbmcp-color-mode",
  emitAuto: true,
  modes: {
    light: "light",
    dark: "dark",
  },
})

export function useTheme() {
  const prefersDark = usePreferredDark()
  const isDark = computed(() => mode.value === "dark" || mode.value === "auto" && prefersDark.value)

  function setTheme(value: ThemeMode) {
    mode.value = value
  }

  return {
    mode,
    isDark,
    setTheme,
  }
}
