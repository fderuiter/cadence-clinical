<template>
  <div
    class="device-investigation-console"
    role="region"
    aria-label="Medical Device & IVD Clinical Investigation Suite"
  >
    <!-- Header -->
    <header class="console-header">
      <div class="header-main">
        <div class="title-group">
          <div class="badge-row">
            <span class="badge badge-regulatory">ISO 14155:2020</span>
            <span class="badge badge-regulatory">EU MDR 2017/745</span>
            <span class="badge badge-regulatory">EU IVDR 2017/746</span>
            <span class="badge badge-regulatory">FDA UDI 21 CFR 830</span>
            <span class="badge badge-audit">21 CFR Part 11</span>
          </div>
          <h2 class="console-title">
            Medical Device &amp; IVD Investigation Suite
          </h2>
          <p class="console-subtitle">
            Serialized UDI accountability, structured deficiency eCRF capture,
            automated ADE/SADE safety investigations, and programmatic RTSM
            quarantine dispensation blocking.
          </p>
        </div>
        <div class="header-study-control">
          <div class="control-item">
            <label for="study-selector">Study Protocol:</label>
            <input
              id="study-selector"
              v-model="studyId"
              type="text"
              class="text-input text-input-sm"
              placeholder="e.g. STUDY-DEV-101"
            />
          </div>
          <div class="control-item">
            <label for="site-selector">Site ID:</label>
            <input
              id="site-selector"
              v-model="siteId"
              type="text"
              class="text-input text-input-sm"
              placeholder="e.g. SITE-101"
            />
          </div>
          <button class="btn btn-secondary btn-sm" @click="refreshData">
            🔄 Refresh Hub
          </button>
        </div>
      </div>

      <!-- Top KPI Summary Metric Cards -->
      <div class="metrics-grid">
        <div class="metric-card">
          <span class="metric-label">Total Device Inventory</span>
          <span class="metric-value">{{ summary.total_devices }}</span>
          <span class="metric-sub"
            >{{ summary.available_devices }} Available |
            {{ summary.dispensed_devices }} Dispensed</span
          >
        </div>
        <div
          class="metric-card"
          :class="{ 'metric-alert-danger': summary.quarantined_devices > 0 }"
        >
          <span class="metric-label">RTSM Quarantined</span>
          <span class="metric-value text-danger">{{
            summary.quarantined_devices
          }}</span>
          <span class="metric-sub"
            >{{ summary.active_quarantines }} Active Lockdowns</span
          >
        </div>
        <div class="metric-card">
          <span class="metric-label">Reported Deficiencies</span>
          <span class="metric-value text-warning">{{
            summary.total_deficiencies
          }}</span>
          <span class="metric-sub"
            >{{ summary.open_safety_cases }} Open Safety Inquiries</span
          >
        </div>
        <div
          class="metric-card"
          :class="{ 'metric-alert-danger': summary.sade_count > 0 }"
        >
          <span class="metric-label">SADE / USADE Critical</span>
          <span class="metric-value text-danger">{{ summary.sade_count }}</span>
          <span class="metric-sub">ISO 14155 Expedited Vigilance</span>
        </div>
      </div>
    </header>

    <!-- Navigation Tabs -->
    <nav
      class="nav-tabs"
      role="tablist"
      aria-label="Device Investigation Console Views"
    >
      <button
        role="tab"
        :aria-selected="activeTab === 'inventory'"
        :class="['tab-button', { active: activeTab === 'inventory' }]"
        @click="activeTab = 'inventory'"
      >
        📦 UDI Inventory &amp; RTSM ({{ devices.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'deficiencies'"
        :class="['tab-button', { active: activeTab === 'deficiencies' }]"
        @click="activeTab = 'deficiencies'"
      >
        ⚠️ Deficiency &amp; Malfunction eCRF ({{ deficiencies.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'quarantine_safety'"
        :class="['tab-button', { active: activeTab === 'quarantine_safety' }]"
        @click="activeTab = 'quarantine_safety'"
      >
        🚨 Automated Quarantine &amp; Safety Cases ({{ safetyCases.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'regulatory_exports'"
        :class="['tab-button', { active: activeTab === 'regulatory_exports' }]"
        @click="activeTab = 'regulatory_exports'"
      >
        📋 Regulatory Incident &amp; SDTM Exports (DD / DO / MIR)
      </button>
    </nav>

    <!-- Main Workspace Panes -->
    <main class="main-content">
      <!-- TAB 1: UDI Inventory & RTSM Accountability -->
      <section
        v-if="activeTab === 'inventory'"
        class="tab-pane"
        aria-label="UDI Inventory and RTSM Accountability"
      >
        <!-- Barcode Scanner & Intake Section -->
        <div class="pane-card scanner-panel">
          <div class="panel-header">
            <h3 class="panel-title">
              📷 UDI Barcode Scanner &amp; Intake Engine
            </h3>
            <span class="badge badge-outline"
              >GS1-128 / GS1-DataMatrix / HIBCC</span
            >
          </div>
          <div class="scanner-grid">
            <div class="scanner-input-col">
              <label for="barcode-scanner-input"
                >Scan or Paste Physical Barcode:</label
              >
              <div class="input-with-action">
                <input
                  id="barcode-scanner-input"
                  v-model="rawBarcode"
                  type="text"
                  class="text-input code-font"
                  placeholder="(01)00840123456789(17)280630(10)LOT-99(21)SN-4410"
                  @input="handleBarcodeParse"
                />
                <button class="btn btn-secondary" @click="handleBarcodeParse">
                  🔍 Parse UDI
                </button>
              </div>
              <div class="sample-barcodes-row">
                <span class="sample-label">Presets:</span>
                <button
                  type="button"
                  class="btn-chip"
                  @click="loadSampleBarcode('GS1_STANDARD')"
                >
                  GS1 Cardiac Pacemaker (Class III)
                </button>
                <button
                  type="button"
                  class="btn-chip"
                  @click="loadSampleBarcode('GS1_IVD')"
                >
                  GS1 Rapid COVID-19 IVD Kit (Class B)
                </button>
                <button
                  type="button"
                  class="btn-chip"
                  @click="loadSampleBarcode('HIBCC')"
                >
                  HIBCC Infusion Pump (+H123...)
                </button>
                <button
                  type="button"
                  class="btn-chip chip-expired"
                  @click="loadSampleBarcode('EXPIRED')"
                >
                  Expired Stent (Test Blocking)
                </button>
              </div>
            </div>

            <!-- Live Parsed Elements Card -->
            <div class="parsed-udi-card">
              <div class="parsed-header">
                <span class="parsed-title">Parsed UDI Attributes</span>
                <span v-if="parsedUdi.is_valid" class="status-pill success"
                  >✓ Valid Checksum</span
                >
                <span v-else class="status-pill danger"
                  >✗ Checksum Mismatch</span
                >
              </div>
              <div class="parsed-grid">
                <div class="parsed-item">
                  <span class="p-label">Carrier Format:</span>
                  <span class="p-val code-font">{{
                    parsedUdi.udi_carrier_format
                  }}</span>
                </div>
                <div class="parsed-item">
                  <span class="p-label">Device Identifier (DI / GTIN):</span>
                  <span class="p-val code-font highlight">{{
                    parsedUdi.udi_di || "N/A"
                  }}</span>
                </div>
                <div class="parsed-item">
                  <span class="p-label">Batch / Lot Number (PI):</span>
                  <span class="p-val code-font">{{
                    parsedUdi.lot_number || "N/A"
                  }}</span>
                </div>
                <div class="parsed-item">
                  <span class="p-label">Serial Number (PI):</span>
                  <span class="p-val code-font">{{
                    parsedUdi.serial_number || "N/A"
                  }}</span>
                </div>
                <div class="parsed-item">
                  <span class="p-label">Expiration Date:</span>
                  <span class="p-val">{{
                    parsedUdi.expiration_date || "N/A"
                  }}</span>
                </div>
                <div class="parsed-item">
                  <span class="p-label">Software Version (SaMD):</span>
                  <span class="p-val">{{
                    parsedUdi.software_version || "N/A"
                  }}</span>
                </div>
              </div>
              <div class="parsed-actions">
                <button
                  class="btn btn-primary btn-sm"
                  :disabled="!parsedUdi.udi_di"
                  @click="registerParsedItem"
                >
                  📥 Ingest into RTSM Inventory
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- Inventory Ledger Table -->
        <div class="pane-card">
          <div class="panel-header">
            <div class="header-with-filters">
              <h3 class="panel-title">Serialized Device Inventory Ledger</h3>
              <div class="filter-pills">
                <button
                  :class="[
                    'filter-pill',
                    { active: inventoryFilter === 'ALL' },
                  ]"
                  @click="inventoryFilter = 'ALL'"
                >
                  All ({{ devices.length }})
                </button>
                <button
                  :class="[
                    'filter-pill',
                    { active: inventoryFilter === 'AVAILABLE' },
                  ]"
                  @click="inventoryFilter = 'AVAILABLE'"
                >
                  Available ({{
                    devices.filter((d) => d.status === "AVAILABLE").length
                  }})
                </button>
                <button
                  :class="[
                    'filter-pill',
                    { active: inventoryFilter === 'DISPENSED' },
                  ]"
                  @click="inventoryFilter = 'DISPENSED'"
                >
                  Dispensed ({{
                    devices.filter((d) => d.status === "DISPENSED").length
                  }})
                </button>
                <button
                  :class="[
                    'filter-pill filter-danger',
                    { active: inventoryFilter === 'QUARANTINED' },
                  ]"
                  @click="inventoryFilter = 'QUARANTINED'"
                >
                  Quarantined ({{
                    devices.filter((d) => d.status === "QUARANTINED").length
                  }})
                </button>
              </div>
            </div>
            <button class="btn btn-secondary btn-sm" @click="openRegisterModal">
              ➕ Manual Intake
            </button>
          </div>

          <div class="table-responsive">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Device Identifier</th>
                  <th>Brand &amp; Modality</th>
                  <th>Classification</th>
                  <th>UDI-DI (GTIN)</th>
                  <th>Lot / Serial Number</th>
                  <th>Expiry Date</th>
                  <th>Status</th>
                  <th>Dispensation Lock</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="item in filteredDevices"
                  :key="item.id"
                  :class="{ 'row-quarantined': item.status === 'QUARANTINED' }"
                >
                  <td class="code-font cell-bold">
                    {{ item.device_identifier }}
                  </td>
                  <td>
                    <div class="brand-cell">
                      <strong>{{ item.brand_name }}</strong>
                      <span class="sub-text">{{ item.device_type }}</span>
                    </div>
                  </td>
                  <td>
                    <span class="badge badge-class">{{
                      item.device_classification
                    }}</span>
                  </td>
                  <td class="code-font">
                    {{ item.udi_di }}
                  </td>
                  <td class="code-font">
                    <span>Lot: {{ item.lot_number }}</span>
                    <span v-if="item.serial_number" class="serial-tag"
                      >SN: {{ item.serial_number }}</span
                    >
                  </td>
                  <td>{{ formatDate(item.expiration_date) }}</td>
                  <td>
                    <span
                      :class="['status-pill', getStatusPillClass(item.status)]"
                    >
                      {{ item.status }}
                    </span>
                  </td>
                  <td>
                    <span
                      v-if="
                        item.is_dispensation_blocked ||
                        item.status === 'QUARANTINED'
                      "
                      class="lock-indicator text-danger"
                    >
                      🔒 BLOCKED
                    </span>
                    <span v-else class="lock-indicator text-success">
                      🔓 UNLOCKED
                    </span>
                  </td>
                  <td>
                    <div class="action-buttons-cell">
                      <button
                        class="btn btn-sm btn-primary"
                        :disabled="
                          item.status !== 'AVAILABLE' ||
                          item.is_dispensation_blocked
                        "
                        @click="openDispenseModal(item)"
                      >
                        Dispense
                      </button>
                      <button
                        v-if="item.status === 'AVAILABLE'"
                        class="btn btn-sm btn-outline-danger"
                        @click="openQuarantineModal(item)"
                      >
                        Quarantine
                      </button>
                    </div>
                  </td>
                </tr>
                <tr v-if="filteredDevices.length === 0">
                  <td colspan="9" class="empty-cell">
                    No investigational devices match the selected filters.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <!-- TAB 2: Deficiency & Malfunction eCRF Capture -->
      <section
        v-if="activeTab === 'deficiencies'"
        class="tab-pane"
        aria-label="Device Deficiency and Malfunction eCRF"
      >
        <div class="pane-card form-pane">
          <div class="panel-header">
            <div>
              <h3 class="panel-title">
                Structured Device Deficiency / Malfunction eCRF
              </h3>
              <p class="panel-subtitle">
                ISO 14155:2020 Clause 3.15 deficiency reporting and root-cause
                classification.
              </p>
            </div>
            <span class="badge badge-audit"
              >Auto-Triggers Safety &amp; Quarantine</span
            >
          </div>

          <form
            class="deficiency-form"
            @submit.prevent="submitDeficiencyReport"
          >
            <div class="form-grid-3">
              <div class="form-group">
                <label for="defic-device-select"
                  >Select Investigational Device:</label
                >
                <select
                  id="defic-device-select"
                  v-model="selectedDeviceForDefic"
                  class="select-input"
                  required
                  @change="populateDeviceFields"
                >
                  <option value="" disabled>
                    -- Select Device from RTSM Inventory --
                  </option>
                  <option v-for="d in devices" :key="d.id" :value="d.id">
                    {{ d.brand_name }} (SN: {{ d.serial_number || "N/A" }}, Lot:
                    {{ d.lot_number }})
                  </option>
                </select>
              </div>

              <div class="form-group">
                <label for="defic-subject-input"
                  >Impacted Subject ID (Optional):</label
                >
                <input
                  id="defic-subject-input"
                  v-model="deficForm.subject_id"
                  type="text"
                  class="text-input"
                  placeholder="e.g. SUBJ-101-0042"
                />
              </div>

              <div class="form-group">
                <label for="defic-type-select">Deficiency Category:</label>
                <select
                  id="defic-type-select"
                  v-model="deficForm.deficiency_type"
                  class="select-input"
                  required
                >
                  <option value="MALFUNCTION">
                    Device Malfunction / Component Failure
                  </option>
                  <option value="USABILITY_ERROR">
                    Usability / Use Error / User Interface Confusion
                  </option>
                  <option value="INADEQUATE_LABELING">
                    Inadequate Labeling / Instructions for Use (IFU)
                  </option>
                  <option value="DESIGN_DEFECT">
                    Design Defect / Inadequate Performance
                  </option>
                  <option value="PACKAGING_BREACH">
                    Packaging Integrity Breach / Sterile Compromise
                  </option>
                  <option value="SOFTWARE_FAULT">
                    SaMD Software Bug / Firmware Crash
                  </option>
                </select>
              </div>
            </div>

            <div class="form-grid-2">
              <div class="form-group">
                <label for="defic-incident-date"
                  >Incident Date &amp; Time:</label
                >
                <input
                  id="defic-incident-date"
                  v-model="deficForm.incident_date"
                  type="datetime-local"
                  class="text-input"
                  required
                />
              </div>

              <div class="form-group">
                <label for="defic-quar-scope"
                  >Automated RTSM Quarantine Scope:</label
                >
                <select
                  id="defic-quar-scope"
                  v-model="deficForm.quarantine_scope"
                  class="select-input"
                  required
                >
                  <option value="DEVICE_SERIAL">
                    Impacted Device Serial Number Only
                  </option>
                  <option value="SITE_LOT">
                    All Devices in Lot at This Site ({{ siteId || "SITE-101" }})
                  </option>
                  <option value="STUDY_GLOBAL_LOT">
                    Global Study Lot Lockdown (All Sites)
                  </option>
                </select>
              </div>
            </div>

            <!-- ISO 14155 Clinical Triage Box -->
            <div class="triage-box">
              <h4 class="triage-title">
                ISO 14155 / MDR Safety Triage &amp; SADE Risk Assessment
              </h4>
              <div class="triage-row">
                <label class="checkbox-label">
                  <input
                    v-model="deficForm.could_lead_to_sade"
                    type="checkbox"
                  />
                  <strong
                    >Could this deficiency have led to a Serious Adverse Device
                    Effect (SADE) if suitable action had not been taken?</strong
                  >
                </label>
              </div>

              <div class="triage-grid">
                <div class="form-group">
                  <label class="checkbox-label">
                    <input
                      v-model="deficForm.patient_involved"
                      type="checkbox"
                    />
                    <span>Participant / Patient Directly Involved</span>
                  </label>
                </div>
                <div class="form-group">
                  <label for="defic-health-impact"
                    >Observed Patient Health Consequence:</label
                  >
                  <select
                    id="defic-health-impact"
                    v-model="deficForm.health_impact"
                    class="select-input"
                  >
                    <option value="NONE">None (No patient impact)</option>
                    <option value="MILD_TRANSIENT">
                      Mild Transient Discomfort
                    </option>
                    <option value="SERIOUS_DETERIORATION">
                      Serious Deterioration in State of Health
                    </option>
                    <option value="PERMANENT_IMPAIRMENT">
                      Permanent Impairment of Body Structure/Function
                    </option>
                    <option value="LIFE_THREATENING">
                      Life-Threatening Incident
                    </option>
                    <option value="DEATH">Patient Death</option>
                  </select>
                </div>
              </div>
            </div>

            <div class="form-group">
              <label for="defic-desc-input"
                >Deficiency Verbatim Narrative &amp; Clinical Context:</label
              >
              <textarea
                id="defic-desc-input"
                v-model="deficForm.deficiency_description"
                class="textarea-input"
                rows="3"
                placeholder="Describe exact sequence of failure, error codes displayed, patient state, and immediate actions taken..."
                required
              />
            </div>

            <div class="form-grid-2">
              <div class="form-group">
                <label class="checkbox-label">
                  <input
                    v-model="deficForm.device_returned_to_sponsor"
                    type="checkbox"
                  />
                  <span
                    >Device Impounded &amp; Returned to Sponsor for Technical
                    Investigation</span
                  >
                </label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
                  v-if="deficForm.device_returned_to_sponsor"
                  v-model="deficForm.return_tracking_number"
                  type="text"
                  class="text-input text-input-sm mt-2"
                  placeholder="Carrier Tracking Number (e.g. 1Z99999999999)"
                />
              </div>

              <div class="form-group">
                <label for="defic-action-select"
                  >Action Taken with Device:</label
                >
                <select
                  id="defic-action-select"
                  v-model="deficForm.action_taken_device"
                  class="select-input"
                >
                  <option value="QUARANTINED_AT_SITE">
                    Quarantined at Clinical Site
                  </option>
                  <option value="RETURNED_TO_SPONSOR">
                    Returned to Sponsor / Manufacturer
                  </option>
                  <option value="REPLACED">
                    Replaced with New Device Item
                  </option>
                  <option value="DISPOSED">
                    Disposed per Biohazard Protocol
                  </option>
                  <option value="REPAIRED">Repaired on Site</option>
                  <option value="NONE">No Action Taken</option>
                </select>
              </div>
            </div>

            <!-- 21 CFR Part 11 Audit Field -->
            <div class="form-group signature-box">
              <label for="defic-reason-input"
                >21 CFR Part 11 Audit Justification (Reason for Change):</label
              >
              <input
                id="defic-reason-input"
                v-model="deficForm.reason_for_change"
                type="text"
                class="text-input"
                placeholder="e.g. Formal investigator report of device malfunction during Visit 4"
                required
              />
            </div>

            <div class="form-actions-bar">
              <button
                type="submit"
                class="btn btn-danger"
                :disabled="isSubmitting"
              >
                🚨 Submit Deficiency Report &amp; Execute Quarantine
              </button>
            </div>
          </form>
        </div>

        <!-- Deficiencies History Table -->
        <div class="pane-card mt-4">
          <h3 class="panel-title">Reported Device Deficiencies Log</h3>
          <div class="table-responsive">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Report ID</th>
                  <th>Device &amp; Serial</th>
                  <th>Category</th>
                  <th>Health Impact</th>
                  <th>SADE Risk</th>
                  <th>Safety Case ID</th>
                  <th>Quarantine</th>
                  <th>Date Reported</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="defic in deficiencies" :key="defic.id">
                  <td class="code-font cell-bold">
                    {{ defic.id.substring(0, 8) }}...
                  </td>
                  <td>
                    <strong>{{ defic.device_name }}</strong>
                    <div class="code-font sub-text">
                      SN: {{ defic.serial_number || "N/A" }} | Lot:
                      {{ defic.lot_number }}
                    </div>
                  </td>
                  <td>
                    <span class="badge badge-defic">{{
                      defic.deficiency_type
                    }}</span>
                  </td>
                  <td>
                    <span class="health-pill">{{ defic.health_impact }}</span>
                  </td>
                  <td>
                    <span
                      v-if="defic.could_lead_to_sade"
                      class="badge badge-danger"
                      >POTENTIAL SADE</span
                    >
                    <span v-else class="badge badge-neutral">NON-CRITICAL</span>
                  </td>
                  <td class="code-font">
                    <span v-if="defic.safety_case_id" class="case-link">{{
                      defic.safety_case_id
                    }}</span>
                    <span v-else class="text-muted">None</span>
                  </td>
                  <td>
                    <span class="status-pill danger"
                      >🔒 {{ defic.quarantine_scope }}</span
                    >
                  </td>
                  <td>{{ formatDate(defic.incident_date) }}</td>
                </tr>
                <tr v-if="deficiencies.length === 0">
                  <td colspan="8" class="empty-cell">
                    No device deficiencies reported for this study.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <!-- TAB 3: Automated Quarantine & Safety Cases Hub -->
      <section
        v-if="activeTab === 'quarantine_safety'"
        class="tab-pane"
        aria-label="Automated Quarantine and Safety Cases"
      >
        <!-- Active Quarantines Overview -->
        <div class="pane-card">
          <div class="panel-header">
            <div>
              <h3 class="panel-title">
                🔒 Active RTSM Inventory Quarantine Records
              </h3>
              <p class="panel-subtitle">
                Immediate dispensation lockouts preventing subject allocation of
                compromised devices.
              </p>
            </div>
            <span class="badge badge-danger"
              >{{
                quarantines.filter((q) => q.status === "ACTIVE").length
              }}
              Active Lockdowns</span
            >
          </div>

          <div class="table-responsive">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Quarantine ID</th>
                  <th>Scope</th>
                  <th>Target Lot / Serial</th>
                  <th>Reason / Trigger</th>
                  <th>Quarantined At</th>
                  <th>Quarantined By</th>
                  <th>Status</th>
                  <th>Part 11 Release</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="q in quarantines"
                  :key="q.id"
                  :class="{ 'row-active-quar': q.status === 'ACTIVE' }"
                >
                  <td class="code-font cell-bold">
                    {{ q.id.substring(0, 8) }}...
                  </td>
                  <td>
                    <span class="badge badge-scope">{{ q.scope }}</span>
                  </td>
                  <td class="code-font">
                    <span v-if="q.serial_number"
                      >SN: {{ q.serial_number }}</span
                    >
                    <span v-if="q.lot_number">Lot: {{ q.lot_number }}</span>
                  </td>
                  <td class="reason-cell">
                    {{ q.reason }}
                  </td>
                  <td>{{ formatDate(q.quarantined_at) }}</td>
                  <td>{{ q.quarantined_by }}</td>
                  <td>
                    <span
                      :class="[
                        'status-pill',
                        q.status === 'ACTIVE' ? 'danger' : 'success',
                      ]"
                    >
                      {{ q.status }}
                    </span>
                  </td>
                  <td>
                    <button
                      v-if="q.status === 'ACTIVE'"
                      class="btn btn-sm btn-primary"
                      @click="openReleaseModal(q)"
                    >
                      Release Quarantine
                    </button>
                    <span v-else class="sub-text text-success">
                      ✓ Released by {{ q.released_by }}
                    </span>
                  </td>
                </tr>
                <tr v-if="quarantines.length === 0">
                  <td colspan="8" class="empty-cell">
                    No quarantine records found.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Open Safety Investigation Cases Grid -->
        <div class="pane-card mt-4">
          <div class="panel-header">
            <div>
              <h3 class="panel-title">
                🛡️ Adverse Device Effect (ADE / SADE / USADE) Safety Inquiries
              </h3>
              <p class="panel-subtitle">
                ISO 14155 &amp; EU MDR vigilance countdown tracking with Safety
                Physician e-signature signoff.
              </p>
            </div>
            <span class="badge badge-audit"
              >Expedited Vigilance Clock Active</span
            >
          </div>

          <div class="safety-cases-grid">
            <div
              v-for="sc in safetyCases"
              :key="sc.id"
              class="safety-case-card"
              :class="{
                'card-sade-critical':
                  sc.classification === 'SADE' || sc.classification === 'USADE',
              }"
            >
              <div class="sc-header">
                <div class="sc-title-area">
                  <span class="case-number code-font">{{
                    sc.case_number
                  }}</span>
                  <span
                    :class="['badge', getSafetyBadgeClass(sc.classification)]"
                    >{{ sc.classification }}</span
                  >
                </div>
                <div
                  class="countdown-badge"
                  :class="{ overdue: sc.is_overdue }"
                >
                  <span class="clock-icon">⏱️</span>
                  <span>{{ sc.days_remaining }} days remaining</span>
                </div>
              </div>

              <div class="sc-details">
                <div class="sc-detail-row">
                  <span class="sc-k">Device:</span>
                  <span class="sc-v"
                    ><strong>{{ sc.device_name }}</strong></span
                  >
                </div>
                <div class="sc-detail-row">
                  <span class="sc-k">UDI-DI / Lot:</span>
                  <span class="sc-v code-font"
                    >{{ sc.device_udi_di }} | Lot {{ sc.lot_number }}</span
                  >
                </div>
                <div class="sc-detail-row">
                  <span class="sc-k">Causality Assessment:</span>
                  <span class="sc-v causality-tag">{{ sc.causality }}</span>
                </div>
                <div class="sc-detail-row">
                  <span class="sc-k">Vigilance Deadline:</span>
                  <span class="sc-v"
                    >{{ sc.vigilance_deadline_days }} calendar days ({{
                      formatDate(sc.vigilance_due_date)
                    }})</span
                  >
                </div>
                <div class="sc-detail-row">
                  <span class="sc-k">Status:</span>
                  <span
                    :class="[
                      'status-pill',
                      sc.status === 'RESOLVED' ? 'success' : 'warning',
                    ]"
                    >{{ sc.status }}</span
                  >
                </div>
              </div>

              <div class="sc-footer">
                <button
                  class="btn btn-sm btn-secondary"
                  @click="generateMirReport(sc)"
                >
                  📄 View MIR Form
                </button>
                <button
                  v-if="sc.status !== 'RESOLVED'"
                  class="btn btn-sm btn-primary"
                  @click="openAdjudicateModal(sc)"
                >
                  ✍️ Adjudicate &amp; Sign
                </button>
              </div>
            </div>

            <div
              v-if="safetyCases.length === 0"
              class="empty-box col-span-full"
            >
              No open Adverse Device Effect safety cases.
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 4: Regulatory Incident & SDTM Exports -->
      <section
        v-if="activeTab === 'regulatory_exports'"
        class="tab-pane"
        aria-label="Regulatory Incident and SDTM Exports"
      >
        <div class="pane-card export-pane">
          <div class="panel-header">
            <div>
              <h3 class="panel-title">
                CDISC SDTM &amp; EU MDR/IVDR Incident Reporting Compiler
              </h3>
              <p class="panel-subtitle">
                Export compliant CDISC SDTM DD (Device Deficiencies), DO (Device
                Tracking), and MIR v7.2 XML/JSON.
              </p>
            </div>
            <button
              class="btn btn-primary"
              :disabled="isSubmitting"
              @click="compileFullExportPackage"
            >
              📦 Compile Full Regulatory Package
            </button>
          </div>

          <div class="export-columns-grid">
            <!-- Left: SDTM DD Preview -->
            <div class="export-box">
              <div class="box-header">
                <h4 class="box-title">CDISC SDTM DD (Device Deficiencies)</h4>
                <button class="btn btn-sm btn-secondary" @click="exportSdtmDD">
                  Export DD (.json)
                </button>
              </div>
              <div class="dataset-preview code-font">
                <pre>{{ sdtmDDPreview }}</pre>
              </div>
            </div>

            <!-- Right: SDTM DO Preview -->
            <div class="export-box">
              <div class="box-header">
                <h4 class="box-title">
                  CDISC SDTM DO (Device Tracking &amp; Operations)
                </h4>
                <button class="btn btn-sm btn-secondary" @click="exportSdtmDO">
                  Export DO (.json)
                </button>
              </div>
              <div class="dataset-preview code-font">
                <pre>{{ sdtmDOPreview }}</pre>
              </div>
            </div>
          </div>

          <!-- Bottom: EU MDR MIR Form Viewer -->
          <div class="pane-card mt-4 mir-viewer-card">
            <div class="panel-header">
              <h4 class="panel-title">
                🇪🇺 EU MDR 2017/745 Manufacturer Incident Report (MIR v7.2 XML)
              </h4>
              <button class="btn btn-sm btn-secondary" @click="copyMirXml">
                📋 Copy XML
              </button>
            </div>
            <div class="xml-preview code-font">
              <pre>{{ mirXmlPreview }}</pre>
            </div>
          </div>
        </div>
      </section>
    </main>

    <!-- MODAL: Dispense Device -->
    <div
      v-if="showDispenseModal"
      ref="dispenseModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card">
        <h3 class="modal-title">Dispense Investigational Device to Subject</h3>
        <p class="modal-subtitle">
          Device: <strong>{{ activeModalDevice?.brand_name }}</strong> (SN:
          {{ activeModalDevice?.serial_number }})
        </p>

        <div class="form-group mt-3">
          <label for="modal-dispense-subject">Select Subject ID:</label>
          <input
            id="modal-dispense-subject"
            v-model="dispenseForm.subject_id"
            type="text"
            class="text-input"
            placeholder="e.g. SUBJ-101-0042"
            required
          />
        </div>

        <div class="form-group">
          <label for="modal-dispense-reason"
            >Reason for Change (21 CFR Part 11):</label
          >
          <input
            id="modal-dispense-reason"
            v-model="dispenseForm.reason_for_change"
            type="text"
            class="text-input"
            required
          />
        </div>

        <div v-if="dispenseError" class="alert-danger-box">
          ⚠️ {{ dispenseError }}
        </div>

        <div class="modal-actions">
          <button class="btn btn-secondary" @click="showDispenseModal = false">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="!dispenseForm.subject_id || isSubmitting"
            @click="executeDispense"
          >
            Confirm Dispensation
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: Manual Quarantine -->
    <div
      v-if="showQuarantineModal"
      ref="quarantineModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card">
        <h3 class="modal-title text-danger">⚠️ Initiate Device Quarantine</h3>
        <p class="modal-subtitle">
          Target: <strong>{{ activeModalDevice?.brand_name }}</strong> (Lot:
          {{ activeModalDevice?.lot_number }})
        </p>

        <div class="form-group mt-3">
          <label for="modal-quar-scope">Quarantine Scope:</label>
          <select
            id="modal-quar-scope"
            v-model="quarForm.scope"
            class="select-input"
          >
            <option value="DEVICE_SERIAL">
              Device Serial Number Only ({{ activeModalDevice?.serial_number }})
            </option>
            <option value="SITE_LOT">
              All Devices in Lot at This Site ({{ siteId || "SITE-101" }})
            </option>
            <option value="STUDY_GLOBAL_LOT">Global Study Lot Lockdown</option>
          </select>
        </div>

        <div class="form-group">
          <label for="modal-quar-reason"
            >Quarantine Justification &amp; Failure Mode:</label
          >
          <textarea
            id="modal-quar-reason"
            v-model="quarForm.reason"
            class="textarea-input"
            rows="3"
            placeholder="Provide engineering or clinical rationale for quarantine..."
            required
          />
        </div>

        <div class="modal-actions">
          <button
            class="btn btn-secondary"
            @click="showQuarantineModal = false"
          >
            Cancel
          </button>
          <button
            class="btn btn-danger"
            :disabled="!quarForm.reason || isSubmitting"
            @click="executeManualQuarantine"
          >
            Lockdown &amp; Block Dispensation
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: Quarantine Release -->
    <div
      v-if="showReleaseModal"
      ref="releaseModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card">
        <h3 class="modal-title">🔓 Release Device Quarantine</h3>
        <p class="modal-subtitle">
          Quarantine ID: {{ activeModalQuarantine?.id }}
        </p>

        <div class="form-group mt-3">
          <label for="modal-rel-just"
            >Engineering / QA Release Justification:</label
          >
          <textarea
            id="modal-rel-just"
            v-model="releaseForm.justification"
            class="textarea-input"
            rows="3"
            placeholder="Confirm that technical assessment passed and device integrity is verified..."
            required
          />
        </div>

        <div class="form-group">
          <label for="modal-rel-sig"
            >21 CFR Part 11 Electronic Signature Token:</label
          >
          <input
            id="modal-rel-sig"
            v-model="releaseForm.signature_token"
            type="password"
            class="text-input"
            placeholder="Enter authorized signature key / token..."
            required
          />
        </div>

        <div class="modal-actions">
          <button class="btn btn-secondary" @click="closeReleaseModal">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="
              !releaseForm.justification ||
              !releaseForm.signature_token ||
              isSubmitting
            "
            @click="executeReleaseQuarantine"
          >
            Authorize Quarantine Release
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: Safety Physician Adjudication -->
    <div
      v-if="showAdjudicateModal"
      ref="adjudicateModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card">
        <h3 class="modal-title">✍️ Safety Physician Formal Adjudication</h3>
        <p class="modal-subtitle">
          Case: {{ activeModalCase?.case_number }} | Device:
          {{ activeModalCase?.device_name }}
        </p>

        <div class="form-grid-2 mt-3">
          <div class="form-group">
            <label for="modal-adj-class">Final Event Classification:</label>
            <select
              id="modal-adj-class"
              v-model="adjudicateForm.classification"
              class="select-input"
            >
              <option value="ADE">
                Adverse Device Effect (ADE - Non-Serious)
              </option>
              <option value="SADE">Serious Adverse Device Effect (SADE)</option>
              <option value="USADE">
                Unanticipated Serious Adverse Device Effect (USADE)
              </option>
              <option value="DEVICE_DEFICIENCY_INCIDENT">
                Device Deficiency Incident
              </option>
            </select>
          </div>

          <div class="form-group">
            <label for="modal-adj-causality"
              >Causal Relationship Assessment:</label
            >
            <select
              id="modal-adj-causality"
              v-model="adjudicateForm.causality"
              class="select-input"
            >
              <option value="CERTAIN">Certain</option>
              <option value="PROBABLE">Probable</option>
              <option value="POSSIBLE">Possible</option>
              <option value="UNLIKELY">Unlikely</option>
              <option value="NOT_RELATED">Not Related</option>
              <option value="UNASSESSABLE">Unassessable</option>
            </select>
          </div>
        </div>

        <div class="form-group">
          <label for="modal-adj-notes"
            >Safety Physician Assessment Notes:</label
          >
          <textarea
            id="modal-adj-notes"
            v-model="adjudicateForm.notes"
            class="textarea-input"
            rows="3"
            placeholder="Clinical evaluation of relation between device malfunction and patient outcome..."
          />
        </div>

        <div class="form-grid-2">
          <label class="checkbox-label">
            <input v-model="adjudicateForm.capa_required" type="checkbox" />
            <span>CAPA Initiation Required</span>
          </label>
          <label class="checkbox-label">
            <input v-model="adjudicateForm.fsca_required" type="checkbox" />
            <span>Field Safety Corrective Action (FSCA)</span>
          </label>
        </div>

        <div class="form-group mt-3">
          <label for="modal-adj-sig"
            >Safety Physician Electronic Signature:</label
          >
          <input
            id="modal-adj-sig"
            v-model="adjudicateForm.signature_token"
            type="password"
            class="text-input"
            placeholder="Enter e-signature credential..."
            required
          />
        </div>

        <div class="modal-actions">
          <button class="btn btn-secondary" @click="closeAdjudicateModal">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="!adjudicateForm.signature_token || isSubmitting"
            @click="executeAdjudication"
          >
            Submit Signed Adjudication
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { useFocusTrap } from "ui";
import { demoRandomInt } from "../../utils/demoMode.js";

// State
const studyId = ref("STUDY-DEV-101");
const siteId = ref("SITE-101");
const activeTab = ref("inventory");
const isSubmitting = ref(false);
const inventoryFilter = ref("ALL");

// Data structures
const devices = ref([]);
const deficiencies = ref([]);
const quarantines = ref([]);
const safetyCases = ref([]);

const summary = ref({
  total_devices: 0,
  available_devices: 0,
  dispensed_devices: 0,
  quarantined_devices: 0,
  total_deficiencies: 0,
  open_safety_cases: 0,
  sade_count: 0,
  active_quarantines: 0,
});

// Barcode scanner
const rawBarcode = ref("");
const parsedUdi = ref({
  raw_barcode: "",
  udi_carrier_format: "GS1_128",
  udi_di: "",
  lot_number: "",
  serial_number: "",
  expiration_date: "",
  software_version: "",
  is_valid: true,
});

// Forms
const selectedDeviceForDefic = ref("");
const deficForm = ref({
  subject_id: "",
  deficiency_type: "MALFUNCTION",
  incident_date: new Date().toISOString().slice(0, 16),
  quarantine_scope: "DEVICE_SERIAL",
  could_lead_to_sade: false,
  patient_involved: false,
  health_impact: "NONE",
  deficiency_description: "",
  device_returned_to_sponsor: false,
  return_tracking_number: "",
  action_taken_device: "QUARANTINED_AT_SITE",
  reason_for_change: "Reported investigational medical device deficiency",
});

// Modal state
const showDispenseModal = ref(false);
const dispenseModalRef = ref(null);
const activeModalDevice = ref(null);
const dispenseForm = ref({
  subject_id: "",
  reason_for_change: "Dispense investigational device",
});
const dispenseError = ref("");

const showQuarantineModal = ref(false);
const quarantineModalRef = ref(null);
const quarForm = ref({ scope: "DEVICE_SERIAL", reason: "" });

const showReleaseModal = ref(false);
const releaseModalRef = ref(null);
const activeModalQuarantine = ref(null);
const releaseForm = ref({ justification: "", signature_token: "" });

const showAdjudicateModal = ref(false);
const adjudicateModalRef = ref(null);
const activeModalCase = ref(null);
const adjudicateForm = ref({
  classification: "SADE",
  causality: "PROBABLE",
  notes: "",
  capa_required: false,
  fsca_required: false,
  signature_token: "",
});

function closeReleaseModal() {
  releaseForm.value.signature_token = "";
  showReleaseModal.value = false;
}

function closeAdjudicateModal() {
  adjudicateForm.value.signature_token = "";
  showAdjudicateModal.value = false;
}

useFocusTrap(dispenseModalRef, showDispenseModal, {
  onEscape: () => {
    showDispenseModal.value = false;
  },
});

useFocusTrap(quarantineModalRef, showQuarantineModal, {
  onEscape: () => {
    showQuarantineModal.value = false;
  },
});

useFocusTrap(releaseModalRef, showReleaseModal, {
  onEscape: closeReleaseModal,
});

useFocusTrap(adjudicateModalRef, showAdjudicateModal, {
  onEscape: closeAdjudicateModal,
});

// Previews
const sdtmDDPreview = ref(
  '// CDISC SDTM DD dataset preview\n[\n  {\n    "STUDYID": "STUDY-DEV-101",\n    "DOMAIN": "DD",\n    "USUBJID": "STUDY-DEV-101-SITE-101-SUBJ-0042",\n    "DDSEQ": 1,\n    "DDTESTCD": "MALFUNC",\n    "DDTEST": "Device Malfunction",\n    "DDCAT": "DEVICE DEFICIENCY",\n    "DDORRES": "Display frozen on boot",\n    "DDSTRESC": "Display frozen on boot",\n    "DDDEVID": "00840123456789",\n    "DDLOT": "LOT-99",\n    "DDSER": "SN-4410",\n    "DDAT": "QUARANTINED_AT_SITE"\n  }\n]'
);

const sdtmDOPreview = ref(
  '// CDISC SDTM DO dataset preview\n[\n  {\n    "STUDYID": "STUDY-DEV-101",\n    "DOMAIN": "DO",\n    "USUBJID": "STUDY-DEV-101-SITE-101-SUBJ-0042",\n    "DOSEQ": 1,\n    "DODEVID": "DEV-00840123456789-SN-4410",\n    "DOTESTCD": "DISP",\n    "DOTEST": "Device Dispensation",\n    "DOCAT": "DEVICE TRACKING",\n    "DOSTATUS": "DISPENSED",\n    "DODI": "00840123456789",\n    "DOLOT": "LOT-99",\n    "DOSER": "SN-4410"\n  }\n]'
);

const mirXmlPreview = ref(`<?xml version="1.0" encoding="UTF-8"?>
<ManufacturerIncidentReport version="7.2" xmlns="urn:eudamed:mdr:mir:v7.2">
  <AdministrativeInformation>
    <ReportReference>MIR-STUDY-DEV-101-DEF-4410</ReportReference>
    <ReportType>INITIAL_INCIDENT_REPORT</ReportType>
    <CompetentAuthority>EUDAMED / National Competent Authority</CompetentAuthority>
  </AdministrativeInformation>
  <DeviceIdentification>
    <BrandName>CardioPulse Active Implantable Pacemaker</BrandName>
    <UDI_DI>00840123456789</UDI_DI>
    <LotNumber>LOT-99</LotNumber>
    <SerialNumber>SN-4410</SerialNumber>
    <StudyIdentifier>STUDY-DEV-101</StudyIdentifier>
  </DeviceIdentification>
  <IncidentInformation>
    <Classification>SADE</Classification>
    <Causality>PROBABLE</Causality>
    <IsUSADE>false</IsUSADE>
  </IncidentInformation>
  <ActionsTaken>
    <ActionTaken>QUARANTINED_AT_SITE</ActionTaken>
    <CAPAInitiated>true</CAPAInitiated>
    <Status>OPEN</Status>
  </ActionsTaken>
</ManufacturerIncidentReport>`);

// Filtered devices
const filteredDevices = computed(() => {
  if (inventoryFilter.value === "ALL") return devices.value;
  return devices.value.filter((d) => d.status === inventoryFilter.value);
});

// Methods
function handleBarcodeParse() {
  const bc = rawBarcode.value.trim();
  if (!bc) return;

  if (bc.startsWith("+")) {
    parsedUdi.value = {
      raw_barcode: bc,
      udi_carrier_format: "HIBCC_128",
      udi_di: "H12345678901",
      lot_number: "LOT-HIBCC-01",
      serial_number: "SN-9001",
      expiration_date: "2028-12-31",
      software_version: "v3.1.0",
      is_valid: true,
    };
  } else if (bc.includes("(01)")) {
    parsedUdi.value = {
      raw_barcode: bc,
      udi_carrier_format: "GS1_128",
      udi_di: "00840123456789",
      lot_number: "LOT-99",
      serial_number: "SN-4410",
      expiration_date: "2028-06-30",
      software_version: "v2.4.1",
      is_valid: true,
    };
  } else {
    parsedUdi.value = {
      raw_barcode: bc,
      udi_carrier_format: "RAW_UDI",
      udi_di: bc,
      lot_number: "LOT-GENERIC",
      serial_number: `SN-${demoRandomInt(1000, 9999, "device-serial")}`,
      expiration_date: "2027-12-31",
      software_version: "",
      is_valid: true,
    };
  }
}

function loadSampleBarcode(type) {
  if (type === "GS1_STANDARD") {
    rawBarcode.value = "(01)00840123456789(17)280630(10)LOT-99(21)SN-4410";
  } else if (type === "GS1_IVD") {
    rawBarcode.value =
      "(01)00840987654321(17)271231(10)LOT-IVD-05(21)SN-IVD-102";
  } else if (type === "HIBCC") {
    rawBarcode.value = "+H12345678901/$1728123110LOT-HIBCC-0121SN-9001";
  } else if (type === "EXPIRED") {
    rawBarcode.value =
      "(01)00840111222333(17)220101(10)LOT-OLD(21)SN-EXPIRED-01";
  }
  handleBarcodeParse();
}

function registerParsedItem() {
  const newItem = {
    id: `dev-${Date.now()}`,
    study_id: studyId.value,
    site_id: siteId.value,
    device_identifier: `DEV-${parsedUdi.value.udi_di}-${parsedUdi.value.serial_number || "LOT"}`,
    brand_name: parsedUdi.value.udi_di.includes("9876")
      ? "Rapid Viral IVD Diagnostic"
      : "CardioPulse Pacemaker",
    device_type: parsedUdi.value.udi_di.includes("9876")
      ? "DIAGNOSTIC_IVD"
      : "IMPLANTABLE",
    device_classification: parsedUdi.value.udi_di.includes("9876")
      ? "IVD_CLASS_B"
      : "CLASS_III",
    udi_di: parsedUdi.value.udi_di,
    lot_number: parsedUdi.value.lot_number || "LOT-DEFAULT",
    serial_number: parsedUdi.value.serial_number,
    expiration_date: parsedUdi.value.expiration_date || "2028-12-31",
    status: "AVAILABLE",
    is_dispensation_blocked: false,
  };
  devices.value.unshift(newItem);
  updateMetrics();
  rawBarcode.value = "";
}

function openRegisterModal() {
  loadSampleBarcode("GS1_STANDARD");
  registerParsedItem();
}

function populateDeviceFields() {
  const dev = devices.value.find((d) => d.id === selectedDeviceForDefic.value);
  if (dev && dev.allocated_subject_id) {
    deficForm.value.subject_id = dev.allocated_subject_id;
  }
}

function openDispenseModal(device) {
  activeModalDevice.value = device;
  dispenseForm.value.subject_id = "";
  dispenseError.value = "";
  showDispenseModal.value = true;
}

function executeDispense() {
  if (!activeModalDevice.value) return;

  // Strict quarantine and expiration check
  if (
    activeModalDevice.value.status === "QUARANTINED" ||
    activeModalDevice.value.is_dispensation_blocked
  ) {
    dispenseError.value = `Cannot dispense device: Device ${activeModalDevice.value.serial_number} is QUARANTINED.`;
    return;
  }

  const expDate = new Date(activeModalDevice.value.expiration_date);
  if (expDate < new Date()) {
    dispenseError.value = `Cannot dispense device: Device expired on ${activeModalDevice.value.expiration_date}.`;
    return;
  }

  activeModalDevice.value.status = "DISPENSED";
  activeModalDevice.value.allocated_subject_id = dispenseForm.value.subject_id;
  showDispenseModal.value = false;
  updateMetrics();
}

function openQuarantineModal(device) {
  activeModalDevice.value = device;
  quarForm.value.reason = "Engineering inspection flag";
  showQuarantineModal.value = true;
}

function executeManualQuarantine() {
  if (!activeModalDevice.value) return;
  activeModalDevice.value.status = "QUARANTINED";
  activeModalDevice.value.is_dispensation_blocked = true;

  const newQuar = {
    id: `quar-${Date.now()}`,
    study_id: studyId.value,
    site_id: siteId.value,
    lot_number: activeModalDevice.value.lot_number,
    serial_number: activeModalDevice.value.serial_number,
    scope: quarForm.value.scope,
    status: "ACTIVE",
    reason: quarForm.value.reason,
    quarantined_at: new Date().toISOString(),
    quarantined_by: "investigator.crc",
  };
  quarantines.value.unshift(newQuar);
  showQuarantineModal.value = false;
  updateMetrics();
}

function submitDeficiencyReport() {
  const dev =
    devices.value.find((d) => d.id === selectedDeviceForDefic.value) ||
    devices.value[0];
  const isSade =
    deficForm.value.could_lead_to_sade ||
    deficForm.value.health_impact !== "NONE";
  const classification = isSade ? "SADE" : "ADE";
  const caseId = `DEV-CASE-${studyId.value}-${demoRandomInt(1000, 9999, "device-case")}`;

  const newDefic = {
    id: `defic-${Date.now()}`,
    device_name: dev ? dev.brand_name : "Investigational Medical Device",
    udi_di: dev ? dev.udi_di : "00840123456789",
    lot_number: dev ? dev.lot_number : "LOT-99",
    serial_number: dev ? dev.serial_number : "SN-4410",
    deficiency_type: deficForm.value.deficiency_type,
    deficiency_description: deficForm.value.deficiency_description,
    incident_date: deficForm.value.incident_date,
    could_lead_to_sade: deficForm.value.could_lead_to_sade,
    patient_involved: deficForm.value.patient_involved,
    health_impact: deficForm.value.health_impact,
    quarantine_scope: deficForm.value.quarantine_scope,
    safety_case_id: caseId,
    safety_classification: classification,
  };
  deficiencies.value.unshift(newDefic);

  // Auto-Quarantine device in RTSM
  if (dev) {
    dev.status = "QUARANTINED";
    dev.is_dispensation_blocked = true;
  }

  // Add Quarantine Record
  const newQuar = {
    id: `quar-${Date.now()}`,
    study_id: studyId.value,
    site_id: siteId.value,
    lot_number: dev ? dev.lot_number : "LOT-99",
    serial_number: dev ? dev.serial_number : "SN-4410",
    scope: deficForm.value.quarantine_scope,
    status: "ACTIVE",
    reason: `Automated quarantine: ${deficForm.value.deficiency_type} - ${deficForm.value.deficiency_description}`,
    quarantined_at: new Date().toISOString(),
    quarantined_by: "system.safety_trigger",
  };
  quarantines.value.unshift(newQuar);

  // Open Safety Case in Apps/Safety
  const newSafetyCase = {
    id: `sc-${Date.now()}`,
    case_number: caseId,
    device_name: newDefic.device_name,
    device_udi_di: newDefic.udi_di,
    lot_number: newDefic.lot_number,
    serial_number: newDefic.serial_number,
    classification: classification,
    causality: "POSSIBLE",
    is_usade: false,
    vigilance_deadline_days: isSade ? 10 : 15,
    vigilance_due_date: new Date(
      Date.now() + (isSade ? 10 : 15) * 86400000
    ).toISOString(),
    days_remaining: isSade ? 10 : 15,
    is_overdue: false,
    status: "OPEN",
  };
  safetyCases.value.unshift(newSafetyCase);

  updateMetrics();
  activeTab.value = "quarantine_safety";
}

function openReleaseModal(quar) {
  activeModalQuarantine.value = quar;
  releaseForm.value.justification =
    "Engineering investigation completed; device verified safe.";
  releaseForm.value.signature_token = "sig_token_part11_demo";
  showReleaseModal.value = true;
}

function executeReleaseQuarantine() {
  if (!activeModalQuarantine.value) return;
  activeModalQuarantine.value.status = "RELEASED";
  activeModalQuarantine.value.released_by = "qa.director";

  // Release affected devices
  devices.value.forEach((d) => {
    if (d.lot_number === activeModalQuarantine.value.lot_number) {
      d.status = "AVAILABLE";
      d.is_dispensation_blocked = false;
    }
  });

  showReleaseModal.value = false;
  updateMetrics();
}

function openAdjudicateModal(sc) {
  activeModalCase.value = sc;
  adjudicateForm.value.classification = sc.classification;
  adjudicateForm.value.causality = "PROBABLE";
  adjudicateForm.value.notes =
    "Confirmed Serious Adverse Device Effect following component stress evaluation.";
  adjudicateForm.value.capa_required = true;
  adjudicateForm.value.fsca_required = false;
  adjudicateForm.value.signature_token = "physician_sig_token_demo";
  showAdjudicateModal.value = true;
}

function executeAdjudication() {
  if (!activeModalCase.value) return;
  activeModalCase.value.classification = adjudicateForm.value.classification;
  activeModalCase.value.causality = adjudicateForm.value.causality;
  activeModalCase.value.status = "RESOLVED";
  showAdjudicateModal.value = false;
  updateMetrics();
}

function generateMirReport(_sc) {
  activeTab.value = "regulatory_exports";
}

function exportSdtmDD() {
  const blob = new Blob([sdtmDDPreview.value], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `SDTM_DD_${studyId.value}.json`;
  a.click();
}

function exportSdtmDO() {
  const blob = new Blob([sdtmDOPreview.value], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `SDTM_DO_${studyId.value}.json`;
  a.click();
}

function compileFullExportPackage() {
  exportSdtmDD();
  setTimeout(exportSdtmDO, 300);
}

function copyMirXml() {
  navigator.clipboard.writeText(mirXmlPreview.value);
}

function updateMetrics() {
  summary.value.total_devices = devices.value.length;
  summary.value.available_devices = devices.value.filter(
    (d) => d.status === "AVAILABLE"
  ).length;
  summary.value.dispensed_devices = devices.value.filter(
    (d) => d.status === "DISPENSED"
  ).length;
  summary.value.quarantined_devices = devices.value.filter(
    (d) => d.status === "QUARANTINED"
  ).length;
  summary.value.total_deficiencies = deficiencies.value.length;
  summary.value.open_safety_cases = safetyCases.value.filter(
    (c) => c.status === "OPEN"
  ).length;
  summary.value.sade_count = safetyCases.value.filter(
    (c) => c.classification === "SADE" || c.classification === "USADE"
  ).length;
  summary.value.active_quarantines = quarantines.value.filter(
    (q) => q.status === "ACTIVE"
  ).length;
}

function refreshData() {
  updateMetrics();
}

function formatDate(iso) {
  if (!iso) return "N/A";
  try {
    return new Date(iso).toLocaleDateString("en-US", {
      year: "numeric",
      month: "short",
      day: "numeric",
    });
  } catch {
    return String(iso);
  }
}

function getStatusPillClass(status) {
  if (status === "AVAILABLE") return "success";
  if (status === "DISPENSED") return "info";
  if (status === "QUARANTINED") return "danger";
  if (status === "DEFICIENT") return "warning";
  return "neutral";
}

function getSafetyBadgeClass(cls) {
  if (cls === "SADE" || cls === "USADE") return "badge-danger";
  if (cls === "ADE") return "badge-warning";
  return "badge-neutral";
}

// Initial mock data setup for interactive experience
onMounted(() => {
  devices.value = [
    {
      id: "dev-1",
      study_id: "STUDY-DEV-101",
      site_id: "SITE-101",
      device_identifier: "DEV-00840123456789-SN-4410",
      brand_name: "CardioPulse Active Pacemaker",
      device_type: "IMPLANTABLE",
      device_classification: "CLASS_III",
      udi_di: "00840123456789",
      lot_number: "LOT-99",
      serial_number: "SN-4410",
      expiration_date: "2028-06-30",
      status: "AVAILABLE",
      is_dispensation_blocked: false,
    },
    {
      id: "dev-2",
      study_id: "STUDY-DEV-101",
      site_id: "SITE-101",
      device_identifier: "DEV-00840123456789-SN-4411",
      brand_name: "CardioPulse Active Pacemaker",
      device_type: "IMPLANTABLE",
      device_classification: "CLASS_III",
      udi_di: "00840123456789",
      lot_number: "LOT-99",
      serial_number: "SN-4411",
      expiration_date: "2028-06-30",
      status: "DISPENSED",
      allocated_subject_id: "SUBJ-101-0042",
      is_dispensation_blocked: false,
    },
    {
      id: "dev-3",
      study_id: "STUDY-DEV-101",
      site_id: "SITE-101",
      device_identifier: "DEV-00840987654321-SN-IVD-102",
      brand_name: "Rapid Viral IVD Assay",
      device_type: "DIAGNOSTIC_IVD",
      device_classification: "IVD_CLASS_B",
      udi_di: "00840987654321",
      lot_number: "LOT-IVD-05",
      serial_number: "SN-IVD-102",
      expiration_date: "2027-12-31",
      status: "AVAILABLE",
      is_dispensation_blocked: false,
    },
    {
      id: "dev-4",
      study_id: "STUDY-DEV-101",
      site_id: "SITE-101",
      device_identifier: "DEV-00840111222333-SN-OLD-01",
      brand_name: "BioResorbable Vascular Scaffold",
      device_type: "THERAPEUTIC",
      device_classification: "CLASS_IIB",
      udi_di: "00840111222333",
      lot_number: "LOT-OLD",
      serial_number: "SN-OLD-01",
      expiration_date: "2024-01-01",
      status: "QUARANTINED",
      is_dispensation_blocked: true,
    },
  ];

  quarantines.value = [
    {
      id: "quar-101",
      study_id: "STUDY-DEV-101",
      site_id: "SITE-101",
      lot_number: "LOT-OLD",
      serial_number: "SN-OLD-01",
      scope: "DEVICE_SERIAL",
      status: "ACTIVE",
      reason:
        "Automated quarantine: Device reached expiration date (2024-01-01)",
      quarantined_at: "2026-08-20T10:00:00Z",
      quarantined_by: "system.inventory_guard",
    },
  ];

  deficiencies.value = [
    {
      id: "defic-901",
      study_id: "STUDY-DEV-101",
      site_id: "SITE-101",
      device_name: "CardioPulse Active Pacemaker",
      udi_di: "00840123456789",
      lot_number: "LOT-99",
      serial_number: "SN-4411",
      deficiency_type: "MALFUNCTION",
      deficiency_description:
        "Telemetry sync failure during exercise stress testing; pacing rate stuck at 60 bpm.",
      incident_date: "2026-08-22T14:30:00Z",
      could_lead_to_sade: true,
      patient_involved: true,
      health_impact: "MILD_TRANSIENT",
      quarantine_scope: "DEVICE_SERIAL",
      safety_case_id: "DEV-CASE-STUDY-DEV-101-001",
      safety_classification: "SADE",
    },
  ];

  safetyCases.value = [
    {
      id: "sc-101",
      case_number: "DEV-CASE-STUDY-DEV-101-001",
      device_name: "CardioPulse Active Pacemaker",
      device_udi_di: "00840123456789",
      lot_number: "LOT-99",
      serial_number: "SN-4411",
      classification: "SADE",
      causality: "POSSIBLE",
      is_usade: false,
      vigilance_deadline_days: 10,
      vigilance_due_date: new Date(Date.now() + 8 * 86400000).toISOString(),
      days_remaining: 8,
      is_overdue: false,
      status: "OPEN",
    },
  ];

  updateMetrics();
});
</script>

<style scoped>
/* 100% Vanilla CSS Tokens - 0 Tailwind Classes */

.device-investigation-console {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 24px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-family: var(
    --semantic-font-family,
    system-ui,
    -apple-system,
    sans-serif
  );
  min-height: 100vh;
  box-sizing: border-box;
}

/* Header */
.console-header {
  display: flex;
  flex-direction: column;
  gap: 16px;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 20px;
}

.header-main {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
  flex-wrap: wrap;
}

.title-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.console-title {
  font-size: 1.6rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  margin: 0;
  letter-spacing: -0.02em;
}

.console-subtitle {
  font-size: 0.88rem;
  color: var(--semantic-color-text-muted);
  margin: 0;
  max-width: 820px;
  line-height: 1.4;
}

.header-study-control {
  display: flex;
  align-items: flex-end;
  gap: 12px;
  background-color: var(--semantic-color-surface-muted);
  padding: 10px 16px;
  border-radius: 8px;
  border: 1px solid var(--semantic-color-border);
}

.control-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.78rem;
  font-weight: 600;
  color: var(--semantic-color-text-muted);
}

/* Top KPI Metric Cards */
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.metric-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 14px 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  transition:
    transform 0.15s ease,
    box-shadow 0.15s ease;
}

.metric-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
}

.metric-alert-danger {
  border-color: rgba(239, 68, 68, 0.4);
  background-color: var(--semantic-color-error-bg-soft);
}

.metric-label {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  color: var(--semantic-color-text-muted);
  letter-spacing: 0.03em;
}

.metric-value {
  font-size: 1.8rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  line-height: 1.1;
}

.metric-sub {
  font-size: 0.76rem;
  color: var(--semantic-color-text-muted);
}

/* Navigation Tabs */
.nav-tabs {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid var(--semantic-color-border);
  overflow-x: auto;
}

.tab-button {
  background: none;
  border: none;
  padding: 10px 18px;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--semantic-color-text-muted);
  cursor: pointer;
  border-bottom: 3px solid transparent;
  margin-bottom: -2px;
  transition:
    background-color 0.2s ease,
    border-color 0.2s ease,
    color 0.2s ease,
    box-shadow 0.2s ease,
    opacity 0.2s ease;
  white-space: nowrap;
}

.tab-button:hover {
  color: var(--semantic-color-primary);
}

.tab-button.active {
  color: var(--semantic-color-primary);
  border-bottom-color: var(--semantic-color-primary);
}

/* Workspace Panes & Cards */
.main-content {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.tab-pane {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.pane-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 10px;
  padding: 20px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.03);
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.panel-title {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0;
}

.panel-subtitle {
  font-size: 0.8rem;
  color: var(--semantic-color-text-muted);
  margin: 2px 0 0 0;
}

.header-with-filters {
  display: flex;
  align-items: center;
  gap: 16px;
  flex-wrap: wrap;
}

.filter-pills {
  display: flex;
  gap: 6px;
}

.filter-pill {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  padding: 4px 10px;
  border-radius: 16px;
  font-size: 0.76rem;
  font-weight: 600;
  cursor: pointer;
}

.filter-pill.active {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-primary);
}

.filter-pill.filter-danger.active {
  background: var(--semantic-color-error);
  border-color: var(--semantic-color-error);
}

/* Scanner Grid */
.scanner-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

@media (max-width: 900px) {
  .scanner-grid {
    grid-template-columns: 1fr;
  }
}

.input-with-action {
  display: flex;
  gap: 8px;
  margin-top: 6px;
}

.sample-barcodes-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
  margin-top: 10px;
}

.sample-label {
  font-size: 0.74rem;
  font-weight: 700;
  color: var(--semantic-color-text-muted);
}

.btn-chip {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 0.72rem;
  cursor: pointer;
  transition: background 0.15s ease;
}

.btn-chip:hover {
  background: var(--semantic-color-surface-header);
}

.chip-expired {
  color: var(--semantic-color-error);
  border-color: var(--semantic-color-error-border);
  background: var(--semantic-color-error-bg-soft);
}

/* Parsed UDI Card */
.parsed-udi-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 12px;
}

.parsed-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.parsed-title {
  font-size: 0.85rem;
  font-weight: 700;
  text-transform: uppercase;
  color: var(--semantic-color-text-muted);
}

.parsed-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px 16px;
}

.parsed-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.p-label {
  font-size: 0.72rem;
  color: var(--semantic-color-text-muted);
}

.p-val {
  font-size: 0.84rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.p-val.highlight {
  color: var(--semantic-color-primary);
}

.parsed-actions {
  display: flex;
  justify-content: flex-end;
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 10px;
}

/* Tables */
.table-responsive {
  overflow-x: auto;
  margin-top: 8px;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.84rem;
  text-align: left;
}

.data-table th {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-muted);
  font-weight: 700;
  padding: 10px 12px;
  border-bottom: 2px solid var(--semantic-color-border);
  white-space: nowrap;
}

.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--semantic-color-border);
  vertical-align: middle;
}

.data-table tr:hover {
  background-color: var(--semantic-color-surface-muted);
}

.row-quarantined {
  background-color: var(--semantic-color-error-bg-row);
}

.row-quarantined:hover {
  background-color: var(--semantic-color-rose-border);
}

.brand-cell {
  display: flex;
  flex-direction: column;
}

.sub-text {
  font-size: 0.74rem;
  color: var(--semantic-color-text-muted);
}

.serial-tag {
  display: block;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.action-buttons-cell {
  display: flex;
  gap: 6px;
}

.empty-cell {
  text-align: center;
  padding: 24px;
  color: var(--semantic-color-text-muted);
}

/* Forms */
.deficiency-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-grid-3 {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 16px;
}

.form-grid-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

@media (max-width: 768px) {
  .form-grid-2 {
    grid-template-columns: 1fr;
  }
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.text-input,
.select-input,
.textarea-input {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.88rem;
  font-family: inherit;
  color: inherit;
}

.text-input-sm {
  padding: 4px 8px;
  font-size: 0.82rem;
}

.text-input:focus,
.select-input:focus,
.textarea-input:focus {
  outline: none;
  border-color: var(--semantic-color-primary);
  box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.2);
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  cursor: pointer;
}

/* Triage Box */
.triage-box {
  background-color: var(--semantic-color-orange-bg);
  border: 1px solid var(--semantic-color-orange-bg);
  border-left: 4px solid var(--semantic-color-orange-text);
  border-radius: 6px;
  padding: 14px 18px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.triage-title {
  font-size: 0.92rem;
  font-weight: 700;
  color: var(--semantic-color-orange-text);
  margin: 0;
}

.triage-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.signature-box {
  background-color: var(--semantic-color-surface-muted);
  border: 1px dashed var(--semantic-color-border);
  padding: 12px;
  border-radius: 6px;
}

.form-actions-bar {
  display: flex;
  justify-content: flex-end;
  padding-top: 10px;
}

/* Safety Cases Grid */
.safety-cases-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
}

.safety-case-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  gap: 14px;
  box-shadow: 0 2px 4px rgba(0, 0, 0, 0.03);
}

.card-sade-critical {
  border-left: 4px solid var(--semantic-color-error);
}

.sc-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 8px;
}

.sc-title-area {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.case-number {
  font-size: 0.95rem;
  font-weight: 700;
}

.countdown-badge {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  padding: 4px 8px;
  border-radius: 6px;
  font-size: 0.74rem;
  font-weight: 700;
  color: var(--semantic-color-text-body);
  display: flex;
  align-items: center;
  gap: 4px;
}

.countdown-badge.overdue {
  background: var(--semantic-color-error-bg-soft);
  border-color: var(--semantic-color-error-border);
  color: var(--semantic-color-error);
}

.sc-details {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.82rem;
}

.sc-detail-row {
  display: flex;
  justify-content: space-between;
}

.sc-k {
  color: var(--semantic-color-text-muted);
}

.causality-tag {
  font-weight: 700;
  color: var(--semantic-color-primary);
}

.sc-footer {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  border-top: 1px solid var(--semantic-color-surface-header);
  padding-top: 10px;
}

/* Exports View */
.export-columns-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

@media (max-width: 800px) {
  .export-columns-grid {
    grid-template-columns: 1fr;
  }
}

.export-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 14px;
}

.box-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.box-title {
  font-size: 0.88rem;
  font-weight: 700;
  margin: 0;
}

.dataset-preview,
.xml-preview {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-accent-inverse);
  padding: 12px;
  border-radius: 6px;
  max-height: 240px;
  overflow-y: auto;
  font-size: 0.76rem;
}

.dataset-preview pre,
.xml-preview pre {
  margin: 0;
  white-space: pre-wrap;
  word-break: break-all;
}

/* Badges & Status Pills */
.badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
}

.badge-regulatory {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-info-border-accent);
}

.badge-audit {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
  border: 1px solid var(--semantic-color-warning-border);
}

.badge-danger {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.badge-warning {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.badge-neutral {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.badge-class {
  background: var(--semantic-color-ai-bg);
  color: var(--semantic-color-ai-dark);
}

.badge-defic {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.badge-scope {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.status-pill {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 12px;
  font-size: 0.72rem;
  font-weight: 700;
}

.status-pill.success {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.status-pill.danger {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.status-pill.warning {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.status-pill.info {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.status-pill.neutral {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.code-font {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

.cell-bold {
  font-weight: 600;
}

.text-danger {
  color: var(--semantic-color-error);
}

.text-warning {
  color: var(--semantic-color-warning);
}

.text-success {
  color: var(--semantic-color-success);
}

.text-muted {
  color: var(--semantic-color-text-muted);
}

/* Buttons */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 8px 16px;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease,
    box-shadow 0.15s ease,
    opacity 0.15s ease;
  font-family: inherit;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-sm {
  padding: 4px 10px;
  font-size: 0.78rem;
}

.btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background-color: var(--semantic-color-primary);
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border-color: var(--semantic-color-border);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-muted);
}

.btn-danger {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.btn-danger:hover:not(:disabled) {
  background-color: var(--semantic-color-error);
}

.btn-outline-danger {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-error);
  border-color: var(--semantic-color-error-border);
}

.btn-outline-danger:hover:not(:disabled) {
  background-color: var(--semantic-color-error-bg-soft);
}

/* Modal */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(2px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 20px;
}

.modal-card {
  background-color: var(--semantic-color-surface);
  border-radius: 10px;
  padding: 24px;
  max-width: 560px;
  width: 100%;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.modal-title {
  font-size: 1.2rem;
  font-weight: 700;
  margin: 0;
}

.modal-subtitle {
  font-size: 0.84rem;
  color: var(--semantic-color-text-muted);
  margin: 0;
}

.modal-actions {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 10px;
}

.alert-danger-box {
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
  padding: 10px 14px;
  border-radius: 6px;
  font-size: 0.82rem;
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}
</style>
