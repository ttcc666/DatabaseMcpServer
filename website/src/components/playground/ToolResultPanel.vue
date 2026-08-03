<script setup lang="ts">
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { ScrollArea } from "@/components/ui/scroll-area"
import type { ToolInvocationResponse } from "@/types/tools"
import { useI18n } from "vue-i18n"
import { Check, CircleX, Clipboard, Clock3, Eraser, LoaderCircle } from "lucide-vue-next"

defineProps<{
  result: ToolInvocationResponse | null
  formattedResult: string
  requestError: string | null
  isInvoking: boolean
}>()

const emit = defineEmits<{ (event: "copy" | "clear"): void }>()
const { t } = useI18n()
</script>

<template>
  <section class="border-t border-border bg-muted/30">
    <div class="flex min-h-12 flex-wrap items-center justify-between gap-3 px-4 py-3 sm:px-5">
      <div class="flex flex-wrap items-center gap-2 text-sm font-medium">
        <LoaderCircle v-if="isInvoking" class="size-4 animate-spin text-primary" />
        <Check v-else-if="result?.toolSuccess" class="size-4 text-emerald-600 dark:text-emerald-400" />
        <CircleX v-else-if="result || requestError" class="size-4 text-destructive" />
        {{ t("playground.result") }}
        <Badge v-if="result" :variant="result.toolSuccess ? 'outline' : 'destructive'">{{ result.toolSuccess ? t("playground.success") : t("playground.toolFailed") }}</Badge>
        <span v-if="result" class="inline-flex items-center gap-1 text-xs text-muted-foreground"><Clock3 class="size-3" />{{ result.durationMs }} ms</span>
      </div>
      <div class="flex gap-2">
        <Button size="sm" variant="outline" :disabled="!result" @click="emit('copy')"><Clipboard class="size-4" />{{ t("playground.copy") }}</Button>
        <Button size="sm" variant="ghost" :disabled="!result && !requestError" @click="emit('clear')"><Eraser class="size-4" />{{ t("playground.clear") }}</Button>
      </div>
    </div>
    <Transition name="result-panel" mode="out-in">
      <Alert v-if="requestError" key="error" variant="destructive" class="m-4"><CircleX class="size-4" /><AlertTitle>{{ t("playground.requestFailed") }}</AlertTitle><AlertDescription class="break-all">{{ requestError }}</AlertDescription></Alert>
      <ScrollArea v-else :key="isInvoking ? 'loading' : result ? 'result' : 'idle'" class="h-[16rem] border-t border-border bg-zinc-950 text-zinc-100 dark:bg-black/40">
        <pre class="p-4 text-xs leading-6 whitespace-pre-wrap break-words sm:p-5">{{ formattedResult || (isInvoking ? t("playground.waitingResponse") : t("playground.resultPlaceholder")) }}</pre>
      </ScrollArea>
    </Transition>
  </section>
</template>

<style scoped>
.result-panel-enter-active,
.result-panel-leave-active {
  transition: opacity 160ms ease, transform 160ms ease;
}

.result-panel-enter-from {
  opacity: 0;
  transform: translateY(6px);
}

.result-panel-leave-to {
  opacity: 0;
  transform: translateY(-3px);
}

@media (prefers-reduced-motion: reduce) {
  .result-panel-enter-active,
  .result-panel-leave-active {
    transition: none;
  }
}
</style>
