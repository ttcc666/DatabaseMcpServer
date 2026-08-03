<script setup lang="ts">
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog"
import { Input } from "@/components/ui/input"
import { ref, watch } from "vue"
import { useI18n } from "vue-i18n"
import { ShieldAlert } from "lucide-vue-next"

const props = defineProps<{ open: boolean, toolName: string }>()
const emit = defineEmits<{
  (event: "update:open", value: boolean): void
  (event: "confirm", confirmation: string): void
}>()
const confirmation = ref("")
const { t } = useI18n()

watch(() => props.open, open => {
  if (open) confirmation.value = ""
})
</script>

<template>
  <AlertDialog :open="open" @update:open="emit('update:open', $event)">
    <AlertDialogContent>
      <AlertDialogHeader>
        <div class="flex items-center gap-2 text-destructive"><ShieldAlert class="size-5" /><AlertDialogTitle>{{ t("playground.confirmTitle") }}</AlertDialogTitle></div>
        <AlertDialogDescription>{{ t("playground.confirmDescription", { name: toolName }) }}</AlertDialogDescription>
      </AlertDialogHeader>
      <Input v-model="confirmation" autocomplete="off" :placeholder="toolName" :aria-label="t('playground.confirmNameAria')" />
      <AlertDialogFooter>
        <AlertDialogCancel @click="emit('update:open', false)">{{ t("common.cancel") }}</AlertDialogCancel>
        <AlertDialogAction :disabled="confirmation !== toolName" class="bg-destructive text-destructive-foreground hover:bg-destructive/90" @click="emit('confirm', confirmation)">{{ t("playground.confirmExecute") }}</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
