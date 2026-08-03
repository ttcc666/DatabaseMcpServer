import { computed } from "vue"
import { useI18n } from "vue-i18n"
import {
  applyDocumentLocale,
  persistLocale,
  type AppLocale,
  type MessageSchema,
} from "@/i18n"

export function useLocale() {
  const { locale, t } = useI18n<{ message: MessageSchema }, AppLocale>()

  const currentLocale = computed(() => locale.value as AppLocale)
  const label = computed(() => currentLocale.value === "zh-CN" ? t("language.zhCN") : t("language.enUS"))

  function setLocale(value: AppLocale) {
    locale.value = value
    persistLocale(value)
    applyDocumentLocale(value, t("app.title"))
  }

  function syncDocument() {
    applyDocumentLocale(currentLocale.value, t("app.title"))
  }

  return {
    locale: currentLocale,
    label,
    setLocale,
    syncDocument,
    t,
  }
}
