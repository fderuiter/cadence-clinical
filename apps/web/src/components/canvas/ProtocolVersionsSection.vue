<template>
  <section
    id="protocol-versions-section"
    class="versions"
    aria-labelledby="protocol-versions-title"
    :aria-busy="loading"
  >
    <div class="versions-header">
      <div class="versions-header-copy">
        <h2 id="protocol-versions-title" tabindex="-1">Protocol versions</h2>
        <div
          v-if="error"
          id="protocol-versions-status"
          class="status-line"
          role="alert"
        >
          <p class="status-line">
            Protocol versions couldn't be loaded right now.
          </p>
          <SupportDetails :detail="error" />
        </div>
        <p
          v-else
          id="protocol-versions-status"
          class="status-line"
          role="status"
        >
          {{ statusLine }}
        </p>
      </div>

      <button
        v-if="showGovernanceControls && canGovern && forkSourceVersion"
        id="btn-open-fork-amendment"
        type="button"
        class="btn btn-secondary versions-fork-btn"
        :disabled="governanceBusy"
        :aria-expanded="forkFormOpen"
        aria-controls="protocol-fork-card"
        @click="toggleForkForm"
      >
        <GitFork :size="14" aria-hidden="true" />
        <span>{{ forkFormOpen ? "Cancel fork" : "Fork amendment" }}</span>
      </button>
    </div>

    <section
      v-if="forkFormOpen && forkSourceVersion && canGovern"
      id="protocol-fork-card"
      class="gov-card"
      aria-labelledby="protocol-fork-heading"
    >
      <div class="gov-card-header">
        <div>
          <h3 id="protocol-fork-heading" tabindex="-1">
            Fork amendment from Version {{ forkSourceVersion.number }}
          </h3>
          <p class="gov-subtitle">
            Creates a mutable <strong>Draft</strong> amendment version linked to
            Approved Version {{ forkSourceVersion.number }} (K-PROT-2).
          </p>
        </div>
      </div>

      <form
        id="protocol-fork-form"
        class="gov-form"
        @submit.prevent="submitForkAmendment"
      >
        <div class="gov-grid">
          <ClinicalInput
            id="fork-amendment-intent"
            v-model="forkForm.intent"
            label="Amendment intent"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :maxlength="500"
            placeholder="e.g. Added Week 4 cardiometabolic biomarker panel"
            :disabled="governanceBusy"
          />

          <ClinicalInput
            id="fork-amendment-classification"
            v-model="forkForm.classification"
            label="Classification"
            type="select"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :disabled="governanceBusy"
          >
            <option value="Substantial">Substantial</option>
            <option value="NonSubstantial">Non-substantial</option>
            <option value="UrgentSafetyMeasure">Urgent safety measure</option>
          </ClinicalInput>
        </div>

        <div class="gov-grid">
          <ClinicalInput
            id="fork-amendment-rationale"
            v-model="forkForm.rationale"
            label="Clinical rationale"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :maxlength="1000"
            placeholder="e.g. Incorporate Week 4 biomarker safety monitoring per DSMB recommendation"
            :disabled="governanceBusy"
          />

          <ClinicalInput
            id="fork-amendment-transition-policy"
            v-model="forkForm.transitionPolicy"
            label="Participant transition policy"
            type="select"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :disabled="governanceBusy"
          >
            <option value="RequiresReconsent">
              Requires re-consent (RequiresReconsent)
            </option>
            <option value="NewParticipantsOnly">
              New participants only (NewParticipantsOnly)
            </option>
            <option value="Automatic">Automatic transition (Automatic)</option>
            <option value="ByCohort">By cohort (ByCohort)</option>
          </ClinicalInput>
        </div>

        <div class="gov-grid">
          <ClinicalInput
            id="fork-amendment-summary"
            v-model="forkForm.summaryOfChanges"
            label="Summary of changes (optional)"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :maxlength="1000"
            placeholder="e.g. Add Week 4 blood draw to Treatment epoch"
            :disabled="governanceBusy"
          />

          <ClinicalInput
            id="fork-amendment-elements"
            v-model="forkForm.impactedElementsText"
            label="Impacted elements (optional, comma-separated)"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            placeholder="e.g. schedule_of_activities, visit.week4"
            :disabled="governanceBusy"
          />
        </div>

        <ClinicalInput
          id="fork-amendment-reason"
          v-model="forkForm.reason"
          label="Reason for change"
          type="text"
          density="compact"
          :show-query-flag="false"
          extra-class="gov-field"
          :maxlength="500"
          placeholder="e.g. Open Protocol Amendment Version 2 per DSMB safety update"
          :disabled="governanceBusy"
        />

        <div
          v-if="forkErrorMessage"
          id="protocol-fork-error"
          class="gov-banner gov-banner-error"
          role="alert"
        >
          {{ forkErrorMessage }}
        </div>

        <div class="gov-actions">
          <button
            id="btn-submit-fork-amendment"
            type="submit"
            class="btn btn-primary"
            :disabled="!canSubmitFork || governanceBusy"
          >
            {{
              governanceBusy ? "Creating amendment…" : "Create draft amendment"
            }}
          </button>
          <button
            id="btn-cancel-fork-amendment"
            type="button"
            class="btn btn-secondary"
            :disabled="governanceBusy"
            @click="forkFormOpen = false"
          >
            Cancel
          </button>
        </div>
      </form>
    </section>

    <ClinicalDataTable
      v-if="versions.length"
      title="Protocol versions"
      labelled-by="protocol-versions-title"
      hide-header
      plain
      wrapper-class="scrollable-table-wrapper"
      table-class="versions-table"
      :columns="versionColumns"
      :data="versions"
      :row-key="(version, index) => version.ref ?? `masked-${index}`"
      :row-class="
        (version) => ({
          selected:
            version.ref !== null && version.ref === activeSummaryVersionRef,
        })
      "
      :is-row-masked="(version) => version.ref === null"
      external-pagination
    >
      <template #masked-row="{ row }">
        A version you may not see ({{ maskWords(row.mask) }})
      </template>
      <template #cell-number="{ row }">
        <button
          :id="`protocol-version-${row.number}`"
          type="button"
          class="row-select"
          :aria-pressed="row.ref === activeSummaryVersionRef"
          @click="emit('select', row)"
        >
          {{ row.number }}
        </button>
      </template>
      <template #cell-parent="{ row }">
        {{ parentNumber(row) }}
      </template>
      <template #cell-amendment_intent="{ row }">
        {{ row.amendment_intent || "None recorded" }}
      </template>
    </ClinicalDataTable>

    <div
      v-if="governanceActionMessage"
      id="protocol-governance-feedback"
      class="gov-banner gov-banner-success"
      role="status"
    >
      {{ governanceActionMessage }}
    </div>
    <div
      v-if="governanceErrorMessage"
      id="protocol-governance-error"
      class="gov-banner gov-banner-error"
      role="alert"
    >
      {{ governanceErrorMessage }}
    </div>

    <section
      v-if="showGovernanceControls && activeVersion"
      id="protocol-governance-card"
      class="gov-card"
      aria-labelledby="protocol-governance-heading"
    >
      <div class="gov-card-header">
        <div>
          <h3 id="protocol-governance-heading" tabindex="-1">
            Version {{ activeVersion.number }} governance
          </h3>
          <p class="gov-subtitle">
            Status:
            <strong id="protocol-active-status">{{
              activeVersion.status
            }}</strong>
            <template v-if="activeVersion.content_hash">
              · Frozen content hash:
              <code id="protocol-content-hash">{{
                activeVersion.content_hash
              }}</code>
            </template>
          </p>
        </div>
      </div>

      <div
        v-if="showAmendmentReadinessCard"
        id="protocol-amendment-impact-review"
        class="amendment-review"
        aria-labelledby="protocol-amendment-review-heading"
      >
        <div class="amendment-review-header">
          <h4
            id="protocol-amendment-review-heading"
            class="amendment-review-title"
          >
            Amendment impact review
          </h4>
          <div class="amendment-review-actions">
            <button
              v-if="approvalReadiness"
              id="btn-explain-approval-readiness"
              type="button"
              class="btn btn-secondary btn-sm"
              @click="explainApprovalReadiness"
            >
              Explain
            </button>
            <button
              v-if="approvalReadiness && !approvalReadiness.ready"
              id="btn-open-amendment-simulate"
              type="button"
              class="btn btn-secondary btn-sm"
              @click="openAmendmentSimulate"
            >
              Open Simulate
            </button>
          </div>
        </div>

        <div class="amendment-review-box" role="status">
          <p v-if="approvalReadinessLoading" class="gov-empty">
            Checking amendment impact review readiness…
          </p>
          <template v-else-if="approvalReadiness">
            <div
              id="readiness-check-simulation-saved"
              class="amendment-check-row"
              :data-met="approvalReadiness.simulation_report_saved"
            >
              <span
                class="amendment-check-icon"
                :class="
                  approvalReadiness.simulation_report_saved
                    ? 'amendment-check-icon--met'
                    : 'amendment-check-icon--unmet'
                "
                aria-hidden="true"
              >
                <Check
                  v-if="approvalReadiness.simulation_report_saved"
                  :size="12"
                />
                <Clock v-else :size="12" />
              </span>
              <div class="amendment-check-copy">
                <strong>
                  {{
                    approvalReadiness.simulation_report_saved
                      ? "Simulation report saved"
                      : "Simulation report required"
                  }}
                </strong>
                <NodeRefChip
                  v-if="
                    approvalReadiness.simulation_report_artifact_version_ref &&
                    parseNodeRef(
                      approvalReadiness.simulation_report_artifact_version_ref
                    )
                  "
                  :node-ref="
                    approvalReadiness.simulation_report_artifact_version_ref
                  "
                />
                <span
                  v-else-if="!approvalReadiness.simulation_report_saved"
                  class="gov-meta"
                >
                  Run Simulate in the inspector and save an unmasked report
                  (K-SIM-5).
                </span>
              </div>
            </div>

            <div
              id="readiness-check-review-signed"
              class="amendment-check-row"
              :data-met="approvalReadiness.review_signed"
            >
              <span
                class="amendment-check-icon"
                :class="
                  approvalReadiness.review_signed
                    ? 'amendment-check-icon--met'
                    : 'amendment-check-icon--unmet'
                "
                aria-hidden="true"
              >
                <Check v-if="approvalReadiness.review_signed" :size="12" />
                <Clock v-else :size="12" />
              </span>
              <div class="amendment-check-copy">
                <strong>
                  {{
                    approvalReadiness.review_signed
                      ? "Impact review signed"
                      : "Part 11 Reviewed signature required"
                  }}
                </strong>
                <span class="gov-meta">(21 CFR Part 11 Reviewed)</span>
                <NodeRefChip
                  v-if="
                    approvalReadiness.review_attestation_ref &&
                    parseNodeRef(approvalReadiness.review_attestation_ref)
                  "
                  :node-ref="approvalReadiness.review_attestation_ref"
                />
              </div>
            </div>
          </template>
        </div>

        <div
          v-if="inlineReadinessExplainOpen && approvalReadiness?.explanation"
          id="protocol-approval-readiness-explanation"
          class="amendment-inline-explanation"
        >
          <KernelExplanation :explanation="approvalReadiness.explanation" />
        </div>
      </div>

      <div
        v-if="
          canGovern &&
          (activeVersion.status === 'Draft' ||
            activeVersion.status === 'InReview' ||
            activeVersion.status === 'Approved')
        "
        id="protocol-lifecycle-controls"
        class="gov-controls"
      >
        <ClinicalInput
          v-if="
            activeVersion.status === 'Draft' ||
            activeVersion.status === 'InReview'
          "
          id="protocol-governance-reason"
          v-model="lifecycleReason"
          label="Reason for change (required for review or return to draft)"
          type="text"
          density="compact"
          :show-query-flag="false"
          extra-class="gov-field"
          :maxlength="500"
          placeholder="e.g. Freeze protocol design v1 for sponsor clinical and QA review"
          :disabled="governanceBusy"
        />

        <div class="gov-actions-row">
          <div class="gov-actions">
            <button
              v-if="activeVersion.status === 'Draft'"
              id="btn-submit-protocol-review"
              type="button"
              class="btn btn-primary"
              :disabled="governanceBusy || !lifecycleReason.trim()"
              @click="submitForReview"
            >
              {{ governanceBusy ? "Submitting…" : "Submit for review" }}
            </button>

            <template v-if="activeVersion.status === 'InReview'">
              <button
                id="btn-approve-protocol-version"
                type="button"
                class="btn btn-primary"
                :disabled="
                  governanceBusy ||
                  (isAmendmentInReview && approvalReadinessLoading) ||
                  isApprovalBlockedByReadiness
                "
                @click="openApprovalSignature(activeVersion)"
              >
                <ShieldCheck :size="14" aria-hidden="true" />
                <span>Sign &amp; approve protocol (Part 11)</span>
              </button>
              <button
                id="btn-return-protocol-draft"
                type="button"
                class="btn btn-secondary"
                :disabled="governanceBusy || !lifecycleReason.trim()"
                @click="returnToDraft"
              >
                Return to draft
              </button>
            </template>
          </div>

          <button
            v-if="
              activeVersion.status === 'InReview' ||
              activeVersion.status === 'Approved'
            "
            id="btn-withdraw-protocol-version"
            type="button"
            class="btn btn-withdraw"
            :disabled="governanceBusy"
            @click="openWithdrawalSignature(activeVersion)"
          >
            Withdraw version
          </button>
        </div>
      </div>
    </section>

    <section
      v-if="showGovernanceControls && canGovern"
      id="protocol-artifacts-card"
      class="gov-card"
      aria-labelledby="protocol-artifacts-heading"
    >
      <div class="gov-card-header">
        <div>
          <h3 id="protocol-artifacts-heading">
            Approval evidence artifacts (K-TIME-5)
          </h3>
          <p class="gov-subtitle">
            Register IRB/ethics, regulatory authority, or CTA acknowledgement
            artifacts required before transitioning a protocol version to
            <strong>Effective</strong>.
          </p>
        </div>
      </div>

      <ul v-if="artifacts.length" id="protocol-artifacts-list" class="gov-list">
        <li
          v-for="art in artifacts"
          :key="art.artifact_version_ref"
          class="gov-list-item"
        >
          <div>
            <strong>{{ art.title }}</strong>
            <span class="gov-pill">{{ art.kind }}</span>
            <span class="gov-meta">
              v{{ art.version_number }} · <code>{{ art.content_hash }}</code>
            </span>
          </div>
          <code class="gov-ref">{{ art.artifact_version_ref }}</code>
        </li>
      </ul>
      <p v-else class="gov-empty">
        No approval evidence artifacts registered for {{ studyCode }} yet.
      </p>

      <form
        id="protocol-artifact-form"
        class="gov-form"
        @submit.prevent="registerArtifact"
      >
        <div class="gov-grid">
          <ClinicalInput
            id="protocol-artifact-kind"
            v-model="artifactForm.kind"
            label="Evidence kind"
            type="select"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :disabled="governanceBusy"
          >
            <option value="EthicsApproval">
              Ethics / IRB approval (EthicsApproval)
            </option>
            <option value="RegulatoryApproval">
              Regulatory approval (RegulatoryApproval)
            </option>
            <option value="CtaAcknowledgement">
              CTA acknowledgement (CtaAcknowledgement)
            </option>
          </ClinicalInput>

          <ClinicalInput
            id="protocol-artifact-title"
            v-model="artifactForm.title"
            label="Document title"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :maxlength="500"
            placeholder="e.g. Central IRB Approval Letter v1.0"
            :disabled="governanceBusy"
          />
        </div>

        <div class="gov-grid">
          <ClinicalFieldLayout
            id="protocol-artifact-file"
            label="Compute SHA-256 from file (optional)"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
          >
            <input
              id="protocol-artifact-file"
              type="file"
              class="gov-file-input"
              :disabled="governanceBusy"
              @change="onArtifactFileSelected"
            />
          </ClinicalFieldLayout>

          <ClinicalInput
            id="protocol-artifact-hash"
            v-model="artifactForm.contentHash"
            label="Content SHA-256 digest"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            placeholder="sha256:<64 lower-case hex characters>"
            :disabled="governanceBusy"
          />
        </div>

        <div class="gov-grid">
          <ClinicalInput
            id="protocol-artifact-storage"
            v-model="artifactForm.storageRef"
            label="Storage reference"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            placeholder="e.g. s3://cadence-etmf/irb-approval-v1.pdf"
            :disabled="governanceBusy"
          />

          <ClinicalInput
            id="protocol-artifact-reason"
            v-model="artifactForm.reason"
            label="Reason for registration"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :maxlength="500"
            placeholder="e.g. Record central IRB approval for protocol go-live"
            :disabled="governanceBusy"
          />
        </div>

        <div class="gov-actions">
          <button
            id="btn-register-artifact"
            type="submit"
            class="btn btn-secondary"
            :disabled="!canSubmitArtifact || governanceBusy"
          >
            {{ governanceBusy ? "Registering…" : "Register evidence artifact" }}
          </button>
        </div>
      </form>
    </section>

    <section
      v-if="showGovernanceControls"
      id="protocol-effectivity-card"
      class="gov-card"
      aria-labelledby="protocol-effectivity-heading"
    >
      <div class="gov-card-header">
        <div>
          <h3 id="protocol-effectivity-heading" tabindex="-1">
            Protocol effectivity
          </h3>
          <p class="gov-subtitle">
            Declare scope effectivity and transition an approved protocol
            version to <strong>Effective</strong> with approval evidence so
            sites can open casebook forms.
          </p>
        </div>
      </div>

      <ul
        v-if="effectivities.length"
        id="protocol-effectivities-list"
        class="gov-list"
      >
        <li
          v-for="eff in effectivities"
          :key="eff.effectivity_ref"
          class="gov-effectivity-item"
        >
          <div class="gov-effectivity-summary">
            <strong>
              Version {{ effectivityVersionLabel(eff) }} ·
              {{ formatEffectivityScope(eff) }}
            </strong>
            <span
              class="gov-pill"
              :class="{ 'gov-pill-effective': eff.status === 'Effective' }"
            >
              {{ eff.status }}
            </span>
          </div>

          <div
            v-if="
              canGovern &&
              eff.status !== 'Effective' &&
              eff.status !== 'Withdrawn'
            "
            class="gov-transition-box"
          >
            <div class="gov-grid">
              <ClinicalInput
                :id="`effectivity-target-status-${eff.effectivity_id}`"
                :model-value="getTargetStatus(eff.effectivity_ref)"
                label="Target status"
                type="select"
                density="compact"
                :show-query-flag="false"
                extra-class="gov-field"
                :disabled="governanceBusy"
                @update:model-value="
                  setTargetStatus(eff.effectivity_ref, String($event ?? ''))
                "
              >
                <option value="Effective">Effective</option>
                <option value="EthicsApproved">EthicsApproved</option>
                <option value="RegulatoryApproved">RegulatoryApproved</option>
                <option value="Withdrawn">Withdrawn</option>
              </ClinicalInput>

              <ClinicalInput
                :id="`effectivity-reason-${eff.effectivity_id}`"
                :model-value="getTransitionReason(eff.effectivity_ref)"
                label="Reason for status transition"
                type="text"
                density="compact"
                :show-query-flag="false"
                extra-class="gov-field"
                :maxlength="500"
                placeholder="e.g. Mark protocol effective following IRB approval"
                :disabled="governanceBusy"
                @update:model-value="
                  setTransitionReason(eff.effectivity_ref, String($event ?? ''))
                "
              />
            </div>

            <fieldset
              v-if="approvalArtifacts.length"
              class="gov-evidence-fieldset"
            >
              <legend class="gov-label">
                Supporting approval evidence (select at least one for Effective)
              </legend>
              <label
                v-for="art in approvalArtifacts"
                :key="`${eff.effectivity_ref}-${art.artifact_version_ref}`"
                class="gov-checkbox-row"
              >
                <input
                  type="checkbox"
                  :value="art.artifact_version_ref"
                  :checked="
                    isEvidenceSelected(
                      eff.effectivity_ref,
                      art.artifact_version_ref
                    )
                  "
                  :disabled="governanceBusy"
                  @change="
                    toggleEvidence(
                      eff.effectivity_ref,
                      art.artifact_version_ref
                    )
                  "
                />
                <span>{{ art.title }} ({{ art.kind }})</span>
              </label>
            </fieldset>
            <p v-else class="gov-empty">
              Register an ethics, regulatory, or CTA artifact above before
              marking this scope Effective.
            </p>

            <div class="gov-actions">
              <button
                :id="`btn-transition-effectivity-${eff.effectivity_id}`"
                data-testid="btn-transition-effectivity"
                type="button"
                class="btn btn-primary"
                :disabled="!canTransitionEffectivity(eff) || governanceBusy"
                @click="transitionEffectivity(eff)"
              >
                {{
                  governanceBusy
                    ? "Updating…"
                    : `Mark ${getTargetStatus(eff.effectivity_ref)}`
                }}
              </button>
            </div>
          </div>
        </li>
      </ul>
      <p v-else class="gov-empty">
        No protocol effectivities declared for {{ studyCode }} yet.
      </p>

      <form
        v-if="canGovern && activeVersion && activeVersion.ref"
        id="protocol-declare-effectivity-form"
        class="gov-form"
        @submit.prevent="declareEffectivity"
      >
        <div class="gov-grid">
          <ClinicalInput
            id="effectivity-scope-level"
            v-model="declareScopeLevel"
            :label="`Scope for Version ${activeVersion.number}`"
            type="select"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :disabled="governanceBusy"
          >
            <option value="Study">Whole study (Study)</option>
            <option value="Country">Country (ISO alpha-2)</option>
            <option value="StudySite">Single study site</option>
          </ClinicalInput>

          <ClinicalInput
            v-if="declareScopeLevel === 'Country'"
            id="effectivity-country"
            v-model="declareCountry"
            label="Country code (ISO 3166-1 alpha-2)"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :maxlength="2"
            placeholder="US"
            :disabled="governanceBusy"
          />

          <template v-if="declareScopeLevel === 'StudySite'">
            <ClinicalInput
              v-if="studySites.length"
              id="effectivity-study-site"
              v-model="declareStudySiteRef"
              label="Study site"
              type="select"
              density="compact"
              :show-query-flag="false"
              extra-class="gov-field"
              :disabled="governanceBusy"
            >
              <option value="">Select a linked study site</option>
              <option
                v-for="site in studySites"
                :key="site.study_site_ref"
                :value="site.study_site_ref"
              >
                {{ site.site_code || site.study_site_ref }} ({{ site.status }})
              </option>
            </ClinicalInput>
            <ClinicalInput
              v-else
              id="effectivity-study-site"
              v-model="declareStudySiteRef"
              label="Study site"
              type="text"
              density="compact"
              :show-query-flag="false"
              extra-class="gov-field"
              placeholder="study_site:<uuid>"
              :disabled="governanceBusy"
            />
          </template>

          <ClinicalInput
            id="effectivity-declare-reason"
            v-model="declareReason"
            label="Reason for declaration"
            type="text"
            density="compact"
            :show-query-flag="false"
            extra-class="gov-field"
            :maxlength="500"
            placeholder="e.g. Declare study-wide effectivity for approved v1"
            :disabled="governanceBusy"
          />
        </div>

        <div class="gov-actions">
          <button
            id="btn-declare-effectivity"
            type="submit"
            class="btn btn-secondary"
            :disabled="!canDeclareEffectivity || governanceBusy"
          >
            {{
              governanceBusy
                ? "Declaring…"
                : `Declare effectivity for Version ${activeVersion.number}`
            }}
          </button>
        </div>
      </form>
    </section>

    <ProtocolImportPanel
      v-if="canShowImportPanel"
      :study-ref="studyNodeRef"
      :study-code="studyCode"
      :selected-version-ref="activeSummaryVersionRef"
      @imported="onDraftImported"
    />

    <SignatureCaptureModal
      v-if="approvingVersion && approvingVersion.ref"
      :is-open="Boolean(approvingVersion)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Sponsor Designer"
      :reasons="PROTOCOL_APPROVE_REASONS"
      :action-url="protocolVersionApproveAction(approvingVersion.ref)"
      :semantic-action="PROTOCOL_APPROVE_SEMANTIC_ACTION"
      :on-sign="approveProtocolWithToken"
      :fallback-focus="resolveVersionFallbackFocus"
      @cancel="approvingVersion = null"
      @success="handleApprovalSigned"
    />

    <SignatureCaptureModal
      v-if="withdrawingVersion && withdrawingVersion.ref"
      :is-open="Boolean(withdrawingVersion)"
      :username="signerUsername"
      :signer-name="signerFullName"
      signer-role="Sponsor Designer"
      :reasons="PROTOCOL_WITHDRAW_REASONS"
      :action-url="protocolVersionWithdrawAction(withdrawingVersion.ref)"
      :semantic-action="PROTOCOL_WITHDRAW_SEMANTIC_ACTION"
      :on-sign="withdrawProtocolWithToken"
      :fallback-focus="resolveVersionFallbackFocus"
      @cancel="withdrawingVersion = null"
      @success="handleWithdrawalSigned"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import { Check, Clock, GitFork, ShieldCheck } from "lucide-vue-next";
import {
  ClinicalDataTable,
  ClinicalFieldLayout,
  ClinicalInput,
  handoffFocus,
} from "ui";
import {
  describeKernelError,
  isMasked,
  kernelApi,
  type KernelProtocolDraftSummary,
  type KernelProtocolVersion,
} from "../../api/kernelApi";
import {
  kernelGovernanceApi,
  PROTOCOL_APPROVE_SEMANTIC_ACTION,
  PROTOCOL_WITHDRAW_SEMANTIC_ACTION,
  protocolVersionApproveAction,
  protocolVersionWithdrawAction,
  type KernelAmendmentClassification,
  type KernelArtifact,
  type KernelEffectivity,
  type KernelProtocolApprovalReadiness,
  type KernelStudySite,
  type KernelTransitionPolicy,
} from "../../api/kernelGovernanceApi";
import { canGovernProtocols } from "../../canvas/authority";
import { parseNodeRef } from "../../canvas/nodeRef";
import { maskReasonWords, seedNodeSummary } from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import { useAuthStore } from "../../stores/auth";
import {
  useStudyCanvasStore,
  type InspectorVerbRequest,
} from "../../stores/studyCanvas";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

function resolveVersionFallbackFocus() {
  if (typeof document === "undefined") return null;
  return (
    document.querySelector<HTMLElement>(
      "#protocol-versions-section .row-select[aria-pressed='true']"
    ) ||
    document.getElementById("btn-open-fork-amendment") ||
    document.querySelector<HTMLElement>(
      "#protocol-versions-section .row-select"
    )
  );
}
import KernelExplanation from "./KernelExplanation.vue";
import NodeRefChip from "./NodeRefChip.vue";
import ProtocolImportPanel from "./ProtocolImportPanel.vue";
import SupportDetails from "./SupportDetails.vue";

/**
 * The study's protocol versions, newest first (ADR-2540), together with review, 21 CFR Part 11 signed approval,
 * evidence artifact registration, and bitemporal effectivity governance controls.
 */
const props = defineProps<{ studyCode: string; selectedRef: string }>();
const emit = defineEmits<{
  select: [version: KernelProtocolVersion, verb?: InspectorVerbRequest];
}>();

const versionColumns = [
  { key: "number", label: "Version", align: "right", rowHeader: true },
  { key: "status", label: "Status" },
  { key: "parent", label: "Amends" },
  { key: "amendment_intent", label: "Amendment intent" },
];

const APPROVAL_EVIDENCE_KINDS = new Set([
  "EthicsApproval",
  "RegulatoryApproval",
  "CtaAcknowledgement",
]);

const PROTOCOL_APPROVE_REASONS = [
  "Approve frozen protocol version for clinical conduct",
  "APPROVAL",
  "CLINICAL_QC",
];

const PROTOCOL_WITHDRAW_REASONS = [
  "Withdraw superseded or rejected protocol version",
  "OBSOLETE_VERSION",
  "CLINICAL_QC",
];

const authStore = useAuthStore();
const canvasStore = useStudyCanvasStore();

const versions = ref<KernelProtocolVersion[]>([]);
const artifacts = ref<KernelArtifact[]>([]);
const effectivities = ref<KernelEffectivity[]>([]);
const studySites = ref<KernelStudySite[]>([]);

const governanceBusy = ref(false);
const governanceActionMessage = ref("");
const governanceErrorMessage = ref("");
const lifecycleReason = ref("");
const approvingVersion = ref<KernelProtocolVersion | null>(null);
const withdrawingVersion = ref<KernelProtocolVersion | null>(null);

const forkFormOpen = ref(false);
const forkErrorMessage = ref("");
const forkForm = reactive({
  intent: "",
  rationale: "",
  classification: "Substantial" as KernelAmendmentClassification,
  transitionPolicy: "RequiresReconsent" as KernelTransitionPolicy,
  summaryOfChanges: "",
  impactedElementsText: "",
  reason: "",
});

const approvalReadiness = ref<KernelProtocolApprovalReadiness | null>(null);
const approvalReadinessLoading = ref(false);
const inlineReadinessExplainOpen = ref(false);
let readinessReqSeq = 0;

function seedVersions(listed: KernelProtocolVersion[]) {
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
}

const {
  studyRef,
  studyLoading: loading,
  studyError: error,
  noStudy,
} = useKernelStudyRef(() => props.studyCode, {
  onReset: () => {
    versions.value = [];
    artifacts.value = [];
    effectivities.value = [];
    studySites.value = [];
    governanceActionMessage.value = "";
    governanceErrorMessage.value = "";
    forkFormOpen.value = false;
    forkErrorMessage.value = "";
    approvalReadiness.value = null;
    approvalReadinessLoading.value = false;
    inlineReadinessExplainOpen.value = false;
  },
  onResolved: async (study, { isCurrent }) => {
    const listed = await kernelApi.listProtocolVersions(study);
    if (isCurrent()) {
      seedVersions(listed);
      void refreshGovernanceLists(study, isCurrent);
    }
  },
});

const studyNodeRef = computed(() => studyRef.value ?? "");

const artifactForm = reactive({
  kind: "EthicsApproval",
  title: "",
  contentHash: "",
  sizeBytes: 1,
  mediaType: "application/pdf",
  storageRef: "",
  reason: "",
});

const declareScopeLevel = ref<"Study" | "Country" | "StudySite">("Study");
const declareCountry = ref("US");
const declareStudySiteRef = ref("");
const declareReason = ref("");

const targetStatusByRef = reactive<Record<string, string>>({});
const transitionReasonByRef = reactive<Record<string, string>>({});
const selectedEvidenceByRef = reactive<Record<string, string[]>>({});

function getTargetStatus(effectivityRef: string): string {
  return targetStatusByRef[effectivityRef] || "Effective";
}

function setTargetStatus(effectivityRef: string, status: string) {
  targetStatusByRef[effectivityRef] = status;
}

function getTransitionReason(effectivityRef: string): string {
  return transitionReasonByRef[effectivityRef] ?? "";
}

function setTransitionReason(effectivityRef: string, reason: string) {
  transitionReasonByRef[effectivityRef] = reason;
}

const canGovern = computed(() => canGovernProtocols(authStore));

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

const showGovernanceControls = computed(
  () =>
    !loading.value &&
    !error.value &&
    !noStudy.value &&
    Boolean(studyNodeRef.value) &&
    typeof kernelGovernanceApi?.submitProtocolVersionForReview === "function"
);

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

const activeVersion = computed<KernelProtocolVersion | null>(() => {
  const targetRef = activeSummaryVersionRef.value;
  if (!targetRef) return null;
  const targetId = parseNodeRef(targetRef)?.id;
  return (
    versions.value.find((v) => {
      if (!v.ref || isMasked(v)) return false;
      if (v.ref === targetRef) return true;
      return Boolean(targetId && parseNodeRef(v.ref)?.id === targetId);
    }) ?? null
  );
});

const forkSourceVersion = computed<KernelProtocolVersion | null>(() => {
  if (
    activeVersion.value?.ref &&
    !isMasked(activeVersion.value) &&
    activeVersion.value.status === "Approved"
  ) {
    return activeVersion.value;
  }
  return (
    versions.value.find(
      (v) => v.ref && !isMasked(v) && v.status === "Approved"
    ) ?? null
  );
});

const canSubmitFork = computed(
  () =>
    Boolean(forkSourceVersion.value?.ref) &&
    Boolean(forkForm.intent.trim()) &&
    Boolean(forkForm.rationale.trim()) &&
    Boolean(forkForm.reason.trim())
);

const isAmendmentInReview = computed(() =>
  Boolean(
    activeVersion.value &&
    activeVersion.value.status === "InReview" &&
    (activeVersion.value.parent_version ||
      approvalReadiness.value?.is_amendment)
  )
);

const showAmendmentReadinessCard = computed(
  () =>
    canGovern.value &&
    isAmendmentInReview.value &&
    (approvalReadinessLoading.value || approvalReadiness.value !== null)
);

const isApprovalBlockedByReadiness = computed(
  () => approvalReadiness.value !== null && !approvalReadiness.value.ready
);

async function refreshApprovalReadiness() {
  const target = activeVersion.value;
  const seq = ++readinessReqSeq;
  inlineReadinessExplainOpen.value = false;
  if (
    !canGovern.value ||
    !target?.ref ||
    target.status !== "InReview" ||
    !target.parent_version ||
    typeof kernelGovernanceApi?.getProtocolApprovalReadiness !== "function"
  ) {
    approvalReadiness.value = null;
    approvalReadinessLoading.value = false;
    return;
  }
  approvalReadinessLoading.value = true;
  try {
    const res = await kernelGovernanceApi.getProtocolApprovalReadiness(
      target.ref
    );
    if (seq !== readinessReqSeq) return;
    if (res && typeof res === "object" && "ready" in res) {
      approvalReadiness.value = res;
      if (res.simulation_report_artifact_version_ref) {
        seedNodeSummary(res.simulation_report_artifact_version_ref, {
          kind: "artifact_version",
          title: `Version ${target.number} simulation report`,
        });
      }
      if (res.review_attestation_ref) {
        seedNodeSummary(res.review_attestation_ref, {
          kind: "attestation",
          title: "Reviewed attestation",
        });
      }
    } else {
      approvalReadiness.value = null;
    }
  } catch {
    if (seq === readinessReqSeq) {
      approvalReadiness.value = null;
    }
  } finally {
    if (seq === readinessReqSeq) {
      approvalReadinessLoading.value = false;
    }
  }
}

watch(
  () => [
    activeVersion.value?.ref ?? "",
    activeVersion.value?.status ?? "",
    activeVersion.value?.content_hash ?? "",
    canGovern.value,
  ],
  () => {
    void refreshApprovalReadiness();
  },
  { immediate: true }
);

watch(
  () => canvasStore.protocolGovernanceRevision,
  () => {
    void refreshApprovalReadiness();
    if (studyNodeRef.value) {
      void refreshGovernanceLists(studyNodeRef.value);
    }
  }
);

const approvalArtifacts = computed(() =>
  artifacts.value.filter((a) => APPROVAL_EVIDENCE_KINDS.has(a.kind))
);

const canSubmitArtifact = computed(
  () =>
    Boolean(studyNodeRef.value) &&
    Boolean(artifactForm.title.trim()) &&
    Boolean(artifactForm.contentHash.trim()) &&
    Boolean(artifactForm.reason.trim())
);

const canDeclareEffectivity = computed(() => {
  if (!activeVersion.value?.ref || !declareReason.value.trim()) return false;
  if (activeVersion.value.status === "Withdrawn") return false;
  if (declareScopeLevel.value === "Country") {
    return /^[A-Za-z]{2}$/.test(declareCountry.value.trim());
  }
  if (declareScopeLevel.value === "StudySite") {
    return Boolean(declareStudySiteRef.value.trim());
  }
  return true;
});

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

function effectivityVersionLabel(eff: KernelEffectivity): string | number {
  if (eff.protocol_version_number) return eff.protocol_version_number;
  const verId = parseNodeRef(eff.protocol_version_ref)?.id;
  const found = verId ? byId.value.get(verId) : undefined;
  return found?.number ?? eff.protocol_version_ref;
}

function formatEffectivityScope(eff: KernelEffectivity): string {
  if (eff.scope_level === "Country" && eff.country) {
    return `Country ${eff.country}`;
  }
  if (eff.scope_level === "StudySite" && eff.study_site_ref) {
    const matched = studySites.value.find(
      (s) => s.study_site_ref === eff.study_site_ref
    );
    return matched?.site_code
      ? `Site ${matched.site_code}`
      : eff.study_site_ref;
  }
  return "Whole study";
}

function syncEffectivityDefaults(items: KernelEffectivity[]) {
  const defaultEvidence = approvalArtifacts.value[0]?.artifact_version_ref
    ? [approvalArtifacts.value[0].artifact_version_ref]
    : [];
  for (const eff of items) {
    if (!targetStatusByRef[eff.effectivity_ref]) {
      targetStatusByRef[eff.effectivity_ref] = "Effective";
    }
    if (
      !selectedEvidenceByRef[eff.effectivity_ref] ||
      selectedEvidenceByRef[eff.effectivity_ref].length === 0
    ) {
      selectedEvidenceByRef[eff.effectivity_ref] = [...defaultEvidence];
    }
  }
}

function isEvidenceSelected(
  effectivityRef: string,
  artifactVersionRef: string
): boolean {
  return (selectedEvidenceByRef[effectivityRef] ?? []).includes(
    artifactVersionRef
  );
}

function toggleEvidence(effectivityRef: string, artifactVersionRef: string) {
  const current = new Set(selectedEvidenceByRef[effectivityRef] ?? []);
  if (current.has(artifactVersionRef)) {
    current.delete(artifactVersionRef);
  } else {
    current.add(artifactVersionRef);
  }
  selectedEvidenceByRef[effectivityRef] = [...current];
}

function canTransitionEffectivity(eff: KernelEffectivity): boolean {
  const target = targetStatusByRef[eff.effectivity_ref] || "Effective";
  const why = (transitionReasonByRef[eff.effectivity_ref] ?? "").trim();
  if (!why) return false;
  if (
    target === "Effective" ||
    target === "EthicsApproved" ||
    target === "RegulatoryApproved"
  ) {
    return (selectedEvidenceByRef[eff.effectivity_ref] ?? []).length > 0;
  }
  return true;
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

function nextIdempotencyKey(): string {
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `gov-${Date.now()}-${Math.random().toString(16).slice(2)}`;
}

async function refreshGovernanceLists(
  study: string,
  isCurrent: () => boolean = () => studyNodeRef.value === study
) {
  const tasks: Promise<void>[] = [];
  let nextArtifacts: KernelArtifact[] | null = null;
  let nextEffectivities: KernelEffectivity[] | null = null;
  let nextSites: KernelStudySite[] | null = null;
  if (typeof kernelGovernanceApi?.listStudyArtifacts === "function") {
    tasks.push(
      kernelGovernanceApi
        .listStudyArtifacts(study)
        .then((res) => {
          nextArtifacts = Array.isArray(res) ? res : [];
        })
        .catch(() => {
          // Optional governance read: do not fail main protocol version view.
        })
    );
  }
  if (typeof kernelGovernanceApi?.listStudyEffectivities === "function") {
    tasks.push(
      kernelGovernanceApi
        .listStudyEffectivities(study)
        .then((res) => {
          nextEffectivities = Array.isArray(res) ? res : [];
        })
        .catch(() => {
          // Optional governance read.
        })
    );
  }
  if (typeof kernelGovernanceApi?.listStudySites === "function") {
    tasks.push(
      kernelGovernanceApi
        .listStudySites(study)
        .then((res) => {
          nextSites = Array.isArray(res) ? res : [];
        })
        .catch(() => {
          // Optional governance read.
        })
    );
  }
  await Promise.all(tasks);
  if (!isCurrent()) return;
  if (nextArtifacts !== null) artifacts.value = nextArtifacts;
  if (nextEffectivities !== null) effectivities.value = nextEffectivities;
  if (nextSites !== null) studySites.value = nextSites;
  syncEffectivityDefaults(effectivities.value);
}

async function refreshVersions() {
  if (!studyNodeRef.value) return;
  const listed = await kernelApi.listProtocolVersions(studyNodeRef.value);
  seedVersions(listed);
}

async function submitForReview() {
  const target = activeVersion.value;
  const reason = lifecycleReason.value.trim();
  if (!target?.ref || !reason || governanceBusy.value) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  let didSubmit = false;
  try {
    const updated = await kernelGovernanceApi.submitProtocolVersionForReview({
      versionRef: target.ref,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    lifecycleReason.value = "";
    await refreshVersions();
    await refreshApprovalReadiness();
    emit("select", updated);
    governanceActionMessage.value = `Submitted Version ${updated.number} for review (${updated.status}).`;
    didSubmit = true;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }

  if (didSubmit) {
    await handoffFocus(
      [
        "#btn-approve-protocol-version",
        "#btn-open-amendment-simulate",
        "#protocol-governance-reason",
        "#protocol-governance-heading",
        "#protocol-versions-title",
      ],
      {
        container: () => document.getElementById("protocol-versions-section"),
      }
    );
  }
}

async function returnToDraft() {
  const target = activeVersion.value;
  const reason = lifecycleReason.value.trim();
  if (!target?.ref || !reason || governanceBusy.value) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  let didReturn = false;
  try {
    const updated = await kernelGovernanceApi.returnProtocolVersionToDraft({
      versionRef: target.ref,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    lifecycleReason.value = "";
    await refreshVersions();
    emit("select", updated);
    governanceActionMessage.value = `Returned Version ${updated.number} to Draft.`;
    didReturn = true;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }

  if (didReturn) {
    await handoffFocus(
      [
        "#protocol-governance-reason",
        "#btn-submit-protocol-review",
        "#protocol-governance-heading",
        "#protocol-versions-title",
      ],
      {
        container: () => document.getElementById("protocol-versions-section"),
      }
    );
  }
}

function toggleForkForm() {
  forkFormOpen.value = !forkFormOpen.value;
  forkErrorMessage.value = "";
  governanceErrorMessage.value = "";
  if (forkFormOpen.value) {
    void handoffFocus(["#fork-amendment-intent", "#protocol-fork-heading"], {
      container: () => document.getElementById("protocol-versions-section"),
    });
  }
}

async function submitForkAmendment() {
  const parent = forkSourceVersion.value;
  if (!parent?.ref || !canSubmitFork.value || governanceBusy.value) return;
  governanceBusy.value = true;
  forkErrorMessage.value = "";
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  let forked: KernelProtocolVersion | null = null;
  try {
    const impactedElements = forkForm.impactedElementsText
      .split(/[,\n]/)
      .map((s) => s.trim())
      .filter(Boolean);
    forked = await kernelGovernanceApi.forkProtocolVersion({
      parentVersionRef: parent.ref,
      intent: forkForm.intent.trim(),
      rationale: forkForm.rationale.trim(),
      classification: forkForm.classification,
      transitionPolicy: forkForm.transitionPolicy,
      summaryOfChanges: forkForm.summaryOfChanges.trim() || null,
      impactedElements,
      reason: forkForm.reason.trim(),
      idempotencyKey: nextIdempotencyKey(),
    });
    forkForm.intent = "";
    forkForm.rationale = "";
    forkForm.summaryOfChanges = "";
    forkForm.impactedElementsText = "";
    forkForm.reason = "";
    forkFormOpen.value = false;
    await refreshVersions();
    emit("select", forked);
    governanceActionMessage.value = `Forked Draft Version ${forked.number} from Version ${parent.number}.`;
  } catch (err) {
    forkErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }

  if (forked) {
    await handoffFocus(
      [
        "#protocol-governance-reason",
        "#btn-submit-protocol-review",
        "#protocol-governance-heading",
        "#protocol-versions-title",
      ],
      {
        container: () => document.getElementById("protocol-versions-section"),
      }
    );
  }
}

function explainApprovalReadiness() {
  const target = activeVersion.value;
  const readiness = approvalReadiness.value;
  if (!readiness?.explanation) return;
  const handled = canvasStore.requestExplain({
    targetNodeRef: target?.ref ?? null,
    title: `Why: Approve Version ${target?.number ?? ""}`.trim(),
    explanation: readiness.explanation,
  });
  if (!handled) {
    inlineReadinessExplainOpen.value = !inlineReadinessExplainOpen.value;
  }
}

function openAmendmentSimulate() {
  const target = activeVersion.value;
  if (!target?.ref) return;
  emit("select", target, "simulate");
}

function openApprovalSignature(version: KernelProtocolVersion) {
  if (
    isApprovalBlockedByReadiness.value ||
    (isAmendmentInReview.value && approvalReadinessLoading.value)
  ) {
    return;
  }
  governanceErrorMessage.value = "";
  approvingVersion.value = version;
}

function handleApprovalSigned() {
  approvingVersion.value = null;
  void handoffFocus(
    [
      "#effectivity-scope-level",
      "#protocol-artifact-kind",
      "#protocol-governance-heading",
      "#protocol-versions-title",
    ],
    {
      container: () => document.getElementById("protocol-versions-section"),
    }
  );
}

async function approveProtocolWithToken(sigToken: string, reason: string) {
  const target = approvingVersion.value;
  if (!target?.ref) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const approved = await kernelGovernanceApi.approveProtocolVersion({
      versionRef: target.ref,
      meaning: "Approved",
      expectedContentHash: target.content_hash,
      ...(approvalReadiness.value?.review_attestation_ref
        ? {
            reviewAttestationRef:
              approvalReadiness.value.review_attestation_ref,
          }
        : {}),
      sigToken,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    await refreshVersions();
    emit("select", approved);
    governanceActionMessage.value = `Signed and approved Version ${approved.number} under 21 CFR Part 11.`;
    return approved;
  } finally {
    governanceBusy.value = false;
  }
}

function openWithdrawalSignature(version: KernelProtocolVersion) {
  governanceErrorMessage.value = "";
  withdrawingVersion.value = version;
}

function handleWithdrawalSigned() {
  withdrawingVersion.value = null;
  void handoffFocus(["#protocol-versions-title"], {
    container: () => document.getElementById("protocol-versions-section"),
  });
}

async function withdrawProtocolWithToken(sigToken: string, reason: string) {
  const target = withdrawingVersion.value;
  if (!target?.ref) return;
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const withdrawn = await kernelGovernanceApi.withdrawProtocolVersion({
      versionRef: target.ref,
      meaning: "Withdrawn",
      expectedContentHash: target.content_hash,
      sigToken,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    await refreshVersions();
    emit("select", withdrawn);
    governanceActionMessage.value = `Signed and withdrew Version ${withdrawn.number} under 21 CFR Part 11.`;
    return withdrawn;
  } finally {
    governanceBusy.value = false;
  }
}

async function onArtifactFileSelected(event: Event) {
  const input = event.target as HTMLInputElement | null;
  const file = input?.files?.[0];
  if (!file) return;
  if (!artifactForm.title.trim()) {
    artifactForm.title = file.name;
  }
  artifactForm.sizeBytes = Math.max(1, file.size);
  artifactForm.mediaType =
    (file.type || "application/pdf").trim().toLowerCase() || "application/pdf";
  if (!artifactForm.storageRef.trim()) {
    artifactForm.storageRef = `urn:cadence:artifact:${encodeURIComponent(file.name)}`;
  }
  if (typeof globalThis.crypto?.subtle?.digest === "function") {
    const bytes = await file.arrayBuffer();
    const digest = await globalThis.crypto.subtle.digest("SHA-256", bytes);
    const hex = Array.from(new Uint8Array(digest))
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");
    artifactForm.contentHash = `sha256:${hex}`;
  }
}

async function registerArtifact() {
  if (!studyNodeRef.value || !canSubmitArtifact.value || governanceBusy.value) {
    return;
  }
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const key = nextIdempotencyKey();
    const created = await kernelGovernanceApi.registerStudyArtifact({
      studyRef: studyNodeRef.value,
      kind: artifactForm.kind,
      title: artifactForm.title.trim(),
      contentHash: artifactForm.contentHash.trim(),
      sizeBytes: artifactForm.sizeBytes,
      mediaType: artifactForm.mediaType,
      storageRef:
        artifactForm.storageRef.trim() || `urn:cadence:artifact:${key}`,
      reason: artifactForm.reason.trim(),
      idempotencyKey: key,
    });
    artifactForm.title = "";
    artifactForm.contentHash = "";
    artifactForm.storageRef = "";
    artifactForm.reason = "";
    artifactForm.sizeBytes = 1;
    await refreshGovernanceLists(studyNodeRef.value);
    for (const eff of effectivities.value) {
      if (eff.status !== "Effective" && eff.status !== "Withdrawn") {
        const current = new Set(
          selectedEvidenceByRef[eff.effectivity_ref] ?? []
        );
        current.add(created.artifact_version_ref);
        selectedEvidenceByRef[eff.effectivity_ref] = [...current];
      }
    }
    governanceActionMessage.value = `Registered ${created.kind} artifact "${created.title}".`;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function declareEffectivity() {
  const target = activeVersion.value;
  if (!target?.ref || !canDeclareEffectivity.value || governanceBusy.value) {
    return;
  }
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  try {
    const declared = await kernelGovernanceApi.declareProtocolEffectivity({
      versionRef: target.ref,
      scopeLevel: declareScopeLevel.value,
      country:
        declareScopeLevel.value === "Country"
          ? declareCountry.value.trim().toUpperCase()
          : null,
      studySiteRef:
        declareScopeLevel.value === "StudySite"
          ? declareStudySiteRef.value.trim()
          : null,
      reason: declareReason.value.trim(),
      idempotencyKey: nextIdempotencyKey(),
    });
    declareReason.value = "";
    if (studyNodeRef.value) {
      await refreshGovernanceLists(studyNodeRef.value);
    } else {
      effectivities.value = [declared, ...effectivities.value];
      syncEffectivityDefaults(effectivities.value);
    }
    governanceActionMessage.value = `Declared ${declared.scope_level} effectivity (${declared.status}) for Version ${target.number}.`;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }
}

async function transitionEffectivity(eff: KernelEffectivity) {
  if (!canTransitionEffectivity(eff) || governanceBusy.value) return;
  const targetStatus = targetStatusByRef[eff.effectivity_ref] || "Effective";
  const reason = (transitionReasonByRef[eff.effectivity_ref] ?? "").trim();
  const evidenceRefs =
    targetStatus === "Withdrawn"
      ? []
      : (selectedEvidenceByRef[eff.effectivity_ref] ?? []);
  governanceBusy.value = true;
  governanceErrorMessage.value = "";
  governanceActionMessage.value = "";
  let didTransition = false;
  try {
    const updated = await kernelGovernanceApi.changeEffectivityStatus({
      effectivityRef: eff.effectivity_ref,
      status: targetStatus,
      evidenceRefs,
      reason,
      idempotencyKey: nextIdempotencyKey(),
    });
    transitionReasonByRef[eff.effectivity_ref] = "";
    if (studyNodeRef.value) {
      await refreshGovernanceLists(studyNodeRef.value);
    }
    if (props.studyCode) {
      void canvasStore.loadScheduleOptions(props.studyCode, { force: true });
    }
    governanceActionMessage.value = `Effectivity moved to ${updated.status}.`;
    didTransition = true;
  } catch (err) {
    governanceErrorMessage.value = describeKernelError(err);
  } finally {
    governanceBusy.value = false;
  }

  if (didTransition) {
    await handoffFocus(
      [
        () =>
          document.getElementById(`effectivity-reason-${eff.effectivity_id}`),
        '[data-testid="btn-transition-effectivity"]:not([disabled])',
        "#effectivity-scope-level",
        "#protocol-effectivity-heading",
        "#protocol-versions-title",
      ],
      {
        container: () => document.getElementById("protocol-versions-section"),
      }
    );
  }
}

async function onDraftImported(summary: KernelProtocolDraftSummary) {
  if (studyNodeRef.value) {
    try {
      await refreshVersions();
      await refreshGovernanceLists(studyNodeRef.value);
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
</script>

<style scoped>
.versions {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
  max-width: 100%;
}

.versions-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 12px;
}

.versions-header-copy {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.versions-fork-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.versions h2 {
  margin: 0;
  font-size: 1rem;
}

.amendment-review {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.amendment-review-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.amendment-review-title {
  margin: 0;
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.04em;
  text-transform: uppercase;
  color: var(--neutral-dark);
}

.amendment-review-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.amendment-review-box {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--neutral-light);
}

.amendment-check-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  font-size: 0.85rem;
}

.amendment-check-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex: none;
  width: 18px;
  height: 18px;
  margin-top: 1px;
  border-radius: 999px;
}

.amendment-check-icon--met {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark, var(--semantic-color-success));
}

.amendment-check-icon--unmet {
  background: var(--semantic-color-warning-bg-soft);
  color: var(--warning);
}

.amendment-check-copy {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
  min-width: 0;
}

.amendment-inline-explanation {
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface);
}

.gov-actions-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 10px;
}

#btn-approve-protocol-version {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.btn-withdraw {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 32px);
  padding: 6px 10px;
  border: 1px solid transparent;
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  font: inherit;
  font-size: 0.85rem;
  font-weight: 500;
  cursor: pointer;
}

.btn-withdraw:hover:not(:disabled) {
  color: var(--semantic-color-error);
  background: var(--semantic-color-error-bg-soft);
  border-color: var(--semantic-color-error-border);
}

.btn-withdraw:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.status-line {
  margin: 0;
  color: var(--neutral-dark);
}

:deep(.versions-table tr.selected) {
  background: var(--neutral-light);
}

.row-select {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  border: none;
  border-radius: 4px;
  background: none;
  padding: 2px 4px;
  font: inherit;
  font-weight: 600;
  color: var(--text-main);
  cursor: pointer;
  box-sizing: border-box;
}
.gov-banner {
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.9rem;
}

.gov-banner-success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.gov-banner-error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error);
}

.gov-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
}

.gov-card-header h3 {
  margin: 0 0 4px;
  font-size: 0.95rem;
}

.gov-subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
  overflow-wrap: anywhere;
}

.gov-controls,
.gov-form {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
  max-width: 100%;
}

.gov-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 210px), 1fr));
  gap: 10px;
  min-width: 0;
}

.gov-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin-bottom: 0;
  min-width: 0;
  max-width: 100%;
}

.gov-label {
  font-size: 0.82rem;
  font-weight: 600;
}

.gov-file-input {
  font-size: 0.84rem;
  min-width: 0;
  max-width: 100%;
  width: 100%;
  box-sizing: border-box;
}

.gov-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.gov-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.gov-list-item,
.gov-effectivity-item {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  font-size: 0.88rem;
  min-width: 0;
  max-width: 100%;
  overflow-wrap: anywhere;
}

.gov-effectivity-summary {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}

.gov-transition-box {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding-top: 8px;
  border-top: 1px solid var(--border);
}

.gov-evidence-fieldset {
  margin: 0;
  padding: 8px 10px;
  border: 1px solid var(--border);
  border-radius: 4px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.gov-checkbox-row {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
}

.gov-pill {
  display: inline-block;
  margin-left: 6px;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.76rem;
  font-weight: 600;
  background: var(--semantic-color-surface-header);
}

.gov-pill-effective {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.gov-meta,
.gov-ref,
.gov-empty {
  margin: 0;
  font-size: 0.82rem;
  color: var(--neutral-dark);
}
</style>
