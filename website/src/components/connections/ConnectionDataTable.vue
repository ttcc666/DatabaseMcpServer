<script setup lang="ts">
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { DropdownMenu, DropdownMenuContent, DropdownMenuGroup, DropdownMenuItem, DropdownMenuLabel, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Skeleton } from "@/components/ui/skeleton"
import { Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import type { ConnectionSortKey, ConnectionTableItem } from "@/types/connections"
import { useI18n } from "vue-i18n"
import { Activity, ChevronRight, CircleCheck, CircleDot, CircleX, Database, Ellipsis, FilePlus2, Layers3, PencilLine, RefreshCcwDot, Rows3, ShieldCheck, Trash2 } from "lucide-vue-next"

const props = defineProps<{
  rows: ConnectionTableItem[]
  totalCount: number
  selectedName: string | null
  busyAction: string | null
  isBootstrapping: boolean
  sortKey: ConnectionSortKey
  sortDescending: boolean
}>()

const emit = defineEmits<{
  (event: "sort", key: ConnectionSortKey): void
  (event: "select" | "edit" | "clone" | "delete" | "set-default" | "switch-current" | "test", name: string): void
}>()

const { t } = useI18n()

function ariaSort(key: ConnectionSortKey) {
  if (props.sortKey !== key) return "none"
  return props.sortDescending ? "descending" : "ascending"
}
</script>

<template>
  <ScrollArea class="h-full min-h-[22rem] border-b border-border/70">
    <Table>
      <TableHeader>
        <TableRow class="hover:bg-transparent">
          <TableHead class="w-[220px] sm:w-[250px]" :aria-sort="ariaSort('name')">
            <button class="font-medium hover:text-foreground" @click="emit('sort', 'name')">{{ t("dataTable.name") }}</button>
          </TableHead>
          <TableHead :aria-sort="ariaSort('dbType')">
            <button class="font-medium hover:text-foreground" @click="emit('sort', 'dbType')">{{ t("dataTable.dbType") }}</button>
          </TableHead>
          <TableHead :aria-sort="ariaSort('status')">
            <button class="font-medium hover:text-foreground" @click="emit('sort', 'status')">{{ t("dataTable.status") }}</button>
          </TableHead>
          <TableHead :aria-sort="ariaSort('latency')">
            <button class="font-medium hover:text-foreground" @click="emit('sort', 'latency')">{{ t("dataTable.health") }}</button>
          </TableHead>
          <TableHead class="hidden min-w-[160px] lg:table-cell">{{ t("dataTable.description") }}</TableHead>
          <TableHead class="w-[72px] text-right">{{ t("dataTable.actions") }}</TableHead>
        </TableRow>
      </TableHeader>
      <TableBody>
        <template v-if="isBootstrapping">
          <TableRow v-for="index in 5" :key="index">
            <TableCell colspan="6"><Skeleton class="h-12 w-full rounded-md" /></TableCell>
          </TableRow>
        </template>
        <template v-else-if="rows.length">
          <TableRow
            v-for="database in rows"
            :key="database.name"
            class="cursor-pointer transition-colors"
            :class="selectedName === database.name ? 'bg-primary/5' : ''"
            @click="emit('select', database.name)"
          >
            <TableCell class="align-top">
              <div class="flex items-start gap-3">
                <div class="mt-0.5 rounded-md bg-muted p-2 text-muted-foreground"><Layers3 class="size-4" /></div>
                <div class="min-w-0 space-y-1">
                  <div class="flex items-center gap-1.5">
                    <span class="truncate font-medium">{{ database.name }}</span>
                    <ChevronRight v-if="selectedName === database.name" class="size-4 shrink-0 text-primary" />
                  </div>
                  <p class="text-xs text-muted-foreground">{{ database.optimizationSettings ? t("dataTable.hasOptimization") : t("dataTable.noOptimization") }}</p>
                  <p class="line-clamp-2 text-xs text-muted-foreground lg:hidden">{{ database.description || t("common.noDescriptionShort") }}</p>
                </div>
              </div>
            </TableCell>
            <TableCell class="align-top"><Badge variant="secondary">{{ database.dbType }}</Badge></TableCell>
            <TableCell class="align-top">
              <div class="flex max-w-[210px] flex-wrap gap-1.5">
                <Badge v-if="database.isDefault" variant="outline">{{ t("common.default") }}</Badge>
                <Badge v-if="database.isCurrent"><CircleDot class="mr-1 size-3" />{{ t("common.current") }}</Badge>
                <Badge v-if="database.allowDangerousOperations" variant="destructive">{{ t("dataTable.dangerousOps") }}</Badge>
                <span v-if="!database.isDefault && !database.isCurrent && !database.allowDangerousOperations" class="text-xs text-muted-foreground">{{ t("common.ordinary") }}</span>
              </div>
            </TableCell>
            <TableCell class="align-top">
              <span v-if="!database.health" class="inline-flex items-center gap-1.5 text-xs text-muted-foreground"><Activity class="size-3.5" />{{ t("dataTable.unchecked") }}</span>
              <span v-else-if="database.health.isHealthy" class="inline-flex items-center gap-1.5 text-xs font-medium text-emerald-700 dark:text-emerald-400">
                <CircleCheck class="size-3.5" />{{ database.health.responseTimeMs }} ms
              </span>
              <Tooltip v-else>
                <TooltipTrigger as-child>
                  <button class="inline-flex items-center gap-1.5 text-xs font-medium text-destructive" @click.stop>
                    <CircleX class="size-3.5" />{{ t("dataTable.unhealthy") }}
                  </button>
                </TooltipTrigger>
                <TooltipContent class="max-w-sm">{{ database.health.errorMessage || t("dataTable.healthFailed") }}</TooltipContent>
              </Tooltip>
            </TableCell>
            <TableCell class="hidden max-w-[260px] align-top text-sm text-muted-foreground lg:table-cell"><span class="line-clamp-2">{{ database.description || t("common.noDescriptionShort") }}</span></TableCell>
            <TableCell class="align-top text-right">
              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button variant="ghost" size="icon" :disabled="busyAction !== null" @click.stop><Ellipsis class="size-4" /><span class="sr-only">{{ t("dataTable.connectionActions") }}</span></Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end" :collision-padding="8" class="w-48">
                  <DropdownMenuLabel>{{ database.name }}</DropdownMenuLabel>
                  <DropdownMenuGroup>
                    <DropdownMenuItem @click="emit('select', database.name)"><Rows3 class="size-4" />{{ t("dataTable.viewDetails") }}</DropdownMenuItem>
                    <DropdownMenuItem @click="emit('edit', database.name)"><PencilLine class="size-4" />{{ t("dataTable.edit") }}</DropdownMenuItem>
                    <DropdownMenuItem @click="emit('clone', database.name)"><FilePlus2 class="size-4" />{{ t("dataTable.clone") }}</DropdownMenuItem>
                  </DropdownMenuGroup>
                  <DropdownMenuSeparator />
                  <DropdownMenuGroup>
                    <DropdownMenuItem @click="emit('set-default', database.name)"><ShieldCheck class="size-4" />{{ t("dataTable.setDefault") }}</DropdownMenuItem>
                    <DropdownMenuItem @click="emit('switch-current', database.name)"><RefreshCcwDot class="size-4" />{{ t("dataTable.switchCurrent") }}</DropdownMenuItem>
                    <DropdownMenuItem @click="emit('test', database.name)"><Database class="size-4" />{{ t("dataTable.testConnection") }}</DropdownMenuItem>
                  </DropdownMenuGroup>
                  <DropdownMenuSeparator />
                  <DropdownMenuItem variant="destructive" @click="emit('delete', database.name)"><Trash2 class="size-4" />{{ t("common.delete") }}</DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </TableCell>
          </TableRow>
        </template>
        <TableEmpty v-else :colspan="6">
          {{ totalCount ? t("dataTable.emptyFiltered") : t("dataTable.emptyAll") }}
        </TableEmpty>
      </TableBody>
    </Table>
  </ScrollArea>
</template>
