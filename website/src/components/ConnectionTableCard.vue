<script setup lang="ts">
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import ConnectionDataTable from "@/components/connections/ConnectionDataTable.vue"
import ConnectionHealthSummary from "@/components/connections/ConnectionHealthSummary.vue"
import ConnectionToolbar from "@/components/connections/ConnectionToolbar.vue"
import { useConnectionTable } from "@/composables/useConnectionTable"
import type { DashboardResponse } from "@/types"
import type { ConnectionHealthResponse, ConnectionHealthResult } from "@/types/connections"
import { computed } from "vue"
import { Database, FilePlus2, Rows3 } from "lucide-vue-next"

const props = defineProps<{
  dashboard: DashboardResponse | null
  healthResponse: ConnectionHealthResponse | null
  healthResults: ReadonlyMap<string, ConnectionHealthResult>
  isCheckingHealth: boolean
  busyAction: string | null
  selectedName: string | null
  isBootstrapping: boolean
}>()

const emit = defineEmits<{
  (event: "health-check" | "create" | "preset"): void
  (event: "select" | "edit" | "clone" | "delete" | "set-default" | "switch-current" | "test", name: string): void
}>()

const connections = computed(() => props.dashboard?.databases ?? [])
const healthResults = computed(() => props.healthResults)
const table = useConnectionTable(connections, healthResults)
</script>

<template>
  <Card class="h-full min-h-0 min-w-0 overflow-hidden border-border bg-card shadow-sm">
    <CardHeader class="flex shrink-0 flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
      <div class="space-y-1.5">
        <CardTitle class="flex items-center gap-2 text-lg"><Database class="size-4" />连接工作区</CardTitle>
        <CardDescription>搜索、筛选和检查所有已配置连接。</CardDescription>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button variant="outline" :disabled="busyAction !== null" @click="emit('create')"><Rows3 class="size-4" />手工新增</Button>
        <Button :disabled="busyAction !== null" @click="emit('preset')"><FilePlus2 class="size-4" />从模板创建</Button>
      </div>
    </CardHeader>

    <CardContent class="flex min-h-0 flex-1 flex-col p-0">
      <ConnectionToolbar
        v-model:query="table.query.value"
        v-model:type-filter="table.typeFilter.value"
        v-model:status-filter="table.statusFilter.value"
        :type-options="table.typeOptions.value"
        :total-count="connections.length"
        :filtered-count="table.rows.value.length"
        :sort-key="table.sortKey.value"
        :sort-descending="table.sortDescending.value"
        :has-active-filters="table.hasActiveFilters.value"
        :is-checking="isCheckingHealth"
        @sort="table.setSort"
        @reset="table.resetFilters"
        @health-check="emit('health-check')"
      />
      <ConnectionHealthSummary :response="healthResponse" />
      <ConnectionDataTable
        class="min-h-0 flex-1"
        :rows="table.rows.value"
        :total-count="connections.length"
        :selected-name="selectedName"
        :busy-action="busyAction"
        :is-bootstrapping="isBootstrapping"
        :sort-key="table.sortKey.value"
        :sort-descending="table.sortDescending.value"
        @sort="table.setSort"
        @select="emit('select', $event)"
        @edit="emit('edit', $event)"
        @clone="emit('clone', $event)"
        @delete="emit('delete', $event)"
        @set-default="emit('set-default', $event)"
        @switch-current="emit('switch-current', $event)"
        @test="emit('test', $event)"
      />
    </CardContent>
  </Card>
</template>
