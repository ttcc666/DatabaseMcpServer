<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Checkbox } from "@/components/ui/checkbox"
import { Field, FieldContent, FieldDescription, FieldError, FieldGroup, FieldLabel, FieldSet, FieldTitle } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectGroup, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Sheet, SheetContent, SheetDescription, SheetFooter, SheetHeader, SheetTitle } from "@/components/ui/sheet"
import { Textarea } from "@/components/ui/textarea"
import ConnectionStringWizard from "@/components/connection-editor/ConnectionStringWizard.vue"
import type { DatabaseDetail, EditorDraft } from "@/types"
import { computed, ref, useTemplateRef, watch } from "vue"
import { CopyPlus, Database, PencilLine, Shapes } from "lucide-vue-next"

const props = defineProps<{
  open: boolean
  draft: EditorDraft | null
  dbTypeOptions: string[]
  selectedDatabase: DatabaseDetail | null
  busyAction: string | null
}>()

const emit = defineEmits<{
  (event: "update:open", value: boolean): void
  (event: "apply-preset", dbType: string): void
  (event: "submit", draft: EditorDraft): void
}>()

const localDraft = ref<EditorDraft | null>(null)
const attemptedSubmit = ref(false)
const wizard = useTemplateRef<{ validate: () => boolean }>("wizard")

watch(() => props.draft, draft => {
  if (props.open && draft) localDraft.value = cloneDraft(draft)
}, { immediate: true })

watch(() => props.open, open => {
  attemptedSubmit.value = false
  if (open && props.draft) localDraft.value = cloneDraft(props.draft)
  if (!open) localDraft.value = null
})

const title = computed(() => ({ edit: "编辑数据库连接", preset: "基于模板创建", clone: "克隆数据库连接", create: "新增数据库连接" })[localDraft.value?.mode ?? "create"])
const description = computed(() => ({
  edit: "默认保留现有连接串；需要替换时可选择向导或原始模式。",
  preset: "选择模板后，通过结构化字段生成连接字符串。",
  clone: "复制当前选中连接，仅需要填写新名称。",
  create: "录入连接参数并写入配置文件。",
})[localDraft.value?.mode ?? "create"])
const nameErrors = computed(() => attemptedSubmit.value && !localDraft.value?.name.trim() ? ["连接名称不能为空。"] : [])
const dbTypeErrors = computed(() => attemptedSubmit.value && localDraft.value?.mode !== "clone" && !localDraft.value?.dbType.trim() ? ["数据库类型不能为空。"] : [])
const connectionErrors = computed(() => {
  const draft = localDraft.value
  if (!attemptedSubmit.value || !draft || draft.mode === "clone" || draft.connectionMode === "unchanged") return []
  if (draft.connectionMode === "raw" && !draft.connectionString.trim()) return ["请输入完整连接字符串。"]
  return []
})
const icon = computed(() => ({ edit: PencilLine, clone: CopyPlus, preset: Shapes, create: Database })[localDraft.value?.mode ?? "create"])

function requestSubmit() {
  attemptedSubmit.value = true
  const draft = localDraft.value
  if (!draft || nameErrors.value.length || dbTypeErrors.value.length || connectionErrors.value.length) return
  if (draft.mode !== "clone" && draft.connectionMode !== "unchanged") {
    if (!draft.dbType.trim()) return
    if (draft.connectionMode === "wizard" && !wizard.value?.validate()) return
  }
  emit("submit", cloneDraft(draft))
}

function handlePresetModelValue(value: unknown) {
  if (typeof value !== "string" || !localDraft.value) return
  localDraft.value.presetDbType = value
  localDraft.value.dbType = value
  emit("apply-preset", value)
}

function updateCheckbox(key: "setDefault" | "clearDescription" | "allowDangerousOperations", value: boolean | "indeterminate") {
  if (localDraft.value) localDraft.value[key] = value === true
}

function cloneDraft(draft: EditorDraft): EditorDraft {
  return { ...draft, connectionFields: { ...draft.connectionFields } }
}
</script>

<template>
  <Sheet :open="open" @update:open="emit('update:open', $event)">
    <SheetContent side="right" class="flex h-full w-full max-w-full flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl">
      <SheetHeader class="shrink-0 border-b border-border/60 px-5 py-4 pr-14 sm:px-6 sm:py-5">
        <div class="flex min-w-0 items-start gap-3">
          <div class="rounded-md bg-primary/10 p-3 text-primary"><component :is="icon" class="size-5" /></div>
          <div class="min-w-0 flex-1">
            <SheetTitle class="text-left text-lg sm:text-xl">{{ title }}</SheetTitle>
            <SheetDescription class="text-left text-pretty">{{ description }}</SheetDescription>
          </div>
        </div>
      </SheetHeader>

      <div v-if="localDraft" class="min-h-0 min-w-0 flex-1 overflow-x-hidden overflow-y-auto px-5 py-5 sm:px-6">
        <FieldGroup class="min-w-0 gap-5">
          <FieldSet class="min-w-0 rounded-lg border border-border/70 bg-muted/20 p-4">
            <FieldTitle>基础信息</FieldTitle>
            <FieldGroup class="mt-4 min-w-0 gap-4">
              <Field class="min-w-0" :data-invalid="nameErrors.length > 0 || undefined">
                <FieldLabel>连接名称</FieldLabel>
                <Input v-model="localDraft.name" class="max-w-full" placeholder="例如 mysql-dev" :aria-invalid="nameErrors.length > 0" />
                <FieldError :errors="nameErrors" />
              </Field>
              <Field v-if="localDraft.mode === 'preset'" class="min-w-0">
                <FieldLabel>模板类型</FieldLabel>
                <Select :model-value="localDraft.presetDbType ?? localDraft.dbType" @update:model-value="handlePresetModelValue">
                  <SelectTrigger class="w-full max-w-full"><SelectValue placeholder="选择模板" /></SelectTrigger>
                  <SelectContent><SelectGroup><SelectItem v-for="dbType in dbTypeOptions" :key="dbType" :value="dbType">{{ dbType }}</SelectItem></SelectGroup></SelectContent>
                </Select>
              </Field>
              <Field v-if="localDraft.mode !== 'clone'" class="min-w-0" :data-invalid="dbTypeErrors.length > 0 || undefined">
                <FieldLabel>数据库类型</FieldLabel>
                <Select v-model="localDraft.dbType">
                  <SelectTrigger class="w-full max-w-full" :aria-invalid="dbTypeErrors.length > 0"><SelectValue placeholder="选择数据库类型" /></SelectTrigger>
                  <SelectContent><SelectGroup><SelectItem v-for="dbType in dbTypeOptions" :key="dbType" :value="dbType">{{ dbType }}</SelectItem></SelectGroup></SelectContent>
                </Select>
                <FieldError :errors="dbTypeErrors" />
              </Field>
            </FieldGroup>
          </FieldSet>

          <FieldSet v-if="localDraft.mode !== 'clone'" class="min-w-0 rounded-lg border border-border/70 bg-muted/20 p-4">
            <FieldTitle>连接参数</FieldTitle>
            <FieldDescription>向导字段由后端数据库类型目录生成，敏感默认值不会下发。</FieldDescription>
            <div class="mt-4 min-w-0">
              <Alert v-if="!localDraft.dbType.trim()" class="mb-4">
                <Database class="size-4" />
                <AlertTitle>先选择数据库类型</AlertTitle>
                <AlertDescription>选择类型后会加载对应的连接字符串向导；也可以随时切换到原始模式手写连接串。</AlertDescription>
              </Alert>
              <ConnectionStringWizard
                v-else
                ref="wizard"
                v-model:mode="localDraft.connectionMode"
                v-model:raw="localDraft.connectionString"
                v-model:fields="localDraft.connectionFields"
                :db-type="localDraft.dbType"
                :allow-unchanged="localDraft.mode === 'edit'"
                :masked-hint="localDraft.maskedConnectionHint"
              />
              <FieldError class="mt-2" :errors="connectionErrors" />
            </div>
            <Field class="mt-5 min-w-0">
              <FieldLabel>描述</FieldLabel>
              <Textarea v-model="localDraft.description" placeholder="本地开发库 / staging / analytics ..." class="min-h-24 max-w-full" />
            </Field>
          </FieldSet>

          <FieldSet class="min-w-0 rounded-lg border border-border/70 bg-muted/20 p-4">
            <FieldTitle>附加行为</FieldTitle>
            <FieldGroup class="mt-4 min-w-0 gap-4">
              <Field orientation="horizontal" class="min-w-0 items-start">
                <Checkbox :checked="localDraft.setDefault" @update:checked="updateCheckbox('setDefault', $event)" />
                <FieldContent class="min-w-0"><FieldLabel>保存后设为默认连接</FieldLabel><FieldDescription>修改配置文件中的唯一默认项。</FieldDescription></FieldContent>
              </Field>
              <Field v-if="localDraft.mode !== 'clone'" orientation="horizontal" class="min-w-0 items-start">
                <Checkbox :checked="localDraft.allowDangerousOperations" @update:checked="updateCheckbox('allowDangerousOperations', $event)" />
                <FieldContent class="min-w-0"><FieldLabel>允许通用命令执行危险操作</FieldLabel><FieldDescription>默认关闭；MCP Tool 仍需服务端确认策略。</FieldDescription></FieldContent>
              </Field>
              <Field v-if="localDraft.mode === 'edit'" orientation="horizontal" class="min-w-0 items-start">
                <Checkbox :checked="localDraft.clearDescription" @update:checked="updateCheckbox('clearDescription', $event)" />
                <FieldContent class="min-w-0"><FieldLabel>提交时清空描述</FieldLabel><FieldDescription>与描述文本同时存在时，以清空为准。</FieldDescription></FieldContent>
              </Field>
            </FieldGroup>
          </FieldSet>

          <Alert v-if="localDraft.mode === 'clone' && selectedDatabase" class="min-w-0 overflow-hidden">
            <Database class="size-4" /><AlertTitle>克隆源</AlertTitle>
            <AlertDescription class="break-words">{{ selectedDatabase.name }} · {{ selectedDatabase.dbType }}</AlertDescription>
          </Alert>
        </FieldGroup>
      </div>

      <SheetFooter class="shrink-0 border-t border-border/60 bg-card px-5 py-4 sm:flex-row sm:justify-end sm:px-6">
        <Button variant="outline" @click="emit('update:open', false)">取消</Button>
        <Button :disabled="busyAction !== null" @click="requestSubmit">{{ localDraft?.mode === "clone" ? "执行克隆" : "保存变更" }}</Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
