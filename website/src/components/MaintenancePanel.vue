<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { ScrollArea } from "@/components/ui/scroll-area"
import { Separator } from "@/components/ui/separator"
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from "@/components/ui/tabs"
import type { ConfigContext, DatabaseDetail, DashboardResponse } from "@/types"
import { AlertTriangle, ArrowRightLeft, Download, HeartPulse, Upload } from "lucide-vue-next"

defineProps<{
  context: ConfigContext | null
  dashboard: DashboardResponse | null
  selectedDatabase: DatabaseDetail | null
  diagnostics: string | null
  busyAction: string | null
}>()

const emit = defineEmits<{
  (event: "validate"): void
  (event: "doctor"): void
  (event: "export"): void
  (event: "pick-import"): void
}>()
</script>

<template>
  <Card class="border-border bg-card shadow-sm">
    <CardHeader class="space-y-3">
      <div class="flex items-center gap-2">
        <HeartPulse class="size-4" />
        <CardTitle class="text-lg">维护与诊断</CardTitle>
      </div>
      <CardDescription>把校验、doctor、导入导出和当前选中连接详情集中到一块。</CardDescription>
    </CardHeader>
    <CardContent class="space-y-5">
      <div class="rounded-md border border-border bg-muted/20 p-4">
        <div class="flex items-start justify-between gap-4">
          <div>
            <p class="text-sm font-medium">当前选中连接</p>
            <p class="mt-2 text-lg font-semibold">
              {{ selectedDatabase?.name ?? "未选中" }}
            </p>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ selectedDatabase?.dbType ?? "请选择左侧表格中的连接" }}
            </p>
          </div>
          <div class="flex flex-wrap gap-2">
            <Badge v-if="selectedDatabase?.isDefault" variant="outline">默认</Badge>
            <Badge v-if="selectedDatabase?.allowDangerousOperations" variant="destructive">允许危险操作</Badge>
          </div>
        </div>

        <Separator class="my-4" />

        <div class="space-y-2 text-sm text-muted-foreground">
          <p>{{ selectedDatabase?.description || "暂无描述。" }}</p>
          <code v-if="selectedDatabase?.connectionString" class="block break-all rounded-md bg-muted px-3 py-2 text-xs">
            {{ selectedDatabase.connectionString }}
          </code>
        </div>
      </div>

      <div class="grid gap-3 sm:grid-cols-2">
        <Button variant="outline" class="justify-start" :disabled="busyAction !== null" @click="emit('validate')">
          <HeartPulse class="size-4" />
          校验配置
        </Button>
        <Button variant="outline" class="justify-start" :disabled="busyAction !== null" @click="emit('doctor')">
          <AlertTriangle class="size-4" />
          运行 doctor
        </Button>
        <Button variant="outline" class="justify-start" :disabled="busyAction !== null" @click="emit('export')">
          <Download class="size-4" />
          导出 JSON
        </Button>
        <Button class="justify-start" variant="secondary" :disabled="busyAction !== null" @click="emit('pick-import')">
          <Upload class="size-4" />
          导入 JSON
        </Button>
      </div>

      <Tabs default-value="diagnostics" class="gap-4">
        <TabsList class="grid w-full grid-cols-3">
          <TabsTrigger value="diagnostics">诊断输出</TabsTrigger>
          <TabsTrigger value="semantics">语义说明</TabsTrigger>
          <TabsTrigger value="target">目标文件</TabsTrigger>
        </TabsList>

        <TabsContent value="diagnostics">
          <ScrollArea class="h-[18rem] rounded-md border border-border bg-zinc-950 text-zinc-100">
            <pre class="p-4 text-xs leading-6 whitespace-pre-wrap">{{ diagnostics || "这里会显示 validate / doctor / test 的 JSON 输出。" }}</pre>
          </ScrollArea>
        </TabsContent>

        <TabsContent value="semantics">
          <Alert>
            <ArrowRightLeft class="size-4" />
            <AlertTitle>默认连接 vs 当前连接</AlertTitle>
            <AlertDescription class="space-y-3">
              <p>默认连接：持久写回 <code>databases.json</code>，用作没有当前状态时的回退。</p>
              <p>当前连接：按配置文件路径隔离持久化到 <code>cli-state.json</code>，适合临时切换上下文。</p>
            </AlertDescription>
          </Alert>
        </TabsContent>

        <TabsContent value="target">
          <div class="rounded-md border border-border bg-muted/20 p-4 text-sm text-muted-foreground">
            <p class="font-medium text-foreground">配置路径</p>
            <code class="mt-3 block break-all">{{ context?.configPath ?? "未解析" }}</code>
            <p class="mt-4 font-medium text-foreground">当前来源</p>
            <p class="mt-2">{{ context?.configSource ?? "未解析" }}</p>
            <p class="mt-4 font-medium text-foreground">连接数量</p>
            <p class="mt-2">{{ dashboard?.totalDatabases ?? 0 }}</p>
          </div>
        </TabsContent>
      </Tabs>
    </CardContent>
  </Card>
</template>
