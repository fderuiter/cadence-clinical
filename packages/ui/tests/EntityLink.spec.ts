// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from "vitest";
import { createApp, h, nextTick } from "vue";
import {
  EntityLink,
  EntityPeekDrawer,
  buildEntityBinderUrl,
  humanizeEntityStatus,
  normalizeEntitySubjectCode,
  resolveDiaArtifactMeta,
  resolveEntityLinkDescriptor,
} from "../index.js";
import { EntityLink as SrcEntityLink } from "../src/index.js";
import { toBeAccessible } from "../accessibility-matcher.js";

describe("EntityLink & EntityPeekDrawer (packages/ui)", () => {
  afterEach(() => {
    document.body.innerHTML = "";
  });

  it("re-exports EntityLink and EntityPeekDrawer from both src/index.js and root index.js", () => {
    expect(EntityLink).toBeDefined();
    expect(EntityPeekDrawer).toBeDefined();
    expect(SrcEntityLink).toBe(EntityLink);
  });

  it("resolves DIA TMF Reference Model artifacts and normalizes raw participant:<uuid> NodeRefs out of subjectId", () => {
    expect(resolveDiaArtifactMeta("05.02.04")?.artifactName).toBe(
      "Delegation of Authority Log"
    );
    expect(resolveDiaArtifactMeta("IRB approval")?.artifactCode).toBe(
      "04.02.01"
    );
    expect(resolveDiaArtifactMeta("Signed 1572")?.artifactCode).toBe(
      "05.02.01"
    );
    expect(resolveDiaArtifactMeta("Investigator CV")?.artifactCode).toBe(
      "05.02.03"
    );
    expect(resolveDiaArtifactMeta("Financial Disclosure")?.artifactCode).toBe(
      "05.02.02"
    );
    expect(resolveDiaArtifactMeta("Informed Consent")?.artifactCode).toBe(
      "05.02.05"
    );
    expect(resolveDiaArtifactMeta("Follow-up Letter")?.artifactCode).toBe(
      "05.04.02"
    );
    expect(resolveDiaArtifactMeta("Protocol Amendment")?.artifactCode).toBe(
      "01.01.02"
    );
    expect(
      resolveDiaArtifactMeta("Clinical Trial Protocol")?.artifactCode
    ).toBe("01.01.01");
    expect(resolveDiaArtifactMeta("Investigator Brochure")?.artifactCode).toBe(
      "02.01.01"
    );
    expect(resolveDiaArtifactMeta("SAE Report")?.artifactCode).toBe("07.01.01");
    expect(resolveDiaArtifactMeta("Lab Certificate")?.artifactCode).toBe(
      "08.01.01"
    );
    expect(
      resolveDiaArtifactMeta("Statistical Analysis Plan")?.artifactCode
    ).toBe("11.01.01");
    expect(
      resolveDiaArtifactMeta("Executed Clinical Site Agreement")?.artifactCode
    ).toBe("05.02.01");
    expect(resolveDiaArtifactMeta("Zone 5 (05.02.04)")?.artifactCode).toBe(
      "05.02.04"
    );
    expect(resolveDiaArtifactMeta("")).toBeNull();
    expect(resolveDiaArtifactMeta("Unclassified Custom Item")).toBeNull();

    expect(
      normalizeEntitySubjectCode(
        "participant:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e"
      )
    ).toBe("");
    expect(
      normalizeEntitySubjectCode(
        "enrollment:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e"
      )
    ).toBe("");
    expect(
      normalizeEntitySubjectCode("0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e")
    ).toBe("");
    expect(normalizeEntitySubjectCode("SUBJ-104")).toBe("SUBJ-104");

    expect(humanizeEntityStatus("")).toBe("");
    expect(humanizeEntityStatus("TECHNICAL_QC")).toBe("Technical QC");
    expect(humanizeEntityStatus("APPROVED")).toBe("Approved");
    expect(humanizeEntityStatus("FDA_IRB_CHECK")).toBe("FDA IRB Check");

    expect(buildEntityBinderUrl()).toBe("/etmf");
    expect(
      buildEntityBinderUrl({
        studyId: "CADENCE-101",
        siteId: "SITE-101",
        subjectId: "participant:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e",
        artifactCode: "05.02.04",
        documentId: "DOC-777",
        tab: "binder",
      })
    ).toBe(
      "/etmf?studyId=CADENCE-101&siteId=SITE-101&artifactCode=05.02.04&documentId=DOC-777&tab=binder"
    );
  });

  it("hides raw DOC-* identifiers inside supportDetails while rendering human-readable chip labels", () => {
    const desc = resolveEntityLinkDescriptor({
      document: {
        id: "DOC-ABCD1234",
        study_id: "CADENCE-101",
        site_id: "SITE-101",
        zone: 5,
        section: "05.02",
        artifact_code: "05.02.01",
        artifact_type: "FDA Form 1572",
        filename: "fda_1572_signed.pdf",
        status: "TECHNICAL_QC",
        version_index: 2,
        content_checksum: "deadbeef",
      },
      subjectId: "SUBJ-01",
      supportDetails: "Visit: SIV-01",
    });

    expect(desc.chipLabel).toBe("FDA Form 1572");
    expect(desc.chipLabel).not.toContain("DOC-ABCD1234");
    expect(desc.statusLabel).toBe("Technical QC");
    expect(desc.versionLabel).toBe("v2");
    expect(desc.query).toEqual({
      studyId: "CADENCE-101",
      siteId: "SITE-101",
      subjectId: "SUBJ-01",
      artifactCode: "05.02.01",
      documentId: "DOC-ABCD1234",
      tab: "qc-queue",
    });
    expect(desc.supportDetails).toContain("Document ID: DOC-ABCD1234");
    expect(desc.supportDetails).toContain("SHA-256: deadbeef");
    expect(desc.supportDetails).toContain("Visit: SIV-01");

    const fallbackDesc = resolveEntityLinkDescriptor({
      documentId: "DOC-ONLY-99",
    });
    expect(fallbackDesc.chipLabel).toBe("Regulated eTMF Document");
    expect(fallbackDesc.supportDetails).toContain("Document ID: DOC-ONLY-99");
  });

  it("opens contextual EntityPeekDrawer on click, preserves scope on navigate, closes cleanly, and passes WCAG 2.2 AA", async () => {
    const host = document.createElement("div");
    document.body.appendChild(host);

    const onNavigate = vi.fn();
    const onOpenPeek = vi.fn();
    const onClosePeek = vi.fn();

    const app = createApp({
      render() {
        return h(
          EntityLink,
          {
            documentId: "ETMF-LTR-001",
            artifactCode: "05.04.02",
            label: "Follow-up Letter",
            status: "FINALIZED",
            studyId: "CAD-101",
            siteId: "SITE-101",
            subjectId: "SUBJ-004",
            teleport: false,
            showCode: true,
            onNavigate,
            onOpenPeek,
            onClosePeek,
          },
          {
            default: () =>
              h("p", { class: "extra-slot" }, "Signed SIV summary"),
            actions: () =>
              h(
                "button",
                { type: "button", class: "btn btn-secondary preview-slot-btn" },
                "Preview PDF"
              ),
          }
        );
      },
    });
    app.mount(host);
    await nextTick();

    const trigger = host.querySelector(
      "button.clinical-entity-link"
    ) as HTMLButtonElement;
    expect(trigger).not.toBeNull();
    expect(trigger.textContent?.trim()).toBe("Follow-up Letter (05.04.02)");
    expect(trigger.getAttribute("aria-expanded")).toBe("false");

    const initialA11y = await toBeAccessible({ element: host });
    expect(initialA11y.pass).toBe(true);

    trigger.focus();
    trigger.click();
    await nextTick();

    expect(onOpenPeek).toHaveBeenCalledTimes(1);
    expect(trigger.getAttribute("aria-expanded")).toBe("true");
    expect(host.textContent).toContain("CAD-101");
    expect(host.textContent).toContain("SITE-101");
    expect(host.textContent).toContain("SUBJ-004");
    expect(host.textContent).toContain("Finalized");
    expect(host.textContent).toContain("Signed SIV summary");
    expect(host.querySelector(".preview-slot-btn")).not.toBeNull();
    expect(
      host.querySelector('[data-testid="entity-peek-support-details"]')
        ?.textContent
    ).toContain("Document ID: ETMF-LTR-001");

    const openA11y = await toBeAccessible({ element: host });
    expect(openA11y.pass).toBe(true);

    const openBtn = host.querySelector(
      '[data-testid="entity-peek-open-binder"]'
    ) as HTMLAnchorElement;
    expect(openBtn.getAttribute("href")).toBe(
      "/etmf?studyId=CAD-101&siteId=SITE-101&subjectId=SUBJ-004&artifactCode=05.04.02&documentId=ETMF-LTR-001&tab=binder"
    );
    openBtn.dispatchEvent(
      new MouseEvent("click", { bubbles: true, cancelable: true })
    );
    await nextTick();

    expect(onNavigate).toHaveBeenCalledTimes(1);
    expect(onNavigate.mock.calls[0][0].query).toEqual({
      studyId: "CAD-101",
      siteId: "SITE-101",
      subjectId: "SUBJ-004",
      artifactCode: "05.04.02",
      documentId: "ETMF-LTR-001",
      tab: "binder",
    });
    expect(onClosePeek).toHaveBeenCalledTimes(1);
    expect(trigger.getAttribute("aria-expanded")).toBe("false");

    app.unmount();
    host.remove();
  });
});
