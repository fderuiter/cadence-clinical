import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createMemoryHistory, createRouter } from "vue-router";
import {
  NODE_KINDS,
  parseNodeRef,
  auditEntryNodeRef,
} from "../src/canvas/nodeRef";
import { formatKindTitle, seedNodeSummary } from "../src/canvas/nodeResolver";
import CanvasInspector from "../src/components/canvas/CanvasInspector.vue";
import TicketDetailDrawer from "../src/components/tickets/TicketDetailDrawer.vue";
import { kernelApi } from "../src/api/kernelApi";
import { auditorService } from "../src/api/auditor";
import * as demoMode from "../src/utils/demoMode";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";

describe("Kernel node history, audit_entry NodeRef, shared AuditLogViewer, and non-demo gating", () => {
  let pinia = createPinia();

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    vi.restoreAllMocks();
  });

  it("registers audit_entry in NODE_KINDS, parses audit_entry:<uuid>, and formats Audit event kind title", () => {
    expect(NODE_KINDS).toContain("audit_entry");
    const ref = auditEntryNodeRef("11111111-2222-3333-4444-555555555555");
    expect(ref).toBe("audit_entry:11111111-2222-3333-4444-555555555555");
    expect(parseNodeRef(ref)).toEqual({
      kind: "audit_entry",
      id: "11111111-2222-3333-4444-555555555555",
    });
    expect(formatKindTitle(ref)).toBe("Audit event");
  });

  it("loads bitemporal history on demand via #verb-history in CanvasInspector and opens the Audit Event peek drawer", async () => {
    const obsRef = "observation:33333333-4444-5555-6666-777777777777";
    const auditRef = "audit_entry:11111111-2222-3333-4444-555555555555";
    seedNodeSummary(obsRef, {
      kind: "observation",
      code: "SYSBP",
      title: "Systolic BP 128 mmHg",
      status: "RECORDED",
    });

    const getHistorySpy = vi
      .spyOn(kernelApi, "getNodeHistory")
      .mockResolvedValue({
        node: obsRef,
        mask: null,
        valid_at: null,
        known_at: null,
        entries: [
          {
            entry_id: auditRef,
            audit_entry_ref: auditRef,
            event_kind: "Corrected",
            action: "Corrected",
            recorded_at: "2026-11-20T14:30:00Z",
            valid_at: "2026-11-20T14:00:00Z",
            actor_id: "crc@site.test",
            actor_ref: null,
            actor_kind: "Person",
            command_type: "CorrectObservation",
            outcome: "Committed",
            reason_for_change: "Updated against source monitor",
            target_ref: obsRef,
            target_kind: "observation",
            version: 2,
            summary: "Corrected systolic blood pressure",
            field_changes: [{ field: "value", before: "120", after: "128" }],
            attestation: null,
            sequence: 7,
            hash: "sha256:abcdef0123456789",
            predecessor_hash: "sha256:0123456789abcdef",
            mask: null,
          },
        ],
      });

    const getAuditEntrySpy = vi
      .spyOn(kernelApi, "getAuditEntry")
      .mockResolvedValue({
        audit_entry_ref: auditRef,
        activity_id: "55555555-6666-7777-8888-999999999999",
        recorded_at: "2026-11-20T14:30:00Z",
        valid_at: "2026-11-20T14:00:00Z",
        actor_id: "crc@site.test",
        actor_ref: null,
        actor_kind: "Person",
        command_type: "CorrectObservation",
        action: "Corrected",
        outcome: "Committed",
        reason_for_change: "Updated against source monitor",
        target_ref: obsRef,
        target_kind: "observation",
        version: 2,
        summary: "Corrected systolic blood pressure",
        field_changes: [{ field: "value", before: "120", after: "128" }],
        attestation: null,
        correlation_id: null,
        sequence: 7,
        hash: "sha256:abcdef0123456789",
        predecessor_hash: "sha256:0123456789abcdef",
        mask: null,
      });

    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
      ],
    });
    await router.push(
      "/studies/CARDIO-101?stage=conduct&section=participants&site=SITE-01"
    );
    await router.isReady();

    const wrapper = mount(CanvasInspector, {
      global: { plugins: [router, pinia] },
      props: {
        studyCode: "CARDIO-101",
        nodeRef: obsRef,
        genericNodeRef: obsRef,
        genericNode: {
          ref: obsRef,
          kind: "observation",
          code: "SYSBP",
          title: "Systolic BP 128 mmHg",
          status: "RECORDED",
          mask: null,
        },
        participant: null,
        protocolVersion: null,
        protocolVersionError: "",
        missing: false,
        canManage: true,
        canUnblind: false,
      },
      attachTo: document.body,
    });

    expect(getHistorySpy).not.toHaveBeenCalled();
    const historyBtn = wrapper.find("#verb-history");
    expect(historyBtn.exists()).toBe(true);

    await historyBtn.trigger("click");
    await flushPromises();

    expect(getHistorySpy).toHaveBeenCalledWith(obsRef, {
      validAt: undefined,
      knownAt: undefined,
    });
    expect(wrapper.find("#kernel-history").text()).toContain(
      "Updated against source monitor"
    );

    const inspectBtn = wrapper.find(".audit-peek-trigger");
    expect(inspectBtn.exists()).toBe(true);
    await inspectBtn.trigger("click");
    await flushPromises();

    expect(getAuditEntrySpy).toHaveBeenCalledWith(auditRef);
    expect(document.body.textContent).toContain("Audit Event");

    const openFullBtn = document.body.querySelector(
      '[data-testid="audit-open-full-view"]'
    ) as HTMLButtonElement | null;
    expect(openFullBtn).not.toBeNull();
    openFullBtn?.click();
    await flushPromises();

    expect(router.currentRoute.value.query.site).toBe("SITE-01");
    expect(router.currentRoute.value.query.observation).toBe(obsRef);
    wrapper.unmount();
  });

  it("renders EntityLink chips and shared AuditLogViewer in TicketDetailDrawer", async () => {
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [
        {
          path: "/studies/:studyCode?",
          name: "study",
          component: { template: "<div />" },
        },
        {
          path: "/tickets",
          name: "tickets",
          component: { template: "<div />" },
        },
        { path: "/safety", name: "safety", component: { template: "<div />" } },
        {
          path: "/quality",
          name: "quality",
          component: { template: "<div />" },
        },
        { path: "/etmf", name: "etmf", component: { template: "<div />" } },
      ],
    });
    await router.push("/tickets");
    await router.isReady();

    const wrapper = mount(TicketDetailDrawer, {
      global: { plugins: [router, pinia] },
      props: {
        isOpen: true,
        ticket: {
          id: "TKT-202",
          title: "SAE Follow-up Required",
          status: "OPEN",
          study_id: "CARDIO-101",
          site_id: "SITE-01",
          subject_id: "017-0001",
          related_entity_type: "SAE_CASE",
          related_entity_id: "SAE-889",
        },
        auditLogs: [
          {
            id: "log-1",
            action: "CREATED",
            user_id: "safety_lead",
            timestamp: "2026-11-20T10:00:00Z",
            reason_for_change: "Escalated serious adverse event",
            block_hash: "sha256:9988776655443322",
          },
        ],
        isAuditLoading: false,
      },
    });

    expect(wrapper.find('[data-testid="ticket-study-link"] a').exists()).toBe(
      true
    );
    expect(wrapper.find('[data-testid="ticket-site-link"] a').exists()).toBe(
      true
    );
    expect(wrapper.find('[data-testid="ticket-subject-link"] a').exists()).toBe(
      true
    );
    expect(
      wrapper.find('[data-testid="ticket-related-entity-link"] a').exists()
    ).toBe(true);
    expect(
      wrapper
        .find('[data-testid="ticket-related-entity-link"] a')
        .attributes("href")
    ).toContain("studyId=CARDIO-101");

    for (const [entityType, entityId] of [
      ["CRF_FORM", "FORM-101"],
      ["SUBJECT", "017-0001"],
      ["SUPPLY_KIT", "KIT-42"],
      ["SAE_CASE", "SAE-889"],
      ["MVR_VISIT", "MVR-11"],
      ["CAPA_ACTION", "CAPA-05"],
      ["TMF_DOCUMENT", "TMF-303"],
    ] as const) {
      await wrapper.setProps({
        ticket: {
          id: "TKT-202",
          title: "Ticket entity link check",
          status: "OPEN",
          study_id: "CARDIO-101",
          site_id: "SITE-01",
          subject_id: "017-0001",
          related_entity_type: entityType,
          related_entity_id: entityId,
        },
      });
      await flushPromises();
      const link = wrapper.find('[data-testid="ticket-related-entity-link"] a');
      expect(link.exists()).toBe(true);
    }

    (wrapper.vm as unknown as { activeTab: string }).activeTab = "audit";
    await flushPromises();
    expect(wrapper.find(".audit-log-viewer").exists()).toBe(true);
    expect(wrapper.text()).toContain("Escalated serious adverse event");
  });

  it("gates unbacked etmf/execution audit routes in non-demo builds and eliminates fake e3b0c442 hashes", async () => {
    vi.spyOn(demoMode, "isDemoBuild").mockReturnValue(false);
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const activitySpy = vi.spyOn(kernelApi, "activity").mockResolvedValue({
      entries: [
        {
          event_id: "evt-99",
          family: "Evidence",
          type: "ObservationCorrected",
          recorded: "2026-11-20T12:00:00Z",
          occurred_valid: "2026-11-20T12:00:00Z",
          actor_id: "crc@site.test",
          subject: "observation:33333333-4444-5555-6666-777777777777",
          subject_kind: "observation",
          reason_for_change: "Source verified",
          audit_entry_ref: "audit_entry:11111111-2222-3333-4444-555555555555",
          attestation_ref: null,
          correlation_id: "00000000-0000-0000-0000-000000000099",
          mask: null,
        },
      ],
      next: null,
    });

    const logs = await auditorService.getAuditLogs({ limit: 10 });
    expect(activitySpy).toHaveBeenCalledTimes(1);
    expect(logs.items[0].reason_for_change).toBe("Source verified");

    const integrity = await auditorService.getExecutionIntegrity();
    expect(integrity.verified).toBeNull();
    expect(fetchSpy).not.toHaveBeenCalled();

    const knowledgeSource = readFileSync(
      resolve(__dirname, "../src/views/KnowledgeView.vue"),
      "utf8"
    );
    const auditSource = readFileSync(
      resolve(__dirname, "../src/views/AuditView.vue"),
      "utf8"
    );
    expect(knowledgeSource).not.toContain("e3b0c442");
    expect(auditSource).not.toContain("e3b0c442");
  });
});
