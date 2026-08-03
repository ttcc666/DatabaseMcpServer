<script setup lang="ts">
import { Badge } from "@/components/ui/badge"
import AnimatedCounter from "@/components/motion/AnimatedCounter.vue"
import type { ConnectionHealthResponse } from "@/types/connections"
import { computed } from "vue"
import { useI18n } from "vue-i18n"
import { Activity, CircleCheck, CircleX } from "lucide-vue-next"

const props = defineProps<{ response: ConnectionHealthResponse | null }>()
const { t, locale } = useI18n()

const checkedAt = computed(() => {
  const timestamps = props.response?.results.map(item => Date.parse(item.checkedAt)).filter(Number.isFinite) ?? []
  if (timestamps.length === 0) return null
  return new Intl.DateTimeFormat(locale.value, { dateStyle: "short", timeStyle: "medium" }).format(Math.max(...timestamps))
})
</script>

<template>
  <Transition name="health-summary">
    <div v-if="response" class="flex flex-wrap items-center gap-3 border-b border-border/70 bg-muted/20 px-4 py-3 text-sm sm:px-5">
      <span class="flex items-center gap-2 font-medium">
        <Activity class="size-4 text-primary" />
        {{ t("health.title") }}
      </span>
      <Badge variant="outline" class="gap-1.5 border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400">
        <CircleCheck class="size-3" />
        {{ t("health.healthy") }} <AnimatedCounter :value="response.healthyConnections" :font-size="12" :font-weight="600" />
      </Badge>
      <Badge v-if="response.unhealthyConnections" variant="destructive" class="gap-1.5">
        <CircleX class="size-3" />
        {{ t("health.unhealthy") }} <AnimatedCounter :value="response.unhealthyConnections" :font-size="12" :font-weight="600" />
      </Badge>
      <Badge v-else variant="outline" class="gap-1.5 text-muted-foreground">
        {{ t("health.unhealthy") }} <AnimatedCounter :value="0" :font-size="12" :font-weight="600" />
      </Badge>
      <span v-if="checkedAt" class="ml-auto text-xs text-muted-foreground">{{ t("health.lastChecked", { time: checkedAt }) }}</span>
    </div>
  </Transition>
</template>

<style scoped>
.health-summary-enter-active,
.health-summary-leave-active {
  transition: opacity 180ms ease, transform 180ms ease;
}

.health-summary-enter-from,
.health-summary-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}

@media (prefers-reduced-motion: reduce) {
  .health-summary-enter-active,
  .health-summary-leave-active {
    transition: none;
  }
}
</style>
