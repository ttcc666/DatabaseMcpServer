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
import AnimatedCounter from "@/components/motion/AnimatedCounter.vue"
import type { ConfigContext, DashboardResponse } from "@/types"
import { useI18n } from "vue-i18n"
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

const { t } = useI18n()
</script>

<template>
  <!-- Multi-root: parent 2x2 grid places these as row 1. h-full makes both cards share the row height. -->
  <Card class="h-full min-w-0 overflow-hidden border-border bg-card shadow-sm">
    <CardHeader class="gap-4 border-b border-border/80">
      <div class="flex flex-wrap items-start justify-between gap-4">
        <div class="min-w-0 space-y-2">
          <Badge variant="outline" class="gap-2 px-3 py-1 text-[11px] tracking-[0.16em] uppercase">
            <Sparkles class="size-3" />
            {{ t("configHero.localConsole") }}
          </Badge>
          <CardTitle class="text-2xl tracking-tight sm:text-3xl">
            {{ t("configHero.title") }}
          </CardTitle>
          <CardDescription class="max-w-2xl text-sm leading-6 text-muted-foreground sm:text-[15px]">
            {{ t("configHero.descriptionPrefix") }}
            <code class="rounded bg-muted px-1 py-0.5 text-[0.9em]">databases.json</code>
            {{ t("configHero.descriptionMiddle") }}
            <code class="rounded bg-muted px-1 py-0.5 text-[0.9em]">cli-state.json</code>
            {{ t("configHero.descriptionSuffix") }}
          </CardDescription>
        </div>
        <Button variant="outline" :disabled="busyAction !== null" @click="emit('refresh')">
          <RefreshCw class="size-4" />
          {{ t("common.refresh") }}
        </Button>
      </div>

      <div v-if="lastMessage" class="rounded-lg border border-border bg-muted/50 px-4 py-3 text-sm text-muted-foreground">
        {{ lastMessage }}
      </div>
    </CardHeader>

    <CardContent class="grid flex-1 content-start gap-4 p-6 md:grid-cols-2 xl:grid-cols-4">
      <template v-if="isBootstrapping">
        <Skeleton v-for="index in 4" :key="index" class="h-28 rounded-xl" />
      </template>
      <template v-else>
        <div class="rounded-xl border border-border/80 bg-muted/30 p-4">
          <p class="text-xs font-medium tracking-[0.12em] text-muted-foreground uppercase">{{ t("configHero.configSource") }}</p>
          <p class="mt-3 break-all text-lg font-semibold leading-snug sm:text-xl">{{ context?.configSource ?? t("common.unresolved") }}</p>
          <p class="mt-2 text-sm text-muted-foreground">
            {{ context?.configExists ? t("configHero.configFound") : t("configHero.configMissing") }}
          </p>
        </div>
        <div class="rounded-xl border border-border/80 bg-muted/30 p-4">
          <p class="text-xs font-medium tracking-[0.12em] text-muted-foreground uppercase">{{ t("configHero.defaultConnection") }}</p>
          <p class="mt-3 break-all text-lg font-semibold leading-snug sm:text-xl">{{ dashboard?.currentDefaultDatabase ?? t("common.notSet") }}</p>
          <p class="mt-2 text-sm text-muted-foreground">{{ t("configHero.defaultHint", { file: "databases.json" }) }}</p>
        </div>
        <div class="rounded-xl border border-border/80 bg-muted/30 p-4">
          <p class="text-xs font-medium tracking-[0.12em] text-muted-foreground uppercase">{{ t("configHero.currentConnection") }}</p>
          <p class="mt-3 break-all text-lg font-semibold leading-snug sm:text-xl">{{ dashboard?.currentDatabase ?? t("common.notSet") }}</p>
          <p class="mt-2 text-sm text-muted-foreground">{{ t("configHero.currentHint", { file: "cli-state.json" }) }}</p>
        </div>
        <div class="rounded-xl border border-border/80 bg-muted/30 p-4">
          <p class="text-xs font-medium tracking-[0.12em] text-muted-foreground uppercase">{{ t("configHero.totalConnections") }}</p>
          <AnimatedCounter :value="dashboard?.totalDatabases ?? 0" :font-size="20" :font-weight="600" class="mt-3" />
          <p class="mt-2 text-sm text-muted-foreground">{{ t("configHero.totalHint") }}</p>
        </div>
      </template>
    </CardContent>
  </Card>

  <Card class="h-full min-w-0 border-border bg-card shadow-sm">
    <CardHeader>
      <CardTitle class="text-lg">{{ t("configHero.targetPathTitle") }}</CardTitle>
      <CardDescription>{{ t("configHero.targetPathDescription") }}</CardDescription>
    </CardHeader>
    <CardContent class="flex flex-1 flex-col gap-4">
      <ScrollArea class="h-24 rounded-lg border border-dashed border-border bg-muted/30 p-4">
        <code class="text-sm leading-6 break-all">{{ context?.configPath ?? t("common.unresolved") }}</code>
      </ScrollArea>

      <Alert v-if="!context?.configExists" variant="destructive">
        <AlertTriangle class="size-4" />
        <AlertTitle>{{ t("configHero.configNotCreatedTitle") }}</AlertTitle>
        <AlertDescription class="space-y-3">
          <p>{{ t("configHero.configNotCreatedDescription") }}</p>
          <div class="flex flex-wrap gap-2">
            <Button :disabled="busyAction === 'init'" @click="emit('initialize', false)">
              {{ t("configHero.initializeConfig") }}
            </Button>
            <Button variant="outline" :disabled="busyAction === 'init'" @click="emit('initialize', true)">
              {{ t("configHero.forceReset") }}
            </Button>
          </div>
        </AlertDescription>
      </Alert>

      <Separator />

      <div class="mt-auto space-y-2 text-sm text-muted-foreground">
        <p class="font-medium text-foreground">{{ t("configHero.semanticsTitle") }}</p>
        <p>{{ t("configHero.semanticsDefault") }}</p>
        <p>{{ t("configHero.semanticsCurrent") }}</p>
      </div>
    </CardContent>
  </Card>
</template>
