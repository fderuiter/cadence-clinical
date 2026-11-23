// @vitest-environment jsdom
import { describe, it, expect, vi } from "vitest";
import { createApp, createSSRApp, defineComponent, h } from "vue";
import { renderToString } from "vue/server-renderer";
import fs from "fs";
import path from "path";
import { ClinicalContextBar } from "../index.js";
import { ClinicalContextBar as SrcClinicalContextBar } from "../src/index.js";
import { toBeAccessible } from "../accessibility-matcher.js";

describe("ClinicalContextBar", () => {
  it("re-exports ClinicalContextBar from both src/index.js and root index.js", () => {
    expect(ClinicalContextBar).toBeDefined();
    expect(SrcClinicalContextBar).toBe(ClinicalContextBar);
  });

  it("renders accessible <nav> landmark with links for Studies, study, site, participant, and current workflow via SSR and DOM", async () => {
    const props = {
      studiesTo: "/studies?site=SITE-101&participant=participant%3Ap-1",
      studyCode: "CADENCE-101",
      studyTo: "/studies/CADENCE-101",
      siteCode: "SITE-101",
      siteTo: {
        path: "/studies/CADENCE-101",
        query: { stage: "conduct", section: "participants", site: "SITE-101" },
      },
      participantCode: "017-0001",
      participantTo: {
        path: "/studies/CADENCE-101",
        query: {
          stage: "conduct",
          site: "SITE-101",
          participant: "participant:p-1",
        },
      },
      currentLabel: "Data collection",
    };

    const ssrHtml = await renderToString(
      createSSRApp(ClinicalContextBar, props)
    );
    expect(ssrHtml).toContain('<nav class="clinical-context-bar zoom-path"');
    expect(ssrHtml).toContain('aria-label="Zoom path"');
    expect(ssrHtml).toContain("CADENCE-101");
    expect(ssrHtml).toContain("SITE-101");
    expect(ssrHtml).toContain("017-0001");
    expect(ssrHtml).toContain("Data collection");

    const host = document.createElement("div");
    document.body.appendChild(host);
    const onNavigate = vi.fn();
    const app = createApp(ClinicalContextBar, {
      ...props,
      onNavigate,
    });
    app.mount(host);

    const nav = host.querySelector("nav");
    expect(nav).not.toBeNull();
    expect(nav?.getAttribute("aria-label")).toBe("Zoom path");

    const links = Array.from(host.querySelectorAll("a.clinical-context-link"));
    expect(links).toHaveLength(4);
    expect(links[0].textContent?.trim()).toBe("Studies");
    expect(links[0].getAttribute("href")).toBe(
      "/studies?site=SITE-101&participant=participant%3Ap-1"
    );
    expect(links[1].textContent?.trim()).toBe("CADENCE-101");
    expect(links[1].getAttribute("href")).toBe("/studies/CADENCE-101");
    expect(links[2].textContent?.replace(/\s+/g, " ").trim()).toBe(
      "Site SITE-101"
    );
    expect(links[2].getAttribute("href")).toBe(
      "/studies/CADENCE-101?stage=conduct&section=participants&site=SITE-101"
    );
    expect(links[3].textContent?.trim()).toBe("017-0001");
    expect(links[3].getAttribute("href")).toBe(
      "/studies/CADENCE-101?stage=conduct&site=SITE-101&participant=participant%3Ap-1"
    );

    const monoCodes = Array.from(host.querySelectorAll(".font-mono")).map(
      (el) => el.textContent?.trim()
    );
    expect(monoCodes).toEqual(["CADENCE-101", "SITE-101", "017-0001"]);

    const current = host.querySelector('[aria-current="page"]');
    expect(current?.tagName).toBe("SPAN");
    expect(current?.textContent?.trim()).toBe("Data collection");

    links[2].addEventListener("click", (e) => e.preventDefault());
    links[2].dispatchEvent(
      new MouseEvent("click", { bubbles: true, cancelable: true })
    );
    expect(onNavigate).toHaveBeenCalledWith({
      step: "site",
      to: props.siteTo,
    });

    const a11y = await toBeAccessible({ element: nav });
    expect(a11y.pass).toBe(true);

    app.unmount();
    host.remove();
  });

  it("marks the final link aria-current=page when currentLabel is omitted and uses RouterLink when registered", async () => {
    const RouterLinkStub = defineComponent({
      name: "RouterLink",
      props: {
        to: { type: [String, Object], required: true },
      },
      setup(stubProps, { slots, attrs }) {
        const href =
          typeof stubProps.to === "string"
            ? stubProps.to
            : stubProps.to?.path || "/";
        return () =>
          h(
            "a",
            { ...attrs, href, "data-router-link": "true" },
            slots.default?.()
          );
      },
    });

    const host = document.createElement("div");
    document.body.appendChild(host);
    const onNavigate = vi.fn();
    const app = createApp(ClinicalContextBar, {
      studyCode: "CAD-101",
      siteCode: "SITE-02",
      participantCode: "SUBJ-09",
      onNavigate,
    });
    app.component("RouterLink", RouterLinkStub);
    app.mount(host);

    const links = Array.from(
      host.querySelectorAll('a[data-router-link="true"]')
    );
    expect(links).toHaveLength(4);
    expect(links[3].getAttribute("aria-current")).toBe("page");
    expect(links[0].getAttribute("aria-current")).toBeNull();

    links[1].click();
    expect(onNavigate).toHaveBeenCalledWith({
      step: "study",
      to: "/studies/CAD-101",
    });

    const nav = host.querySelector("nav");
    const a11y = await toBeAccessible({ element: nav });
    expect(a11y.pass).toBe(true);

    app.unmount();
    host.remove();
  });

  it("handles fallback targets without studyCode and enforces 44px touch targets and zero emojis", () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(ClinicalContextBar, {
      siteCode: "SITE-204",
      participantCode: "SUBJ-204",
      ariaLabel: "Clinical context",
    });
    app.mount(host);

    const links = Array.from(host.querySelectorAll("a.clinical-context-link"));
    expect(links).toHaveLength(3);
    expect(links[1].getAttribute("href")).toBe("/studies?site=SITE-204");
    expect(links[2].getAttribute("href")).toBe("/studies?participant=SUBJ-204");

    app.unmount();
    host.remove();

    const source = fs.readFileSync(
      path.resolve(
        __dirname,
        "../src/components/clinical/ClinicalContextBar.vue"
      ),
      "utf-8"
    );
    expect(source).toContain("min-height: 44px;");
    expect(source).toContain("min-width: 44px;");
    expect(source).not.toMatch(/\p{Extended_Pictographic}/u);
    expect(source).toContain("@media (max-width: 767px)");
    expect(source).toContain(
      ".clinical-context-step.is-ancestor .clinical-context-prefix"
    );
  });

  it("aligns off-screen [aria-current='page'] into view and toggles non-blocking start/end overflow chevron cues", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(ClinicalContextBar, {
      studyCode: "CADENCE-101",
      siteCode: "SITE-101",
      participantCode: "017-0001",
      currentLabel: "Data collection",
    });
    app.mount(host);

    const nav = host.querySelector("nav.clinical-context-bar") as HTMLElement;
    const list = host.querySelector("ol.clinical-context-list") as HTMLElement;
    const current = host.querySelector('[aria-current="page"]') as HTMLElement;

    let scrollLeftValue = 0;
    Object.defineProperty(list, "clientWidth", {
      configurable: true,
      get: () => 320,
    });
    Object.defineProperty(list, "scrollWidth", {
      configurable: true,
      get: () => 680,
    });
    Object.defineProperty(list, "scrollLeft", {
      configurable: true,
      get: () => scrollLeftValue,
      set: (val: number) => {
        scrollLeftValue = val;
      },
    });
    list.scrollTo = ((arg: ScrollToOptions | number) => {
      if (typeof arg === "number") scrollLeftValue = arg;
      else if (arg && typeof arg.left === "number") scrollLeftValue = arg.left;
    }) as typeof list.scrollTo;

    list.getBoundingClientRect = () =>
      ({
        left: 0,
        top: 0,
        right: 320,
        bottom: 44,
        width: 320,
        height: 44,
        x: 0,
        y: 0,
        toJSON: () => ({}),
      }) as DOMRect;
    current.getBoundingClientRect = () => {
      const visibleLeft = 510 - scrollLeftValue;
      return {
        left: visibleLeft,
        top: 0,
        right: visibleLeft + 140,
        bottom: 44,
        width: 140,
        height: 44,
        x: visibleLeft,
        y: 0,
        toJSON: () => ({}),
      } as DOMRect;
    };

    // Wait for post-mount watch alignment: active crumb at end aligns scrollLeft to maxScrollLeft (360)
    await Promise.resolve();
    await Promise.resolve();
    expect(list.scrollLeft).toBe(360);
    expect(nav.getAttribute("data-overflow-start")).toBe("true");
    expect(nav.getAttribute("data-overflow-end")).toBeNull();

    const startCue = host.querySelector(
      ".clinical-context-overflow-cue--start"
    ) as HTMLElement;
    const endCue = host.querySelector(
      ".clinical-context-overflow-cue--end"
    ) as HTMLElement;
    expect(startCue.getAttribute("aria-hidden")).toBe("true");
    expect(endCue.getAttribute("aria-hidden")).toBe("true");
    expect(startCue.style.display).toBe("");
    expect(endCue.style.display).toBe("none");
    expect(endCue.querySelector("svg")?.getAttribute("stroke-width")).toBe(
      "1.75"
    );

    // Scroll back to start boundary (scrollLeft = 0): start cue clears, end cue appears
    list.scrollLeft = 0;
    list.dispatchEvent(new Event("scroll"));
    await Promise.resolve();
    expect(nav.getAttribute("data-overflow-start")).toBeNull();
    expect(nav.getAttribute("data-overflow-end")).toBe("true");
    expect(startCue.style.display).toBe("none");
    expect(endCue.style.display).toBe("");

    const a11y = await toBeAccessible({ element: nav });
    expect(a11y.pass).toBe(true);

    app.unmount();
    host.remove();
  });
});
