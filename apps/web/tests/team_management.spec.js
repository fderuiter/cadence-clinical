import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import TeamManagementView from "../src/views/TeamManagementView.vue";
import CtmsView from "../src/views/CtmsView.vue";
import { apiClient, ApiError } from "../src/api/apiClient";

const sampleDirectory = {
  tenant_code: "cadence-local",
  can_invite: true,
  can_delegate: true,
  can_revoke: true,
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
      kind: "StudySite",
      target_id: "01950000-0000-7000-8000-000000000401", // deid-ignore
      code: "SITE-101",
      label: "StudySite: SITE-101",
    },
  ],
  delegable_grants: [],
  duty_catalog: [
    {
      duty_code: "CRF_DATA_ENTRY",
      label: "CRF Data Entry",
      capability: "collection.record",
    },
  ],
  members: [
    {
      person_id: "cf64a3b4-33cc-4925-8452-75e29ca9526e", // deid-ignore
      person_ref: "person:cf64a3b4-33cc-4925-8452-75e29ca9526e", // deid-ignore
      display_label: "Member cf64a3b4",
      has_sealed_identity: true,
      email: null,
      active_grants: [
        {
          grant_id: "01950000-0000-7000-8000-000000000102", // deid-ignore
          grant_ref: "grant:01950000-0000-7000-8000-000000000102", // deid-ignore
          role_id: "01950000-0000-7000-8000-000000000202", // deid-ignore
          role_code: "DEMO_INVESTIGATOR",
          role_name: "Site investigator",
          scope_kind: "StudySite",
          scope_target_id: "01950000-0000-7000-8000-000000000401", // deid-ignore
          scope_label: "StudySite: SITE-101",
          parent_grant_id: null,
          valid_from: "2026-01-05T09:00:00Z",
          valid_to: null,
          with_grant_option: true,
          capabilities: ["collection.sign", "collection.record"],
          is_active: true,
          revoked_at: null,
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

  it("submits seat invitation with X-Change-Reason and renders structured refusal explanations verbatim", async () => {
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
                { action: "Request Sponsor Admin authorization." },
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

    const explanation = wrapper.get('[data-testid="refusal-explanation"]');
    expect(explanation.text()).toContain(
      "Action grant.issue is not permitted."
    );
    expect(explanation.text()).toContain("grant.covering");
    expect(explanation.text()).toContain(
      "Request Sponsor Admin authorization."
    );
  });

  it("supports audited identity disclosure and immediate grant revocation", async () => {
    const fetchSpy = vi
      .spyOn(globalThis, "fetch")
      .mockResolvedValueOnce(
        new Response(JSON.stringify(sampleDirectory), { status: 200 })
      )
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            person_id: "cf64a3b4-33cc-4925-8452-75e29ca9526e", // deid-ignore
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

  it("allows CTMS delegation by colleague email without pre-existing Staff User ID and renders kernel refusal explanations", async () => {
    vi.stubEnv("VITE_DEMO_MODE", "true");
    const pinia = createPinia();
    setActivePinia(pinia);

    vi.spyOn(apiClient, "get").mockImplementation((url) => {
      if (url.includes("/doa/sites/")) {
        return Promise.resolve({ delegated_staff: [], pi_name: "PI Lead" });
      }
      return Promise.resolve([]);
    });

    const postSpy = vi.spyOn(apiClient, "post").mockRejectedValueOnce(
      new ApiError("Delegation exceeds held authority", 422, "Unprocessable", {
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
          next_actions: [{ action: "Select only duties held at SITE-101." }],
        },
      })
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

    expect(postSpy).toHaveBeenCalledWith(
      "/api/v1/ctms/doa/delegate",
      expect.objectContaining({
        staff_email: "crc.new@example.com", // deid-ignore
        staff_user_id: null,
      }),
      expect.objectContaining({
        changeReason: "Delegate screening duties to new coordinator",
      })
    );

    const refusal = wrapper.get('[data-testid="ctms-refusal-explanation"]');
    expect(refusal.text()).toContain(
      "Requested duty capability exceeds parent grant."
    );
    expect(refusal.text()).toContain("parent_grant_capabilities");
  });
});
