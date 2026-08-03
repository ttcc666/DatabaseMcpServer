<script setup lang="ts">
import { Badge } from "@/components/ui/badge"
import { Input } from "@/components/ui/input"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import type { ToolCategory, ToolMetadata } from "@/types/tools"
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { Search, ShieldAlert, Wrench } from "lucide-vue-next"

defineProps<{
  tools: readonly ToolMetadata[]
  selectedName: string | null
  isLoading: boolean
}>()

const query = defineModel<string>("query", { required: true })
const category = defineModel<ToolCategory>("category", { required: true })
const risk = defineModel<"all" | "safe" | "protected">("risk", { required: true })
const emit = defineEmits<{ (event: "select", name: string): void }>()
const { t } = useI18n()

const categoryLabels = computed<Record<ToolCategory, string>>(() => ({
  all: t("playground.categoryAll"),
  connection: t("playground.categoryConnection"),
  schema: t("playground.categorySchema"),
  query: t("playground.categoryQuery"),
  command: t("playground.categoryCommand"),
  other: t("playground.categoryOther"),
}))

function updateCategory(value: unknown) {
  if (typeof value === "string") category.value = value as ToolCategory
}

function updateRisk(value: unknown) {
  if (value === "all" || value === "safe" || value === "protected") risk.value = value
}
</script>

<template>
  <aside class="flex min-h-[28rem] flex-col border-b border-border lg:min-h-[36rem] lg:border-b-0 lg:border-r">
    <div class="space-y-3 border-b border-border p-4">
      <div class="relative">
        <Search class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="query" class="pl-9" :placeholder="t('playground.searchPlaceholder')" :aria-label="t('playground.searchAria')" />
      </div>
      <div class="grid grid-cols-2 gap-2">
        <Select :model-value="category" @update:model-value="updateCategory">
          <SelectTrigger :aria-label="t('playground.categoryAria')"><SelectValue /></SelectTrigger>
          <SelectContent><SelectItem v-for="(label, value) in categoryLabels" :key="value" :value="value">{{ label }}</SelectItem></SelectContent>
        </Select>
        <Select :model-value="risk" @update:model-value="updateRisk">
          <SelectTrigger :aria-label="t('playground.riskAria')"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">{{ t("playground.riskAll") }}</SelectItem>
            <SelectItem value="safe">{{ t("playground.riskSafe") }}</SelectItem>
            <SelectItem value="protected">{{ t("playground.riskProtected") }}</SelectItem>
          </SelectContent>
        </Select>
      </div>
      <p class="text-xs text-muted-foreground">{{ t("playground.matchCount", { count: tools.length }) }}</p>
    </div>

    <ScrollArea class="h-[24rem] lg:h-[30rem]">
      <div class="p-2">
        <div v-if="isLoading" class="space-y-1">
          <Skeleton v-for="index in 8" :key="index" class="h-20" />
        </div>
        <TransitionGroup v-else name="tool-item" tag="div" class="tool-list space-y-1">
          <button
            v-for="(tool, index) in tools"
            :key="tool.name"
            type="button"
            class="w-full rounded-lg border px-3 py-2.5 text-left transition-colors hover:bg-muted/70"
            :class="selectedName === tool.name ? 'border-primary/40 bg-primary/5 shadow-sm' : 'border-transparent'"
            :style="{ '--tool-stagger': `${Math.min(index, 5) * 18}ms` }"
            @click="emit('select', tool.name)"
          >
            <div class="flex items-start justify-between gap-2">
              <code class="break-all text-xs font-semibold text-foreground">{{ tool.name }}</code>
              <ShieldAlert v-if="tool.requiresConfirmation" class="size-4 shrink-0 text-destructive" />
              <Wrench v-else class="size-4 shrink-0 text-muted-foreground" />
            </div>
            <p class="mt-1.5 line-clamp-2 text-xs leading-5 text-muted-foreground">{{ tool.description }}</p>
            <Badge variant="outline" class="mt-2 text-[10px]">{{ categoryLabels[tool.category] }}</Badge>
          </button>
          <p v-if="tools.length === 0" key="empty" class="px-3 py-12 text-center text-sm text-muted-foreground">{{ t("playground.noMatch") }}</p>
        </TransitionGroup>
      </div>
    </ScrollArea>
  </aside>
</template>

<style scoped>
.tool-list {
  position: relative;
}

.tool-item-enter-active {
  transition: opacity 180ms ease, transform 180ms ease;
  transition-delay: var(--tool-stagger, 0ms);
}

.tool-item-leave-active {
  position: absolute;
  right: 0;
  left: 0;
  transition: opacity 120ms ease, transform 120ms ease;
}

.tool-item-enter-from,
.tool-item-leave-to {
  opacity: 0;
  transform: translateY(6px) scale(0.99);
}

.tool-item-move {
  transition: transform 180ms ease;
}

@media (prefers-reduced-motion: reduce) {
  .tool-item-enter-active,
  .tool-item-leave-active,
  .tool-item-move {
    transition: none;
  }
}
</style>
