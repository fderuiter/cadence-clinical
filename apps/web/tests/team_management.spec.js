import { describe, it, expect, vi, beforeEach } from 'vitest';
import { mount, flushPromises } from '@vue/test-utils';
import TeamManagementView from '../src/views/TeamManagementView.vue';

const sampleDirectory = {
  tenant_id: '01950000-0000-7000-8000-000000000001',
  tenant_code: 'cadence-local',
  can_invite: true,
  can_delegate: true,
  can_revoke: true,
  can_reveal_identity: true,
  members: [
    {
      person_id: 'cf64a3b4-33cc-4925-8452-75e29ca9526e',
      display_label: 'Member cf64a3b4',
      masked_identifier: 'cf64a3b4…9526e',
      has_sealed_identity: true,
      joined_at: '2026-01-05T09:00:00Z',
      active_grants: [
        {
          grant_id: '01950000-0000-7000-8000-000000000102',
          grantee_id: 'person:cf64a3b4-33cc-4925-8452-75e29ca9526e',
          role_id: '01950000-0000-7000-8000-000000000202',
          role_code: 'DEMO_INVESTIGATOR',
          role_name: 'Site investigator',
          effective_capabilities: ['collection.sign', 'observation.record'],
          scope: {
            scope_key: 'StudySite:01950000-0000-7000-8000-000000000401',
            level: 'StudySite',
            target_id: '01950000-0000-7000-8000-000000000401',
            code: 'SITE-101',
            label: 'CADENCE-101 / SITE-101',
          },
          valid_from: '2026-01-05T09:00:00Z',
          valid_to: null,
          with_grant_option: true,
          in_effect: true,
          revoked: false,
          is_delegation: false,
          created_at: '2026-01-05T09:00:00Z',
          issued_by: 'svc:kernel-development-bootstrap',
        },
      ],
      historical_grants: [],
    },
  ],
  roles: [
    {
      role_id: '01950000-0000-7000-8000-000000000202',
      code: 'DEMO_INVESTIGATOR',
      name: 'Site investigator',
      capabilities: ['collection.sign', 'observation.record'],
      contains_unblinding: false,
    },
  ],
  available_scopes: [
    {
      scope_key: 'StudySite:01950000-0000-7000-8000-000000000401',
      level: 'StudySite',
      target_id: '01950000-0000-7000-8000-000000000401',
      code: 'SITE-101',
      label: 'CADENCE-101 / SITE-101',
    },
  ],
  delegable_grants: [],
};

describe('TeamManagementView', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it('keeps seat invitation form to 4 inputs or fewer before the mandatory reason for change', async () => {
    vi.spyOn(globalThis, 'fetch').mockResolvedValue(
      new Response(JSON.stringify(sampleDirectory), { status: 200 }),
    );

    const wrapper = mount(TeamManagementView);
    await flushPromises();

    const form = wrapper.get('[data-testid="seat-invite-form"]');
    const controls = form.findAll('input, select, textarea');
    const reasonIdx = controls.findIndex((el) => el.attributes('id') === 'invite-reason');

    expect(reasonIdx).toBeGreaterThan(0);
    expect(reasonIdx).toBeLessThanOrEqual(4);
  });

  it('submits seat invitation with X-Change-Reason and renders structured refusal explanations', async () => {
    const fetchSpy = vi
      .spyOn(globalThis, 'fetch')
      .mockResolvedValueOnce(new Response(JSON.stringify(sampleDirectory), { status: 200 }))
      .mockResolvedValueOnce(
        new Response(
          JSON.stringify({
            title: 'Delegation exceeds held authority',
            detail: 'Caller lacks grant.issue at StudySite SITE-101.',
            explanation: {
              claim: 'Action grant.issue is not permitted.',
              conditions: [
                {
                  code: 'grant.covering',
                  description: 'Hold an active grant carrying grant.issue',
                  satisfied: false,
                },
              ],
              next_actions: [{ description: 'Request Sponsor Admin authorization.' }],
            },
          }),
          { status: 403 },
        ),
      );

    const wrapper = mount(TeamManagementView);
    await flushPromises();

    await wrapper.get('#invite-email').setValue('new.pi@site101.org');
    await wrapper.get('#invite-name').setValue('Dr. Clara Oswald');
    await wrapper.get('#invite-reason').setValue('Assign principal investigator for SITE-101');
    await wrapper.get('[data-testid="seat-invite-form"]').trigger('submit.prevent');
    await flushPromises();

    expect(fetchSpy).toHaveBeenCalledTimes(2);
    const [, postInit] = fetchSpy.mock.calls[1];
    expect(postInit.headers['X-Change-Reason']).toBe('Assign principal investigator for SITE-101');
    expect(postInit.headers['Idempotency-Key']).toMatch(/^invite:/);

    const explanation = wrapper.get('[data-testid="refusal-explanation"]');
    expect(explanation.text()).toContain('Action grant.issue is not permitted.');
    expect(explanation.text()).toContain('grant.covering');
    expect(explanation.text()).toContain('Request Sponsor Admin authorization.');
  });
});
