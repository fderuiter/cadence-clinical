<template>
  <section
    id="study-locks-section"
    class="study-locks"
    aria-labelledby="study-locks-title"
    :aria-busy="studyLoading || locksLoading"
  >
    <header class="study-locks-header">
      <div>
        <h2 id="study-locks-title">Locks</h2>
        <p class="study-locks-subtitle">
          Signed <strong>Frozen</strong> and <strong>Locked</strong> assertions
          across study, site, participant, form, and observation scopes.
        </p>
      </div>
      <div class="study-locks-filters" data-testid="study-locks-filters">
        <label class="checkbox-label" for="study-locks-active-only">
          <input
            id="study-locks-active-only"
            v-model="activeOnly"
            type="checkbox"
            @change="reloadLocks"
          />
          Active locks only
        </label>

        <label for="study-locks-as-of">As of (ISO UTC)</label>
        <input
          id="study-locks-as-of"
          v-model="asOfInput"
          type="text"
          autocomplete="off"
          placeholder="Latest truth"
        />
        <button
          id="study-locks-refresh"
          type="button"
          class="btn btn-secondary"
          :disabled="studyLoading || locksLoading"
          @click="reloadLocks"
        >
          Inspect
        </button>
      </div>
    </header>

    <p v-if="studyLoading" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <div
      v-else-if="studyError"
      id="study-locks-error"
      class="status-line error-banner"
      role="alert"
    >
      <p class="status-line">
        {{ studyCode }}'s locks couldn't be loaded.
        <button
          id="btn-retry-study-locks"
          type="button"
          class="btn btn-secondary"
          :disabled="studyLoading"
          @click="loadStudyLocksContext(studyCode)"
        >
          Try again
        </button>
      </p>
      <SupportDetails :detail="studyError" />
    </div>
    <p v-else-if="!studyRef" id="study-locks-no-study" class="status-line">
      Study {{ studyCode }} wasn't found or isn't visible to your account, so
      its locks cannot be listed here.
    </p>

    <template v-else>
      <form
        v-if="canApply"
        id="study-locks-apply-form"
        class="apply-card"
        data-testid="study-locks-apply-form"
        @submit.prevent="openApplyModal"
      >
        <h3>Apply signed freeze or lock</h3>
        <div class="apply-grid">
          <div class="field-group">
            <label for="lock-apply-scope-kind">Scope level</label>
            <select
              id="lock-apply-scope-kind"
              v-model="applyScopeKind"
              @change="onScopeKindChange"
            >
              <option value="study">Study</option>
              <option value="study_site">Study site</option>
              <option value="enrollment">Participant enrollment</option>
              <option value="collection_instance">Form instance</option>
              <option value="observation">Observation</option>
            </select>
          </div>

          <div v-if="needsSitePicker" class="field-group">
            <label for="lock-apply-site">Study site</label>
            <select
              id="lock-apply-site"
              v-model="selectedSiteRef"
              @change="onSitePickerChange"
            >
              <option value="">Select study site…</option>
              <option
                v-for="opt in studySiteOptions"
                :key="opt.key"
                :value="opt.value"
                :disabled="opt.disabled"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div v-if="needsEnrollmentPicker" class="field-group">
            <label for="lock-apply-enrollment">Participant enrollment</label>
            <select
              id="lock-apply-enrollment"
              v-model="selectedEnrollmentRef"
              :disabled="!selectedSiteRef || siteChildrenLoading"
              @change="onEnrollmentPickerChange"
            >
              <option value="">
                {{
                  siteChildrenLoading
                    ? "Loading enrollments…"
                    : "Select participant…"
                }}
              </option>
              <option
                v-for="opt in enrollmentOptions"
                :key="opt.key"
                :value="opt.value"
                :disabled="opt.disabled"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div v-if="needsCollectionPicker" class="field-group">
            <label for="lock-apply-collection">Form instance</label>
            <select
              id="lock-apply-collection"
              v-model="selectedCollectionRef"
              :disabled="!selectedEnrollmentRef || enrollmentChildrenLoading"
              @change="onCollectionPickerChange"
            >
              <option value="">
                {{
                  enrollmentChildrenLoading
                    ? "Loading forms…"
                    : "Select form instance…"
                }}
              </option>
              <option
                v-for="opt in collectionOptions"
                :key="opt.key"
                :value="opt.value"
                :disabled="opt.disabled"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div v-if="needsObservationPicker" class="field-group">
            <label for="lock-apply-observation">Observation</label>
            <select
              id="lock-apply-observation"
              v-model="selectedObservationRef"
              :disabled="!selectedCollectionRef || collectionChildrenLoading"
              @change="onObservationPickerChange"
            >
              <option value="">
                {{
                  collectionChildrenLoading
                    ? "Loading observations…"
                    : "Select observation…"
                }}
              </option>
              <option
                v-for="opt in observationOptions"
                :key="opt.key"
                :value="opt.value"
                :disabled="opt.disabled"
              >
                {{ opt.label }}
              </option>
            </select>
          </div>

          <div class="field-group">
            <label for="lock-apply-scope-ref">Target reference</label>
            <input
              id="lock-apply-scope-ref"
              v-model="applyScopeRef"
              type="text"
              autocomplete="off"
              list="lock-known-scopes"
              :readonly="applyScopeKind === 'study'"
              placeholder="kind:uuid"
            />
            <datalist id="lock-known-scopes">
              <option
                v-for="opt in scopeSuggestions"
                :key="opt.ref"
                :value="opt.ref"
              >
                {{ opt.label }}
              </option>
            </datalist>
          </div>

          <div class="field-group">
            <label for="lock-apply-kind">Assertion</label>
            <select id="lock-apply-kind" v-model="applyKind">
              <option value="Frozen">
                Frozen (blocks entry & corrections)
              </option>
              <option value="Locked">Locked (blocks all changes)</option>
            </select>
          </div>

          <div class="field-group field-group--wide">
            <label for="lock-apply-reason">Reason for change</label>
            <input
              id="lock-apply-reason"
              v-model="applyReason"
              type="text"
              autocomplete="off"
              placeholder="Interim analysis cut / database lock after query closure"
            />
          </div>
        </div>
        <div class="apply-actions">
          <button
            id="btn-apply-study-lock"
            type="submit"
            class="btn btn-primary"
            :disabled="busy || !applyScopeRef.trim() || !applyReason.trim()"
          >
            Sign &amp; apply {{ applyKind }}
          </button>
        </div>
      </form>

      <div v-if="canRelease" class="release-reason-bar">
        <label for="lock-release-reason">Reason when releasing a lock</label>
        <input
          id="lock-release-reason"
          v-model="releaseReason"
          type="text"
          autocomplete="off"
          placeholder="Reopening scope for documented source addendum"
        />
      </div>

      <p
        v-if="outcome"
        id="study-locks-outcome"
        :class="[
          'status-line',
          outcome.kind === 'ok' ? 'ok-banner' : 'error-banner',
        ]"
        :role="outcome.kind === 'ok' ? 'status' : 'alert'"
        aria-live="polite"
      >
        {{ outcome.message }}
      </p>

      <p v-if="locksLoading" class="status-line" role="status">
        Loading locks for {{ studyCode }}…
      </p>
      <div
        v-else-if="locksError"
        id="study-locks-load-error"
        class="status-line error-banner"
        role="alert"
      >
        <p class="status-line">
          {{ studyCode }}'s locks couldn't be loaded.
          <button
            id="btn-retry-locks-list"
            type="button"
            class="btn btn-secondary"
            :disabled="locksLoading"
            @click="reloadLocks"
          >
            Try again
          </button>
        </p>
        <SupportDetails :detail="locksError" />
      </div>
      <p
        v-else-if="locks.length === 0"
        id="study-locks-empty"
        class="status-line"
      >
        No locks recorded on {{ studyCode }} at this point in time.
      </p>
      <table v-else class="locks-table" data-testid="study-locks-table">
        <thead>
          <tr>
            <th scope="col">Kind</th>
            <th scope="col">Scope</th>
            <th scope="col">Participant</th>
            <th scope="col">Signed by</th>
            <th scope="col">Locked at</th>
            <th scope="col">Reason</th>
            <th scope="col">State</th>
            <th v-if="canRelease" scope="col">Action</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(entry, idx) in locks"
            :key="entry.ref ?? `masked-${idx}`"
            :data-masked="entry.masked ? 'true' : undefined"
          >
            <td>
              <span
                :class="[
                  'lock-badge',
                  `lock-badge--${entry.kind.toLowerCase()}`,
                ]"
              >
                {{ entry.kind }}
              </span>
            </td>
            <td>
              <template v-if="entry.masked">
                <span class="masked-pill">
                  Masked ({{ entry.mask ?? "Restricted" }}):
                  {{ scopeLabel(entry.scope_kind) }}
                </span>
              </template>
              <template v-else>
                <NodeRefChip
                  v-if="entry.scope_ref"
                  :node-ref="entry.scope_ref"
                  :fallback-title="entry.subject_code"
                  include-kind
                />
                <span v-else>—</span>
              </template>
            </td>
            <td>
              {{ entry.masked ? "masked" : (entry.subject_code ?? "—") }}
            </td>
            <td>
              <template v-if="entry.masked"> masked </template>
              <template v-else>
                <NodeRefChip
                  v-if="entry.locked_by && parseNodeRef(entry.locked_by)"
                  :node-ref="entry.locked_by"
                />
                <template v-else>{{ entry.locked_by ?? "—" }}</template>
                <span v-if="entry.attestation_meaning" class="meaning-note">
                  ({{ entry.attestation_meaning }})
                </span>
              </template>
            </td>
            <td>{{ entry.masked ? "—" : (entry.locked_at ?? "—") }}</td>
            <td>
              {{ entry.masked ? "masked" : (entry.reason_for_change ?? "—") }}
            </td>
            <td>
              <template v-if="entry.released">
                Released
                <span
                  v-if="!entry.masked && entry.released_at"
                  class="meaning-note"
                >
                  at {{ entry.released_at }}
                </span>
              </template>
              <template v-else> Active </template>
            </td>
            <td v-if="canRelease">
              <button
                v-if="!entry.masked && !entry.released && entry.ref"
                :data-testid="`release-lock-${entry.ref}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy || !releaseReason.trim()"
                @click="openReleaseModal(entry.ref, entry.kind)"
              >
                Release
              </button>
            </td>
          </tr>
        </tbody>
      </table>

      <p
        v-if="!canApply && !canRelease"
        id="study-locks-note"
        class="status-line"
      >
        A data manager with electronic signature authority applies or releases
        study locks.
      </p>
    </template>

    <SignatureCaptureModal
      v-if="modalOp"
      :is-open="Boolean(modalOp)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Data Manager"
      :reasons="LOCK_SIGNING_REASONS"
      :action-url="modalActionUrl"
      :semantic-action="modalSemanticAction"
      :on-sign="executeModalSign"
      @cancel="modalOp = null"
      @success="modalOp = null"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  describeLockError,
  kernelApi,
  LOCK_APPLY_ACTION,
  LOCK_APPLY_SEMANTIC_ACTION,
  LOCK_RELEASE_SEMANTIC_ACTION,
  lockReleaseAction,
  type KernelChild,
  type KernelLock,
  type KernelLockKind,
  type KernelLockMeaning,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import { getNodeDisplay, seedNodeSummary } from "../../canvas/nodeResolver";
import { useAuthStore } from "../../stores/auth.js";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import NodeRefChip from "./NodeRefChip.vue";
import SupportDetails from "./SupportDetails.vue";

const LOCK_SIGNING_REASONS = ["Approved", "Responsibility"];

const SCOPE_LABELS: Record<string, string> = {
  study: "study",
  study_site: "site",
  enrollment: "participant",
  collection_instance: "form",
  observation: "observation",
};

const props = defineProps<{
  studyCode: string;
}>();

const authStore = useAuthStore();

const studyRef = ref<string | null>(null);
const studyLoading = ref(false);
const studyError = ref<string | null>(null);

const locks = ref<KernelLock[]>([]);
const locksLoading = ref(false);
const locksError = ref<string | null>(null);
const activeOnly = ref(false);
const asOfInput = ref("");

const canApply = ref(false);
const canRelease = ref(false);
const studyChildren = ref<KernelChild[]>([]);
const siteChildren = ref<KernelChild[]>([]);
const siteChildrenLoading = ref(false);
const enrollmentChildren = ref<KernelChild[]>([]);
const enrollmentChildrenLoading = ref(false);
const collectionChildren = ref<KernelChild[]>([]);
const collectionChildrenLoading = ref(false);

const selectedSiteRef = ref("");
const selectedEnrollmentRef = ref("");
const selectedCollectionRef = ref("");
const selectedObservationRef = ref("");

const applyScopeKind = ref<string>("study");
const applyScopeRef = ref<string>("");
const applyKind = ref<KernelLockKind>("Frozen");
const applyReason = ref("");
const releaseReason = ref("");
const busy = ref(false);
const outcome = ref<{ kind: "ok" | "error"; message: string } | null>(null);

type ModalOperation =
  | {
      mode: "apply";
      idempotencyKey: string;
      lockId: string;
      attestationId: string;
    }
  | {
      mode: "release";
      lockRef: string;
      lockKind: string;
      idempotencyKey: string;
      assertionId: string;
      attestationId: string;
    };

const modalOp = ref<ModalOperation | null>(null);

const signerUsername = computed(
  () => authStore?.identity?.username ?? authStore?.user?.username ?? ""
);
const signerFullName = computed(() => {
  const identity = authStore?.identity ?? authStore?.user;
  if (!identity) return "";
  const fullName = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return fullName || identity.username || "";
});

interface ScopePickerOption {
  key: string;
  value: string;
  label: string;
  disabled: boolean;
}

function unversionedNodeRef(raw: string | null | undefined): string {
  if (!raw) return "";
  const parsed = parseNodeRef(raw);
  if (parsed) return `${parsed.kind}:${parsed.id}`;
  const at = raw.indexOf("@");
  return at < 0 ? raw : raw.slice(0, at);
}

function seedVisibleChildren(children: KernelChild[]) {
  for (const child of children) {
    if (child.node.ref && !child.node.mask) {
      const summary = {
        kind: child.node.kind,
        code: child.node.code,
        title: child.node.title,
        status: child.node.status,
      };
      seedNodeSummary(child.node.ref, summary);
      const unversioned = unversionedNodeRef(child.node.ref);
      if (unversioned && unversioned !== child.node.ref) {
        seedNodeSummary(unversioned, summary);
      }
    }
  }
}

function buildPickerOptions(
  children: KernelChild[],
  expectedKind: string
): ScopePickerOption[] {
  return children
    .filter((child) => child.node.kind === expectedKind)
    .map((child, idx) => {
      if (child.node.mask || !child.node.ref) {
        return {
          key: `masked-${expectedKind}-${idx}`,
          value: "",
          label: `Masked (${child.node.mask ?? "Restricted"}): ${scopeLabel(expectedKind)}`,
          disabled: true,
        };
      }
      const cleanRef = unversionedNodeRef(child.node.ref);
      const code = child.node.code?.trim();
      const title = child.node.title?.trim();
      const label =
        code && title && code !== title
          ? `${code} — ${title}`
          : code || title || cleanRef;
      return {
        key: cleanRef,
        value: cleanRef,
        label,
        disabled: false,
      };
    });
}

const needsSitePicker = computed(() => applyScopeKind.value !== "study");
const needsEnrollmentPicker = computed(
  () =>
    applyScopeKind.value === "enrollment" ||
    applyScopeKind.value === "collection_instance" ||
    applyScopeKind.value === "observation"
);
const needsCollectionPicker = computed(
  () =>
    applyScopeKind.value === "collection_instance" ||
    applyScopeKind.value === "observation"
);
const needsObservationPicker = computed(
  () => applyScopeKind.value === "observation"
);

const studySiteOptions = computed(() =>
  buildPickerOptions(studyChildren.value, "study_site")
);
const enrollmentOptions = computed(() =>
  buildPickerOptions(siteChildren.value, "enrollment")
);
const collectionOptions = computed(() =>
  buildPickerOptions(enrollmentChildren.value, "collection_instance")
);
const observationOptions = computed(() =>
  buildPickerOptions(collectionChildren.value, "observation")
);

const scopeSuggestions = computed(() => {
  const items: Array<{ ref: string; label: string }> = [];
  if (studyRef.value) {
    items.push({ ref: studyRef.value, label: `Study ${props.studyCode}` });
  }
  const allOptions = [
    ...studySiteOptions.value,
    ...enrollmentOptions.value,
    ...collectionOptions.value,
    ...observationOptions.value,
  ];
  for (const opt of allOptions) {
    if (!opt.disabled && opt.value) {
      items.push({ ref: opt.value, label: opt.label });
    }
  }
  return items.filter((item) =>
    item.ref.startsWith(`${applyScopeKind.value}:`)
  );
});

const modalActionUrl = computed(() => {
  if (!modalOp.value) return LOCK_APPLY_ACTION;
  return modalOp.value.mode === "apply"
    ? LOCK_APPLY_ACTION
    : lockReleaseAction(modalOp.value.lockRef);
});

const modalSemanticAction = computed(() =>
  modalOp.value?.mode === "release"
    ? LOCK_RELEASE_SEMANTIC_ACTION
    : LOCK_APPLY_SEMANTIC_ACTION
);

function scopeLabel(kind: string): string {
  return SCOPE_LABELS[kind] ?? kind;
}

let siteLoadSeq = 0;
let enrollmentLoadSeq = 0;
let collectionLoadSeq = 0;

async function loadSiteChildren(siteRef: string) {
  const seq = ++siteLoadSeq;
  siteChildren.value = [];
  selectedEnrollmentRef.value = "";
  enrollmentChildren.value = [];
  selectedCollectionRef.value = "";
  collectionChildren.value = [];
  selectedObservationRef.value = "";
  if (!siteRef || typeof kernelApi.getChildren !== "function") return;

  siteChildrenLoading.value = true;
  try {
    const children = await kernelApi.getChildren(siteRef);
    if (seq !== siteLoadSeq) return;
    siteChildren.value = children ?? [];
    seedVisibleChildren(siteChildren.value);
    if (needsEnrollmentPicker.value) {
      const firstEnrollment = enrollmentOptions.value.find((o) => !o.disabled);
      selectedEnrollmentRef.value = firstEnrollment?.value ?? "";
      if (applyScopeKind.value === "enrollment") {
        applyScopeRef.value = selectedEnrollmentRef.value;
      } else if (selectedEnrollmentRef.value) {
        await loadEnrollmentChildren(selectedEnrollmentRef.value);
      } else {
        applyScopeRef.value = "";
      }
    }
  } catch {
    if (seq === siteLoadSeq) siteChildren.value = [];
  } finally {
    if (seq === siteLoadSeq) siteChildrenLoading.value = false;
  }
}

async function loadEnrollmentChildren(enrollmentRef: string) {
  const seq = ++enrollmentLoadSeq;
  enrollmentChildren.value = [];
  selectedCollectionRef.value = "";
  collectionChildren.value = [];
  selectedObservationRef.value = "";
  if (!enrollmentRef || typeof kernelApi.getChildren !== "function") return;

  enrollmentChildrenLoading.value = true;
  try {
    const children = await kernelApi.getChildren(enrollmentRef);
    if (seq !== enrollmentLoadSeq) return;
    enrollmentChildren.value = children ?? [];
    seedVisibleChildren(enrollmentChildren.value);
    if (needsCollectionPicker.value) {
      const firstCollection = collectionOptions.value.find((o) => !o.disabled);
      selectedCollectionRef.value = firstCollection?.value ?? "";
      if (applyScopeKind.value === "collection_instance") {
        applyScopeRef.value = selectedCollectionRef.value;
      } else if (selectedCollectionRef.value) {
        await loadCollectionChildren(selectedCollectionRef.value);
      } else {
        applyScopeRef.value = "";
      }
    }
  } catch {
    if (seq === enrollmentLoadSeq) enrollmentChildren.value = [];
  } finally {
    if (seq === enrollmentLoadSeq) enrollmentChildrenLoading.value = false;
  }
}

async function loadCollectionChildren(collectionRef: string) {
  const seq = ++collectionLoadSeq;
  collectionChildren.value = [];
  selectedObservationRef.value = "";
  if (!collectionRef || typeof kernelApi.getChildren !== "function") return;

  collectionChildrenLoading.value = true;
  try {
    const children = await kernelApi.getChildren(collectionRef);
    if (seq !== collectionLoadSeq) return;
    collectionChildren.value = children ?? [];
    seedVisibleChildren(collectionChildren.value);
    if (needsObservationPicker.value) {
      const firstObs = observationOptions.value.find((o) => !o.disabled);
      selectedObservationRef.value = firstObs?.value ?? "";
      applyScopeRef.value = selectedObservationRef.value;
    }
  } catch {
    if (seq === collectionLoadSeq) collectionChildren.value = [];
  } finally {
    if (seq === collectionLoadSeq) collectionChildrenLoading.value = false;
  }
}

async function onSitePickerChange() {
  if (applyScopeKind.value === "study_site") {
    applyScopeRef.value = selectedSiteRef.value;
    return;
  }
  applyScopeRef.value = "";
  await loadSiteChildren(selectedSiteRef.value);
}

async function onEnrollmentPickerChange() {
  if (applyScopeKind.value === "enrollment") {
    applyScopeRef.value = selectedEnrollmentRef.value;
    return;
  }
  applyScopeRef.value = "";
  await loadEnrollmentChildren(selectedEnrollmentRef.value);
}

async function onCollectionPickerChange() {
  if (applyScopeKind.value === "collection_instance") {
    applyScopeRef.value = selectedCollectionRef.value;
    return;
  }
  applyScopeRef.value = "";
  await loadCollectionChildren(selectedCollectionRef.value);
}

function onObservationPickerChange() {
  if (applyScopeKind.value === "observation") {
    applyScopeRef.value = selectedObservationRef.value;
  }
}

async function onScopeKindChange() {
  if (applyScopeKind.value === "study") {
    applyScopeRef.value = studyRef.value ?? "";
    return;
  }

  if (!selectedSiteRef.value) {
    const firstSite = studySiteOptions.value.find((o) => !o.disabled);
    selectedSiteRef.value = firstSite?.value ?? "";
  }

  if (applyScopeKind.value === "study_site") {
    applyScopeRef.value = selectedSiteRef.value;
    return;
  }

  if (!selectedSiteRef.value) {
    applyScopeRef.value = "";
    return;
  }

  if (siteChildren.value.length === 0) {
    await loadSiteChildren(selectedSiteRef.value);
    return;
  }

  if (!selectedEnrollmentRef.value) {
    const firstEnrollment = enrollmentOptions.value.find((o) => !o.disabled);
    selectedEnrollmentRef.value = firstEnrollment?.value ?? "";
  }

  if (applyScopeKind.value === "enrollment") {
    applyScopeRef.value = selectedEnrollmentRef.value;
    return;
  }

  if (!selectedEnrollmentRef.value) {
    applyScopeRef.value = "";
    return;
  }

  if (enrollmentChildren.value.length === 0) {
    await loadEnrollmentChildren(selectedEnrollmentRef.value);
    return;
  }

  if (!selectedCollectionRef.value) {
    const firstCollection = collectionOptions.value.find((o) => !o.disabled);
    selectedCollectionRef.value = firstCollection?.value ?? "";
  }

  if (applyScopeKind.value === "collection_instance") {
    applyScopeRef.value = selectedCollectionRef.value;
    return;
  }

  if (!selectedCollectionRef.value) {
    applyScopeRef.value = "";
    return;
  }

  if (collectionChildren.value.length === 0) {
    await loadCollectionChildren(selectedCollectionRef.value);
    return;
  }

  if (!selectedObservationRef.value) {
    const firstObs = observationOptions.value.find((o) => !o.disabled);
    selectedObservationRef.value = firstObs?.value ?? "";
  }
  applyScopeRef.value = selectedObservationRef.value;
}

let loadSeq = 0;

async function reloadLocks() {
  if (!studyRef.value || typeof kernelApi.listStudyLocks !== "function") return;
  locksLoading.value = true;
  locksError.value = null;
  try {
    const res = await kernelApi.listStudyLocks(studyRef.value, {
      activeOnly: activeOnly.value || undefined,
      asOf: asOfInput.value.trim() || undefined,
    });
    locks.value = res.locks ?? [];
  } catch (err) {
    locksError.value = err instanceof Error ? err.message : String(err);
  } finally {
    locksLoading.value = false;
  }
}

async function loadStudyLocksContext(code: string) {
  const seq = ++loadSeq;
  studyRef.value = null;
  studyError.value = null;
  locks.value = [];
  locksError.value = null;
  outcome.value = null;
  studyChildren.value = [];
  siteChildren.value = [];
  enrollmentChildren.value = [];
  collectionChildren.value = [];
  selectedSiteRef.value = "";
  selectedEnrollmentRef.value = "";
  selectedCollectionRef.value = "";
  selectedObservationRef.value = "";
  if (!code) return;

  studyLoading.value = true;
  try {
    const resolved = await findKernelStudyRef(code);
    if (seq !== loadSeq) return;
    studyRef.value = resolved;
    if (!resolved) return;
    seedNodeSummary(resolved, { kind: "study", code });

    applyScopeKind.value = "study";
    applyScopeRef.value = resolved;

    const tasks: Promise<unknown>[] = [reloadLocks()];
    if (typeof kernelApi.canI === "function") {
      tasks.push(
        Promise.all([
          kernelApi.canI("lock.apply", resolved),
          kernelApi.canI("lock.release", resolved),
        ]).then(([applyDec, releaseDec]) => {
          if (seq !== loadSeq) return;
          canApply.value = Boolean(applyDec?.permitted);
          canRelease.value = Boolean(releaseDec?.permitted);
        })
      );
    }
    if (typeof kernelApi.getChildren === "function") {
      tasks.push(
        kernelApi
          .getChildren(resolved)
          .then((children) => {
            if (seq !== loadSeq) return;
            studyChildren.value = children ?? [];
            seedVisibleChildren(studyChildren.value);
          })
          .catch(() => undefined)
      );
    }
    await Promise.all(tasks);
  } catch (err) {
    if (seq !== loadSeq) return;
    studyError.value = err instanceof Error ? err.message : String(err);
  } finally {
    if (seq === loadSeq) studyLoading.value = false;
  }
}

watch(
  () => props.studyCode,
  (code) => {
    void loadStudyLocksContext(code);
  },
  { immediate: true }
);

function openApplyModal() {
  if (!applyScopeRef.value.trim() || !applyReason.value.trim()) return;
  outcome.value = null;
  modalOp.value = {
    mode: "apply",
    idempotencyKey: globalThis.crypto.randomUUID(),
    lockId: globalThis.crypto.randomUUID(),
    attestationId: globalThis.crypto.randomUUID(),
  };
}

function openReleaseModal(lockRef: string, lockKind: string) {
  if (!releaseReason.value.trim()) return;
  outcome.value = null;
  modalOp.value = {
    mode: "release",
    lockRef,
    lockKind,
    idempotencyKey: globalThis.crypto.randomUUID(),
    assertionId: globalThis.crypto.randomUUID(),
    attestationId: globalThis.crypto.randomUUID(),
  };
}

async function executeModalSign(sigToken: string, signingReason: string) {
  const op = modalOp.value;
  if (!op || !studyRef.value) return { kind: "signed" as const };
  const meaning: KernelLockMeaning =
    signingReason === "Responsibility" ? "Responsibility" : "Approved";
  busy.value = true;
  try {
    if (op.mode === "apply") {
      await kernelApi.applyLock({
        studyRef: studyRef.value,
        scopeRef: applyScopeRef.value.trim(),
        kind: applyKind.value,
        meaning,
        lockId: op.lockId,
        attestationId: op.attestationId,
        sigToken,
        reason: applyReason.value.trim(),
        idempotencyKey: op.idempotencyKey,
      });
      const scopeDisplay = getNodeDisplay(applyScopeRef.value.trim());
      outcome.value = {
        kind: "ok",
        message: `${applyKind.value} applied to ${scopeDisplay.label}.`,
      };
      applyReason.value = "";
    } else {
      await kernelApi.releaseLock({
        lockRef: op.lockRef,
        studyRef: studyRef.value,
        meaning,
        assertionId: op.assertionId,
        attestationId: op.attestationId,
        sigToken,
        reason: releaseReason.value.trim(),
        idempotencyKey: op.idempotencyKey,
      });
      outcome.value = {
        kind: "ok",
        message: `${op.lockKind} released.`,
      };
      releaseReason.value = "";
    }
    await reloadLocks();
    return { kind: "signed" as const };
  } catch (err) {
    const message = describeLockError(err);
    outcome.value = { kind: "error", message };
    throw new Error(message, { cause: err });
  } finally {
    busy.value = false;
  }
}
</script>

<style scoped>
.study-locks {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.study-locks-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-end;
  gap: 12px;
}

.study-locks-header h2 {
  margin: 0;
  font-size: 1.2rem;
}

.study-locks-subtitle {
  margin: 4px 0 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.study-locks-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
}

.checkbox-label {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.apply-card {
  padding: 14px 16px;
  border: 1px solid var(--semantic-color-border-muted);
  border-radius: 8px;
  background: var(--semantic-color-surface-muted);
}

.apply-card h3 {
  margin: 0 0 10px;
  font-size: 0.95rem;
}

.apply-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 10px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-group--wide {
  grid-column: 1 / -1;
}

.field-group label,
.release-reason-bar label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.field-group input,
.field-group select,
.release-reason-bar input,
.study-locks-filters input[type="text"] {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
}

.apply-actions {
  margin-top: 10px;
}

.release-reason-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.release-reason-bar input {
  flex: 1 1 260px;
}

.locks-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  font-variant-numeric: tabular-nums;
}

.locks-table th,
.locks-table td {
  padding: 8px 10px;
  border-bottom: 1px solid var(--semantic-color-border);
  text-align: left;
}

.locks-table th {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  background: var(--semantic-color-surface-muted);
}

.lock-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
}

.lock-badge--frozen {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
}

.lock-badge--locked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}

.masked-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.8rem;
}

.meaning-note {
  color: var(--semantic-color-text-muted);
  font-size: 0.8rem;
}

.status-line {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
}

.ok-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.error-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}
</style>
