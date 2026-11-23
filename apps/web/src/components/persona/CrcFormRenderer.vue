<template>
  <div class="crc-form-renderer">
    <div class="card">
      <div
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-bottom: var(--spacing-sm);
        "
      >
        <div class="card-title" style="margin-bottom: 0">
          {{ $t("ecrf.form.title") }}
        </div>
        <div style="display: flex; align-items: center; gap: 8px">
          <span
            class="badge"
            :style="{
              backgroundColor:
                activeSubjectVersion === '2.0.0'
                  ? 'var(--semantic-color-info-light)'
                  : 'var(--semantic-color-surface-header)',
              color:
                activeSubjectVersion === '2.0.0'
                  ? 'var(--semantic-color-info)'
                  : 'var(--semantic-color-text-secondary)',
              padding: '4px 8px',
              borderRadius: '4px',
              fontWeight: 600,
              fontSize: '0.75rem',
            }"
          >
            {{ $t("ecrf.form.schemaBadge", { version: activeSubjectVersion }) }}
          </span>
          <button
            id="btn-enroll-subject"
            type="button"
            class="btn btn-secondary"
            style="
              display: inline-flex;
              align-items: center;
              gap: 4px;
              padding: 6px 12px;
              font-size: 0.85rem;
              font-weight: 600;
              border: 1px solid var(--border);
              border-radius: 4px;
              cursor: pointer;
            "
            @click="showEnrollModal = true"
          >
            <span>➕</span> {{ $t("ecrf.form.enrollNewSubject") }}
          </button>
        </div>
      </div>

      <!-- Subject & Visit Selection Panel -->
      <div
        style="
          display: flex;
          flex-wrap: wrap;
          gap: var(--spacing-md);
          margin-bottom: var(--spacing-md);
          border-bottom: 1px solid var(--border);
          padding-bottom: 12px;
        "
      >
        <div class="form-group" style="flex: 1">
          <label for="ecrf-subject-selector" style="font-weight: bold">{{
            $t("ecrf.form.activeSubjectId")
          }}</label>
          <select
            id="ecrf-subject-selector"
            :value="selectedSubjectId"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @change="
              $emit('update:selectedSubjectId', $event.target.value);
              $emit('load-ecrf-session');
            "
          >
            <option
              v-if="!(store?.subjects || clinicalStore.subjects).length"
              value=""
              disabled
            >
              {{ $t("ecrf.form.noSubjects") }}
            </option>
            <option
              v-for="sub in store?.subjects || clinicalStore.subjects"
              :key="sub.id"
              :value="sub.id"
            >
              {{ sub.label || `${sub.id} (${sub.status})` }}
            </option>
          </select>
        </div>
        <div class="form-group" style="flex: 1">
          <label for="ecrf-visit-selector" style="font-weight: bold">{{
            $t("ecrf.form.activeVisit")
          }}</label>
          <select
            id="ecrf-visit-selector"
            :value="selectedVisitId"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 4px;
            "
            @change="
              $emit('update:selectedVisitId', $event.target.value);
              $emit('load-ecrf-session');
            "
          >
            <option value="Screening">
              {{ $t("ecrf.form.visitScreening") }}
            </option>
            <option value="Week2">{{ $t("ecrf.form.visitWeek2") }}</option>
            <option value="Week4">{{ $t("ecrf.form.visitWeek4") }}</option>
          </select>
        </div>
      </div>

      <!-- Protocol Training Qualification Gating Banner (PRD-TRN-002) -->
      <div
        v-if="isTrainingGated"
        id="training-gating-banner"
        class="training-banner"
        style="
          background-color: var(--semantic-color-warning-bg-soft);
          border: 2px solid var(--semantic-color-warning-border-strong);
          border-radius: 8px;
          padding: 16px 20px;
          margin-bottom: var(--spacing-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 12px;
        "
      >
        <div>
          <div
            style="
              font-size: 1.05rem;
              font-weight: 700;
              color: var(--semantic-color-warning-dark);
              display: flex;
              align-items: center;
              gap: 8px;
            "
          >
            <span>🎓</span>
            <span>{{ $t("ecrf.form.trainingGatedTitle") }}</span>
          </div>
          <p
            style="
              margin: 4px 0 0 0;
              font-size: 0.875rem;
              color: var(--semantic-color-warning-dark);
            "
          >
            {{ $t("ecrf.form.trainingGatedDesc") }}
          </p>
        </div>
        <div v-if="trainingAvailable">
          <router-link v-slot="{ navigate }" to="/training" custom>
            <button
              id="btn-complete-training"
              type="button"
              class="btn btn-primary"
              style="
                background-color: var(--semantic-color-warning-action);
                color: white;
                font-weight: bold;
                padding: 8px 14px;
                font-size: 0.85rem;
                border: none;
                border-radius: 4px;
                cursor: pointer;
              "
              @click="navigate"
            >
              {{ $t("ecrf.form.completeTrainingNow") }}
            </button>
          </router-link>
        </div>
      </div>

      <!-- Protocol Amendment Re-Consent Gating Banner -->
      <div
        v-if="isReconsentGated"
        id="reconsent-gating-banner"
        class="reconsent-banner"
        style="
          background-color: var(--semantic-color-error-bg-soft);
          border: 2px solid var(--semantic-color-error);
          border-radius: 8px;
          padding: 16px 20px;
          margin-bottom: var(--spacing-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 12px;
        "
      >
        <div>
          <div
            style="
              font-size: 1.05rem;
              font-weight: 700;
              color: var(--semantic-color-error-text-dark);
              display: flex;
              align-items: center;
              gap: 8px;
            "
          >
            <span>⚠️</span>
            <span>{{ $t("ecrf.form.reconsentGatedTitle") }}</span>
          </div>
          <p
            style="
              margin: 4px 0 0 0;
              font-size: 0.875rem;
              color: var(--semantic-color-error-text-deep);
            "
          >
            {{ $t("ecrf.form.reconsentGatedDesc") }}
          </p>
        </div>
        <div style="display: flex; gap: 8px; flex-wrap: wrap">
          <button
            id="btn-open-econsent"
            type="button"
            class="btn btn-primary"
            style="
              background-color: var(--semantic-color-error);
              color: white;
              font-weight: bold;
              padding: 8px 14px;
              font-size: 0.85rem;
              border-radius: 6px;
              border: none;
              cursor: pointer;
            "
            @click="$emit('open-econsent-modal')"
          >
            ✍️ {{ $t("ecrf.form.openEconsent") }}
          </button>
          <button
            id="btn-upload-paper-icf"
            type="button"
            class="btn btn-secondary"
            style="
              background-color: var(--semantic-color-surface);
              color: var(--semantic-color-error-text-dark);
              border: 1px solid var(--semantic-color-error);
              font-weight: bold;
              padding: 8px 14px;
              font-size: 0.85rem;
              border-radius: 6px;
              cursor: pointer;
            "
            @click="$emit('open-paper-icf-modal')"
          >
            📄 {{ $t("ecrf.form.uploadPaperIcf") }}
          </button>
        </div>
      </div>

      <!-- Live Edit Checks Summary Bar -->
      <div
        v-if="activeEditCheckAlerts.length > 0"
        id="edit-checks-summary-bar"
        style="
          background-color: var(--semantic-color-warning-bg-soft);
          border: 1px solid var(--semantic-color-warning-border);
          border-left: 4px solid var(--semantic-color-warning-border-strong);
          border-radius: 6px;
          padding: 10px 14px;
          margin-bottom: var(--spacing-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 8px;
        "
      >
        <div
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 0.875rem;
            color: var(--semantic-color-warning-dark);
            font-weight: 600;
          "
        >
          <span>⚡ {{ $t("ecrf.form.liveEditChecks") }}</span>
          <span
            v-if="discrepancyAlertsCount > 0"
            class="badge badge-discrepancy tabular-nums"
            style="
              background-color: var(--semantic-color-error);
              color: white;
              padding: 2px 6px;
              border-radius: 4px;
              font-size: 0.75rem;
            "
          >
            {{
              $t("ecrf.form.discrepancyCount", {
                count: discrepancyAlertsCount,
              })
            }}
          </span>
          <span
            v-if="warningAlertsCount > 0"
            class="badge badge-warning tabular-nums"
            style="
              background-color: var(--semantic-color-warning-action);
              color: white;
              padding: 2px 6px;
              border-radius: 4px;
              font-size: 0.75rem;
            "
          >
            {{ $t("ecrf.form.warningCount", { count: warningAlertsCount }) }}
          </span>
        </div>
        <div style="font-size: 0.8rem; color: var(--semantic-color-warning)">
          {{ $t("ecrf.form.realtimeEvaluationActive") }}
        </div>
      </div>

      <!-- Unsaved Draft Changes / Local Draft Recovery Banner -->
      <div
        v-if="isDirty || draftRestored"
        id="ecrf-draft-status-banner"
        class="draft-banner"
        style="
          background-color: var(--semantic-color-info-bg);
          border: 1px solid var(--semantic-color-divider-accent);
          border-left: 4px solid var(--semantic-color-interactive-primary);
          border-radius: 6px;
          padding: 10px 14px;
          margin-bottom: var(--spacing-md);
          display: flex;
          justify-content: space-between;
          align-items: center;
          flex-wrap: wrap;
          gap: 8px;
        "
      >
        <div
          style="
            display: flex;
            align-items: center;
            gap: 8px;
            font-size: 0.875rem;
            color: var(--semantic-color-info);
            font-weight: 600;
          "
        >
          <span
            >📝
            {{
              isDirty
                ? $t("ecrf.form.unsavedDraftChanges")
                : $t("ecrf.form.draftRecoveryActive")
            }}</span
          >
          <span
            class="badge badge-draft"
            style="
              background-color: var(--semantic-color-interactive-primary);
              color: white;
              padding: 2px 6px;
              border-radius: 4px;
              font-size: 0.75rem;
            "
          >
            Draft
          </span>
        </div>
        <div style="font-size: 0.8rem; color: var(--semantic-color-info)">
          {{
            isDirty
              ? $t("ecrf.form.draftAutoSaving")
              : $t("ecrf.form.draftRestoredMsg")
          }}
        </div>
      </div>

      <form
        id="form-VS_DEMO"
        class="clinical-form responsive-grid"
        @submit.prevent
      >
        <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
        <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
        <fieldset
          :disabled="isReconsentGated"
          style="border: none; padding: 0; margin: 0; display: contents"
          @click="handleFieldsetInteraction"
        >
          <template v-for="field in renderedFields" :key="field.id">
            <div
              v-show="store.fieldVisibility[field.id] ?? true"
              :style="`grid-column: span ${field.gridSpan || 12}; display: flex; flex-direction: column; gap: 8px;`"
              style="margin-bottom: 8px; position: relative"
            >
              <div
                v-if="dirtyFields && dirtyFields[field.id]"
                :id="`dirty-badge-${field.id}`"
                class="dirty-dot-badge"
                style="
                  position: absolute;
                  top: -2px;
                  right: 0px;
                  display: inline-flex;
                  align-items: center;
                  gap: 4px;
                  background-color: var(--semantic-color-warning-bg-soft);
                  border: 1px solid var(--semantic-color-warning-border);
                  color: var(--semantic-color-warning);
                  font-size: 0.7rem;
                  font-weight: 600;
                  padding: 2px 6px;
                  border-radius: 10px;
                  z-index: 10;
                "
                title="Unsaved draft change"
              >
                <span
                  class="dirty-dot"
                  style="
                    width: 6px;
                    height: 6px;
                    background-color: var(--semantic-color-warning-action);
                    border-radius: 50%;
                  "
                ></span>
                <span>Unsaved</span>
              </div>
              <ClinicalFormField
                :field="field"
                :model-value="store.formValues[field.id]"
                :query="store.formQueries[field.id]"
                :error="getValidationError(field)"
                :lookup-status="lookupStatuses[field.id]"
                :can-manage-queries="store.canManageQueries"
                :query-label="store.getQueryLabel(store.formQueries[field.id])"
                @update:model-value="updateFieldValue(field.id, $event)"
                @input="$emit('handle-lookup-input', field, $event)"
                @change="
                  (val, target) =>
                    $emit('handle-field-change', field, val, target)
                "
                @create-query="$emit('create-query', field.id, $event)"
                @respond-query="$emit('respond-query', field.id, $event)"
                @close-query="$emit('close-query', field.id)"
                @reopen-query="$emit('reopen-query', field.id)"
              />

              <!-- Live Edit Check Inline Banner -->
              <template
                v-for="alert in getFieldAlerts(field.id)"
                :key="alert.id"
              >
                <div
                  :id="`edit-check-banner-${field.id}`"
                  class="edit-check-banner"
                  :style="{
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'space-between',
                    gap: '8px',
                    padding: '6px 10px',
                    borderRadius: '4px',
                    fontSize: '0.8rem',
                    backgroundColor:
                      alert.severity === 'DISCREPANCY'
                        ? 'var(--semantic-color-error-bg-soft)'
                        : 'var(--semantic-color-warning-bg-soft)',
                    border:
                      alert.severity === 'DISCREPANCY'
                        ? '1px solid var(--semantic-color-error)'
                        : '1px solid var(--semantic-color-warning-border)',
                    color:
                      alert.severity === 'DISCREPANCY'
                        ? 'var(--semantic-color-error-text-dark)'
                        : 'var(--semantic-color-warning-dark)',
                  }"
                >
                  <div style="display: flex; align-items: center; gap: 6px">
                    <span
                      :class="`badge badge-${alert.severity.toLowerCase()}`"
                      :style="{
                        backgroundColor:
                          alert.severity === 'DISCREPANCY'
                            ? 'var(--semantic-color-error)'
                            : 'var(--semantic-color-warning-action)',
                        color: 'white',
                        padding: '2px 6px',
                        borderRadius: '4px',
                        fontSize: '0.7rem',
                        fontWeight: 'bold',
                        letterSpacing: '0.5px',
                      }"
                    >
                      {{ alert.severity }}
                    </span>
                    <span>{{ alert.message }}</span>
                  </div>
                  <button
                    v-if="!store.formQueries[field.id]"
                    type="button"
                    class="btn-raise-query"
                    style="
                      background: transparent;
                      border: 1px solid currentColor;
                      border-radius: 4px;
                      padding: 2px 6px;
                      font-size: 0.75rem;
                      font-weight: 600;
                      cursor: pointer;
                      color: inherit;
                    "
                    @click="raiseQueryForAlert(field.id, alert.message)"
                  >
                    Raise Query
                  </button>
                </div>
              </template>

              <!-- Single Field SDV Checkbox -->
              <div
                v-if="isCraUser"
                style="
                  display: flex;
                  align-items: center;
                  gap: 8px;
                  background-color: var(--semantic-color-success-bg-soft);
                  border: 1px dashed var(--semantic-color-success-border-soft);
                  padding: 8px;
                  border-radius: 4px;
                "
                class="sdv-box"
              >
                <input
                  :id="`sdv-${field.id}`"
                  type="checkbox"
                  :checked="Boolean(sdvStates[getSdvKey(field.id)])"
                  style="cursor: pointer"
                  @change="
                    $emit('handle-sdv-toggle', field.id, $event.target.checked)
                  "
                />
                <label
                  :for="`sdv-${field.id}`"
                  style="
                    font-size: 0.8rem;
                    color: var(--semantic-color-success-dark);
                    font-weight: 600;
                    margin: 0;
                    cursor: pointer;
                  "
                >
                  Source Document Verified (SDV)
                </label>
              </div>
            </div>
          </template>
        </fieldset>
      </form>

      <div class="form-actions">
        <button
          id="btn-clear-ecrf"
          class="btn"
          :disabled="isReconsentGated"
          @click="$emit('clear-form')"
        >
          {{ $t("ecrf.form.clearForm") }}
        </button>
        <button
          id="btn-submit-ecrf"
          class="btn btn-primary"
          :style="{
            backgroundColor: isReconsentGated
              ? 'var(--semantic-color-error)'
              : undefined,
            opacity: isReconsentGated ? 0.9 : 1,
          }"
          @click="handleSubmitClick"
        >
          {{
            isReconsentGated
              ? $t("ecrf.form.lockedReconsent")
              : $t("ecrf.form.submitEcrfSession")
          }}
        </button>
      </div>
    </div>

    <!-- Subject Enrollment Modal Dialog -->
    <ClinicalModal
      v-model="showEnrollModal"
      :teleport="false"
      overlay-id="enroll-subject-modal"
      dialog-class="modal"
      :title="`➕ ${$t('ecrf.enrollModal.header')}`"
    >
      <p style="font-size: 0.9rem; color: var(--semantic-color-text-secondary)">
        {{ $t("ecrf.enrollModal.desc") }}
      </p>
      <div class="form-group" style="margin-bottom: 12px">
        <label
          for="enroll-subject-id"
          style="font-size: 0.85rem; font-weight: 600"
          >{{ $t("ecrf.enrollModal.subjectId") }}</label
        >
        <input
          id="enroll-subject-id"
          v-model="enrollForm.subjectId"
          type="text"
          class="form-control"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            box-sizing: border-box;
          "
          placeholder="e.g. SUBJ-101-011"
        />
      </div>
      <div class="form-group" style="margin-bottom: 12px">
        <label
          for="enroll-site-id"
          style="font-size: 0.85rem; font-weight: 600"
          >{{ $t("ecrf.enrollModal.siteId") }}</label
        >
        <select
          id="enroll-site-id"
          v-model="enrollForm.siteId"
          class="form-control"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            box-sizing: border-box;
          "
        >
          <option value="SITE-101">SITE-101 (Main Clinical Center)</option>
          <option value="SITE-102">SITE-102 (Regional Oncology Center)</option>
          <option value="SITE-103">
            SITE-103 (Metropolitan Research Site)
          </option>
        </select>
      </div>
      <div class="form-group" style="margin-bottom: 12px">
        <label
          for="enroll-consent-date"
          style="font-size: 0.85rem; font-weight: 600"
          >{{ $t("ecrf.enrollModal.consentDate") }}</label
        >
        <input
          id="enroll-consent-date"
          v-model="enrollForm.consentDate"
          type="date"
          class="form-control"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            box-sizing: border-box;
          "
        />
      </div>
      <div class="form-group" style="margin-bottom: 12px">
        <label
          for="enroll-arm-id"
          style="font-size: 0.85rem; font-weight: 600"
          >{{ $t("ecrf.enrollModal.assignedArm") }}</label
        >
        <select
          id="enroll-arm-id"
          v-model="enrollForm.armId"
          class="form-control"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            box-sizing: border-box;
          "
        >
          <option value="ARM-A">Arm A: Active Cadence-001 (10mg/day)</option>
          <option value="ARM-B">Arm B: Placebo Control</option>
        </select>
      </div>
      <div class="form-group" style="margin-bottom: 12px">
        <label
          for="enroll-change-reason"
          style="font-size: 0.85rem; font-weight: 600"
          >{{ $t("ecrf.enrollModal.changeReason") }}</label
        >
        <input
          id="enroll-change-reason"
          v-model="enrollForm.changeReason"
          type="text"
          class="form-control"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            box-sizing: border-box;
          "
          placeholder="Initial subject enrollment"
        />
      </div>
      <template #footer>
        <button
          id="btn-cancel-enroll"
          type="button"
          class="btn btn-secondary"
          @click="showEnrollModal = false"
        >
          {{ $t("ecrf.enrollModal.cancel") }}
        </button>
        <button
          id="btn-confirm-enroll"
          type="button"
          class="btn btn-primary"
          style="
            background-color: var(--semantic-color-interactive-primary);
            color: var(--semantic-color-surface);
          "
          :disabled="!enrollForm.subjectId.trim()"
          @click="handleEnrollSubmit"
        >
          {{ $t("ecrf.enrollModal.submit") }}
        </button>
      </template>
    </ClinicalModal>

    <!-- Re-Consent Gate Blocking Modal Dialog -->
    <ClinicalModal
      v-model="showReconsentGateModal"
      :teleport="false"
      overlay-id="reconsent-gate-modal"
      dialog-class="modal"
      :title="`⚠️ ${$t('ecrf.gateModal.header')}`"
    >
      <p
        style="
          font-size: 0.95rem;
          color: var(--semantic-color-error-text-deep);
          font-weight: 600;
        "
      >
        {{ $t("ecrf.gateModal.subjectGated", { id: selectedSubjectId }) }}
      </p>
      <p
        style="font-size: 0.875rem; color: var(--semantic-color-text-secondary)"
      >
        {{ $t("ecrf.gateModal.desc") }}
      </p>
      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          @click="showReconsentGateModal = false"
        >
          {{ $t("ecrf.gateModal.close") }}
        </button>
        <button
          type="button"
          class="btn btn-primary"
          style="
            background-color: var(--semantic-color-error);
            color: var(--semantic-color-surface);
          "
          @click="
            showReconsentGateModal = false;
            $emit('open-econsent-modal');
          "
        >
          ✍️ {{ $t("ecrf.form.openEconsent") }}
        </button>
        <button
          type="button"
          class="btn btn-secondary"
          style="
            border: 1px solid var(--semantic-color-error);
            color: var(--semantic-color-error-text-dark);
          "
          @click="
            showReconsentGateModal = false;
            $emit('open-paper-icf-modal');
          "
        >
          📄 {{ $t("ecrf.form.uploadPaperIcf") }}
        </button>
      </template>
    </ClinicalModal>

    <!-- eConsent Signing Modal Dialog -->
    <SignatureCaptureModal
      v-if="showEconsentModal"
      :is-open="showEconsentModal"
      :username="econsentSignerName || selectedSubjectId"
      :signer-name="econsentSignerName || selectedSubjectId"
      signer-role="Subject"
      action-url="/api/v1/econsent/templates/icf-001/versions/2/capture-consent"
      semantic-action="econsent.consent.capture"
      :on-sign="
        (sigToken, signingReason) =>
          $emit('handle-complete-reconsent', 'ECONSENT', {
            sigToken,
            signingReason,
          })
      "
      @cancel="$emit('update:showEconsentModal', false)"
      @success="() => $emit('update:showEconsentModal', false)"
    />

    <!-- Paper ICF Upload Modal Dialog -->
    <ClinicalModal
      :open="showPaperIcfModal"
      :teleport="false"
      :dismissible="!reconsentSubmitting"
      overlay-id="paper-icf-modal"
      dialog-class="modal"
      title="Register Signed Paper ICF (v2.0.0)"
      @close="$emit('update:showPaperIcfModal', false)"
    >
      <p>
        Upload or register site-verified paper Informed Consent Form for Subject
        <strong>{{ selectedSubjectId }}</strong>.
      </p>
      <div class="form-group" style="margin-bottom: 12px">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label style="font-size: 0.85rem; font-weight: 600"
          >Date ICF Signed by Subject:</label
        >
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          :value="paperIcfDate"
          type="date"
          class="form-control"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            box-sizing: border-box;
          "
          @input="$emit('update:paperIcfDate', $event.target.value)"
        />
      </div>
      <div class="form-group" style="margin-bottom: 12px">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label style="font-size: 0.85rem; font-weight: 600"
          >Investigator Verification Note:</label
        >
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          :value="paperIcfNote"
          type="text"
          class="form-control"
          style="
            width: 100%;
            padding: 8px;
            border: 1px solid var(--border);
            border-radius: 4px;
            box-sizing: border-box;
          "
          placeholder="Paper ICF verified and archived in ISF binder."
          @input="$emit('update:paperIcfNote', $event.target.value)"
        />
      </div>
      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="reconsentSubmitting"
          @click="$emit('update:showPaperIcfModal', false)"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          style="
            background-color: var(--semantic-color-interactive-primary);
            color: var(--semantic-color-surface);
          "
          :disabled="reconsentSubmitting"
          @click="$emit('handle-complete-reconsent', 'PAPER_UPLOAD')"
        >
          {{ reconsentSubmitting ? "Uploading..." : "Verify & Unlock eCRF" }}
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { computed, reactive, ref } from "vue";
import { ClinicalFormField, ClinicalModal } from "ui";
import { isScreenAvailable } from "../../router/availability.js";
import { useClinicalStore } from "../../stores/clinical";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

const clinicalStore = useClinicalStore();
const trainingAvailable = computed(() => isScreenAvailable("training"));

const props = defineProps({
  store: { type: Object, required: true },
  selectedSubjectId: { type: String, required: true },
  selectedVisitId: { type: String, required: true },
  isReconsentGated: { type: Boolean, required: true },
  isTrainingGated: { type: Boolean, default: false },
  showEconsentModal: { type: Boolean, required: true },

  showPaperIcfModal: { type: Boolean, required: true },
  reconsentSubmitting: { type: Boolean, required: true },
  econsentSignerName: { type: String, required: true },
  paperIcfDate: { type: String, required: true },
  paperIcfNote: { type: String, required: true },
  lookupStatuses: { type: Object, required: true },
  getValidationError: { type: Function, required: true },
  isCraUser: { type: Boolean, required: true },
  sdvStates: { type: Object, required: true },
  getSdvKey: { type: Function, required: true },
  dirtyFields: { type: Object, default: () => ({}) },
  isDirty: { type: Boolean, default: false },
  draftRestored: { type: Boolean, default: false },
});

const emit = defineEmits([
  "update:selectedSubjectId",
  "update:selectedVisitId",
  "update:showEconsentModal",
  "update:showPaperIcfModal",
  "update:econsentSignerName",
  "update:paperIcfDate",
  "update:paperIcfNote",
  "load-ecrf-session",
  "open-econsent-modal",
  "open-paper-icf-modal",
  "handle-complete-reconsent",
  "handle-lookup-input",
  "handle-field-change",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
  "handle-sdv-toggle",
  "clear-form",
  "submit-ecrf",
  "enroll-subject",
]);

// Subject Enrollment Modal State
const showEnrollModal = ref(false);
const showReconsentGateModal = ref(false);
const enrollForm = reactive({
  subjectId: "",
  siteId: "SITE-101",
  consentDate: new Date().toISOString().split("T")[0],
  armId: "ARM-A",
  changeReason: "Initial subject enrollment",
});

// Dynamic Active Subject & Protocol Schema Projection
const currentStore = computed(() => props.store || clinicalStore);

const activeSubject = computed(() => {
  const list = currentStore.value?.subjects || [];
  return list.find((s) => s.id === props.selectedSubjectId);
});

const activeSubjectVersion = computed(() => {
  return activeSubject.value?.active_protocol_version || "1.0.0";
});

const renderedFields = computed(() => {
  if (typeof currentStore.value?.getEcrfFieldsForVersion === "function") {
    return currentStore.value.getEcrfFieldsForVersion(
      activeSubjectVersion.value
    );
  }
  return currentStore.value?.ecrfFields || [];
});

// Real-Time Edit Check Rule Evaluation
const activeEditCheckAlerts = computed(() => {
  const alerts = [];
  const vals = currentStore.value?.formValues || {};

  const vssbp = parseFloat(vals.vssbp);
  const vsdpb = parseFloat(vals.vsdpb);
  const pulse = parseFloat(vals.pulse);
  const wbc = parseFloat(vals.lb_wbc || vals.wbc);
  const gluc = parseFloat(vals.lb_gluc || vals.gluc);

  // 1. Cross-field Blood Pressure Discrepancy (Diastolic >= Systolic)
  if (!isNaN(vssbp) && !isNaN(vsdpb) && vsdpb >= vssbp) {
    alerts.push({
      id: "EC_VSDPB_VSSBP",
      fieldId: "vsdpb",
      severity: "DISCREPANCY",
      message: `Diastolic BP (${vsdpb} mmHg) cannot equal or exceed Systolic BP (${vssbp} mmHg).`,
    });
  }

  // 2. Severe Hypertension Alert (Systolic > 180 mmHg)
  if (!isNaN(vssbp) && vssbp > 180) {
    alerts.push({
      id: "EC_VSSBP_HIGH",
      fieldId: "vssbp",
      severity: "WARNING",
      message: `Systolic BP (${vssbp} mmHg) exceeds 180 mmHg threshold (Stage 3 crisis alert).`,
    });
  }

  // 3. Tachycardia Alert (Pulse > 100 bpm)
  if (!isNaN(pulse) && pulse > 100) {
    alerts.push({
      id: "EC_VSHR_HIGH",
      fieldId: "pulse",
      severity: "WARNING",
      message: `Tachycardia warning: Pulse Rate (${pulse} bpm) exceeds 100 bpm.`,
    });
  }

  // 4. Bradycardia Alert (Pulse < 40 bpm)
  if (!isNaN(pulse) && pulse > 0 && pulse < 40) {
    alerts.push({
      id: "EC_VSHR_LOW",
      fieldId: "pulse",
      severity: "WARNING",
      message: `Bradycardia warning: Pulse Rate (${pulse} bpm) is below 40 bpm.`,
    });
  }

  // 5. White Blood Cell Reference Range Alert
  if (!isNaN(wbc) && (wbc < 4.0 || wbc > 11.0)) {
    alerts.push({
      id: "EC_LB_WBC",
      fieldId: "lb_wbc",
      severity: "WARNING",
      message: `Lab Alert: WBC (${wbc} 10^9/L) is outside normal clinical reference range (4.0 - 11.0 10^9/L).`,
    });
  }

  // 6. Fasting Glucose Reference Range Alert
  if (!isNaN(gluc) && gluc > 126.0) {
    alerts.push({
      id: "EC_LB_GLUC",
      fieldId: "lb_gluc",
      severity: "WARNING",
      message: `Lab Alert: Fasting Glucose (${gluc} mg/dL) exceeds normal threshold (>126 mg/dL indicative of hyperglycemia).`,
    });
  }

  return alerts;
});

const discrepancyAlertsCount = computed(() => {
  return activeEditCheckAlerts.value.filter((a) => a.severity === "DISCREPANCY")
    .length;
});

const warningAlertsCount = computed(() => {
  return activeEditCheckAlerts.value.filter((a) => a.severity === "WARNING")
    .length;
});

function getFieldAlerts(fieldId) {
  return activeEditCheckAlerts.value.filter((a) => a.fieldId === fieldId);
}

function raiseQueryForAlert(fieldId, message) {
  emit("create-query", fieldId, message);
}

function handleFieldsetInteraction() {
  if (props.isReconsentGated) {
    showReconsentGateModal.value = true;
  }
}

function handleSubmitClick() {
  if (props.isReconsentGated) {
    showReconsentGateModal.value = true;
    return;
  }
  emit("submit-ecrf");
}

async function handleEnrollSubmit() {
  if (!enrollForm.subjectId.trim()) return;

  const storeInstance = props.store || clinicalStore;
  if (typeof storeInstance.enrollSubject === "function") {
    const enrolled = await storeInstance.enrollSubject({
      id: enrollForm.subjectId.trim(),
      siteId: enrollForm.siteId,
      consentDate: enrollForm.consentDate,
      armId: enrollForm.armId,
      protocolVersion: "1.0.0",
      reason: enrollForm.changeReason,
    });
    // Refused (no backend yet, #4827): keep the dialog open and report nothing as enrolled.
    if (!enrolled) return;
  }

  emit("enroll-subject", { ...enrollForm });
  emit("update:selectedSubjectId", enrollForm.subjectId.trim());
  emit("load-ecrf-session");

  showEnrollModal.value = false;
  enrollForm.subjectId = "";
}

function updateFieldValue(fieldId, val) {
  const storeInstance = props.store || clinicalStore;
  storeInstance.formValues[fieldId] = val;
}
</script>

<style scoped>
#form-VS_DEMO :deep(.clinical-form-field-wrapper) {
  content-visibility: visible !important;
}
</style>
