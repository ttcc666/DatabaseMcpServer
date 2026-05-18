<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Button } from "@/components/ui/button"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Separator } from "@/components/ui/separator"
import { Skeleton } from "@/components/ui/skeleton"
import type { ConfigContext, DashboardResponse } from "@/types"
import { AlertTriangle, RefreshCw, Sparkles } from "lucide-vue-next"

defineProps<{
  context: ConfigContext | null
  dashboard: DashboardResponse | null
  isBootstrapping: boolean
  busyAction: string | null
  lastMessage: string | null
}>()

const emit = defineEmits<{
  (event: "refresh"): void
  (event: "initialize", force: boolean): void
}>()
</script>

<template>
  <section class="grid gap-5 xl:grid-cols-[minmax(0,1.4fr)_minmax(320px,0.85fr)]">
    <Card class="overflow-hidden border-border bg-card shadow-sm">
      <CardHeader class="gap-5 border-b border-border bg-card">
        <div class="flex flex-wrap items-start justify-between gap-4">
          <div class="space-y-2">
            <Badge variant="outline" class="gap-2 px-3 py-1 text-[11px] tracking-[0.2em] uppercase">
              <Sparkles class="size-3" />
              shadcn-vue console
            </Badge>
            <CardTitle class="font-serif text-4xl tracking-tight sm:text-5xl">
              DatabaseMcpServer 配置台
            </CardTitle>
            <CardDescription class="max-w-2xl text-sm leading-6 text-muted-foreground sm:text-base">
              在一个本地工作台里统一管理 <code>databases.json</code> 和 <code>cli-state.json</code>，
              把默认连接、当前连接、模板创建、诊断输出和导入导出放到同一视图。
            </CardDescription>
          </div>
          <Button variant="outline" :disabled="busyAction !== null" @click="emit('refresh')">
            <RefreshCw class="size-4" />
            刷新
          </Button>
        </div>

        <div v-if="lastMessage" class="rounded-md border border-border bg-muted/50 px-4 py-3 text-sm text-muted-foreground">
          {{ lastMessage }}
        </div>
      </CardHeader>

      <CardContent class="grid gap-4 p-6 md:grid-cols-2 xl:grid-cols-4">
        <template v-if="isBootstrapping">
          <Skeleton v-for="index in 4" :key="index" class="h-28 rounded-xl" />
        </template>
        <template v-else>
          <div class="rounded-xl border border-border bg-muted/20 p-4">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">配置来源</p>
            <p class="mt-3 text-xl font-semibold">{{ context?.configSource ?? "未解析" }}</p>
            <p class="mt-2 text-sm text-muted-foreground">
              {{ context?.configExists ? "已找到可用配置文件" : "当前目标路径尚未创建" }}
            </p>
          </div>
          <div class="rounded-xl border border-border bg-muted/20 p-4">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">默认连接</p>
            <p class="mt-3 text-xl font-semibold">{{ dashboard?.currentDefaultDatabase ?? "未设置" }}</p>
            <p class="mt-2 text-sm text-muted-foreground">写入 <code>databases.json</code></p>
          </div>
          <div class="rounded-xl border border-border bg-muted/20 p-4">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">当前连接</p>
            <p class="mt-3 text-xl font-semibold">{{ dashboard?.currentDatabase ?? "未设置" }}</p>
            <p class="mt-2 text-sm text-muted-foreground">写入 <code>cli-state.json</code></p>
          </div>
          <div class="rounded-xl border border-border bg-muted/20 p-4">
            <p class="text-xs uppercase tracking-[0.18em] text-muted-foreground">连接总数</p>
            <p class="mt-3 text-xl font-semibold">{{ dashboard?.totalDatabases ?? 0 }}</p>
            <p class="mt-2 text-sm text-muted-foreground">已注册数据库连接项</p>
          </div>
        </template>
      </CardContent>
    </Card>

    <Card class="border-border bg-card shadow-sm">
      <CardHeader>
        <CardTitle class="text-lg">当前目标路径</CardTitle>
        <CardDescription>CLI 解析顺序的最终结果；`-web --config` 传参会覆盖这里。</CardDescription>
      </CardHeader>
      <CardContent class="space-y-4">
        <ScrollArea class="h-24 rounded-md border border-dashed border-border bg-muted/20 p-4">
          <code class="text-sm leading-6 break-all">{{ context?.configPath ?? "未解析" }}</code>
        </ScrollArea>

        <Alert v-if="!context?.configExists" variant="destructive">
          <AlertTriangle class="size-4" />
          <AlertTitle>配置文件尚未创建</AlertTitle>
          <AlertDescription class="space-y-3">
            <p>可以先初始化默认配置，再通过页面补充连接。</p>
            <div class="flex flex-wrap gap-2">
              <Button :disabled="busyAction === 'init'" @click="emit('initialize', false)">
                初始化配置
              </Button>
              <Button variant="outline" :disabled="busyAction === 'init'" @click="emit('initialize', true)">
                强制重置
              </Button>
            </div>
          </AlertDescription>
        </Alert>

        <Separator />

        <div class="space-y-2 text-sm text-muted-foreground">
          <p class="font-medium text-foreground">语义提示</p>
          <p>默认连接：持久写回配置文件，供 CLI / MCP 默认回退使用。</p>
          <p>当前连接：按配置文件路径隔离保存，适合临时切换执行上下文。</p>
        </div>
      </CardContent>
    </Card>
  </section>
</template>
