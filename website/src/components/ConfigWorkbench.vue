<script setup lang="ts">
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog"
import { Toaster } from "@/components/ui/sonner"
import { onMounted, useTemplateRef } from "vue"
import ConfigHero from "./ConfigHero.vue"
import ConnectionTableCard from "./ConnectionTableCard.vue"
import EditorSheet from "./EditorSheet.vue"
import MaintenancePanel from "./MaintenancePanel.vue"
import { useConfigWorkbench } from "@/composables/useConfigWorkbench"

const fileInput = useTemplateRef<HTMLInputElement>("fileInput")
const workbench = useConfigWorkbench()
const {
  context,
  dashboard,
  selectedDatabase,
  diagnostics,
  lastMessage,
  busyAction,
  editorDraft,
  editorOpen,
  deleteTarget,
  deleteOpen,
  importOpen,
  dbTypeOptions,
  selectedName,
  isBootstrapping,
} = workbench

onMounted(() => {
  void workbench.bootstrap()
})

function pickImportFile() {
  fileInput.value?.click()
}

function onImportFileChange(event: Event) {
  const target = event.target as HTMLInputElement
  workbench.requestImport(target.files?.[0] ?? null)
  target.value = ""
}
</script>

<template>
  <div class="relative min-h-screen bg-muted/20">
    <div class="mx-auto flex min-h-screen w-full max-w-[1480px] flex-col gap-6 px-4 py-6 sm:px-6 xl:px-8">
      <ConfigHero
        :context="context"
        :dashboard="dashboard"
        :is-bootstrapping="isBootstrapping"
        :busy-action="busyAction"
        :last-message="lastMessage"
        @refresh="workbench.refresh"
        @initialize="workbench.initializeConfig"
      />

      <section class="grid gap-6 xl:grid-cols-[minmax(0,1.45fr)_minmax(360px,0.85fr)]">
        <ConnectionTableCard
          :dashboard="dashboard"
          :busy-action="busyAction"
          :selected-name="selectedName"
          :is-bootstrapping="isBootstrapping"
          @select="workbench.selectDatabase"
          @create="workbench.startCreate"
          @preset="workbench.startPresetCreate()"
          @edit="workbench.startEdit"
          @clone="workbench.startClone"
          @delete="workbench.requestDelete"
          @set-default="workbench.setDefault"
          @switch-current="workbench.switchCurrent"
          @test="workbench.testSelected"
        />

        <MaintenancePanel
          :context="context"
          :dashboard="dashboard"
          :selected-database="selectedDatabase"
          :diagnostics="diagnostics"
          :busy-action="busyAction"
          @validate="workbench.validateConfig"
          @doctor="workbench.doctorConfig"
          @export="workbench.exportConfig"
          @pick-import="pickImportFile"
        />
      </section>

      <EditorSheet
        :open="editorOpen"
        :draft="editorDraft"
        :db-type-options="dbTypeOptions"
        :selected-database="selectedDatabase"
        :busy-action="busyAction"
        @update:open="value => value ? null : workbench.closeEditor()"
        @apply-preset="workbench.applyPreset"
        @submit="workbench.submitEditor"
      />

      <AlertDialog :open="deleteOpen" @update:open="value => !value && workbench.cancelDelete()">
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>删除数据库连接？</AlertDialogTitle>
            <AlertDialogDescription>
              这会从配置文件里移除 <code>{{ deleteTarget }}</code>。如果它同时是默认/当前连接，后续解析会回退到其他项。
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel @click="workbench.cancelDelete">取消</AlertDialogCancel>
            <AlertDialogAction class="bg-destructive text-destructive-foreground hover:bg-destructive/90" @click="workbench.confirmDelete">
              确认删除
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog :open="importOpen" @update:open="value => !value && workbench.cancelImport()">
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>覆盖导入配置？</AlertDialogTitle>
            <AlertDialogDescription>
              导入会用选中的 JSON 文件覆盖当前目标配置文件：
              <code>{{ context?.configPath }}</code>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel @click="workbench.cancelImport">取消</AlertDialogCancel>
            <AlertDialogAction @click="workbench.confirmImport">
              继续导入
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <input
        ref="fileInput"
        type="file"
        accept=".json,application/json"
        class="hidden"
        @change="onImportFileChange"
      >
    </div>

    <Toaster rich-colors position="top-right" />
  </div>
</template>
