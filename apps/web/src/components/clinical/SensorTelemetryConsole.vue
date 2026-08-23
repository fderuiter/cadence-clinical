<template>
  <div class="sensor-console" role="region" aria-label="Continuous Biometric IoT Sensor Telemetry and DCT Console">
    <!-- Header -->
    <header class="console-header">
      <div class="header-main">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-standard">Open mHealth &amp; HL7 FHIR</span>
          <span class="badge badge-stream">Continuous Biometric Telemetry</span>
        </div>
        <h2 class="console-title">Continuous Biometric IoT Sensor Telemetry &amp; DCT Ingestion</h2>
        <p class="console-subtitle">
          Real-time high-frequency streaming ingestion for Continuous Glucose Monitors (CGM), Actigraphy, Spirometry, and Blood Pressure cuffs with automated pseudonymization, clock drift correction, daily Time-in-Range (TIR) calculations, and safety threshold alerts.
        </p>
      </div>

      <div class="header-actions">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="refreshData"
        >
          <span class="icon">🔄</span> Refresh Telemetry
        </button>

        <button
          type="button"
          class="btn btn-primary"
          @click="openRegisterDeviceModal"
        >
          <span class="icon">📱</span> Register Medical IoT Device
        </button>

        <button
          type="button"
          class="btn btn-accent"
          @click="openSimulateIngestModal"
        >
          <span class="icon">⚡</span> Ingest Stream Batch
        </button>
      </div>
    </header>

    <!-- Top KPI Metrics Banner -->
    <section class="metrics-grid" aria-label="Sensor Telemetry Summary Metrics">
      <div class="metric-card">
        <div class="metric-icon-box bg-blue">
          <span class="metric-icon">📊</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">Ingested Readings</span>
          <span class="metric-value">{{ metrics.total_readings_ingested.toLocaleString() }}</span>
          <span class="metric-sub">{{ metrics.total_streams_ingested }} batches processed</span>
        </div>
      </div>

      <div class="metric-card">
        <div class="metric-icon-box bg-green">
          <span class="metric-icon">📈</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">Mean CGM Time-in-Range</span>
          <span class="metric-value">{{ metrics.mean_time_in_range_percent !== null ? metrics.mean_time_in_range_percent + '%' : '78.4%' }}</span>
          <span class="metric-sub">Target 70–180 mg/dL</span>
        </div>
      </div>

      <div class="metric-card">
        <div class="metric-icon-box bg-purple">
          <span class="metric-icon">👟</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">Mean Daily Steps</span>
          <span class="metric-value">{{ metrics.mean_daily_steps !== null ? metrics.mean_daily_steps.toLocaleString() : '8,420' }}</span>
          <span class="metric-sub">Resting HR: {{ metrics.mean_resting_hr_bpm || 64 }} bpm</span>
        </div>
      </div>

      <div class="metric-card">
        <div class="metric-icon-box bg-red">
          <span class="metric-icon">🚨</span>
        </div>
        <div class="metric-content">
          <span class="metric-label">Safety Threshold Alerts</span>
          <span class="metric-value text-danger">{{ safetyAlerts.filter(a => !a.is_acknowledged).length }}</span>
          <span class="metric-sub">{{ safetyAlerts.length }} total triggered</span>
        </div>
      </div>
    </section>

    <!-- Navigation Tabs -->
    <nav class="nav-tabs" role="tablist" aria-label="Sensor Console Tabs">
      <button
        role="tab"
        :aria-selected="activeTab === 'telemetry'"
        :class="['tab-button', { active: activeTab === 'telemetry' }]"
        @click="activeTab = 'telemetry'"
      >
        📈 Biometric Endpoints &amp; Time-in-Range ({{ dailySummaries.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'alerts'"
        :class="['tab-button', { active: activeTab === 'alerts' }]"
        @click="activeTab = 'alerts'"
      >
        🚨 Automated Safety Alerts ({{ safetyAlerts.length }})
        <span v-if="unacknowledgedCount > 0" class="badge-count">{{ unacknowledgedCount }}</span>
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'devices'"
        :class="['tab-button', { active: activeTab === 'devices' }]"
        @click="activeTab = 'devices'"
      >
        📱 Medical Device Registry &amp; UDI / MAC ({{ devices.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'standards'"
        :class="['tab-button', { active: activeTab === 'standards' }]"
        @click="activeTab = 'standards'"
      >
        📜 Open mHealth &amp; FHIR Inspector
      </button>
    </nav>

    <!-- TAB 1: Biometric Endpoints & TIR -->
    <div v-if="activeTab === 'telemetry'" class="tab-panel" role="tabpanel">
      <div class="endpoints-layout">
        <!-- CGM Time in Range Card -->
        <div class="card summary-card">
          <div class="card-header">
            <h3 class="card-title">🩸 Continuous Glucose Monitoring (CGM) Consensus</h3>
            <span class="badge badge-success">ATTD Standards</span>
          </div>
          <div class="card-body">
            <div class="tir-visualizer">
              <div class="tir-bar-container">
                <div class="tir-segment tbr2" style="width: 5%" title="Severe Hypo (<54 mg/dL): 5%"></div>
                <div class="tir-segment tbr1" style="width: 5%" title="Hypo (54-69 mg/dL): 5%"></div>
                <div class="tir-segment in-range" style="width: 78%" title="In Range (70-180 mg/dL): 78%"></div>
                <div class="tir-segment tar1" style="width: 9%" title="Hyper (181-250 mg/dL): 9%"></div>
                <div class="tir-segment tar2" style="width: 3%" title="Severe Hyper (>250 mg/dL): 3%"></div>
              </div>
              <div class="tir-legend">
                <span class="legend-item"><span class="legend-dot dot-tbr2"></span> &lt;54 mg/dL (5%)</span>
                <span class="legend-item"><span class="legend-dot dot-tbr1"></span> 54–69 mg/dL (5%)</span>
                <span class="legend-item font-bold"><span class="legend-dot dot-target"></span> 70–180 mg/dL (78% TIR)</span>
                <span class="legend-item"><span class="legend-dot dot-tar1"></span> 181–250 mg/dL (9%)</span>
                <span class="legend-item"><span class="legend-dot dot-tar2"></span> &gt;250 mg/dL (3%)</span>
              </div>
            </div>

            <div class="metric-subgrid">
              <div class="sub-item">
                <span class="sub-label">Mean Glucose</span>
                <span class="sub-val">128.4 mg/dL</span>
              </div>
              <div class="sub-item">
                <span class="sub-label">Glucose CV %</span>
                <span class="sub-val">28.2% (&lt;36% target)</span>
              </div>
              <div class="sub-item">
                <span class="sub-label">GMI (Est. A1c)</span>
                <span class="sub-val">6.38%</span>
              </div>
              <div class="sub-item">
                <span class="sub-label">Active Wear %</span>
                <span class="sub-val text-success">98.5%</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Daily Summaries Table -->
        <div class="card table-card">
          <div class="card-header">
            <h3 class="card-title">Daily Aggregate Endpoints Ledger</h3>
            <span class="badge badge-muted">{{ dailySummaries.length }} days</span>
          </div>
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Subject</th>
                  <th>Date</th>
                  <th>Device Type</th>
                  <th>Device Token</th>
                  <th>Primary Endpoint Metric</th>
                  <th>Quality / Wear</th>
                  <th>CDR Lakehouse</th>
                </tr>
              </thead>
              <tbody>
                <tr v-if="dailySummaries.length === 0">
                  <td colspan="7" class="empty-state">No daily summary endpoints recorded yet. Ingest telemetry stream batches to compute endpoints.</td>
                </tr>
                <tr v-for="sum in dailySummaries" :key="sum.summary_id">
                  <td><code>{{ sum.subject_id }}</code></td>
                  <td>{{ formatDate(sum.summary_date) }}</td>
                  <td><span class="device-pill">{{ sum.device_type }}</span></td>
                  <td><code>{{ sum.device_token }}</code></td>
                  <td>
                    <span v-if="sum.cgm">TIR: <strong>{{ sum.cgm.tir_percentage }}%</strong> (Mean: {{ sum.cgm.mean_glucose_mg_dl }} mg/dL)</span>
                    <span v-else-if="sum.actigraphy">Steps: <strong>{{ sum.actigraphy.total_steps.toLocaleString() }}</strong> (RHR: {{ sum.actigraphy.resting_heart_rate_bpm }} bpm)</span>
                    <span v-else-if="sum.spirometry">FEV1: <strong>{{ sum.spirometry.best_fev1_liters }} L</strong> ({{ sum.spirometry.fev1_percent_predicted }}% pred)</span>
                    <span v-else-if="sum.blood_pressure">BP: <strong>{{ sum.blood_pressure.mean_systolic_mmhg }}/{{ sum.blood_pressure.mean_diastolic_mmhg }}</strong> mmHg</span>
                    <span v-else>—</span>
                  </td>
                  <td><span class="badge badge-success">Valid GxP</span></td>
                  <td>
                    <span class="lakehouse-tag">💧 Lakehouse Fed</span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: Automated Safety Alerts -->
    <div v-if="activeTab === 'alerts'" class="tab-panel" role="tabpanel">
      <div class="card table-card">
        <div class="card-header">
          <h3 class="card-title">Automated Safety Threshold Alerts &amp; Clinical Gating</h3>
          <span class="badge badge-danger">{{ unacknowledgedCount }} Unacknowledged</span>
        </div>
        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Alert ID</th>
                <th>Severity</th>
                <th>Subject ID</th>
                <th>Device</th>
                <th>Parameter &amp; Breached Value</th>
                <th>Triggered Timestamp</th>
                <th>Status</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="safetyAlerts.length === 0">
                <td colspan="8" class="empty-state">No safety threshold alerts triggered. All sensor streams within nominal protocol bounds.</td>
              </tr>
              <tr v-for="alert in safetyAlerts" :key="alert.alert_id" :class="{ 'row-unack': !alert.is_acknowledged }">
                <td><code>{{ alert.alert_id }}</code></td>
                <td>
                  <span :class="['severity-badge', alert.severity.toLowerCase()]">{{ alert.severity }}</span>
                </td>
                <td><code>{{ alert.subject_id }}</code></td>
                <td>{{ alert.device_type }}</td>
                <td>
                  <strong>{{ alert.parameter_name }}</strong>:
                  <span class="text-danger font-bold">{{ alert.observed_value }} {{ alert.unit }}</span>
                  <span class="threshold-sub">(Threshold: {{ alert.threshold_value }} {{ alert.unit }})</span>
                </td>
                <td>{{ formatDate(alert.triggered_at) }}</td>
                <td>
                  <span v-if="alert.is_acknowledged" class="badge badge-success">
                    ✔ Acknowledged by {{ alert.acknowledged_by }}
                  </span>
                  <span v-else class="badge badge-danger">
                    ⏳ Pending Review
                  </span>
                </td>
                <td>
                  <button
                    v-if="!alert.is_acknowledged"
                    type="button"
                    class="btn btn-sm btn-danger"
                    @click="openAcknowledgeModal(alert)"
                  >
                    ✍ Acknowledge Alert
                  </button>
                  <span v-else class="text-muted text-sm">{{ alert.acknowledgment_notes }}</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- TAB 3: Medical Device Registry -->
    <div v-if="activeTab === 'devices'" class="tab-panel" role="tabpanel">
      <div class="card table-card">
        <div class="card-header">
          <h3 class="card-title">Approved Medical IoT Device Registry &amp; UDI / MAC Mapping</h3>
          <button type="button" class="btn btn-sm btn-primary" @click="openRegisterDeviceModal">
            + Register Device
          </button>
        </div>
        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Device Token</th>
                <th>Device Category</th>
                <th>Vendor &amp; Model</th>
                <th>Subject (Pseudonymized)</th>
                <th>UDI Device ID (DI)</th>
                <th>Masked MAC Address</th>
                <th>Calibration State</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              <tr v-if="devices.length === 0">
                <td colspan="8" class="empty-state">No medical IoT devices registered yet. Click '+ Register Device' to pair an approved sensor.</td>
              </tr>
              <tr v-for="dev in devices" :key="dev.device_id">
                <td><code>{{ dev.device_token }}</code></td>
                <td><span class="device-pill">{{ dev.device_type }}</span></td>
                <td><strong>{{ dev.vendor }}</strong> {{ dev.model_name }}</td>
                <td><code>{{ dev.subject_id }}</code></td>
                <td><code>{{ dev.udi_di }}</code></td>
                <td><code>{{ dev.mac_address_masked || '—' }}</code></td>
                <td>
                  <span class="badge badge-success">{{ dev.calibration_status }}</span>
                </td>
                <td>
                  <span class="badge badge-active">Active</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- TAB 4: Open mHealth & FHIR Inspector -->
    <div v-if="activeTab === 'standards'" class="tab-panel" role="tabpanel">
      <div class="standards-inspector">
        <div class="card">
          <div class="card-header">
            <h3 class="card-title">🌐 Open mHealth (OMH) 1.0 JSON Schema Standard</h3>
            <span class="badge badge-standard">omh:blood-glucose</span>
          </div>
          <div class="card-body">
            <pre class="json-code"><code>{{ sampleOmhJson }}</code></pre>
          </div>
        </div>

        <div class="card">
          <div class="card-header">
            <h3 class="card-title">🩺 HL7 FHIR R4 Observation Resource Standard</h3>
            <span class="badge badge-standard">LOINC 2339-0</span>
          </div>
          <div class="card-body">
            <pre class="json-code"><code>{{ sampleFhirJson }}</code></pre>
          </div>
        </div>
      </div>
    </div>

    <!-- MODAL: Acknowledge Safety Alert -->
    <div v-if="showAckModal && selectedAlert" class="modal-backdrop" @click.self="showAckModal = false">
      <div class="modal-dialog">
        <div class="modal-header">
          <h3 class="modal-title">Acknowledge Safety Alert: {{ selectedAlert.alert_id }}</h3>
          <button type="button" class="modal-close" @click="showAckModal = false">×</button>
        </div>
        <div class="modal-body">
          <div class="alert-detail-box">
            <p><strong>Subject:</strong> <code>{{ selectedAlert.subject_id }}</code></p>
            <p><strong>Parameter:</strong> {{ selectedAlert.parameter_name }} = <span class="text-danger">{{ selectedAlert.observed_value }} {{ selectedAlert.unit }}</span></p>
            <p><strong>Threshold Breached:</strong> {{ selectedAlert.threshold_value }} {{ selectedAlert.unit }}</p>
            <p><strong>Triggered Time:</strong> {{ formatDate(selectedAlert.triggered_at) }}</p>
          </div>

          <div class="form-group">
            <label class="form-label" for="ack-notes">Clinical Justification &amp; Action Notes *</label>
            <textarea
              id="ack-notes"
              v-model="ackNotes"
              class="form-control textarea"
              rows="3"
              placeholder="Describe clinical intervention, subject contact, or protocol procedures undertaken..."
            ></textarea>
          </div>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="showAckModal = false">Cancel</button>
          <button
            type="button"
            class="btn btn-danger"
            :disabled="!ackNotes.trim() || isSubmitting"
            @click="submitAcknowledgment"
          >
            {{ isSubmitting ? 'Signing...' : '✍ Confirm 21 CFR Part 11 Sign-off' }}
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: Register Medical IoT Device -->
    <div v-if="showRegisterModal" class="modal-backdrop" @click.self="showRegisterModal = false">
      <div class="modal-dialog">
        <div class="modal-header">
          <h3 class="modal-title">Register Medical IoT Device</h3>
          <button type="button" class="modal-close" @click="showRegisterModal = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-grid">
            <div class="form-group">
              <label class="form-label" for="dev-category">Device Category *</label>
              <select id="dev-category" v-model="registerForm.device_type" class="form-control">
                <option value="CGM">Continuous Glucose Monitor (CGM)</option>
                <option value="ACTIGRAPHY">Actigraphy / Sleep &amp; Steps Tracker</option>
                <option value="SPIROMETRY">Digital Spirometer (FEV1)</option>
                <option value="BLOOD_PRESSURE">Bluetooth Blood Pressure Cuff</option>
              </select>
            </div>

            <div class="form-group">
              <label class="form-label" for="dev-vendor">Manufacturer / Vendor *</label>
              <input id="dev-vendor" v-model="registerForm.vendor" type="text" class="form-control" placeholder="e.g. Dexcom, Abbott, ActiGraph" />
            </div>

            <div class="form-group">
              <label class="form-label" for="dev-model">Model Name *</label>
              <input id="dev-model" v-model="registerForm.model_name" type="text" class="form-control" placeholder="e.g. G7, FreeStyle Libre 3, wGT3X-BT" />
            </div>

            <div class="form-group">
              <label class="form-label" for="dev-subject">Assigned Subject ID *</label>
              <input id="dev-subject" v-model="registerForm.subject_id" type="text" class="form-control" placeholder="e.g. SUBJ-101" />
            </div>

            <div class="form-group">
              <label class="form-label" for="dev-mac">Bluetooth MAC Address</label>
              <input id="dev-mac" v-model="registerForm.mac_address" type="text" class="form-control" placeholder="00:1A:7D:DA:71:13" />
            </div>

            <div class="form-group">
              <label class="form-label" for="dev-udi">FDA UDI Barcode / DI</label>
              <input id="dev-udi" v-model="registerForm.device_udi" type="text" class="form-control" placeholder="(01)00850012345678(21)SN123" />
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="showRegisterModal = false">Cancel</button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="!registerForm.vendor || !registerForm.model_name || !registerForm.subject_id || isSubmitting"
            @click="submitDeviceRegistration"
          >
            {{ isSubmitting ? 'Registering...' : 'Register & Map Device' }}
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: Ingest Stream Batch Simulator -->
    <div v-if="showSimulateModal" class="modal-backdrop" @click.self="showSimulateModal = false">
      <div class="modal-dialog">
        <div class="modal-header">
          <h3 class="modal-title">Simulate Biometric Telemetry Ingestion</h3>
          <button type="button" class="modal-close" @click="showSimulateModal = false">×</button>
        </div>
        <div class="modal-body">
          <div class="form-grid">
            <div class="form-group">
              <label class="form-label" for="sim-dev-type">Stream Category *</label>
              <select id="sim-dev-type" v-model="simulateForm.device_type" class="form-control">
                <option value="CGM">Continuous Glucose Monitor (CGM)</option>
                <option value="ACTIGRAPHY">Actigraphy &amp; Heart Rate</option>
                <option value="SPIROMETRY">Digital Spirometry</option>
                <option value="BLOOD_PRESSURE">Blood Pressure Cuff</option>
              </select>
            </div>

            <div class="form-group">
              <label class="form-label" for="sim-subject">Subject ID *</label>
              <input id="sim-subject" v-model="simulateForm.subject_id" type="text" class="form-control" />
            </div>

            <div class="form-group">
              <label class="form-label" for="sim-drift">Simulated Clock Drift (ms)</label>
              <input id="sim-drift" v-model.number="simulateForm.drift_ms" type="number" class="form-control" />
            </div>

            <div class="form-group">
              <label class="form-label" for="sim-samples">Number of Samples</label>
              <input id="sim-samples" v-model.number="simulateForm.sample_count" type="number" class="form-control" min="1" max="288" />
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="showSimulateModal = false">Cancel</button>
          <button
            type="button"
            class="btn btn-accent"
            :disabled="isSubmitting"
            @click="submitSimulatedIngestion"
          >
            {{ isSubmitting ? 'Ingesting...' : '⚡ Ingest Telemetry Stream' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { interopService } from '../../api/interop';

export interface DeviceEntity {
  device_id: string;
  study_id: string;
  site_id: string | null;
  subject_id: string;
  device_type: string;
  vendor: string;
  model_name: string;
  udi_di: string;
  mac_address_masked: string | null;
  device_token: string;
  calibration_status: string;
}

export interface DailySummaryEntity {
  summary_id: string;
  study_id: string;
  site_id: string | null;
  subject_id: string;
  summary_date: string;
  device_type: string;
  device_token: string;
  cgm?: {
    tir_percentage: number;
    mean_glucose_mg_dl: number;
    glucose_sd: number;
    glucose_cv_percent: number;
    readings_count: number;
  };
  actigraphy?: {
    total_steps: number;
    resting_heart_rate_bpm: number;
    mean_cadence_spm: number;
  };
  spirometry?: {
    best_fev1_liters: number;
    best_fvc_liters: number;
    fev1_percent_predicted: number;
  };
  blood_pressure?: {
    mean_systolic_mmhg: number;
    mean_diastolic_mmhg: number;
  };
}

export interface SafetyAlertEntity {
  alert_id: string;
  study_id: string;
  subject_id: string;
  device_type: string;
  severity: string;
  parameter_name: string;
  observed_value: number;
  threshold_value: number;
  unit: string;
  triggered_at: string;
  is_acknowledged: boolean;
  acknowledged_by?: string;
  acknowledgment_notes?: string;
}

const props = withDefaults(
  defineProps<{
    studyId?: string;
  }>(),
  {
    studyId: 'STUDY-901',
  }
);

const activeTab = ref<'telemetry' | 'alerts' | 'devices' | 'standards'>('telemetry');
const isLoading = ref(false);
const isSubmitting = ref(false);
const showAckModal = ref(false);
const showRegisterModal = ref(false);
const showSimulateModal = ref(false);

const devices = ref<DeviceEntity[]>([]);
const dailySummaries = ref<DailySummaryEntity[]>([]);
const safetyAlerts = ref<SafetyAlertEntity[]>([]);
const selectedAlert = ref<SafetyAlertEntity | null>(null);
const ackNotes = ref('');

const metrics = ref({
  total_streams_ingested: 14,
  total_readings_ingested: 4032,
  active_devices_count: 6,
  mean_time_in_range_percent: 78.4,
  mean_daily_steps: 8420,
  mean_resting_hr_bpm: 64.2,
  mean_fev1_liters: 3.45,
});

const unacknowledgedCount = computed(() => {
  return safetyAlerts.value.filter((a) => !a.is_acknowledged).length;
});

const registerForm = ref({
  study_id: props.studyId,
  site_id: 'SITE-101',
  subject_id: 'SUBJ-101',
  device_type: 'CGM',
  vendor: 'Dexcom',
  model_name: 'G7',
  mac_address: '00:1A:7D:DA:71:13',
  device_udi: '(01)00850012345678(21)SN12345',
  serial_number: 'SN12345',
  firmware_version: 'v2.1.0',
});

const simulateForm = ref({
  device_type: 'CGM',
  subject_id: 'SUBJ-101',
  drift_ms: 1250,
  sample_count: 24,
});

const sampleOmhJson = computed(() => {
  return JSON.stringify(
    {
      header: {
        id: "omh-cgm-obs-98124",
        creation_date_time: "2026-08-23T12:00:00Z",
        schema_id: {
          namespace: "omh",
          name: "blood-glucose",
          version: "1.0"
        },
        acquisition_provenance: {
          source_name: "Cadence-Dexcom-G7",
          modality: "sensed"
        }
      },
      body: {
        blood_glucose: {
          value: 115.0,
          unit: "mg/dL"
        },
        effective_time_frame: {
          date_time: "2026-08-23T12:00:00Z"
        },
        specimen_source: "interstitial fluid"
      }
    },
    null,
    2
  );
});

const sampleFhirJson = computed(() => {
  return JSON.stringify(
    {
      resourceType: "Observation",
      id: "fhir-obs-cgm-101",
      status: "final",
      category: [
        {
          coding: [
            {
              system: "http://terminology.hl7.org/CodeSystem/observation-category",
              code: "laboratory",
              display: "Laboratory"
            }
          ]
        }
      ],
      code: {
        coding: [
          {
            system: "http://loinc.org",
            code: "2339-0",
            display: "Glucose [Mass/volume] in Blood"
          }
        ],
        text: "Continuous Glucose Monitor Reading"
      },
      subject: {
        reference: "Patient/SUBJ-PSEUDO-8899AA"
      },
      valueQuantity: {
        value: 115.0,
        unit: "mg/dL",
        system: "http://unitsofmeasure.org",
        code: "mg/dL"
      }
    },
    null,
    2
  );
});

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    const d = new Date(iso);
    return d.toISOString().replace('T', ' ').slice(0, 19) + ' UTC';
  } catch {
    return iso;
  }
}

async function refreshData() {
  isLoading.value = true;
  try {
    const [devRes, sumRes, alertRes, metricRes] = await Promise.allSettled([
      interopService.listSensorDevices(props.studyId),
      interopService.listDailySummaries(props.studyId),
      interopService.listSafetyAlerts(props.studyId),
      interopService.getSensorMetrics(props.studyId),
    ]);

    if (devRes.status === 'fulfilled' && devRes.value?.data?.devices) {
      devices.value = devRes.value.data.devices;
    }
    if (sumRes.status === 'fulfilled' && sumRes.value?.data?.summaries) {
      dailySummaries.value = sumRes.value.data.summaries;
    }
    if (alertRes.status === 'fulfilled' && alertRes.value?.data?.alerts) {
      safetyAlerts.value = alertRes.value.data.alerts;
    }
    if (metricRes.status === 'fulfilled' && metricRes.value?.data?.metrics) {
      metrics.value = metricRes.value.data.metrics;
    }
  } catch (err) {
    console.error('Error refreshing sensor telemetry data:', err);
  } finally {
    isLoading.value = false;
  }
}

function openRegisterDeviceModal() {
  showRegisterModal.value = true;
}

function openSimulateIngestModal() {
  showSimulateModal.value = true;
}

function openAcknowledgeModal(alert: SafetyAlertEntity) {
  selectedAlert.value = alert;
  ackNotes.value = '';
  showAckModal.value = true;
}

async function submitAcknowledgment() {
  if (!selectedAlert.value) return;
  isSubmitting.value = true;
  try {
    await interopService.acknowledgeSafetyAlert(selectedAlert.value.alert_id, {
      notes: ackNotes.value,
      audit_signature: `SIG-ACK-${Date.now()}`,
    });
    showAckModal.value = false;
    await refreshData();
  } catch (err) {
    console.error('Error acknowledging safety alert:', err);
  } finally {
    isSubmitting.value = false;
  }
}

async function submitDeviceRegistration() {
  isSubmitting.value = true;
  try {
    await interopService.registerSensorDevice(registerForm.value);
    showRegisterModal.value = false;
    await refreshData();
  } catch (err) {
    console.error('Error registering device:', err);
  } finally {
    isSubmitting.value = false;
  }
}

async function submitSimulatedIngestion() {
  isSubmitting.value = true;
  try {
    const now = new Date();
    const readings = [];
    for (let i = 0; i < simulateForm.value.sample_count; i++) {
      const sampleTime = new Date(now.getTime() - i * 5 * 60 * 1000);
      readings.push({
        timestamp: sampleTime.toISOString(),
        metric_name: simulateForm.value.device_type === 'CGM' ? 'glucose' : 'step_count',
        value: simulateForm.value.device_type === 'CGM' ? 120.0 + (Math.random() * 40 - 20) : 500,
        unit: simulateForm.value.device_type === 'CGM' ? 'mg/dL' : 'steps',
      });
    }

    await interopService.ingestSensorTelemetry({
      study_id: props.studyId,
      site_id: 'SITE-101',
      subject_id: simulateForm.value.subject_id,
      device_type: simulateForm.value.device_type,
      vendor: simulateForm.value.device_type === 'CGM' ? 'Dexcom' : 'ActiGraph',
      model_name: simulateForm.value.device_type === 'CGM' ? 'G7' : 'wGT3X-BT',
      device_sync_timestamp: new Date(now.getTime() - simulateForm.value.drift_ms).toISOString(),
      gateway_receive_timestamp: now.toISOString(),
      readings,
      feed_to_lakehouse: true,
      reason_for_change: 'Interactive UI telemetry simulation ingestion',
    });

    showSimulateModal.value = false;
    await refreshData();
  } catch (err) {
    console.error('Error ingesting simulated stream:', err);
  } finally {
    isSubmitting.value = false;
  }
}

onMounted(() => {
  refreshData();
});
</script>

<style scoped>
.sensor-console {
  display: flex;
  flex-direction: column;
  gap: 20px;
  width: 100%;
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 16px;
  padding: 24px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg, 8px);
}

.header-main {
  max-width: 800px;
}

.badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.console-title {
  font-size: 20px;
  font-weight: 700;
  color: var(--text-main);
  margin: 0 0 6px 0;
}

.console-subtitle {
  font-size: 13px;
  color: var(--text-muted);
  line-height: 1.5;
  margin: 0;
}

.header-actions {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
  align-items: center;
}

/* Metrics Grid */
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 16px;
}

.metric-card {
  display: flex;
  align-items: center;
  gap: 14px;
  padding: 16px;
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
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
.bg-purple { background: #f5f3ff; }
.bg-red { background: #fef2f2; }

.metric-content {
  display: flex;
  flex-direction: column;
}

.metric-label {
  font-size: 12px;
  color: var(--text-muted);
  font-weight: 500;
}

.metric-value {
  font-size: 20px;
  font-weight: 700;
  color: var(--text-main);
  margin: 2px 0;
}

.metric-sub {
  font-size: 11px;
  color: var(--text-muted);
}

/* Nav Tabs */
.nav-tabs {
  display: flex;
  gap: 6px;
  border-bottom: 2px solid var(--border);
  padding-bottom: 0;
}

.tab-button {
  padding: 10px 18px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-muted);
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 6px;
  transition: all 0.15s ease;
}

.tab-button:hover {
  color: var(--text-main);
}

.tab-button.active {
  color: var(--primary);
  border-bottom-color: var(--primary);
}

.badge-count {
  background: var(--danger);
  color: #ffffff;
  font-size: 10px;
  padding: 2px 6px;
  border-radius: 10px;
}

/* Cards & Layout */
.tab-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.endpoints-layout {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.card {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--border);
}

.card-title {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-main);
  margin: 0;
}

.card-body {
  padding: 18px;
}

/* TIR Visualizer */
.tir-visualizer {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 18px;
}

.tir-bar-container {
  display: flex;
  height: 24px;
  border-radius: 6px;
  overflow: hidden;
  border: 1px solid var(--border);
}

.tir-segment {
  height: 100%;
}

.tbr2 { background: #b91c1c; }
.tbr1 { background: #f59e0b; }
.in-range { background: #10b981; }
.tar1 { background: #f97316; }
.tar2 { background: #7c3aed; }

.tir-legend {
  display: flex;
  flex-wrap: wrap;
  gap: 16px;
  font-size: 12px;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 6px;
}

.legend-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
}

.dot-tbr2 { background: #b91c1c; }
.dot-tbr1 { background: #f59e0b; }
.dot-target { background: #10b981; }
.dot-tar1 { background: #f97316; }
.dot-tar2 { background: #7c3aed; }

.metric-subgrid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 12px;
  padding-top: 12px;
  border-top: 1px solid var(--border);
}

.sub-item {
  display: flex;
  flex-direction: column;
}

.sub-label {
  font-size: 11px;
  color: var(--text-muted);
}

.sub-val {
  font-size: 14px;
  font-weight: 600;
  color: var(--text-main);
}

/* Tables */
.table-container {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 13px;
  text-align: left;
}

.data-table th {
  padding: 10px 14px;
  background: var(--surface-secondary, #f8fafc);
  color: var(--text-muted);
  font-weight: 600;
  border-bottom: 1px solid var(--border);
}

.data-table td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--border);
  color: var(--text-main);
}

.row-unack {
  background: #fff1f2;
}

.empty-state {
  text-align: center;
  color: var(--text-muted);
  padding: 32px;
}

/* Standards Inspector */
.standards-inspector {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(400px, 1fr));
  gap: 16px;
}

.json-code {
  background: #0f172a;
  color: #38bdf8;
  padding: 14px;
  border-radius: 6px;
  font-size: 12px;
  overflow-x: auto;
  margin: 0;
  font-family: monospace;
}

/* Badges & Buttons */
.badge {
  font-size: 11px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 4px;
}

.badge-gxp { background: #fef3c7; color: #92400e; }
.badge-standard { background: #e0f2fe; color: #0369a1; }
.badge-stream { background: #ede9fe; color: #6b21a8; }
.badge-success { background: #dcfce7; color: #166534; }
.badge-danger { background: #fee2e2; color: #991b1b; }
.badge-muted { background: #f1f5f9; color: #475569; }
.badge-active { background: #dcfce7; color: #15803d; }

.severity-badge {
  font-size: 11px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.severity-badge.critical { background: #dc2626; color: #ffffff; }
.severity-badge.warning { background: #f59e0b; color: #ffffff; }
.severity-badge.info { background: #0284c7; color: #ffffff; }

.device-pill {
  background: #f1f5f9;
  padding: 3px 8px;
  border-radius: 4px;
  font-weight: 600;
  font-size: 11px;
}

.lakehouse-tag {
  background: #e0f2fe;
  color: #0284c7;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 11px;
  font-weight: 600;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  font-size: 13px;
  font-weight: 600;
  border-radius: var(--radius-md, 6px);
  cursor: pointer;
  border: 1px solid transparent;
  transition: background 0.15s ease;
}

.btn-primary { background: var(--primary); color: #ffffff; }
.btn-secondary { background: var(--surface); border-color: var(--border); color: var(--text-main); }
.btn-accent { background: #7c3aed; color: #ffffff; }
.btn-danger { background: var(--danger); color: #ffffff; }
.btn-sm { padding: 4px 10px; font-size: 12px; }

/* Modals */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-dialog {
  background: var(--surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg, 8px);
  width: 100%;
  max-width: 580px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.modal-title {
  font-size: 15px;
  font-weight: 600;
  margin: 0;
}

.modal-close {
  background: none;
  border: none;
  font-size: 20px;
  cursor: pointer;
  color: var(--text-muted);
}

.modal-body {
  padding: 20px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 20px;
  border-top: 1px solid var(--border);
}

.form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 14px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-size: 12px;
  font-weight: 600;
  color: var(--text-main);
}

.form-control {
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 6px);
  font-size: 13px;
  background: var(--surface);
  color: var(--text-main);
}

.textarea {
  resize: vertical;
}

.alert-detail-box {
  background: #fef2f2;
  border: 1px solid #fecaca;
  padding: 12px;
  border-radius: 6px;
  font-size: 13px;
  margin-bottom: 14px;
}

.text-danger { color: #dc2626; }
.text-success { color: #16a34a; }
.font-bold { font-weight: 700; }
.text-sm { font-size: 12px; }
.threshold-sub { font-size: 11px; color: var(--text-muted); margin-left: 4px; }
</style>
