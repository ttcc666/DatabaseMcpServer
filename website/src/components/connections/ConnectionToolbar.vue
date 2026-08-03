<script setup lang="ts">
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select"
import type { ConnectionSortKey, ConnectionStatusFilter } from "@/types/connections"
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

const statusOptions: { value: ConnectionStatusFilter, label: string }[] = [
  { value: "all", label: "全部状态" },
  { value: "default", label: "默认连接" },
  { value: "current", label: "当前连接" },
  { value: "dangerous", label: "允许危险操作" },
  { value: "healthy", label: "健康" },
  { value: "unhealthy", label: "异常" },
  { value: "unchecked", label: "未检查" },
]

const sortOptions: { value: ConnectionSortKey, label: string }[] = [
  { value: "name", label: "按名称" },
  { value: "dbType", label: "按类型" },
  { value: "status", label: "按状态" },
  { value: "latency", label: "按延迟" },
]

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
        <Input v-model="query" class="pl-9" placeholder="搜索名称、类型或说明" aria-label="搜索数据库连接" />
      </div>

      <Select :model-value="typeFilter" @update:model-value="onTypeFilter">
        <SelectTrigger aria-label="按数据库类型筛选">
          <SelectValue placeholder="全部类型" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="all">全部类型</SelectItem>
          <SelectItem v-for="option in typeOptions" :key="option" :value="option">{{ option }}</SelectItem>
        </SelectContent>
      </Select>

      <Select :model-value="statusFilter" @update:model-value="onStatusFilter">
        <SelectTrigger aria-label="按连接状态筛选">
          <SelectValue placeholder="全部状态" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
        </SelectContent>
      </Select>

      <Select :model-value="sortKey" @update:model-value="onSort">
        <SelectTrigger aria-label="连接排序方式">
          <SelectValue placeholder="排序" />
        </SelectTrigger>
        <SelectContent>
          <SelectItem v-for="option in sortOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
        </SelectContent>
      </Select>

      <div class="flex gap-2 sm:col-span-2 xl:col-span-1">
        <Button variant="outline" size="icon" :title="sortDescending ? '当前降序，点击切换升序' : '当前升序，点击切换降序'" @click="emit('sort', sortKey)">
          <ArrowDownAZ v-if="sortDescending" class="size-4" />
          <ArrowUpAZ v-else class="size-4" />
          <span class="sr-only">切换排序方向</span>
        </Button>
        <Button variant="outline" size="icon" :disabled="!hasActiveFilters" title="重置筛选" @click="emit('reset')">
          <RotateCcw class="size-4" />
          <span class="sr-only">重置筛选</span>
        </Button>
      </div>
    </div>

    <div class="flex flex-wrap items-center justify-between gap-3 text-sm text-muted-foreground">
      <span>显示 {{ filteredCount }} / {{ totalCount }} 个连接</span>
      <Button size="sm" :disabled="isChecking || totalCount === 0" @click="emit('health-check')">
        <HeartPulse class="size-4" :class="isChecking ? 'animate-pulse' : ''" />
        {{ isChecking ? "正在检查" : "全部健康检查" }}
      </Button>
    </div>
  </div>
</template>
