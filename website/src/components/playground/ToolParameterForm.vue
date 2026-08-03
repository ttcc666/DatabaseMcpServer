<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Field, FieldDescription, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Textarea } from "@/components/ui/textarea"
import type { ToolArgumentValue, ToolMetadata, ToolParameterMetadata } from "@/types/tools"
import { Braces, Play, ShieldAlert, Square } from "lucide-vue-next"

defineProps<{
  tool: ToolMetadata
  argumentsState: Record<string, ToolArgumentValue>
  errors: Record<string, string>
  isInvoking: boolean
}>()

const emit = defineEmits<{
  (event: "update-argument", optionName: string, value: ToolArgumentValue): void
  (event: "invoke" | "abort"): void
}>()

function isMultiline(parameter: ToolParameterMetadata) {
  return parameter.type === "json" || /sql|query|columns|parameters/i.test(parameter.optionName)
}

function stringValue(value: ToolArgumentValue | undefined) {
  return typeof value === "string" || typeof value === "number" ? String(value) : ""
}
</script>

<template>
  <form class="space-y-5" @submit.prevent="emit('invoke')">
    <div>
      <div class="flex flex-wrap items-start justify-between gap-3">
        <div class="min-w-0">
          <div class="flex items-center gap-2">
            <code class="break-all text-base font-semibold">{{ tool.name }}</code>
            <ShieldAlert v-if="tool.requiresConfirmation" class="size-4 shrink-0 text-destructive" />
          </div>
          <p class="mt-2 max-w-3xl text-sm leading-6 text-muted-foreground">{{ tool.description }}</p>
        </div>
      </div>
      <Alert v-if="tool.requiresConfirmation" variant="destructive" class="mt-4">
        <ShieldAlert class="size-4" /><AlertTitle>受保护 Tool</AlertTitle>
        <AlertDescription>执行前需要输入完整 Tool 名称；服务端会再次验证。</AlertDescription>
      </Alert>
    </div>

    <div v-if="tool.parameters.length" class="grid gap-4 xl:grid-cols-2">
      <Field v-for="parameter in tool.parameters" :key="parameter.optionName" :data-invalid="Boolean(errors[parameter.optionName]) || undefined" :class="isMultiline(parameter) ? 'xl:col-span-2' : ''">
        <FieldLabel :for="`tool-${parameter.optionName}`">
          --{{ parameter.optionName }}<span v-if="parameter.required" class="text-destructive">*</span>
        </FieldLabel>
        <div v-if="parameter.type === 'bool'" class="flex min-h-10 items-center justify-between rounded-md border border-border bg-background px-3">
          <span class="text-sm text-muted-foreground">{{ argumentsState[parameter.optionName] ? "true" : "false" }}</span>
          <Switch :model-value="Boolean(argumentsState[parameter.optionName])" :aria-label="parameter.name" @update:model-value="emit('update-argument', parameter.optionName, $event)" />
        </div>
        <Textarea
          v-else-if="isMultiline(parameter)"
          :id="`tool-${parameter.optionName}`"
          :model-value="stringValue(argumentsState[parameter.optionName])"
          :placeholder="parameter.type === 'json' ? '{}' : parameter.description"
          class="min-h-28 font-mono text-sm"
          :aria-invalid="Boolean(errors[parameter.optionName]) || undefined"
          @update:model-value="emit('update-argument', parameter.optionName, $event)"
        />
        <Input
          v-else
          :id="`tool-${parameter.optionName}`"
          :type="parameter.type === 'int' ? 'number' : 'text'"
          :model-value="stringValue(argumentsState[parameter.optionName])"
          :placeholder="parameter.description"
          :aria-invalid="Boolean(errors[parameter.optionName]) || undefined"
          @update:model-value="emit('update-argument', parameter.optionName, $event)"
        />
        <FieldDescription class="line-clamp-2">{{ parameter.description }}</FieldDescription>
        <FieldError v-if="errors[parameter.optionName]" :errors="[errors[parameter.optionName]]" />
      </Field>
    </div>
    <div v-else class="rounded-lg border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground">此 Tool 无需参数。</div>

    <div class="flex flex-wrap justify-end gap-2 border-t border-border pt-4">
      <Button v-if="isInvoking" type="button" variant="outline" @click="emit('abort')"><Square class="size-4" />停止等待</Button>
      <Button type="submit" :disabled="isInvoking">
        <Braces v-if="tool.parameters.some(item => item.type === 'json')" class="size-4" /><Play v-else class="size-4" />
        {{ isInvoking ? "执行中" : "执行 Tool" }}
      </Button>
    </div>
  </form>
</template>
