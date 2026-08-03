import { describe, expect, it } from "vitest"
import { resolveInitialLocale } from "@/i18n"

describe("resolveInitialLocale", () => {
  it("prefers an explicit stored locale", () => {
    expect(resolveInitialLocale("en-US", "zh-CN")).toBe("en-US")
    expect(resolveInitialLocale("zh-CN", "en-US")).toBe("zh-CN")
  })

  it("falls back to browser language when storage is empty", () => {
    expect(resolveInitialLocale(null, "zh-Hans-CN")).toBe("zh-CN")
    expect(resolveInitialLocale(undefined, "en-GB")).toBe("en-US")
  })
})
