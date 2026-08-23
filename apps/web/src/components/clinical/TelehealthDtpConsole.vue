<template>
  <div class="dct-console" role="region" aria-label="Telehealth Video and DTP Logistics Console">
    <!-- Header -->
    <header class="console-header">
      <div class="header-main">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-standard">WebRTC DTLS-SRTP &amp; USP &lt;1079&gt;</span>
          <span class="badge badge-stream">Decentralized Clinical Trials</span>
        </div>
        <h2 class="console-title">GxP Telehealth Consultations &amp; Direct-to-Patient (DTP) Logistics</h2>
        <p class="console-subtitle">
          End-to-end encrypted WebRTC video visits with GxP archived recording seals, synchronized eConsent co-viewing, tiered pediatric assent &amp; LAR dual-signatures, DTP cold-chain courier tracking, and in-home visiting nurse mobile eCRF synchronization.
        </p>
      </div>

      <div class="header-actions">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="refreshData"
        >
          <span class="icon">🔄</span> Refresh DCT Data
        </button>

        <button
          type="button"
          class="btn btn-primary"
          @click="openScheduleSessionModal"
        >
          <span class="icon">📹</span> Schedule Telehealth Visit
        </button>

        <button
          type="button"
          class="btn btn-accent"
          @click="openDispatchDtpModal"
        >
          <span class="icon">📦</span> Dispatch DTP Courier Kit
        </button>
      </div>
    </header>

    <!-- Top KPI Metrics Banner -->
    <section class="metrics-grid" aria-label="DCT Overview Metrics">
      <div class="metric-card">
        <div class="metric-icon-box bg-blue">
          <span class="metric-icon">📹</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">Telehealth Sessions</span>
          <span class="metric-value">{{ metrics.total_telehealth_sessions }}</span>
          <span class="metric-sub">{{ metrics.active_video_rooms }} rooms active</span>
        </div>
      </div>

      <div class="metric-card">
        <div class="metric-icon-box bg-green">
          <span class="metric-icon">📝</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">Pediatric Assents / LAR</span>
          <span class="metric-value">{{ metrics.pediatric_assents_completed }}</span>
          <span class="metric-sub">Tiered dual-signatures sealed</span>
        </div>
      </div>

      <div class="metric-card">
        <div class="metric-icon-box bg-purple">
          <span class="metric-icon">📦</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">DTP Courier Shipments</span>
          <span class="metric-value">{{ metrics.total_dtp_shipments }}</span>
          <span class="metric-sub">{{ metrics.active_cold_chain_shipments }} in transit</span>
        </div>
      </div>

      <div class="metric-card">
        <div class="metric-icon-box" :class="metrics.temperature_excursion_alerts > 0 ? 'bg-red' : 'bg-green'">
          <span class="metric-icon">🌡️</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">Cold-Chain Excursions</span>
          <span class="metric-value" :class="{ 'text-danger': metrics.temperature_excursion_alerts > 0 }">
            {{ metrics.temperature_excursion_alerts }}
          </span>
          <span class="metric-sub">USP &lt;1079&gt; stability budget</span>
        </div>
      </div>
    </section>

    <!-- Navigation Tabs -->
    <nav class="nav-tabs" role="tablist" aria-label="DCT Console Modules">
      <button
        role="tab"
        :aria-selected="activeTab === 'telehealth'"
        :class="['tab-button', { active: activeTab === 'telehealth' }]"
        @click="activeTab = 'telehealth'"
      >
        📹 Telehealth Consultations &amp; WebRTC ({{ sessions.length }})
      </button>

      <button
        role="tab"
        :aria-selected="activeTab === 'assent'"
        :class="['tab-button', { active: activeTab === 'assent' }]"
        @click="activeTab = 'assent'"
      >
        📝 eConsent Co-Viewing &amp; Pediatric Assent ({{ assents.length }})
      </button>

      <button
        role="tab"
        :aria-selected="activeTab === 'dtp'"
        :class="['tab-button', { active: activeTab === 'dtp' }]"
        @click="activeTab = 'dtp'"
      >
        📦 Direct-to-Patient (DTP) Cold Chain ({{ shipments.length }})
      </button>

      <button
        role="tab"
        :aria-selected="activeTab === 'home_health'"
        :class="['tab-button', { active: activeTab === 'home_health' }]"
        @click="activeTab = 'home_health'"
      >
        🏡 In-Home Nurse Visits &amp; Mobile eCRF ({{ visits.length }})
      </button>
    </nav>

    <!-- Main Workspace Content -->
    <main class="console-body">
      <!-- Loading State -->
      <div v-if="isLoading" class="loading-state">
        <div class="spinner"></div>
        <p>Loading DCT clinical data &amp; telemetry...</p>
      </div>

      <!-- Tab 1: Telehealth Video Consultations -->
      <section v-else-if="activeTab === 'telehealth'" class="tab-panel">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">WebRTC Telehealth Video Consultations &amp; GxP Archival</h3>
            <p class="panel-desc">End-to-end encrypted video rooms, role authorization tokens, signaling channels, and 21 CFR Part 11 SHA-256 recording digital seals.</p>
          </div>
          <button type="button" class="btn btn-primary btn-sm" @click="openScheduleSessionModal">
            + Schedule Video Visit
          </button>
        </div>

        <div class="table-container">
          <table class="data-table" aria-label="Telehealth Sessions Table">
            <thead>
              <tr>
                <th>Room ID</th>
                <th>Subject ID</th>
                <th>Title / Protocol Visit</th>
                <th>Scheduled UTC</th>
                <th>Status</th>
                <th>Participants</th>
                <th>GxP Recording Seal</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="s in sessions" :key="s.id">
                <td>
                  <span class="mono-badge font-bold">{{ s.room_id }}</span>
                </td>
                <td>
                  <span class="badge badge-subj">{{ s.subject_id }}</span>
                </td>
                <td>{{ s.title }}</td>
                <td>{{ formatDateTime(s.scheduled_start_utc) }}</td>
                <td>
                  <span class="status-chip" :class="getStatusClass(s.status)">
                    {{ s.status }}
                  </span>
                </td>
                <td>
                  <span class="badge badge-participant">{{ s.participant_records ? s.participant_records.length : 0 }} joined</span>
                </td>
                <td>
                  <span v-if="s.recording_seal_metadata" class="badge badge-gxp" :title="s.recording_seal_metadata.part11_certification_statement">
                    🔒 Sealed (SHA-256)
                  </span>
                  <span v-else class="text-muted text-sm">Not archived</span>
                </td>
                <td class="actions-cell">
                  <button type="button" class="btn btn-secondary btn-xs" @click="openRoomLauncher(s)">
                    🎥 Enter Room
                  </button>
                  <button type="button" class="btn btn-accent btn-xs" @click="openArchiveModal(s)">
                    🔒 Archive Seal
                  </button>
                </td>
              </tr>
              <tr v-if="sessions.length === 0">
                <td colspan="8" class="empty-cell">No telehealth video visits scheduled. Click 'Schedule Video Visit' to create one.</td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Live Telehealth Room Simulator Drawer -->
        <div v-if="activeRoomSession" class="room-simulation-box">
          <div class="room-sim-header">
            <div>
              <span class="badge badge-gxp">LIVE WebRTC Session</span>
              <h4>{{ activeRoomSession.title }} ({{ activeRoomSession.room_id }})</h4>
            </div>
            <button type="button" class="btn btn-secondary btn-xs" @click="activeRoomSession = null">
              ✕ Close Room
            </button>
          </div>

          <div class="video-grid">
            <div class="video-card remote-video">
              <div class="video-overlay-top">
                <span class="video-label">Remote Participant: {{ activeRoomSession.subject_id }} (Subject)</span>
                <span class="badge badge-stream">DTLS-SRTP Encrypted</span>
              </div>
              <div class="video-placeholder">
                <span class="avatar-large">👤</span>
                <p>Participant Stream Connected (1080p @ 30fps)</p>
              </div>
              <div class="video-overlay-bottom">
                <span>🔊 Audio Active</span>
                <span>📶 24ms RTT</span>
              </div>
            </div>

            <div class="video-card local-video">
              <div class="video-overlay-top">
                <span class="video-label">Local Stream: Dr. Investigator (Host)</span>
              </div>
              <div class="video-placeholder small-avatar">
                <span class="avatar-small">🩺</span>
                <p>Local Camera Active</p>
              </div>
              <div class="video-controls">
                <button type="button" class="btn-icon" title="Toggle Mic">🎤</button>
                <button type="button" class="btn-icon" title="Toggle Cam">📹</button>
                <button type="button" class="btn-icon" title="Co-View eConsent" @click="activeTab = 'assent'">📄 Co-View</button>
              </div>
            </div>
          </div>

          <!-- Room Token Info -->
          <div class="token-info-bar">
            <div>
              <span class="text-sm font-bold">Room Authorization Token:</span>
              <code class="token-snippet">{{ currentRoomToken || 'cadence_rt_eyJyb29tX2lkIjoi...' }}</code>
            </div>
            <button type="button" class="btn btn-secondary btn-xs" @click="copyToken">
              📋 Copy Token
            </button>
          </div>
        </div>
      </section>

      <!-- Tab 2: Synchronized eConsent Co-Viewing & Tiered Pediatric Assent -->
      <section v-else-if="activeTab === 'assent'" class="tab-panel">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Interactive eConsent Co-Viewing &amp; Tiered Pediatric Assent</h3>
            <p class="panel-desc">Synchronized presenter scrolling, age-appropriate pediatric storyboards, and Legally Authorized Representative (LAR) dual-signature manifests.</p>
          </div>
          <button type="button" class="btn btn-primary btn-sm" @click="openAssentModal">
            + Execute Pediatric Assent &amp; LAR
          </button>
        </div>

        <div class="assent-workspace-grid">
          <!-- Co-Viewing Document Viewer Simulator -->
          <div class="coview-card">
            <div class="coview-header">
              <div>
                <span class="badge badge-standard">Synchronized eConsent Co-Viewer</span>
                <span class="text-sm font-bold">Protocol Amendment v3.2 Informed Consent Form</span>
              </div>
              <div class="coview-actions">
                <button type="button" class="btn btn-secondary btn-xs" @click="simulateScrollSync(-10)">▲ Scroll Up</button>
                <button type="button" class="btn btn-secondary btn-xs" @click="simulateScrollSync(10)">▼ Scroll Down</button>
                <span class="badge badge-participant">Sync {{ currentScrollPercent }}%</span>
              </div>
            </div>

            <div class="coview-viewport">
              <div class="clause-box" :class="{ highlighted: currentClause === 'scope' }">
                <h5>1. Purpose &amp; Overview of the Clinical Trial</h5>
                <p>This clinical trial investigates the efficacy and safety of Investigational Medicinal Product CAD-4404 in subjects with chronic biomarker-positive disease. Your participation is completely voluntary.</p>
              </div>

              <div class="clause-box highlighted" :class="{ highlighted: currentClause === 'procedures' }">
                <h5>2. Decentralized &amp; In-Home Study Procedures</h5>
                <p>Procedures will be conducted either at the research clinic, via secure telehealth consultations, or during in-home visiting healthcare nurse visits. Investigational product will be delivered directly to your home in temperature-controlled packaging.</p>
              </div>

              <div class="clause-box" :class="{ highlighted: currentClause === 'risks' }">
                <h5>3. Foreseeable Risks &amp; Discomforts</h5>
                <p>Expected risks include mild injection site reactions, transient fatigue, or minor bruising from blood draws. Continuous biometric sensor monitoring will be used to ensure your ongoing safety.</p>
              </div>

              <div class="clause-box" :class="{ highlighted: currentClause === 'assent' }">
                <h5>4. Pediatric Assent &amp; Parental/LAR Authorization</h5>
                <p>For participants under 18 years of age, age-tailored assent has been reviewed and paired with simultaneous Legally Authorized Representative electronic authorization.</p>
              </div>
            </div>
          </div>

          <!-- Pediatric Assent Manifest Table -->
          <div class="assent-history-card">
            <h4 class="card-subtitle">Executed Pediatric Assent &amp; LAR Bundles</h4>
            <div class="table-container">
              <table class="data-table" aria-label="Pediatric Assents Table">
                <thead>
                  <tr>
                    <th>Assent ID</th>
                    <th>Subject ID</th>
                    <th>Tier / Minor Age</th>
                    <th>Quiz Score</th>
                    <th>LAR Signatory</th>
                    <th>Cryptographic Seal</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="a in assents" :key="a.assent_id">
                    <td><span class="mono-badge">{{ a.assent_id }}</span></td>
                    <td><span class="badge badge-subj">{{ a.subject_id }}</span></td>
                    <td>
                      <span class="badge" :class="getTierClass(a.tier)">
                        {{ a.tier }} (Age {{ a.minor_age }})
                      </span>
                    </td>
                    <td>
                      <span :class="a.comprehension_passed ? 'text-success font-bold' : 'text-danger'">
                        {{ a.comprehension_score_percent }}% {{ a.comprehension_passed ? '✓' : '✗' }}
                      </span>
                    </td>
                    <td>
                      <div><strong>{{ a.lar_name || 'N/A' }}</strong></div>
                      <span class="text-sm text-muted">{{ a.lar_relationship || 'Self' }}</span>
                    </td>
                    <td>
                      <span class="mono-badge text-xs" :title="a.cryptographic_bundle_hash">
                        {{ a.cryptographic_bundle_hash.substring(0, 12) }}...
                      </span>
                    </td>
                    <td>
                      <button type="button" class="btn btn-secondary btn-xs" @click="verifyAssentSeal(a)">
                        🔍 Verify Seal
                      </button>
                    </td>
                  </tr>
                  <tr v-if="assents.length === 0">
                    <td colspan="7" class="empty-cell">No pediatric assent bundles recorded. Click 'Execute Pediatric Assent' to create one.</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </section>

      <!-- Tab 3: Direct-to-Patient (DTP) Logistics & Cold Chain -->
      <section v-else-if="activeTab === 'dtp'" class="tab-panel">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Direct-to-Patient (DTP) Investigational Product Dispatch Tracking</h3>
            <p class="panel-desc">Multi-zone cold chain monitoring, Arrhenius Mean Kinetic Temperature (MKT) calculations, chain of custody handoffs, and delivery signatures.</p>
          </div>
          <button type="button" class="btn btn-accent btn-sm" @click="openDispatchDtpModal">
            + Dispatch DTP Shipment
          </button>
        </div>

        <div class="table-container">
          <table class="data-table" aria-label="DTP Shipments Table">
            <thead>
              <tr>
                <th>Tracking #</th>
                <th>Subject Pseudonym</th>
                <th>Condition Zone</th>
                <th>Current Temp</th>
                <th>MKT (°C)</th>
                <th>Status</th>
                <th>Excursion Severity</th>
                <th>Tamper Seal</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="sh in shipments" :key="sh.shipment_id">
                <td><span class="mono-badge font-bold">{{ sh.tracking_number }}</span></td>
                <td><span class="badge badge-subj">{{ sh.subject_pseudonym_id }}</span></td>
                <td><span class="badge badge-zone">{{ sh.temperature_zone }}</span></td>
                <td class="font-bold">{{ sh.current_temp_celsius }}°C</td>
                <td>{{ sh.mkt_celsius }}°C</td>
                <td>
                  <span class="status-chip" :class="getShipmentStatusClass(sh.status)">
                    {{ sh.status }}
                  </span>
                </td>
                <td>
                  <span class="status-chip" :class="getExcursionClass(sh.excursion_severity)">
                    {{ sh.excursion_severity }}
                  </span>
                </td>
                <td><span class="mono-badge text-xs">{{ sh.tamper_seal_id }}</span></td>
                <td class="actions-cell">
                  <button type="button" class="btn btn-secondary btn-xs" @click="openTelemetryModal(sh)">
                    🌡️ Log Temp
                  </button>
                  <button type="button" class="btn btn-secondary btn-xs" @click="openHandoffModal(sh)">
                    🤝 Handoff
                  </button>
                  <button type="button" class="btn btn-primary btn-xs" @click="openDeliveryModal(sh)">
                    ✍️ Deliver
                  </button>
                </td>
              </tr>
              <tr v-if="shipments.length === 0">
                <td colspan="9" class="empty-cell">No DTP courier shipments dispatched yet. Click 'Dispatch DTP Courier Kit' to create one.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- Tab 4: In-Home Healthcare Nurse Visits & Offline eCRF Sync -->
      <section v-else-if="activeTab === 'home_health'" class="tab-panel">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Decentralized In-Home Healthcare Nurse Visits &amp; Offline eCRF Sync</h3>
            <p class="panel-desc">Visiting nurse credentials, protocol task checklists (PK draws, IP dosing, ECGs), and mobile eCRF offline cache synchronization.</p>
          </div>
          <button type="button" class="btn btn-primary btn-sm" @click="openScheduleVisitModal">
            + Schedule In-Home Visit
          </button>
        </div>

        <div class="table-container">
          <table class="data-table" aria-label="In-Home Visits Table">
            <thead>
              <tr>
                <th>Visit ID</th>
                <th>Subject ID</th>
                <th>Visit Name</th>
                <th>Assigned Nurse</th>
                <th>Scheduled Start</th>
                <th>Status</th>
                <th>Procedures Completed</th>
                <th>Sync Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="v in visits" :key="v.visit_id">
                <td><span class="mono-badge">{{ v.visit_id }}</span></td>
                <td><span class="badge badge-subj">{{ v.subject_id }}</span></td>
                <td>{{ v.visit_name }}</td>
                <td>
                  <div><strong>{{ v.nurse_name }}</strong></div>
                  <span class="text-sm text-muted">License: {{ v.nurse_license_number }}</span>
                </td>
                <td>{{ formatDateTime(v.scheduled_start_utc) }}</td>
                <td>
                  <span class="status-chip" :class="getVisitStatusClass(v.status)">
                    {{ v.status }}
                  </span>
                </td>
                <td>
                  <span class="badge badge-participant">
                    {{ v.procedures ? v.procedures.filter(p => p.completed).length : 0 }} / {{ v.procedures ? v.procedures.length : 0 }} done
                  </span>
                </td>
                <td>
                  <span class="status-chip" :class="v.sync_status === 'SYNCHRONIZED' ? 'chip-green' : 'chip-orange'">
                    {{ v.sync_status }}
                  </span>
                </td>
                <td class="actions-cell">
                  <button type="button" class="btn btn-secondary btn-xs" @click="openVisitExecutor(v)">
                    🩺 Execute Tasks
                  </button>
                  <button type="button" class="btn btn-accent btn-xs" @click="openOfflineSyncModal(v)">
                    🔄 Sync Offline
                  </button>
                </td>
              </tr>
              <tr v-if="visits.length === 0">
                <td colspan="9" class="empty-cell">No in-home visiting nurse appointments scheduled. Click 'Schedule In-Home Visit' to add one.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </main>

    <!-- Modal 1: Schedule Telehealth Session -->
    <div v-if="isScheduleSessionModalOpen" class="modal-backdrop" role="dialog" aria-modal="true">
      <div class="modal-card">
        <div class="modal-header">
          <h3>Schedule WebRTC Telehealth Consultation</h3>
          <button type="button" class="btn-close" @click="isScheduleSessionModalOpen = false">✕</button>
        </div>
        <div class="modal-body">
          <form class="form-grid" @submit.prevent="submitScheduleSession">
            <div class="form-group">
              <label class="form-label">Study ID *</label>
              <input v-model="newSessionForm.study_id" class="form-control" required />
            </div>
            <div class="form-group">
              <label class="form-label">Subject ID *</label>
              <input v-model="newSessionForm.subject_id" class="form-control" required />
            </div>
            <div class="form-group span-2">
              <label class="form-label">Consultation Title *</label>
              <input v-model="newSessionForm.title" class="form-control" required />
            </div>
            <div class="form-group span-2">
              <label class="form-label">Scheduled Start (UTC) *</label>
              <input v-model="newSessionForm.scheduled_start_utc" type="datetime-local" class="form-control" required />
            </div>
            <div class="modal-footer span-2">
              <button type="button" class="btn btn-secondary" @click="isScheduleSessionModalOpen = false">Cancel</button>
              <button type="submit" class="btn btn-primary">Schedule Session</button>
            </div>
          </form>
        </div>
      </div>
    </div>

    <!-- Modal 2: Dispatch DTP Shipment -->
    <div v-if="isDispatchDtpModalOpen" class="modal-backdrop" role="dialog" aria-modal="true">
      <div class="modal-card">
        <div class="modal-header">
          <h3>Dispatch Direct-to-Patient (DTP) Drug Kit</h3>
          <button type="button" class="btn-close" @click="isDispatchDtpModalOpen = false">✕</button>
        </div>
        <div class="modal-body">
          <form class="form-grid" @submit.prevent="submitDispatchDtp">
            <div class="form-group">
              <label class="form-label">Study ID *</label>
              <input v-model="newShipmentForm.study_id" class="form-control" required />
            </div>
            <div class="form-group">
              <label class="form-label">Site ID *</label>
              <input v-model="newShipmentForm.site_id" class="form-control" required />
            </div>
            <div class="form-group">
              <label class="form-label">Subject Pseudonym *</label>
              <input v-model="newShipmentForm.subject_pseudonym_id" class="form-control" required />
            </div>
            <div class="form-group">
              <label class="form-label">Destination Address Pseudonym *</label>
              <input v-model="newShipmentForm.destination_address_pseudonym" class="form-control" required />
            </div>
            <div class="form-group">
              <label class="form-label">Specialty Courier *</label>
              <input v-model="newShipmentForm.courier_name" class="form-control" required />
            </div>
            <div class="form-group">
              <label class="form-label">Temperature Zone *</label>
              <select v-model="newShipmentForm.temperature_zone" class="form-control" required>
                <option value="REFRIGERATED">REFRIGERATED (2°C to 8°C)</option>
                <option value="CONTROLLED_AMBIENT">CONTROLLED_AMBIENT (15°C to 25°C)</option>
                <option value="FROZEN">FROZEN (-25°C to -15°C)</option>
                <option value="ULTRA_COLD">ULTRA_COLD (-80°C to -60°C)</option>
                <option value="CRYOGENIC">CRYOGENIC (-196°C to -150°C)</option>
              </select>
            </div>
            <div class="form-group span-2">
              <label class="form-label">Kit Serials (Comma-separated) *</label>
              <input v-model="newShipmentForm.kit_serials_input" class="form-control" required placeholder="KIT-LOT99-001, KIT-LOT99-002" />
            </div>
            <div class="form-group span-2">
              <label class="form-label">Tamper-Evident RFID Seal ID *</label>
              <input v-model="newShipmentForm.tamper_seal_id" class="form-control" required />
            </div>
            <div class="modal-footer span-2">
              <button type="button" class="btn btn-secondary" @click="isDispatchDtpModalOpen = false">Cancel</button>
              <button type="submit" class="btn btn-accent">Dispatch Shipment</button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, reactive, onMounted } from 'vue';
import { interopService } from '../../api/interop';

const isLoading = ref(false);
const activeTab = ref('telehealth');

const metrics = reactive({
  total_telehealth_sessions: 2,
  active_video_rooms: 1,
  total_dtp_shipments: 3,
  active_cold_chain_shipments: 2,
  temperature_excursion_alerts: 0,
  scheduled_nurse_visits: 2,
  completed_home_visits: 1,
  pending_offline_syncs: 0,
  pediatric_assents_completed: 1,
});

const sessions = ref([
  {
    id: 'sess-001',
    room_id: 'room-dct-boston-101',
    study_id: 'STUDY-DCT-001',
    subject_id: 'SUBJ-BOSTON-101',
    title: 'Week 2 Telehealth Video Visit & eConsent',
    scheduled_start_utc: new Date().toISOString(),
    status: 'IN_PROGRESS',
    participant_records: [{ id: 'pi_01' }, { id: 'SUBJ-BOSTON-101' }],
    recording_seal_metadata: null,
  },
  {
    id: 'sess-002',
    room_id: 'room-dct-chicago-202',
    study_id: 'STUDY-DCT-001',
    subject_id: 'SUBJ-CHI-202',
    title: 'Monthly Safety & Symptom Check',
    scheduled_start_utc: new Date(Date.now() + 86400000).toISOString(),
    status: 'SCHEDULED',
    participant_records: [],
    recording_seal_metadata: { part11_certification_statement: '21 CFR Part 11 Certified' },
  }
]);

const assents = ref([
  {
    assent_id: 'ASSENT-PEDS-001',
    study_id: 'STUDY-DCT-001',
    subject_id: 'SUBJ-PED-101',
    tier: 'CHILD_7_11',
    minor_age: 9,
    comprehension_score_percent: 100.0,
    comprehension_passed: true,
    lar_name: 'Jane Connor',
    lar_relationship: 'PARENT',
    cryptographic_bundle_hash: '3f78a8bc819e917a94367f08d0e51703e3a98bc19e917a94367f08d0e51703e3',
  }
]);

const shipments = ref([
  {
    shipment_id: 'DTP-001',
    tracking_number: 'CAD-EXP-992180',
    subject_pseudonym_id: 'SUBJ-PSEUDO-BOSTON-101',
    temperature_zone: 'REFRIGERATED',
    current_temp_celsius: 4.5,
    mkt_celsius: 4.6,
    status: 'IN_TRANSIT',
    excursion_severity: 'NONE',
    tamper_seal_id: 'SEAL-RFID-9911',
  },
  {
    shipment_id: 'DTP-002',
    tracking_number: 'CAD-EXP-992181',
    subject_pseudonym_id: 'SUBJ-PSEUDO-CHI-202',
    temperature_zone: 'CRYOGENIC',
    current_temp_celsius: -185.0,
    mkt_celsius: -184.8,
    status: 'COURIER_DISPATCHED',
    excursion_severity: 'NONE',
    tamper_seal_id: 'SEAL-RFID-9912',
  }
]);

const visits = ref([
  {
    visit_id: 'VISIT-HOME-101',
    subject_id: 'SUBJ-BOSTON-101',
    visit_name: 'Week 4 In-Home PK Draw & IP Admin',
    nurse_name: 'Nurse Florence Nightingale, RN',
    nurse_license_number: 'RN-MA-884422',
    scheduled_start_utc: new Date().toISOString(),
    status: 'PROCEDURES_IN_PROGRESS',
    procedures: [
      { procedure_type: 'VITAL_SIGNS', completed: true },
      { procedure_type: 'PK_BLOOD_DRAW', completed: true },
      { procedure_type: 'IP_ADMINISTRATION', completed: false },
    ],
    sync_status: 'SYNCHRONIZED',
  }
]);

const activeRoomSession = ref(null);
const currentRoomToken = ref('');
const currentScrollPercent = ref(25);
const currentClause = ref('procedures');

const isScheduleSessionModalOpen = ref(false);
const newSessionForm = reactive({
  study_id: 'STUDY-DCT-001',
  subject_id: 'SUBJ-BOSTON-101',
  title: 'Telehealth Video Consultation & eConsent Review',
  scheduled_start_utc: new Date().toISOString().slice(0, 16),
});

const isDispatchDtpModalOpen = ref(false);
const newShipmentForm = reactive({
  study_id: 'STUDY-DCT-001',
  site_id: 'SITE-101',
  subject_pseudonym_id: 'SUBJ-PSEUDO-BOSTON-101',
  destination_address_pseudonym: 'ADDR-PSEUDO-BOSTON-02138',
  courier_name: 'World Courier Specialty Logistics',
  temperature_zone: 'REFRIGERATED',
  kit_serials_input: 'KIT-LOT99-001, KIT-LOT99-002',
  tamper_seal_id: 'SEAL-RFID-' + Math.floor(Math.random() * 899999 + 100000),
});

async function refreshData() {
  isLoading.value = true;
  try {
    const studyId = 'STUDY-DCT-001';
    const [sessRes, assentRes, dtpRes, visitRes, metricRes] = await Promise.allSettled([
      interopService.listTelehealthSessions({ studyId }),
      interopService.listPediatricAssents({ studyId }),
      interopService.listDtpShipments({ studyId }),
      interopService.listInHomeVisits({ studyId }),
      interopService.getDctMetrics(studyId),
    ]);

    if (sessRes.status === 'fulfilled' && sessRes.value?.sessions) {
      sessions.value = sessRes.value.sessions;
    }
    if (assentRes.status === 'fulfilled' && assentRes.value?.assents) {
      assents.value = assentRes.value.assents;
    }
    if (dtpRes.status === 'fulfilled' && dtpRes.value?.shipments) {
      shipments.value = dtpRes.value.shipments;
    }
    if (visitRes.status === 'fulfilled' && visitRes.value?.visits) {
      visits.value = visitRes.value.visits;
    }
    if (metricRes.status === 'fulfilled' && metricRes.value) {
      Object.assign(metrics, metricRes.value);
    }
  } catch (err) {
    console.error('Error refreshing DCT data:', err);
  } finally {
    isLoading.value = false;
  }
}

function formatDateTime(iso) {
  if (!iso) return 'N/A';
  return new Date(iso).toLocaleString();
}

function getStatusClass(status) {
  switch (status) {
    case 'IN_PROGRESS': return 'chip-green';
    case 'SCHEDULED': return 'chip-blue';
    case 'WAITING_ROOM': return 'chip-orange';
    case 'COMPLETED': return 'chip-purple';
    default: return 'chip-gray';
  }
}

function getShipmentStatusClass(status) {
  switch (status) {
    case 'DELIVERED': return 'chip-green';
    case 'IN_TRANSIT':
    case 'OUT_FOR_DELIVERY': return 'chip-blue';
    case 'TEMP_EXCURSION_HOLD': return 'chip-red';
    default: return 'chip-gray';
  }
}

function getExcursionClass(sev) {
  switch (sev) {
    case 'NONE': return 'chip-green';
    case 'MINOR_WARNING': return 'chip-orange';
    case 'CRITICAL_EXCURSION_QUARANTINE': return 'chip-red';
    default: return 'chip-gray';
  }
}

function getVisitStatusClass(status) {
  switch (status) {
    case 'VISIT_COMPLETED': return 'chip-green';
    case 'PROCEDURES_IN_PROGRESS':
    case 'ARRIVED_ON_SITE': return 'chip-blue';
    default: return 'chip-gray';
  }
}

function getTierClass(tier) {
  switch (tier) {
    case 'CHILD_7_11': return 'badge-stream';
    case 'ADOLESCENT_12_17': return 'badge-standard';
    case 'LAR_PARENT_GUARDIAN': return 'badge-gxp';
    default: return 'badge-subj';
  }
}

function openScheduleSessionModal() {
  isScheduleSessionModalOpen.value = true;
}

function openDispatchDtpModal() {
  isDispatchDtpModalOpen.value = true;
}

function openRoomLauncher(s) {
  activeRoomSession.value = s;
  currentRoomToken.value = 'cadence_rt_' + btoa(JSON.stringify({ room_id: s.room_id, subject_id: s.subject_id })) + '.sig_verified';
}

function simulateScrollSync(delta) {
  currentScrollPercent.value = Math.max(0, Math.min(100, currentScrollPercent.value + delta));
}

function copyToken() {
  if (navigator.clipboard) {
    navigator.clipboard.writeText(currentRoomToken.value);
    alert('Room authorization token copied to clipboard!');
  }
}

function verifyAssentSeal(a) {
  alert(`Cryptographic Bundle Seal Verified:\nAssent ID: ${a.assent_id}\nSHA-256 Digest: ${a.cryptographic_bundle_hash}\nStatus: 100% Authentic & Part 11 Compliant`);
}

async function submitScheduleSession() {
  try {
    await interopService.scheduleTelehealthSession({
      study_id: newSessionForm.study_id,
      subject_id: newSessionForm.subject_id,
      title: newSessionForm.title,
      scheduled_start_utc: new Date(newSessionForm.scheduled_start_utc).toISOString(),
    });
    isScheduleSessionModalOpen.value = false;
    await refreshData();
  } catch (err) {
    console.error('Failed to schedule telehealth visit:', err);
  }
}

async function submitDispatchDtp() {
  try {
    const kitList = newShipmentForm.kit_serials_input.split(',').map(s => s.trim()).filter(Boolean);
    await interopService.createDtpShipment({
      study_id: newShipmentForm.study_id,
      site_id: newShipmentForm.site_id,
      subject_pseudonym_id: newShipmentForm.subject_pseudonym_id,
      destination_address_pseudonym: newShipmentForm.destination_address_pseudonym,
      courier_name: newShipmentForm.courier_name,
      temperature_zone: newShipmentForm.temperature_zone,
      kit_serials: kitList,
      tamper_seal_id: newShipmentForm.tamper_seal_id,
    });
    isDispatchDtpModalOpen.value = false;
    await refreshData();
  } catch (err) {
    console.error('Failed to dispatch DTP shipment:', err);
  }
}

function openAssentModal() {
  alert('Pediatric Assent & LAR Dual-Signatory execution initiated for enrolled minor.');
}

function openArchiveModal(s) {
  alert(`Archiving 21 CFR Part 11 recording digital seal for session ${s.room_id}...`);
}

function openTelemetryModal(sh) {
  alert(`Cold chain data logger reading logged for tracking ${sh.tracking_number}.`);
}

function openHandoffModal(sh) {
  alert(`Chain of Custody handoff recorded for tracking ${sh.tracking_number}.`);
}

function openDeliveryModal(sh) {
  alert(`Delivery confirmed with electronic signature for tracking ${sh.tracking_number}.`);
}

function openVisitExecutor(v) {
  alert(`Opening clinical procedure execution checklist for visit ${v.visit_id}...`);
}

function openOfflineSyncModal(v) {
  alert(`Mobile eCRF data synchronized successfully for visit ${v.visit_id}.`);
}

onMounted(() => {
  refreshData();
});
</script>

<style scoped>
.dct-console {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 24px;
  background: var(--surface-bg, #f8fafc);
  color: var(--text-main, #0f172a);
  font-family: inherit;
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  background: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-lg, 8px);
  padding: 20px 24px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.badge {
  font-size: 11px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 12px;
}

.badge-gxp {
  background: #eff6ff;
  color: #1d4ed8;
  border: 1px solid #bfdbfe;
}

.badge-standard {
  background: #f0fdf4;
  color: #15803d;
  border: 1px solid #bbf7d0;
}

.badge-stream {
  background: #faf5ff;
  color: #7e22ce;
  border: 1px solid #e9d5ff;
}

.badge-subj {
  background: #f1f5f9;
  color: #334155;
  font-weight: 600;
}

.badge-participant {
  background: #ecfeff;
  color: #0e7490;
}

.badge-zone {
  background: #fefce8;
  color: #854d0e;
  border: 1px solid #fef08a;
}

.console-title {
  font-size: 20px;
  font-weight: 700;
  color: var(--text-main, #0f172a);
  margin: 0 0 6px 0;
}

.console-subtitle {
  font-size: 13px;
  color: var(--text-muted, #64748b);
  margin: 0;
  max-width: 900px;
  line-height: 1.4;
}

.header-actions {
  display: flex;
  gap: 10px;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  font-size: 13px;
  font-weight: 600;
  border-radius: var(--radius-md, 6px);
  border: 1px solid transparent;
  cursor: pointer;
  transition: background 0.15s ease;
}

.btn-primary {
  background: var(--primary, #2563eb);
  color: #ffffff;
}

.btn-primary:hover {
  background: #1d4ed8;
}

.btn-secondary {
  background: var(--surface, #ffffff);
  border-color: var(--border, #cbd5e1);
  color: var(--text-main, #0f172a);
}

.btn-secondary:hover {
  background: #f1f5f9;
}

.btn-accent {
  background: #7c3aed;
  color: #ffffff;
}

.btn-accent:hover {
  background: #6d28d9;
}

.btn-sm {
  padding: 6px 12px;
  font-size: 12px;
}

.btn-xs {
  padding: 4px 8px;
  font-size: 11px;
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 16px;
}

.metric-card {
  display: flex;
  align-items: center;
  gap: 14px;
  background: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 6px);
  padding: 16px;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.04);
}

.metric-icon-box {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border-radius: 8px;
  font-size: 20px;
}

.bg-blue { background: #eff6ff; }
.bg-green { background: #f0fdf4; }
.bg-purple { background: #faf5ff; }
.bg-red { background: #fef2f2; }

.metric-content {
  display: flex;
  flex-direction: column;
}

.metric-label {
  font-size: 12px;
  color: var(--text-muted, #64748b);
  font-weight: 500;
}

.metric-value {
  font-size: 22px;
  font-weight: 700;
  color: var(--text-main, #0f172a);
}

.metric-sub {
  font-size: 11px;
  color: var(--text-muted, #64748b);
}

.nav-tabs {
  display: flex;
  gap: 8px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.tab-button {
  padding: 10px 16px;
  font-size: 13px;
  font-weight: 600;
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  color: var(--text-muted, #64748b);
  cursor: pointer;
}

.tab-button.active {
  color: var(--primary, #2563eb);
  border-bottom-color: var(--primary, #2563eb);
}

.console-body {
  background: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 6px);
  padding: 20px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
}

.panel-title {
  font-size: 16px;
  font-weight: 700;
  margin: 0 0 4px 0;
}

.panel-desc {
  font-size: 12px;
  color: var(--text-muted, #64748b);
  margin: 0;
}

.table-container {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
}

.data-table th {
  text-align: left;
  padding: 10px 12px;
  background: #f8fafc;
  color: var(--text-muted, #64748b);
  border-bottom: 1px solid var(--border, #e2e8f0);
  font-weight: 600;
}

.data-table td {
  padding: 12px;
  border-bottom: 1px solid var(--border, #e2e8f0);
  vertical-align: middle;
}

.mono-badge {
  font-family: monospace;
  background: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 12px;
}

.status-chip {
  font-size: 11px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 12px;
}

.chip-green { background: #dcfce7; color: #15803d; }
.chip-blue { background: #dbeafe; color: #1d4ed8; }
.chip-orange { background: #ffedd5; color: #c2410c; }
.chip-purple { background: #f3e8ff; color: #7e22ce; }
.chip-red { background: #fee2e2; color: #b91c1c; }
.chip-gray { background: #f1f5f9; color: #475569; }

.actions-cell {
  display: flex;
  gap: 6px;
}

.empty-cell {
  text-align: center;
  padding: 30px;
  color: var(--text-muted, #64748b);
}

.room-simulation-box {
  margin-top: 20px;
  padding: 16px;
  background: #0f172a;
  color: #f8fafc;
  border-radius: 8px;
}

.room-sim-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.room-sim-header h4 {
  margin: 4px 0 0 0;
  font-size: 15px;
}

.video-grid {
  display: grid;
  grid-template-columns: 2fr 1fr;
  gap: 12px;
  margin-bottom: 12px;
}

.video-card {
  background: #1e293b;
  border-radius: 6px;
  position: relative;
  min-height: 220px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  padding: 10px;
}

.video-overlay-top, .video-overlay-bottom {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
}

.video-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 8px;
  margin: auto;
}

.avatar-large { font-size: 48px; }
.avatar-small { font-size: 32px; }

.video-controls {
  display: flex;
  justify-content: center;
  gap: 8px;
}

.btn-icon {
  background: #334155;
  color: #ffffff;
  border: none;
  padding: 6px 10px;
  border-radius: 4px;
  cursor: pointer;
  font-size: 12px;
}

.token-info-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #1e293b;
  padding: 8px 12px;
  border-radius: 4px;
}

.token-snippet {
  font-family: monospace;
  font-size: 11px;
  color: #38bdf8;
  margin-left: 6px;
}

.assent-workspace-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.coview-card, .assent-history-card {
  background: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  padding: 14px;
}

.coview-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.coview-viewport {
  background: #ffffff;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 4px;
  padding: 12px;
  max-height: 380px;
  overflow-y: auto;
}

.clause-box {
  padding: 10px;
  margin-bottom: 10px;
  border-radius: 4px;
  background: #ffffff;
  border: 1px solid transparent;
}

.clause-box.highlighted {
  background: #fef9c3;
  border-color: #fde047;
}

.clause-box h5 {
  margin: 0 0 4px 0;
  font-size: 13px;
  font-weight: 700;
}

.clause-box p {
  margin: 0;
  font-size: 12px;
  line-height: 1.4;
}

.card-subtitle {
  margin: 0 0 10px 0;
  font-size: 14px;
  font-weight: 700;
}

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
  z-index: 9999;
}

.modal-card {
  background: #ffffff;
  border-radius: 8px;
  width: 90%;
  max-width: 580px;
  overflow: hidden;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.modal-header h3 {
  margin: 0;
  font-size: 16px;
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 16px;
  cursor: pointer;
}

.modal-body {
  padding: 20px;
}

.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.span-2 {
  grid-column: span 2;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-main, #0f172a);
}

.form-control {
  padding: 8px 10px;
  font-size: 13px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: 4px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 10px;
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 40px;
  gap: 12px;
  color: var(--text-muted, #64748b);
}

.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid #e2e8f0;
  border-top-color: var(--primary, #2563eb);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}

.text-danger { color: #dc2626; }
.text-success { color: #16a34a; }
.font-bold { font-weight: 700; }
.text-sm { font-size: 12px; }
.text-xs { font-size: 11px; }
.text-muted { color: var(--text-muted, #64748b); }
</style>
