<script setup lang="ts">
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Skeleton } from "@/components/ui/skeleton"
import {
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table"
import type { DashboardResponse } from "@/types"
import {
  ChevronRight,
  CircleDot,
  Database,
  Ellipsis,
  FilePlus2,
  Layers3,
  PencilLine,
  RefreshCcwDot,
  Rows3,
  ShieldCheck,
  Trash2,
} from "lucide-vue-next"

defineProps<{
  dashboard: DashboardResponse | null
  busyAction: string | null
  selectedName: string | null
  isBootstrapping: boolean
}>()

const emit = defineEmits<{
  (event: "select", name: string): void
  (event: "create"): void
  (event: "preset"): void
  (event: "edit", name: string): void
  (event: "clone", name: string): void
  (event: "delete", name: string): void
  (event: "set-default", name: string): void
  (event: "switch-current", name: string): void
  (event: "test", name: string): void
}>()
</script>

<template>
  <Card class="border-border bg-card shadow-sm">
    <CardHeader class="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
      <div class="space-y-1.5">
        <CardTitle class="flex items-center gap-2 text-lg">
          <Database class="size-4" />
          连接工作区
        </CardTitle>
        <CardDescription>以表格视图管理连接，右侧菜单分发默认/当前/测试/编辑等动作。</CardDescription>
      </div>
      <div class="flex flex-wrap gap-2">
        <Button variant="outline" :disabled="busyAction !== null" @click="emit('create')">
          <Rows3 class="size-4" />
          手工新增
        </Button>
        <Button :disabled="busyAction !== null" @click="emit('preset')">
          <FilePlus2 class="size-4" />
          从模板创建
        </Button>
      </div>
    </CardHeader>
    <CardContent class="space-y-4">
      <ScrollArea class="h-[32rem] rounded-md border border-border bg-muted/20">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead class="w-[260px]">连接名</TableHead>
              <TableHead>数据库类型</TableHead>
              <TableHead>状态</TableHead>
              <TableHead>说明</TableHead>
              <TableHead class="w-[84px] text-right">操作</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            <template v-if="isBootstrapping">
              <TableRow v-for="index in 5" :key="index">
                <TableCell colspan="5">
                  <Skeleton class="h-12 w-full rounded-md" />
                </TableCell>
              </TableRow>
            </template>
            <template v-else-if="dashboard?.databases.length">
              <TableRow
                v-for="database in dashboard.databases"
                :key="database.name"
                class="cursor-pointer transition-colors"
                :class="selectedName === database.name ? 'bg-muted/50' : ''"
                @click="emit('select', database.name)"
              >
                <TableCell class="align-top">
                  <div class="flex items-start gap-3">
                    <div class="mt-0.5 rounded-md bg-muted p-2 text-foreground">
                      <Layers3 class="size-4" />
                    </div>
                    <div class="min-w-0 space-y-1">
                      <div class="flex items-center gap-2">
                        <span class="truncate font-medium" :class="selectedName === database.name ? 'text-foreground' : ''">{{ database.name }}</span>
                        <ChevronRight v-if="selectedName === database.name" class="size-4 text-foreground" />
                      </div>
                      <p class="text-xs text-muted-foreground">
                        {{ database.optimizationSettings ? "含优化参数" : "无额外优化项" }}
                      </p>
                    </div>
                  </div>
                </TableCell>
                <TableCell class="align-top">
                  <Badge variant="secondary">{{ database.dbType }}</Badge>
                </TableCell>
                <TableCell class="align-top">
                  <div class="flex flex-wrap gap-2">
                    <Badge v-if="database.isDefault" variant="outline">
                      默认
                    </Badge>
                    <Badge v-if="database.isCurrent" variant="default">
                      <CircleDot class="size-3 mr-1" />
                      当前
                    </Badge>
                  </div>
                </TableCell>
                <TableCell class="max-w-[320px] align-top text-sm text-muted-foreground">
                  <span class="line-clamp-2">{{ database.description || "暂无说明" }}</span>
                </TableCell>
                <TableCell class="align-top text-right">
                  <DropdownMenu>
                    <DropdownMenuTrigger as-child>
                      <Button variant="ghost" size="icon" @click.stop>
                        <Ellipsis class="size-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end" class="w-48">
                      <DropdownMenuLabel>{{ database.name }}</DropdownMenuLabel>
                      <DropdownMenuGroup>
                        <DropdownMenuItem @click="emit('select', database.name)">
                          <Rows3 class="size-4" />
                          查看详情
                        </DropdownMenuItem>
                        <DropdownMenuItem @click="emit('edit', database.name)">
                          <PencilLine class="size-4" />
                          编辑
                        </DropdownMenuItem>
                        <DropdownMenuItem @click="emit('clone', database.name)">
                          <FilePlus2 class="size-4" />
                          克隆
                        </DropdownMenuItem>
                      </DropdownMenuGroup>
                      <DropdownMenuSeparator />
                      <DropdownMenuGroup>
                        <DropdownMenuItem @click="emit('set-default', database.name)">
                          <ShieldCheck class="size-4" />
                          设为默认
                        </DropdownMenuItem>
                        <DropdownMenuItem @click="emit('switch-current', database.name)">
                          <RefreshCcwDot class="size-4" />
                          切到当前
                        </DropdownMenuItem>
                        <DropdownMenuItem @click="emit('test', database.name)">
                          <Database class="size-4" />
                          测试连接
                        </DropdownMenuItem>
                      </DropdownMenuGroup>
                      <DropdownMenuSeparator />
                      <DropdownMenuItem variant="destructive" @click="emit('delete', database.name)">
                        <Trash2 class="size-4" />
                        删除
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </TableCell>
              </TableRow>
            </template>
            <template v-else>
              <TableEmpty :colspan="5">
                当前还没有数据库连接。可以先初始化配置，再从模板创建第一个连接。
              </TableEmpty>
            </template>
          </TableBody>
        </Table>
      </ScrollArea>
    </CardContent>
  </Card>
</template>
