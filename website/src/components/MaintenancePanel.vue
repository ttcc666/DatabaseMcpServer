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
import { useI18n } from "vue-i18n"
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

const { t } = useI18n()
</script>

<template>
  <Card class="h-full min-h-0 min-w-0 border-border bg-card shadow-sm">
    <CardHeader class="shrink-0 space-y-3">
      <div class="flex items-center gap-2">
        <HeartPulse class="size-4" />
        <CardTitle class="text-lg">{{ t("maintenance.title") }}</CardTitle>
      </div>
      <CardDescription>{{ t("maintenance.description") }}</CardDescription>
    </CardHeader>
    <CardContent class="flex min-h-0 flex-1 flex-col gap-5">
      <div class="shrink-0 rounded-md border border-border bg-muted/20 p-4">
        <div class="flex items-start justify-between gap-4">
          <div class="min-w-0">
            <p class="text-sm font-medium">{{ t("maintenance.selectedConnection") }}</p>
            <p class="mt-2 truncate text-lg font-semibold">
              {{ selectedDatabase?.name ?? t("common.noneSelected") }}
            </p>
            <p class="mt-1 text-sm text-muted-foreground">
              {{ selectedDatabase?.dbType ?? t("maintenance.selectHint") }}
            </p>
          </div>
          <div class="flex flex-wrap gap-2">
            <Badge v-if="selectedDatabase?.isDefault" variant="outline">{{ t("common.default") }}</Badge>
            <Badge v-if="selectedDatabase?.enableDangerousOperations" variant="destructive">{{ t("maintenance.enableDangerous") }}</Badge>
          </div>
        </div>

        <Separator class="my-4" />

        <div class="space-y-2 text-sm text-muted-foreground">
          <p>{{ selectedDatabase?.description || t("common.noDescription") }}</p>
          <code v-if="selectedDatabase?.connectionString" class="block break-all rounded-md bg-muted px-3 py-2 text-xs">
            {{ selectedDatabase.connectionString }}
          </code>
        </div>
      </div>

      <div class="grid shrink-0 gap-3 sm:grid-cols-2">
        <Button variant="outline" class="justify-start" :disabled="busyAction !== null" @click="emit('validate')">
          <HeartPulse class="size-4" />
          {{ t("maintenance.validateConfig") }}
        </Button>
        <Button variant="outline" class="justify-start" :disabled="busyAction !== null" @click="emit('doctor')">
          <AlertTriangle class="size-4" />
          {{ t("maintenance.runDoctor") }}
        </Button>
        <Button variant="outline" class="justify-start" :disabled="busyAction !== null" @click="emit('export')">
          <Download class="size-4" />
          {{ t("maintenance.exportJson") }}
        </Button>
        <Button class="justify-start" variant="secondary" :disabled="busyAction !== null" @click="emit('pick-import')">
          <Upload class="size-4" />
          {{ t("maintenance.importJson") }}
        </Button>
      </div>

      <Tabs default-value="diagnostics" class="flex min-h-0 flex-1 flex-col gap-4">
        <TabsList class="grid w-full shrink-0 grid-cols-3">
          <TabsTrigger value="diagnostics">{{ t("maintenance.diagnostics") }}</TabsTrigger>
          <TabsTrigger value="semantics">{{ t("maintenance.semantics") }}</TabsTrigger>
          <TabsTrigger value="target">{{ t("maintenance.target") }}</TabsTrigger>
        </TabsList>

        <TabsContent value="diagnostics" class="mt-0 min-h-0 flex-1 data-[state=inactive]:hidden">
          <ScrollArea class="h-full min-h-[14rem] rounded-md border border-border bg-zinc-950 text-zinc-100">
            <pre class="p-4 text-xs leading-6 whitespace-pre-wrap">{{ diagnostics || t("maintenance.diagnosticsPlaceholder") }}</pre>
          </ScrollArea>
        </TabsContent>

        <TabsContent value="semantics" class="mt-0 min-h-0 flex-1 data-[state=inactive]:hidden">
          <Alert>
            <ArrowRightLeft class="size-4" />
            <AlertTitle>{{ t("maintenance.semanticsTitle") }}</AlertTitle>
            <AlertDescription class="space-y-3">
              <p>{{ t("maintenance.semanticsDefault", { file: "databases.json" }) }}</p>
              <p>{{ t("maintenance.semanticsCurrent", { file: "cli-state.json" }) }}</p>
            </AlertDescription>
          </Alert>
        </TabsContent>

        <TabsContent value="target" class="mt-0 min-h-0 flex-1 data-[state=inactive]:hidden">
          <div class="h-full rounded-md border border-border bg-muted/20 p-4 text-sm text-muted-foreground">
            <p class="font-medium text-foreground">{{ t("maintenance.configPath") }}</p>
            <code class="mt-3 block break-all">{{ context?.configPath ?? t("common.unresolved") }}</code>
            <p class="mt-4 font-medium text-foreground">{{ t("maintenance.currentSource") }}</p>
            <p class="mt-2">{{ context?.configSource ?? t("common.unresolved") }}</p>
            <p class="mt-4 font-medium text-foreground">{{ t("maintenance.connectionCount") }}</p>
            <p class="mt-2">{{ dashboard?.totalDatabases ?? 0 }}</p>
          </div>
        </TabsContent>
      </Tabs>
    </CardContent>
  </Card>
</template>
