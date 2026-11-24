// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from "vitest";
import { createApp, createSSRApp, h, nextTick } from "vue";
import { renderToString } from "vue/server-renderer";
import {
  AuditLogViewer,
  EntityLink,
  SupportDetails,
  adaptKernelActivityEntries,
  adaptKernelAuditEntryDetail,
  adaptKernelHistoryEntries,
  adaptKnowledgeAuditLogs,
  adaptObservationAuditEntries,
  adaptTicketAuditLogs,
  provideEntityLinkResolver,
} from "../index.js";

describe("EntityLink, SupportDetails, auditAdapters, and AuditLogViewer peek drawer", () => {
  const mounted = [];

  afterEach(() => {
    while (mounted.length > 0) {
      const { app, host } = mounted.pop();
      app.unmount();
      host.remove();
    }
  });
  it("renders EntityLink using injected resolver without exposing raw UUIDs", async () => {
    const navigate = vi.fn();
    const App = {
      setup() {
        provideEntityLinkResolver({
          resolve: ({ nodeRef }) => {
            if (
              nodeRef === "observation:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@2"
            ) {
              return {
                nodeRef,
                label: "SYSBP · 128 mmHg",
                kindLabel: "Observation",
                href: "/studies/CARDIO-101/inspect/observation%3A0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e%402",
                navigable: true,
              };
            }
            return null;
          },
          navigate,
        });
        return () =>
          h("div", [
            h(EntityLink, {
              nodeRef: "observation:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6e@2",
            }),
            h(EntityLink, {
              nodeRef: "audit_entry:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6f",
            }),
            h(EntityLink, {
              nodeRef: "participant:0192f3c4-5e6a-7b8c-9d0e-1f2a3b4c5d6a",
              masked: true,
              maskReason: "Blinded",
            }),
          ]);
      },
    };

    const html = await renderToString(createSSRApp(App));
    const visibleText = html.replace(/<[^>]+>/g, " ");
    expect(visibleText).toContain("SYSBP · 128 mmHg");
    expect(visibleText).toContain("Audit event");
    expect(visibleText).toContain("Blinded");
    expect(visibleText).not.toContain("0192f3c4");
  });

  it("nests sequence, hash, and predecessor_hash inside SupportDetails in AuditLogViewer and opens Audit Event peek drawer", async () => {
    const historyEntries = adaptKernelHistoryEntries([
      {
        entry_id: "audit_entry:11111111-2222-3333-4444-555555555555",
        audit_entry_ref: "audit_entry:11111111-2222-3333-4444-555555555555",
        event_kind: "Corrected",
        action: "Corrected",
        recorded_at: "2026-11-20T14:30:00Z",
        valid_at: "2026-11-20T14:00:00Z",
        actor_id: "22222222-3333-4444-5555-666666666666",
        actor_ref: "person:22222222-3333-4444-5555-666666666666",
        actor_kind: "Person",
        command_type: "CorrectObservation",
        outcome: "Committed",
        reason_for_change: "Updated against source vital signs monitor",
        target_ref: "observation:33333333-4444-5555-6666-777777777777@2",
        target_kind: "observation",
        version: 2,
        summary: "Corrected systolic blood pressure",
        field_changes: [{ field: "value", before: "120", after: "128" }],
        attestation: {
          attestation_ref: "attestation:44444444-5555-6666-7777-888888888888",
          actor_id: "22222222-3333-4444-5555-666666666666",
          actor_ref: "person:22222222-3333-4444-5555-666666666666",
          meaning: "Investigator Approval",
          method: "PasswordReentry",
          authenticated_at: "2026-11-20T14:29:55Z",
          recorded_at: "2026-11-20T14:30:00Z",
          target_content_hash: "sha256:9f86d081884c7d65",
        },
        sequence: 42,
        hash: "sha256:abcdef0123456789",
        predecessor_hash: "sha256:0123456789abcdef",
        mask: null,
      },
      {
        entry_id: "masked-1",
        audit_entry_ref: null,
        event_kind: "Restricted",
        action: "Restricted",
        recorded_at: "2026-11-20T15:00:00Z",
        target_kind: "enrollment",
        field_changes: [],
        mask: "Blinded",
      },
    ]);

    const openFullView = vi.fn();
    const fetchAuditEntry = vi.fn().mockResolvedValue({
      audit_entry_ref: "audit_entry:11111111-2222-3333-4444-555555555555",
      activity_id: "55555555-6666-7777-8888-999999999999",
      recorded_at: "2026-11-20T14:30:00Z",
      valid_at: "2026-11-20T14:00:00Z",
      actor_id: "22222222-3333-4444-5555-666666666666",
      actor_ref: "person:22222222-3333-4444-5555-666666666666",
      command_type: "CorrectObservation",
      action: "Corrected",
      outcome: "Committed",
      reason_for_change: "Updated against source vital signs monitor",
      target_ref: "observation:33333333-4444-5555-6666-777777777777@2",
      target_kind: "observation",
      version: 2,
      summary: "Corrected systolic blood pressure",
      field_changes: [{ field: "value", before: "120", after: "128" }],
      attestation: {
        attestation_ref: "attestation:44444444-5555-6666-7777-888888888888",
        actor_id: "22222222-3333-4444-5555-666666666666",
        actor_ref: "person:22222222-3333-4444-5555-666666666666",
        meaning: "Investigator Approval",
        method: "PasswordReentry",
        authenticated_at: "2026-11-20T14:29:55Z",
        recorded_at: "2026-11-20T14:30:00Z",
      },
      sequence: 42,
      hash: "sha256:abcdef0123456789",
      predecessor_hash: "sha256:0123456789abcdef",
      mask: null,
    });

    let exposed;
    const App = {
      setup() {
        provideEntityLinkResolver({
          resolve: ({ nodeRef }) => {
            if (nodeRef?.startsWith("person:")) {
              return { label: "Dr. Alice Chen", navigable: true };
            }
            if (nodeRef?.startsWith("observation:")) {
              return { label: "Systolic BP (128 mmHg)", navigable: true };
            }
            return null;
          },
          openFullView,
          fetchAuditEntry,
        });
        return () =>
          h(AuditLogViewer, {
            entries: historyEntries,
            teleportDrawer: false,
            ref: (inst) => {
              exposed = inst;
            },
          });
      },
    };

    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(App);
    app.mount(host);
    mounted.push({ app, host });
    await nextTick();

    const html = host.innerHTML;
    expect(html).toContain("Dr. Alice Chen");
    expect(html).toContain("Systolic BP (128 mmHg)");
    expect(html).toContain("Investigator Approval");
    expect(html).toContain("RESTRICTED");
    expect(html).toContain("enrollment.unblind");
    expect(html).toContain('class="support-details"');
    expect(html).toContain("#42");
    expect(html).toContain("sha256:abcdef0123456789");
    expect(html).toContain("sha256:0123456789abcdef");

    await exposed.openEventPeek(historyEntries[0]);
    await nextTick();
    expect(exposed.peekOpen).toBe(true);
    expect(fetchAuditEntry).toHaveBeenCalledWith(
      "audit_entry:11111111-2222-3333-4444-555555555555"
    );
    expect(host.textContent).toContain("Audit Event");
    const openFullBtn = host.querySelector(
      '[data-testid="audit-open-full-view"]'
    );
    expect(openFullBtn).not.toBeNull();
    openFullBtn.click();
    await nextTick();
    expect(openFullView).toHaveBeenCalledTimes(1);
  });

  it("strips fake sha256:e3b0c442 digest in adaptKnowledgeAuditLogs and adapts activity, observation, and ticket logs", async () => {
    const knowledge = adaptKnowledgeAuditLogs([
      {
        id: "k-1",
        timestamp: "2026-11-20T10:00:00Z",
        performed_by: "coder_lee",
        action: "APPROVED",
        dictionary: "MedDRA",
        code: "10019211",
        term: "Headache",
        hash: "sha256:e3b0c442",
      },
    ]);
    expect(knowledge[0].hash).toBeNull();
    expect(knowledge[0].changes).toEqual([
      { field: "code", old_val: null, new_val: "10019211" },
      { field: "term", old_val: null, new_val: "Headache" },
    ]);

    const activity = adaptKernelActivityEntries([
      {
        event_id: "evt-1",
        family: "Evidence",
        type: "Cadence.Kernel.ObservationCorrected",
        recorded: "2026-11-20T12:00:00Z",
        actor_id: "22222222-3333-4444-5555-666666666666",
        subject: "observation:33333333-4444-5555-6666-777777777777",
        subject_kind: "observation",
        reason_for_change: "Source verified",
        audit_entry_ref: "audit_entry:11111111-2222-3333-4444-555555555555",
        attestation_ref: "attestation:44444444-5555-6666-7777-888888888888",
        mask: null,
      },
    ]);
    expect(activity[0].audit_entry_ref).toBe(
      "audit_entry:11111111-2222-3333-4444-555555555555"
    );
    expect(activity[0].attestation?.attestation_ref).toBe(
      "attestation:44444444-5555-6666-7777-888888888888"
    );
    expect(activity[0].reason_for_change).toBe("Source verified");

    const obs = adaptObservationAuditEntries(
      [
        {
          id: "oa-1",
          changed_at: "2026-11-20T12:05:00Z",
          changed_by: "crc_patel",
          old_value: "98.6",
          new_value: "99.1",
          reason: "Rechecked oral thermometer",
        },
      ],
      {
        observationRef: "observation:33333333-4444-5555-6666-777777777777",
        field: "TEMP",
      }
    );
    expect(obs[0].target_ref).toBe(
      "observation:33333333-4444-5555-6666-777777777777"
    );
    expect(obs[0].changes[0]).toEqual({
      field: "TEMP",
      old_val: "98.6",
      new_val: "99.1",
    });

    const tickets = adaptTicketAuditLogs(
      [
        {
          id: "ta-1",
          created_at: "2026-11-20T12:10:00Z",
          actor_id: "dm_wong",
          action: "STATUS_CHANGED",
          details: "Escalated to medical monitor",
        },
      ],
      {
        id: "TKT-9",
        relatedEntityType: "ecrf_query",
        relatedEntityId: "66666666-7777-8888-9999-aaaaaaaaaaaa",
      }
    );
    expect(tickets[0].target_ref).toBe(
      "form_query:66666666-7777-8888-9999-aaaaaaaaaaaa"
    );

    const detail = adaptKernelAuditEntryDetail({
      audit_entry_ref: "audit_entry:11111111-2222-3333-4444-555555555555",
      recorded_at: "2026-11-20T14:30:00Z",
      action: "Recorded",
      target_kind: "study",
      field_changes: [],
      sequence: 1,
      hash: "sha256:1111",
      mask: null,
    });
    expect(detail?.sequence).toBe(1);

    const supportHtml = await renderToString(
      createSSRApp(SupportDetails, {
        entries: [{ key: "hash", label: "Entry hash", value: "sha256:1111" }],
      })
    );
    expect(supportHtml).toContain('class="hash-code"');
    expect(supportHtml).toContain('title="sha256:1111"');
  });

  it("renders variant='activity' with listId, EntityLink chips, and exact actor/subject classes", async () => {
    const entries = adaptKernelActivityEntries([
      {
        event_id: "act-1",
        family: "Execution",
        type: "ParticipantScreened",
        recorded: "2026-11-20T12:00:00Z",
        actor_id: "crc@site.test",
        subject: "enrollment:11111111-2222-3333-4444-555555555555",
        subject_kind: "enrollment",
        audit_entry_ref: "audit_entry:99999999-8888-7777-6666-555555555555",
        mask: null,
      },
      {
        event_id: "act-2",
        family: "Execution",
        type: "EnrollmentStatusChanged",
        recorded: "2026-11-20T12:05:00Z",
        actor_id: "crc@site.test",
        subject: null,
        subject_kind: "enrollment",
        mask: "NotPermitted",
        masked_subject_label: "Hidden enrollment (not permitted)",
      },
    ]);

    const App = {
      setup() {
        provideEntityLinkResolver({
          resolve: ({ nodeRef }) => {
            if (nodeRef?.startsWith("enrollment:")) {
              return {
                label: "017-0001 · Enrollment 017-0001",
                href: "/studies/CARDIO-101?node=" + encodeURIComponent(nodeRef),
                navigable: true,
              };
            }
            return null;
          },
        });
        return () =>
          h(AuditLogViewer, {
            entries,
            variant: "activity",
            listId: "activity-list",
            hideHeader: true,
            teleportDrawer: false,
          });
      },
    };

    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(App);
    app.mount(host);
    mounted.push({ app, host });
    await nextTick();

    const items = host.querySelectorAll("#activity-list li");
    expect(items.length).toBe(2);
    expect(items[0].querySelector(".activity-what")?.textContent?.trim()).toBe(
      "Participant screened"
    );
    expect(
      items[0].querySelector(".activity-subject")?.textContent?.trim()
    ).toBe("017-0001 · Enrollment 017-0001");
    expect(items[0].querySelector(".activity-actor")?.textContent?.trim()).toBe(
      "by crc@site.test"
    );
    expect(
      items[1].querySelector(".activity-subject")?.textContent?.trim()
    ).toBe("Hidden enrollment (not permitted)");
  });
});
