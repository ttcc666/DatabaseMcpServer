<script setup lang="ts">
import { motion, useReducedMotion, useSpring, useTransform } from "motion-v"
import type { CSSProperties } from "vue"
import { computed, defineComponent, h, watchEffect } from "vue"

type PlaceValue = number | "."

interface AnimatedCounterProps {
  value: number
  fontSize?: number
  padding?: number
  places?: PlaceValue[]
  gap?: number
  horizontalPadding?: number
  textColor?: string
  fontWeight?: CSSProperties["fontWeight"]
  className?: string
  containerStyle?: CSSProperties
  counterStyle?: CSSProperties
  digitStyle?: CSSProperties
}

const props = withDefaults(defineProps<AnimatedCounterProps>(), {
  fontSize: 20,
  padding: 0,
  places: undefined,
  gap: 2,
  horizontalPadding: 0,
  textColor: "inherit",
  fontWeight: "inherit",
  className: "",
})

function normalizeNearInteger(num: number): number {
  const nearest = Math.round(num)
  const tolerance = 1e-9 * Math.max(1, Math.abs(num))
  return Math.abs(num - nearest) < tolerance ? nearest : num
}

function getValueRoundedToPlace(value: number, place: number): number {
  return Math.floor(normalizeNearInteger(value / place))
}

function derivePlaces(value: number): PlaceValue[] {
  return [...value.toString()].map((character, index, characters) => {
    if (character === ".") return "."
    const dotIndex = characters.indexOf(".")
    const exponent = dotIndex === -1
      ? characters.length - index - 1
      : index < dotIndex
        ? dotIndex - index - 1
        : -(index - dotIndex)
    return 10 ** exponent
  })
}

const height = computed(() => props.fontSize + props.padding)
const places = computed<PlaceValue[]>(() => props.places ?? derivePlaces(props.value))
const shouldReduceMotion = useReducedMotion()

const computedCounterStyle = computed<CSSProperties>(() => ({
  fontSize: `${props.fontSize}px`,
  display: "flex",
  gap: props.gap,
  overflow: "hidden",
  paddingLeft: props.horizontalPadding,
  paddingRight: props.horizontalPadding,
  lineHeight: 1,
  color: props.textColor,
  fontWeight: props.fontWeight,
  fontVariantNumeric: "tabular-nums",
  direction: "ltr",
  ...props.counterStyle,
}))

const DigitColumn = defineComponent({
  name: "AnimatedCounterDigitColumn",
  props: {
    place: { type: Number, required: true },
    value: { type: Number, required: true },
    height: { type: Number, required: true },
    digitStyle: { type: Object as () => CSSProperties | undefined, default: undefined },
    reduceMotion: { type: Boolean, default: false },
  },
  setup(columnProps) {
    const valueRoundedToPlace = computed(() => getValueRoundedToPlace(columnProps.value, columnProps.place))
    const animatedValue = useSpring(valueRoundedToPlace.value, { stiffness: 300, damping: 30 })

    watchEffect(() => {
      if (columnProps.reduceMotion) {
        animatedValue.jump(valueRoundedToPlace.value)
      }
      else {
        animatedValue.set(valueRoundedToPlace.value)
      }
    })

    const digitNodes = Array.from({ length: 10 }, (_, index) => {
      const y = useTransform(animatedValue, (latest: number) => {
        const placeValue = latest % 10
        const offset = (10 + index - placeValue) % 10
        let offsetPixels = offset * columnProps.height
        if (offset > 5) offsetPixels -= 10 * columnProps.height
        return offsetPixels
      })
      return { index, y }
    })

    return () => {
      const wrapperStyle: CSSProperties = {
        height: `${columnProps.height}px`,
        position: "relative",
        width: "1ch",
        display: "inline-flex",
        overflow: "hidden",
        ...columnProps.digitStyle,
      }
      const baseStyle: CSSProperties = {
        position: "absolute",
        inset: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
      }

      return h(
        "span",
        { style: wrapperStyle, "aria-hidden": "true" },
        digitNodes.map(({ index, y }) => h(motion.span, { key: index, style: { ...baseStyle, y } }, () => String(index))),
      )
    }
  },
})
</script>

<template>
  <span
    :class="props.className"
    :style="{ position: 'relative', display: 'inline-block', ...props.containerStyle }"
    aria-live="polite"
    aria-atomic="true"
  >
    <span aria-hidden="true" :style="computedCounterStyle">
      <template v-for="place in places" :key="place">
        <span
          v-if="place === '.'"
          :style="{
            height: `${height}px`,
            width: 'fit-content',
            position: 'relative',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            ...props.digitStyle
          }"
        >
          .
        </span>
        <DigitColumn
          v-else
          :place="place as number"
          :value="value"
          :height="height"
          :digit-style="props.digitStyle"
          :reduce-motion="shouldReduceMotion"
        />
      </template>
    </span>
    <span class="sr-only">{{ value }}</span>
  </span>
</template>
