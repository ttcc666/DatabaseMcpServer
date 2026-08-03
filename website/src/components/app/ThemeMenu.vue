<script setup lang="ts">
import { Button } from "@/components/ui/button"
import { DropdownMenu, DropdownMenuContent, DropdownMenuLabel, DropdownMenuRadioGroup, DropdownMenuRadioItem, DropdownMenuSeparator, DropdownMenuTrigger } from "@/components/ui/dropdown-menu"
import { useTheme, type ThemeMode } from "@/composables/useTheme"
import { computed } from "vue"
import { Monitor, Moon, Sun } from "lucide-vue-next"

const { mode, isDark, setTheme } = useTheme()
const icon = computed(() => mode.value === "auto" ? Monitor : isDark.value ? Moon : Sun)
const label = computed(() => mode.value === "auto" ? "跟随系统" : mode.value === "dark" ? "暗色模式" : "亮色模式")

function updateMode(value: unknown) {
  if (value === "light" || value === "dark" || value === "auto") setTheme(value as ThemeMode)
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon" :aria-label="`主题：${label}`" :title="label">
        <component :is="icon" class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-44">
      <DropdownMenuLabel>界面主题</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuRadioGroup :model-value="mode" @update:model-value="updateMode">
        <DropdownMenuRadioItem value="light"><Sun class="size-4" />亮色</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="dark"><Moon class="size-4" />暗色</DropdownMenuRadioItem>
        <DropdownMenuRadioItem value="auto"><Monitor class="size-4" />跟随系统</DropdownMenuRadioItem>
      </DropdownMenuRadioGroup>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
