<template>
  <div class="safety-narrative-reviewer" role="region" aria-label="Generative Pharmacovigilance Safety Narrative Reviewer">
    <!-- Top Action Bar & Header -->
    <header class="narrative-header">
      <div class="header-main">
        <div class="title-group">
          <span class="badge badge-regulatory">ICH E2B(R3)</span>
          <h2 class="narrative-title">{{ narrative.title || 'Serious Adverse Event Safety Narrative' }}</h2>
        </div>
        <div class="header-metadata">
          <span class="meta-item"><strong>Subject:</strong> {{ narrative.subject_id }}</span>
          <span class="meta-item"><strong>Study:</strong> {{ narrative.study_id }}</span>
          <span class="meta-item"><strong>SAE Key:</strong> {{ narrative.sae_event_key }}</span>
          <span class="meta-item"><strong>Status:</strong>
            <span :class="['status-pill', narrative.review_status.toLowerCase()]">
              {{ narrative.review_status }}
            </span>
          </span>
          <span v-if="narrative.confidence_score" class="confidence-tag">
            Confidence: {{ Math.round(narrative.confidence_score * 100) }}%
          </span>
        </div>
      </div>

      <div class="header-actions">
        <button
          v-if="narrative.review_status !== 'APPROVED'"
          class="btn btn-primary"
          @click="openSignatureModal"
          :disabled="isSubmitting"
        >
          <span class="icon">✍️</span> Part 11 Sign & Approve
        </button>
        <button
          v-if="narrative.review_status === 'APPROVED'"
          class="btn btn-secondary"
          @click="exportE2BXml"
          :disabled="isExporting"
        >
          <span class="icon">📄</span> Export E2B(R3) XML
        </button>
      </div>
    </header>

    <!-- Main Side-by-Side Review Grid -->
    <main class="review-grid">
      <!-- Left Column: Chronological Event Stream Timeline -->
      <section class="timeline-pane" aria-labelledby="timeline-pane-heading">
        <div class="pane-header">
          <h3 id="timeline-pane-heading">Clinical Event Timeline</h3>
          <span class="event-count">{{ narrative.timeline_events?.length || 0 }} Grounded Events</span>
        </div>
        <div class="timeline-scroll-area">
          <div
            v-for="event in narrative.timeline_events"
            :key="event.event_id"
            :id="`timeline-${event.event_id}`"
            :class="[
              'timeline-card',
              event.event_type.toLowerCase(),
              { 'is-highlighted': highlightedEventIds.includes(event.event_id) }
            ]"
          >
            <div class="card-top">
              <span class="event-type-badge">{{ event.event_type }}</span>
              <span class="event-date">{{ event.event_date || 'Date Unknown' }}</span>
            </div>
            <h4 class="event-title">{{ event.title }}</h4>
            <p class="event-desc">{{ event.description }}</p>
            <div class="card-footer">
              <span class="event-id-tag">{{ event.event_id }}</span>
              <span v-if="event.domain" class="domain-tag">Domain: {{ event.domain }}</span>
            </div>
          </div>
        </div>
      </section>

      <!-- Right Column: Narrative Sections & Grounded Claims -->
      <section class="narrative-pane" aria-labelledby="narrative-pane-heading">
        <div class="pane-header">
          <h3 id="narrative-pane-heading">Regulatory Narrative Course</h3>
          <span class="model-info">Model: {{ narrative.model_identifier }}</span>
        </div>

        <div class="narrative-sections-area">
          <article
            v-for="section in narrative.sections"
            :key="section.section_type"
            class="section-card"
          >
            <header class="section-header">
              <h4 class="section-title">{{ section.section_title }}</h4>
              <span class="section-type-code">{{ section.section_type }}</span>
            </header>

            <div class="section-body">
              <p class="section-content">{{ section.content }}</p>
            </div>

            <!-- Grounded Claims Sub-block -->
            <footer v-if="section.grounded_claims?.length" class="grounded-claims-block">
              <h5 class="claims-heading">Grounded eCRF Claim References</h5>
              <ul class="claims-list">
                <li
                  v-for="claim in section.grounded_claims"
                  :key="claim.claim_id"
                  :class="['claim-item', { 'is-active': activeClaimId === claim.claim_id }]"
                  @mouseenter="highlightEvents(claim.grounded_event_ids, claim.claim_id)"
                  @mouseleave="clearHighlight"
                  @click="highlightEvents(claim.grounded_event_ids, claim.claim_id)"
                >
                  <span class="claim-bullet">🔗</span>
                  <span class="claim-text">"{{ claim.sentence_text }}"</span>
                  <span class="grounded-tags">
                    <button
                      v-for="evtId in claim.grounded_event_ids"
                      :key="evtId"
                      type="button"
                      class="grounded-evt-btn"
                      @click.stop="scrollToEvent(evtId)"
                    >
                      {{ evtId }}
                    </button>
                  </span>
                </li>
              </ul>
            </footer>
          </article>
        </div>
      </section>
    </main>

    <!-- Part 11 Electronic Signature Modal -->
    <div v-if="showSignatureModal" class="modal-backdrop" role="dialog" aria-modal="true">
      <div class="modal-dialog">
        <header class="modal-header">
          <h3>21 CFR Part 11 Electronic Signature</h3>
          <button type="button" class="btn-close" @click="closeSignatureModal">✕</button>
        </header>
        <div class="modal-body">
          <p class="cert-notice">
            By applying your electronic signature, you legally certify as the designated Safety Physician or Medical Monitor
            that this serious adverse event narrative accurately reflects the clinical course documented in the trial casebook.
          </p>
          <div class="form-group">
            <label for="sig-reason">Reason for Signing / Justification *</label>
            <input
              id="sig-reason"
              v-model="signForm.reason"
              type="text"
              class="form-control"
              placeholder="e.g. Safety Physician Medical Review & Approval"
              required
            />
          </div>
          <div class="form-group">
            <label for="sig-secret">Security Credential / Passcode</label>
            <input
              id="sig-secret"
              v-model="signForm.secret"
              type="password"
              class="form-control"
              placeholder="Enter signer passcode"
            />
          </div>
          <p v-if="signError" class="error-msg">{{ signError }}</p>
        </div>
        <footer class="modal-footer">
          <button type="button" class="btn btn-text" @click="closeSignatureModal">Cancel</button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="!signForm.reason.trim() || isSubmitting"
            @click="submitSignature"
          >
            {{ isSubmitting ? 'Signing...' : 'Sign Document' }}
          </button>
        </footer>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive } from 'vue';

const props = defineProps({
  narrative: {
    type: Object,
    required: true,
    default: () => ({
      id: '',
      study_id: '',
      subject_id: '',
      sae_event_key: '',
      title: '',
      review_status: 'DRAFT_AI',
      confidence_score: 0.95,
      model_identifier: 'cadence-frontier-reasoner-v1',
      sections: [],
      timeline_events: [],
    }),
  },
});

const emit = defineEmits(['sign', 'export-e2b']);

const highlightedEventIds = ref([]);
const activeClaimId = ref(null);
const showSignatureModal = ref(false);
const isSubmitting = ref(false);
const isExporting = ref(false);
const signError = ref('');

const signForm = reactive({
  reason: 'Safety Physician Medical Review & Regulatory Approval',
  secret: '',
});

function highlightEvents(eventIds, claimId) {
  highlightedEventIds.value = eventIds || [];
  activeClaimId.value = claimId;
}

function clearHighlight() {
  highlightedEventIds.value = [];
  activeClaimId.value = null;
}

function scrollToEvent(eventId) {
  const el = document.getElementById(`timeline-${eventId}`);
  if (el) {
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    highlightedEventIds.value = [eventId];
  }
}

function openSignatureModal() {
  signError.value = '';
  showSignatureModal.value = true;
}

function closeSignatureModal() {
  showSignatureModal.value = false;
}

async function submitSignature() {
  if (!signForm.reason.trim()) {
    signError.value = 'Reason for change is mandatory.';
    return;
  }
  isSubmitting.value = true;
  signError.value = '';
  try {
    emit('sign', {
      narrativeId: props.narrative.id,
      reason: signForm.reason,
      secret: signForm.secret,
    });
    showSignatureModal.value = false;
  } catch (err) {
    signError.value = err.message || 'Signature failed.';
  } finally {
    isSubmitting.value = false;
  }
}

function exportE2BXml() {
  emit('export-e2b', props.narrative.id);
}
</script>

<style scoped>
.safety-narrative-reviewer {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--surface, #ffffff);
  color: var(--text-primary, #1e293b);
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
}

.narrative-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.25rem 1.5rem;
  border-bottom: 1px solid var(--border, #e2e8f0);
  background: var(--surface-subtle, #f8fafc);
}

.title-group {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.badge-regulatory {
  background: var(--primary-light, #e0f2fe);
  color: var(--primary, #0284c7);
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
}

.narrative-title {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 0;
}

.header-metadata {
  display: flex;
  flex-wrap: wrap;
  gap: 1rem;
  margin-top: 0.5rem;
  font-size: 0.875rem;
  color: var(--text-secondary, #64748b);
}

.status-pill {
  padding: 0.15rem 0.5rem;
  border-radius: 9999px;
  font-weight: 600;
  text-transform: uppercase;
  font-size: 0.75rem;
}

.status-pill.draft_ai {
  background: #fef3c7;
  color: #b45309;
}

.status-pill.approved {
  background: #dcfce7;
  color: #15803d;
}

.confidence-tag {
  background: #f1f5f9;
  color: #475569;
  font-size: 0.75rem;
  padding: 0.15rem 0.5rem;
  border-radius: 4px;
  font-weight: 600;
}

.header-actions {
  display: flex;
  gap: 0.75rem;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.5rem 1rem;
  font-size: 0.875rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn-primary {
  background: #0284c7;
  color: #ffffff;
}

.btn-primary:hover {
  background: #0369a1;
}

.btn-secondary {
  background: #f1f5f9;
  color: #1e293b;
  border-color: #cbd5e1;
}

.btn-secondary:hover {
  background: #e2e8f0;
}

.btn-text {
  background: transparent;
  color: #64748b;
}

/* Review Grid */
.review-grid {
  display: grid;
  grid-template-columns: 420px 1fr;
  flex: 1;
  overflow: hidden;
}

.timeline-pane {
  border-right: 1px solid var(--border, #e2e8f0);
  background: #fafafa;
  display: flex;
  flex-direction: column;
}

.pane-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border, #e2e8f0);
  background: #ffffff;
}

.pane-header h3 {
  font-size: 1rem;
  font-weight: 700;
  margin: 0;
}

.event-count, .model-info {
  font-size: 0.75rem;
  color: #64748b;
}

.timeline-scroll-area {
  padding: 1rem;
  overflow-y: auto;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.timeline-card {
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 0.875rem;
  transition: all 0.2s ease;
  position: relative;
}

.timeline-card.is-highlighted {
  border-color: #0284c7;
  background: #f0f9ff;
  box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.2);
  transform: translateX(4px);
}

.card-top {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  margin-bottom: 0.25rem;
}

.event-type-badge {
  font-weight: 700;
  color: #475569;
  text-transform: uppercase;
}

.event-date {
  color: #64748b;
}

.event-title {
  font-size: 0.875rem;
  font-weight: 600;
  margin: 0 0 0.25rem 0;
}

.event-desc {
  font-size: 0.8125rem;
  color: #334155;
  margin: 0 0 0.5rem 0;
  line-height: 1.4;
}

.card-footer {
  display: flex;
  gap: 0.5rem;
  font-size: 0.75rem;
}

.event-id-tag {
  background: #f1f5f9;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  font-family: monospace;
}

.domain-tag {
  color: #64748b;
}

/* Narrative Pane */
.narrative-pane {
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: #ffffff;
}

.narrative-sections-area {
  padding: 1.5rem;
  overflow-y: auto;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.section-card {
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 1.25rem;
  background: #ffffff;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.75rem;
}

.section-title {
  font-size: 1rem;
  font-weight: 700;
  margin: 0;
  color: #0f172a;
}

.section-type-code {
  font-size: 0.75rem;
  color: #94a3b8;
  font-family: monospace;
}

.section-content {
  font-size: 0.9375rem;
  line-height: 1.6;
  color: #334155;
  margin: 0;
}

.grounded-claims-block {
  margin-top: 1rem;
  padding-top: 0.75rem;
  border-top: 1px dashed #e2e8f0;
}

.claims-heading {
  font-size: 0.75rem;
  text-transform: uppercase;
  color: #64748b;
  font-weight: 700;
  margin: 0 0 0.5rem 0;
}

.claims-list {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.claim-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.35rem 0.5rem;
  border-radius: 4px;
  font-size: 0.8125rem;
  cursor: pointer;
  transition: background 0.15s ease;
}

.claim-item:hover, .claim-item.is-active {
  background: #f0f9ff;
}

.claim-bullet {
  font-size: 0.75rem;
}

.claim-text {
  flex: 1;
  color: #1e293b;
  font-style: italic;
}

.grounded-evt-btn {
  background: #e0f2fe;
  color: #0369a1;
  border: none;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-family: monospace;
  cursor: pointer;
  font-weight: 600;
  margin-left: 0.25rem;
}

.grounded-evt-btn:hover {
  background: #bae6fd;
}

/* Modal */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(15, 23, 42, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-dialog {
  background: #ffffff;
  border-radius: 8px;
  width: 100%;
  max-width: 520px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
  overflow: hidden;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1.25rem;
  border-bottom: 1px solid #e2e8f0;
}

.modal-header h3 {
  margin: 0;
  font-size: 1.125rem;
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.25rem;
  cursor: pointer;
  color: #94a3b8;
}

.modal-body {
  padding: 1.25rem;
}

.cert-notice {
  font-size: 0.8125rem;
  color: #475569;
  background: #f8fafc;
  border-left: 3px solid #0284c7;
  padding: 0.75rem;
  margin-bottom: 1rem;
  line-height: 1.4;
}

.form-group {
  margin-bottom: 1rem;
}

.form-group label {
  display: block;
  font-size: 0.875rem;
  font-weight: 600;
  margin-bottom: 0.35rem;
}

.form-control {
  width: 100%;
  padding: 0.5rem 0.75rem;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.875rem;
  box-sizing: border-box;
}

.error-msg {
  color: #dc2626;
  font-size: 0.8125rem;
  margin-top: 0.5rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid #e2e8f0;
  background: #f8fafc;
}
</style>
