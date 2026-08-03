import {
  getCoreRowModel,
  getSortedRowModel,
  useVueTable,
  type ColumnDef,
  type SortingState,
  type Updater,
} from "@tanstack/vue-table"
import { computed, shallowRef, toValue, type MaybeRefOrGetter } from "vue"
import type { DatabaseSummary } from "@/types"
import type {
  ConnectionHealthResult,
  ConnectionSortKey,
  ConnectionStatusFilter,
  ConnectionTableItem,
} from "@/types/connections"

const columns: ColumnDef<ConnectionTableItem>[] = [
  { id: "name", accessorFn: row => row.name },
  { id: "dbType", accessorFn: row => row.dbType },
  { id: "status", accessorFn: row => getStatusRank(row) },
  { id: "latency", accessorFn: row => row.health?.responseTimeMs ?? Number.MAX_SAFE_INTEGER },
]

export function useConnectionTable(
  connections: MaybeRefOrGetter<readonly DatabaseSummary[]>,
  healthResults: MaybeRefOrGetter<ReadonlyMap<string, ConnectionHealthResult>>,
) {
  const query = shallowRef("")
  const typeFilter = shallowRef("all")
  const statusFilter = shallowRef<ConnectionStatusFilter>("all")
  const sorting = shallowRef<SortingState>([{ id: "name", desc: false }])

  const typeOptions = computed(() => [...new Set(toValue(connections).map(item => item.dbType))]
    .sort((left, right) => left.localeCompare(right)))

  const filteredItems = computed<ConnectionTableItem[]>(() => {
    const normalizedQuery = query.value.trim().toLocaleLowerCase()
    const health = toValue(healthResults)

    return toValue(connections)
      .map(item => ({ ...item, health: health.get(item.name) }))
      .filter(item => typeFilter.value === "all" || item.dbType === typeFilter.value)
      .filter(item => matchesStatus(item, statusFilter.value))
      .filter(item => {
        if (!normalizedQuery) {
          return true
        }

        return [item.name, item.dbType, item.description ?? ""]
          .some(value => value.toLocaleLowerCase().includes(normalizedQuery))
      })
  })

  const table = useVueTable({
    get data() {
      return filteredItems.value
    },
    columns,
    state: {
      get sorting() {
        return sorting.value
      },
    },
    onSortingChange: (updater: Updater<SortingState>) => {
      sorting.value = typeof updater === "function" ? updater(sorting.value) : updater
    },
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
  })

  const rows = computed(() => table.getRowModel().rows.map(row => row.original))
  const sortKey = computed(() => (sorting.value[0]?.id ?? "name") as ConnectionSortKey)
  const sortDescending = computed(() => sorting.value[0]?.desc ?? false)
  const hasActiveFilters = computed(() => Boolean(query.value.trim()) || typeFilter.value !== "all" || statusFilter.value !== "all")

  function setSort(key: ConnectionSortKey) {
    const current = sorting.value[0]
    sorting.value = [{ id: key, desc: current?.id === key ? !current.desc : false }]
  }

  function resetFilters() {
    query.value = ""
    typeFilter.value = "all"
    statusFilter.value = "all"
  }

  return {
    query,
    typeFilter,
    statusFilter,
    rows,
    typeOptions,
    sortKey,
    sortDescending,
    hasActiveFilters,
    setSort,
    resetFilters,
  }
}

export function getStatusRank(item: ConnectionTableItem) {
  if (item.health && !item.health.isHealthy) return 0
  if (item.health?.isHealthy) return 1
  if (item.isCurrent) return 2
  if (item.isDefault) return 3
  if (item.allowDangerousOperations) return 4
  return 5
}

function matchesStatus(item: ConnectionTableItem, status: ConnectionStatusFilter) {
  switch (status) {
    case "default": return item.isDefault
    case "current": return item.isCurrent
    case "dangerous": return item.allowDangerousOperations
    case "healthy": return item.health?.isHealthy === true
    case "unhealthy": return item.health?.isHealthy === false
    case "unchecked": return item.health === undefined
    default: return true
  }
}
