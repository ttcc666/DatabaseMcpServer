<script setup lang="ts">
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import { useLocale } from "@/composables/useLocale"
import type { AppLocale } from "@/i18n"
import { Languages } from "lucide-vue-next"

const { locale, label, setLocale, t } = useLocale()

function updateLocale(value: unknown) {
  if (value === "zh-CN" || value === "en-US") {
    setLocale(value as AppLocale)
  }
}
</script>

<template>
  <!--
    Non-modal menus skip body scroll locking. Modal open would hide the
    scrollbar and shift sticky header / page content by ~scrollbar width.
  -->
  <DropdownMenu :modal="false">
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon" class="shrink-0" :aria-label="t('language.ariaLabel', { label })" :title="label">
        <Languages class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" :collision-padding="8" class="w-44">
      <DropdownMenuLabel>{{ t("language.menuLabel") }}</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuRadioGroup :model-value="locale" @update:model-value="updateLocale">
        <DropdownMenuRadioItem value="zh-CN">{{ t("language.zhCN") }}</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="en-US">{{ t("language.enUS") }}</DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
