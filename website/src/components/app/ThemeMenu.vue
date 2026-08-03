<script setup lang="ts">
import { Button } from "@/components/ui/button"
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { useTheme, type ThemeMode } from "@/composables/useTheme"
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { Monitor, Moon, Sun } from "lucide-vue-next"

const { mode, isDark, setTheme } = useTheme()
const { t } = useI18n()
const icon = computed(() => mode.value === "auto" ? Monitor : isDark.value ? Moon : Sun)
const label = computed(() => mode.value === "auto" ? t("theme.followSystem") : mode.value === "dark" ? t("theme.darkMode") : t("theme.lightMode"))

function updateMode(value: unknown) {
  if (value === "light" || value === "dark" || value === "auto") setTheme(value as ThemeMode)
}
</script>

<template>
  <!-- Non-modal: avoid body scroll-lock layout shift on open. -->
  <DropdownMenu :modal="false">
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon" class="shrink-0" :aria-label="t('theme.ariaLabel', { label })" :title="label">
        <component :is="icon" class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" :collision-padding="8" class="w-44">
      <DropdownMenuLabel>{{ t("theme.menuLabel") }}</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuRadioGroup :model-value="mode" @update:model-value="updateMode">
        <DropdownMenuRadioItem value="light"><Sun class="size-4" />{{ t("theme.light") }}</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="dark"><Moon class="size-4" />{{ t("theme.dark") }}</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="auto"><Monitor class="size-4" />{{ t("theme.auto") }}</DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
