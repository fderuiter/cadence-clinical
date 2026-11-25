// @vitest-environment jsdom
import fs from "node:fs";
import path from "node:path";
import { describe, it, expect, vi } from "vitest";
import { createApp, defineComponent, h, nextTick, ref } from "vue";
import {
  ClinicalBreadcrumbs,
  ClinicalDrawer,
  ClinicalIcon,
  ClinicalModal,
  EntityLink,
  decodeJwtPayloadClaims,
  extractAssignedSites,
  extractAssignedStudies,
  extractPrimarySiteId,
  extractPrimaryStudyId,
  formatScopeForbiddenGuidance,
  isForbiddenScopeError,
  parseClaimStringList,
} from "../index.js";

function encodeBase64Url(obj) {
  const json = JSON.stringify(obj);
  return Buffer.from(json, "utf8")
    .toString("base64")
    .replace(/\+/g, "-")
    .replace(/\//g, "_")
    .replace(/=+$/g, "");
}

describe("packages/ui shared portal & callerScope primitives", () => {
  it("extracts studyId and siteId from OIDC claims and JWT payloads", () => {
    expect(parseClaimStringList("CADENCE-101, STUDY-02")).toEqual([
      "CADENCE-101",
      "STUDY-02",
    ]);
    expect(parseClaimStringList(["SITE-101", " SITE-101 ", ""])).toEqual([
      "SITE-101",
    ]);
    expect(parseClaimStringList(null)).toEqual([]);

    const token = `${encodeBase64Url({ alg: "HS256" })}.${encodeBase64Url({
      sub: "subject_001",
      assigned_studies: ["CADENCE-101"],
      site_ids: "SITE-101 SITE-102",
    })}.sig`;

    const decoded = decodeJwtPayloadClaims(token);
    expect(decoded?.sub).toBe("subject_001");
    expect(extractAssignedStudies(token)).toEqual({
      assignedStudies: ["CADENCE-101"],
      hasAllStudiesScope: false,
    });
    expect(extractAssignedSites(token)).toEqual(["SITE-101", "SITE-102"]);
    expect(extractPrimaryStudyId(token)).toBe("CADENCE-101");
    expect(extractPrimarySiteId(token)).toBe("SITE-101");
    expect(decodeJwtPayloadClaims("not-a-jwt")).toBeNull();
    expect(extractPrimaryStudyId(null)).toBeNull();
    expect(extractPrimarySiteId({})).toBeNull();

    expect(
      isForbiddenScopeError({ status: 403, data: { code: "FORBIDDEN_SCOPE" } })
    ).toBe(true);
    expect(
      isForbiddenScopeError({
        status: 403,
        message: "Caller not assigned to study CADENCE-101",
      })
    ).toBe(true);
    expect(isForbiddenScopeError({ status: 404 })).toBe(false);
    expect(
      formatScopeForbiddenGuidance(
        { status: 403, data: { detail: "Scope mismatch" } },
        {
          studyId: "CADENCE-101",
          siteId: "SITE-101",
          assignedStudies: ["CADENCE-101"],
          assignedSites: ["SITE-102"],
        }
      )
    ).toContain("Scope access restricted (Scope mismatch)");
    expect(formatScopeForbiddenGuidance(null, {})).toContain(
      "this study or site"
    );
  });

  it("renders ClinicalBreadcrumbs with aria-label=Breadcrumb, aria-current=page, compactMobile option, and navigation handler", async () => {
    const onSelect = vi.fn();
    const container = document.createElement("div");
    document.body.appendChild(container);

    const app = createApp(
      defineComponent({
        setup() {
          return () =>
            h(ClinicalBreadcrumbs, {
              compactMobile: true,
              items: [
                {
                  id: "study",
                  label: "Cadence Cardiovascular Study",
                  href: "/",
                  onSelect,
                },
                {
                  id: "section",
                  label: "My Surveys",
                  onSelect,
                },
                {
                  id: "record",
                  label: "Daily Health & Vital Diary",
                },
              ],
            });
        },
      })
    );
    app.mount(container);
    await nextTick();

    const nav = container.querySelector('nav[aria-label="Breadcrumb"]');
    expect(nav).not.toBeNull();
    expect(nav.classList.contains("is-compact-mobile")).toBe(true);
    const current = nav.querySelector('[aria-current="page"]');
    expect(current?.textContent?.trim()).toBe("Daily Health & Vital Diary");

    const firstLink = nav.querySelector("a.clinical-breadcrumb-link");
    firstLink.click();
    expect(onSelect).toHaveBeenCalledTimes(1);

    app.unmount();
    container.remove();
  });

  it("renders EntityLink with NodeRef metadata, sr-only context, and emits peek with triggerEl", async () => {
    const onPeek = vi.fn();
    const container = document.createElement("div");
    document.body.appendChild(container);

    const app = createApp(
      defineComponent({
        setup() {
          return () =>
            h("div", [
              h(EntityLink, {
                nodeRef: {
                  type: "expense_claim",
                  id: "claim_01",
                  label: "View details",
                },
                srContext: "Clinic Partner Hotel €135.00",
                tabular: true,
                onPeek,
              }),
              h(ClinicalIcon, { name: "wallet", size: 18 }),
              h(ClinicalIcon, { name: "clipboard-list" }),
              h(ClinicalIcon, { name: "globe" }),
              h(ClinicalIcon, { name: "check" }),
              h(ClinicalIcon, { name: "user" }),
              h(ClinicalIcon, { name: "refresh" }),
              h(ClinicalIcon, { name: "alert-circle" }),
            ]);
        },
      })
    );
    app.mount(container);
    await nextTick();

    const btn = container.querySelector("button.clinical-entity-link");
    expect(btn).not.toBeNull();
    expect(btn.getAttribute("data-entity-type")).toBe("expense_claim");
    expect(btn.getAttribute("data-entity-id")).toBe("claim_01");
    expect(btn.textContent).toContain("View details");
    expect(btn.querySelector(".sr-only")?.textContent).toContain(
      "Clinic Partner Hotel"
    );

    btn.click();
    expect(onPeek).toHaveBeenCalledTimes(1);
    expect(onPeek.mock.calls[0][0].nodeRef.id).toBe("claim_01");
    expect(onPeek.mock.calls[0][0].triggerEl).toBe(btn);

    const icons = container.querySelectorAll("svg.clinical-icon");
    expect(icons.length).toBe(7);
    expect(icons[0].getAttribute("aria-hidden")).toBe("true");

    app.unmount();
    container.remove();
  });

  it("renders ClinicalDrawer in 90dvh bottom-sheet mode and restores focus on close", async () => {
    const open = ref(false);
    const container = document.createElement("div");
    document.body.appendChild(container);

    const app = createApp(
      defineComponent({
        setup() {
          return () =>
            h("div", [
              h(
                "button",
                {
                  id: "trigger-peek",
                  type: "button",
                  onClick: () => {
                    open.value = true;
                  },
                },
                "Inspect Claim"
              ),
              h(
                ClinicalDrawer,
                {
                  modelValue: open.value,
                  "onUpdate:modelValue": (val) => {
                    open.value = val;
                  },
                  position: "bottom",
                  mobileBottomSheet: true,
                  title: "Expense Claim Details",
                  subtitle: "Waiting for coordinator review",
                  teleport: false,
                },
                {
                  default: () =>
                    h("p", "Your study coordinator will check this receipt."),
                  footer: () =>
                    h("button", { type: "button" }, "Open full view"),
                }
              ),
            ]);
        },
      })
    );
    app.mount(container);
    await nextTick();

    const trigger = container.querySelector("#trigger-peek");
    trigger.focus();
    trigger.click();
    await nextTick();

    const panel = container.querySelector(".clinical-drawer-panel");
    expect(panel).not.toBeNull();
    expect(panel.classList.contains("position-bottom")).toBe(true);
    expect(panel.classList.contains("drawer-bottom-sheet")).toBe(true);
    expect(panel.classList.contains("drawer-mobile-bottom-sheet")).toBe(true);
    expect(
      container.querySelector(".clinical-drawer-sheet-handle")
    ).not.toBeNull();

    const closeBtn = container.querySelector(".clinical-drawer-close-btn");
    expect(closeBtn.getAttribute("aria-label")).toBe("Close drawer");
    closeBtn.click();
    await nextTick();

    expect(container.querySelector(".clinical-drawer-panel")).toBeNull();
    expect(document.activeElement).toBe(trigger);

    app.unmount();
    container.remove();
  });

  it("enforces bounded short-landscape flex-column scrolling on ClinicalModal and <=767px bottom-sheet breakpoint on ClinicalDrawer", async () => {
    const modalSource = fs.readFileSync(
      path.resolve(
        import.meta.dirname,
        "../src/components/clinical/ClinicalModal.vue"
      ),
      "utf8"
    );
    expect(modalSource).toMatch(
      /@media\s*\(max-height:\s*480px\)[\s\S]*?\.clinical-modal-dialog\s*\{[\s\S]*?display:\s*flex;[\s\S]*?flex-direction:\s*column;[\s\S]*?max-height:\s*calc\(100dvh\s*-\s*16px\);[\s\S]*?overflow:\s*hidden;/
    );
    expect(modalSource).not.toMatch(
      /@media\s*\(max-height:\s*480px\)[\s\S]*?overflow-y:\s*visible;/
    );

    const drawerSource = fs.readFileSync(
      path.resolve(
        import.meta.dirname,
        "../src/components/clinical/ClinicalDrawer.vue"
      ),
      "utf8"
    );
    expect(drawerSource).toContain("@media (max-width: 767px)");
    expect(drawerSource).toContain(
      ".clinical-drawer-panel:not(.position-bottom) .clinical-drawer-sheet-handle"
    );

    const open = ref(true);
    const container = document.createElement("div");
    document.body.appendChild(container);
    const app = createApp(
      defineComponent({
        setup() {
          return () =>
            h(
              ClinicalModal,
              {
                modelValue: open.value,
                title: "Short Viewport Verification",
                teleport: false,
              },
              {
                default: () => h("p", "Scrollable modal body content."),
              }
            );
        },
      })
    );
    app.mount(container);
    await nextTick();
    expect(container.querySelector(".clinical-modal-dialog")).not.toBeNull();
    expect(container.querySelector(".modal-body")).not.toBeNull();
    expect(container.querySelector(".modal-footer")).not.toBeNull();
    app.unmount();
    container.remove();
  });
});
