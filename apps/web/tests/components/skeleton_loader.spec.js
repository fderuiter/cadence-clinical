import { describe, it, expect } from "vitest";
import { mount } from "@vue/test-utils";
import SkeletonLoader from "../../src/components/common/SkeletonLoader.vue";

describe("SkeletonLoader Component", () => {
  it("renders with default props (row mode)", () => {
    const wrapper = mount(SkeletonLoader);
    expect(wrapper.classes()).toContain("type-row");
    expect(wrapper.attributes("role")).toBe("status");
    expect(wrapper.attributes("aria-busy")).toBe("true");

    const rows = wrapper.findAll(".skeleton-row");
    expect(rows.length).toBe(3); // default count is 3
  });

  it("renders card mode with specified count", () => {
    const wrapper = mount(SkeletonLoader, {
      props: {
        type: "card",
        count: 4,
      },
    });
    expect(wrapper.classes()).toContain("type-card");
    const cards = wrapper.findAll(".skeleton-card");
    expect(cards.length).toBe(4);
  });

  it("renders text mode with specified height", () => {
    const wrapper = mount(SkeletonLoader, {
      props: {
        type: "text",
        count: 2,
        height: "20px",
      },
    });
    expect(wrapper.classes()).toContain("type-text");
    const lines = wrapper.findAll(".skeleton-line");
    expect(lines.length).toBe(2);
  });

  it("renders timeline mode with bullet circles", () => {
    const wrapper = mount(SkeletonLoader, {
      props: {
        type: "timeline",
        count: 3,
      },
    });
    expect(wrapper.classes()).toContain("type-timeline");
    const items = wrapper.findAll(".skeleton-timeline-item");
    expect(items.length).toBe(3);
    const bullets = wrapper.findAll(".skeleton-bullet");
    expect(bullets.length).toBe(3);
  });

  it("respects columns prop in row mode", () => {
    const wrapper = mount(SkeletonLoader, {
      props: {
        type: "row",
        count: 1,
        columns: 7,
      },
    });
    const cells = wrapper.findAll(".skeleton-cell");
    expect(cells.length).toBe(7);
  });
});
