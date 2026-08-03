<script setup lang="ts">
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle } from "@/components/ui/alert-dialog"
import { Input } from "@/components/ui/input"
import { ref, watch } from "vue"
import { ShieldAlert } from "lucide-vue-next"

const props = defineProps<{ open: boolean, toolName: string }>()
const emit = defineEmits<{
  (event: "update:open", value: boolean): void
  (event: "confirm", confirmation: string): void
}>()
const confirmation = ref("")

watch(() => props.open, open => {
  if (open) confirmation.value = ""
})
</script>

<template>
  <AlertDialog :open="open" @update:open="emit('update:open', $event)">
    <AlertDialogContent>
      <AlertDialogHeader>
        <div class="flex items-center gap-2 text-destructive"><ShieldAlert class="size-5" /><AlertDialogTitle>确认执行受保护 Tool</AlertDialogTitle></div>
        <AlertDialogDescription>输入完整名称 <code>{{ toolName }}</code> 后才能继续。此检查在服务端仍会执行。</AlertDialogDescription>
      </AlertDialogHeader>
      <Input v-model="confirmation" autocomplete="off" :placeholder="toolName" aria-label="危险 Tool 确认名称" />
      <AlertDialogFooter>
        <AlertDialogCancel @click="emit('update:open', false)">取消</AlertDialogCancel>
        <AlertDialogAction :disabled="confirmation !== toolName" class="bg-destructive text-destructive-foreground hover:bg-destructive/90" @click="emit('confirm', confirmation)">确认执行</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
