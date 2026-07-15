<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import {
  Field,
  FieldContent,
  FieldDescription,
  FieldError,
  FieldGroup,
  FieldLabel,
  FieldSet,
  FieldTitle,
} from "@/components/ui/field"
import { Checkbox } from "@/components/ui/checkbox"
import { Input } from "@/components/ui/input"
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetFooter,
  SheetHeader,
  SheetTitle,
} from "@/components/ui/sheet"
import { Textarea } from "@/components/ui/textarea"
import type { DatabaseDetail, EditorDraft } from "@/types"
import { computed, ref, watch } from "vue"
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
  (event: "submit"): void
}>()

const attemptedSubmit = ref(false)

const title = computed(() => {
  switch (props.draft?.mode) {
    case "edit":
      return "编辑数据库连接"
    case "preset":
      return "基于模板创建"
    case "clone":
      return "克隆数据库连接"
    default:
      return "新增数据库连接"
  }
})

const description = computed(() => {
  switch (props.draft?.mode) {
    case "edit":
      return "保留原连接的基础上修改字段，空的连接字符串会视为保持不变。"
    case "preset":
      return "先选模板，再覆盖名称、连接串和描述。"
    case "clone":
      return "复制当前选中连接，仅需要填写新名称。"
    default:
      return "手工录入连接参数并写入配置文件。"
  }
})

const nameErrors = computed(() => {
  if (!attemptedSubmit.value || props.draft?.name.trim()) {
    return []
  }
  return ["连接名称不能为空。"]
})

const dbTypeErrors = computed(() => {
  if (!props.draft || props.draft.mode === "clone") {
    return []
  }
  if (!attemptedSubmit.value || props.draft.dbType.trim()) {
    return []
  }
  return ["数据库类型不能为空。"]
})

const connectionErrors = computed(() => {
  if (!props.draft || props.draft.mode === "edit" || props.draft.mode === "clone") {
    return []
  }
  if (!attemptedSubmit.value || props.draft.connectionString.trim()) {
    return []
  }
  return ["连接字符串不能为空。"]
})

watch(() => props.open, (value) => {
  if (value) {
    attemptedSubmit.value = false
  }
})

function requestSubmit() {
  attemptedSubmit.value = true
  if (nameErrors.value.length || dbTypeErrors.value.length || connectionErrors.value.length) {
    return
  }
  emit("submit")
}

function handlePresetChange(value: string) {
  emit("apply-preset", value)
}

function handlePresetModelValue(value: string | number | Record<string, any> | bigint | null | undefined) {
  if (typeof value === "string") {
    handlePresetChange(value)
  }
}

function updateSetDefault(value: boolean | "indeterminate") {
  if (props.draft) {
    props.draft.setDefault = value === true
  }
}

function updateClearDescription(value: boolean | "indeterminate") {
  if (props.draft) {
    props.draft.clearDescription = value === true
  }
}

function updateAllowDangerousOperations(value: boolean | "indeterminate") {
  if (props.draft) {
    props.draft.allowDangerousOperations = value === true
  }
}

const icon = computed(() => {
  switch (props.draft?.mode) {
    case "edit":
      return PencilLine
    case "clone":
      return CopyPlus
    case "preset":
      return Shapes
    default:
      return Database
  }
})
</script>

<template>
  <Sheet :open="open" @update:open="emit('update:open', $event)">
    <SheetContent side="right" class="w-full gap-6 sm:max-w-2xl">
      <SheetHeader class="border-b border-border/60 pb-4">
        <div class="flex items-center gap-3">
          <div class="rounded-2xl bg-primary/10 p-3 text-primary">
            <component :is="icon" class="size-5" />
          </div>
          <div>
            <SheetTitle class="text-left text-xl">{{ title }}</SheetTitle>
            <SheetDescription class="text-left">{{ description }}</SheetDescription>
          </div>
        </div>
      </SheetHeader>

      <div v-if="draft" class="flex-1 overflow-auto pr-1">
        <FieldGroup>
          <FieldSet class="rounded-2xl border border-border/70 bg-muted/30 p-4">
            <FieldTitle>基础信息</FieldTitle>
            <FieldDescription>连接名称永远必填；克隆模式只会生成新条目，不直接改源连接。</FieldDescription>

            <FieldGroup class="mt-4">
              <Field :data-invalid="nameErrors.length > 0 || undefined">
                <FieldLabel>连接名称</FieldLabel>
                <Input v-model="draft.name" placeholder="例如 mysql-dev" :aria-invalid="nameErrors.length > 0" />
                <FieldError :errors="nameErrors" />
              </Field>

              <Field v-if="draft.mode === 'preset'">
                <FieldLabel>模板类型</FieldLabel>
                <Select :model-value="draft.presetDbType ?? draft.dbType" @update:model-value="handlePresetModelValue">
                  <SelectTrigger>
                    <SelectValue placeholder="选择模板" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem v-for="dbType in dbTypeOptions" :key="dbType" :value="dbType">
                        {{ dbType }}
                      </SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
              </Field>

              <Field v-if="draft.mode !== 'clone'" :data-invalid="dbTypeErrors.length > 0 || undefined">
                <FieldLabel>数据库类型</FieldLabel>
                <Select v-model="draft.dbType">
                  <SelectTrigger :aria-invalid="dbTypeErrors.length > 0">
                    <SelectValue placeholder="选择数据库类型" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectGroup>
                      <SelectItem v-for="dbType in dbTypeOptions" :key="dbType" :value="dbType">
                        {{ dbType }}
                      </SelectItem>
                    </SelectGroup>
                  </SelectContent>
                </Select>
                <FieldError :errors="dbTypeErrors" />
              </Field>
            </FieldGroup>
          </FieldSet>

          <FieldSet v-if="draft.mode !== 'clone'" class="rounded-2xl border border-border/70 bg-muted/30 p-4">
            <FieldTitle>连接参数</FieldTitle>
            <FieldDescription>编辑模式下留空连接字符串表示保持原值；页面里只展示脱敏后的旧值提示。</FieldDescription>

            <FieldGroup class="mt-4">
              <Field :data-invalid="connectionErrors.length > 0 || undefined">
                <FieldLabel>连接字符串</FieldLabel>
                <Textarea
                  v-model="draft.connectionString"
                  :placeholder="draft.maskedConnectionHint ?? 'Server=...;Database=...;'"
                  class="min-h-32 font-mono text-sm"
                  :aria-invalid="connectionErrors.length > 0"
                />
                <FieldError :errors="connectionErrors" />
              </Field>

              <Field>
                <FieldLabel>描述</FieldLabel>
                <Textarea v-model="draft.description" placeholder="本地开发库 / staging / analytics ..." class="min-h-24" />
              </Field>
            </FieldGroup>
          </FieldSet>

          <FieldSet class="rounded-2xl border border-border/70 bg-muted/30 p-4">
            <FieldTitle>附加行为</FieldTitle>
            <FieldDescription>默认连接会写回配置文件；当前连接切换请在主表格中执行。</FieldDescription>
            <FieldGroup class="mt-4">
              <Field orientation="horizontal">
                <Checkbox
                  :checked="draft.setDefault"
                  @update:checked="updateSetDefault"
                />
                <FieldContent>
                  <FieldLabel>保存后设为默认连接</FieldLabel>
                  <FieldDescription>这会修改 <code>databases.json</code> 中唯一的默认项。</FieldDescription>
                </FieldContent>
              </Field>

              <Field v-if="draft.mode !== 'clone'" orientation="horizontal">
                <Checkbox
                  :checked="draft.allowDangerousOperations"
                  @update:checked="updateAllowDangerousOperations"
                />
                <FieldContent>
                  <FieldLabel>允许通用命令执行危险操作</FieldLabel>
                  <FieldDescription>开启后当前连接的 <code>execute_command</code> 等通用命令可执行 DDL/无条件更新；默认关闭。</FieldDescription>
                </FieldContent>
              </Field>

              <Field v-if="draft.mode === 'edit'" orientation="horizontal">
                <Checkbox
                  :checked="draft.clearDescription"
                  @update:checked="updateClearDescription"
                />
                <FieldContent>
                  <FieldLabel>提交时清空描述</FieldLabel>
                  <FieldDescription>用于把现有描述置空；和文本描述同时使用时，以清空为准。</FieldDescription>
                </FieldContent>
              </Field>
            </FieldGroup>
          </FieldSet>

          <Alert v-if="draft.mode === 'clone' && selectedDatabase">
            <Database class="size-4" />
            <AlertTitle>克隆源</AlertTitle>
            <AlertDescription class="space-y-1">
              <p>{{ selectedDatabase.name }} · {{ selectedDatabase.dbType }}</p>
              <p class="text-muted-foreground">{{ selectedDatabase.description || "暂无说明" }}</p>
            </AlertDescription>
          </Alert>
        </FieldGroup>
      </div>

      <SheetFooter class="border-t border-border/60 pt-4">
        <Button variant="outline" @click="emit('update:open', false)">
          取消
        </Button>
        <Button :disabled="busyAction !== null" @click="requestSubmit">
          {{ draft?.mode === "clone" ? "执行克隆" : "保存变更" }}
        </Button>
      </SheetFooter>
    </SheetContent>
  </Sheet>
</template>
