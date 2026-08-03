<script setup lang="ts">
import AppHeader from "@/components/app/AppHeader.vue"
import ConfigWorkbench from "@/components/ConfigWorkbench.vue"
import ToolPlayground from "@/components/playground/ToolPlayground.vue"
import { Toaster } from "@/components/ui/sonner"
import { TooltipProvider } from "@/components/ui/tooltip"
import type { WorkspaceMode } from "@/types"
import { ConfigProvider } from "reka-ui"
import { nextTick, ref, useTemplateRef, watch } from "vue"

const workspace = ref<WorkspaceMode>("connections")
const playgroundVisited = ref(false)
const scrollRoot = useTemplateRef<HTMLElement>("scrollRoot")

watch(workspace, async (mode, previous) => {
  if (mode === "playground") playgroundVisited.value = true
  // Reset the app scroll container (not window) when the workspace changes.
  if (mode !== previous) {
    await nextTick()
    scrollRoot.value?.scrollTo({ top: 0, left: 0, behavior: "auto" })
  }
}, { immediate: true })
</script>

<template>
  <!--
    App-owned scroll container: reka-ui Dialog/Sheet/Select body-lock targets
    document body. With document overflow hidden and scrolling inside #scroll-root,
    overlay open/close never changes the visible scrollbar or layout width.
  -->
  <ConfigProvider :scroll-body="false">
    <TooltipProvider :delay-duration="300">
      <div class="flex h-dvh flex-col overflow-hidden bg-background text-foreground">
        <AppHeader v-model="workspace" class="shrink-0" />
        <main
          ref="scrollRoot"
          class="min-h-0 flex-1 overflow-x-hidden overflow-y-auto"
        >
          <div class="mx-auto w-full max-w-[1480px] px-4 py-5 sm:px-6 sm:py-6 xl:px-8">
            <!--
              Keep workspaces mounted after first visit (v-show) so switching does not
              remount the whole tree, reflow from empty->loaded, or thrash scrollbar width.
            -->
            <div v-show="workspace === 'connections'">
              <ConfigWorkbench />
            </div>
            <div v-if="playgroundVisited" v-show="workspace === 'playground'">
              <ToolPlayground />
            </div>
          </div>
        </main>
        <Toaster
          rich-colors
          close-button
          position="top-right"
          class="toaster z-[100]"
          :toast-options="{
            class: 'pointer-events-auto',
          }"
        />
      </div>
    </TooltipProvider>
  </ConfigProvider>
</template>
