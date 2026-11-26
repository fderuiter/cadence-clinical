/**
 * Unit & Component tests for MediaPreviewModal.vue document approval & 21 CFR Part 11 electronic signature features.
 *
 * Requirements Traceability: PRD-SYS-001, PRD-DOC-001 | GxP 21 CFR Part 11
 */

import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import MediaPreviewModal from "../../src/components/fileshare/MediaPreviewModal.vue";
import { fileshareService } from "../../src/api/fileshare";

describe("MediaPreviewModal.vue - Document Approval & Signatures", () => {
  let pinia: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.clearAllMocks();
  });

  it("displays 'Approved' badge and signer metadata when fileRecord.approval_status is APPROVED", () => {
    /**
     * @req: PRD-DOC-003
     */
    const fileRecord = {
      id: "doc-101",
      filename: "Approved_Protocol_v1.0.pdf",
      study_id: "STUDY-001",
      mime_type: "application/pdf",
      size_bytes: 1048576,
      version_index: 1,
      approval_status: "APPROVED",
      approved_by: "dr.smith",
      approved_at: "2026-08-22T14:30:00Z",
      approval_reason: "SPONSOR_APPROVAL",
    };

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord,
        permissions: { canDownload: true, canApprove: true },
      },
      global: {
        plugins: [pinia],
      },
    });

    const badge = wrapper.find('[data-testid="approved-badge"]');
    expect(badge.exists()).toBe(true);
    expect(badge.text()).toContain("Approved");
    expect(badge.text()).toContain("dr.smith");

    // "Approve this document" button should NOT be shown when already approved
    expect(wrapper.find('[data-testid="approve-doc-btn"]').exists()).toBe(
      false
    );
  });

  it("displays 'Approve this document' button when permissions.canApprove is true and document is not yet approved", () => {
    const fileRecord = {
      id: "doc-102",
      filename: "Pending_Protocol_v1.0.pdf",
      study_id: "STUDY-001",
      mime_type: "application/pdf",
      size_bytes: 1048576,
      version_index: 1,
      approval_status: "PENDING_APPROVAL",
    };

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord,
        permissions: { canDownload: true, canApprove: true },
      },
      global: {
        plugins: [pinia],
      },
    });

    const approveBtn = wrapper.find('[data-testid="approve-doc-btn"]');
    expect(approveBtn.exists()).toBe(true);
    expect(approveBtn.text()).toContain("Approve this document");
  });

  it("hides 'Approve this document' button when permissions.canApprove is false", () => {
    const fileRecord = {
      id: "doc-103",
      filename: "Pending_Protocol_v1.0.pdf",
      study_id: "STUDY-001",
      mime_type: "application/pdf",
      size_bytes: 1048576,
      version_index: 1,
      approval_status: "PENDING_APPROVAL",
    };

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord,
        permissions: { canDownload: true, canApprove: false },
      },
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.find('[data-testid="approve-doc-btn"]').exists()).toBe(
      false
    );
  });

  it("invokes fileshareService.approveFile when ReAuthModal confirms and updates document state", async () => {
    /**
     * @req: PRD-DOC-003
     */
    const fileRecord = {
      id: "doc-104",
      filename: "Pending_Protocol_v1.0.pdf",
      study_id: "STUDY-001",
      mime_type: "application/pdf",
      size_bytes: 1048576,
      version_index: 1,
      approval_status: "PENDING_APPROVAL",
    };

    const approveSpy = vi
      .spyOn(fileshareService, "approveFile")
      .mockResolvedValue({
        id: "doc-104",
        approval_status: "APPROVED",
        approved_by: "authenticated_user",
        approved_at: "2026-08-22T14:35:00Z",
        approval_reason: "APPROVAL",
      } as any);

    const wrapper = mount(MediaPreviewModal, {
      props: {
        fileRecord,
        permissions: { canDownload: true, canApprove: true },
      },
      global: {
        plugins: [pinia],
      },
    });

    // Click approve button to open ReAuthModal
    const approveBtn = wrapper.find('[data-testid="approve-doc-btn"]');
    await approveBtn.trigger("click");

    expect((wrapper.vm as any).isReAuthOpen).toBe(true);

    // Trigger confirmation handler
    await (wrapper.vm as any).handleApproveConfirm({
      credential: "SecretPIN1234!",
      signing_reason: "APPROVAL",
    });

    expect(approveSpy).toHaveBeenCalledWith("doc-104", {
      credential: "SecretPIN1234!",
      signing_reason: "APPROVAL",
    });

    expect(wrapper.emitted("approved")).toBeTruthy();
  });
});
