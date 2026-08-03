<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Field, FieldDescription, FieldError, FieldLabel } from "@/components/ui/field"
import { Skeleton } from "@/components/ui/skeleton"
import { Textarea } from "@/components/ui/textarea"
import ConnectionFieldControl from "./ConnectionFieldControl.vue"
import { useConnectionStringWizard } from "@/composables/useConnectionStringWizard"
import type { ConnectionEditorMode, ConnectionStringFieldDefinition } from "@/types/connections"
import { computed, ref, watch } from "vue"
import { useI18n } from "vue-i18n"
import { Braces, ChevronDown, ChevronUp, Info, SlidersHorizontal } from "lucide-vue-next"

const props = defineProps<{
  dbType: string
  allowUnchanged: boolean
  maskedHint?: string | null
}>()

const mode = defineModel<ConnectionEditorMode>("mode", { required: true })
const raw = defineModel<string>("raw", { required: true })
const fields = defineModel<Record<string, string>>("fields", { required: true })
const { t } = useI18n()
const { profile, isLoading, error } = useConnectionStringWizard(() => props.dbType)
const showAdvanced = ref(false)
const fieldErrors = ref<Record<string, string>>({})
let initializedDbType = ""

const primaryFields = computed(() => profile.value?.fields.filter(field => !field.advanced) ?? [])
const advancedFields = computed(() => profile.value?.fields.filter(field => field.advanced) ?? [])
const maskedPreview = computed(() => {
  if (!profile.value || mode.value !== "wizard") return ""
  if (profile.value.format === "uri") {
    const host = fields.value.Host || "host"
    const port = fields.value.Port ? `:${fields.value.Port}` : ""
    const database = fields.value.Database ? `/${fields.value.Database}` : ""
    const user = fields.value.Username
    const credentials = user ? `${encodeURIComponent(user)}:${fields.value.Password ? "********" : ""}@` : ""
    return `mongodb://${credentials}${host}${port}${database}`
  }

  return profile.value.fields
    .flatMap(field => {
      const value = fields.value[field.key]
      if (value === undefined || value === "") return []
      return [`${field.key}=${field.sensitive ? "********" : value}`]
    })
    .join(";")
})

watch(profile, next => {
  if (!next) return
  if (!next.supportsWizard && mode.value === "wizard") {
    mode.value = "raw"
    return
  }
  if (next.dbType !== initializedDbType) {
    initializedDbType = next.dbType
    fields.value = Object.fromEntries(next.fields.map(field => [field.key, field.defaultValue ?? ""]))
    fieldErrors.value = {}
  }
})

function setMode(next: ConnectionEditorMode) {
  mode.value = next
  fieldErrors.value = {}
}

function updateField(field: ConnectionStringFieldDefinition, value: string) {
  fields.value = { ...fields.value, [field.key]: value }
  if (value.trim()) {
    const next = { ...fieldErrors.value }
    delete next[field.key]
    fieldErrors.value = next
  }
}

function validate() {
  if (mode.value === "unchanged") return props.allowUnchanged
  if (mode.value === "raw") return Boolean(raw.value.trim())
  if (!profile.value?.supportsWizard) return false

  const next: Record<string, string> = {}
  for (const field of profile.value.fields) {
    const value = fields.value[field.key] ?? ""
    if (field.required && !value.trim()) next[field.key] = t("wizard.fieldRequired", { label: field.label })
    if (field.inputType === "number" && value.trim() && !/^\d+$/.test(value.trim())) next[field.key] = t("wizard.fieldInteger", { label: field.label })
  }
  fieldErrors.value = next
  return Object.keys(next).length === 0
}

defineExpose({ validate })
</script>

<template>
  <div class="min-w-0 space-y-4">
    <div
      class="grid gap-2"
      :class="allowUnchanged ? 'grid-cols-1 sm:grid-cols-3' : 'grid-cols-2'"
      role="group"
      :aria-label="t('wizard.modeAria')"
    >
      <Button
        v-if="allowUnchanged"
        type="button"
        size="sm"
        class="w-full justify-center"
        :variant="mode === 'unchanged' ? 'default' : 'outline'"
        @click="setMode('unchanged')"
      >
        {{ t("wizard.keepExisting") }}
      </Button>
      <Button
        type="button"
        size="sm"
        class="w-full justify-center"
        :variant="mode === 'wizard' ? 'default' : 'outline'"
        :disabled="profile?.supportsWizard === false"
        @click="setMode('wizard')"
      >
        <SlidersHorizontal class="size-4" />{{ t("wizard.wizard") }}
      </Button>
      <Button
        type="button"
        size="sm"
        class="w-full justify-center"
        :variant="mode === 'raw' ? 'default' : 'outline'"
        @click="setMode('raw')"
      >
        <Braces class="size-4" />{{ t("wizard.raw") }}
      </Button>
    </div>

    <Alert v-if="mode === 'unchanged'" class="min-w-0 overflow-hidden">
      <Info class="size-4" />
      <AlertTitle>{{ t("wizard.keepExistingTitle") }}</AlertTitle>
      <AlertDescription class="min-w-0 space-y-2">
        <p>{{ t("wizard.keepExistingDesc") }}</p>
        <code class="block max-w-full overflow-hidden rounded-md border border-border/70 bg-muted/60 px-2.5 py-2 font-mono text-[11px] leading-5 break-all whitespace-pre-wrap">{{ maskedHint || t("common.hidden") }}</code>
      </AlertDescription>
    </Alert>

    <template v-else-if="mode === 'wizard'">
      <div v-if="isLoading" class="grid gap-3 sm:grid-cols-2"><Skeleton v-for="index in 6" :key="index" class="h-16" /></div>
      <Alert v-else-if="error" variant="destructive" class="min-w-0 overflow-hidden">
        <AlertTitle>{{ t("wizard.loadFailed") }}</AlertTitle>
        <AlertDescription class="break-words">{{ error }}</AlertDescription>
      </Alert>
      <Alert v-else-if="profile && !profile.supportsWizard">
        <AlertTitle>{{ t("wizard.noWizardTitle") }}</AlertTitle>
        <AlertDescription>{{ t("wizard.noWizardDesc") }}</AlertDescription>
      </Alert>
      <template v-else-if="profile">
        <div class="grid min-w-0 gap-4 sm:grid-cols-2">
          <Field v-for="field in primaryFields" :key="field.key" class="min-w-0" :data-invalid="Boolean(fieldErrors[field.key]) || undefined">
            <FieldLabel>{{ field.label }}<span v-if="field.required" class="text-destructive">*</span></FieldLabel>
            <ConnectionFieldControl
              :field="field"
              :model-value="fields[field.key] ?? ''"
              :invalid="Boolean(fieldErrors[field.key])"
              @update:model-value="updateField(field, $event)"
            />
            <FieldError v-if="fieldErrors[field.key]" :errors="[fieldErrors[field.key]]" />
          </Field>
        </div>

        <div v-if="advancedFields.length" class="min-w-0 space-y-4">
          <Button type="button" variant="ghost" size="sm" class="px-0" @click="showAdvanced = !showAdvanced">
            <ChevronUp v-if="showAdvanced" class="size-4" /><ChevronDown v-else class="size-4" />{{ t("wizard.advancedFields") }}
          </Button>
          <div v-if="showAdvanced" class="grid min-w-0 gap-4 sm:grid-cols-2">
            <Field v-for="field in advancedFields" :key="field.key" class="min-w-0" :data-invalid="Boolean(fieldErrors[field.key]) || undefined">
              <FieldLabel>{{ field.label }}</FieldLabel>
              <ConnectionFieldControl
                :field="field"
                :model-value="fields[field.key] ?? ''"
                :invalid="Boolean(fieldErrors[field.key])"
                @update:model-value="updateField(field, $event)"
              />
              <FieldError v-if="fieldErrors[field.key]" :errors="[fieldErrors[field.key]]" />
            </Field>
          </div>
        </div>

        <Field class="min-w-0">
          <FieldLabel>{{ t("wizard.maskedPreview") }}</FieldLabel>
          <code class="block min-h-12 max-w-full overflow-hidden break-all rounded-md border border-border bg-muted/50 px-3 py-2 font-mono text-xs leading-6 whitespace-pre-wrap">{{ maskedPreview || t("wizard.previewPlaceholder") }}</code>
          <FieldDescription>{{ t("wizard.previewHint") }}</FieldDescription>
        </Field>
      </template>
    </template>

    <Field v-else class="min-w-0">
      <FieldLabel>{{ t("wizard.rawConnectionString") }}</FieldLabel>
      <Textarea v-model="raw" :placeholder="maskedHint ?? 'Server=...;Database=...;'" class="min-h-32 max-w-full font-mono text-sm break-all" />
      <FieldDescription>{{ t("wizard.rawHint") }}</FieldDescription>
    </Field>
  </div>
</template>
