<script setup lang="ts">
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog"
import { Button } from "@/components/ui/button"
import { computed, onMounted, useTemplateRef, watch } from "vue"
import { useI18n } from "vue-i18n"
import ConfigHero from "./ConfigHero.vue"
import ConnectionTableCard from "./ConnectionTableCard.vue"
import EditorSheet from "./EditorSheet.vue"
import MaintenancePanel from "./MaintenancePanel.vue"
import { useConfigWorkbench } from "@/composables/useConfigWorkbench"
import { useConnectionHealth } from "@/composables/useConnectionHealth"

const fileInput = useTemplateRef<HTMLInputElement>("fileInput")
const workbench = useConfigWorkbench()
const connectionHealth = useConnectionHealth()
const { t } = useI18n()
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
  pendingImportFile,
  dbTypeOptions,
  selectedName,
  isBootstrapping,
} = workbench

const databaseNames = computed(() => dashboard.value?.databases.map(item => item.name) ?? [])

watch(databaseNames, names => connectionHealth.prune(names))

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

function onImportOpenChange(open: boolean) {
  // Only treat external closes (Esc / overlay) as cancel. Confirm uses Button, not
  // AlertDialogAction, so it no longer races cancelImport() by auto-closing first.
  if (!open) workbench.cancelImport()
}
</script>

<template>
  <div class="flex flex-col gap-6">
      <!--
        True 2x2 grid: shared column tracks + stretched row heights so all four
        panel edges line up (left/right/top/bottom of each cell).
        [配置台]      [目标路径]
        [连接工作区]  [维护与诊断]
      -->
      <section class="grid items-stretch gap-6 xl:grid-cols-[minmax(0,1.55fr)_minmax(360px,1fr)] xl:grid-rows-[auto_minmax(34rem,auto)]">
        <ConfigHero
          :context="context"
          :dashboard="dashboard"
          :is-bootstrapping="isBootstrapping"
          :busy-action="busyAction"
          :last-message="lastMessage"
          @refresh="workbench.refresh"
          @initialize="workbench.initializeConfig"
        />

        <ConnectionTableCard
          class="min-h-0 min-w-0 xl:row-start-2"
          :dashboard="dashboard"
          :health-response="connectionHealth.response.value"
          :health-results="connectionHealth.resultMap.value"
          :is-checking-health="connectionHealth.isChecking.value"
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
          @health-check="connectionHealth.checkAll"
        />

        <MaintenancePanel
          class="min-h-0 min-w-0 xl:row-start-2"
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
            <AlertDialogTitle>{{ t("dialogs.deleteTitle") }}</AlertDialogTitle>
            <AlertDialogDescription>
              {{ t("dialogs.deleteDescription", { name: deleteTarget }) }}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel @click="workbench.cancelDelete">{{ t("common.cancel") }}</AlertDialogCancel>
            <AlertDialogAction class="bg-destructive text-destructive-foreground hover:bg-destructive/90" @click="workbench.confirmDelete">
              {{ t("common.confirmDelete") }}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog :open="importOpen" @update:open="onImportOpenChange">
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>{{ t("dialogs.importTitle") }}</AlertDialogTitle>
            <AlertDialogDescription class="space-y-2">
              <p>{{ t("dialogs.importDescription") }}</p>
              <code class="block break-all rounded-md bg-muted px-2 py-1.5 text-xs">{{ context?.configPath }}</code>
              <p v-if="pendingImportFile" class="text-foreground">
                {{ t("dialogs.pendingImportFile") }}<span class="font-medium">{{ pendingImportFile.name }}</span>
              </p>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <Button variant="outline" :disabled="busyAction === 'import'" @click="workbench.cancelImport">
              {{ t("common.cancel") }}
            </Button>
            <Button :disabled="busyAction === 'import' || !pendingImportFile" @click="workbench.confirmImport">
              {{ busyAction === 'import' ? t("dialogs.importing") : t("dialogs.continueImport") }}
            </Button>
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
</template>
