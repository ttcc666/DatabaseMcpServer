import { describe, expect, it } from "vitest"
import { useTheme } from "@/composables/useTheme"

describe("useTheme", () => {
  it("stores explicit and system theme modes", () => {
    const theme = useTheme()
    theme.setTheme("dark")
    expect(theme.mode.value).toBe("dark")
    theme.setTheme("light")
    expect(theme.mode.value).toBe("light")
    theme.setTheme("auto")
    expect(theme.mode.value).toBe("auto")
  })
})
