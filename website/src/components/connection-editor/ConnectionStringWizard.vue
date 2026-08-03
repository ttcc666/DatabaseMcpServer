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
import { Braces, ChevronDown, ChevronUp, Info, SlidersHorizontal } from "lucide-vue-next"

const props = defineProps<{
  dbType: string
  allowUnchanged: boolean
  maskedHint?: string | null
}>()

const mode = defineModel<ConnectionEditorMode>("mode", { required: true })
const raw = defineModel<string>("raw", { required: true })
const fields = defineModel<Record<string, string>>("fields", { required: true })
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
    if (field.required && !value.trim()) next[field.key] = `${field.label}不能为空。`
    if (field.inputType === "number" && value.trim() && !/^\d+$/.test(value.trim())) next[field.key] = `${field.label}需要整数值。`
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
      aria-label="连接字符串输入模式"
    >
      <Button
        v-if="allowUnchanged"
        type="button"
        size="sm"
        class="w-full justify-center"
        :variant="mode === 'unchanged' ? 'default' : 'outline'"
        @click="setMode('unchanged')"
      >
        保持现有
      </Button>
      <Button
        type="button"
        size="sm"
        class="w-full justify-center"
        :variant="mode === 'wizard' ? 'default' : 'outline'"
        :disabled="profile?.supportsWizard === false"
        @click="setMode('wizard')"
      >
        <SlidersHorizontal class="size-4" />向导
      </Button>
      <Button
        type="button"
        size="sm"
        class="w-full justify-center"
        :variant="mode === 'raw' ? 'default' : 'outline'"
        @click="setMode('raw')"
      >
        <Braces class="size-4" />原始
      </Button>
    </div>

    <Alert v-if="mode === 'unchanged'" class="min-w-0 overflow-hidden">
      <Info class="size-4" />
      <AlertTitle>保持现有连接字符串</AlertTitle>
      <AlertDescription class="min-w-0 space-y-2">
        <p>保存时不会提交或覆盖当前连接字符串。现有值仍只以脱敏形式显示：</p>
        <code class="block max-w-full overflow-hidden rounded-md border border-border/70 bg-muted/60 px-2.5 py-2 font-mono text-[11px] leading-5 break-all whitespace-pre-wrap">{{ maskedHint || "已隐藏" }}</code>
      </AlertDescription>
    </Alert>

    <template v-else-if="mode === 'wizard'">
      <div v-if="isLoading" class="grid gap-3 sm:grid-cols-2"><Skeleton v-for="index in 6" :key="index" class="h-16" /></div>
      <Alert v-else-if="error" variant="destructive" class="min-w-0 overflow-hidden">
        <AlertTitle>向导加载失败</AlertTitle>
        <AlertDescription class="break-words">{{ error }}</AlertDescription>
      </Alert>
      <Alert v-else-if="profile && !profile.supportsWizard">
        <AlertTitle>该类型暂无结构化向导</AlertTitle>
        <AlertDescription>请切换到原始模式输入完整连接字符串。</AlertDescription>
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
            <ChevronUp v-if="showAdvanced" class="size-4" /><ChevronDown v-else class="size-4" />高级字段
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
          <FieldLabel>脱敏预览</FieldLabel>
          <code class="block min-h-12 max-w-full overflow-hidden break-all rounded-md border border-border bg-muted/50 px-3 py-2 font-mono text-xs leading-6 whitespace-pre-wrap">{{ maskedPreview || "填写字段后显示预览" }}</code>
          <FieldDescription>密码等敏感字段不会出现在预览文本中。</FieldDescription>
        </Field>
      </template>
    </template>

    <Field v-else class="min-w-0">
      <FieldLabel>原始连接字符串</FieldLabel>
      <Textarea v-model="raw" :placeholder="maskedHint ?? 'Server=...;Database=...;'" class="min-h-32 max-w-full font-mono text-sm break-all" />
      <FieldDescription>原始模式适用于自定义参数；编辑时必须输入完整替换值。</FieldDescription>
    </Field>
  </div>
</template>
