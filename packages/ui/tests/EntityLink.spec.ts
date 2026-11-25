// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from "vitest";
import { createApp, createSSRApp, nextTick } from "vue";
import { renderToString } from "vue/server-renderer";
import {
  EntityLink,
  registerDraftSource,
  resetDraftSourcesForTests,
} from "../index.js";
import { EntityLink as SrcEntityLink } from "../src/index.js";
import { toBeAccessible } from "../accessibility-matcher.js";

expect.extend({ toBeAccessible });

describe("EntityLink", () => {
  afterEach(() => {
    resetDraftSourcesForTests();
    document.body.innerHTML = "";
  });

  it("re-exports EntityLink from both src/index.js and root index.js", () => {
    expect(EntityLink).toBeDefined();
    expect(SrcEntityLink).toBe(EntityLink);
  });

  it("formats NodeRef clinical labels without exposing raw UUIDs or snake_case table names", async () => {
    const rawRef = "protocol_version:11111111-2222-3333-4444-555555555555@2";

    const ssrHtml = await renderToString(
      createSSRApp(EntityLink, {
        nodeRef: rawRef,
        resolveRemotely: false,
      })
    );
    expect(ssrHtml).toContain("Protocol version v2");
    expect(ssrHtml).not.toContain("11111111-2222-3333-4444-555555555555");
    expect(ssrHtml).not.toContain("protocol_version");

    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(EntityLink, {
      nodeRef: rawRef,
      label: "Version 2 · Oncology Master Protocol",
      to: "/studies/ONC-101?stage=design&section=versions",
      teleport: false,
      resolveRemotely: false,
    });
    app.mount(host);

    const anchor = host.querySelector("a.entity-link");
    expect(anchor).not.toBeNull();
    expect(anchor?.textContent?.trim()).toBe(
      "Version 2 · Oncology Master Protocol"
    );
    expect(anchor?.getAttribute("href")).toBe(
      "/studies/ONC-101?stage=design&section=versions"
    );
    await expect(host).toBeAccessible();
    app.unmount();
  });

  it("renders masked visibility placeholders without leaking NodeRef identifiers", () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(EntityLink, {
      nodeRef: "participant:aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
      masked: "OutsideScope",
      resolveRemotely: false,
    });
    app.mount(host);

    const maskedEl = host.querySelector('[data-masked="true"]');
    expect(maskedEl).not.toBeNull();
    expect(maskedEl?.textContent?.trim()).toBe(
      "Hidden participant (outside your site)"
    );
    expect(host.innerHTML).not.toContain(
      "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"
    );
    expect(host.querySelector("a, button")).toBeNull();
    app.unmount();
  });

  it("resolves labels asynchronously via resolveNode or injected cadenceEntityResolver", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const resolveNode = vi.fn().mockResolvedValue({
      kind: "collection_instance",
      code: "VS-WK4",
      title: "Vital Signs Week 4",
      status: "in_progress",
    });

    const app = createApp(EntityLink, {
      nodeRef: "collection_instance:22222222-3333-4444-5555-666666666666",
      resolveNode,
      teleport: false,
    });
    app.mount(host);

    await Promise.resolve();
    await nextTick();

    expect(resolveNode).toHaveBeenCalledWith(
      "collection_instance:22222222-3333-4444-5555-666666666666"
    );
    expect(host.textContent).toContain("VS-WK4 · Vital Signs Week 4");
    expect(host.textContent).not.toContain(
      "22222222-3333-4444-5555-666666666666"
    );
    app.unmount();
  });

  it("opens contextual ClinicalDrawer preview first and enforces unsaved-draft guard before full-view navigation", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);

    const routerPush = vi.fn().mockResolvedValue(undefined);
    const confirmDiscardDrafts = vi
      .fn()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true);

    registerDraftSource({
      hasDrafts: () => true,
      flush: () => undefined,
    });

    const app = createApp(EntityLink, {
      nodeRef: "participant:33333333-4444-5555-6666-777777777777",
      label: "Participant SUBJ-014",
      code: "SUBJ-014",
      status: "enrolled",
      subtitle: "Study ONC-101 · Site SITE-01",
      to: "/studies/ONC-101?stage=conduct&participant=participant%3A33333333-4444-5555-6666-777777777777",
      previewMode: "drawer",
      teleport: false,
      resolveRemotely: false,
      confirmDiscardDrafts,
    });
    app.config.globalProperties.$router = {
      push: routerPush,
      resolve: (target: unknown) => ({
        href: typeof target === "string" ? target : "/studies/ONC-101",
      }),
    };
    app.mount(host);

    const trigger = host.querySelector("a.entity-link") as HTMLAnchorElement;
    expect(trigger).not.toBeNull();
    expect(
      host.querySelector('[data-testid="entity-link-preview-drawer"]')
    ).toBeNull();

    // First click opens contextual preview drawer instead of navigating away immediately
    trigger.click();
    await nextTick();

    const drawer = host.querySelector(
      '[data-testid="entity-link-preview-drawer"]'
    );
    expect(drawer).not.toBeNull();
    expect(drawer?.textContent).toContain("Participant SUBJ-014");
    expect(drawer?.textContent).toContain("SUBJ-014");
    expect(drawer?.textContent).toContain("Study ONC-101 · Site SITE-01");
    expect(routerPush).not.toHaveBeenCalled();

    const openFullBtn = host.querySelector(
      '[data-testid="entity-link-open-full-view"]'
    ) as HTMLButtonElement;
    expect(openFullBtn).not.toBeNull();

    // When unsaved drafts exist and user cancels discard, stay in preview
    openFullBtn.click();
    await Promise.resolve();
    await nextTick();
    expect(confirmDiscardDrafts).toHaveBeenCalledTimes(1);
    expect(routerPush).not.toHaveBeenCalled();
    expect(
      host.querySelector('[data-testid="entity-link-preview-drawer"]')
    ).not.toBeNull();

    // When user confirms discard, navigate to canonical full view and close drawer
    openFullBtn.click();
    await Promise.resolve();
    await nextTick();
    expect(confirmDiscardDrafts).toHaveBeenCalledTimes(2);
    expect(routerPush).toHaveBeenCalledWith(
      "/studies/ONC-101?stage=conduct&participant=participant%3A33333333-4444-5555-6666-777777777777"
    );
    app.unmount();
  });

  it("supports inspector previewMode for in-canvas CanvasInspector selection", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);

    const routerPush = vi.fn().mockResolvedValue(undefined);
    const onInspect = vi.fn();

    const app = createApp(EntityLink, {
      nodeRef: "proposal:44444444-5555-6666-7777-888888888888",
      label: "Proposal · Adjust Visit Window",
      previewMode: "inspector",
      inspectorTo: {
        path: "/studies/ONC-101",
        query: { inspect: "proposal:44444444-5555-6666-7777-888888888888" },
      },
      to: "/studies/ONC-101?stage=conduct&section=proposals",
      resolveRemotely: false,
      onInspect,
    });
    app.config.globalProperties.$router = {
      push: routerPush,
      resolve: () => ({
        href: "/studies/ONC-101?inspect=proposal%3A44444444-5555-6666-7777-888888888888",
      }),
    };
    app.mount(host);

    const trigger = host.querySelector("a.entity-link") as HTMLAnchorElement;
    trigger.click();
    await Promise.resolve();
    await nextTick();

    expect(onInspect).toHaveBeenCalledWith(
      "proposal:44444444-5555-6666-7777-888888888888"
    );
    expect(routerPush).toHaveBeenCalledWith({
      path: "/studies/ONC-101",
      query: { inspect: "proposal:44444444-5555-6666-7777-888888888888" },
    });
    app.unmount();
  });
});
