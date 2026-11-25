// @vitest-environment jsdom
import { createApp, defineComponent, h, nextTick, ref } from "vue";
import { describe, expect, it } from "vitest";

import * as callerScopeBarrel from "../callerScope.js";
import * as dataTableBarrel from "../data-table.js";
import * as econsentBarrel from "../econsent.js";
import * as portalOverlaysBarrel from "../portal-overlays.js";
import * as portalShellBarrel from "../portal-shell.js";
import * as sessionBarrel from "../session.js";
import * as signingBarrel from "../signing.js";
import * as buildersBarrel from "../src/builders.js";
import { defineLazyOverlay } from "../src/composables/useLazyOverlay.js";

describe("defineLazyOverlay and granular ui/* subpath entrypoints", () => {
  it("resolves lazy overlay components when an outer v-if gate opens and forwards props and slots", async () => {
    let loadCount = 0;
    const InnerDialog = defineComponent({
      name: "InnerDialog",
      props: {
        heading: {
          type: String,
          required: true,
        },
      },
      setup(props, { slots }) {
        return () =>
          h("div", { id: "lazy-dialog-probe", role: "dialog" }, [
            h("h2", props.heading),
            slots.default ? slots.default() : null,
          ]);
      },
    });

    const LazyDialog = defineLazyOverlay(async () => {
      loadCount += 1;
      return { default: InnerDialog };
    });

    const pending = (
      globalThis as unknown as {
        __CADENCE_LAZY_OVERLAY_PROMISES__?: Set<Promise<unknown>>;
      }
    ).__CADENCE_LAZY_OVERLAY_PROMISES__;
    if (pending && pending.size > 0) {
      await Promise.all(Array.from(pending));
    }

    const isOpen = ref(false);
    const hostEl = document.createElement("div");
    document.body.appendChild(hostEl);

    const app = createApp({
      setup() {
        return () =>
          h("div", [
            isOpen.value
              ? h(
                  LazyDialog,
                  { heading: "Participant Security Check", "data-probe": "ok" },
                  { default: () => h("p", "Body copy") }
                )
              : null,
          ]);
      },
    });

    try {
      app.mount(hostEl);
      await nextTick();
      expect(hostEl.querySelector("#lazy-dialog-probe")).toBeNull();

      isOpen.value = true;
      await nextTick();

      const mountedDialog = hostEl.querySelector("#lazy-dialog-probe");
      expect(mountedDialog).not.toBeNull();
      expect(mountedDialog?.getAttribute("data-probe")).toBe("ok");
      expect(mountedDialog?.querySelector("h2")?.textContent).toBe(
        "Participant Security Check"
      );
      expect(mountedDialog?.querySelector("p")?.textContent).toBe("Body copy");
      expect(loadCount).toBe(1);
    } finally {
      app.unmount();
      hostEl.remove();
    }
  });

  it("exposes narrow portal-shell exports without pulling heavy data table or modal primitives", () => {
    expect(portalShellBarrel.ClinicalBreadcrumbs).toBeDefined();
    expect(portalShellBarrel.ClinicalIcon).toBeDefined();
    expect(portalShellBarrel.ClinicalToastHost).toBeDefined();
    expect(portalShellBarrel.defineLazyOverlay).toBe(defineLazyOverlay);
    expect(typeof portalShellBarrel.handoffFocus).toBe("function");
    expect(typeof portalShellBarrel.initHoverDetection).toBe("function");

    expect("ClinicalDataTable" in portalShellBarrel).toBe(false);
    expect("ClinicalDrawer" in portalShellBarrel).toBe(false);
    expect("SignOutConfirmModal" in portalShellBarrel).toBe(false);
  });

  it("exposes expected symbols from overlay, data-table, session, caller-scope, signing, econsent, and builders subpaths", () => {
    expect(portalOverlaysBarrel.ClinicalDrawer).toBeDefined();
    expect(portalOverlaysBarrel.ClinicalModal).toBeDefined();
    expect(portalOverlaysBarrel.EntityLink).toBeDefined();
    expect(portalOverlaysBarrel.ModalErrorAlert).toBeDefined();
    expect(portalOverlaysBarrel.SignOutConfirmModal).toBeDefined();
    expect(typeof portalOverlaysBarrel.useFocusTrap).toBe("function");

    expect(dataTableBarrel.ClinicalDataTable).toBeDefined();
    expect(typeof sessionBarrel.createOidcSessionCoordinator).toBe("function");
    expect(typeof sessionBarrel.sanitizeReturnUrl).toBe("function");
    expect(typeof callerScopeBarrel.decodeJwtPayloadClaims).toBe("function");
    expect(typeof callerScopeBarrel.extractPrimaryStudyId).toBe("function");
    expect(typeof signingBarrel.encryptAESGCM).toBe("function");
    expect(typeof signingBarrel.validateField).toBe("function");
    expect(typeof econsentBarrel.normalizeApprovedConsent).toBe("function");
    expect(typeof buildersBarrel.createClinicalLookupInput).toBe("function");
  });
});
