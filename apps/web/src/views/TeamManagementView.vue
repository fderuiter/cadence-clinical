<template>
  <div id="section-team-management" class="team-management-view">
    <header class="page-header">
      <div>
        <p class="eyebrow">Clinical Kernel Access &amp; IdentityVault</p>
        <h2>Team &amp; access</h2>
        <p class="subtitle">
          Provision tenant-bound OIDC accounts, assign scoped clinical roles,
          delegate bounded site duties, and revoke grants immediately while
          keeping direct human identifiers sealed in
          <code>IdentityVault</code>.
        </p>
      </div>
      <div class="header-actions">
        <button
          v-if="canDelegate"
          id="btn-open-delegate-modal"
          type="button"
          class="btn btn-secondary"
          @click="openDelegateModal"
        >
          Delegate site duties
        </button>
        <button
          id="btn-refresh-team"
          type="button"
          class="btn btn-secondary"
          :disabled="loading"
          @click="loadDirectory"
        >
          Refresh
        </button>
      </div>
    </header>

    <div
      v-if="statusBanner"
      class="status-banner"
      role="status"
      data-testid="team-status-banner"
    >
      {{ statusBanner }}
    </div>

    <div
      v-if="loadError"
      class="error-banner"
      role="alert"
      data-testid="team-load-error"
    >
      {{ loadError }}
    </div>

    <div
      v-if="activeProblem"
      class="refusal-panel"
      role="alert"
      data-testid="refusal-explanation"
    >
      <div class="refusal-header">
        <strong>{{
          activeProblem.title || "Authorization or policy refusal"
        }}</strong>
        <button
          type="button"
          class="btn-dismiss"
          aria-label="Dismiss refusal explanation"
          @click="activeProblem = null"
        >
          Dismiss
        </button>
      </div>
      <p v-if="activeProblem.detail" class="refusal-detail">
        {{ activeProblem.detail }}
      </p>
      <div v-if="activeProblem.explanation" class="explanation-body">
        <p class="explanation-claim">
          {{ activeProblem.explanation.claim }}
        </p>
        <ul
          v-if="
            activeProblem.explanation.conditions &&
            activeProblem.explanation.conditions.length > 0
          "
          class="condition-list"
        >
          <li
            v-for="cond in activeProblem.explanation.conditions"
            :key="cond.code"
            class="condition-item"
          >
            <span
              class="condition-badge"
              :class="conditionMet(cond) ? 'is-met' : 'is-unmet'"
            >
              {{ conditionMet(cond) ? "Met" : "Unmet" }}
            </span>
            <code>{{ cond.code }}</code>
            <span>{{ cond.description }}</span>
          </li>
        </ul>
        <div
          v-if="
            activeProblem.explanation.next_actions &&
            activeProblem.explanation.next_actions.length > 0
          "
          class="next-actions"
        >
          <span class="next-actions-label">Next steps:</span>
          <ul>
            <li
              v-for="(step, idx) in activeProblem.explanation.next_actions"
              :key="idx"
            >
              {{ step.action || step.description }}
            </li>
          </ul>
        </div>
      </div>
    </div>

    <div class="team-grid">
      <section
        v-if="canInvite"
        class="card invite-card"
        aria-labelledby="invite-heading"
      >
        <h3 id="invite-heading">Invite workspace member</h3>
        <p class="card-hint">
          Creates or reuses the colleague's OIDC login in tenant
          <strong>{{ tenantCode || "current workspace" }}</strong>, dispatches a
          welcome password-setup email when newly created, seals identity
          details in <code>IdentityVault</code>, and issues a scoped kernel
          grant.
        </p>

        <form data-testid="seat-invite-form" @submit.prevent="submitInvitation">
          <div class="form-field">
            <label for="invite-email">Work email address</label>
            <input
              id="invite-email"
              v-model="inviteEmail"
              type="email"
              required
              placeholder="Colleague work email"
              :disabled="submittingInvite"
            />
          </div>

          <div class="form-field">
            <label for="invite-name">Full name (sealed in IdentityVault)</label>
            <input
              id="invite-name"
              v-model="inviteFullName"
              type="text"
              placeholder="Optional display name for sealed record"
              :disabled="submittingInvite"
            />
          </div>

          <div class="form-field">
            <label for="invite-role">Clinical role</label>
            <select
              id="invite-role"
              v-model="inviteRoleCode"
              required
              :disabled="submittingInvite"
            >
              <option
                v-for="role in roleOptions"
                :key="role.code"
                :value="role.code"
              >
                {{ role.name }} ({{ role.code }})
              </option>
            </select>
          </div>

          <div class="form-field">
            <label for="invite-scope">Authority scope</label>
            <select
              id="invite-scope"
              v-model="inviteScopeKey"
              required
              :disabled="submittingInvite"
            >
              <option
                v-for="scope in scopeOptions"
                :key="scope.key"
                :value="scope.key"
              >
                {{ scope.label }}
              </option>
            </select>
          </div>

          <div class="form-field">
            <label for="invite-reason">Reason for change</label>
            <textarea
              id="invite-reason"
              v-model="inviteReason"
              rows="2"
              required
              placeholder="Record regulatory justification for issuing this seat..."
              :disabled="submittingInvite"
            />
          </div>

          <div class="advanced-toggle-row">
            <button
              id="btn-toggle-invite-advanced"
              type="button"
              class="btn-link"
              :aria-expanded="showInviteAdvanced ? 'true' : 'false'"
              @click="showInviteAdvanced = !showInviteAdvanced"
            >
              {{
                showInviteAdvanced
                  ? "Hide validity & delegation options"
                  : "Advanced validity & delegation options"
              }}
            </button>
          </div>

          <div v-if="showInviteAdvanced" class="advanced-fields">
            <div class="form-field">
              <label for="invite-valid-from">Effective from (optional)</label>
              <input
                id="invite-valid-from"
                v-model="inviteValidFrom"
                type="date"
                :disabled="submittingInvite"
              />
            </div>
            <div class="form-field">
              <label for="invite-valid-to">Effective until (optional)</label>
              <input
                id="invite-valid-to"
                v-model="inviteValidTo"
                type="date"
                :disabled="submittingInvite"
              />
            </div>
            <div class="checkbox-row">
              <input
                id="invite-grant-option"
                v-model="inviteWithGrantOption"
                type="checkbox"
                :disabled="submittingInvite"
              />
              <label for="invite-grant-option">
                Allow recipient to delegate subsets of this grant
                (<code>WithGrantOption</code>)
              </label>
            </div>
          </div>

          <button
            id="btn-submit-invite"
            type="submit"
            class="btn btn-primary"
            :disabled="submittingInvite"
          >
            {{
              submittingInvite
                ? "Provisioning seat & grant…"
                : "Invite & issue grant"
            }}
          </button>
        </form>
      </section>

      <section class="card directory-card" aria-labelledby="directory-heading">
        <div class="directory-header">
          <div>
            <h3 id="directory-heading">Workspace members &amp; grants</h3>
            <p class="card-hint">
              Clinical records reference pseudonymous <code>PersonId</code> keys
              only. Direct identifiers require an audited
              <code>identity.reveal.person</code> disclosure.
            </p>
          </div>
          <span class="member-count-pill">
            {{ members.length }}
            {{ members.length === 1 ? "member" : "members" }}
          </span>
        </div>

        <div v-if="loading && members.length === 0" class="empty-state">
          Loading workspace directory…
        </div>
        <div v-else-if="members.length === 0" class="empty-state">
          No workspace members recorded yet.
        </div>

        <div v-else class="member-list" data-testid="team-member-list">
          <article
            v-for="member in members"
            :key="member.person_id"
            class="member-item"
            :data-testid="`member-card-${member.person_id}`"
          >
            <div class="member-summary">
              <div>
                <div class="member-title-row">
                  <strong>{{ formatMemberName(member) }}</strong>
                  <code class="person-id-chip">{{ member.person_id }}</code>
                  <span
                    v-if="member.has_sealed_identity"
                    class="vault-pill"
                    title="Direct identifiers sealed in IdentityVault"
                  >
                    Sealed in IdentityVault
                  </span>
                </div>
                <p
                  v-if="revealedByPerson[member.person_id]"
                  class="revealed-identity"
                  :data-testid="`revealed-identity-${member.person_id}`"
                >
                  Disclosed identity:
                  <strong>{{
                    formatRevealedIdentity(revealedByPerson[member.person_id])
                  }}</strong>
                </p>
              </div>
              <div class="member-actions">
                <button
                  v-if="
                    (member.can_reveal_identity ?? canRevealIdentity) &&
                    member.has_sealed_identity
                  "
                  type="button"
                  class="btn btn-sm btn-secondary"
                  :data-testid="`btn-reveal-${member.person_id}`"
                  @click="openRevealModal(member)"
                >
                  Reveal identity
                </button>
              </div>
            </div>

            <div class="grants-group">
              <div class="grants-label">Active grants</div>
              <div
                v-if="
                  !member.active_grants || member.active_grants.length === 0
                "
                class="no-grants"
              >
                No active grants in effect.
              </div>
              <ul v-else class="grant-list">
                <li
                  v-for="grant in member.active_grants"
                  :key="grant.grant_id"
                  class="grant-row"
                >
                  <div class="grant-meta">
                    <span class="role-pill">{{
                      formatGrantSummary(grant)
                    }}</span>
                    <span class="scope-pill">{{
                      formatGrantScope(grant)
                    }}</span>
                    <span v-if="grant.with_grant_option" class="delegable-pill">
                      Delegable
                    </span>
                    <span
                      v-if="
                        grant.parent_grant_id ||
                        grant.kind === 'delegation' ||
                        grant.is_delegation
                      "
                      class="delegated-pill"
                    >
                      Delegated
                    </span>
                    <span v-if="grant.valid_to" class="expiry-text">
                      Until {{ String(grant.valid_to).slice(0, 10) }}
                    </span>
                  </div>
                  <button
                    v-if="grant.can_revoke ?? canRevoke"
                    type="button"
                    class="btn btn-sm btn-danger-outline"
                    :data-testid="`btn-revoke-${grant.grant_id}`"
                    @click="openRevokeModal(grant)"
                  >
                    Revoke
                  </button>
                </li>
              </ul>
            </div>

            <details
              v-if="
                member.historical_grants && member.historical_grants.length > 0
              "
              class="historical-grants"
            >
              <summary>
                Historical / revoked grants ({{
                  member.historical_grants.length
                }})
              </summary>
              <ul class="grant-list is-historical">
                <li
                  v-for="hGrant in member.historical_grants"
                  :key="hGrant.grant_id"
                  class="grant-row"
                >
                  <div class="grant-meta">
                    <span class="role-pill is-muted">{{
                      formatGrantSummary(hGrant)
                    }}</span>
                    <span class="scope-pill">{{
                      formatGrantScope(hGrant)
                    }}</span>
                    <span class="revoked-pill">
                      {{
                        hGrant.is_revoked || hGrant.revoked_at || hGrant.revoked
                          ? "Revoked"
                          : "Expired"
                      }}
                    </span>
                  </div>
                </li>
              </ul>
            </details>
          </article>
        </div>
      </section>
    </div>

    <!-- Delegate Site Duties Modal -->
    <ClinicalModal
      v-model="showDelegateModal"
      :teleport="false"
      :dismissible="!submittingDelegate"
      :show-footer="false"
      title="Delegate clinical site duties"
      data-testid="org-delegate-modal"
    >
      <p class="card-hint">
        Issues a bounded child grant derived from your held site authority.
        Scope, capabilities, and validity cannot exceed your parent grant.
      </p>

      <form @submit.prevent="submitDelegation">
        <div class="form-field">
          <label for="delegate-email">Delegatee work email</label>
          <input
            id="delegate-email"
            v-model="delegateEmail"
            type="email"
            required
            placeholder="Delegatee work email"
            :disabled="submittingDelegate"
          />
        </div>

        <div class="form-field">
          <label for="delegate-name">Full name (optional)</label>
          <input
            id="delegate-name"
            v-model="delegateFullName"
            type="text"
            placeholder="Sealed in IdentityVault"
            :disabled="submittingDelegate"
          />
        </div>

        <div class="form-field">
          <label for="delegate-site">Site code</label>
          <select
            id="delegate-site"
            v-model="delegateSiteCode"
            required
            :disabled="submittingDelegate"
          >
            <option
              v-for="site in siteScopeOptions"
              :key="site.code"
              :value="site.code"
            >
              {{ site.label }}
            </option>
          </select>
        </div>

        <fieldset class="duties-fieldset" :disabled="submittingDelegate">
          <legend>Delegated clinical duties</legend>
          <div
            v-for="duty in dutyOptions"
            :key="duty.code"
            class="checkbox-row"
          >
            <input
              :id="`duty-${duty.code}`"
              v-model="delegateDuties"
              type="checkbox"
              :value="duty.code"
            />
            <label :for="`duty-${duty.code}`">
              {{ duty.label }} (<code>{{ duty.capabilitiesLabel }}</code
              >)
            </label>
          </div>
        </fieldset>

        <div class="form-field">
          <label for="delegate-valid-to">Valid until (optional)</label>
          <input
            id="delegate-valid-to"
            v-model="delegateValidTo"
            type="date"
            :disabled="submittingDelegate"
          />
        </div>

        <div class="form-field">
          <label for="delegate-reason">Reason for change</label>
          <textarea
            id="delegate-reason"
            v-model="delegateReason"
            rows="2"
            required
            placeholder="Justification for delegating these clinical duties..."
            :disabled="submittingDelegate"
          />
        </div>

        <div class="modal-actions">
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="submittingDelegate"
            @click="showDelegateModal = false"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-delegate"
            type="submit"
            class="btn btn-primary"
            :disabled="submittingDelegate"
          >
            {{ submittingDelegate ? "Delegating…" : "Confirm delegation" }}
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Audited Reveal Identity Modal -->
    <ClinicalModal
      :open="Boolean(revealTarget)"
      :teleport="false"
      :dismissible="!submittingReveal"
      :show-footer="false"
      :title="
        revealTarget
          ? `Reveal sealed identity (${formatMemberName(revealTarget)})`
          : 'Reveal sealed identity'
      "
      data-testid="reveal-identity-modal"
      @close="revealTarget = null"
    >
      <p class="card-hint">
        Disclosing direct identifiers writes an immutable Part 11 audit entry
        against <code>identity.reveal.person</code>.
      </p>
      <form @submit.prevent="confirmRevealIdentity">
        <div class="form-field">
          <label for="reveal-reason">Disclosure justification</label>
          <textarea
            id="reveal-reason"
            v-model="revealReason"
            rows="2"
            required
            placeholder="State regulatory or operational reason for disclosing identity..."
            :disabled="submittingReveal"
          />
        </div>
        <div class="modal-actions">
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="submittingReveal"
            @click="revealTarget = null"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-reveal"
            type="submit"
            class="btn btn-primary"
            :disabled="submittingReveal"
          >
            {{ submittingReveal ? "Disclosing…" : "Disclose identity" }}
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Revoke Grant Modal -->
    <ClinicalModal
      :open="Boolean(revokeTarget)"
      :teleport="false"
      :dismissible="!submittingRevoke"
      :show-footer="false"
      title="Revoke clinical grant"
      data-testid="revoke-grant-modal"
      @close="revokeTarget = null"
    >
      <p v-if="revokeTarget" class="card-hint">
        Immediately ends
        <strong>{{ formatGrantSummary(revokeTarget) }}</strong> at
        <strong>{{ formatGrantScope(revokeTarget) }}</strong> and cascades to
        any child delegations while preserving historical audit and
        <code>grant_periods</code> provenance.
      </p>
      <form @submit.prevent="confirmRevokeGrant">
        <div class="form-field">
          <label for="revoke-reason">Revocation reason</label>
          <textarea
            id="revoke-reason"
            v-model="revokeReason"
            rows="2"
            required
            placeholder="Record reason for ending this grant..."
            :disabled="submittingRevoke"
          />
        </div>
        <div class="modal-actions">
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="submittingRevoke"
            @click="revokeTarget = null"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-revoke"
            type="submit"
            class="btn btn-danger"
            :disabled="submittingRevoke"
          >
            {{ submittingRevoke ? "Revoking…" : "Revoke grant" }}
          </button>
        </div>
      </form>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { ClinicalModal } from "ui";
import { extractOrgProblem, orgApi } from "../api/orgApi";

const loading = ref(false);
const loadError = ref("");
const statusBanner = ref("");
const activeProblem = ref(null);
const directory = ref(null);
const revealedByPerson = ref({});

const inviteEmail = ref("");
const inviteFullName = ref("");
const inviteRoleCode = ref("");
const inviteScopeKey = ref("");
const inviteReason = ref("");
const showInviteAdvanced = ref(false);
const inviteValidFrom = ref("");
const inviteValidTo = ref("");
const inviteWithGrantOption = ref(false);
const submittingInvite = ref(false);

const showDelegateModal = ref(false);
const delegateEmail = ref("");
const delegateFullName = ref("");
const delegateSiteCode = ref("");
const delegateDuties = ref([]);
const delegateValidTo = ref("");
const delegateReason = ref("");
const submittingDelegate = ref(false);

const revealTarget = ref(null);
const revealReason = ref("");
const submittingReveal = ref(false);

const revokeTarget = ref(null);
const revokeReason = ref("");
const submittingRevoke = ref(false);

const tenantCode = computed(() => directory.value?.tenant_code || "");
const canInvite = computed(() => Boolean(directory.value?.can_invite));
const canDelegate = computed(() => Boolean(directory.value?.can_delegate));
const canRevoke = computed(() => Boolean(directory.value?.can_revoke));
const canRevealIdentity = computed(() =>
  Boolean(directory.value?.can_reveal_identity)
);
const members = computed(() => directory.value?.members || []);
const roleOptions = computed(() => directory.value?.roles || []);

const allScopeOptions = computed(() => {
  const raw =
    directory.value?.scopes || directory.value?.available_scopes || [];
  return raw.map((s) => {
    const level = s.level || s.kind || "Tenant";
    const targetId = s.target_id || "";
    const country = s.country || null;
    const key = s.key || s.scope_key || `${level}:${targetId}:${country || ""}`;
    return {
      key,
      level,
      targetId: targetId || null,
      country,
      code: s.code || null,
      label: s.label || (s.code ? `${level}: ${s.code}` : level),
      canIssueGrants: s.can_issue_grants !== false,
    };
  });
});

const scopeOptions = computed(() => {
  const issuable = allScopeOptions.value.filter((s) => s.canIssueGrants);
  return issuable.length > 0 ? issuable : allScopeOptions.value;
});

const siteScopeOptions = computed(() => {
  const sites = allScopeOptions.value.filter(
    (s) => s.level === "StudySite" && s.code
  );
  if (sites.length > 0) return sites;
  return [{ code: "SITE-101", label: "SITE-101" }];
});

const dutyOptions = computed(() => {
  const raw = directory.value?.duty_catalog?.length
    ? directory.value.duty_catalog
    : [
        {
          duty: "CRF_DATA_ENTRY",
          label: "CRF Data Entry",
          capabilities: ["collection.record"],
        },
        {
          duty: "SUBJECT_SCREENING",
          label: "Subject Screening",
          capabilities: ["participant.screen"],
        },
        {
          duty: "INFORMED_CONSENT",
          label: "Informed Consent",
          capabilities: ["participant.register"],
        },
      ];
  return raw.map((d) => {
    const code = d.duty || d.duty_code || "";
    const capabilitiesLabel = Array.isArray(d.capabilities)
      ? d.capabilities.join(", ")
      : d.capability || "";
    return {
      code,
      label: d.label || code,
      capabilitiesLabel,
    };
  });
});

function conditionMet(cond) {
  if (typeof cond?.met === "boolean") return cond.met;
  if (typeof cond?.satisfied === "boolean") return cond.satisfied;
  return false;
}

function formatMemberName(member) {
  if (member?.display_name) return member.display_name;
  if (member?.display_label) return member.display_label;
  return `Member ${String(member?.person_id || "").slice(0, 8)}`;
}

function formatGrantSummary(grant) {
  if (grant?.summary) return grant.summary;
  if (grant?.role_name) return grant.role_name;
  if (grant?.role_code) return grant.role_code;
  if (grant?.capability) return grant.capability;
  return "Grant";
}

function formatGrantScope(grant) {
  if (grant?.scope_label) return grant.scope_label;
  if (grant?.scope?.label) return grant.scope.label;
  if (grant?.scope_code) return grant.scope_code;
  if (grant?.scope_level) return grant.scope_level;
  if (grant?.scope_kind) return grant.scope_kind;
  return "Workspace";
}

function formatRevealedIdentity(info) {
  if (!info) return "";
  const fullName = [info.given_name, info.family_name]
    .filter(Boolean)
    .join(" ")
    .trim();
  if (fullName && info.email) return `${fullName} <${info.email}>`;
  return info.email || fullName || info.person_id;
}

function handleApiFailure(err, fallbackTitle) {
  const problem = extractOrgProblem(err);
  if (problem) {
    activeProblem.value = {
      ...problem,
      title: problem.title || fallbackTitle,
    };
  } else {
    activeProblem.value = {
      title: fallbackTitle,
      detail: err?.message || "Unexpected request error.",
    };
  }
}

async function loadDirectory() {
  loading.value = true;
  loadError.value = "";
  try {
    const data = await orgApi.getTeam();
    directory.value = data;
    if (!inviteRoleCode.value && data?.roles?.length) {
      inviteRoleCode.value = data.roles[0].code;
    }
    if (!inviteScopeKey.value && scopeOptions.value.length > 0) {
      inviteScopeKey.value = scopeOptions.value[0].key;
    }
  } catch (err) {
    loadError.value =
      err?.message || "Workspace team directory could not be loaded.";
  } finally {
    loading.value = false;
  }
}

async function submitInvitation() {
  if (!inviteEmail.value.trim() || !inviteReason.value.trim()) return;
  activeProblem.value = null;
  statusBanner.value = "";
  submittingInvite.value = true;

  const selectedScope = scopeOptions.value.find(
    (s) => s.key === inviteScopeKey.value
  ) ||
    scopeOptions.value[0] || {
      level: "Tenant",
      targetId: null,
      country: null,
    };

  try {
    const res = await orgApi.inviteMember({
      email: inviteEmail.value.trim(),
      full_name: inviteFullName.value.trim() || null,
      role_code: inviteRoleCode.value,
      scope_level: selectedScope.level,
      scope_target_id: selectedScope.targetId,
      scope_country: selectedScope.country || null,
      valid_from: inviteValidFrom.value
        ? new Date(`${inviteValidFrom.value}T00:00:00Z`).toISOString()
        : null,
      valid_to: inviteValidTo.value
        ? new Date(`${inviteValidTo.value}T23:59:59Z`).toISOString()
        : null,
      with_grant_option: inviteWithGrantOption.value,
      reason_for_change: inviteReason.value.trim(),
    });

    const personRef = res.person_ref || `person:${res.person_id}`;
    statusBanner.value = res.reused_existing_account
      ? `Issued grant ${res.grant_id} for existing tenant account (${personRef}).`
      : `Provisioned OIDC account (${personRef}), dispatched welcome email, and issued grant ${res.grant_id}.`;
    inviteEmail.value = "";
    inviteFullName.value = "";
    inviteReason.value = "";
    await loadDirectory();
  } catch (err) {
    handleApiFailure(err, "Seat invitation refused");
  } finally {
    submittingInvite.value = false;
  }
}

function openDelegateModal() {
  delegateEmail.value = "";
  delegateFullName.value = "";
  delegateSiteCode.value = siteScopeOptions.value[0]?.code || "SITE-101";
  delegateDuties.value = dutyOptions.value.slice(0, 1).map((d) => d.code);
  delegateValidTo.value = "";
  delegateReason.value = "";
  activeProblem.value = null;
  showDelegateModal.value = true;
}

async function submitDelegation() {
  if (
    !delegateEmail.value.trim() ||
    delegateDuties.value.length === 0 ||
    !delegateReason.value.trim()
  ) {
    return;
  }
  activeProblem.value = null;
  submittingDelegate.value = true;
  try {
    const res = await orgApi.delegateDuties({
      email: delegateEmail.value.trim(),
      full_name: delegateFullName.value.trim() || null,
      site_code: delegateSiteCode.value,
      duties: delegateDuties.value,
      valid_to: delegateValidTo.value
        ? new Date(`${delegateValidTo.value}T23:59:59Z`).toISOString()
        : null,
      reason_for_change: delegateReason.value.trim(),
    });
    showDelegateModal.value = false;
    const personRef = res.person_ref || `person:${res.person_id}`;
    statusBanner.value = `Delegated ${res.grant_ids.length} bounded grant(s) to ${personRef}.`;
    await loadDirectory();
  } catch (err) {
    showDelegateModal.value = false;
    handleApiFailure(err, "Duty delegation refused");
  } finally {
    submittingDelegate.value = false;
  }
}

function openRevealModal(member) {
  revealTarget.value = member;
  revealReason.value = "";
  activeProblem.value = null;
}

async function confirmRevealIdentity() {
  if (!revealTarget.value || !revealReason.value.trim()) return;
  submittingReveal.value = true;
  try {
    const targetId = revealTarget.value.person_id;
    const disclosed = await orgApi.revealMemberIdentity(
      targetId,
      revealReason.value.trim()
    );
    revealedByPerson.value = {
      ...revealedByPerson.value,
      [targetId]: disclosed,
    };
    revealTarget.value = null;
  } catch (err) {
    revealTarget.value = null;
    handleApiFailure(err, "Identity disclosure refused");
  } finally {
    submittingReveal.value = false;
  }
}

function openRevokeModal(grant) {
  revokeTarget.value = grant;
  revokeReason.value = "";
  activeProblem.value = null;
}

async function confirmRevokeGrant() {
  if (!revokeTarget.value || !revokeReason.value.trim()) return;
  submittingRevoke.value = true;
  try {
    const grantId = revokeTarget.value.grant_id;
    await orgApi.revokeGrant(grantId, revokeReason.value.trim());
    revokeTarget.value = null;
    statusBanner.value = `Revoked grant ${grantId}. Downstream authorization ended immediately.`;
    await loadDirectory();
  } catch (err) {
    revokeTarget.value = null;
    handleApiFailure(err, "Grant revocation refused");
  } finally {
    submittingRevoke.value = false;
  }
}

onMounted(() => {
  loadDirectory();
});
</script>

<style scoped>
.team-management-view {
  max-width: 1180px;
  margin: 24px auto 56px;
  padding: 0 20px;
  color: var(--semantic-color-text);
}

.page-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  margin-bottom: 20px;
}

.eyebrow {
  text-transform: uppercase;
  letter-spacing: 0.08em;
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-interactive-primary);
  margin: 0 0 4px;
}

.page-header h2 {
  font-size: 1.65rem;
  margin: 0 0 6px;
}

.subtitle {
  color: var(--semantic-color-text-muted);
  font-size: 0.94rem;
  max-width: 760px;
  margin: 0;
}

.header-actions {
  display: flex;
  gap: 8px;
}

.status-banner {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
  border-radius: 8px;
  padding: 12px 16px;
  margin-bottom: 16px;
  font-size: 0.9rem;
}

.error-banner {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
  border-radius: 8px;
  padding: 12px 16px;
  margin-bottom: 16px;
}

.refusal-panel {
  background: var(--semantic-color-rose-bg);
  border: 1px solid var(--semantic-color-rose-border);
  border-left: 4px solid var(--semantic-color-error);
  color: var(--semantic-color-rose-text);
  border-radius: 8px;
  padding: 14px 18px;
  margin-bottom: 20px;
}

.refusal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
}

.btn-dismiss {
  background: transparent;
  border: 1px solid var(--semantic-color-rose-border);
  color: var(--semantic-color-rose-text);
  border-radius: 6px;
  padding: 3px 8px;
  font-size: 0.78rem;
  cursor: pointer;
}

.refusal-detail,
.explanation-claim {
  margin: 8px 0;
  font-size: 0.9rem;
}

.condition-list {
  list-style: none;
  padding: 0;
  margin: 8px 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.condition-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.86rem;
}

.condition-badge {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  text-transform: uppercase;
}

.condition-badge.is-met {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.condition-badge.is-unmet {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.next-actions {
  margin-top: 8px;
  font-size: 0.86rem;
}

.next-actions-label {
  font-weight: 700;
}

.team-grid {
  display: grid;
  grid-template-columns: minmax(310px, 380px) 1fr;
  gap: 20px;
  align-items: start;
}

@media (max-width: 920px) {
  .team-grid {
    grid-template-columns: 1fr;
  }
}

.card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 12px;
  padding: 20px;
}

.card h3 {
  margin: 0 0 6px;
  font-size: 1.12rem;
}

.card-hint {
  color: var(--semantic-color-text-muted);
  font-size: 0.85rem;
  margin: 0 0 16px;
  line-height: 1.45;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 5px;
  margin-bottom: 14px;
}

.form-field label {
  font-weight: 600;
  font-size: 0.86rem;
}

.form-field input,
.form-field select,
.form-field textarea {
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 0.9rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.advanced-toggle-row {
  margin-bottom: 12px;
}

.btn-link {
  background: none;
  border: none;
  padding: 0;
  color: var(--semantic-color-interactive-primary);
  font-size: 0.84rem;
  font-weight: 600;
  cursor: pointer;
  text-decoration: underline;
}

.advanced-fields {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 12px;
  margin-bottom: 14px;
}

.checkbox-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: 0.85rem;
  margin-bottom: 6px;
}

.directory-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.member-count-pill {
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
  border: 1px solid var(--semantic-color-info-border);
  border-radius: 999px;
  padding: 3px 10px;
  font-size: 0.78rem;
  font-weight: 700;
  white-space: nowrap;
}

.member-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.member-item {
  border: 1px solid var(--semantic-color-border);
  border-radius: 10px;
  padding: 14px;
  background: var(--semantic-color-surface-muted);
}

.member-summary {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.member-title-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.person-id-chip {
  font-size: 0.76rem;
  color: var(--semantic-color-text-muted);
  background: var(--semantic-color-surface-header);
  padding: 2px 6px;
  border-radius: 4px;
}

.vault-pill {
  font-size: 0.73rem;
  font-weight: 600;
  background: var(--semantic-color-teal-bg);
  color: var(--semantic-color-teal-text);
  border: 1px solid var(--semantic-color-teal-border);
  padding: 2px 7px;
  border-radius: 999px;
}

.revealed-identity {
  margin: 6px 0 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
}

.grants-group {
  margin-top: 10px;
}

.grants-label {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  color: var(--semantic-color-text-muted);
  margin-bottom: 6px;
}

.grant-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.grant-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 7px 10px;
}

.grant-meta {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  font-size: 0.83rem;
}

.role-pill {
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 4px;
}

.role-pill.is-muted {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-muted);
}

.scope-pill {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  padding: 2px 8px;
  border-radius: 4px;
}

.delegable-pill,
.delegated-pill {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.revoked-pill {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.historical-grants {
  margin-top: 10px;
  font-size: 0.83rem;
  color: var(--semantic-color-text-muted);
}

.btn-danger-outline {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-error);
  color: var(--semantic-color-error);
}

.btn-danger {
  background: var(--semantic-color-error);
  border: 1px solid var(--semantic-color-error-text-dark);
  color: var(--semantic-color-text-on-solid);
}

.duties-fieldset {
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 8px;
  padding: 10px 12px;
  margin-bottom: 14px;
}

.duties-fieldset legend {
  font-weight: 600;
  font-size: 0.85rem;
  padding: 0 4px;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 16px;
}

.empty-state,
.no-grants {
  color: var(--semantic-color-text-muted);
  font-size: 0.86rem;
}
</style>
