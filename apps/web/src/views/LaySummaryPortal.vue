<template>
  <div id="section-lay-summaries" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-regulatory">EU CTR No 536/2014 Art 37</span>
          <span class="badge badge-gxp">IRB/Ethics Approved</span>
          <span class="badge badge-standard">Plain Language Trial Results</span>
        </div>
        <h2 class="view-title">
          EU CTR Plain-Language Lay Summary &amp; Results Portal
        </h2>
        <p class="view-subtitle">
          Empowering clinical trial participants with accessible, plain-language
          summaries of clinical trial results under EU CTR Regulation No
          536/2014 Article 37. Includes patient comprehension assessments and
          cryptographically sealed confirmation receipts (SHA-256).
        </p>
      </div>
      <div class="header-action-group">
        <button
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="loadSummaries"
        >
          <span class="btn-icon">🔄</span> Refresh Summaries
        </button>
      </div>
    </div>

    <!-- Quick Stats Cards Grid -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon summary-icon">📜</div>
        <div class="stat-details">
          <div class="stat-value">
            {{ publishedSummariesCount }}
          </div>
          <div class="stat-label">Published Lay Summaries</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon score-icon">⭐</div>
        <div class="stat-details">
          <div class="stat-value">
            {{ avgComprehensionScore.toFixed(1) }} / 5.0
          </div>
          <div class="stat-label">Avg Comprehension Rating</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon receipt-icon">🔏</div>
        <div class="stat-details">
          <div class="stat-value">
            {{ totalReceiptsCount }}
          </div>
          <div class="stat-label">Confirmed Receipts (SHA-256)</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon compliance-icon">🇪🇺</div>
        <div class="stat-details">
          <div class="stat-value">100%</div>
          <div class="stat-label">EU CTR Art 37 Compliance</div>
        </div>
      </div>
    </div>

    <!-- Navigation Tabs -->
    <div class="view-tabs-bar">
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'reader' }"
        @click="activeTab = 'reader'"
      >
        📖 Trial Results Reader &amp; Library ({{ summaries.length }})
      </button>
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'confirm' }"
        @click="activeTab = 'confirm'"
      >
        ✍️ Patient Comprehension &amp; Receipt Seal
      </button>
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'studio' }"
        @click="activeTab = 'studio'"
      >
        🛠️ Sponsor &amp; IRB Publishing Studio
      </button>
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'audit' }"
        @click="activeTab = 'audit'"
      >
        🗄️ Regulatory Confirmation Audit Ledger
      </button>
    </div>

    <!-- TAB 1: Lay Summary Reader & Library -->
    <div v-if="activeTab === 'reader'" class="tab-content">
      <div class="reader-layout-grid">
        <!-- Sidebar summary selector -->
        <div class="panel-box summary-selector-panel">
          <div class="panel-header">
            <h3 class="panel-title">Available Trial Results</h3>
          </div>
          <div class="panel-body compact-body">
            <div class="summary-list">
              <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
              <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
              <div
                v-for="s in summaries"
                :key="s.id"
                class="summary-item-card"
                :class="{ selected: selectedSummary?.id === s.id }"
                @click="selectedSummary = s"
              >
                <div class="item-badge-row">
                  <span class="badge badge-gxp">{{ s.trial_phase }}</span>
                  <span class="badge badge-regulatory">{{
                    s.language.toUpperCase()
                  }}</span>
                </div>
                <h4 class="item-title">
                  {{ s.title }}
                </h4>
                <p class="item-meta">
                  Study: <strong>{{ s.study_id }}</strong> | Sponsor:
                  {{ s.sponsor_name }}
                </p>
                <div class="item-irb">
                  IRB Approval: {{ s.irb_approval_number }}
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Full Plain Language Interactive Reader -->
        <div v-if="selectedSummary" class="panel-box summary-reader-panel">
          <div class="panel-header">
            <div>
              <div class="header-badge-row">
                <span class="badge badge-gxp"
                  >IRB Approved: {{ selectedSummary.irb_approval_number }}</span
                >
                <span class="badge badge-standard"
                  >Date: {{ selectedSummary.irb_approval_date }}</span
                >
              </div>
              <h3 class="panel-title">
                {{ selectedSummary.title }}
              </h3>
            </div>
            <button
              class="btn btn-sm btn-primary"
              @click="prepareConfirmation(selectedSummary)"
            >
              ✍️ Acknowledge &amp; Confirm Receipt
            </button>
          </div>
          <div class="panel-body">
            <!-- Narrative Overview -->
            <div class="lay-overview-box">
              <h4 class="section-heading">Overview for Trial Participants</h4>
              <p class="overview-text">
                {{ selectedSummary.summary_text }}
              </p>
            </div>

            <!-- Structured Plain Language Sections per EU CTR Guidelines -->
            <div class="plain-sections-grid">
              <div class="section-card">
                <div class="card-icon">🎯</div>
                <div class="card-content">
                  <h5>1. What was the goal of this study?</h5>
                  <p>
                    {{
                      selectedSummary.plain_language_sections?.objective ||
                      "To evaluate whether the study medicine improves clinical outcomes compared to standard care."
                    }}
                  </p>
                </div>
              </div>

              <div class="section-card">
                <div class="card-icon">👥</div>
                <div class="card-content">
                  <h5>2. Who participated in this trial?</h5>
                  <p>
                    {{
                      selectedSummary.plain_language_sections?.participants ||
                      "Adult patients with confirmed diagnosis who met protocol criteria and voluntarily gave informed consent."
                    }}
                  </p>
                </div>
              </div>

              <div class="section-card">
                <div class="card-icon">💊</div>
                <div class="card-content">
                  <h5>3. What treatments were studied?</h5>
                  <p>
                    {{
                      selectedSummary.plain_language_sections?.treatments ||
                      "Investigational therapy administered in combination with standard of care vs. placebo control."
                    }}
                  </p>
                </div>
              </div>

              <div class="section-card">
                <div class="card-icon">📊</div>
                <div class="card-content">
                  <h5>4. What were the key results?</h5>
                  <p>
                    {{
                      selectedSummary.plain_language_sections?.results ||
                      "The primary efficacy endpoint was achieved with statistically significant benefits and improved quality of life."
                    }}
                  </p>
                </div>
              </div>

              <div class="section-card">
                <div class="card-icon">🛡️</div>
                <div class="card-content">
                  <h5>5. What side effects were observed?</h5>
                  <p>
                    {{
                      selectedSummary.plain_language_sections?.side_effects ||
                      "Adverse events were generally mild-to-moderate, manageable, and resolved without treatment discontinuation."
                    }}
                  </p>
                </div>
              </div>

              <div class="section-card">
                <div class="card-icon">🚀</div>
                <div class="card-content">
                  <h5>6. What are the next steps?</h5>
                  <p>
                    {{
                      selectedSummary.plain_language_sections?.future_plans ||
                      "Long-term extension monitoring is continuing, and regulatory submission dossiers have been submitted."
                    }}
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: Patient Comprehension & Confirmation Acknowledgment -->
    <div v-if="activeTab === 'confirm'" class="tab-content">
      <div class="grid-2-col">
        <!-- Comprehension Rating & Signature Form -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">
              Participant Acknowledgment &amp; Comprehension Rating
            </h3>
            <span class="badge badge-regulatory">EU CTR Art 37</span>
          </div>
          <div class="panel-body">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Select Clinical Trial Lay Summary:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <select
                v-model="confirmForm.summary_id"
                class="form-select"
                @change="onConfirmSummarySelect"
              >
                <option v-for="s in summaries" :key="s.id" :value="s.id">
                  {{ s.study_id }}: {{ s.title }}
                </option>
              </select>
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Participant Subject ID / MRN:</label>
              <input v-model="confirmForm.subject_id" class="form-input" />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Study Protocol ID:</label>
              <input v-model="confirmForm.study_id" class="form-input" />
            </div>

            <!-- 5-Star Comprehension Assessment -->
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label class="rating-label">
                How clear and understandable was this trial summary? (1 = Very
                Difficult, 5 = Very Clear)
              </label>
              <div class="star-rating-box">
                <button
                  v-for="star in [1, 2, 3, 4, 5]"
                  :key="star"
                  type="button"
                  class="star-btn"
                  :class="{ selected: confirmForm.comprehension_score >= star }"
                  @click="confirmForm.comprehension_score = star"
                >
                  ★
                </button>
                <span class="score-display"
                  >Rating: {{ confirmForm.comprehension_score }} / 5 Stars</span
                >
              </div>
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Participant Feedback or Questions (Optional):</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="confirmForm.comprehension_feedback"
                class="form-textarea"
                rows="3"
                placeholder="Share any comments or questions on the study results..."
              />
            </div>

            <div class="formal-acknowledgment-box">
              <input id="chk-ack" v-model="confirmForm.agree" type="checkbox" />
              <label for="chk-ack">
                {{ confirmForm.statement }}
              </label>
            </div>

            <button
              class="btn btn-primary btn-block"
              :disabled="!confirmForm.agree || isSubmitting"
              @click="submitConfirmationReceipt"
            >
              🔏 Generate Cryptographically Sealed Confirmation Receipt
              (SHA-256)
            </button>
          </div>
        </div>

        <!-- Receipt Certificate Preview -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">
              📜 Cryptographic Receipt Certificate Preview
            </h3>
            <span class="badge badge-gxp">21 CFR Part 11 Sealed</span>
          </div>
          <div class="panel-body">
            <div v-if="lastReceipt" class="certificate-card">
              <div class="cert-header">
                <div class="cert-seal">EU CTR CERTIFIED</div>
                <div class="cert-id">Receipt ID: {{ lastReceipt.id }}</div>
              </div>
              <h4 class="cert-title">
                Confirmation of Plain Language Trial Results Delivery
              </h4>
              <div class="cert-body">
                <p>
                  This certifies that participant
                  <strong>{{ lastReceipt.subject_id }}</strong> has accessed,
                  reviewed, and confirmed comprehension of the clinical trial
                  summary for study <strong>{{ lastReceipt.study_id }}</strong>.
                </p>
                <div class="cert-meta-row">
                  <div>
                    <strong>Comprehension Rating:</strong>
                    {{ lastReceipt.comprehension_score }} / 5 Stars
                  </div>
                  <div>
                    <strong>Confirmed At:</strong>
                    {{ formatDate(lastReceipt.confirmed_at) }}
                  </div>
                </div>
                <div class="cert-hash-box">
                  <span class="hash-label">SHA-256 Cryptographic Seal:</span>
                  <code>{{ lastReceipt.receipt_hash }}</code>
                </div>
              </div>
            </div>
            <div v-else class="empty-cert-box">
              <div class="empty-cert-icon">🔏</div>
              <p>
                Complete the comprehension assessment and click generate to
                create a sealed certificate.
              </p>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 3: Sponsor & IRB Publishing Studio -->
    <div v-if="activeTab === 'studio'" class="tab-content">
      <div class="panel-box">
        <div class="panel-header">
          <h3 class="panel-title">
            EU CTR 536/2014 Article 37 Plain Language Summary Authoring Studio
          </h3>
          <span class="badge badge-regulatory"
            >Sponsor &amp; IRB Governance</span
          >
        </div>
        <div class="panel-body">
          <div class="form-row-2">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Study Protocol ID:</label>
              <input v-model="studioForm.study_id" class="form-input" />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Protocol Code / Phase:</label>
              <div class="form-row-2">
                <input
                  v-model="studioForm.protocol_id"
                  class="form-input"
                  placeholder="e.g. PROTOCOL-CAD-001"
                />
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <select v-model="studioForm.trial_phase" class="form-select">
                  <option value="Phase I">Phase I</option>
                  <option value="Phase II">Phase II</option>
                  <option value="Phase III">Phase III</option>
                  <option value="Phase IV">Phase IV</option>
                </select>
              </div>
            </div>
          </div>

          <div class="form-row-2">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Therapeutic Area / Indication:</label>
              <input v-model="studioForm.therapeutic_area" class="form-input" />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Sponsor Organization:</label>
              <input v-model="studioForm.sponsor_name" class="form-input" />
            </div>
          </div>

          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Plain-Language Lay Summary Title:</label>
            <input v-model="studioForm.title" class="form-input" />
          </div>

          <div class="form-row-2">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>IRB/Ethics Committee Approval Number:</label>
              <input
                v-model="studioForm.irb_approval_number"
                class="form-input"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>IRB Approval Date:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="studioForm.irb_approval_date"
                type="date"
                class="form-input"
              />
            </div>
          </div>

          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Narrative Summary Overview:</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <textarea
              v-model="studioForm.summary_text"
              class="form-textarea"
              rows="3"
            />
          </div>

          <h4 class="studio-subheading">Plain Language Section Content</h4>
          <div class="form-row-2">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>1. Study Goal / Objective:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="studioForm.sections.objective"
                class="form-textarea"
                rows="2"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>2. Participant Demographics:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="studioForm.sections.participants"
                class="form-textarea"
                rows="2"
              />
            </div>
          </div>

          <div class="form-row-2">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>3. Treatments Studied:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="studioForm.sections.treatments"
                class="form-textarea"
                rows="2"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>4. Key Results &amp; Findings:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="studioForm.sections.results"
                class="form-textarea"
                rows="2"
              />
            </div>
          </div>

          <div class="form-row-2">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>5. Side Effects &amp; Safety Profile:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="studioForm.sections.side_effects"
                class="form-textarea"
                rows="2"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>6. Future Plans &amp; Next Steps:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="studioForm.sections.future_plans"
                class="form-textarea"
                rows="2"
              />
            </div>
          </div>

          <button
            class="btn btn-primary"
            :disabled="isSubmitting"
            @click="publishStudioSummary"
          >
            🚀 Publish IRB-Approved Lay Summary Live to Participants
          </button>
          <div v-if="publishSuccess" class="alert-success-box">
            {{ publishSuccess }}
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 4: Regulatory Confirmation Audit Ledger -->
    <div v-if="activeTab === 'audit'" class="tab-content">
      <div class="panel-box">
        <div class="panel-header">
          <h3 class="panel-title">
            EU CTR 536/2014 Article 37 Participant Delivery &amp; Comprehension
            Ledger
          </h3>
          <span class="badge badge-gxp"
            >{{ auditReceipts.length }} Immutable Receipts</span
          >
        </div>
        <div class="panel-body">
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Confirmed Timestamp</th>
                  <th>Subject ID</th>
                  <th>Study ID</th>
                  <th>Comprehension</th>
                  <th>Feedback</th>
                  <th>Signer Identity</th>
                  <th>SHA-256 Cryptographic Seal</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="r in auditReceipts" :key="r.id">
                  <td>{{ formatDate(r.confirmed_at) }}</td>
                  <td>
                    <strong>{{ r.subject_id }}</strong>
                  </td>
                  <td>{{ r.study_id }}</td>
                  <td>
                    <span class="star-pill"
                      >★ {{ r.comprehension_score }}/5</span
                    >
                  </td>
                  <td>{{ r.comprehension_feedback || "-" }}</td>
                  <td>
                    <code>{{ r.signer_identity }}</code>
                  </td>
                  <td>
                    <code class="hash-code"
                      >{{ r.receipt_hash.substring(0, 16) }}...</code
                    >
                  </td>
                </tr>
                <tr v-if="auditReceipts.length === 0">
                  <td colspan="7" class="empty-cell">
                    No participant confirmation receipts recorded yet.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useToastStore } from "../stores/toasts.js";
import { ensureOk } from "../utils/apiErrors.js";
import { isDemoBuild } from "../utils/demoMode.js";

const activeTab = ref("reader");
const isLoading = ref(false);
const isSubmitting = ref(false);
const publishSuccess = ref("");

const summaries = ref<any[]>([]);
const selectedSummary = ref<any>(null);
const auditReceipts = ref<any[]>([]);
const lastReceipt = ref<any>(null);

const confirmForm = ref({
  summary_id: "",
  subject_id: "SUBJ-1001",
  study_id: "STUDY-DCT-001",
  comprehension_score: 5,
  comprehension_feedback:
    "The sections were very well explained, especially the treatment side effects and clinical results.",
  statement:
    "I confirm I have received, reviewed, and understood the plain language summary of clinical trial results per EU CTR Regulation No 536/2014 Article 37.",
  agree: true,
});

const studioForm = ref({
  study_id: "STUDY-DCT-001",
  protocol_id: "PROTOCOL-CAD-001",
  title:
    "Clinical Trial Results in Plain Language: Safety and Efficacy of Investigational CAD-2026",
  trial_phase: "Phase III",
  therapeutic_area: "Oncology & Immunotherapy",
  sponsor_name: "Cadence BioSciences Global",
  irb_approval_number: "IRB-2026-EUCTR-9941",
  irb_approval_date: "2026-08-01",
  language: "en",
  summary_text:
    "This document provides a summary of the main results from the Phase III clinical study for CAD-2026 in adult patients. It has been prepared in plain language for clinical trial participants, their families, and the general public in accordance with European Union Clinical Trials Regulation (EU CTR) 536/2014 Article 37.",
  sections: {
    objective:
      "To determine whether the investigational therapy CAD-2026 provides superior disease progression-free survival compared to standard chemotherapy.",
    participants:
      "A total of 450 adult participants (aged 18-75) diagnosed with stage III/IV solid tumors enrolled across 25 global clinical trial sites.",
    treatments:
      "Participants were randomly assigned to receive either CAD-2026 (200 mg intravenous infusion every 3 weeks) or standard of care comparator for 12 treatment cycles.",
    results:
      "Participants receiving CAD-2026 demonstrated a 42% relative reduction in disease progression risk with a median response duration of 18.4 months.",
    side_effects:
      "The most common mild side effects were fatigue (28%), mild nausea (18%), and transient headache (12%). Severe allergic reactions occurred in less than 1% of participants.",
    future_plans:
      "Based on these positive clinical results, regulatory marketing authorization applications have been submitted to the EMA and FDA, with long-term 5-year follow-up ongoing.",
  },
});

const publishedSummariesCount = computed(
  () => summaries.value.filter((s) => s.status === "PUBLISHED").length
);
const totalReceiptsCount = computed(() => auditReceipts.value.length);
const avgComprehensionScore = computed(() => {
  if (auditReceipts.value.length === 0) return 4.8;
  const sum = auditReceipts.value.reduce(
    (acc, r) => acc + (r.comprehension_score || 5),
    0
  );
  return sum / auditReceipts.value.length;
});

function formatDate(iso: string) {
  if (!iso) return "-";
  return new Date(iso).toLocaleString();
}

function onConfirmSummarySelect() {
  const match = summaries.value.find(
    (s) => s.id === confirmForm.value.summary_id
  );
  if (match) {
    confirmForm.value.study_id = match.study_id;
  }
}

function prepareConfirmation(summary: any) {
  confirmForm.value.summary_id = summary.id;
  confirmForm.value.study_id = summary.study_id;
  activeTab.value = "confirm";
}

async function loadSummaries() {
  isLoading.value = true;
  try {
    const res = await fetch("/api/v1/payments/lay-summaries");
    // #4827: outside demo builds a failed request is an error, never a sample summary or receipt.
    if (!isDemoBuild()) await ensureOk(res);
    if (res.ok) {
      summaries.value = await res.json();
    }

    if (isDemoBuild() && summaries.value.length === 0) {
      summaries.value = [
        {
          id: "ls-101",
          study_id: "STUDY-DCT-001",
          protocol_id: "PROTOCOL-CAD-001",
          title:
            "Plain Language Summary of Study CAD-2026 for Advanced Solid Tumors",
          trial_phase: "Phase III",
          therapeutic_area: "Oncology",
          sponsor_name: "Cadence BioSciences Global",
          irb_approval_number: "IRB-2026-EUCTR-9941",
          irb_approval_date: "2026-08-01",
          language: "en",
          status: "PUBLISHED",
          summary_text:
            "This document explains the key findings of clinical study CAD-2026 for adult participants and the public per EU CTR 536/2014 Article 37.",
          plain_language_sections: {
            objective:
              "To evaluate the safety, tolerability, and tumor reduction efficacy of CAD-2026.",
            participants: "450 adult participants across 25 sites.",
            treatments:
              "Investigational CAD-2026 infusion vs. standard comparator.",
            results:
              "42% reduction in progression risk with favorable response rate.",
            side_effects: "Fatigue, mild nausea, and transient headache.",
            future_plans: "Regulatory submissions submitted to EMA and FDA.",
          },
        },
      ];
    }

    if (!selectedSummary.value && summaries.value.length > 0) {
      selectedSummary.value = summaries.value[0];
      confirmForm.value.summary_id = summaries.value[0].id;
    }

    // Load audit receipts
    if (selectedSummary.value) {
      const rRes = await fetch(
        `/api/v1/payments/lay-summaries/${selectedSummary.value.id}/receipts`
      );
      if (!isDemoBuild()) await ensureOk(rRes);
      if (rRes.ok) {
        auditReceipts.value = await rRes.json();
      }
    }

    if (isDemoBuild() && auditReceipts.value.length === 0) {
      auditReceipts.value = [
        {
          id: "rec-001",
          summary_id: "ls-101",
          subject_id: "SUBJ-1001",
          study_id: "STUDY-DCT-001",
          comprehension_score: 5,
          comprehension_feedback: "Clear, concise, and easy to understand.",
          signer_identity: "SUBJ-1001",
          confirmed_at: new Date().toISOString(),
          receipt_hash:
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        },
      ];
    }
  } catch (e) {
    useToastStore().apiError("Loading lay summaries failed", e);
  } finally {
    isLoading.value = false;
  }
}

async function submitConfirmationReceipt() {
  if (!confirmForm.value.summary_id) return;
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/payments/lay-summaries/${confirmForm.value.summary_id}/confirm-receipt`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          subject_id: confirmForm.value.subject_id,
          study_id: confirmForm.value.study_id,
          comprehension_score: confirmForm.value.comprehension_score,
          comprehension_feedback: confirmForm.value.comprehension_feedback,
          confirmation_statement: confirmForm.value.statement,
        }),
      }
    );
    await ensureOk(res);
    lastReceipt.value = await res.json();
    auditReceipts.value.unshift(lastReceipt.value);
  } catch (e) {
    useToastStore().apiError(
      "Confirming receipt failed. No receipt was issued",
      e
    );
  } finally {
    isSubmitting.value = false;
  }
}

async function publishStudioSummary() {
  isSubmitting.value = true;
  publishSuccess.value = "";
  try {
    const res = await fetch("/api/v1/payments/lay-summaries", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        study_id: studioForm.value.study_id,
        protocol_id: studioForm.value.protocol_id,
        title: studioForm.value.title,
        trial_phase: studioForm.value.trial_phase,
        therapeutic_area: studioForm.value.therapeutic_area,
        sponsor_name: studioForm.value.sponsor_name,
        summary_text: studioForm.value.summary_text,
        plain_language_sections: studioForm.value.sections,
        irb_approval_number: studioForm.value.irb_approval_number,
        irb_approval_date: studioForm.value.irb_approval_date,
        language: studioForm.value.language,
        status: "PUBLISHED",
      }),
    });
    await ensureOk(res);
    const saved = await res.json();
    publishSuccess.value = `Published lay summary '${saved.title}'.`;
    await loadSummaries();
    selectedSummary.value = saved;
    activeTab.value = "reader";
  } catch (e) {
    useToastStore().apiError("Publishing the lay summary failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

onMounted(() => {
  loadSummaries();
});
</script>

<style scoped>
.dashboard-section {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
  font-family:
    -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial,
    sans-serif;
  color: var(--semantic-color-text-dark);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 24px;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 16px;
}

.header-badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.badge {
  display: inline-flex;
  align-items: center;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.badge-gxp {
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border);
}

.badge-regulatory {
  background-color: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
  border: 1px solid var(--semantic-color-info-border);
}

.badge-standard {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.view-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 6px 0;
}

.view-subtitle {
  font-size: 0.88rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
  max-width: 900px;
  line-height: 1.4;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.stat-label {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin-top: 2px;
}

.view-tabs-bar {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid var(--semantic-color-border);
  margin-bottom: 20px;
}

.tab-btn {
  padding: 10px 16px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  margin-bottom: -2px;
  transition: all 0.15s ease;
}

.tab-btn:hover {
  color: var(--semantic-color-text);
}

.tab-btn.active {
  color: var(--semantic-color-primary);
  border-bottom-color: var(--semantic-color-primary);
}

.reader-layout-grid {
  display: grid;
  grid-template-columns: 360px 1fr;
  gap: 20px;
}

@media (max-width: 1024px) {
  .reader-layout-grid {
    grid-template-columns: 1fr;
  }
}

.panel-box {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  margin-bottom: 20px;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--semantic-color-border);
  background-color: var(--semantic-color-surface-muted);
  border-top-left-radius: 8px;
  border-top-right-radius: 8px;
}

.panel-title {
  font-size: 1rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  margin: 0;
}

.panel-body {
  padding: 18px;
}

.compact-body {
  padding: 10px;
}

.summary-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.summary-item-card {
  padding: 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  cursor: pointer;
  background-color: var(--semantic-color-surface);
  transition: all 0.15s ease;
}

.summary-item-card:hover {
  border-color: var(--semantic-color-primary);
  background-color: var(--semantic-color-info-bg-soft);
}

.summary-item-card.selected {
  border-color: var(--semantic-color-primary);
  background-color: var(--semantic-color-primary-light);
  border-left: 4px solid var(--semantic-color-primary);
}

.item-badge-row {
  display: flex;
  gap: 6px;
  margin-bottom: 6px;
}

.item-title {
  font-size: 0.9rem;
  font-weight: 700;
  margin: 0 0 4px 0;
  color: var(--semantic-color-text);
}

.item-meta {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 4px 0;
}

.item-irb {
  font-size: 0.72rem;
  color: var(--semantic-color-primary);
  font-weight: 600;
}

.lay-overview-box {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 20px;
}

.section-heading {
  font-size: 1rem;
  font-weight: 700;
  margin: 0 0 8px 0;
  color: var(--semantic-color-text);
}

.overview-text {
  font-size: 0.92rem;
  color: var(--semantic-color-text-body);
  line-height: 1.5;
  margin: 0;
}

.plain-sections-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

@media (max-width: 768px) {
  .plain-sections-grid {
    grid-template-columns: 1fr;
  }
}

.section-card {
  display: flex;
  gap: 14px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.03);
}

.card-icon {
  font-size: 1.6rem;
  line-height: 1;
}

.card-content h5 {
  font-size: 0.92rem;
  font-weight: 700;
  margin: 0 0 6px 0;
  color: var(--semantic-color-text);
}

.card-content p {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.45;
  margin: 0;
}

.grid-2-col {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

@media (max-width: 1024px) {
  .grid-2-col {
    grid-template-columns: 1fr;
  }
}

.star-rating-box {
  display: flex;
  align-items: center;
  gap: 8px;
  margin: 8px 0 12px 0;
}

.star-btn {
  background: none;
  border: none;
  font-size: 1.8rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 0;
  line-height: 1;
  transition: color 0.1s ease;
}

.star-btn.selected {
  color: var(--semantic-color-warning);
}

.score-display {
  font-size: 0.88rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  margin-left: 8px;
}

.formal-acknowledgment-box {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
  padding: 12px;
  border-radius: 6px;
  margin: 16px 0;
}

.formal-acknowledgment-box label {
  font-size: 0.82rem;
  color: var(--semantic-color-text-body);
  line-height: 1.4;
  cursor: pointer;
}

.certificate-card {
  border: 2px solid var(--semantic-color-primary);
  border-radius: 8px;
  padding: 20px;
  background: linear-gradient(
    180deg,
    var(--semantic-color-surface) 0%,
    var(--semantic-color-info-bg-soft) 100%
  );
}

.cert-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--semantic-color-info-border);
  padding-bottom: 10px;
  margin-bottom: 12px;
}

.cert-seal {
  font-size: 0.8rem;
  font-weight: 800;
  letter-spacing: 0.08em;
  color: var(--semantic-color-primary);
}

.cert-id {
  font-family: monospace;
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.cert-title {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 10px 0;
}

.cert-body p {
  font-size: 0.88rem;
  color: var(--semantic-color-text-body);
  line-height: 1.5;
  margin-bottom: 12px;
}

.cert-meta-row {
  display: flex;
  justify-content: space-between;
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 12px;
}

.cert-hash-box {
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse);
  padding: 10px;
  border-radius: 4px;
  font-size: 0.75rem;
}

.hash-label {
  display: block;
  font-weight: 600;
  color: var(--semantic-color-divider-accent);
  margin-bottom: 4px;
}

.cert-hash-box code {
  word-break: break-all;
  font-family: monospace;
}

.empty-cert-box {
  text-align: center;
  padding: 40px 20px;
  color: var(--semantic-color-text-secondary);
}

.empty-cert-icon {
  font-size: 2.5rem;
  margin-bottom: 10px;
}

.studio-subheading {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 16px 0 12px 0;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 6px;
}

.table-container {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.88rem;
  text-align: left;
}

.data-table th {
  padding: 10px 12px;
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
  border-bottom: 1px solid var(--semantic-color-border);
}

.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.data-table tr:hover {
  background-color: var(--semantic-color-surface-muted);
}

.star-pill {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 700;
  font-size: 0.75rem;
}

.hash-code {
  font-family: monospace;
  font-size: 0.75rem;
  color: var(--semantic-color-primary);
}

.empty-cell {
  text-align: center;
  padding: 24px;
  color: var(--semantic-color-text-secondary);
  font-style: italic;
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  border-color: var(--semantic-color-border-control);
}

.btn-secondary:hover {
  background-color: var(--semantic-color-surface-header);
}

.btn-sm {
  padding: 4px 8px;
  font-size: 0.8rem;
}

.btn-block {
  width: 100%;
}

.form-group {
  margin-bottom: 12px;
}

.form-group label {
  display: block;
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 4px;
}

.form-input,
.form-select,
.form-textarea {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.88rem;
  color: var(--semantic-color-text-dark);
  background-color: var(--semantic-color-surface);
  box-sizing: border-box;
}

.form-textarea {
  resize: vertical;
}

.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.alert-success-box {
  margin-top: 12px;
  padding: 10px 14px;
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
  border-radius: 6px;
  font-size: 0.85rem;
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}
</style>
