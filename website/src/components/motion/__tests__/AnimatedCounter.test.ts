import { mount } from "@vue/test-utils"
import { describe, expect, it } from "vitest"
import AnimatedCounter from "@/components/motion/AnimatedCounter.vue"

describe("AnimatedCounter", () => {
  it("keeps an accessible value while the visual digits animate", async () => {
    const wrapper = mount(AnimatedCounter, { props: { value: 12 } })

    expect(wrapper.attributes("aria-live")).toBe("polite")
    expect(wrapper.get(".sr-only").text()).toBe("12")
    expect(wrapper.get("[aria-hidden='true']").attributes("aria-hidden")).toBe("true")

    await wrapper.setProps({ value: 7 })

    expect(wrapper.get(".sr-only").text()).toBe("7")
  })
})
