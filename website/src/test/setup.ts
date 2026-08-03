import { config } from "@vue/test-utils"
import { beforeAll } from "vitest"
import { createAppI18n } from "@/i18n"

beforeAll(() => {
  config.global.plugins = [...(config.global.plugins ?? []), createAppI18n("zh-CN")]
})
