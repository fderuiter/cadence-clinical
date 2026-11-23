<template>
  <section
    id="protocol-versions-section"
    class="versions"
    aria-labelledby="protocol-versions-title"
    :aria-busy="loading"
  >
    <h2 id="protocol-versions-title">Protocol versions</h2>
    <div
      v-if="error"
      id="protocol-versions-status"
      class="status-line"
      role="alert"
    >
      <p class="status-line">Protocol versions couldn't be loaded right now.</p>
      <SupportDetails :detail="error" />
    </div>
    <p v-else id="protocol-versions-status" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <div v-if="versions.length" class="scrollable-table-wrapper">
      <table class="versions-table">
        <thead>
          <tr>
            <th scope="col" class="numeric">Version</th>
            <th scope="col">Status</th>
            <th scope="col">Amends</th>
            <th scope="col">Amendment intent</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="(version, index) in versions"
            :key="version.ref ?? `masked-${index}`"
            :class="{
              selected: version.ref !== null && version.ref === selectedRef,
            }"
          >
            <template v-if="version.ref !== null">
              <th scope="row" class="numeric">
                <button
                  :id="`protocol-version-${version.number}`"
                  type="button"
                  class="row-select"
                  :aria-pressed="version.ref === selectedRef"
                  @click="emit('select', version)"
                >
                  {{ version.number }}
                </button>
              </th>
              <td>{{ version.status }}</td>
              <td>{{ parentNumber(version) }}</td>
              <td>{{ version.amendment_intent || "None recorded" }}</td>
            </template>
            <td v-else colspan="4" class="masked-row">
              A version you may not see ({{ maskWords(version.mask) }})
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <ProtocolImportPanel
      v-if="canShowImportPanel"
      :study-ref="studyNodeRef"
      :study-code="studyCode"
      :selected-version-ref="activeSummaryVersionRef"
      @imported="onDraftImported"
    />

    <section
      v-if="canManageProtocol && selectedVersion && selectedVersion.ref"
      id="protocol-version-governance"
      class="governance-card"
      aria-labelledby="protocol-governance-heading"
    >
      <div class="governance-header">
        <div>
          <h3 id="protocol-governance-heading">
            Version {{ selectedVersion.number }} governance &amp; CDISC mapping
          </h3>
          <p class="governance-subtitle">
            Status:
            <strong data-testid="selected-protocol-status">{{
              selectedVersion.status
            }}</strong>
            · Every collected concept must be mapped to a pinned CDISC standard
            before submitting a draft for review (K-STD-1).
          </p>
        </div>
      </div>

      <div
        v-if="governanceMessage"
        id="protocol-governance-outcome"
        data-testid="protocol-governance-outcome"
        class="banner banner-success"
        role="status"
      >
        {{ governanceMessage }}
      </div>

      <div
        v-if="governanceError"
        id="protocol-governance-error"
        data-testid="protocol-governance-error"
        class="banner banner-error"
        role="alert"
      >
        {{ governanceError }}
      </div>

      <div class="field-row">
        <label for="protocol-governance-reason" class="field-label">
          Reason for governance action
        </label>
        <input
          id="protocol-governance-reason"
          v-model="governanceReason"
          type="text"
          class="text-input"
          autocomplete="off"
          placeholder="e.g. Submit mapped protocol draft for clinical review"
          :disabled="governanceBusy"
        />
      </div>

      <div
        v-if="selectedVersion.status === 'Draft'"
        id="protocol-mapping-box"
        class="mapping-box"
      >
        <div class="mapping-summary">
          <strong>SDTMIG 3.4 concept mapping</strong>
          <span>
            {{ collectedConcepts.length }}
            {{ collectedConcepts.length === 1 ? "concept" : "concepts" }} in
            draft
            <template v-if="sdtmigStandard">
              · SDTMIG {{ sdtmigStandard.version }} ({{
                sdtmigStandard.status
              }})
            </template>
            <template v-if="pinnedMappingCount > 0">
              · {{ pinnedMappingCount }} mapping specification pinned
            </template>
          </span>
        </div>

        <div class="governance-actions">
          <button
            v-if="!sdtmigStandard"
            id="btn-seed-sdtmig"
            type="button"
            class="btn btn-secondary"
            :disabled="governanceBusy || !governanceReason.trim()"
            @click="seedSdtmigStandard"
          >
            Register bundled SDTMIG 3.4
          </button>
          <button
            v-else-if="sdtmigStandard.status === 'Draft'"
            id="btn-approve-sdtmig"
            type="button"
            class="btn btn-primary"
            :disabled="governanceBusy"
            @click="openSignatureModal('approve-standard')"
          >
            Approve SDTMIG 3.4
          </button>
          <button
            v-if="
              sdtmigStandard?.status === 'Approved' &&
              collectedConcepts.length > 0
            "
            id="btn-draft-mapping-spec"
            type="button"
            class="btn btn-secondary"
            :disabled="governanceBusy || !governanceReason.trim()"
            @click="draftSdtmMappingSpecification"
          >
            Draft SDTM concept mapping
          </button>
        </div>

        <ul
          v-if="mappingSpecs.length > 0"
          class="mapping-spec-list"
          data-testid="mapping-spec-list"
        >
          <li
            v-for="spec in mappingSpecs"
            :key="spec.ref"
            class="mapping-spec-row"
          >
            <span>
              <code>{{ spec.key }}</code> · v{{ spec.version }} ·
              <strong>{{ spec.status }}</strong>
              <span v-if="spec.pinned" class="pinned-tag">Pinned on study</span>
            </span>
            <span class="governance-actions">
              <button
                v-if="spec.status === 'Draft'"
                :id="`btn-approve-mapping-${spec.key}-${spec.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="governanceBusy"
                @click="beginApproveMapping(spec)"
              >
                Approve mapping
              </button>
              <button
                v-if="spec.status === 'Approved' && !spec.pinned"
                :id="`btn-pin-mapping-${spec.key}-${spec.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="governanceBusy || !governanceReason.trim()"
                @click="pinMappingOnStudy(spec)"
              >
                Pin mapping &amp; SDTMIG 3.4
              </button>
            </span>
          </li>
        </ul>
      </div>

      <div class="governance-actions">
        <button
          v-if="selectedVersion.status === 'Draft'"
          id="btn-submit-protocol-version"
          type="button"
          class="btn btn-primary"
          :disabled="governanceBusy || !governanceReason.trim()"
          @click="submitSelectedForReview"
        >
          Submit version for review
        </button>

        <template v-else-if="selectedVersion.status === 'InReview'">
          <button
            id="btn-approve-protocol-version"
            type="button"
            class="btn btn-primary"
            :disabled="governanceBusy"
            @click="openSignatureModal('approve-protocol')"
          >
            Approve protocol version
          </button>
          <button
            id="btn-return-protocol-draft"
            type="button"
            class="btn btn-secondary"
            :disabled="governanceBusy || !governanceReason.trim()"
            @click="returnSelectedToDraft"
          >
            Return to draft
          </button>
          <button
            id="btn-withdraw-protocol-version"
            type="button"
            class="btn btn-secondary"
            :disabled="governanceBusy"
            @click="openSignatureModal('withdraw-protocol')"
          >
            Withdraw version
          </button>
        </template>

        <template v-else-if="selectedVersion.status === 'Approved'">
          <button
            id="btn-toggle-amendment-form"
            type="button"
            class="btn btn-primary"
            :disabled="governanceBusy"
            @click="showAmendmentForm = !showAmendmentForm"
          >
            {{
              showAmendmentForm
                ? "Cancel amendment draft"
                : "Create amendment draft"
            }}
          </button>
          <button
            id="btn-withdraw-protocol-version"
            type="button"
            class="btn btn-secondary"
            :disabled="governanceBusy"
            @click="openSignatureModal('withdraw-protocol')"
          >
            Withdraw version
          </button>
        </template>
      </div>

      <form
        v-if="selectedVersion.status === 'Approved' && showAmendmentForm"
        id="protocol-amendment-form"
        class="amendment-form"
        @submit.prevent="forkSelectedAmendment"
      >
        <div class="field-row">
          <label for="amendment-intent" class="field-label">
            Amendment intent
          </label>
          <input
            id="amendment-intent"
            v-model="amendmentIntent"
            type="text"
            class="text-input"
            placeholder="e.g. Add follow-up blood pressure safety check"
            :disabled="governanceBusy"
          />
        </div>
        <div class="field-row">
          <label for="amendment-rationale" class="field-label">
            Scientific / operational rationale
          </label>
          <input
            id="amendment-rationale"
            v-model="amendmentRationale"
            type="text"
            class="text-input"
            placeholder="e.g. DSMB recommendation following interim safety review"
            :disabled="governanceBusy"
          />
        </div>
        <div class="field-row">
          <label for="amendment-summary" class="field-label">
            Summary of changes
          </label>
          <input
            id="amendment-summary"
            v-model="amendmentSummary"
            type="text"
            class="text-input"
            placeholder="e.g. Added standing BP assessment at Visit 2"
            :disabled="governanceBusy"
          />
        </div>
        <div class="field-row">
          <label for="amendment-elements" class="field-label">
            Impacted element IDs (comma-separated)
          </label>
          <input
            id="amendment-elements"
            v-model="amendmentElements"
            type="text"
            class="text-input"
            placeholder="e.g. VS"
            :disabled="governanceBusy"
          />
        </div>
        <div class="field-grid">
          <div class="field-row">
            <label for="amendment-classification" class="field-label">
              Classification
            </label>
            <select
              id="amendment-classification"
              v-model="amendmentClassification"
              class="text-input"
              :disabled="governanceBusy"
            >
              <option value="Substantial">Substantial</option>
              <option value="NonSubstantial">NonSubstantial</option>
              <option value="UrgentSafetyMeasure">UrgentSafetyMeasure</option>
            </select>
          </div>
          <div class="field-row">
            <label for="amendment-transition-policy" class="field-label">
              Participant transition policy
            </label>
            <select
              id="amendment-transition-policy"
              v-model="amendmentTransitionPolicy"
              class="text-input"
              :disabled="governanceBusy"
            >
              <option value="NewParticipantsOnly">NewParticipantsOnly</option>
              <option value="RequiresReconsent">RequiresReconsent</option>
              <option value="Automatic">Automatic</option>
              <option value="ByCohort">ByCohort</option>
            </select>
          </div>
        </div>
        <button
          id="btn-confirm-fork-protocol"
          type="submit"
          class="btn btn-primary"
          :disabled="!canSubmitAmendment || governanceBusy"
        >
          Fork amendment draft
        </button>
      </form>
    </section>

    <SignatureCaptureModal
      v-if="signatureMode"
      :is-open="Boolean(signatureMode)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Sponsor Designer"
      :reasons="signatureReasons"
      :action-url="signatureActionUrl"
      :semantic-action="signatureSemanticAction"
      :on-sign="completeSignedGovernanceAction"
      @cancel="closeSignatureModal"
      @success="closeSignatureModal"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import {
  describeKernelError,
  isMasked,
  kernelApi,
  PROTOCOL_VERSION_APPROVE_SEMANTIC_ACTION,
  PROTOCOL_VERSION_WITHDRAW_SEMANTIC_ACTION,
  protocolVersionApproveAction,
  protocolVersionWithdrawAction,
  RULE_PACK_APPROVE_SEMANTIC_ACTION,
  rulePackApproveAction,
  STANDARD_APPROVE_SEMANTIC_ACTION,
  standardApproveAction,
  type KernelLibraryItem,
  type KernelProtocolDraftSummary,
  type KernelProtocolFormItem,
  type KernelProtocolVersion,
  type KernelStandardVersion,
} from "../../api/kernelApi";
import { canManageRulePacks } from "../../canvas/authority";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";
import { maskReasonWords, seedNodeSummary } from "../../canvas/nodeResolver";
import { useAuthStore } from "../../stores/auth";
import { demoRandomToken, isDemoBuild } from "../../utils/demoMode.js";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import ProtocolImportPanel from "./ProtocolImportPanel.vue";
import SupportDetails from "./SupportDetails.vue";

/**
 * The study's protocol versions, newest first (ADR-2540). Restricted versions keep their place as masked placeholders.
 */
const props = defineProps<{ studyCode: string; selectedRef: string }>();
const emit = defineEmits<{ select: [version: KernelProtocolVersion] }>();

const authStore = useAuthStore();
const versions = ref<KernelProtocolVersion[]>([]);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);
const studyNodeRef = ref("");

const selectedDesignSummary = ref<KernelProtocolDraftSummary | null>(null);
const sdtmigStandard = ref<KernelStandardVersion | null>(null);
const mappingSpecs = ref<KernelLibraryItem[]>([]);
const existingPinnedRulePackRefs = ref<string[]>([]);

const governanceBusy = ref(false);
const governanceReason = ref("Protocol study-build governance update");
const governanceMessage = ref("");
const governanceError = ref("");

const showAmendmentForm = ref(false);
const amendmentIntent = ref("");
const amendmentRationale = ref("");
const amendmentSummary = ref("");
const amendmentElements = ref("");
const amendmentClassification = ref("Substantial");
const amendmentTransitionPolicy = ref("NewParticipantsOnly");

type SignatureMode =
  | "approve-protocol"
  | "withdraw-protocol"
  | "approve-standard"
  | "approve-mapping"
  | null;
const signatureMode = ref<SignatureMode>(null);
const signingMappingSpec = ref<KernelLibraryItem | null>(null);

const canManageProtocol = computed(() => canManageRulePacks(authStore));

const signerUsername = computed(() => authStore.identity?.username ?? "");
const signerFullName = computed(() => {
  const identity = authStore.identity;
  if (!identity) return "";
  const full = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return full || identity.username || "";
});

const canShowImportPanel = computed(
  () =>
    !loading.value &&
    !error.value &&
    !noStudy.value &&
    Boolean(studyNodeRef.value) &&
    typeof kernelApi.importProtocolDraft === "function"
);

const activeSummaryVersionRef = computed(() => {
  if (props.selectedRef) return props.selectedRef;
  const firstVisible = versions.value.find((v) => v.ref && !isMasked(v));
  return firstVisible?.ref ?? "";
});

const selectedVersion = computed(() => {
  const targetRef = activeSummaryVersionRef.value;
  if (!targetRef) return null;
  const targetId = parseNodeRef(targetRef)?.id;
  return (
    versions.value.find(
      (v) =>
        v.ref === targetRef ||
        (targetId && parseNodeRef(v.ref)?.id === targetId)
    ) ?? null
  );
});

interface CollectedConceptItem {
  formId: string;
  item: KernelProtocolFormItem;
  conceptId: string;
  conceptVersion: number;
}

function parseConceptReference(raw: string | null | undefined) {
  if (!raw) return null;
  const trimmed = raw.trim();
  return parseNodeRef(trimmed) ?? parseNodeRef(`concept:${trimmed}`);
}

const collectedConcepts = computed<CollectedConceptItem[]>(() => {
  const summary = selectedDesignSummary.value;
  if (!summary?.forms) return [];
  const seen = new Set<string>();
  const items: CollectedConceptItem[] = [];
  for (const form of summary.forms) {
    for (const item of form.items) {
      const parsed = parseConceptReference(item.concept);
      if (!parsed || seen.has(parsed.id)) continue;
      seen.add(parsed.id);
      items.push({
        formId: form.id,
        item,
        conceptId: parsed.id,
        conceptVersion: parsed.version ?? 1,
      });
    }
  }
  return items;
});

const pinnedMappingCount = computed(
  () => mappingSpecs.value.filter((spec) => spec.pinned).length
);

const canSubmitAmendment = computed(
  () =>
    Boolean(governanceReason.value.trim()) &&
    Boolean(amendmentIntent.value.trim()) &&
    Boolean(amendmentRationale.value.trim()) &&
    Boolean(amendmentSummary.value.trim()) &&
    Boolean(amendmentElements.value.trim())
);

const signatureReasons = computed(() => {
  if (signatureMode.value === "withdraw-protocol") {
    return ["Protocol version withdrawal", "SPONSOR_APPROVAL", "APPROVAL"];
  }
  return ["APPROVAL", "SPONSOR_APPROVAL", "CLINICAL_QC"];
});

const signatureActionUrl = computed(() => {
  if (
    signatureMode.value === "approve-protocol" &&
    selectedVersion.value?.ref
  ) {
    return protocolVersionApproveAction(selectedVersion.value.ref);
  }
  if (
    signatureMode.value === "withdraw-protocol" &&
    selectedVersion.value?.ref
  ) {
    return protocolVersionWithdrawAction(selectedVersion.value.ref);
  }
  if (signatureMode.value === "approve-standard" && sdtmigStandard.value?.ref) {
    return standardApproveAction(sdtmigStandard.value.ref);
  }
  if (
    signatureMode.value === "approve-mapping" &&
    signingMappingSpec.value?.ref
  ) {
    return rulePackApproveAction(signingMappingSpec.value.ref);
  }
  return "";
});

const signatureSemanticAction = computed(() => {
  if (signatureMode.value === "approve-protocol") {
    return PROTOCOL_VERSION_APPROVE_SEMANTIC_ACTION;
  }
  if (signatureMode.value === "withdraw-protocol") {
    return PROTOCOL_VERSION_WITHDRAW_SEMANTIC_ACTION;
  }
  if (signatureMode.value === "approve-standard") {
    return STANDARD_APPROVE_SEMANTIC_ACTION;
  }
  return RULE_PACK_APPROVE_SEMANTIC_ACTION;
});

function makeIdempotencyKey(): string {
  if (isDemoBuild()) {
    return `protocol-gov-${demoRandomToken(12, 36, "protocol-gov.idempotency")}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `protocol-gov-${demoRandomToken(12, 36, "protocol-gov.idempotency")}`;
}

/** Visible versions by id: a parent reference may name the version with or without its revision. */
const byId = computed(
  () =>
    new Map(
      versions.value.flatMap((v) => {
        const node = parseNodeRef(v.ref);
        return node ? [[node.id, v] as const] : [];
      })
    )
);

function parentNumber(version: KernelProtocolVersion): string {
  if (!version.parent_version) return "Nothing (first version)";
  const parentId = parseNodeRef(version.parent_version)?.id;
  const parent = parentId ? byId.value.get(parentId) : undefined;
  return parent ? `Version ${parent.number}` : "A version you may not see";
}

function maskWords(mask: string | null): string {
  return maskReasonWords(mask);
}

const statusLine = computed(() => {
  if (loading.value) return "Loading protocol versions...";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  const count = versions.value.length;
  if (count === 0) return `${props.studyCode} has no protocol versions yet.`;
  const hidden = versions.value.filter((v) => isMasked(v)).length;
  const noun = count === 1 ? "version" : "versions";
  return hidden
    ? `${count} ${noun}, ${hidden} of them hidden from you.`
    : `${count} ${noun}.`;
});

let latest = 0;

async function refreshGovernanceContext() {
  if (!canManageProtocol.value || !studyNodeRef.value) return;
  try {
    if (
      activeSummaryVersionRef.value &&
      typeof kernelApi.getProtocolDesignSummary === "function"
    ) {
      selectedDesignSummary.value = await kernelApi.getProtocolDesignSummary(
        activeSummaryVersionRef.value
      );
    } else {
      selectedDesignSummary.value = null;
    }
  } catch {
    selectedDesignSummary.value = null;
  }

  try {
    if (typeof kernelApi.listStandards === "function") {
      const catalog = await kernelApi.listStandards("SDTMIG");
      sdtmigStandard.value =
        catalog.standards.find((s) => s.version === "3.4") ??
        catalog.standards[0] ??
        null;
    }
    if (typeof kernelApi.listLibraryItems === "function") {
      mappingSpecs.value = await kernelApi.listLibraryItems({
        kind: "MappingSpecification",
        studyRef: studyNodeRef.value,
      });
    }
    if (typeof kernelApi.listStudyRulePacks === "function") {
      const rulePacks = await kernelApi.listStudyRulePacks(studyNodeRef.value);
      existingPinnedRulePackRefs.value = rulePacks.pinned_rule_pack_refs ?? [];
    }
  } catch {
    // Keep protocol table usable even if standards endpoints are mocked or unavailable.
  }
}

async function refreshVersionsAndSelect(preferred?: KernelProtocolVersion) {
  if (!studyNodeRef.value) return;
  const listed = await kernelApi.listProtocolVersions(studyNodeRef.value);
  versions.value = listed;
  for (const v of listed) {
    if (v.ref) {
      seedNodeSummary(v.ref, {
        kind: "protocol_version",
        title: `Version ${v.number}`,
        status: v.status,
      });
    }
  }
  if (preferred?.ref) {
    emit("select", preferred);
  }
  await refreshGovernanceContext();
}

async function load(code: string) {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  noStudy.value = false;
  studyNodeRef.value = "";
  versions.value = [];
  governanceMessage.value = "";
  governanceError.value = "";
  let shouldRefreshGovernance = false;
  try {
    const study = await findKernelStudyRef(code);
    if (request !== latest) return;
    if (!study) {
      noStudy.value = true;
      return;
    }
    studyNodeRef.value = study;
    const listed = await kernelApi.listProtocolVersions(study);
    if (request === latest) {
      versions.value = listed;
      for (const v of listed) {
        if (v.ref) {
          seedNodeSummary(v.ref, {
            kind: "protocol_version",
            title: `Version ${v.number}`,
            status: v.status,
          });
        }
      }
      shouldRefreshGovernance = true;
    }
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
  if (shouldRefreshGovernance && request === latest) {
    void refreshGovernanceContext();
  }
}

async function onDraftImported(summary: KernelProtocolDraftSummary) {
  selectedDesignSummary.value = summary;
  if (studyNodeRef.value) {
    try {
      await refreshVersionsAndSelect(summary.version);
      return;
    } catch {
      // Keep the imported version visible even if refreshing the list fails.
    }
  }
  if (summary.version?.ref) {
    seedNodeSummary(summary.version.ref, {
      kind: "protocol_version",
      title: `Version ${summary.version.number}`,
      status: summary.version.status,
    });
    emit("select", summary.version);
  }
}

function buildSdtmigMappingContent() {
  const unitsMap = new Map<string, string>();
  const mappings = collectedConcepts.value.map((entry, idx) => {
    const rawCode = entry.item.id
      .replace(/[^A-Za-z0-9]/g, "")
      .toUpperCase()
      .slice(-8);
    const testCode = rawCode || `ITEM${idx + 1}`;
    const isLab = entry.formId.toUpperCase().startsWith("LB");
    const domain = isLab ? "LB" : "VS";
    const prefix = isLab ? "LB" : "VS";
    const unit = entry.item.unit?.trim() || null;
    if (unit) {
      unitsMap.set(unit, unit.replace(/[[\]]/g, "") || unit);
    }
    return {
      id: `map-${idx + 1}-${testCode.toLowerCase()}`,
      concept: {
        id: entry.conceptId,
        version: entry.conceptVersion,
      },
      dataset: domain,
      assign: {
        [`${prefix}TESTCD`]: testCode,
      },
      result: unit
        ? {
            original: `${prefix}ORRES`,
            originalUnit: `${prefix}ORRESU`,
            standardCharacter: `${prefix}STRESC`,
            standardNumeric: `${prefix}STRESN`,
            standardUnit: `${prefix}STRESU`,
            status: `${prefix}STAT`,
            reasonNotDone: `${prefix}REASND`,
          }
        : {
            original: `${prefix}ORRES`,
            standardCharacter: `${prefix}STRESC`,
            status: `${prefix}STAT`,
            reasonNotDone: `${prefix}REASND`,
          },
      ...(unit ? { standardUnit: unit } : {}),
    };
  });

  return {
    title: `${props.studyCode} SDTMIG 3.4 Concept Mapping`,
    controlledTerminology: [],
    units: [...unitsMap.entries()].map(([ucum, submissionValue]) => ({
      ucum,
      submissionValue,
    })),
    mappings,
  };
}

async function seedSdtmigStandard() {
  if (!governanceReason.value.trim() || governanceBusy.value) return;
  governanceBusy.value = true;
  governanceError.value = "";
  governanceMessage.value = "";
  try {
    const created = await kernelApi.registerStandardVersion({
      seedFile: "sdtmig-3-4.json",
      reason: governanceReason.value.trim(),
      idempotencyKey: makeIdempotencyKey(),
    });
    sdtmigStandard.value = created;
    governanceMessage.value = `Registered ${created.standard_code} ${created.version} (${created.status}).`;
  } catch (err) {
    governanceError.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function draftSdtmMappingSpecification() {
  if (
    !sdtmigStandard.value?.ref ||
    collectedConcepts.value.length === 0 ||
    !governanceReason.value.trim() ||
    governanceBusy.value
  ) {
    return;
  }
  governanceBusy.value = true;
  governanceError.value = "";
  governanceMessage.value = "";
  try {
    const key = `${props.studyCode
      .toLowerCase()
      .replace(/[^a-z0-9-]+/g, "-")}-sdtm-mapping`;
    const drafted = await kernelApi.draftLibraryItem({
      kind: "MappingSpecification",
      key,
      title: `${props.studyCode} SDTMIG 3.4 Mapping Specification`,
      content: buildSdtmigMappingContent(),
      standardVersionRefs: [sdtmigStandard.value.ref],
      reason: governanceReason.value.trim(),
      idempotencyKey: makeIdempotencyKey(),
    });
    await refreshGovernanceContext();
    governanceMessage.value = `Drafted mapping specification ${drafted.key} v${drafted.version}.`;
  } catch (err) {
    governanceError.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

function openSignatureModal(mode: SignatureMode) {
  governanceError.value = "";
  signatureMode.value = mode;
}

function beginApproveMapping(spec: KernelLibraryItem) {
  signingMappingSpec.value = spec;
  openSignatureModal("approve-mapping");
}

function closeSignatureModal() {
  signatureMode.value = null;
  signingMappingSpec.value = null;
}

async function pinMappingOnStudy(spec: KernelLibraryItem) {
  if (
    !studyNodeRef.value ||
    !sdtmigStandard.value?.ref ||
    !governanceReason.value.trim() ||
    governanceBusy.value
  ) {
    return;
  }
  governanceBusy.value = true;
  governanceError.value = "";
  governanceMessage.value = "";
  try {
    await kernelApi.declareStudyStandards({
      studyRef: studyNodeRef.value,
      standardVersionRefs: [sdtmigStandard.value.ref],
      mappingSpecRefs: [spec.ref],
      rulePackRefs: existingPinnedRulePackRefs.value,
      reason: governanceReason.value.trim(),
      idempotencyKey: makeIdempotencyKey(),
    });
    await refreshGovernanceContext();
    governanceMessage.value = `Pinned ${spec.key} v${spec.version} and SDTMIG 3.4 on ${props.studyCode}.`;
  } catch (err) {
    governanceError.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function submitSelectedForReview() {
  if (
    !selectedVersion.value?.ref ||
    !governanceReason.value.trim() ||
    governanceBusy.value
  ) {
    return;
  }
  governanceBusy.value = true;
  governanceError.value = "";
  governanceMessage.value = "";
  try {
    const updated = await kernelApi.submitProtocolVersion({
      versionRef: selectedVersion.value.ref,
      reason: governanceReason.value.trim(),
      idempotencyKey: makeIdempotencyKey(),
    });
    await refreshVersionsAndSelect(updated);
    governanceMessage.value = `Submitted Version ${updated.number} for review.`;
  } catch (err) {
    governanceError.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function returnSelectedToDraft() {
  if (
    !selectedVersion.value?.ref ||
    !governanceReason.value.trim() ||
    governanceBusy.value
  ) {
    return;
  }
  governanceBusy.value = true;
  governanceError.value = "";
  governanceMessage.value = "";
  try {
    const updated = await kernelApi.returnProtocolVersionToDraft({
      versionRef: selectedVersion.value.ref,
      reason: governanceReason.value.trim(),
      idempotencyKey: makeIdempotencyKey(),
    });
    await refreshVersionsAndSelect(updated);
    governanceMessage.value = `Returned Version ${updated.number} to Draft.`;
  } catch (err) {
    governanceError.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function forkSelectedAmendment() {
  if (
    !selectedVersion.value?.ref ||
    !canSubmitAmendment.value ||
    governanceBusy.value
  ) {
    return;
  }
  governanceBusy.value = true;
  governanceError.value = "";
  governanceMessage.value = "";
  try {
    const elements = amendmentElements.value
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    const created = await kernelApi.forkProtocolVersion({
      versionRef: selectedVersion.value.ref,
      intent: amendmentIntent.value.trim(),
      rationale: amendmentRationale.value.trim(),
      summaryOfChanges: amendmentSummary.value.trim(),
      impactedElements: elements,
      classification: amendmentClassification.value,
      transitionPolicy: amendmentTransitionPolicy.value,
      reason: governanceReason.value.trim(),
      idempotencyKey: makeIdempotencyKey(),
    });
    showAmendmentForm.value = false;
    amendmentIntent.value = "";
    amendmentRationale.value = "";
    amendmentSummary.value = "";
    amendmentElements.value = "";
    await refreshVersionsAndSelect(created);
    governanceMessage.value = `Created amendment Draft Version ${created.number}.`;
  } catch (err) {
    governanceError.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function completeSignedGovernanceAction(
  sigToken: string,
  reason: string
) {
  const effectiveReason = governanceReason.value.trim() || reason;
  governanceBusy.value = true;
  governanceError.value = "";
  governanceMessage.value = "";
  try {
    if (
      signatureMode.value === "approve-protocol" &&
      selectedVersion.value?.ref
    ) {
      const approved = await kernelApi.approveProtocolVersion({
        versionRef: selectedVersion.value.ref,
        meaning: "Approved",
        sigToken,
        reason: effectiveReason,
        idempotencyKey: makeIdempotencyKey(),
      });
      await refreshVersionsAndSelect(approved);
      governanceMessage.value = `Approved Protocol Version ${approved.number}.`;
      return approved;
    }

    if (
      signatureMode.value === "withdraw-protocol" &&
      selectedVersion.value?.ref
    ) {
      const withdrawn = await kernelApi.withdrawProtocolVersion({
        versionRef: selectedVersion.value.ref,
        meaning: "Withdrawn",
        sigToken,
        reason: effectiveReason,
        idempotencyKey: makeIdempotencyKey(),
      });
      await refreshVersionsAndSelect(withdrawn);
      governanceMessage.value = `Withdrew Protocol Version ${withdrawn.number}.`;
      return withdrawn;
    }

    if (
      signatureMode.value === "approve-standard" &&
      sdtmigStandard.value?.ref
    ) {
      const approved = await kernelApi.approveStandardVersion({
        standardRef: sdtmigStandard.value.ref,
        meaning: "Approved",
        sigToken,
        reason: effectiveReason,
        idempotencyKey: makeIdempotencyKey(),
      });
      sdtmigStandard.value = approved;
      governanceMessage.value = `Approved ${approved.standard_code} ${approved.version}.`;
      return approved;
    }

    if (
      signatureMode.value === "approve-mapping" &&
      signingMappingSpec.value?.ref
    ) {
      const approved = await kernelApi.approveLibraryItem({
        itemRef: signingMappingSpec.value.ref,
        meaning: "Approved",
        sigToken,
        reason: effectiveReason,
        idempotencyKey: makeIdempotencyKey(),
      });
      await refreshGovernanceContext();
      governanceMessage.value = `Approved mapping specification ${approved.key} v${approved.version}.`;
      return approved;
    }

    return null;
  } finally {
    governanceBusy.value = false;
  }
}

watch(
  () => activeSummaryVersionRef.value,
  () => {
    if (studyNodeRef.value) {
      refreshGovernanceContext();
    }
  }
);

watch(() => props.studyCode, load, { immediate: true });
</script>

<style scoped>
.versions {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.versions h2 {
  margin: 0;
  font-size: 1rem;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
}

.versions-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.versions-table th,
.versions-table td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.versions-table .numeric {
  text-align: right;
}

.versions-table tr.selected {
  background: var(--neutral-light);
}

.row-select {
  border: none;
  background: none;
  padding: 0;
  font: inherit;
  font-weight: 600;
  color: var(--primary);
  cursor: pointer;
}

.masked-row {
  color: var(--neutral-dark);
  font-style: italic;
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}

.governance-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.governance-header h3 {
  margin: 0 0 4px;
  font-size: 0.95rem;
}

.governance-subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.mapping-box,
.amendment-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.mapping-summary {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
}

.mapping-spec-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  list-style: none;
  margin: 0;
  padding: 0;
  font-size: 0.85rem;
}

.mapping-spec-row,
.governance-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.mapping-spec-row {
  justify-content: space-between;
}

.pinned-tag {
  margin-left: 6px;
  padding: 2px 6px;
  border-radius: 999px;
  font-size: 0.75rem;
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.field-row {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 10px;
}

.field-label {
  font-size: 0.82rem;
  font-weight: 600;
}

.text-input {
  padding: 7px 10px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--semantic-color-surface);
}

.banner {
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}

.banner-success {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.banner-error {
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
</style>
