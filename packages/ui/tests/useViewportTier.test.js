// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, defineComponent, h, nextTick } from "vue";
import fs from "fs";
import path from "path";
import {
  ClinicalDrawer,
  VIEWPORT_BREAKPOINTS,
  classifyViewportTier,
  useViewportTier,
} from "../index.js";

const uiRoot = process.cwd();

function mountComponent(Component, props = {}, slots = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const app = createApp({
    render: () => h(Component, props, slots),
  });
  app.mount(host);
  return {
    el: host.firstElementChild,
    host,
    unmount() {
      app.unmount();
      host.remove();
    },
  };
}

describe("useViewportTier & Tablet Workspace Primitives", () => {
  let originalInnerWidth;
  let originalMatchMedia;

  beforeEach(() => {
    originalInnerWidth = window.innerWidth;
    originalMatchMedia = window.matchMedia;
  });

  afterEach(() => {
    Object.defineProperty(window, "innerWidth", {
      value: originalInnerWidth,
      writable: true,
      configurable: true,
    });
    window.matchMedia = originalMatchMedia;
  });

  it("classifies <768px as mobile, 768px..1279px as tablet, and >=1280px as desktop", () => {
    expect(VIEWPORT_BREAKPOINTS).toEqual({ tablet: 768, desktop: 1280 });
    expect(classifyViewportTier(320)).toBe("mobile");
    expect(classifyViewportTier(767)).toBe("mobile");
    expect(classifyViewportTier(768)).toBe("tablet");
    expect(classifyViewportTier(861)).toBe("tablet");
    expect(classifyViewportTier(961)).toBe("tablet");
    expect(classifyViewportTier(1024)).toBe("tablet");
    expect(classifyViewportTier(1180)).toBe("tablet");
    expect(classifyViewportTier(1279)).toBe("tablet");
    expect(classifyViewportTier(1280)).toBe("desktop");
    expect(classifyViewportTier(1440)).toBe("desktop");
    expect(classifyViewportTier(Number.NaN)).toBe("desktop");
  });

  it("reacts to window resize across mobile, tablet, and wide-desktop tiers and cleans up listeners", async () => {
    const listeners = new Map();
    window.matchMedia = vi.fn().mockImplementation((query) => ({
      get matches() {
        const w = window.innerWidth;
        if (query.includes("max-width: 767")) return w < 768;
        if (query.includes("min-width: 768px")) return w >= 768 && w < 1280;
        if (query.includes("min-width: 1280px")) return w >= 1280;
        return false;
      },
      media: query,
      addEventListener: vi.fn((evt, cb) => {
        if (!listeners.has(query)) listeners.set(query, new Set());
        listeners.get(query).add(cb);
      }),
      removeEventListener: vi.fn((evt, cb) => {
        listeners.get(query)?.delete(cb);
      }),
    }));

    Object.defineProperty(window, "innerWidth", {
      value: 768,
      writable: true,
      configurable: true,
    });

    const Probe = defineComponent({
      setup() {
        const vp = useViewportTier();
        return { vp };
      },
      render() {
        return h(
          "div",
          {
            "data-tier": this.vp.tier.value,
            "data-width": String(this.vp.width.value),
          },
          `${this.vp.tier.value}:${this.vp.isMobile.value}:${this.vp.isTablet.value}:${this.vp.isDesktop.value}`
        );
      },
    });

    const wrapper = mountComponent(Probe);
    expect(wrapper.el.getAttribute("data-tier")).toBe("tablet");
    expect(wrapper.el.textContent).toBe("tablet:false:true:false");

    Object.defineProperty(window, "innerWidth", {
      value: 1024,
      writable: true,
      configurable: true,
    });
    window.dispatchEvent(new Event("resize"));
    await nextTick();
    expect(wrapper.el.getAttribute("data-tier")).toBe("tablet");
    expect(wrapper.el.getAttribute("data-width")).toBe("1024");

    Object.defineProperty(window, "innerWidth", {
      value: 1280,
      writable: true,
      configurable: true,
    });
    window.dispatchEvent(new Event("resize"));
    await nextTick();
    expect(wrapper.el.getAttribute("data-tier")).toBe("desktop");
    expect(wrapper.el.textContent).toBe("desktop:false:false:true");

    Object.defineProperty(window, "innerWidth", {
      value: 640,
      writable: true,
      configurable: true,
    });
    window.dispatchEvent(new Event("resize"));
    await nextTick();
    expect(wrapper.el.getAttribute("data-tier")).toBe("mobile");
    expect(wrapper.el.textContent).toBe("mobile:true:false:false");

    wrapper.unmount();
    for (const set of listeners.values()) {
      expect(set.size).toBe(0);
    }
  });

  it("defines 1280px wide-desktop, 52px shell chrome, 44px staff target tokens, sticky helpers, and ClinicalDrawer Lucide close icon", () => {
    const tokensCss = fs.readFileSync(
      path.resolve(uiRoot, "tokens.css"),
      "utf8"
    );
    expect(tokensCss).toContain("--breakpoint-wide: 1280px;");
    expect(tokensCss).toContain("--shell-chrome-height: 52px;");
    expect(tokensCss).toContain("--touch-target-staff: 44px;");
    expect(tokensCss).toContain(
      "--semantic-shell-chrome-height: var(--shell-chrome-height);"
    );
    expect(tokensCss).toContain(
      "--semantic-touch-target-staff: var(--touch-target-staff);"
    );

    const responsiveCss = fs.readFileSync(
      path.resolve(uiRoot, "responsive.css"),
      "utf8"
    );
    expect(responsiveCss).toContain("@media (min-width: 1280px)");
    expect(responsiveCss).toContain(".sticky-col-start");
    expect(responsiveCss).toContain(".sticky-action-bar");
    expect(responsiveCss).toContain(".touch-target-staff");

    const drawerSrc = fs.readFileSync(
      path.resolve(uiRoot, "src/components/clinical/ClinicalDrawer.vue"),
      "utf8"
    );
    expect(drawerSrc).toContain("--semantic-drawer-width-sm");
    expect(drawerSrc).toContain("--semantic-drawer-width-md");
    expect(drawerSrc).not.toContain("✕");

    const wrapper = mountComponent(
      ClinicalDrawer,
      {
        open: true,
        teleport: false,
        size: "md",
        title: "Inspector",
      },
      {
        default: () => h("p", "Body"),
      }
    );
    const closeBtn = wrapper.host.querySelector(".clinical-drawer-close-btn");
    expect(closeBtn).not.toBeNull();
    const svg = closeBtn.querySelector("svg");
    expect(svg).not.toBeNull();
    expect(svg.getAttribute("aria-hidden")).toBe("true");
    wrapper.unmount();
  });
});
