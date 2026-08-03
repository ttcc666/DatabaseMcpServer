<script setup lang="ts">
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { ConnectionSortKey, ConnectionStatusFilter } from "@/types/connections"
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { ArrowDownAZ, ArrowUpAZ, HeartPulse, RotateCcw, Search } from "lucide-vue-next"

defineProps<{
  typeOptions: string[]
  totalCount: number
  filteredCount: number
  sortKey: ConnectionSortKey
  sortDescending: boolean
  hasActiveFilters: boolean
  isChecking: boolean
}>()

const query = defineModel<string>("query", { required: true })
const typeFilter = defineModel<string>("typeFilter", { required: true })
const statusFilter = defineModel<ConnectionStatusFilter>("statusFilter", { required: true })

const emit = defineEmits<{
  (event: "sort", key: ConnectionSortKey): void
  (event: "reset"): void
  (event: "health-check"): void
}>()

const { t } = useI18n()

const statusOptions = computed(() => [
  { value: "all" as const, label: t("toolbar.statusAll") },
  { value: "default" as const, label: t("toolbar.statusDefault") },
  { value: "current" as const, label: t("toolbar.statusCurrent") },
  { value: "dangerous" as const, label: t("toolbar.statusDangerous") },
  { value: "healthy" as const, label: t("toolbar.statusHealthy") },
  { value: "unhealthy" as const, label: t("toolbar.statusUnhealthy") },
  { value: "unchecked" as const, label: t("toolbar.statusUnchecked") },
])

const sortOptions = computed(() => [
  { value: "name" as const, label: t("toolbar.sortByName") },
  { value: "dbType" as const, label: t("toolbar.sortByType") },
  { value: "status" as const, label: t("toolbar.sortByStatus") },
  { value: "latency" as const, label: t("toolbar.sortByLatency") },
])

function onTypeFilter(value: unknown) {
  if (typeof value === "string") typeFilter.value = value
}

function onStatusFilter(value: unknown) {
  if (typeof value === "string") statusFilter.value = value as ConnectionStatusFilter
}

function onSort(value: unknown) {
  if (typeof value === "string") emit("sort", value as ConnectionSortKey)
}
</script>

<template>
  <div class="space-y-3 border-y border-border/70 bg-muted/30 px-4 py-4 sm:px-5">
    <div class="grid gap-3 sm:grid-cols-2 xl:grid-cols-[minmax(220px,1.4fr)_minmax(140px,0.8fr)_minmax(140px,0.8fr)_minmax(120px,0.7fr)_auto]">
      <div class="relative sm:col-span-2 xl:col-span-1">
        <Search class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-muted-foreground" />
        <Input v-model="query" class="pl-9" :placeholder="t('toolbar.searchPlaceholder')" :aria-label="t('toolbar.searchAria')" />
      </div>

      <Select :model-value="typeFilter" @update:model-value="onTypeFilter">
        <SelectTrigger :aria-label="t('toolbar.filterByType')">
          <SelectValue :placeholder="t('toolbar.allTypes')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">{{ t("toolbar.allTypes") }}</SelectItem>
          <SelectItem v-for="option in typeOptions" :key="option" :value="option">{{ option }}</SelectItem>
        </SelectContent>
      </Select>

      <Select :model-value="statusFilter" @update:model-value="onStatusFilter">
        <SelectTrigger :aria-label="t('toolbar.filterByStatus')">
          <SelectValue :placeholder="t('toolbar.statusAll')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
        </SelectContent>
      </Select>

      <Select :model-value="sortKey" @update:model-value="onSort">
        <SelectTrigger :aria-label="t('toolbar.sortAria')">
          <SelectValue :placeholder="t('toolbar.sortPlaceholder')" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="option in sortOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
        </SelectContent>
      </Select>

      <div class="flex gap-2 sm:col-span-2 xl:col-span-1">
        <Button variant="outline" size="icon" :title="sortDescending ? t('toolbar.sortDesc') : t('toolbar.sortAsc')" @click="emit('sort', sortKey)">
          <ArrowDownAZ v-if="sortDescending" class="size-4" />
          <ArrowUpAZ v-else class="size-4" />
          <span class="sr-only">{{ t("toolbar.toggleSortDirection") }}</span>
        </Button>
        <Button variant="outline" size="icon" :disabled="!hasActiveFilters" :title="t('toolbar.resetFilters')" @click="emit('reset')">
          <RotateCcw class="size-4" />
          <span class="sr-only">{{ t("toolbar.resetFilters") }}</span>
        </Button>
      </div>
    </div>

    <div class="flex flex-wrap items-center justify-between gap-3 text-sm text-muted-foreground">
      <span>{{ t("toolbar.showing", { filtered: filteredCount, total: totalCount }) }}</span>
      <Button size="sm" :disabled="isChecking || totalCount === 0" @click="emit('health-check')">
        <HeartPulse class="size-4" :class="isChecking ? 'animate-pulse' : ''" />
        {{ isChecking ? t("toolbar.checking") : t("toolbar.healthCheck") }}
      </Button>
    </div>
  </div>
</template>
