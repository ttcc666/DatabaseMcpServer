import { createI18n, type I18n } from "vue-i18n"
import enUS from "./locales/en-US"
import zhCN from "./locales/zh-CN"

export const LOCALE_STORAGE_KEY = "dbmcp-locale"
export const SUPPORTED_LOCALES = ["zh-CN", "en-US"] as const

export type AppLocale = (typeof SUPPORTED_LOCALES)[number]
export type MessageSchema = typeof zhCN

export const messages: Record<AppLocale, MessageSchema> = {
  "zh-CN": zhCN,
  // English strings share the same key tree; cast keeps MessageSchema unified.
  "en-US": enUS as unknown as MessageSchema,
}

export function resolveInitialLocale(stored?: string | null, language = typeof navigator === "undefined" ? "zh-CN" : navigator.language): AppLocale {
  if (stored === "zh-CN" || stored === "en-US") {
    return stored
  }

  return language.toLowerCase().startsWith("zh") ? "zh-CN" : "en-US"
}

export type AppI18n = I18n<{ "zh-CN": MessageSchema, "en-US": MessageSchema }, {}, {}, AppLocale, false>

export function createAppI18n(locale: AppLocale = resolveInitialLocale(readStoredLocale())): AppI18n {
  return createI18n({
    legacy: false,
    locale,
    fallbackLocale: "zh-CN",
    messages,
  })
}

export function readStoredLocale() {
  try {
    return localStorage.getItem(LOCALE_STORAGE_KEY)
  }
  catch {
    return null
  }
}

export function persistLocale(locale: AppLocale) {
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, locale)
  }
  catch {
    // Ignore quota / private-mode failures; in-memory locale still works.
  }
}

export function applyDocumentLocale(locale: AppLocale, title: string) {
  if (typeof document === "undefined") {
    return
  }

  document.documentElement.lang = locale
  document.title = title
}

export const i18n = createAppI18n()
