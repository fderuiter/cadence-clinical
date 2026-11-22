<template>
  <section
    id="study-validation-section"
    class="study-validation"
    aria-labelledby="study-validation-title"
    :aria-busy="loading"
  >
    <header class="validation-header">
      <div>
        <h2 id="study-validation-title">Validation &amp; edit checks</h2>
        <p class="validation-subtitle">
          Governed edit-check rule packs, in-memory study previews, active study
          pins, and post-commit validation findings for {{ studyCode }}.
        </p>
      </div>
      <div class="header-actions">
        <button
          v-if="canManageRules && studyRef"
          id="btn-open-rule-composer"
          type="button"
          class="btn btn-primary"
          :disabled="loading || busy"
          @click="openNewRuleComposer"
        >
          Compose rule pack
        </button>
        <button
          id="btn-refresh-validation"
          type="button"
          class="btn btn-secondary"
          :disabled="loading || busy"
          @click="refreshAll"
        >
          Refresh
        </button>
      </div>
    </header>

    <p v-if="loading && !studyRef" class="status-line" role="status">
      Looking up {{ studyCode }}…
    </p>
    <p
      v-else-if="errorMessage"
      id="study-validation-error"
      class="banner banner-error"
      role="alert"
    >
      {{ errorMessage }}
    </p>
    <p v-else-if="!studyRef" id="study-validation-no-study" class="status-line">
      Study {{ studyCode }} wasn't found or isn't visible to your account, so
      its validation rules and findings cannot be listed here.
    </p>

    <template v-else>
      <div
        v-if="actionMessage"
        id="study-validation-outcome"
        data-testid="study-validation-outcome"
        class="banner banner-success"
        role="status"
      >
        {{ actionMessage }}
      </div>

      <section
        v-if="composerOpen && canManageRules"
        id="rule-pack-composer"
        data-testid="rule-pack-composer"
        class="validation-card composer-card"
        aria-labelledby="rule-pack-composer-heading"
      >
        <div class="card-header-row">
          <div>
            <h3 id="rule-pack-composer-heading">
              {{
                composer.revisingVersion !== null
                  ? `Revise rule pack ${composer.key} (from v${composer.revisingVersion})`
                  : "Draft new study rule pack"
              }}
            </h3>
            <p class="validation-subtitle">
              Collection rules never block clinical fact writes (K-FIND-3) and
              must cite public regulatory, standards, sponsor, or study
              documents (K-RULE-2).
            </p>
          </div>
          <button
            id="btn-cancel-rule-composer"
            type="button"
            class="btn btn-secondary"
            :disabled="busy"
            @click="closeComposer"
          >
            Cancel
          </button>
        </div>

        <p
          v-if="composerError"
          id="composer-validation-error"
          data-testid="composer-validation-error"
          class="banner banner-error"
          role="alert"
        >
          {{ composerError }}
        </p>

        <div class="composer-meta-grid">
          <label for="composer-pack-key">
            Pack key
            <input
              id="composer-pack-key"
              v-model="composer.key"
              type="text"
              autocomplete="off"
              :readonly="composer.revisingVersion !== null"
              placeholder="cadence-101-study-checks"
              :disabled="busy"
            />
          </label>
          <label for="composer-pack-title">
            Pack title
            <input
              id="composer-pack-title"
              v-model="composer.title"
              type="text"
              autocomplete="off"
              placeholder="Study Collection Edit Checks"
              :disabled="busy"
            />
          </label>
          <label for="composer-pack-authority">
            Authority
            <select
              id="composer-pack-authority"
              v-model="composer.authority"
              :disabled="busy"
            >
              <option
                v-for="auth in RULE_AUTHORITIES"
                :key="auth"
                :value="auth"
              >
                {{ auth }}
              </option>
            </select>
          </label>
        </div>

        <datalist id="composer-study-forms-list">
          <option v-for="form in studyForms" :key="form.id" :value="form.id">
            {{ form.name }}
          </option>
        </datalist>

        <div class="composer-rules-stack">
          <fieldset
            v-for="(row, idx) in composer.rules"
            :key="row.localId"
            class="composer-rule-row"
            :data-testid="`composer-rule-row-${idx}`"
          >
            <legend>Rule {{ idx + 1 }}</legend>

            <div class="composer-rule-grid">
              <label :for="`composer-rule-dataset-${idx}`">
                Collection form / dataset
                <select
                  v-if="studyForms.length > 0"
                  :id="`composer-rule-dataset-select-${idx}`"
                  :value="
                    studyForms.some((f) => f.id === row.dataset)
                      ? row.dataset
                      : ''
                  "
                  :disabled="busy"
                  @change="onSelectRowForm(row, $event)"
                >
                  <option value="">All / custom dataset…</option>
                  <option
                    v-for="form in studyForms"
                    :key="form.id"
                    :value="form.id"
                  >
                    {{ form.id }} — {{ form.name }}
                  </option>
                </select>
                <input
                  :id="`composer-rule-dataset-${idx}`"
                  v-model="row.dataset"
                  type="text"
                  list="composer-study-forms-list"
                  autocomplete="off"
                  placeholder="VS"
                  :disabled="busy"
                />
              </label>

              <label
                v-if="row.template !== 'custom'"
                :for="`composer-rule-item-${idx}`"
              >
                Collection item
                <select
                  v-if="itemsForDataset(row.dataset).length > 0"
                  :id="`composer-rule-item-select-${idx}`"
                  :value="
                    itemsForDataset(row.dataset).some((i) => i.id === row.item)
                      ? row.item
                      : ''
                  "
                  :disabled="busy"
                  @change="onSelectRowItem(row, $event)"
                >
                  <option value="">Select form item…</option>
                  <option
                    v-for="item in itemsForDataset(row.dataset)"
                    :key="item.id"
                    :value="item.id"
                  >
                    {{ item.id }} — {{ item.prompt }} ({{ item.value_kind }})
                  </option>
                </select>
                <input
                  :id="`composer-rule-item-${idx}`"
                  v-model="row.item"
                  type="text"
                  autocomplete="off"
                  placeholder="SYSBP"
                  :disabled="busy"
                />
              </label>

              <label :for="`composer-rule-id-${idx}`">
                Rule ID
                <input
                  :id="`composer-rule-id-${idx}`"
                  v-model="row.id"
                  type="text"
                  autocomplete="off"
                  placeholder="VS_SYSBP_RANGE"
                  :disabled="busy"
                />
              </label>

              <label :for="`composer-rule-severity-${idx}`">
                Severity
                <select
                  :id="`composer-rule-severity-${idx}`"
                  v-model="row.severity"
                  :disabled="busy"
                >
                  <option value="Warning">Warning</option>
                  <option value="Error">Error</option>
                  <option value="Notice">Notice</option>
                </select>
              </label>

              <label :for="`composer-rule-template-${idx}`">
                Check condition
                <select
                  :id="`composer-rule-template-${idx}`"
                  v-model="row.template"
                  :disabled="busy"
                >
                  <option value="missing">Missing / empty value</option>
                  <option value="comparison">Value comparison</option>
                  <option value="range">Outside numeric range</option>
                  <option value="cross_item">Cross-item comparison</option>
                  <option value="not_in">Outside permitted values</option>
                  <option value="max_length">Exceeds max text length</option>
                  <option value="custom">Custom JSON expression</option>
                </select>
              </label>
            </div>

            <div
              v-if="
                row.template === 'comparison' || row.template === 'cross_item'
              "
              class="composer-condition-params"
            >
              <label :for="`composer-rule-op-${idx}`">
                Violates when item is
                <select
                  :id="`composer-rule-op-${idx}`"
                  v-model="row.operator"
                  :disabled="busy"
                >
                  <option value=">">&gt; (greater than)</option>
                  <option value=">=">&gt;= (greater or equal)</option>
                  <option value="<">&lt; (less than)</option>
                  <option value="<=">&lt;= (less or equal)</option>
                  <option value="==">== (equal to)</option>
                  <option value="!=">!= (not equal to)</option>
                </select>
              </label>
              <label
                v-if="row.template === 'comparison'"
                :for="`composer-rule-threshold-${idx}`"
              >
                Threshold / value
                <input
                  :id="`composer-rule-threshold-${idx}`"
                  v-model="row.threshold"
                  type="text"
                  autocomplete="off"
                  placeholder="220"
                  :disabled="busy"
                />
              </label>
              <label v-else :for="`composer-rule-other-item-${idx}`">
                Compared item ID
                <input
                  :id="`composer-rule-other-item-${idx}`"
                  v-model="row.otherItem"
                  type="text"
                  autocomplete="off"
                  placeholder="DIABP"
                  :disabled="busy"
                />
              </label>
            </div>

            <div
              v-else-if="row.template === 'range'"
              class="composer-condition-params"
            >
              <label :for="`composer-rule-min-${idx}`">
                Minimum inclusive
                <input
                  :id="`composer-rule-min-${idx}`"
                  v-model="row.minValue"
                  type="number"
                  step="any"
                  placeholder="70"
                  :disabled="busy"
                />
              </label>
              <label :for="`composer-rule-max-${idx}`">
                Maximum inclusive
                <input
                  :id="`composer-rule-max-${idx}`"
                  v-model="row.maxValue"
                  type="number"
                  step="any"
                  placeholder="220"
                  :disabled="busy"
                />
              </label>
            </div>

            <div
              v-else-if="row.template === 'not_in'"
              class="composer-condition-params"
            >
              <label :for="`composer-rule-allowed-${idx}`">
                Permitted values (comma-separated)
                <input
                  :id="`composer-rule-allowed-${idx}`"
                  v-model="row.allowedValues"
                  type="text"
                  autocomplete="off"
                  placeholder="M, F, U"
                  :disabled="busy"
                />
              </label>
            </div>

            <div
              v-else-if="row.template === 'max_length'"
              class="composer-condition-params"
            >
              <label :for="`composer-rule-maxlen-${idx}`">
                Maximum character length
                <input
                  :id="`composer-rule-maxlen-${idx}`"
                  v-model="row.maxLength"
                  type="number"
                  min="1"
                  step="1"
                  placeholder="200"
                  :disabled="busy"
                />
              </label>
            </div>

            <div
              v-else-if="row.template === 'custom'"
              class="composer-condition-params"
            >
              <label
                class="full-width-label"
                :for="`composer-rule-custom-${idx}`"
              >
                Canonical condition JSON
                <textarea
                  :id="`composer-rule-custom-${idx}`"
                  v-model="row.customConditionJson"
                  rows="2"
                  :disabled="busy"
                ></textarea>
              </label>
            </div>

            <div class="composer-rule-grid">
              <label :for="`composer-rule-explanation-${idx}`">
                Discrepancy explanation
                <input
                  :id="`composer-rule-explanation-${idx}`"
                  v-model="row.explanation"
                  type="text"
                  autocomplete="off"
                  placeholder="Systolic BP outside physiological range [70, 220] mm[Hg]."
                  :disabled="busy"
                />
              </label>
              <label :for="`composer-rule-remediation-${idx}`">
                Remediation guidance (optional)
                <input
                  :id="`composer-rule-remediation-${idx}`"
                  v-model="row.remediation"
                  type="text"
                  autocomplete="off"
                  placeholder="Verify measurement against source vital-signs chart."
                  :disabled="busy"
                />
              </label>
            </div>

            <div class="composer-rule-grid">
              <label :for="`composer-rule-citation-doc-${idx}`">
                Public citation document (K-RULE-2)
                <input
                  :id="`composer-rule-citation-doc-${idx}`"
                  v-model="row.citationDocument"
                  type="text"
                  autocomplete="off"
                  placeholder="Study Data Validation Specification"
                  :disabled="busy"
                />
              </label>
              <label :for="`composer-rule-citation-section-${idx}`">
                Citation section (optional)
                <input
                  :id="`composer-rule-citation-section-${idx}`"
                  v-model="row.citationSection"
                  type="text"
                  autocomplete="off"
                  placeholder="VS.SYSBP"
                  :disabled="busy"
                />
              </label>
              <label :for="`composer-rule-citation-uri-${idx}`">
                Citation URI (optional https://)
                <input
                  :id="`composer-rule-citation-uri-${idx}`"
                  v-model="row.citationUri"
                  type="url"
                  autocomplete="off"
                  placeholder="https://www.cdisc.org/standards"
                  :disabled="busy"
                />
              </label>
            </div>

            <div v-if="composer.rules.length > 1" class="composer-row-footer">
              <button
                :id="`btn-remove-rule-${idx}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="removeComposerRule(idx)"
              >
                Remove rule
              </button>
            </div>
          </fieldset>
        </div>

        <div class="composer-footer">
          <button
            id="btn-composer-add-rule"
            type="button"
            class="btn btn-secondary"
            :disabled="busy"
            @click="addComposerRule"
          >
            + Add rule
          </button>

          <label class="composer-reason-label" for="composer-change-reason">
            Reason for change
            <input
              id="composer-change-reason"
              v-model="composer.reason"
              type="text"
              autocomplete="off"
              placeholder="Author study collection edit-check draft"
              :disabled="busy"
            />
          </label>

          <button
            id="btn-submit-rule-pack"
            type="button"
            class="btn btn-primary"
            :disabled="busy"
            @click="submitRulePackDraft"
          >
            {{
              composer.revisingVersion !== null
                ? "Save revised draft"
                : "Draft rule pack"
            }}
          </button>
        </div>
      </section>

      <section
        id="study-rule-packs"
        class="validation-card"
        aria-labelledby="study-rule-packs-heading"
      >
        <div class="card-header-row">
          <h3 id="study-rule-packs-heading">
            Rule packs ({{ rulePacks.length }})
          </h3>
          <span class="pinned-summary" data-testid="pinned-rule-packs-count">
            {{ pinnedRefs.length }} pinned on {{ studyCode }}
          </span>
        </div>

        <p v-if="rulePacks.length === 0" data-testid="no-rule-packs">
          No edit-check rule packs are registered in this tenant yet. Compose a
          rule pack above or import an ODM XML, USDM v4 JSON, or CDISC Library
          protocol template in Design → Protocol versions to draft one
          automatically.
        </p>

        <div v-else class="rule-pack-list">
          <article
            v-for="pack in rulePacks"
            :key="pack.ref"
            :data-testid="`rule-pack-${pack.key}-v${pack.version}`"
            class="rule-pack-item"
          >
            <div class="rule-pack-top">
              <div>
                <strong class="rule-pack-title">{{ pack.title }}</strong>
                <div class="rule-pack-meta">
                  <code>{{ pack.key }}</code> · v{{ pack.version }} ·
                  {{ pack.rule_count }}
                  {{ Number(pack.rule_count) === 1 ? "rule" : "rules" }}
                </div>
              </div>
              <div class="rule-pack-badges">
                <span
                  class="status-pill"
                  :data-status="pack.status.toLowerCase()"
                >
                  {{ pack.status }}
                </span>
                <span
                  v-if="pack.pinned"
                  class="status-pill status-pill--pinned"
                  data-testid="rule-pack-pinned-badge"
                >
                  Pinned on study
                </span>
              </div>
            </div>

            <div class="rule-pack-actions">
              <button
                :id="`btn-toggle-rules-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-secondary"
                @click="toggleExpanded(pack.ref)"
              >
                {{ expandedRef === pack.ref ? "Hide rules" : "Inspect rules" }}
              </button>
              <button
                :id="`btn-preview-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="runPackPreview(pack)"
              >
                Preview on study
              </button>
              <button
                v-if="canManageRules"
                :id="`btn-revise-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="openReviseRuleComposer(pack)"
              >
                Revise rule pack
              </button>
              <button
                v-if="canManageRules && pack.status === 'Draft'"
                :id="`btn-approve-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="openApprovePackModal(pack)"
              >
                Approve rule pack
              </button>
              <button
                v-if="canManageRules && pack.status === 'Approved'"
                :id="`btn-pin-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy"
                @click="beginPinToggle(pack)"
              >
                {{ pack.pinned ? "Unpin from study" : "Pin on study" }}
              </button>
              <button
                v-if="canManageRules && pack.status === 'Approved'"
                :id="`btn-deprecate-pack-${pack.key}-${pack.version}`"
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="openDeprecatePackModal(pack)"
              >
                Deprecate
              </button>
            </div>

            <div
              v-if="pinningPack?.ref === pack.ref"
              class="pin-reason-row"
              data-testid="pin-reason-form"
            >
              <label for="validation-pin-reason-input">
                Reason for {{ pack.pinned ? "unpinning" : "pinning" }}
                {{ pack.key }}
                <input
                  id="validation-pin-reason-input"
                  v-model="pinReason"
                  type="text"
                  autocomplete="off"
                  placeholder="Activate approved edit checks for study collection"
                  :disabled="busy"
                />
              </label>
              <button
                :id="`btn-confirm-pin-${pack.key}`"
                type="button"
                class="btn btn-primary"
                :disabled="busy || !pinReason.trim()"
                @click="confirmPinToggle(pack)"
              >
                Confirm
              </button>
              <button
                type="button"
                class="btn btn-secondary"
                :disabled="busy"
                @click="pinningPack = null"
              >
                Cancel
              </button>
            </div>

            <ul
              v-if="expandedRef === pack.ref && pack.rules.length > 0"
              class="compiled-rules-list"
              :data-testid="`compiled-rules-${pack.key}`"
            >
              <li v-for="rule in pack.rules" :key="rule.id">
                <code>{{ rule.id }}</code>
                <span class="severity-tag">[{{ rule.severity }}]</span>
                <span>{{ rule.explanation }}</span>
                <div class="rule-expr">
                  <code v-if="rule.dataset">dataset: {{ rule.dataset }}</code>
                  <code v-if="rule.condition"
                    >condition: {{ rule.condition }}</code
                  >
                  <span v-if="rule.citation_document">
                    cite: {{ rule.citation_document }}
                  </span>
                </div>
              </li>
            </ul>

            <div
              v-if="previewByRef[pack.ref]"
              class="pack-preview-box"
              :data-testid="`pack-preview-${pack.key}`"
            >
              <strong>
                In-memory study preview ({{
                  previewByRef[pack.ref].forms_evaluated
                }}
                forms, {{ previewByRef[pack.ref].rules_evaluated }} rules):
              </strong>
              <span v-if="previewByRef[pack.ref].violations.length === 0">
                0 discrepancies across current study forms.
              </span>
              <ul v-else>
                <li
                  v-for="(v, idx) in previewByRef[pack.ref].violations"
                  :key="`${v.form_id}-${v.rule_id}-${idx}`"
                >
                  <strong>[{{ v.severity }}] {{ v.rule_id }}</strong>
                  <template v-if="v.dataset">
                    on <code>{{ v.dataset }}</code>
                  </template>
                  <template v-if="v.item_id">
                    · <code>{{ v.item_id }}</code> </template
                  >: {{ v.message }}
                </li>
              </ul>
            </div>
          </article>
        </div>
      </section>

      <section
        id="study-validation-findings"
        class="validation-card"
        aria-labelledby="study-findings-heading"
      >
        <div class="card-header-row">
          <h3 id="study-findings-heading">
            Validation findings ({{ findings.length }})
          </h3>
          <div class="findings-filters">
            <label for="validation-status-filter">
              Status
              <select
                id="validation-status-filter"
                v-model="statusFilter"
                @change="loadFindings"
              >
                <option value="">All statuses</option>
                <option value="Open">Open</option>
                <option value="Investigating">Investigating</option>
                <option value="Accepted">Accepted</option>
                <option value="Explained">Explained</option>
                <option value="Resolved">Resolved</option>
              </select>
            </label>
            <label for="validation-severity-filter">
              Severity
              <select
                id="validation-severity-filter"
                v-model="severityFilter"
                @change="loadFindings"
              >
                <option value="">All severities</option>
                <option value="Error">Error</option>
                <option value="Warning">Warning</option>
                <option value="Notice">Notice</option>
              </select>
            </label>
          </div>
        </div>

        <div
          v-if="effectiveCanRunValidation"
          class="validation-run-bar"
          data-testid="validation-run-bar"
        >
          <label for="validation-run-reason">
            Validation run reason
            <input
              id="validation-run-reason"
              v-model="validationRunReason"
              type="text"
              autocomplete="off"
              placeholder="Run post-commit collection edit checks"
              :disabled="busy"
            />
          </label>
          <button
            id="btn-run-study-validation"
            type="button"
            class="btn btn-primary"
            :disabled="busy || !validationRunReason.trim()"
            @click="executeStudyValidationRun"
          >
            Run study validation
          </button>
        </div>

        <p v-if="findings.length === 0" data-testid="no-validation-findings">
          No validation findings match the current filter.
        </p>

        <ul v-else class="findings-list" data-testid="validation-findings-list">
          <li
            v-for="finding in findings"
            :key="finding.ref"
            :data-testid="`finding-${finding.rule_id}-${finding.status}`"
            class="finding-item"
          >
            <div class="finding-head">
              <span
                class="status-pill"
                :data-status="finding.status.toLowerCase()"
              >
                {{ finding.status }}
              </span>
              <span
                v-if="finding.classification"
                class="status-pill"
                data-testid="finding-classification"
              >
                {{ finding.classification }}
              </span>
              <span class="severity-tag">[{{ finding.severity }}]</span>
              <code>{{ finding.rule_id }}</code>
              <span>{{ finding.message }}</span>
            </div>

            <p
              v-if="finding.decision_text"
              class="finding-decision-note"
              data-testid="finding-decision-text"
            >
              Decision note: {{ finding.decision_text }}
            </p>

            <div class="finding-actions">
              <router-link
                v-if="findingFormLocation(finding)"
                class="form-link"
                :to="findingFormLocationOrDefault(finding)"
              >
                Open {{ finding.variable ?? "form" }}
              </router-link>
              <button
                type="button"
                class="btn btn-secondary"
                data-testid="btn-inspect-finding"
                @click="inspectFinding(finding.ref)"
              >
                {{
                  selectedFinding?.finding.ref === finding.ref
                    ? "Hide explanation"
                    : "Inspect explanation"
                }}
              </button>

              <template
                v-if="
                  effectiveCanTriageFindings && finding.status !== 'Resolved'
                "
              >
                <button
                  v-if="finding.status === 'Open'"
                  type="button"
                  class="btn btn-secondary"
                  data-testid="btn-investigate-finding"
                  :disabled="busy"
                  @click="beginFindingTriage(finding, 'investigate')"
                >
                  Investigate
                </button>
                <button
                  type="button"
                  class="btn btn-secondary"
                  data-testid="btn-classify-finding"
                  :disabled="busy"
                  @click="beginFindingTriage(finding, 'classify')"
                >
                  Classify
                </button>
                <button
                  v-if="
                    finding.status === 'Open' ||
                    finding.status === 'Investigating'
                  "
                  type="button"
                  class="btn btn-primary"
                  data-testid="btn-accept-finding"
                  :disabled="busy"
                  @click="beginFindingTriage(finding, 'accept')"
                >
                  Accept (Sign)
                </button>
                <button
                  v-if="
                    finding.status === 'Open' ||
                    finding.status === 'Investigating'
                  "
                  type="button"
                  class="btn btn-primary"
                  data-testid="btn-explain-finding"
                  :disabled="busy"
                  @click="beginFindingTriage(finding, 'explain')"
                >
                  Mark Explained (Sign)
                </button>
                <button
                  v-if="
                    finding.status === 'Investigating' ||
                    finding.status === 'Accepted' ||
                    finding.status === 'Explained'
                  "
                  type="button"
                  class="btn btn-secondary"
                  data-testid="btn-reopen-finding"
                  :disabled="busy"
                  @click="beginFindingTriage(finding, 'reopen')"
                >
                  Reopen
                </button>
              </template>
            </div>

            <div
              v-if="activeTriage?.finding.ref === finding.ref"
              class="triage-panel"
              data-testid="finding-triage-panel"
            >
              <template v-if="activeTriage.mode === 'classify'">
                <label>
                  Classification
                  <select
                    v-model="triageClassification"
                    data-testid="triage-classification-select"
                    :disabled="busy"
                  >
                    <option
                      v-for="cls in FINDING_CLASSIFICATIONS"
                      :key="cls"
                      :value="cls"
                    >
                      {{ cls }}
                    </option>
                  </select>
                </label>
              </template>

              <label
                v-if="
                  activeTriage.mode === 'accept' ||
                  activeTriage.mode === 'explain'
                "
              >
                {{
                  activeTriage.mode === "accept"
                    ? "Signed acceptance rationale"
                    : "Signed clinical explanation"
                }}
                <input
                  v-model="triageDecisionText"
                  type="text"
                  autocomplete="off"
                  data-testid="triage-decision-text-input"
                  :placeholder="
                    activeTriage.mode === 'accept'
                      ? 'Documented baseline hypertension confirmed in source'
                      : 'Transient reading explained by white-coat elevation'
                  "
                  :disabled="busy"
                />
              </label>

              <label>
                Reason for change
                <input
                  v-model="triageReason"
                  type="text"
                  autocomplete="off"
                  data-testid="triage-reason-input"
                  placeholder="Record validation finding triage decision"
                  :disabled="busy"
                />
              </label>

              <div class="triage-panel-actions">
                <button
                  type="button"
                  class="btn btn-primary"
                  data-testid="btn-confirm-triage"
                  :disabled="busy || !canConfirmActiveTriage"
                  @click="confirmFindingTriage"
                >
                  {{
                    activeTriage.mode === "accept"
                      ? "Sign & Accept"
                      : activeTriage.mode === "explain"
                        ? "Sign & Explain"
                        : "Confirm"
                  }}
                </button>
                <button
                  type="button"
                  class="btn btn-secondary"
                  :disabled="busy"
                  @click="cancelFindingTriage"
                >
                  Cancel
                </button>
              </div>
            </div>

            <div
              v-if="selectedFinding?.finding.ref === finding.ref"
              class="finding-explanation"
              data-testid="finding-explanation"
            >
              <p>
                <strong>{{ selectedFinding.explanation.claim }}</strong>
              </p>
              <ul v-if="selectedFinding.explanation.steps.length > 0">
                <li
                  v-for="(step, idx) in selectedFinding.explanation.steps"
                  :key="idx"
                >
                  {{ step.claim }}
                </li>
              </ul>
            </div>
          </li>
        </ul>
      </section>
    </template>

    <SignatureCaptureModal
      v-if="signatureModalConfig"
      :is-open="Boolean(signatureModalConfig)"
      :username="signerUsername"
      :signer-name="signerFullName"
      :signer-role="signatureModalConfig.signerRole"
      :reasons="signatureModalConfig.reasons"
      :action-url="signatureModalConfig.actionUrl"
      :semantic-action="signatureModalConfig.semanticAction"
      :batch-id="signatureModalConfig.batchId"
      :on-sign="signatureModalConfig.onSign"
      @cancel="closeSignatureModal"
      @success="closeSignatureModal"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  describeKernelError,
  FINDING_ACCEPT_SEMANTIC_ACTION,
  FINDING_EXPLAIN_SEMANTIC_ACTION,
  findingAcceptAction,
  findingExplainAction,
  kernelApi,
  RULE_PACK_APPROVE_SEMANTIC_ACTION,
  RULE_PACK_DEPRECATE_SEMANTIC_ACTION,
  rulePackApproveAction,
  rulePackDeprecateAction,
  type KernelFindingClassification,
  type KernelProtocolFormItem,
  type KernelProtocolFormSummary,
  type KernelRulePack,
  type KernelRulePackAuthority,
  type KernelRulePackPreview,
  type KernelRuleSummary,
  type KernelValidationFinding,
  type KernelValidationFindingDetail,
} from "../../api/kernelApi";
import {
  canRunValidation as canRunValidationFromAuth,
  canTriageFindings as canTriageFindingsFromAuth,
} from "../../canvas/authority";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { collectionInstanceRef, parseNodeRef } from "../../canvas/nodeRef";
import { useAuthStore } from "../../stores/auth";
import { findingDecisionHash } from "../../utils/signatureBinding";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";

const props = withDefaults(
  defineProps<{
    studyCode: string;
    canManageRules: boolean;
    canRunValidation?: boolean;
    canTriageFindings?: boolean;
  }>(),
  {
    canRunValidation: undefined,
    canTriageFindings: undefined,
  }
);

const authStore = useAuthStore();
const effectiveCanRunValidation = computed(
  () => props.canRunValidation ?? canRunValidationFromAuth(authStore)
);
const effectiveCanTriageFindings = computed(
  () => props.canTriageFindings ?? canTriageFindingsFromAuth(authStore)
);

const RULE_AUTHORITIES: KernelRulePackAuthority[] = [
  "Study",
  "Sponsor",
  "CDISC",
  "FDA",
  "EMA",
  "PMDA",
  "NMPA",
];

const FINDING_CLASSIFICATIONS: KernelFindingClassification[] = [
  "DataIssue",
  "MappingIssue",
  "MetadataIssue",
  "RuleIssue",
  "NotAnIssue",
];

const FORBIDDEN_VALIDATOR_CITATIONS = [
  "pinnacle 21",
  "pinnacle21",
  "opencdisc",
  "p21 validator",
];

const RULE_ID_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/;

type RuleTemplateKind =
  | "missing"
  | "comparison"
  | "range"
  | "cross_item"
  | "not_in"
  | "max_length"
  | "custom";

type ComparisonOperator = ">" | ">=" | "<" | "<=" | "==" | "!=";

interface ComposerRuleRow {
  localId: string;
  id: string;
  version: number;
  dataset: string;
  item: string;
  severity: "Error" | "Warning" | "Notice";
  template: RuleTemplateKind;
  operator: ComparisonOperator;
  threshold: string;
  otherItem: string;
  minValue: string;
  maxValue: string;
  allowedValues: string;
  maxLength: string;
  customConditionJson: string;
  explanation: string;
  remediation: string;
  citationDocument: string;
  citationSection: string;
  citationUri: string;
}

type FindingTriageMode =
  "investigate" | "classify" | "accept" | "explain" | "reopen";

type PendingSignatureTarget =
  | { kind: "approvePack"; pack: KernelRulePack }
  | { kind: "deprecatePack"; pack: KernelRulePack }
  | {
      kind: "acceptFinding";
      finding: KernelValidationFinding;
      rationale: string;
      reason: string;
      batchId: string;
    }
  | {
      kind: "explainFinding";
      finding: KernelValidationFinding;
      explanation: string;
      reason: string;
      batchId: string;
    };

const studyRef = ref<string | null>(null);
const loading = ref(false);
const busy = ref(false);
const errorMessage = ref("");
const actionMessage = ref("");

const rulePacks = ref<KernelRulePack[]>([]);
const pinnedRefs = ref<string[]>([]);
const findings = ref<KernelValidationFinding[]>([]);
const studyForms = ref<KernelProtocolFormSummary[]>([]);
const loadedFormsForStudyRef = ref<string | null>(null);

const statusFilter = ref("");
const severityFilter = ref("");
const expandedRef = ref<string | null>(null);
const previewByRef = reactive<Record<string, KernelRulePackPreview>>({});
const selectedFinding = ref<KernelValidationFindingDetail | null>(null);

const pinningPack = ref<KernelRulePack | null>(null);
const pinReason = ref("");

const validationRunReason = ref("Run post-commit collection edit checks");

const composerOpen = ref(false);
const composerError = ref("");
let nextLocalRuleSeq = 1;
const composer = reactive({
  key: "",
  title: "",
  authority: "Study" as KernelRulePackAuthority,
  revisingVersion: null as number | null,
  reason: "Author study collection edit-check rule pack",
  rules: [] as ComposerRuleRow[],
});

const activeTriage = ref<{
  finding: KernelValidationFinding;
  mode: FindingTriageMode;
} | null>(null);
const triageReason = ref("");
const triageClassification = ref<KernelFindingClassification>("DataIssue");
const triageDecisionText = ref("");

const signatureTarget = ref<PendingSignatureTarget | null>(null);

const APPROVE_REASONS = [
  "Rule pack governance approval",
  "APPROVAL",
  "CLINICAL_QC",
];

const DEPRECATE_REASONS = [
  "Supersede deprecated rule pack version",
  "GOVERNANCE_DEPRECATION",
];

const FINDING_DECISION_REASONS = [
  "Signed clinical validation finding disposition",
  "CLINICAL_QC",
];

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

const defaultSignerRole = computed(() => {
  const raw = new Set(authStore.rawRoles.map((r) => r.trim().toLowerCase()));
  if (raw.has("cra")) return "CRA / Monitor";
  if (raw.has("data_manager")) return "Data Manager";
  return "Study Designer";
});

const signatureModalConfig = computed(() => {
  const target = signatureTarget.value;
  if (!target) return null;
  if (target.kind === "approvePack") {
    return {
      signerRole: "Study Designer",
      reasons: APPROVE_REASONS,
      actionUrl: rulePackApproveAction(target.pack.ref),
      semanticAction: RULE_PACK_APPROVE_SEMANTIC_ACTION,
      batchId: "",
      onSign: approveWithToken,
    };
  }
  if (target.kind === "deprecatePack") {
    return {
      signerRole: "Study Designer",
      reasons: DEPRECATE_REASONS,
      actionUrl: rulePackDeprecateAction(target.pack.ref),
      semanticAction: RULE_PACK_DEPRECATE_SEMANTIC_ACTION,
      batchId: "",
      onSign: deprecateWithToken,
    };
  }
  if (target.kind === "acceptFinding") {
    return {
      signerRole: defaultSignerRole.value,
      reasons: Array.from(
        new Set([target.reason, ...FINDING_DECISION_REASONS])
      ),
      actionUrl: findingAcceptAction(target.finding.ref),
      semanticAction: FINDING_ACCEPT_SEMANTIC_ACTION,
      batchId: target.batchId,
      onSign: acceptFindingWithToken,
    };
  }
  return {
    signerRole: defaultSignerRole.value,
    reasons: Array.from(new Set([target.reason, ...FINDING_DECISION_REASONS])),
    actionUrl: findingExplainAction(target.finding.ref),
    semanticAction: FINDING_EXPLAIN_SEMANTIC_ACTION,
    batchId: target.batchId,
    onSign: explainFindingWithToken,
  };
});

const canConfirmActiveTriage = computed(() => {
  if (!activeTriage.value || !triageReason.value.trim()) return false;
  if (
    activeTriage.value.mode === "accept" ||
    activeTriage.value.mode === "explain"
  ) {
    return triageDecisionText.value.trim().length > 0;
  }
  return true;
});

function closeSignatureModal() {
  signatureTarget.value = null;
}

function unversionedPackRef(refText: string): string {
  const parsed = parseNodeRef(refText);
  return parsed ? `${parsed.kind}:${parsed.id}` : refText;
}

function toggleExpanded(refText: string) {
  expandedRef.value = expandedRef.value === refText ? null : refText;
}

function itemsForDataset(dataset: string): KernelProtocolFormItem[] {
  const trimmed = dataset.trim();
  if (!trimmed) return [];
  const form = studyForms.value.find((f) => f.id === trimmed);
  return form?.items ?? [];
}

async function ensureProtocolFormsLoaded() {
  if (!studyRef.value || loadedFormsForStudyRef.value === studyRef.value) {
    return;
  }
  loadedFormsForStudyRef.value = studyRef.value;
  try {
    const versions = await kernelApi.listProtocolVersions(studyRef.value);
    const unmasked = versions.filter((v) => !v.mask && v.ref);
    if (unmasked.length === 0) return;
    const preferred =
      unmasked.find((v) => v.status === "Draft") ??
      unmasked.find((v) => v.status === "Approved") ??
      unmasked[0];
    if (!preferred?.ref) return;
    const summary = await kernelApi.getProtocolDesignSummary(preferred.ref);
    studyForms.value = summary.forms ?? [];
  } catch {
    studyForms.value = [];
  }
}

function createBlankComposerRule(index: number): ComposerRuleRow {
  const firstForm = studyForms.value[0];
  const firstItem = firstForm?.items?.[0];
  const dataset = firstForm?.id ?? "";
  const item = firstItem?.id ?? "";
  const suggestedId =
    dataset && item
      ? `${dataset}_${item}_CHECK_${index}`
      : `STUDY_CHECK_${index}`;
  return {
    localId: `rule-${nextLocalRuleSeq++}`,
    id: suggestedId,
    version: 1,
    dataset,
    item,
    severity: "Warning",
    template: "comparison",
    operator: ">",
    threshold: "220",
    otherItem: "",
    minValue: "",
    maxValue: "",
    allowedValues: "",
    maxLength: "200",
    customConditionJson: '{"op":"is_empty","args":[{"var":"ITEM"}]}',
    explanation: "",
    remediation: "",
    citationDocument: `${props.studyCode || "Study"} Data Validation Plan`,
    citationSection: "",
    citationUri: "",
  };
}

function parseRuleSummaryToComposerRow(
  rule: KernelRuleSummary,
  fallbackIndex: number
): ComposerRuleRow {
  const base = createBlankComposerRule(fallbackIndex);
  base.id = rule.id || base.id;
  base.version = Math.max(1, Number(rule.version || 1));
  base.dataset = rule.dataset ?? "";
  base.severity =
    rule.severity === "Error" ||
    rule.severity === "Warning" ||
    rule.severity === "Notice"
      ? rule.severity
      : "Warning";
  base.explanation = rule.explanation ?? "";
  base.remediation = rule.remediation_guidance ?? "";
  base.citationDocument =
    rule.citation_document?.trim() ||
    `${props.studyCode || "Study"} Data Validation Plan`;
  base.citationSection = rule.citation_section ?? "";
  base.citationUri = rule.citation_uri ?? "";

  if (!rule.condition) {
    base.template = "missing";
    return base;
  }

  base.customConditionJson = rule.condition;
  try {
    const parsed = JSON.parse(rule.condition) as Record<string, unknown>;
    const op = typeof parsed.op === "string" ? parsed.op : "";
    const args = Array.isArray(parsed.args) ? parsed.args : [];

    if (op === "is_empty" && args.length === 1 && isVarNode(args[0])) {
      base.template = "missing";
      base.item = args[0].var;
      return base;
    }

    if (
      (op === ">" ||
        op === ">=" ||
        op === "<" ||
        op === "<=" ||
        op === "==" ||
        op === "!=") &&
      args.length === 2 &&
      isVarNode(args[0])
    ) {
      base.item = args[0].var;
      base.operator = op;
      if (isVarNode(args[1])) {
        base.template = "cross_item";
        base.otherItem = args[1].var;
        return base;
      }
      if (
        typeof args[1] === "number" ||
        typeof args[1] === "string" ||
        typeof args[1] === "boolean"
      ) {
        base.template = "comparison";
        base.threshold = String(args[1]);
        return base;
      }
    }

    if (
      op === "or" &&
      args.length === 2 &&
      isBinaryOpNode(args[0], "<") &&
      isBinaryOpNode(args[1], ">") &&
      isVarNode(args[0].args[0]) &&
      isVarNode(args[1].args[0]) &&
      args[0].args[0].var === args[1].args[0].var &&
      typeof args[0].args[1] === "number" &&
      typeof args[1].args[1] === "number"
    ) {
      base.template = "range";
      base.item = args[0].args[0].var;
      base.minValue = String(args[0].args[1]);
      base.maxValue = String(args[1].args[1]);
      return base;
    }
  } catch {
    // Keep custom JSON representation.
  }

  base.template = "custom";
  return base;
}

function isVarNode(node: unknown): node is { var: string } {
  return (
    Boolean(node) &&
    typeof node === "object" &&
    typeof (node as { var?: unknown }).var === "string"
  );
}

function isBinaryOpNode(
  node: unknown,
  expectedOp: string
): node is { op: string; args: [unknown, unknown] } {
  return (
    Boolean(node) &&
    typeof node === "object" &&
    (node as { op?: unknown }).op === expectedOp &&
    Array.isArray((node as { args?: unknown }).args) &&
    (node as { args: unknown[] }).args.length === 2
  );
}

async function openNewRuleComposer() {
  composerError.value = "";
  await ensureProtocolFormsLoaded();
  const slug =
    props.studyCode
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9-]+/g, "-")
      .replace(/^-+|-+$/g, "") || "study";
  composer.key = `${slug}-edit-checks`;
  composer.title = `${props.studyCode || "Study"} Collection Edit Checks`;
  composer.authority = "Study";
  composer.revisingVersion = null;
  composer.reason = `Draft collection edit checks for ${props.studyCode}`;
  composer.rules = [createBlankComposerRule(1)];
  composerOpen.value = true;
}

async function openReviseRuleComposer(pack: KernelRulePack) {
  composerError.value = "";
  await ensureProtocolFormsLoaded();
  const normalizedAuthority = RULE_AUTHORITIES.includes(
    pack.authority as KernelRulePackAuthority
  )
    ? (pack.authority as KernelRulePackAuthority)
    : "Study";
  composer.key = pack.key;
  composer.title = pack.title;
  composer.authority = normalizedAuthority;
  composer.revisingVersion = pack.version;
  composer.reason = `Revise rule pack ${pack.key} from v${pack.version}`;
  composer.rules =
    pack.rules.length > 0
      ? pack.rules.map((r, i) => parseRuleSummaryToComposerRow(r, i + 1))
      : [createBlankComposerRule(1)];
  composerOpen.value = true;
}

function closeComposer() {
  composerOpen.value = false;
  composerError.value = "";
}

function addComposerRule() {
  composer.rules.push(createBlankComposerRule(composer.rules.length + 1));
}

function removeComposerRule(index: number) {
  if (composer.rules.length <= 1) return;
  composer.rules.splice(index, 1);
}

function onSelectRowForm(row: ComposerRuleRow, event: Event) {
  const value = (event.target as HTMLSelectElement).value;
  row.dataset = value;
  const items = itemsForDataset(value);
  if (items.length > 0 && !items.some((i) => i.id === row.item)) {
    row.item = items[0].id;
    suggestRuleMetadataFromSelection(row, items[0]);
  }
}

function onSelectRowItem(row: ComposerRuleRow, event: Event) {
  const value = (event.target as HTMLSelectElement).value;
  row.item = value;
  const selected = itemsForDataset(row.dataset).find((i) => i.id === value);
  if (selected) {
    suggestRuleMetadataFromSelection(row, selected);
  }
}

function suggestRuleMetadataFromSelection(
  row: ComposerRuleRow,
  item: KernelProtocolFormItem
) {
  const prefix = row.dataset.trim() ? `${row.dataset.trim()}_` : "";
  const sanitized = `${prefix}${item.id}_CHECK`.replace(
    /[^A-Za-z0-9._-]/g,
    "_"
  );
  if (!row.id.trim() || row.id.endsWith("_CHECK_1")) {
    row.id = sanitized.slice(0, 64);
  }
  if (!row.explanation.trim()) {
    row.explanation = item.unit
      ? `${item.prompt} (${item.id}) failed validation check (${item.unit}).`
      : `${item.prompt} (${item.id}) failed validation check.`;
  }
}

function parseScalarLiteral(raw: string): number | boolean | string {
  const trimmed = raw.trim();
  if (trimmed === "true") return true;
  if (trimmed === "false") return false;
  const asNum = Number(trimmed);
  if (trimmed !== "" && Number.isFinite(asNum)) return asNum;
  return trimmed;
}

function containsForbiddenCitation(text: string): string | null {
  const lower = text.toLowerCase();
  for (const forbidden of FORBIDDEN_VALIDATOR_CITATIONS) {
    if (lower.includes(forbidden)) {
      return forbidden;
    }
  }
  return null;
}

function buildRuleConditionAst(row: ComposerRuleRow): Record<string, unknown> {
  if (row.template === "custom") {
    const parsed = JSON.parse(row.customConditionJson);
    if (!parsed || typeof parsed !== "object" || Array.isArray(parsed)) {
      throw new Error(
        `Rule ${row.id}: custom condition must be a JSON object.`
      );
    }
    return parsed as Record<string, unknown>;
  }

  const itemVar = row.item.trim();
  if (!itemVar) {
    throw new Error(
      `Rule ${row.id || "(unnamed)"}: collection item is required.`
    );
  }

  switch (row.template) {
    case "missing":
      return { op: "is_empty", args: [{ var: itemVar }] };
    case "comparison": {
      if (!row.threshold.trim()) {
        throw new Error(`Rule ${row.id}: comparison threshold is required.`);
      }
      return {
        op: row.operator,
        args: [{ var: itemVar }, parseScalarLiteral(row.threshold)],
      };
    }
    case "range": {
      const min = Number(row.minValue);
      const max = Number(row.maxValue);
      if (
        row.minValue === "" ||
        row.maxValue === "" ||
        !Number.isFinite(min) ||
        !Number.isFinite(max) ||
        min > max
      ) {
        throw new Error(
          `Rule ${row.id}: enter numeric minimum <= maximum for range check.`
        );
      }
      return {
        op: "or",
        args: [
          { op: "<", args: [{ var: itemVar }, min] },
          { op: ">", args: [{ var: itemVar }, max] },
        ],
      };
    }
    case "cross_item": {
      const other = row.otherItem.trim();
      if (!other) {
        throw new Error(`Rule ${row.id}: compared item ID is required.`);
      }
      return {
        op: row.operator,
        args: [{ var: itemVar }, { var: other }],
      };
    }
    case "not_in": {
      const values = row.allowedValues
        .split(",")
        .map((v) => v.trim())
        .filter((v) => v.length > 0)
        .map(parseScalarLiteral);
      if (values.length === 0) {
        throw new Error(`Rule ${row.id}: enter at least one permitted value.`);
      }
      return {
        op: "and",
        args: [
          { op: "is_not_empty", args: [{ var: itemVar }] },
          { op: "not_in", args: [{ var: itemVar }, ...values] },
        ],
      };
    }
    case "max_length": {
      const maxLen = Math.trunc(Number(row.maxLength));
      if (!Number.isFinite(maxLen) || maxLen < 1) {
        throw new Error(
          `Rule ${row.id}: maximum length must be a positive integer.`
        );
      }
      return {
        op: ">",
        args: [{ op: "length", args: [{ var: itemVar }] }, maxLen],
      };
    }
  }
}

function buildComposerPayload(): Record<string, unknown> {
  const key = composer.key.trim();
  const title = composer.title.trim();
  if (!key) {
    throw new Error("Pack key is required.");
  }
  if (!title) {
    throw new Error("Pack title is required.");
  }
  if (!composer.reason.trim()) {
    throw new Error("Reason for change is required.");
  }
  if (composer.rules.length === 0) {
    throw new Error("Add at least one edit-check rule.");
  }

  const seenIds = new Set<string>();
  const compiledRules = composer.rules.map((row, idx) => {
    const ruleId = row.id.trim();
    if (!RULE_ID_PATTERN.test(ruleId)) {
      throw new Error(
        `Rule ${idx + 1}: Rule ID must start with a letter or digit and use only letters, digits, '.', '_', or '-'.`
      );
    }
    if (seenIds.has(ruleId)) {
      throw new Error(`Duplicate rule ID "${ruleId}" in rule pack.`);
    }
    seenIds.add(ruleId);

    const explanation = row.explanation.trim();
    if (!explanation) {
      throw new Error(`Rule ${ruleId}: discrepancy explanation is required.`);
    }

    const citationDoc = row.citationDocument.trim();
    const citationSec = row.citationSection.trim();
    const citationUri = row.citationUri.trim();
    if (!citationDoc) {
      throw new Error(
        `Rule ${ruleId}: public citation document is required (K-RULE-2).`
      );
    }
    if (
      citationUri &&
      !citationUri.startsWith("https://") &&
      !citationUri.startsWith("http://")
    ) {
      throw new Error(
        `Rule ${ruleId}: citation URI must start with https:// or http://.`
      );
    }

    const forbiddenMatch = containsForbiddenCitation(
      `${citationDoc} ${citationSec} ${citationUri}`
    );
    if (forbiddenMatch) {
      throw new Error(
        `Rule ${ruleId}: citation references proprietary validator "${forbiddenMatch}". K-RULE-2 requires public regulatory, standards, sponsor, or study citations.`
      );
    }

    const condition = buildRuleConditionAst(row);
    const dataset = row.dataset.trim();
    const remediation = row.remediation.trim();

    return {
      id: ruleId,
      version: Math.max(1, Math.trunc(Number(row.version) || 1)),
      authority: composer.authority,
      citation: {
        document: citationDoc,
        ...(citationSec ? { section: citationSec } : {}),
        ...(citationUri ? { uri: citationUri } : {}),
      },
      applicability: {
        stage: "Collection",
        ...(dataset ? { dataset } : {}),
      },
      severity: row.severity,
      condition,
      explanation,
      ...(remediation ? { remediation } : {}),
    };
  });

  return {
    authority: composer.authority,
    title,
    rules: compiledRules,
  };
}

async function submitRulePackDraft() {
  if (!studyRef.value || busy.value) return;
  composerError.value = "";
  errorMessage.value = "";

  let content: Record<string, unknown>;
  try {
    content = buildComposerPayload();
  } catch (validationErr) {
    composerError.value =
      validationErr instanceof Error
        ? validationErr.message
        : String(validationErr);
    return;
  }

  busy.value = true;
  try {
    const drafted = await kernelApi.draftStudyRulePack({
      studyRef: studyRef.value,
      key: composer.key.trim(),
      title: composer.title.trim(),
      content,
      reason: composer.reason.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    composerOpen.value = false;
    actionMessage.value = `Drafted rule pack ${drafted.key} v${drafted.version} (${drafted.rule_count} ${drafted.rule_count === 1 ? "rule" : "rules"}).`;
    await loadRulePacks();
  } catch (error) {
    composerError.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function loadRulePacks() {
  if (!studyRef.value) return;
  const response = await kernelApi.listStudyRulePacks(studyRef.value);
  rulePacks.value = response.rule_packs;
  pinnedRefs.value = response.pinned_rule_pack_refs.map(unversionedPackRef);
}

async function loadFindings() {
  if (!studyRef.value) return;
  const page = await kernelApi.listStudyValidationFindings(studyRef.value, {
    status: statusFilter.value || undefined,
    severity: severityFilter.value || undefined,
  });
  findings.value = page.findings;
}

async function refreshAll() {
  if (!studyRef.value) return;
  loading.value = true;
  errorMessage.value = "";
  try {
    await Promise.all([loadRulePacks(), loadFindings()]);
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    loading.value = false;
  }
}

watch(
  () => props.studyCode,
  async (code) => {
    studyRef.value = null;
    rulePacks.value = [];
    findings.value = [];
    studyForms.value = [];
    loadedFormsForStudyRef.value = null;
    selectedFinding.value = null;
    activeTriage.value = null;
    composerOpen.value = false;
    composerError.value = "";
    errorMessage.value = "";
    actionMessage.value = "";
    if (!code) return;
    loading.value = true;
    try {
      studyRef.value = await findKernelStudyRef(code);
      if (studyRef.value) {
        await Promise.all([loadRulePacks(), loadFindings()]);
      }
    } catch (error) {
      errorMessage.value = describeKernelError(error);
    } finally {
      loading.value = false;
    }
  },
  { immediate: true }
);

async function runPackPreview(pack: KernelRulePack) {
  if (!studyRef.value || busy.value) return;
  busy.value = true;
  errorMessage.value = "";
  try {
    previewByRef[pack.ref] = await kernelApi.previewStudyRulePack(
      studyRef.value,
      pack.ref
    );
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

function openApprovePackModal(pack: KernelRulePack) {
  signatureTarget.value = { kind: "approvePack", pack };
}

function openDeprecatePackModal(pack: KernelRulePack) {
  signatureTarget.value = { kind: "deprecatePack", pack };
}

async function approveWithToken(sigToken: string, reason: string) {
  const target = signatureTarget.value;
  if (!target || target.kind !== "approvePack") return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const approved = await kernelApi.approveLibraryItem({
      itemRef: target.pack.ref,
      meaning: "Approved",
      sigToken,
      reason,
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    actionMessage.value = `Approved rule pack ${approved.key} v${approved.version}.`;
    await loadRulePacks();
    return approved;
  } finally {
    busy.value = false;
  }
}

async function deprecateWithToken(sigToken: string, reason: string) {
  const target = signatureTarget.value;
  if (!target || target.kind !== "deprecatePack") return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const deprecated = await kernelApi.deprecateLibraryItem({
      itemRef: target.pack.ref,
      meaning: "Responsibility",
      sigToken,
      reason,
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    actionMessage.value = `Deprecated rule pack ${deprecated.key} v${deprecated.version}.`;
    await loadRulePacks();
    return deprecated;
  } finally {
    busy.value = false;
  }
}

function beginPinToggle(pack: KernelRulePack) {
  pinningPack.value = pack;
  pinReason.value = "";
}

async function confirmPinToggle(pack: KernelRulePack) {
  if (!studyRef.value || !pinReason.value.trim() || busy.value) return;
  const targetRef = unversionedPackRef(pack.ref);
  const current = new Set(pinnedRefs.value);
  if (pack.pinned) {
    current.delete(targetRef);
  } else {
    current.add(targetRef);
  }

  busy.value = true;
  errorMessage.value = "";
  try {
    const updated = await kernelApi.declareStudyRulePacks({
      studyRef: studyRef.value,
      rulePackRefs: [...current],
      reason: pinReason.value.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    rulePacks.value = updated.rule_packs;
    pinnedRefs.value = updated.pinned_rule_pack_refs.map(unversionedPackRef);
    pinningPack.value = null;
    pinReason.value = "";
    actionMessage.value = pack.pinned
      ? `Unpinned ${pack.key} from ${props.studyCode}.`
      : `Pinned ${pack.key} v${pack.version} on ${props.studyCode}.`;
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function executeStudyValidationRun() {
  if (!studyRef.value || !validationRunReason.value.trim() || busy.value) {
    return;
  }
  busy.value = true;
  errorMessage.value = "";
  try {
    const run = await kernelApi.runStudyValidation({
      studyRef: studyRef.value,
      reason: validationRunReason.value.trim(),
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    actionMessage.value = `Validation run complete: ${run.detected} detected (${run.new} new, ${run.resolved} resolved).`;
    await loadFindings();
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

function beginFindingTriage(
  finding: KernelValidationFinding,
  mode: FindingTriageMode
) {
  activeTriage.value = { finding, mode };
  triageClassification.value =
    (finding.classification as KernelFindingClassification | null) ??
    "DataIssue";
  triageDecisionText.value = finding.decision_text ?? "";
  triageReason.value = `Record ${mode} decision on ${finding.rule_id}`;
}

function cancelFindingTriage() {
  activeTriage.value = null;
  triageReason.value = "";
  triageDecisionText.value = "";
}

async function confirmFindingTriage() {
  if (!activeTriage.value || !canConfirmActiveTriage.value || busy.value) {
    return;
  }
  const { finding, mode } = activeTriage.value;
  const reason = triageReason.value.trim();

  if (mode === "accept" || mode === "explain") {
    const decisionText = triageDecisionText.value.trim();
    const detectedRunRef = finding.last_detected_run_ref ?? "";
    const batchId = await findingDecisionHash(
      finding.ref,
      detectedRunRef,
      mode === "accept" ? "Accepted" : "Explained",
      decisionText
    );
    signatureTarget.value =
      mode === "accept"
        ? {
            kind: "acceptFinding",
            finding,
            rationale: decisionText,
            reason,
            batchId,
          }
        : {
            kind: "explainFinding",
            finding,
            explanation: decisionText,
            reason,
            batchId,
          };
    return;
  }

  busy.value = true;
  errorMessage.value = "";
  try {
    if (mode === "investigate") {
      await kernelApi.investigateValidationFinding({
        findingRef: finding.ref,
        reason,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      actionMessage.value = `Marked finding ${finding.rule_id} as Investigating.`;
    } else if (mode === "classify") {
      await kernelApi.classifyValidationFinding({
        findingRef: finding.ref,
        classification: triageClassification.value,
        reason,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      actionMessage.value = `Classified finding ${finding.rule_id} as ${triageClassification.value}.`;
    } else if (mode === "reopen") {
      await kernelApi.reopenValidationFinding({
        findingRef: finding.ref,
        reason,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      actionMessage.value = `Reopened finding ${finding.rule_id}.`;
    }
    cancelFindingTriage();
    await loadFindings();
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  } finally {
    busy.value = false;
  }
}

async function acceptFindingWithToken(sigToken: string, reason: string) {
  const target = signatureTarget.value;
  if (!target || target.kind !== "acceptFinding") return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const updated = await kernelApi.acceptValidationFinding({
      findingRef: target.finding.ref,
      rationale: target.rationale,
      lastDetectedRunRef: target.finding.last_detected_run_ref ?? null,
      sigToken,
      reason,
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    actionMessage.value = `Accepted finding ${updated.rule_id} with signed attestation.`;
    cancelFindingTriage();
    await loadFindings();
    return updated;
  } finally {
    busy.value = false;
  }
}

async function explainFindingWithToken(sigToken: string, reason: string) {
  const target = signatureTarget.value;
  if (!target || target.kind !== "explainFinding") return;
  busy.value = true;
  errorMessage.value = "";
  try {
    const updated = await kernelApi.explainValidationFinding({
      findingRef: target.finding.ref,
      explanation: target.explanation,
      lastDetectedRunRef: target.finding.last_detected_run_ref ?? null,
      sigToken,
      reason,
      idempotencyKey: globalThis.crypto.randomUUID(),
    });
    actionMessage.value = `Marked finding ${updated.rule_id} as Explained with signed attestation.`;
    cancelFindingTriage();
    await loadFindings();
    return updated;
  } finally {
    busy.value = false;
  }
}

async function inspectFinding(findingRef: string) {
  if (selectedFinding.value?.finding.ref === findingRef) {
    selectedFinding.value = null;
    return;
  }
  try {
    selectedFinding.value = await kernelApi.getValidationFinding(findingRef);
  } catch (error) {
    errorMessage.value = describeKernelError(error);
  }
}

function findingFormLocationOrDefault(
  finding: KernelValidationFinding
): RouteLocationRaw {
  return (
    findingFormLocation(finding) ?? {
      name: "study",
      params: { studyCode: props.studyCode },
    }
  );
}

function findingFormLocation(
  finding: KernelValidationFinding
): RouteLocationRaw | null {
  if (
    finding.target_kind !== "Record" &&
    finding.target_kind !== "DatasetRecord"
  ) {
    return null;
  }
  const formRef = collectionInstanceRef(finding.record_key);
  if (!formRef) return null;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "data",
      section: "validation",
      form: formRef,
      ...(finding.variable ? { item: finding.variable } : {}),
    },
  };
}
</script>

<style scoped>
.study-validation {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.validation-header,
.header-actions,
.card-header-row,
.rule-pack-top,
.rule-pack-actions,
.pin-reason-row,
.validation-run-bar,
.findings-filters,
.finding-head,
.finding-actions,
.composer-footer,
.composer-row-footer,
.triage-panel-actions {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
}
.header-actions,
.rule-pack-actions,
.pin-reason-row,
.validation-run-bar,
.finding-head,
.finding-actions,
.triage-panel-actions {
  justify-content: flex-start;
}
.validation-subtitle,
.status-line,
.rule-pack-meta,
.finding-decision-note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}
.validation-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 14px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}
.composer-meta-grid,
.composer-rule-grid,
.composer-condition-params {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
  gap: 10px;
}
.composer-rules-stack {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.composer-rule-row,
.triage-panel {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}
.composer-card label,
.triage-panel label,
.validation-run-bar label {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.82rem;
}
.full-width-label,
.composer-reason-label {
  flex: 1 1 260px;
}
.rule-pack-list,
.findings-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  list-style: none;
  margin: 0;
  padding: 0;
}
.rule-pack-item,
.finding-item {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
}
.status-pill {
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
  background: var(--neutral-light);
}
.status-pill--pinned {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.severity-tag {
  font-weight: 600;
  font-size: 0.8rem;
}
.compiled-rules-list,
.pack-preview-box {
  margin: 4px 0 0;
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
  font-size: 0.82rem;
}
.rule-expr {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  margin-top: 2px;
}
.banner {
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}
.banner-error {
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}
.banner-success {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
</style>
