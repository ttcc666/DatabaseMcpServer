<script setup lang="ts">
import AppHeader from "@/components/app/AppHeader.vue"
import ConfigWorkbench from "@/components/ConfigWorkbench.vue"
import ToolPlayground from "@/components/playground/ToolPlayground.vue"
import { Toaster } from "@/components/ui/sonner"
import { TooltipProvider } from "@/components/ui/tooltip"
import type { WorkspaceMode } from "@/types"
import { nextTick, ref, watch } from "vue"

const workspace = ref<WorkspaceMode>("connections")
const playgroundVisited = ref(false)

watch(workspace, async (mode, previous) => {
  if (mode === "playground") playgroundVisited.value = true
  // Reset scroll only when the active workspace actually changes, after layout settles.
  if (mode !== previous) {
    await nextTick()
    window.scrollTo({ top: 0, left: 0, behavior: "auto" })
  }
}, { immediate: true })
</script>

<template>
  <TooltipProvider :delay-duration="300">
    <div class="min-h-screen bg-background text-foreground">
      <AppHeader v-model="workspace" />
      <main class="mx-auto w-full max-w-[1480px] px-4 py-5 sm:px-6 sm:py-6 xl:px-8">
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
</template>
