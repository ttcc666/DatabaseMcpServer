<script setup lang="ts">
import { Button } from "@/components/ui/button"
import ThemeMenu from "./ThemeMenu.vue"
import type { WorkspaceMode } from "@/types"
import { DatabaseZap, PlugZap, Wrench } from "lucide-vue-next"

const workspace = defineModel<WorkspaceMode>({ required: true })
</script>

<template>
  <header class="sticky top-0 z-30 border-b border-border/80 bg-card/95 backdrop-blur supports-[backdrop-filter]:bg-card/90">
    <div class="mx-auto flex min-h-16 w-full max-w-[1480px] flex-wrap items-center justify-between gap-3 px-4 py-2 sm:px-6 xl:px-8">
      <div class="flex min-w-0 items-center gap-2.5">
        <div class="rounded-md bg-primary p-2 text-primary-foreground shadow-sm">
          <DatabaseZap class="size-5" />
        </div>
        <div class="min-w-0">
          <p class="truncate text-sm font-semibold leading-tight">DatabaseMcpServer</p>
          <p class="truncate text-xs text-muted-foreground">本地运维控制台</p>
        </div>
      </div>

      <nav class="order-3 flex w-full items-center gap-1 rounded-lg bg-muted p-1 sm:order-none sm:w-auto" aria-label="工作区">
        <Button
          class="flex-1 sm:w-[8.5rem] sm:flex-none"
          size="sm"
          :variant="workspace === 'connections' ? 'default' : 'ghost'"
          :aria-pressed="workspace === 'connections'"
          @click="workspace = 'connections'"
        >
          <Wrench class="size-4" />连接管理
        </Button>
        <Button
          class="flex-1 sm:w-[10.5rem] sm:flex-none"
          size="sm"
          :variant="workspace === 'playground' ? 'default' : 'ghost'"
          :aria-pressed="workspace === 'playground'"
          @click="workspace = 'playground'"
        >
          <PlugZap class="size-4" />Tool Playground
        </Button>
      </nav>

      <ThemeMenu />
    </div>
  </header>
</template>
