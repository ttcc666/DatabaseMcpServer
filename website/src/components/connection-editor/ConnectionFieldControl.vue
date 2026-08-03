<script setup lang="ts">
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Tooltip, TooltipContent, TooltipTrigger } from "@/components/ui/tooltip"
import type { ConnectionStringFieldDefinition } from "@/types/connections"
import { ref } from "vue"
import { useI18n } from "vue-i18n"
import { Eye, EyeOff } from "lucide-vue-next"

defineProps<{
  field: ConnectionStringFieldDefinition
  invalid?: boolean
}>()

const value = defineModel<string>({ required: true })
const showPassword = ref(false)
const { t } = useI18n()

function updateBoolean(next: boolean) {
  value.value = next ? "true" : "false"
}
</script>

<template>
  <div v-if="field.inputType === 'boolean'" class="flex min-h-10 items-center justify-between gap-4 rounded-md border border-border px-3 py-2">
    <span class="text-sm">{{ field.label }}</span>
    <Switch :model-value="value === 'true'" :aria-label="field.label" @update:model-value="updateBoolean" />
  </div>

  <div v-else class="relative">
    <Input
      v-model="value"
      :type="field.inputType === 'password' && !showPassword ? 'password' : field.inputType === 'number' ? 'number' : 'text'"
      :inputmode="field.inputType === 'number' ? 'numeric' : undefined"
      :placeholder="field.required ? t('common.required') : t('common.optional')"
      :aria-invalid="invalid || undefined"
      :class="field.inputType === 'password' ? 'pr-10' : ''"
    />
    <Tooltip v-if="field.inputType === 'password'">
      <TooltipTrigger as-child>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          class="absolute right-0 top-0"
          :aria-label="showPassword ? t('wizard.hidePassword') : t('wizard.showPassword')"
          @click="showPassword = !showPassword"
        >
          <EyeOff v-if="showPassword" class="size-4" />
          <Eye v-else class="size-4" />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{{ showPassword ? t("wizard.hidePassword") : t("wizard.showPassword") }}</TooltipContent>
    </Tooltip>
  </div>
</template>
