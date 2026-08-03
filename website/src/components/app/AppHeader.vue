<script setup lang="ts">
import { Button } from "@/components/ui/button"
import LanguageMenu from "./LanguageMenu.vue"
import ThemeMenu from "./ThemeMenu.vue"
import type { WorkspaceMode } from "@/types"
import { useI18n } from "vue-i18n"
import { DatabaseZap, PlugZap, Wrench } from "lucide-vue-next"

const workspace = defineModel<WorkspaceMode>({ required: true })
const { t } = useI18n()
</script>

<template>
  <header class="z-30 border-b border-border/80 bg-card/95 backdrop-blur supports-[backdrop-filter]:bg-card/90">
    <div class="mx-auto flex min-h-16 w-full max-w-[1480px] flex-wrap items-center justify-between gap-3 px-4 py-2 sm:px-6 xl:px-8">
      <div class="flex min-w-0 items-center gap-2.5">
        <div class="rounded-md bg-primary p-2 text-primary-foreground shadow-sm">
          <DatabaseZap class="size-5" />
        </div>
        <div class="min-w-0">
          <p class="truncate text-sm font-semibold leading-tight">DatabaseMcpServer</p>
          <p class="truncate text-xs text-muted-foreground">{{ t("app.brandSubtitle") }}</p>
        </div>
      </div>

      <!-- Fixed widths keep zh/en label changes from reflowing the header cluster. -->
      <nav class="order-3 flex w-full items-center gap-1 rounded-lg bg-muted p-1 sm:order-none sm:w-auto" :aria-label="t('app.workspaceNav')">
        <Button
          class="flex-1 sm:w-[9.5rem] sm:flex-none"
          size="sm"
          :variant="workspace === 'connections' ? 'default' : 'ghost'"
          :aria-pressed="workspace === 'connections'"
          @click="workspace = 'connections'"
        >
          <Wrench class="size-4" />{{ t("app.connections") }}
        </Button>
        <Button
          class="flex-1 sm:w-[10.5rem] sm:flex-none"
          size="sm"
          :variant="workspace === 'playground' ? 'default' : 'ghost'"
          :aria-pressed="workspace === 'playground'"
          @click="workspace = 'playground'"
        >
          <PlugZap class="size-4" />{{ t("app.playground") }}
        </Button>
      </nav>

      <div class="ml-auto flex shrink-0 items-center gap-1 sm:ml-0">
        <LanguageMenu />
        <ThemeMenu />
      </div>
    </div>
  </header>
</template>
