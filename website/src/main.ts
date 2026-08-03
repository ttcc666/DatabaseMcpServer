import { createApp } from "vue"
import "vue-sonner/style.css"
import "./index.css"
import App from "./App.vue"
import { applyDocumentLocale, i18n, type AppLocale } from "./i18n"

const app = createApp(App)
app.use(i18n)

const locale = i18n.global.locale.value as AppLocale
applyDocumentLocale(locale, String(i18n.global.t("app.title")))

app.mount("#app")
