<template>
  <div class="training-console-card card">
    <!-- Header -->
    <div class="console-header">
      <div class="header-titles">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH GCP E6(R2) §4</span>
          <span class="badge badge-standard">DIA TMF 05.02.04</span>
          <span
            class="badge"
            :class="isGated ? 'badge-locked' : 'badge-unlocked'"
          >
            {{
              isGated
                ? "🔒 EDC Write Permissions Gated"
                : "🟢 EDC Write & Signature Unlocked"
            }}
          </span>
        </div>
        <h2 class="console-title">
          Protocol Amendment Training &amp; Qualification Console
        </h2>
        <p class="console-subtitle">
          Mandatory procedural delta training, time-on-page verification,
          comprehension quizzes (&ge;80%), and Part 11 electronic signature
          certificates.
        </p>
      </div>

      <div class="header-actions">
        <button
          class="btn btn-secondary"
          :disabled="loading"
          @click="refreshData"
        >
          <span class="btn-icon">🔄</span> Refresh
        </button>
      </div>
    </div>

    <!-- Active Protocol / Assignment Selector -->
    <div class="assignment-selector-panel">
      <div class="form-group">
        <label for="assignment-select"
          >Active Training Assignment / Curriculum:</label
        >
        <select
          id="assignment-select"
          v-model="selectedAssignmentId"
          class="select-input"
          @change="onAssignmentChange"
        >
          <option v-for="asn in assignments" :key="asn.id" :value="asn.id">
            [{{ asn.status }}]
            {{ asn.curriculum?.title || asn.study_id + " Protocol Training" }} —
            Site {{ asn.site_id }}
          </option>
          <option v-if="assignments.length === 0" value="" disabled>
            No active training assignments found for current user
          </option>
        </select>
      </div>

      <div class="status-summary-chips">
        <div class="summary-chip">
          <span class="chip-label">Status:</span>
          <span
            class="chip-value"
            :class="
              'status-' + (activeAssignment?.status || 'PENDING').toLowerCase()
            "
          >
            {{ activeAssignment?.status || "PENDING" }}
          </span>
        </div>
        <div class="summary-chip">
          <span class="chip-label">Time on Page:</span>
          <span class="chip-value">{{
            formatTime(activeAssignment?.time_spent_seconds || totalTimeSpent)
          }}</span>
        </div>
        <div class="summary-chip">
          <span class="chip-label">Quiz Score:</span>
          <span class="chip-value">
            {{
              activeAssignment?.quiz_score != null
                ? Math.round(activeAssignment.quiz_score * 100) + "%"
                : "Not Taken"
            }}
          </span>
        </div>
      </div>
    </div>

    <!-- Inline Sync Error Alert Banner -->
    <div
      v-if="syncError"
      class="sync-error-banner alert-banner error-banner"
      role="alert"
    >
      <div class="banner-content">
        <span class="banner-icon">⚠️</span>
        <div class="banner-text">
          <strong class="banner-title">Heartbeat Synchronization Failed</strong>
          <span class="banner-message">
            {{
              syncErrorMessage ||
              "Failed to record training progress heartbeat. Time tracking has been paused."
            }}
          </span>
        </div>
      </div>
      <button
        class="btn btn-retry btn-sm"
        :disabled="isSyncing"
        @click="retrySync"
      >
        <span v-if="isSyncing">Syncing...</span>
        <span v-else>🔄 Retry Sync</span>
      </button>
    </div>

    <!-- Main Learning & Assessment Workspace -->
    <div v-if="activeCurriculum" class="training-workspace">
      <!-- Navigation Tabs: Modules | Quiz | Certificate -->
      <div class="tab-bar">
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'modules' }"
          @click="activeTab = 'modules'"
        >
          📖 Procedural Modules ({{ activeCurriculum.modules?.length || 0 }})
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'quiz' }"
          @click="activeTab = 'quiz'"
        >
          📝 Comprehension Quiz (&ge;80%)
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'certificate' }"
          :disabled="activeAssignment?.status !== 'COMPLETED'"
          @click="activeTab = 'certificate'"
        >
          📜 21 CFR Part 11 Certificate
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'batch' }"
          @click="activeTab = 'batch'"
        >
          👥 Batch Assignments
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'delegations' }"
          @click="
            loadDelegations();
            activeTab = 'delegations';
          "
        >
          ✍️ Delegation Bulk Sign-Off
        </button>
      </div>

      <!-- TAB 1: MODULES -->
      <div v-if="activeTab === 'modules'" class="tab-content modules-layout">
        <div class="module-sidebar">
          <h4 class="sidebar-heading">Curriculum Sections</h4>
          <ul class="module-list">
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <li
              v-for="(mod, idx) in activeCurriculum.modules"
              :key="mod.id"
              class="module-item"
              :class="{ active: selectedModuleIndex === idx }"
              @click="selectModule(idx)"
            >
              <span class="module-num">{{ idx + 1 }}</span>
              <div class="module-meta">
                <span class="module-title">{{ mod.title }}</span>
                <span
                  class="badge badge-sm"
                  :class="
                    'badge-mod-' + (mod.module_type || 'default').toLowerCase()
                  "
                >
                  {{ mod.module_type }}
                </span>
              </div>
            </li>
          </ul>
        </div>

        <div class="module-reader">
          <div v-if="currentModule" class="reader-content">
            <div class="reader-header">
              <div class="reader-title-group">
                <span class="badge badge-delta">{{
                  currentModule.module_type
                }}</span>
                <h3 class="reader-title">
                  {{ currentModule.title }}
                </h3>
              </div>
              <div class="reader-timer">
                ⏱️ Section Time: <strong>{{ formatTime(moduleTimer) }}</strong>
                <span
                  v-if="
                    moduleTimer >=
                    (currentModule.min_reading_time_seconds || 30)
                  "
                  class="text-success"
                  >✅ Min Read Time Met</span
                >
                <span v-else class="text-muted"
                  >({{
                    (currentModule.min_reading_time_seconds || 30) -
                    moduleTimer
                  }}s remaining)</span
                >
              </div>
            </div>

            <div class="markdown-body">
              <pre class="content-text">{{ currentModule.content_text }}</pre>
            </div>

            <div class="reader-footer">
              <button
                class="btn btn-secondary"
                :disabled="selectedModuleIndex <= 0"
                @click="selectModule(selectedModuleIndex - 1)"
              >
                ⬅️ Previous Section
              </button>
              <button
                v-if="selectedModuleIndex < activeCurriculum.modules.length - 1"
                class="btn btn-primary"
                @click="selectModule(selectedModuleIndex + 1)"
              >
                Next Section ➡️
              </button>
              <button
                v-else
                class="btn btn-primary btn-proceed-quiz"
                @click="activeTab = 'quiz'"
              >
                Proceed to Comprehension Quiz 📝
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- TAB 2: COMPREHENSION QUIZ -->
      <div v-if="activeTab === 'quiz'" class="tab-content quiz-layout">
        <div class="quiz-header-card">
          <div class="quiz-info">
            <h3>Protocol Comprehension Assessment</h3>
            <p>
              Under ICH GCP E6(R2) Section 4, clinical personnel must
              demonstrate procedural mastery before entering eCRF data. Passing
              threshold: <strong>&ge;80%</strong>.
            </p>
          </div>
          <div
            v-if="quizResult"
            class="quiz-score-badge"
            :class="quizResult.passed ? 'score-passed' : 'score-failed'"
          >
            <div class="score-number">
              {{ Math.round(quizResult.score_percentage * 100) }}%
            </div>
            <div class="score-label">
              {{
                quizResult.passed
                  ? "PASSED (Qualified)"
                  : "FAILED (Retry Required)"
              }}
            </div>
          </div>
        </div>

        <div class="quiz-questions-list">
          <div
            v-for="(q, qIndex) in aggregatedQuestions"
            :key="q.id"
            class="quiz-card"
          >
            <div class="question-title">
              <span class="q-number">Question {{ qIndex + 1 }}:</span>
              <span>{{ q.question }}</span>
            </div>

            <div class="options-group">
              <label
                v-for="(opt, optIdx) in q.options"
                :key="optIdx"
                class="option-item"
                :class="{
                  selected: userAnswers[q.id] === optIdx,
                  correct: quizResult && optIdx === q.correct_option_index,
                  incorrect:
                    quizResult &&
                    userAnswers[q.id] === optIdx &&
                    optIdx !== q.correct_option_index,
                }"
              >
                <input
                  v-model="userAnswers[q.id]"
                  type="radio"
                  :name="'question_' + q.id"
                  :value="optIdx"
                  :disabled="activeAssignment?.status === 'COMPLETED'"
                />
                <span class="option-text">{{ opt }}</span>
              </label>
            </div>

            <div v-if="quizResult && q.rationale" class="rationale-box">
              💡 <strong>GxP Rationale:</strong> {{ q.rationale }}
            </div>
          </div>
        </div>

        <div class="quiz-actions-bar">
          <button
            v-if="activeAssignment?.status !== 'COMPLETED'"
            class="btn btn-primary btn-submit-quiz"
            :disabled="
              submittingQuiz ||
              Object.keys(userAnswers).length < aggregatedQuestions.length
            "
            @click="handleSubmitQuiz"
          >
            <span v-if="submittingQuiz">Scoring Assessment...</span>
            <span v-else>Submit Quiz for Evaluation 📊</span>
          </button>

          <button
            v-if="
              quizResult?.passed && activeAssignment?.status !== 'COMPLETED'
            "
            class="btn btn-success btn-signoff"
            @click="showSignModal = true"
          >
            ✍️ 21 CFR Part 11 Digital Sign-Off
          </button>
        </div>
      </div>

      <!-- TAB 3: CERTIFICATE -->
      <div
        v-if="activeTab === 'certificate'"
        class="tab-content certificate-layout"
      >
        <div class="certificate-paper">
          <div class="cert-header">
            <div class="cert-brand">CADENCE CLINICAL RESEARCH PLATFORM</div>
            <h2 class="cert-title">CERTIFICATE OF PROTOCOL QUALIFICATION</h2>
            <div class="cert-subtitle">
              Compliant with ICH GCP E6(R2)/(R3) &amp; 21 CFR Part 11 Electronic
              Signatures
            </div>
          </div>

          <div class="cert-body">
            <p class="cert-lead">
              This certifies that
              <strong>{{ activeAssignment?.user_name }}</strong> (ID:
              <code>{{ activeAssignment?.user_id }}</code
              >), acting in the capacity of
              <strong>{{ activeAssignment?.user_role }}</strong
              >, has successfully fulfilled all mandatory protocol curriculum
              requirements and demonstrated mastery under:
            </p>

            <table class="cert-table">
              <tbody>
                <tr>
                  <td>Curriculum / Scope:</td>
                  <td>
                    <strong>{{ activeCurriculum?.title }}</strong>
                  </td>
                </tr>
                <tr>
                  <td>Study &amp; Site:</td>
                  <td>
                    Study <code>{{ activeAssignment?.study_id }}</code> (Site:
                    <code>{{ activeAssignment?.site_id }}</code
                    >)
                  </td>
                </tr>
                <tr>
                  <td>Protocol Version:</td>
                  <td>Version {{ activeCurriculum?.protocol_version }}</td>
                </tr>
                <tr>
                  <td>Comprehension Score:</td>
                  <td>
                    <strong
                      >{{
                        Math.round((activeAssignment?.quiz_score || 1.0) * 100)
                      }}%</strong
                    >
                    (Threshold: &ge;80%)
                  </td>
                </tr>
                <tr>
                  <td>Verified Duration:</td>
                  <td>
                    {{ activeAssignment?.time_spent_seconds || 0 }} seconds
                    recorded time-on-page
                  </td>
                </tr>
                <tr>
                  <td>Completion Date:</td>
                  <td>{{ activeAssignment?.completed_at || "Verified" }}</td>
                </tr>
              </tbody>
            </table>

            <div v-if="activeCertificate" class="cert-signature-manifest">
              <div class="manifest-header">
                🔒 21 CFR Part 11 Cryptographic Signature Manifestation
              </div>
              <div class="manifest-grid">
                <div>
                  <strong>Signer:</strong>
                  {{ activeCertificate.signature_manifestation?.signer_name }}
                </div>
                <div>
                  <strong>Role:</strong>
                  {{ activeCertificate.signature_manifestation?.signer_role }}
                </div>
                <div>
                  <strong>Meaning:</strong>
                  {{ activeCertificate.signature_manifestation?.meaning }}
                </div>
                <div>
                  <strong>Seal (SHA-256):</strong>
                  <code>{{ activeCertificate.sha256_seal }}</code>
                </div>
              </div>
            </div>

            <div class="cert-routing-status">
              <div class="routing-badge badge-etmf">
                📁 Routed to eTMF Zone 5 (Artifact 05.02.04 Site Staff Training
                Records)
              </div>
              <div class="routing-badge badge-eisf">
                📑 Routed to Site eISF Regulatory Binder (Section 05.02)
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- TAB 4: BATCH ASSIGNMENT WORKSPACE -->
      <div v-if="activeTab === 'batch'" class="tab-content batch-layout">
        <div class="batch-card card">
          <div class="batch-header">
            <h3>Multi-Site &amp; Role Batch Curriculum Assignment</h3>
            <p class="section-desc">
              Select target clinical trial sites and roles to batch-assign
              training curricula. Bypasses duplicate active assignments
              automatically with a 200 target personnel limit.
            </p>
          </div>

          <p
            v-if="availableSites.length === 0"
            class="text-muted batch-empty"
            data-testid="batch-empty"
          >
            No staff are on this study's delegation log yet, so there is no one
            to assign this curriculum to.
          </p>

          <div v-else class="batch-filter-grid">
            <div class="filter-col">
              <div class="filter-title">Target Sites (Multi-Select):</div>
              <div class="checkbox-group">
                <label
                  v-for="site in availableSites"
                  :key="site"
                  class="checkbox-label"
                >
                  <input
                    type="checkbox"
                    :value="site"
                    v-model="batchForm.selectedSites"
                  />
                  <span>{{ site }}</span>
                </label>
              </div>
            </div>

            <div class="filter-col">
              <div class="filter-title">Clinical Roles (Multi-Select):</div>
              <div class="checkbox-group">
                <label
                  v-for="role in availableRoles"
                  :key="role"
                  class="checkbox-label"
                >
                  <input
                    type="checkbox"
                    :value="role"
                    v-model="batchForm.selectedRoles"
                  />
                  <span>{{ role }}</span>
                </label>
              </div>
            </div>
          </div>

          <div class="batch-preview-bar">
            <div class="preview-info">
              <span
                >Target Scope:
                <strong>{{ computedBatchCount }} personnel</strong> ({{
                  batchForm.selectedSites.length
                }}
                sites &times; {{ batchForm.selectedRoles.length }} roles)</span
              >
              <span v-if="computedBatchCount > 200" class="badge badge-error"
                >⚠️ Exceeds 200 Synchronous Limit</span
              >
            </div>
            <button
              class="btn btn-primary"
              :disabled="
                computedBatchCount === 0 ||
                computedBatchCount > 200 ||
                submittingBatch
              "
              @click="showBatchModal = true"
            >
              <span>Execute Batch Assignment 🚀</span>
            </button>
          </div>

          <p v-if="batchError" class="badge badge-error" role="alert">
            {{ batchError }}
          </p>

          <!-- Batch Execution Result Manifest -->
          <div v-if="batchResult" class="batch-result-card">
            <div class="result-header">
              <h4>Batch Execution Manifest</h4>
              <span class="badge badge-gxp"
                >Tx ID: {{ batchResult.batch_transaction_id }}</span
              >
            </div>
            <div class="result-stats">
              <div class="stat-pill success-pill">
                Assigned: {{ batchResult.total_assigned }}
              </div>
              <div class="stat-pill warning-pill">
                Skipped Duplicates: {{ batchResult.total_skipped }}
              </div>
              <div class="stat-pill info-pill">
                Total Eligible: {{ batchResult.total_eligible }}
              </div>
            </div>
            <div
              v-if="batchResult.skipped_items &amp;&amp; batchResult.skipped_items.length > 0"
              class="skipped-table-wrapper"
            >
              <h5>Skipped Duplicate Assignments</h5>
              <table class="data-table">
                <thead>
                  <tr>
                    <th>User ID</th>
                    <th>Site</th>
                    <th>Role</th>
                    <th>Reason</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(item, idx) in batchResult.skipped_items"
                    :key="idx"
                  >
                    <td>{{ item.user_id }}</td>
                    <td>{{ item.site_id }}</td>
                    <td>{{ item.user_role }}</td>
                    <td>
                      <span class="badge badge-warning">{{ item.reason }}</span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>

      <!-- TAB 5: DELEGATION BULK SIGN-OFF WORKSPACE -->
      <div
        v-if="activeTab === 'delegations'"
        class="tab-content delegations-layout"
      >
        <div class="delegations-card card">
          <div class="delegation-header">
            <div>
              <h3>
                21 CFR Part 11 Principal Investigator Delegation Log Sign-Off
              </h3>
              <p class="section-desc">
                Review pending site staff delegation duties and execute legally
                binding electronic bulk signatures with digital seals (SHA-256)
                routed to eTMF Zone 5 (Artifact 05.02.04).
              </p>
            </div>
            <button class="btn btn-secondary btn-sm" @click="loadDelegations">
              🔄 Refresh Log
            </button>
          </div>

          <div class="delegations-table-wrapper">
            <table class="data-table">
              <thead>
                <tr>
                  <th style="width: 40px">
                    <input
                      type="checkbox"
                      aria-label="Select all delegations"
                      :checked="isAllDelegationsSelected"
                      @change="toggleSelectAllDelegations"
                    />
                  </th>
                  <th>Staff Member</th>
                  <th>Role</th>
                  <th>Site ID</th>
                  <th>Task &amp; Duties</th>
                  <th>Status</th>
                  <th>Digital Seal</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="del in delegations" :key="del.id">
                  <td>
                    <input
                      type="checkbox"
                      :value="del.id"
                      :aria-label="`Select delegation ${del.id}`"
                      v-model="selectedDelegationIds"
                      :disabled="del.status === 'COMPLETED'"
                    />
                  </td>
                  <td>
                    <strong>{{ del.staff_name }}</strong
                    ><br /><small class="text-muted">{{
                      del.staff_user_id
                    }}</small>
                  </td>
                  <td>
                    <span class="badge badge-role">{{ del.role }}</span>
                  </td>
                  <td>{{ del.site_id }}</td>
                  <td>
                    <strong>{{ del.task_code }}</strong
                    ><br /><small>{{ del.duties }}</small>
                  </td>
                  <td>
                    <span
                      class="badge"
                      :class="
                        del.status === 'COMPLETED'
                          ? 'badge-success'
                          : 'badge-warning'
                      "
                    >
                      {{ del.status }}
                    </span>
                  </td>
                  <td>
                    <code v-if="del.pi_signature_hash" class="seal-hash"
                      >{{ del.pi_signature_hash.substring(0, 12) }}...</code
                    >
                    <span v-else class="text-muted">Unsigned</span>
                  </td>
                </tr>
                <tr v-if="delegations.length === 0">
                  <td colspan="7" class="text-center">
                    No delegation records found.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div class="delegation-actions-bar">
            <span
              >Selected for Sign-Off:
              <strong>{{ selectedDelegationIds.length }} records</strong></span
            >
            <button
              class="btn btn-success"
              :disabled="selectedDelegationIds.length === 0"
              @click="showBulkSignModal = true"
            >
              ✍️ Execute Part 11 Bulk Signature
            </button>
          </div>

          <!-- Last Bulk Sign Response Manifest -->
          <div
            v-if="bulkSignResult"
            class="bulk-sign-result alert-banner success-banner"
          >
            <div class="banner-content">
              <span class="banner-icon">✅</span>
              <div>
                <strong>Bulk Delegation Sign-Off Executed Successfully</strong
                ><br />
                <small>
                  Batch Tx: {{ bulkSignResult.batch_transaction_id }} | Signed
                  Records: {{ bulkSignResult.total_signed }} | eTMF Artifact:
                  Zone 5 ({{ bulkSignResult.etmf_artifact_id }})<br />
                  SHA-256 Digital Seal:
                  <code>{{ bulkSignResult.sha256_seal }}</code>
                </small>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Electronic Signature Modal -->
    <div v-if="showSignModal" class="modal-backdrop">
      <div class="modal-card">
        <div class="modal-header">
          <h3>21 CFR Part 11 Electronic Signature Sign-Off</h3>
          <button class="modal-close-btn" @click="showSignModal = false">
            ✕
          </button>
        </div>

        <div class="modal-body">
          <p class="modal-description">
            In accordance with 21 CFR Part 11, entering your printed legal name
            and executing this digital sign-off represents the legally binding
            equivalent of your handwritten signature.
          </p>

          <div class="form-group">
            <label for="signer-name">Printed Legal Name:</label>
            <input
              id="signer-name"
              v-model="signaturePayload.signerName"
              type="text"
              class="text-input"
              placeholder="Your full name"
            />
          </div>

          <div class="form-group">
            <label for="signer-role">Clinical Role:</label>
            <input
              id="signer-role"
              v-model="signaturePayload.signerRole"
              type="text"
              class="text-input"
              placeholder="e.g. Principal Investigator"
            />
          </div>

          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Legal Confirmation Statement:</label>
            <div class="statement-box">
              "I certify under penalty of law that I have reviewed, understood,
              and agree to execute clinical procedures in accordance with
              approved Protocol Version
              {{ activeCurriculum?.protocol_version }}."
            </div>
          </div>
        </div>

        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showSignModal = false">
            Cancel
          </button>
          <button
            class="btn btn-success"
            :disabled="signing || !signaturePayload.signerName"
            @click="handleSignOff"
          >
            <span v-if="signing">Signing &amp; Sealing...</span>
            <span v-else>Confirm &amp; Execute Signature ✍️</span>
          </button>
        </div>
      </div>
    </div>

    <!-- Batch Assignment Modal -->
    <div v-if="showBatchModal" class="modal-backdrop">
      <div class="modal-card">
        <div class="modal-header">
          <h3>Confirm Batch Curriculum Assignment</h3>
          <button class="modal-close-btn" @click="showBatchModal = false">
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p class="modal-description">
            You are about to assign curriculum
            <strong>{{
              activeCurriculum?.title || "Protocol Training"
            }}</strong>
            across
            <strong>{{ batchForm.selectedSites.length }} sites</strong> and
            <strong>{{ batchForm.selectedRoles.length }} roles</strong> ({{
              computedBatchCount
            }}
            target personnel).
          </p>
          <div class="form-group">
            <label for="batch-reason">Reason for Change / Audit Note:</label>
            <input
              id="batch-reason"
              v-model="batchForm.reasonForChange"
              type="text"
              class="text-input"
              placeholder="e.g. Protocol Amendment V3.0 Deployment"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showBatchModal = false">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="submittingBatch || !batchForm.reasonForChange.trim()"
            @click="handleExecuteBatchAssign"
          >
            <span v-if="submittingBatch">Deploying Batch...</span>
            <span v-else>Confirm &amp; Deploy Batch 🚀</span>
          </button>
        </div>
      </div>
    </div>

    <!-- Bulk Delegation Sign-Off Modal -->
    <div v-if="showBulkSignModal" class="modal-backdrop">
      <div class="modal-card">
        <div class="modal-header">
          <h3>21 CFR Part 11 Bulk Delegation Log Sign-Off</h3>
          <button class="modal-close-btn" @click="showBulkSignModal = false">
            ✕
          </button>
        </div>
        <div class="modal-body">
          <p class="modal-description">
            Executing bulk electronic sign-off for
            <strong
              >{{ selectedDelegationIds.length }} pending delegation
              records</strong
            >. This operation generates an immutable SHA-256 digital seal and
            routes signed records to eTMF Zone 5 (Artifact 05.02.04).
          </p>
          <div class="form-group">
            <label for="bulk-signer-name">Signer Printed Name:</label>
            <input
              id="bulk-signer-name"
              v-model="bulkSignForm.signerName"
              type="text"
              class="text-input"
              placeholder="Your full name"
            />
          </div>
          <div class="form-group">
            <label for="bulk-signer-role">Clinical Authority Role:</label>
            <input
              id="bulk-signer-role"
              v-model="bulkSignForm.signerRole"
              type="text"
              class="text-input"
              placeholder="Principal Investigator"
            />
          </div>
          <div class="form-group">
            <label for="bulk-password"
              >21 CFR Part 11 Password Credential:</label
            >
            <input
              id="bulk-password"
              v-model="bulkSignForm.password"
              type="password"
              class="text-input"
              placeholder="Enter password to verify identity"
            />
          </div>
          <div class="form-group">
            <span>Legal Attestation Statement:</span>
            <div class="statement-box">
              "I confirm that I have reviewed and approved the delegated duties
              for the selected site staff personnel in accordance with 21 CFR
              Part 11 and ICH GCP E6(R2)."
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showBulkSignModal = false">
            Cancel
          </button>
          <button
            class="btn btn-success"
            :disabled="
              submittingBulkSign ||
              !bulkSignForm.password ||
              !bulkSignForm.signerName
            "
            @click="handleExecuteBulkSignOff"
          >
            <span v-if="submittingBulkSign"
              >Executing Part 11 Signature...</span
            >
            <span v-else>Confirm &amp; Execute Signature ✍️</span>
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted, onUnmounted } from "vue";
import { soaClient } from "../../api/soaClient";
import { trainingApi } from "../../api/training";
import {
  DELEGATION_SIGN_OFF_ACTION,
  DELEGATION_SIGN_OFF_SEMANTIC_ACTION,
  delegationBatchBindingId,
} from "../../utils/signatureBinding";
import { useAuthStore } from "../../stores/auth";

const authStore = useAuthStore();

const loading = ref(false);
const assignments = ref([]);
const selectedAssignmentId = ref("");
const activeAssignment = ref(null);
const activeCurriculum = ref(null);
const delegations = ref([]);
const activeCertificate = ref(null);
const activeTab = ref("modules");
const selectedModuleIndex = ref(0);

const isGated = ref(true);
const totalTimeSpent = ref(0);
const moduleTimer = ref(0);
let timerInterval = null;

const syncError = ref(false);
const syncErrorMessage = ref("");
const isSyncing = ref(false);

const userAnswers = ref({});
const submittingQuiz = ref(false);
const quizResult = ref(null);

const showSignModal = ref(false);
const signing = ref(false);
const signaturePayload = ref({
  signerName: "",
  signerRole: "Principal Investigator",
});

const currentModule = computed(() => {
  if (!activeCurriculum.value?.modules) return null;
  return activeCurriculum.value.modules[selectedModuleIndex.value] || null;
});

const aggregatedQuestions = computed(() => {
  if (!activeCurriculum.value?.modules) return [];
  const questions = [];
  activeCurriculum.value.modules.forEach((m) => {
    if (m.quiz_questions && Array.isArray(m.quiz_questions)) {
      questions.push(...m.quiz_questions);
    }
  });
  return questions;
});

function formatTime(seconds) {
  const s = seconds || 0;
  const mins = Math.floor(s / 60);
  const rem = s % 60;
  return `${mins}m ${rem < 10 ? "0" : ""}${rem}s`;
}

async function refreshData() {
  loading.value = true;
  try {
    // The signed-in identity is the only source of who is training (#5400): no invented user stands in for it.
    const userId = authStore.identity?.sub || authStore.identity?.username;
    signaturePayload.value.signerName = authStore.identity?.username || "";
    signaturePayload.value.signerRole = authStore.activeRole || "";
    if (!userId) {
      assignments.value = [];
      return;
    }

    const asnList = await trainingApi.listAssignments({ user_id: userId });
    assignments.value = asnList;

    if (asnList.length > 0 && !selectedAssignmentId.value) {
      selectedAssignmentId.value = asnList[0].id;
    }

    if (selectedAssignmentId.value) {
      await loadAssignmentDetails(selectedAssignmentId.value);
    }
    await loadDelegations();
  } catch (err) {
    console.error("Failed to load training assignments:", err);
  } finally {
    loading.value = false;
  }
}

async function loadAssignmentDetails(asnId) {
  syncError.value = false;
  syncErrorMessage.value = "";
  try {
    const detail = await trainingApi.getAssignmentDetail(asnId);
    activeAssignment.value = detail;
    activeCurriculum.value = detail.curriculum;
    totalTimeSpent.value = detail.time_spent_seconds || 0;

    isGated.value = detail.status !== "COMPLETED";

    if (detail.status === "COMPLETED") {
      try {
        const cert = await trainingApi.getCertificate(asnId);
        activeCertificate.value = cert;
      } catch {
        // certificate not yet generated or available
      }
    }
  } catch (err) {
    console.error("Error loading assignment detail:", err);
  }
}

function onAssignmentChange() {
  syncError.value = false;
  syncErrorMessage.value = "";
  if (selectedAssignmentId.value) {
    loadAssignmentDetails(selectedAssignmentId.value);
    selectedModuleIndex.value = 0;
    moduleTimer.value = 0;
    quizResult.value = null;
    userAnswers.value = {};
  }
}

async function retrySync() {
  if (!activeAssignment.value) return;
  isSyncing.value = true;
  try {
    await trainingApi.recordProgress(activeAssignment.value.id, {
      time_spent_delta_seconds: 15,
      reason_for_change: "Heartbeat training time-on-page recording",
    });
    syncError.value = false;
    syncErrorMessage.value = "";
  } catch (err) {
    syncError.value = true;
    syncErrorMessage.value =
      err?.message ||
      "Failed to record training progress heartbeat. Time tracking has been paused.";
  } finally {
    isSyncing.value = false;
  }
}

function selectModule(index) {
  selectedModuleIndex.value = index;
  moduleTimer.value = 0;
}

async function handleSubmitQuiz() {
  if (!activeAssignment.value) return;
  submittingQuiz.value = true;
  try {
    const res = await trainingApi.submitQuiz(activeAssignment.value.id, {
      answers: userAnswers.value,
      reason_for_change: "Submit comprehension quiz evaluation",
    });
    quizResult.value = res;
    if (res.passed) {
      activeAssignment.value.status = "IN_PROGRESS";
      activeAssignment.value.quiz_score = res.score_percentage;
    }
  } catch (err) {
    console.error("Quiz submission error:", err);
  } finally {
    submittingQuiz.value = false;
  }
}

async function handleSignOff() {
  if (!activeAssignment.value) return;
  signing.value = true;
  try {
    const cert = await trainingApi.signOffTraining(activeAssignment.value.id, {
      signer_name: signaturePayload.value.signerName,
      signer_role: signaturePayload.value.signerRole,
      meaning:
        "I certify that I have reviewed, understood, and agree to execute clinical procedures in accordance with this approved protocol curriculum.",
      reason_for_change:
        "21 CFR Part 11 Electronic Signature for training completion",
    });
    activeCertificate.value = cert;
    activeAssignment.value.status = "COMPLETED";
    isGated.value = false;
    showSignModal.value = false;
    activeTab.value = "certificate";
  } catch (err) {
    console.error("Sign-off error:", err);
  } finally {
    signing.value = false;
  }
}

function startTimer() {
  timerInterval = setInterval(async () => {
    if (syncError.value) {
      return;
    }
    if (
      activeAssignment.value &&
      activeAssignment.value.status !== "COMPLETED"
    ) {
      moduleTimer.value += 1;
      totalTimeSpent.value += 1;

      // Periodically sync progress every 15 seconds
      if (moduleTimer.value % 15 === 0) {
        try {
          isSyncing.value = true;
          await trainingApi.recordProgress(activeAssignment.value.id, {
            time_spent_delta_seconds: 15,
            reason_for_change: "Heartbeat training time-on-page recording",
          });
          syncError.value = false;
          syncErrorMessage.value = "";
        } catch (err) {
          syncError.value = true;
          syncErrorMessage.value =
            err?.message ||
            "Failed to record training progress heartbeat. Time tracking has been paused.";
        } finally {
          isSyncing.value = false;
        }
      }
    }
  }, 1000);
}

onMounted(() => {
  refreshData();
  startTimer();
});

// Batch Curriculum Assignment State. The audience comes from the study's delegation log (#5400): the sites and roles
// offered are the ones staff actually hold, nothing is pre-selected, and nothing is offered without an active curriculum.
const distinctSorted = (values) =>
  [...new Set(values.filter(Boolean))].sort((a, b) => a.localeCompare(b));
const availableSites = computed(() =>
  distinctSorted(delegations.value.map((d) => d.site_id))
);
const availableRoles = computed(() =>
  distinctSorted(delegations.value.map((d) => d.role))
);
const batchForm = ref({
  selectedSites: [],
  selectedRoles: [],
  reasonForChange: "",
});
const showBatchModal = ref(false);
const submittingBatch = ref(false);
const batchResult = ref(null);
const batchError = ref("");

// The people a batch would reach: distinct staff per site on the delegation log matching the selected filters.
const computedBatchCount = computed(() => {
  const sites = batchForm.value.selectedSites;
  const roles = batchForm.value.selectedRoles;
  if (sites.length === 0 && roles.length === 0) return 0;
  const reached = new Set(
    delegations.value
      .filter((d) => sites.length === 0 || sites.includes(d.site_id))
      .filter((d) => roles.length === 0 || roles.includes(d.role))
      .map((d) => `${d.staff_user_id}|${d.site_id}`)
  );
  return reached.size;
});

async function handleExecuteBatchAssign() {
  if (!activeCurriculum.value) return;
  submittingBatch.value = true;
  batchError.value = "";
  try {
    const res = await trainingApi.batchAssignCurriculum({
      curriculum_id: activeCurriculum.value.id,
      study_id: activeCurriculum.value.study_id,
      site_ids: batchForm.value.selectedSites,
      roles: batchForm.value.selectedRoles,
      reason_for_change: batchForm.value.reasonForChange,
    });
    batchResult.value = res;
    showBatchModal.value = false;
    await refreshData();
  } catch (err) {
    batchError.value = err?.message || "The batch assignment was refused.";
    console.error("Batch assignment error:", err);
  } finally {
    submittingBatch.value = false;
  }
}

// Bulk Delegation Log Sign-Off State
const selectedDelegationIds = ref([]);
const loadingDelegations = ref(false);
const showBulkSignModal = ref(false);
const submittingBulkSign = ref(false);
const bulkSignResult = ref(null);
const bulkSignForm = ref({
  signerName: authStore.identity?.username || "",
  signerRole: authStore.activeRole || "",
  password: "",
  reasonForChange:
    "21 CFR Part 11 Principal Investigator bulk delegation sign-off",
});

const isAllDelegationsSelected = computed(() => {
  const pending = delegations.value.filter((d) => d.status !== "COMPLETED");
  return (
    pending.length > 0 && selectedDelegationIds.value.length === pending.length
  );
});

function toggleSelectAllDelegations() {
  const pending = delegations.value.filter((d) => d.status !== "COMPLETED");
  if (selectedDelegationIds.value.length === pending.length) {
    selectedDelegationIds.value = [];
  } else {
    selectedDelegationIds.value = pending.map((d) => d.id);
  }
}

async function loadDelegations() {
  const studyId = activeCurriculum.value?.study_id;
  if (!studyId) {
    delegations.value = [];
    return;
  }
  loadingDelegations.value = true;
  try {
    const res = await trainingApi.listDelegations({ study_id: studyId });
    delegations.value = res;
  } catch (err) {
    console.error("Failed to load delegations:", err);
  } finally {
    loadingDelegations.value = false;
  }
}

// The site of the selected delegations when they share one; a sign-off across sites names none.
function singleSite(delegationIds) {
  const sites = new Set(
    delegations.value
      .filter((d) => delegationIds.includes(d.id))
      .map((d) => d.site_id)
  );
  return sites.size === 1 ? [...sites][0] : null;
}

async function handleExecuteBulkSignOff() {
  if (selectedDelegationIds.value.length === 0) return;
  submittingBulkSign.value = true;
  try {
    const delegationIds = [...selectedDelegationIds.value];
    const verification = await soaClient.verifySignature({
      username: authStore.identity?.username || "",
      password: bulkSignForm.value.password,
      action: DELEGATION_SIGN_OFF_ACTION,
      semanticAction: DELEGATION_SIGN_OFF_SEMANTIC_ACTION,
      batchId: await delegationBatchBindingId(delegationIds),
    });
    bulkSignForm.value.password = "";
    const res = await trainingApi.bulkSignOffDelegations(
      {
        delegation_ids: delegationIds,
        study_id: activeCurriculum.value?.study_id,
        site_id: singleSite(delegationIds),
        signature_payload: {
          meaning:
            "I confirm that I have reviewed and approved the delegated duties for the selected site staff personnel in accordance with 21 CFR Part 11 and ICH GCP E6(R2).",
          reason_for_change: bulkSignForm.value.reasonForChange,
        },
      },
      verification.sig_token
    );
    bulkSignResult.value = res;
    showBulkSignModal.value = false;
    selectedDelegationIds.value = [];
    await loadDelegations();
  } catch (err) {
    console.error("Bulk sign-off error:", err);
  } finally {
    submittingBulkSign.value = false;
  }
}

onUnmounted(() => {
  if (timerInterval) clearInterval(timerInterval);
});
</script>

<style scoped>
.training-console-card {
  padding: var(--spacing-lg, 24px);
  background: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md, 16px);
  font-family: inherit;
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  border-bottom: 1px solid var(--border, #e2e8f0);
  padding-bottom: var(--spacing-md, 16px);
}

.badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 6px;
  flex-wrap: wrap;
}

.badge-gxp {
  background: #f1f5f9;
  color: #334155;
  font-weight: 700;
}

.badge-regulatory {
  background: #e0f2fe;
  color: #0369a1;
  font-weight: 600;
}

.badge-locked {
  background: #fee2e2;
  color: #991b1b;
  font-weight: 700;
}

.badge-unlocked {
  background: #dcfce7;
  color: #15803d;
  font-weight: 700;
}

.console-title {
  font-size: 1.35rem;
  font-weight: 700;
  color: var(--text-primary, #0f172a);
  margin: 0 0 4px 0;
}

.console-subtitle {
  font-size: 0.9rem;
  color: var(--text-secondary, #64748b);
  margin: 0;
}

.assignment-selector-panel {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  background: var(--surface-secondary, #f8fafc);
  padding: 12px 16px;
  border-radius: var(--radius-sm, 6px);
  border: 1px solid var(--border, #e2e8f0);
  flex-wrap: wrap;
}

.select-input {
  padding: 8px 12px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-sm, 4px);
  background: #ffffff;
  font-size: 0.9rem;
  min-width: 320px;
}

.status-summary-chips {
  display: flex;
  gap: 12px;
}

.summary-chip {
  display: flex;
  flex-direction: column;
  background: #ffffff;
  border: 1px solid var(--border, #e2e8f0);
  padding: 4px 10px;
  border-radius: 4px;
}

.chip-label {
  font-size: 0.75rem;
  color: var(--text-secondary, #64748b);
}

.chip-value {
  font-size: 0.85rem;
  font-weight: 700;
}

.status-completed {
  color: #15803d;
}

.status-in_progress {
  color: #0369a1;
}

.status-pending {
  color: #b45309;
}

.status-failed {
  color: #b91c1c;
}

.tab-bar {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid var(--border, #e2e8f0);
  margin-bottom: 16px;
}

.tab-btn {
  padding: 10px 18px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-weight: 600;
  font-size: 0.95rem;
  color: var(--text-secondary, #64748b);
  cursor: pointer;
  margin-bottom: -2px;
}

.tab-btn.active {
  color: var(--primary, #2563eb);
  border-bottom-color: var(--primary, #2563eb);
}

.modules-layout {
  display: grid;
  grid-template-columns: 280px 1fr;
  gap: 20px;
}

.module-sidebar {
  border-right: 1px solid var(--border, #e2e8f0);
  padding-right: 16px;
}

.sidebar-heading {
  font-size: 0.9rem;
  text-transform: uppercase;
  color: var(--text-secondary, #64748b);
  margin-top: 0;
  margin-bottom: 10px;
}

.module-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.module-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 12px;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.2s;
}

.module-item:hover {
  background: #f1f5f9;
}

.module-item.active {
  background: #eff6ff;
  border-color: #bfdbfe;
}

.module-num {
  font-weight: 700;
  color: var(--primary, #2563eb);
}

.module-meta {
  display: flex;
  flex-direction: column;
}

.module-title {
  font-size: 0.85rem;
  font-weight: 600;
}

.badge-sm {
  font-size: 0.7rem;
  padding: 2px 6px;
  border-radius: 4px;
}

.badge-mod-procedure_delta {
  background: #fef3c7;
  color: #92400e;
}

.badge-mod-protocol_overview {
  background: #e0f2fe;
  color: #0369a1;
}

.module-reader {
  display: flex;
  flex-direction: column;
}

.reader-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--border, #e2e8f0);
  padding-bottom: 12px;
  margin-bottom: 16px;
}

.reader-title {
  margin: 4px 0 0 0;
  font-size: 1.15rem;
}

.content-text {
  white-space: pre-wrap;
  font-family: inherit;
  font-size: 0.95rem;
  line-height: 1.6;
  background: #f8fafc;
  padding: 16px;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
}

.reader-footer {
  display: flex;
  justify-content: space-between;
  margin-top: 20px;
}

/* Quiz Styles */
.quiz-header-card {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  padding: 16px;
  border-radius: 6px;
  margin-bottom: 20px;
}

.quiz-score-badge {
  text-align: center;
  padding: 10px 20px;
  border-radius: 6px;
}

.score-passed {
  background: #dcfce7;
  color: #166534;
  border: 1px solid #86efac;
}

.score-failed {
  background: #fee2e2;
  color: #991b1b;
  border: 1px solid #fca5a5;
}

.score-number {
  font-size: 1.8rem;
  font-weight: 800;
}

.score-label {
  font-size: 0.8rem;
  font-weight: 700;
}

.quiz-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 16px;
  margin-bottom: 16px;
}

.question-title {
  font-weight: 600;
  font-size: 1rem;
  margin-bottom: 12px;
}

.options-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.option-item {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 10px 14px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
}

.option-item.selected {
  background: #eff6ff;
  border-color: #3b82f6;
}

.option-item.correct {
  background: #dcfce7;
  border-color: #22c55e;
}

.option-item.incorrect {
  background: #fee2e2;
  border-color: #ef4444;
}

.rationale-box {
  margin-top: 10px;
  background: #f8fafc;
  padding: 8px 12px;
  border-radius: 4px;
  font-size: 0.85rem;
  color: #475569;
}

.quiz-actions-bar {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 20px;
}

/* Certificate Styles */
.certificate-paper {
  background: #ffffff;
  border: 2px solid #94a3b8;
  padding: 32px;
  border-radius: 8px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
}

.cert-header {
  text-align: center;
  border-bottom: 2px solid #0f172a;
  padding-bottom: 16px;
  margin-bottom: 24px;
}

.cert-brand {
  font-size: 0.85rem;
  font-weight: 700;
  letter-spacing: 1px;
  color: #64748b;
}

.cert-title {
  font-size: 1.6rem;
  font-weight: 800;
  color: #0f172a;
  margin: 6px 0;
}

.cert-subtitle {
  font-size: 0.85rem;
  color: #475569;
}

.cert-lead {
  font-size: 1rem;
  line-height: 1.5;
}

.cert-table {
  width: 100%;
  border-collapse: collapse;
  margin: 20px 0;
}

.cert-table td {
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  font-size: 0.9rem;
}

.cert-signature-manifest {
  background: #f1f5f9;
  border: 1px solid #cbd5e1;
  padding: 14px;
  border-radius: 6px;
  margin-top: 20px;
}

.manifest-header {
  font-weight: 700;
  margin-bottom: 8px;
  font-size: 0.9rem;
}

.manifest-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px 16px;
  font-size: 0.85rem;
}

.cert-routing-status {
  display: flex;
  gap: 12px;
  margin-top: 20px;
}

.routing-badge {
  font-size: 0.8rem;
  font-weight: 600;
  padding: 6px 12px;
  border-radius: 4px;
  background: #f8fafc;
  border: 1px solid #cbd5e1;
}

/* Modal */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-card {
  background: #ffffff;
  width: 520px;
  max-width: 90vw;
  border-radius: 8px;
  padding: 24px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.modal-close-btn {
  background: none;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
}

.modal-description {
  font-size: 0.85rem;
  color: #475569;
  line-height: 1.4;
  margin-bottom: 16px;
}

.text-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  margin-top: 4px;
  font-size: 0.9rem;
}

.statement-box {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  padding: 10px 12px;
  border-radius: 4px;
  font-style: italic;
  font-size: 0.85rem;
  color: #334155;
  margin-top: 4px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 20px;
}

/* Inline Sync Error Banner Styles */
.sync-error-banner {
  display: flex;
  align-items: center;
  justify-content: space-between;
  background-color: #fef2f2;
  border: 1px solid #fca5a5;
  color: #991b1b;
  padding: 12px 16px;
  border-radius: var(--radius-sm, 6px);
  gap: 12px;
  margin-bottom: 16px;
}

.banner-content {
  display: flex;
  align-items: center;
  gap: 12px;
}

.banner-icon {
  font-size: 1.25rem;
}

.banner-text {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.banner-title {
  font-size: 0.9rem;
  font-weight: 700;
}

.banner-message {
  font-size: 0.85rem;
  color: #7f1d1d;
}

.btn-retry {
  background-color: #dc2626;
  color: #ffffff;
  border: none;
  padding: 6px 14px;
  border-radius: 4px;
  font-weight: 600;
  cursor: pointer;
  white-space: nowrap;
}

.btn-retry:hover:not(:disabled) {
  background-color: #b91c1c;
}

.btn-retry:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

/* Batch & Delegation Layouts */
.batch-layout,
.delegations-layout {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.batch-card,
.delegations-card {
  padding: 20px;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.section-desc {
  font-size: 0.9rem;
  color: #64748b;
  margin-top: 4px;
}

.batch-filter-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
  background: #f8fafc;
  padding: 16px;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
}

.filter-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: #1e293b;
  margin-bottom: 8px;
  display: block;
}

.checkbox-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: #334155;
  cursor: pointer;
}

.batch-preview-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #eff6ff;
  border: 1px solid #bfdbfe;
  padding: 12px 16px;
  border-radius: 6px;
}

.preview-info {
  display: flex;
  align-items: center;
  gap: 12px;
  font-size: 0.9rem;
  color: #1e3a8a;
}

.badge-error {
  background: #fef2f2;
  color: #991b1b;
  border: 1px solid #fca5a5;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
}

.batch-result-card {
  background: #f8fafc;
  border: 1px solid #cbd5e1;
  padding: 16px;
  border-radius: 6px;
}

.result-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.result-stats {
  display: flex;
  gap: 12px;
  margin-bottom: 12px;
}

.stat-pill {
  padding: 6px 12px;
  border-radius: 4px;
  font-weight: 700;
  font-size: 0.85rem;
}

.success-pill {
  background: #dcfce7;
  color: #166534;
}
.warning-pill {
  background: #fef3c7;
  color: #92400e;
}
.info-pill {
  background: #e0f2fe;
  color: #075985;
}

.skipped-table-wrapper {
  margin-top: 12px;
}

.delegation-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.delegation-actions-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 16px;
  background: #f1f5f9;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
}

.seal-hash {
  background: #f1f5f9;
  color: #0284c7;
  padding: 2px 6px;
  border-radius: 4px;
  font-family: monospace;
  font-size: 0.8rem;
}

.badge-role {
  background: #f1f5f9;
  color: #334155;
  font-size: 0.75rem;
}
</style>
