<script setup lang="ts">
import ToolCatalogPanel from "./ToolCatalogPanel.vue"
import ToolParameterForm from "./ToolParameterForm.vue"
import ToolResultPanel from "./ToolResultPanel.vue"
import DangerousToolDialog from "./DangerousToolDialog.vue"
import { useToolPlayground } from "@/composables/useToolPlayground"
import type { ToolArgumentValue } from "@/types/tools"
import { onMounted } from "vue"
import { useI18n } from "vue-i18n"
import { toast } from "vue-sonner"
import { PlugZap } from "lucide-vue-next"

const playground = useToolPlayground()
const { t } = useI18n()

onMounted(() => void playground.loadTools())

function updateArgument(optionName: string, value: ToolArgumentValue) {
  playground.argumentsState[optionName] = value
}

function closeConfirmation(open: boolean) {
  playground.pendingConfirmation.value = open
}

async function copyResult() {
  if (!playground.formattedResult.value) return
  await navigator.clipboard.writeText(playground.formattedResult.value)
  toast.success(t("playground.resultCopied"))
}
</script>

<template>
  <section class="min-h-[42rem] overflow-hidden rounded-xl border border-border bg-card shadow-sm">
    <header class="border-b border-border px-5 py-4 sm:px-6">
      <div class="flex items-center gap-2">
        <div class="rounded-md bg-primary/10 p-2 text-primary">
          <PlugZap class="size-4" />
        </div>
        <div>
          <h1 class="text-lg font-semibold sm:text-xl">{{ t("playground.title") }}</h1>
          <p class="mt-1 text-sm text-muted-foreground">{{ t("playground.description") }}</p>
        </div>
      </div>
    </header>
    <div class="grid min-h-[36rem] lg:grid-cols-[300px_minmax(0,1fr)] xl:grid-cols-[320px_minmax(0,1fr)]">
      <ToolCatalogPanel
        v-model:query="playground.query.value"
        v-model:category="playground.category.value"
        v-model:risk="playground.risk.value"
        :tools="playground.filteredTools.value"
        :selected-name="playground.selectedName.value"
        :is-loading="playground.isLoadingCatalog.value"
        @select="playground.selectTool"
      />
      <div class="flex min-w-0 flex-col">
        <div class="min-h-0 flex-1 p-4 sm:p-5">
          <ToolParameterForm
            v-if="playground.selectedTool.value"
            :tool="playground.selectedTool.value"
            :arguments-state="playground.argumentsState"
            :errors="playground.errors"
            :is-invoking="playground.isInvoking.value"
            @update-argument="updateArgument"
            @invoke="playground.requestInvocation"
            @abort="playground.abort"
          />
          <div v-else class="flex min-h-[20rem] items-center justify-center rounded-lg border border-dashed border-border px-6 text-center text-sm text-muted-foreground">
            {{ playground.isLoadingCatalog.value ? t("playground.loadingCatalog") : playground.requestError.value || t("playground.selectTool") }}
          </div>
        </div>
        <ToolResultPanel
          :result="playground.result.value"
          :formatted-result="playground.formattedResult.value"
          :request-error="playground.requestError.value"
          :is-invoking="playground.isInvoking.value"
          @copy="copyResult"
          @clear="playground.clearResult"
        />
      </div>
    </div>
    <DangerousToolDialog
      :open="playground.pendingConfirmation.value"
      :tool-name="playground.selectedTool.value?.name ?? ''"
      @update:open="closeConfirmation"
      @confirm="playground.confirmInvocation"
    />
  </section>
</template>
