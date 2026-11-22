import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import TeamManagementView from "../src/views/TeamManagementView.vue";
import CtmsView from "../src/views/CtmsView.vue";
import CtmsDelegationSection from "../src/components/clinical/CtmsDelegationSection.vue";
import { apiClient, ApiError } from "../src/api/apiClient";
import { ctmsApi } from "../src/api/ctmsApi";
import * as authorization from "../src/api/authorization.js";
import { orgApi } from "../src/api/orgApi";
import { useAuthStore } from "../src/stores/auth";

const sampleDirectory = {
  tenant_code: "cadence-local",
  tenant_name: "Cadence Local",
  actor_id: "person:01950000-0000-7000-8000-000000000001", // deid-ignore
  can_invite: true,
  can_delegate: true,
  can_reveal_identity: true,
  roles: [
    {
      role_id: "01950000-0000-7000-8000-000000000202", // deid-ignore
      code: "DEMO_INVESTIGATOR",
      name: "Site investigator",
      capabilities: ["collection.sign", "collection.record"],
    },
  ],
  scopes: [
    {
      key: "StudySite:01950000-0000-7000-8000-000000000401", // deid-ignore
      level: "StudySite",
      target_id: "01950000-0000-7000-8000-000000000401", // deid-ignore
      country: null,
      code: "SITE-101",
      label: "StudySite: SITE-101",
      parent_label: "Study: CAD-ONC-2026",
      can_issue_grants: true,
    },
  ],
  delegable_grants: [],
  duty_catalog: [
    {
      duty: "CRF_DATA_ENTRY",
      label: "CRF Data Entry",
      capabilities: ["collection.record"],
    },
  ],
  members: [
    {
      person_id: "cf64a3b4-33cc-4925-8452-75e29ca9526e", // deid-ignore
      display_name: "Member cf64a3b4",
      email: null,
      identity_revealed: false,
      has_sealed_identity: true,
      can_reveal_identity: true,
      joined_at: "2026-01-05T09:00:00Z", // deid-ignore
      active_grants: [
        {
          grant_id: "01950000-0000-7000-8000-000000000102", // deid-ignore
          kind: "role",
          summary: "Site investigator (DEMO_INVESTIGATOR)",
          role_id: "01950000-0000-7000-8000-000000000202", // deid-ignore
          role_code: "DEMO_INVESTIGATOR",
          capability: null,
          capabilities: ["collection.sign", "collection.record"],
          scope_level: "StudySite",
          scope_target_id: "01950000-0000-7000-8000-000000000401", // deid-ignore
          scope_country: null,
          scope_code: "SITE-101",
          scope_label: "StudySite: SITE-101",
          parent_grant_id: null,
          with_grant_option: true,
          issued_by: "person:01950000-0000-7000-8000-000000000001", // deid-ignore
          issued_at: "2026-01-05T09:00:00Z", // deid-ignore
          reason_for_change: "Initial site assignment",
          valid_from: "2026-01-05T09:00:00Z", // deid-ignore
          valid_to: null,
          is_active: true,
          is_revoked: false,
          can_revoke: true,
        },
      ],
      historical_grants: [],
    },
  ],
};

describe("TeamManagementView & CTMS Email Delegation", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.restoreAllMocks();
  });

  it("keeps seat invitation form to 4 inputs or fewer before the mandatory reason for change", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify(sampleDirectory), { status: 200 })
    );

    const wrapper = mount(TeamManagementView);
    await flushPromises();

    const form = wrapper.get('[data-testid="seat-invite-form"]');
    const controls = form.findAll("input, select, textarea");
    const reasonIdx = controls.findIndex(
      (el) => el.attributes("id") === "invite-reason"
    );

    expect(reasonIdx).toBeGreaterThan(0);
    expect(reasonIdx).toBeLessThanOrEqual(4);
  });

  it("submits seat invitation with canonical scope_level, X-Change-Reason, and renders structured refusal explanations verbatim", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        new Response(JSON.stringify(sampleDirectory), { status: 200 })
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            title: "Authorization denied",
            detail: "Caller lacks grant.issue at StudySite SITE-101.",
            explanation: {
              claim: "Action grant.issue is not permitted.",
              conditions: [
                {
                  code: "grant.covering",
                  description: "Hold an active grant carrying grant.issue",
                  met: false,
                },
              ],
              next_actions: [
                { description: "Request Sponsor Admin authorization." },
              ],
            },
          }),
          { status: 403 }
        )
      );

    const wrapper = mount(TeamManagementView);
    await flushPromises();

    await wrapper.get("#invite-email").setValue("invitee.site101@example.com"); // deid-ignore
    await wrapper.get("#invite-name").setValue("Site Coordinator");
    await wrapper
      .get("#invite-reason")
      .setValue("Assign site investigator for SITE-101");
    await wrapper
      .get('[data-testid="seat-invite-form"]')
      .trigger("submit.prevent");
    await flushPromises();

    expect(fetchSpy).toHaveBeenCalledTimes(2);
    const [, postInit] = fetchSpy.mock.calls[1];
    expect(postInit.headers["X-Change-Reason"]).toBe(
      "Assign site investigator for SITE-101"
    );
    expect(postInit.headers["Idempotency-Key"]).toMatch(/^invite:/);
    const requestPayload = JSON.parse(postInit.body);
    expect(requestPayload).toMatchObject({
      email: "invitee.site101@example.com", // deid-ignore
      role_code: "DEMO_INVESTIGATOR",
      scope_level: "StudySite",
      scope_target_id: "01950000-0000-7000-8000-000000000401", // deid-ignore
    });

    const explanation = wrapper.get('[data-testid="refusal-explanation"]');
    expect(explanation.text()).toContain(
      "Action grant.issue is not permitted."
    );
    expect(explanation.text()).toContain("grant.covering");
    expect(explanation.text()).toContain(
      "Request Sponsor Admin authorization."
    );
  });

  it("supports audited identity disclosure and per-grant revocation", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        new Response(JSON.stringify(sampleDirectory), { status: 200 })
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            person_id: "cf64a3b4-33cc-4925-8452-75e29ca9526e", // deid-ignore
            display_name: "Principal Investigator",
            email: "pi.site101@example.com", // deid-ignore
            given_name: "Principal",
            family_name: "Investigator",
          }),
          { status: 200 }
        )
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            grant_id: "01950000-0000-7000-8000-000000000102", // deid-ignore
            revoked: true,
            valid_from: "2026-01-05T09:00:00Z", // deid-ignore
            valid_to: "2026-11-20T12:00:00Z", // deid-ignore
          }),
          { status: 200 }
        )
      )
      .mockResolvedValueOnce(
        new Response(JSON.stringify(sampleDirectory), { status: 200 })
      );

    const wrapper = mount(TeamManagementView);
    await flushPromises();

    expect(wrapper.text()).toContain("Member cf64a3b4");
    expect(wrapper.text()).not.toContain("pi.site101@example.com"); // deid-ignore

    await wrapper
      .get('[data-testid="btn-reveal-cf64a3b4-33cc-4925-8452-75e29ca9526e"]') // deid-ignore
      .trigger("click");
    await wrapper
      .get("#reveal-reason")
      .setValue("Inspection verification of site investigator");
    await wrapper
      .get('[data-testid="reveal-identity-modal"] form')
      .trigger("submit.prevent");
    await flushPromises();

    expect(
      wrapper
        .get(
          '[data-testid="revealed-identity-cf64a3b4-33cc-4925-8452-75e29ca9526e"]' // deid-ignore
        )
        .text()
    ).toContain("pi.site101@example.com"); // deid-ignore

    await wrapper
      .get('[data-testid="btn-revoke-01950000-0000-7000-8000-000000000102"]') // deid-ignore
      .trigger("click");
    await wrapper
      .get("#revoke-reason")
      .setValue("Completed clinical site rotation");
    await wrapper
      .get('[data-testid="revoke-grant-modal"] form')
      .trigger("submit.prevent");
    await flushPromises();

    expect(fetchSpy).toHaveBeenCalledTimes(4);
    expect(wrapper.get('[data-testid="team-status-banner"]').text()).toContain(
      "Revoked grant"
    );
  });

  it("locks Escape dismissal while submitting regulated TeamManagementView ClinicalModal actions and restores focus on Escape", async () => {
    vi.spyOn(globalThis, "fetch").mockResolvedValue(
      new Response(JSON.stringify(sampleDirectory), { status: 200 })
    );

    const mountEl = document.createElement("div");
    document.body.appendChild(mountEl);
    const wrapper = mount(TeamManagementView, { attachTo: mountEl });
    await flushPromises();

    const triggerBtn = wrapper.get("#btn-open-delegate-modal");
    triggerBtn.element.focus();
    expect(document.activeElement).toBe(triggerBtn.element);

    await triggerBtn.trigger("click");
    await flushPromises();

    const dialog = wrapper.get('[data-testid="org-delegate-modal"]');
    expect(dialog.attributes("role")).toBe("dialog");
    expect(dialog.attributes("aria-modal")).toBe("true");

    // Lock Escape dismissal while delegation mutation is in-flight (PRD-SYS-001)
    wrapper.vm.submittingDelegate = true;
    await wrapper.vm.$nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await wrapper.vm.$nextTick();
    expect(wrapper.vm.showDelegateModal).toBe(true);

    // Unlock and press Escape -> closes ClinicalModal and restores focus to trigger button
    wrapper.vm.submittingDelegate = false;
    await wrapper.vm.$nextTick();
    document.dispatchEvent(
      new KeyboardEvent("keydown", {
        key: "Escape",
        bubbles: true,
        cancelable: true,
      })
    );
    await wrapper.vm.$nextTick();
    await wrapper.vm.$nextTick();
    expect(wrapper.vm.showDelegateModal).toBe(false);
    expect(document.activeElement).toBe(triggerBtn.element);

    wrapper.unmount();
    mountEl.remove();
  });

  it("retries orgApi mutations once after single-flight 401 token refresh while preserving Idempotency-Key and X-Change-Reason", async () => {
    vi.spyOn(authorization, "getBearerToken").mockResolvedValue("expired-tok");
    const refreshSpy = vi
      .spyOn(authorization, "refreshBearerTokenOnUnauthorized")
      .mockResolvedValue("refreshed-tok");

    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        new Response(JSON.stringify({ code: "TOKEN_EXPIRED" }), { status: 401 })
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            person_id: "01950000-0000-7000-8000-000000000999", // deid-ignore
            grant_id: "01950000-0000-7000-8000-000000000888", // deid-ignore
            reused_existing_account: false,
            reused_existing_grant: false,
            welcome_email_sent: true,
            scope_label: "StudySite: SITE-101",
            valid_from: "2026-11-20T12:00:00Z", // deid-ignore
            valid_to: null,
          }),
          { status: 201 }
        )
      );

    const res = await orgApi.inviteMember({
      email: "crc.refresh@example.com", // deid-ignore
      role_code: "DEMO_CRC",
      scope_level: "StudySite",
      scope_target_id: "01950000-0000-7000-8000-000000000401", // deid-ignore
      reason_for_change: "Provision site coordinator after token refresh",
    });

    expect(res.person_id).toBe("01950000-0000-7000-8000-000000000999"); // deid-ignore
    expect(refreshSpy).toHaveBeenCalledTimes(1);
    expect(fetchSpy).toHaveBeenCalledTimes(2);
    const [, firstInit] = fetchSpy.mock.calls[0];
    const [, secondInit] = fetchSpy.mock.calls[1];
    expect(firstInit.headers.Authorization).toBe("Bearer expired-tok");
    expect(secondInit.headers.Authorization).toBe("Bearer refreshed-tok");
    expect(secondInit.headers["Idempotency-Key"]).toBe(
      firstInit.headers["Idempotency-Key"]
    );
    expect(secondInit.headers["X-Change-Reason"]).toBe(
      firstInit.headers["X-Change-Reason"]
    );
  });

  it("allows CTMS delegation by colleague email without pre-existing Staff User ID and renders kernel refusal explanations", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.rawRoles = ["Site Investigator"];
    authStore.assignedSites = ["SITE-101"];

    vi.spyOn(apiClient, "get").mockResolvedValue([]);
    vi.spyOn(ctmsApi, "getSiteDoaLog").mockResolvedValue({
      site_id: "SITE-101",
      delegated_staff: [],
      audit_history: [],
      pi_name: "PI Lead",
    });

    const delegateSpy = vi
      .spyOn(ctmsApi, "delegateSiteTasks")
      .mockRejectedValueOnce(
        new ApiError(
          "Delegation exceeds held authority",
          422,
          "Unprocessable",
          {
            title: "Delegation exceeds held authority",
            explanation: {
              claim: "Requested duty capability exceeds parent grant.",
              conditions: [
                {
                  code: "parent_grant_capabilities",
                  description: "Parent grant must hold observation.lock.",
                  met: false,
                },
              ],
              next_actions: [
                { action: "Select only duties held at SITE-101." },
              ],
            },
          }
        )
      );

    const wrapper = mount(CtmsView, { global: { plugins: [pinia] } });
    await flushPromises();
    wrapper.vm.activeTab = "delegation";
    await flushPromises();

    await wrapper.get("#btn-delegate-task").trigger("click");
    await wrapper.get("#del-staff-email").setValue("crc.new@example.com"); // deid-ignore
    const firstCheckbox = wrapper.get(
      '[data-testid="ctms-delegate-modal"] input[type="checkbox"]'
    );
    await firstCheckbox.setChecked(true);
    await wrapper
      .get("#del-reason")
      .setValue("Delegate screening duties to new coordinator");
    await wrapper
      .findAll('[data-testid="ctms-delegate-modal"] .btn-primary')[0]
      .trigger("click");
    await flushPromises();

    expect(delegateSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        staff_email: "crc.new@example.com", // deid-ignore
        staff_user_id: null,
        reason_for_change: "Delegate screening duties to new coordinator",
      }),
      "Delegate screening duties to new coordinator"
    );

    const refusal = wrapper.get('[data-testid="ctms-refusal-explanation"]');
    expect(refusal.text()).toContain(
      "Requested duty capability exceeds parent grant."
    );
    expect(refusal.text()).toContain("parent_grant_capabilities");
  });

  it("allows live CtmsDelegationSection delegation by colleague email and renders kernel refusal explanations", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);
    const authStore = useAuthStore();
    authStore.rawRoles = ["Site Investigator"];
    authStore.assignedSites = ["SITE-101"];

    vi.spyOn(ctmsApi, "getSiteDoaLog").mockResolvedValue({
      site_id: "SITE-101",
      pi_name: "PI Lead",
      delegated_staff: [
        {
          record_id: "rec-01",
          site_id: "SITE-101",
          staff_user_id: "person:01950000-0000-7000-8000-000000000042", // deid-ignore
          task_codes: ["CRF_DATA_ENTRY"],
          start_date: "2026-11-20", // deid-ignore
          end_date: null,
          is_active: true,
          signed_off: false,
          reason_for_change: "Initial site staffing",
          created_by: "pi-01",
          version_index: 1,
          kernel_grant_ids: [],
        },
      ],
      audit_history: [],
    });

    const delegateSpy = vi
      .spyOn(ctmsApi, "delegateSiteTasks")
      .mockRejectedValueOnce(
        new ApiError(
          "Delegation exceeds held authority",
          422,
          "Unprocessable",
          {
            title: "Delegation exceeds held authority",
            explanation: {
              claim: "Requested duty capability exceeds parent grant.",
              conditions: [
                {
                  code: "parent_grant_capabilities",
                  description: "Parent grant must hold observation.lock.",
                  met: false,
                },
              ],
              next_actions: [
                { action: "Select only duties held at SITE-101." },
              ],
            },
          }
        )
      );

    const wrapper = mount(CtmsDelegationSection, {
      props: { studyCode: "CAD-101", siteId: "SITE-101" },
      global: { plugins: [pinia] },
    });
    await flushPromises();

    expect(wrapper.find(".doa-pending_pi_signature").exists()).toBe(true);

    await wrapper.get("#del-staff-email").setValue("crc.live@example.com"); // deid-ignore
    await wrapper.get("#del-start-date").setValue("2026-11-20"); // deid-ignore
    await wrapper
      .get("#del-change-reason")
      .setValue("Delegate CRF entry duty to new site coordinator");
    await wrapper.get("#delegate-task-form").trigger("submit.prevent");
    await flushPromises();

    expect(delegateSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        site_id: "SITE-101",
        study_id: "CAD-101",
        staff_email: "crc.live@example.com", // deid-ignore
        staff_user_id: null,
      }),
      "Delegate CRF entry duty to new site coordinator"
    );

    const refusal = wrapper.get('[data-testid="ctms-refusal-explanation"]');
    expect(refusal.text()).toContain(
      "Requested duty capability exceeds parent grant."
    );
    expect(refusal.text()).toContain("parent_grant_capabilities");
  });
});
