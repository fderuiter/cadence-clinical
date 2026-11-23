<template>
  <div class="cgt-coc-console">
    <!-- Header -->
    <header class="console-header">
      <div class="header-left">
        <div class="header-badge-row">
          <span class="badge badge-cgt">Cell &amp; Gene Therapy</span>
          <span class="badge badge-gxp">21 CFR Part 11 Regulated</span>
          <span class="badge badge-isbt">ISBT 128 Compliant</span>
          <span
            v-if="cocData?.status === 'BEDSIDE_VERIFIED'"
            class="badge badge-unlocked"
            >Infusion Unlocked</span
          >
          <span
            v-else-if="cocData?.status === 'QUARANTINED'"
            class="badge badge-quarantine"
            >Cryo Quarantined</span
          >
          <span v-else class="badge badge-stage">{{
            cocData?.current_stage || "Awaiting Harvest"
          }}</span>
        </div>
        <h2 class="title">
          Vein-to-Vein Chain of Custody &amp; Identity (COC/COI)
        </h2>
        <p class="subtitle">
          Unbroken cryptographic identity binding, cryogenic transit temperature
          logging, and mandatory two-person bedside verification.
        </p>
      </div>

      <div class="header-actions">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="fetchTraceability"
        >
          Refresh Ledger
        </button>
        <button
          type="button"
          class="btn btn-primary"
          @click="openApheresisModal"
        >
          + Apheresis Harvest
        </button>
      </div>
    </header>

    <!-- Search & Filter Bar -->
    <section class="filter-panel card">
      <div class="filter-group">
        <label for="input-study">Study Protocol</label>
        <input
          id="input-study"
          v-model="filters.studyId"
          type="text"
          placeholder="e.g. STUDY-CGT-801"
          @change="fetchTraceability"
        />
      </div>
      <div class="filter-group">
        <label for="input-subject">{{ formatLabel("subject_id") }}</label>
        <input
          id="input-subject"
          v-model="filters.subjectId"
          type="text"
          placeholder="e.g. SUBJ-101-0042"
          @change="fetchTraceability"
        />
      </div>
      <div class="filter-group action-btn-group">
        <button
          type="button"
          class="btn btn-action"
          :disabled="!filters.studyId || !filters.subjectId"
          @click="fetchTraceability"
        >
          Load COC/COI
        </button>
      </div>
    </section>

    <!-- Main COC/COI Overview -->
    <section
      v-if="cocData"
      class="console-body"
      aria-label="Chain of custody overview"
    >
      <!-- Top Metrics / Cards -->
      <section class="overview-grid">
        <div class="card metric-card">
          <span class="metric-label">Donation ID (ISBT 128 DIN)</span>
          <span class="metric-value code-font">{{ cocData.din }}</span>
          <span class="metric-sub"
            >{{ formatDatabaseKey("collection_site_id") }}:
            {{ formatFacilityCode(cocData.collection_site_id) }}</span
          >
        </div>

        <div class="card metric-card">
          <span class="metric-label">Subject Identity Binding</span>
          <span class="metric-value">{{ cocData.subject_id }}</span>
          <span class="metric-sub">Study: {{ cocData.study_id }}</span>
        </div>

        <div class="card metric-card">
          <span class="metric-label">Manufacturing Batch</span>
          <span class="metric-value highlight">{{
            cocData.batch_id || "Pending Synthesis"
          }}</span>
          <span class="metric-sub"
            >{{ formatDatabaseKey("manufacturing_facility_id") }}:
            {{
              formatFacilityCode(cocData.manufacturing_facility_id) ||
              "In Queue"
            }}</span
          >
        </div>

        <div
          class="card metric-card"
          :class="{ 'card-excursion': cocData.cryo_temp_excursion_count > 0 }"
        >
          <span class="metric-label">Cryogenic Integrity</span>
          <span class="metric-value">
            {{
              cocData.lowest_temp_celsius != null
                ? `${cocData.lowest_temp_celsius.toFixed(1)}°C`
                : "-196.0°C"
            }}
          </span>
          <span
            class="metric-sub"
            :class="
              cocData.cryo_temp_excursion_count > 0
                ? 'text-danger'
                : 'text-success'
            "
          >
            {{
              cocData.cryo_temp_excursion_count === 0
                ? "Optimal (0 Excursions)"
                : `${cocData.cryo_temp_excursion_count} Excursions`
            }}
          </span>
        </div>
      </section>

      <!-- Stage Visualizer Ribbon -->
      <section class="card lifecycle-ribbon">
        <h3 class="section-title">Vein-to-Vein Continuum Lifecycle</h3>
        <div class="stages-track">
          <div
            v-for="(st, idx) in stagesList"
            :key="st.key"
            class="stage-node"
            :class="{
              'stage-complete': isStageComplete(st.key),
              'stage-current': cocData.current_stage === st.key,
              'stage-pending':
                !isStageComplete(st.key) && cocData.current_stage !== st.key,
            }"
          >
            <div class="node-circle">
              <span class="node-idx">{{ idx + 1 }}</span>
            </div>
            <span class="node-label">{{ st.label }}</span>
          </div>
        </div>
      </section>

      <!-- Action Toolbar -->
      <section class="card action-toolbar">
        <div class="toolbar-title-block">
          <h4 class="toolbar-title">
            Chain of Custody Handover &amp; Verification Actions
          </h4>
          <p class="toolbar-hint">
            Execute Part 11 e-signature step-up operations across the chain.
          </p>
        </div>
        <div class="toolbar-buttons">
          <button
            type="button"
            class="btn btn-outline"
            @click="openMilestoneModal"
          >
            + Log Transit Milestone
          </button>
          <button
            type="button"
            class="btn btn-outline"
            :disabled="!cocData || cocData.batch_id != null"
            @click="openManufacturingModal"
          >
            Bind Manufacturing Batch
          </button>
          <button
            type="button"
            class="btn btn-stepup"
            :disabled="
              cocData.status === 'BEDSIDE_VERIFIED' ||
              cocData.status === 'INFUSED'
            "
            @click="openBedsideModal"
          >
            Bedside Dual-Verification
          </button>
          <button
            type="button"
            class="btn btn-infuse"
            :disabled="
              !cocData.infusion_unlocked || cocData.status === 'INFUSED'
            "
            @click="openInfusionModal"
          >
            Administer Infusion
          </button>
          <button
            type="button"
            class="btn btn-danger-outline"
            @click="openDestructionModal"
          >
            Generate Cert of Destruction
          </button>
        </div>
      </section>

      <!-- Cryptographic Barcode Verification Box -->
      <section class="card barcode-verification-card">
        <div class="barcode-info-col">
          <h3 class="section-title">Tamper-Evident Cryptographic Barcode</h3>
          <p class="barcode-meta">
            HMAC-SHA256 signed 2D DataMatrix payload dynamically binding Subject
            Identifier, ISBT 128 DIN, and Manufacturing Batch.
          </p>
          <div class="hash-display">
            <span class="hash-label">Cryptographic Seal:</span>
            <span class="hash-code">{{
              cocData.barcode_signature || "Not Generated"
            }}</span>
          </div>
          <div class="raw-payload-box">
            <span class="raw-label">Encoded 2D Barcode Payload:</span>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <textarea
              readonly
              class="payload-textarea"
              :value="cocData.tamper_evident_barcode || ''"
              rows="3"
            />
          </div>
        </div>
        <div class="barcode-scan-simulator">
          <h4 class="scan-title">Direct Scanner Verification</h4>
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <input
            v-model="scanTestInput"
            type="text"
            placeholder="Scan / paste barcode payload here..."
            class="scan-input"
          />
          <button
            type="button"
            class="btn btn-secondary btn-block"
            :disabled="!scanTestInput"
            @click="testScanBarcode"
          >
            Validate Barcode Cryptography
          </button>
          <div
            v-if="scanResult"
            class="scan-result"
            :class="scanResult.is_valid ? 'scan-valid' : 'scan-invalid'"
          >
            <span class="result-badge">{{
              scanResult.is_valid ? "VALID SEAL" : "TAMPER DETECTED"
            }}</span>
            <span class="result-detail">{{
              scanResult.failure_reason ||
              "Cryptographic seal and identity bindings match."
            }}</span>
          </div>
        </div>
      </section>

      <!-- Milestone Ledger Table -->
      <section class="card milestone-ledger-card">
        <h3 class="section-title">
          Immutable Vein-to-Vein Audit Ledger ({{ milestones.length }}
          Milestones)
        </h3>
        <div class="table-responsive">
          <table class="table">
            <thead>
              <tr>
                <th>Timestamp (UTC)</th>
                <th>Stage</th>
                <th>Location / Waypoint</th>
                <th>GPS Coordinates</th>
                <th>Cryo Temp</th>
                <th>Custodian</th>
                <th>Tamper Seal</th>
                <th>E-Signature</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="m in milestones" :key="m.id">
                <td class="code-font">
                  {{ formatDateTime(m.timestamp) }}
                </td>
                <td>
                  <span class="badge badge-sm">{{ m.stage }}</span>
                </td>
                <td>{{ m.location_name }}</td>
                <td class="code-font">
                  {{
                    m.latitude != null && m.longitude != null
                      ? `${m.latitude.toFixed(4)}, ${m.longitude.toFixed(4)}`
                      : "N/A"
                  }}
                </td>
                <td
                  :class="
                    m.temperature_status === 'EXCURSION'
                      ? 'text-danger font-bold'
                      : 'text-success'
                  "
                >
                  {{ m.temperature_celsius.toFixed(1) }}°C
                </td>
                <td>{{ m.custodian_name }} ({{ m.custodian_role }})</td>
                <td>
                  <span
                    :class="
                      m.seal_intact ? 'status-pill-ok' : 'status-pill-breach'
                    "
                  >
                    {{ m.seal_intact ? m.seal_id || "Intact" : "BREACH" }}
                  </span>
                </td>
                <td>
                  <span
                    v-if="m.electronic_signature"
                    class="sig-pill"
                    title="Signed via 21 CFR Part 11"
                  >
                    Verified
                  </span>
                  <span v-else class="text-muted">-</span>
                </td>
              </tr>
              <tr v-if="milestones.length === 0">
                <td colspan="8" class="text-center text-muted">
                  No milestone logs recorded yet.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </section>

    <!-- Empty State -->
    <div v-else class="card empty-state">
      <p class="empty-text">
        Enter a valid Study Protocol and Subject Identifier above to load the
        Vein-to-Vein Chain of Custody ledger.
      </p>
    </div>

    <!-- Modals -->

    <!-- 1. Apheresis Modal -->
    <ClinicalModal
      v-model="showApheresisModal"
      :teleport="false"
      :show-footer="false"
      backdrop-class="modal-overlay"
      dialog-class="modal-card"
      title="Register Apheresis Cellular Harvest"
    >
      <form @submit.prevent="submitApheresis">
        <div class="form-grid">
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Study Protocol</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="apheresisForm.study_id" type="text" required />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>{{ formatDatabaseKey("site_id") }}</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="apheresisForm.site_id" type="text" required />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>{{ formatDatabaseKey("subject_id") }}</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="apheresisForm.subject_id" type="text" required />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Donation ID (ISBT 128 DIN)</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="apheresisForm.din"
              type="text"
              placeholder="Auto-generated if empty"
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Collector User ID</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="apheresisForm.collector_id" type="text" required />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Collector Name</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="apheresisForm.collector_name"
              type="text"
              required
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Initial Cryo Temp (°C)</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model.number="apheresisForm.initial_temp_celsius"
              type="number"
              step="0.1"
              required
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Dry Shipper / Dewar ID</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="apheresisForm.dry_shipper_id"
              type="text"
              placeholder="e.g. MVE-LN2-9901"
            />
          </div>
        </div>
        <footer class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showApheresisModal = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">
            Initialize Chain of Custody
          </button>
        </footer>
      </form>
    </ClinicalModal>

    <!-- 2. Bedside Dual Verification Modal -->
    <ClinicalModal
      v-model="showBedsideModal"
      :teleport="false"
      :show-footer="false"
      size="lg"
      backdrop-class="modal-overlay"
      dialog-class="modal-card modal-lg"
      title="Bedside Two-Person Electronic Verification (Infusion Gating)"
    >
      <form @submit.prevent="submitBedsideVerify">
        <div class="verification-instructions alert alert-info">
          <strong>Mandatory GxP Safety Gate:</strong> Both the Bedside Nurse and
          Principal Investigator must independently verify the patient wristband
          scan against the cell bag cryptographic barcode.
        </div>

        <div class="form-grid">
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Scanned Patient Wristband Subject Identifier</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="bedsideForm.scanned_wristband_subject_id"
              type="text"
              required
              placeholder="Scan patient wristband barcode..."
            />
          </div>
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Scanned Cell Infusion Bag Cryptographic Barcode</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <textarea
              v-model="bedsideForm.scanned_bag_barcode"
              rows="2"
              required
              placeholder="Scan infusion bag DataMatrix..."
            />
          </div>

          <!-- Primary Verifier (Nurse) -->
          <div class="verifier-panel col-half">
            <h4 class="verifier-title">Primary Verifier (Bedside Nurse)</h4>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>User ID</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="bedsideForm.primary_verifier_id"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Full Name</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="bedsideForm.primary_verifier_name"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>E-Signature Step-Up Token</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="bedsideForm.primary_signature_token"
                type="password"
                required
              />
            </div>
          </div>

          <!-- Secondary Verifier (PI) -->
          <div class="verifier-panel col-half">
            <h4 class="verifier-title">
              Secondary Verifier (Principal Investigator)
            </h4>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>User ID</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="bedsideForm.secondary_verifier_id"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Full Name</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="bedsideForm.secondary_verifier_name"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>E-Signature Step-Up Token</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="bedsideForm.secondary_signature_token"
                type="password"
                required
              />
            </div>
          </div>
        </div>

        <footer class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showBedsideModal = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-stepup">
            Execute Dual Electronic Signature
          </button>
        </footer>
      </form>
    </ClinicalModal>

    <!-- 3. Certificate of Destruction Modal -->
    <ClinicalModal
      v-model="showDestructionModal"
      :teleport="false"
      :show-footer="false"
      size="lg"
      backdrop-class="modal-overlay"
      dialog-class="modal-card modal-lg"
      title="Generate Formal Certificate of Destruction"
    >
      <form @submit.prevent="submitDestruction">
        <div class="form-grid">
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Study Protocol</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="destructionForm.study_id" type="text" required />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Site ID</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="destructionForm.site_id" type="text" required />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Lot Number</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="destructionForm.lot_number" type="text" required />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Kit Numbers (Comma-separated)</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="destructionForm.kit_numbers_str"
              type="text"
              required
              placeholder="KIT-001, KIT-002"
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Vial Identifiers (Comma-separated)</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="destructionForm.vial_identifiers_str"
              type="text"
              required
              placeholder="VIAL-A1, VIAL-A2"
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Destruction Method</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select v-model="destructionForm.destruction_method" required>
              <option value="HIGH_TEMPERATURE_INCINERATION">
                High Temperature Incineration
              </option>
              <option value="AUTOCLAVE_BIOHAZARD">
                Autoclave Biohazard Decontamination
              </option>
              <option value="CHEMICAL_INACTIVATION">
                Chemical Inactivation
              </option>
              <option value="HAZARDOUS_WASTE_FACILITY">
                Hazardous Waste Facility
              </option>
            </select>
          </div>
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Destruction Facility</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="destructionForm.destruction_facility"
              type="text"
              required
              placeholder="e.g. Bio-Hazard Waste Center Alpha"
            />
          </div>
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Destruction Reason</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="destructionForm.destruction_reason"
              type="text"
              required
              placeholder="e.g. Residual post-infusion cellular product destruction per protocol."
            />
          </div>

          <!-- Witness 1 -->
          <div class="verifier-panel col-half">
            <h4 class="verifier-title">Witness 1 (Site Pharmacist / CRC)</h4>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>User ID</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="destructionForm.witness_1_id"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Full Name</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="destructionForm.witness_1_name"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>E-Signature Token</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="destructionForm.witness_1_signature_token"
                type="password"
                required
              />
            </div>
          </div>

          <!-- Witness 2 -->
          <div class="verifier-panel col-half">
            <h4 class="verifier-title">
              Witness 2 (Principal Investigator / QA)
            </h4>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>User ID</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="destructionForm.witness_2_id"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Full Name</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="destructionForm.witness_2_name"
                type="text"
                required
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>E-Signature Token</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="destructionForm.witness_2_signature_token"
                type="password"
                required
              />
            </div>
          </div>
        </div>

        <footer class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showDestructionModal = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-danger">
            Issue Dual-Witness Certificate
          </button>
        </footer>
      </form>
    </ClinicalModal>

    <!-- 4. Transit Milestone & Telemetry Upload Modal -->
    <ClinicalModal
      v-model="showMilestoneModal"
      :teleport="false"
      :show-footer="false"
      size="xl"
      backdrop-class="modal-overlay"
      dialog-class="modal-card modal-xl"
      title="Log Transit Milestone & Telemetry Upload"
    >
      <template #header="{ titleId }">
        <div>
          <h3 :id="titleId">Log Transit Milestone &amp; Telemetry Upload</h3>
          <p class="modal-subtitle">
            Upload Sensitech or TempTale logger file and review cryogenic
            excursion details.
          </p>
        </div>
      </template>

      <div class="milestone-modal-content">
        <!-- File Dropzone Section -->
        <div class="dropzone-section">
          <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
          <label class="section-label"
            >Logger Telemetry Upload (Sensitech / TempTale CSV or JSON)</label
          >
          <ClinicalDropzone
            class="dropzone-box"
            :class="{ 'dropzone-has-file': parsedTelemetry !== null }"
            accept=".csv,.json,.txt"
            :disabled="isParsing"
            :has-selection="isParsing || parsedTelemetry !== null"
            :prompt-text="
              !isParsing && !parsedTelemetry
                ? 'Drag and drop Sensitech or TempTale telemetry export file here'
                : ''
            "
            :hint-text="
              !isParsing && !parsedTelemetry
                ? 'Supports cryogenic temperature validation (-196.0°C to -70.0°C safe range)'
                : ''
            "
            :button-label="
              parsedTelemetry ? 'Replace File' : 'Upload Logger File'
            "
            input-aria-label="Upload logger file"
            removable
            remove-label="Remove File"
            :remove-context="parsedTelemetry ? parsedTelemetry.fileName : ''"
            @file-selected="handleFileUpload"
            @remove="
              parsedTelemetry = null;
              parsingError = null;
            "
          >
            <template #selected>
              <div v-if="isParsing" class="dropzone-status">
                <p>
                  <strong
                    >Parsing time-series telemetry data off main
                    thread...</strong
                  >
                </p>
              </div>
              <div
                v-else-if="parsedTelemetry"
                class="dropzone-status text-success"
              >
                <p>
                  <strong>Loaded: {{ parsedTelemetry.fileName }}</strong>
                  ({{ parsedTelemetry.totalSamples }} readings, Vendor:
                  {{ parsedTelemetry.vendor }})
                </p>
              </div>
            </template>
          </ClinicalDropzone>

          <div v-if="parsingError" class="alert alert-danger" role="alert">
            <strong>Parsing Error:</strong> {{ parsingError }}
          </div>
        </div>

        <!-- Excursion Preview Drawer Card -->
        <section v-if="parsedTelemetry" class="excursion-drawer card">
          <div class="drawer-header">
            <div class="drawer-title-group">
              <h4 class="drawer-title">Excursion Preview Drawer</h4>
              <span class="badge badge-stage">
                {{
                  parsedTelemetry.isDownsampled
                    ? `Downsampled Keypoints (${parsedTelemetry.keypoints.length} / ${parsedTelemetry.totalSamples})`
                    : `Full Resolution (${parsedTelemetry.totalSamples} readings)`
                }}
              </span>
            </div>
            <span
              class="badge"
              :class="
                parsedTelemetry.excursionCount > 0
                  ? 'badge-quarantine'
                  : 'badge-unlocked'
              "
            >
              {{
                parsedTelemetry.excursionCount > 0
                  ? `EXCURSION (${parsedTelemetry.excursionCount} Readings Out of Range)`
                  : "OPTIMAL (0 Excursions)"
              }}
            </span>
          </div>

          <!-- Drawer Key Statistics -->
          <div class="drawer-stats-grid">
            <div class="drawer-stat">
              <span class="stat-label">Min Temperature</span>
              <span
                class="stat-value code-font"
                :class="
                  parsedTelemetry.minTemperature < -196.0
                    ? 'text-danger'
                    : 'text-success'
                "
              >
                {{ parsedTelemetry.minTemperature.toFixed(1) }}°C
              </span>
            </div>
            <div class="drawer-stat">
              <span class="stat-label">Max Temperature</span>
              <span
                class="stat-value code-font"
                :class="
                  parsedTelemetry.maxTemperature > -70.0
                    ? 'text-danger'
                    : 'text-success'
                "
              >
                {{ parsedTelemetry.maxTemperature.toFixed(1) }}°C
              </span>
            </div>
            <div class="drawer-stat">
              <span class="stat-label">Total Sample Count</span>
              <span class="stat-value code-font">{{
                parsedTelemetry.totalSamples.toLocaleString()
              }}</span>
            </div>
            <div class="drawer-stat">
              <span class="stat-label">Excursion Reading Points</span>
              <span
                class="stat-value code-font"
                :class="
                  parsedTelemetry.excursionCount > 0
                    ? 'text-danger font-bold'
                    : 'text-success'
                "
              >
                {{ parsedTelemetry.excursionCount }}
              </span>
            </div>
          </div>

          <!-- Excursion Range Events List -->
          <div
            v-if="parsedTelemetry.excursionEvents.length > 0"
            class="excursion-events-box"
          >
            <h5 class="sub-title text-danger">Detected Excursion Events</h5>
            <div class="table-responsive">
              <table class="table table-sm">
                <thead>
                  <tr>
                    <th>Event ID</th>
                    <th>Excursion Type</th>
                    <th>Start Time (UTC)</th>
                    <th>End Time (UTC)</th>
                    <th>Duration</th>
                    <th>Peak Temp</th>
                    <th>Readings</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="evt in parsedTelemetry.excursionEvents"
                    :key="evt.id"
                    class="row-excursion-event"
                  >
                    <td class="code-font font-bold">
                      {{ evt.id }}
                    </td>
                    <td>
                      <span class="badge badge-quarantine">
                        {{
                          evt.type === "WARM_EXCURSION"
                            ? "WARM EXCURSION (> -70.0°C)"
                            : "COLD EXCURSION (< -196.0°C)"
                        }}
                      </span>
                    </td>
                    <td class="code-font">
                      {{ formatDateTime(evt.startTime) }}
                    </td>
                    <td class="code-font">
                      {{ formatDateTime(evt.endTime) }}
                    </td>
                    <td class="code-font font-bold">
                      {{ evt.durationFormatted }}
                    </td>
                    <td class="code-font font-bold text-danger">
                      {{ evt.peakTemperature.toFixed(1) }}°C
                    </td>
                    <td>{{ evt.sampleCount }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- Time-Series Keypoint Samples Preview Table -->
          <div class="telemetry-samples-box">
            <h5 class="sub-title">Time-Series Keypoint Readings</h5>
            <div class="table-responsive preview-scroll">
              <table class="table table-xs">
                <thead>
                  <tr>
                    <th>Timestamp (UTC)</th>
                    <th>Temperature (°C)</th>
                    <th>Thermal Zone</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="(reading, idx) in parsedTelemetry.sampleReadings"
                    :key="idx"
                    :class="{
                      'row-excursion-highlight': reading.isExcursion,
                    }"
                  >
                    <td class="code-font">
                      {{ formatDateTime(reading.timestamp) }}
                    </td>
                    <td
                      class="code-font"
                      :class="
                        reading.isExcursion
                          ? 'text-danger font-bold'
                          : 'text-success'
                      "
                    >
                      {{ reading.temperatureCelsius.toFixed(1) }}°C
                    </td>
                    <td>
                      <span
                        :class="
                          reading.isExcursion
                            ? 'badge badge-quarantine'
                            : 'badge badge-unlocked'
                        "
                      >
                        {{ reading.isExcursion ? "EXCURSION" : "OPTIMAL" }}
                      </span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </section>

        <!-- Milestone Form -->
        <form class="milestone-form" @submit.prevent="submitMilestone">
          <div class="form-grid">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Transit Stage</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <select v-model="milestoneForm.stage" required>
                <option value="APHERESIS_COLLECTION">
                  1. Apheresis Collection
                </option>
                <option value="CRYOPRESERVATION">2. Cryopreservation</option>
                <option value="COURIER_TRANSIT_OUTBOUND">
                  3. Outbound Courier Transit
                </option>
                <option value="MANUFACTURING_PROCESSING">
                  4. Manufacturing Processing
                </option>
                <option value="COURIER_TRANSIT_RETURN">
                  5. Return Courier Transit
                </option>
                <option value="SITE_RECEIPT_AND_STORAGE">
                  6. Site Storage
                </option>
              </select>
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Milestone Event Type</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <select v-model="milestoneForm.milestone_type" required>
                <option value="TRANSIT_CHECKPOINT">Transit Checkpoint</option>
                <option value="FACILITY_HANDOVER">Facility Handover</option>
                <option value="TEMPERATURE_AUDIT">Temperature Audit</option>
                <option value="DEWAR_RECEIPT">Dewar Receipt</option>
              </select>
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Location / Waypoint</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="milestoneForm.location_name"
                type="text"
                required
                placeholder="e.g. Frankfurt Cryo Hub"
              />
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Recorded Checkpoint Temp (°C)</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model.number="milestoneForm.temperature_celsius"
                type="number"
                step="0.1"
                required
              />
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Custodian User ID</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="milestoneForm.custodian_id"
                type="text"
                required
              />
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Custodian Full Name</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="milestoneForm.custodian_name"
                type="text"
                required
              />
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Courier / Carrier</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="milestoneForm.carrier"
                type="text"
                placeholder="e.g. WorldCourier Cryo"
              />
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Tracking Number</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="milestoneForm.tracking_number"
                type="text"
                placeholder="e.g. WC-TRACK-01"
              />
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Dry Shipper / Dewar ID</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="milestoneForm.dry_shipper_id"
                type="text"
                placeholder="e.g. DEWAR-LN2-9901"
              />
            </div>

            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Tamper Seal ID</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="milestoneForm.seal_id"
                type="text"
                placeholder="e.g. SEAL-4482"
              />
            </div>

            <div class="form-group col-full">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>
                GxP Justification Notes &amp; Observations
                <span
                  v-if="parsedTelemetry && parsedTelemetry.excursionCount > 0"
                  class="text-danger"
                  >* (Required for Excursion)</span
                >
              </label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="milestoneForm.notes"
                rows="3"
                :required="
                  parsedTelemetry != null && parsedTelemetry.excursionCount > 0
                "
                placeholder="Enter GxP justification notes, courier comments, or excursion investigation references..."
              />
            </div>
          </div>

          <footer class="modal-footer">
            <button
              type="button"
              class="btn btn-secondary"
              @click="showMilestoneModal = false"
            >
              Cancel
            </button>
            <button type="submit" class="btn btn-primary">
              Log Milestone &amp; Update Ledger
            </button>
          </footer>
        </form>
      </div>
    </ClinicalModal>

    <!-- 5. Bind Manufacturing Batch Modal -->
    <ClinicalModal
      v-model="showManufacturingModal"
      :teleport="false"
      :show-footer="false"
      backdrop-class="modal-overlay"
      dialog-class="modal-card"
      title="Bind Manufacturing Batch"
    >
      <form @submit.prevent="submitManufacturingBind">
        <div class="form-grid">
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Manufacturing Batch ID</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input v-model="manufacturingForm.batch_id" type="text" required />
          </div>
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Manufacturing Facility ID</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="manufacturingForm.manufacturing_facility_id"
              type="text"
              required
            />
          </div>
        </div>
        <footer class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showManufacturingModal = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">
            Bind Batch to Chain of Custody
          </button>
        </footer>
      </form>
    </ClinicalModal>

    <!-- 6. Administer Infusion Modal -->
    <ClinicalModal
      v-model="showInfusionModal"
      :teleport="false"
      :show-footer="false"
      backdrop-class="modal-overlay"
      dialog-class="modal-card"
      title="Administer Cellular Infusion"
    >
      <form @submit.prevent="submitInfusion">
        <div class="form-grid">
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Administrator ID</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="infusionForm.administrator_id"
              type="text"
              required
            />
          </div>
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Administrator Name</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="infusionForm.administrator_name"
              type="text"
              required
            />
          </div>
          <div class="form-group col-full">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>E-Signature Step-Up Token</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="infusionForm.signature_token"
              type="password"
              required
            />
          </div>
        </div>
        <footer class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showInfusionModal = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-infuse">
            Confirm Infusion Administration
          </button>
        </footer>
      </form>
    </ClinicalModal>
  </div>
</template>

<script setup lang="ts">
import { ClinicalDropzone, ClinicalModal } from "ui";
import { ref, reactive, onMounted } from "vue";
import { useDomainFormatter } from "@/composables/useDomainFormatter";
import {
  parseCryoLoggerFile,
  type ParsedCryoLoggerResult,
} from "@/utils/cryoLoggerParser";

const { formatLabel, formatDatabaseKey, formatFacilityCode } =
  useDomainFormatter();

interface CGTChainOfCustody {
  id: string;
  study_id: string;
  subject_id: string;
  din: string;
  batch_id: string | null;
  current_stage: string;
  status: string;
  tamper_evident_barcode: string | null;
  barcode_signature: string | null;
  collection_site_id: string | null;
  collection_timestamp: string | null;
  manufacturing_facility_id: string | null;
  cryo_temp_excursion_count: number;
  lowest_temp_celsius: number | null;
  highest_temp_celsius: number | null;
  infusion_unlocked: boolean;
  infused_at: string | null;
  reconciled_at: string | null;
}

interface CGTMilestone {
  id: string;
  study_id: string;
  chain_of_custody_id: string;
  stage: string;
  milestone_type: string;
  timestamp: string;
  location_name: string;
  facility_id: string | null;
  latitude: number | null;
  longitude: number | null;
  temperature_celsius: number;
  temperature_status: string;
  custodian_id: string;
  custodian_name: string;
  custodian_role: string;
  carrier: string | null;
  tracking_number: string | null;
  seal_intact: boolean;
  seal_id: string | null;
  dry_shipper_id: string | null;
  electronic_signature: Record<string, any> | null;
  notes: string | null;
}

const stagesList = [
  { key: "APHERESIS_COLLECTION", label: "1. Apheresis" },
  { key: "CRYOPRESERVATION", label: "2. Cryopreservation" },
  { key: "COURIER_TRANSIT_OUTBOUND", label: "3. Outbound Transit" },
  { key: "MANUFACTURING_PROCESSING", label: "4. Manufacturing" },
  { key: "COURIER_TRANSIT_RETURN", label: "5. Return Transit" },
  { key: "SITE_RECEIPT_AND_STORAGE", label: "6. Site Storage" },
  { key: "BEDSIDE_VERIFICATION", label: "7. Bedside Verify" },
  { key: "INFUSION_ADMINISTRATION", label: "8. Infusion" },
  { key: "DESTRUCTION", label: "9. Destruction" },
];

const filters = reactive({
  study_id: "STUDY-CGT-801",
  studyId: "STUDY-CGT-801",
  subjectId: "SUBJ-101-0042",
});

const cocData = ref<CGTChainOfCustody | null>(null);
const milestones = ref<CGTMilestone[]>([]);
const isLoading = ref(false);

const scanTestInput = ref("");
const scanResult = ref<{ is_valid: boolean; failure_reason?: string } | null>(
  null
);

// Modal visibility flags
const showApheresisModal = ref(false);
const showBedsideModal = ref(false);
const showDestructionModal = ref(false);
const showMilestoneModal = ref(false);
const showManufacturingModal = ref(false);
const showInfusionModal = ref(false);

// Telemetry File Upload State
const isParsing = ref(false);
const parsingError = ref<string | null>(null);
const parsedTelemetry = ref<ParsedCryoLoggerResult | null>(null);

// Forms
const milestoneForm = reactive({
  study_id: "STUDY-CGT-801",
  chain_of_custody_id: "",
  stage: "COURIER_TRANSIT_OUTBOUND",
  milestone_type: "TRANSIT_CHECKPOINT",
  timestamp: "",
  location_name: "In Transit Waypoint",
  facility_id: "WAYPOINT-01",
  latitude: 50.1109,
  longitude: 8.6821,
  temperature_celsius: -196.0,
  custodian_id: "courier_mark",
  custodian_name: "Mark Courier",
  custodian_role: "Specialized Cryo Courier",
  carrier: "WorldCourier Cryo Express",
  tracking_number: "WC-8839210",
  seal_intact: true,
  seal_id: "SEAL-4482",
  dry_shipper_id: "DEWAR-LN2-9901",
  notes: "",
});

const manufacturingForm = reactive({
  study_id: "STUDY-CGT-801",
  chain_of_custody_id: "",
  batch_id: "BATCH-CAR-T-2026-A9",
  manufacturing_facility_id: "CMO-BIO-CENTRAL",
});

const infusionForm = reactive({
  study_id: "STUDY-CGT-801",
  chain_of_custody_id: "",
  site_id: "SITE-101",
  subject_id: "SUBJ-101-0042",
  administrator_id: "dr_johnson_pi",
  administrator_name: "Dr. Robert Johnson, MD",
  signature_token: "pi-sig-token-999",
  notes: "Infusion administered without acute immediate reaction.",
});
const apheresisForm = reactive({
  study_id: "STUDY-CGT-801",
  site_id: "SITE-101",
  subject_id: "SUBJ-101-0042",
  din: "",
  collector_id: "nurse_smith",
  collector_name: "Sarah Smith, RN",
  collector_role: "Apheresis Nurse",
  initial_temp_celsius: -196.0,
  dry_shipper_id: "DEWAR-LN2-9901",
  seal_id: "SEAL-4482",
});

const bedsideForm = reactive({
  scanned_wristband_subject_id: "",
  scanned_bag_barcode: "",
  primary_verifier_id: "nurse_smith",
  primary_verifier_name: "Sarah Smith, RN",
  primary_verifier_role: "Bedside Nurse",
  primary_signature_token: "valid-nurse-sig-token-12345",
  secondary_verifier_id: "dr_johnson_pi",
  secondary_verifier_name: "Dr. Robert Johnson, MD",
  secondary_verifier_role: "Principal Investigator",
  secondary_signature_token: "valid-pi-sig-token-67890",
});

const destructionForm = reactive({
  study_id: "STUDY-CGT-801",
  site_id: "SITE-101",
  lot_number: "LOT-CAR-T-2026-01",
  kit_numbers_str: "KIT-CGT-001",
  vial_identifiers_str: "VIAL-001-A",
  destruction_reason: "Residual post-infusion cell disposal.",
  destruction_method: "HIGH_TEMPERATURE_INCINERATION",
  destruction_facility: "Bio-Hazard Incineration Facility 1",
  witness_1_id: "pharmacist_lee",
  witness_1_name: "David Lee, PharmD",
  witness_1_role: "Investigational Pharmacist",
  witness_1_signature_token: "pharm-token-111",
  witness_2_id: "dr_johnson_pi",
  witness_2_name: "Dr. Robert Johnson, MD",
  witness_2_role: "Principal Investigator",
  witness_2_signature_token: "pi-token-222",
});

function isStageComplete(stageKey: string): boolean {
  if (!cocData.value) return false;
  const stageOrder = stagesList.map((s) => s.key);
  const currentIdx = stageOrder.indexOf(cocData.value.current_stage);
  const targetIdx = stageOrder.indexOf(stageKey);
  return targetIdx < currentIdx;
}

function formatDateTime(isoStr?: string | null): string {
  if (!isoStr) return "-";
  const d = new Date(isoStr);
  return d.toISOString().replace("T", " ").substring(0, 19);
}

async function fetchTraceability() {
  if (!filters.studyId || !filters.subjectId) return;
  isLoading.value = true;
  try {
    const res = await fetch(
      `/api/v1/execution/rtsm/cgt/chain-of-custody/${filters.studyId}/${filters.subjectId}`
    );
    if (res.ok) {
      const data = await res.json();
      cocData.value = data.chain_of_custody;
      milestones.value = data.milestones || [];
      if (cocData.value) {
        bedsideForm.scanned_wristband_subject_id = cocData.value.subject_id;
        bedsideForm.scanned_bag_barcode =
          cocData.value.tamper_evident_barcode || "";
      }
    } else {
      cocData.value = null;
      milestones.value = [];
    }
  } catch (err) {
    console.error("Failed to load COC traceability", err);
  } finally {
    isLoading.value = false;
  }
}

async function testScanBarcode() {
  if (!scanTestInput.value) return;
  try {
    const res = await fetch("/api/v1/execution/rtsm/cgt/verify-barcode", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        barcode_payload: scanTestInput.value,
        expected_subject_id: cocData.value?.subject_id,
        expected_din: cocData.value?.din,
      }),
    });
    scanResult.value = await res.json();
  } catch {
    scanResult.value = {
      is_valid: false,
      failure_reason: "Verification request failed",
    };
  }
}

function openApheresisModal() {
  apheresisForm.study_id = filters.studyId;
  apheresisForm.subject_id = filters.subjectId;
  showApheresisModal.value = true;
}

async function submitApheresis() {
  try {
    const res = await fetch("/api/v1/execution/rtsm/cgt/apheresis", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-change-justification": "Initial apheresis harvest",
      },
      body: JSON.stringify(apheresisForm),
    });
    if (res.ok) {
      showApheresisModal.value = false;
      await fetchTraceability();
    }
  } catch (err) {
    console.error("Failed to initialize apheresis", err);
  }
}

function openBedsideModal() {
  showBedsideModal.value = true;
}

async function submitBedsideVerify() {
  if (!cocData.value) return;
  try {
    const res = await fetch("/api/v1/execution/rtsm/cgt/bedside-verify", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-change-justification": "Bedside dual verification",
      },
      body: JSON.stringify({
        coc_id: cocData.value.id,
        ...bedsideForm,
      }),
    });
    if (res.ok) {
      showBedsideModal.value = false;
      await fetchTraceability();
    }
  } catch (err) {
    console.error("Bedside verification failed", err);
  }
}

function openDestructionModal() {
  if (cocData.value) {
    destructionForm.study_id = cocData.value.study_id;
  }
  showDestructionModal.value = true;
}

async function submitDestruction() {
  try {
    const kit_numbers = destructionForm.kit_numbers_str
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    const vial_identifiers = destructionForm.vial_identifiers_str
      .split(",")
      .map((s) => s.trim())
      .filter(Boolean);
    const res = await fetch(
      "/api/v1/execution/rtsm/cgt/destruction-certificate",
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "x-change-justification": "Product destruction",
        },
        body: JSON.stringify({
          study_id: destructionForm.study_id,
          site_id: destructionForm.site_id,
          lot_number: destructionForm.lot_number,
          kit_numbers,
          vial_identifiers,
          destruction_reason: destructionForm.destruction_reason,
          destruction_method: destructionForm.destruction_method,
          destruction_facility: destructionForm.destruction_facility,
          witness_1_id: destructionForm.witness_1_id,
          witness_1_name: destructionForm.witness_1_name,
          witness_1_role: destructionForm.witness_1_role,
          witness_1_signature_token: destructionForm.witness_1_signature_token,
          witness_2_id: destructionForm.witness_2_id,
          witness_2_name: destructionForm.witness_2_name,
          witness_2_role: destructionForm.witness_2_role,
          witness_2_signature_token: destructionForm.witness_2_signature_token,
          coc_id: cocData.value?.id,
        }),
      }
    );
    if (res.ok) {
      showDestructionModal.value = false;
      await fetchTraceability();
    }
  } catch (err) {
    console.error("Destruction certification failed", err);
  }
}

function openMilestoneModal() {
  milestoneForm.study_id = filters.studyId;
  milestoneForm.chain_of_custody_id = cocData.value?.id || "";
  milestoneForm.stage =
    cocData.value?.current_stage || "COURIER_TRANSIT_OUTBOUND";
  milestoneForm.temperature_celsius =
    cocData.value?.lowest_temp_celsius ?? -196.0;
  milestoneForm.timestamp = new Date().toISOString();
  milestoneForm.notes = "";

  parsedTelemetry.value = null;
  parsingError.value = null;
  isParsing.value = false;
  showMilestoneModal.value = true;
}

async function handleFileUpload(file: File) {
  isParsing.value = true;
  parsingError.value = null;
  try {
    const text = await file.text();
    const result = await parseCryoLoggerFile(text, file.name);
    parsedTelemetry.value = result;

    milestoneForm.temperature_celsius = result.minTemperature;

    if (result.excursionCount > 0) {
      milestoneForm.notes = `[AUTOMATED TELEMETRY AUDIT] Telemetry log '${file.name}' processed (${result.totalSamples} samples, Vendor: ${result.vendor}). ${result.excursionCount} excursion readings detected. Max temperature reached: ${result.maxTemperature.toFixed(1)}°C. GxP Excursion Review required.`;
    } else {
      milestoneForm.notes = `[AUTOMATED TELEMETRY AUDIT] Telemetry log '${file.name}' processed (${result.totalSamples} samples, Vendor: ${result.vendor}). Cryogenic integrity maintained (Min: ${result.minTemperature.toFixed(1)}°C, Max: ${result.maxTemperature.toFixed(1)}°C).`;
    }
  } catch (err: any) {
    parsedTelemetry.value = null;
    parsingError.value = err.message || "Failed to parse telemetry file.";
  } finally {
    isParsing.value = false;
  }
}

async function submitMilestone() {
  if (!milestoneForm.chain_of_custody_id && cocData.value) {
    milestoneForm.chain_of_custody_id = cocData.value.id;
  }

  try {
    const res = await fetch("/api/v1/execution/rtsm/cgt/milestone", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-change-justification":
          milestoneForm.notes || "Log transit milestone",
      },
      body: JSON.stringify({
        study_id: milestoneForm.study_id,
        chain_of_custody_id: milestoneForm.chain_of_custody_id,
        stage: milestoneForm.stage,
        milestone_type: milestoneForm.milestone_type,
        timestamp: milestoneForm.timestamp || new Date().toISOString(),
        location_name: milestoneForm.location_name,
        facility_id: milestoneForm.facility_id,
        latitude: milestoneForm.latitude,
        longitude: milestoneForm.longitude,
        temperature_celsius: milestoneForm.temperature_celsius,
        custodian_id: milestoneForm.custodian_id,
        custodian_name: milestoneForm.custodian_name,
        custodian_role: milestoneForm.custodian_role,
        carrier: milestoneForm.carrier,
        tracking_number: milestoneForm.tracking_number,
        seal_intact: milestoneForm.seal_intact,
        seal_id: milestoneForm.seal_id,
        dry_shipper_id: milestoneForm.dry_shipper_id,
        notes: milestoneForm.notes,
      }),
    });

    if (res.ok) {
      showMilestoneModal.value = false;
      await fetchTraceability();
    } else {
      const errData = await res.json().catch(() => null);
      parsingError.value =
        errData?.detail || "Failed to log transit milestone.";
    }
  } catch (err: any) {
    console.error("Milestone log submission failed", err);
    parsingError.value = "Network error submitting milestone.";
  }
}

function openManufacturingModal() {
  if (cocData.value) {
    manufacturingForm.study_id = cocData.value.study_id;
    manufacturingForm.chain_of_custody_id = cocData.value.id;
  }
  showManufacturingModal.value = true;
}

async function submitManufacturingBind() {
  try {
    const res = await fetch("/api/v1/execution/rtsm/cgt/manufacturing-bind", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-change-justification": "Bind manufacturing batch",
      },
      body: JSON.stringify(manufacturingForm),
    });
    if (res.ok) {
      showManufacturingModal.value = false;
      await fetchTraceability();
    }
  } catch (err) {
    console.error("Manufacturing bind failed", err);
  }
}

function openInfusionModal() {
  if (cocData.value) {
    infusionForm.study_id = cocData.value.study_id;
    infusionForm.chain_of_custody_id = cocData.value.id;
    infusionForm.subject_id = cocData.value.subject_id;
  }
  showInfusionModal.value = true;
}

async function submitInfusion() {
  try {
    const res = await fetch("/api/v1/execution/rtsm/cgt/infuse", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "x-change-justification": "Administer infusion",
      },
      body: JSON.stringify(infusionForm),
    });
    if (res.ok) {
      showInfusionModal.value = false;
      await fetchTraceability();
    }
  } catch (err) {
    console.error("Infusion administration failed", err);
  }
}

onMounted(() => {
  fetchTraceability();
});
</script>

<style scoped>
/* Pure Vanilla CSS Tokens — 0 Tailwind Classes */
.cgt-coc-console {
  display: flex;
  flex-direction: column;
  gap: var(--semantic-spacing-md, 1.5rem);
  padding: var(--semantic-spacing-md, 1.5rem);
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  font-family: var(--font-sans, system-ui, -apple-system, sans-serif);
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: var(--semantic-spacing-sm, 1rem);
}

.header-badge-row {
  display: flex;
  gap: var(--semantic-spacing-xs, 0.5rem);
  margin-bottom: var(--semantic-spacing-xs, 0.5rem);
}

.title {
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0;
  color: var(--semantic-color-text);
}

.subtitle {
  font-size: 0.875rem;
  color: var(--color-text-muted);
  margin: 0.25rem 0 0 0;
}

.header-actions {
  display: flex;
  gap: var(--semantic-spacing-xs, 0.5rem);
}

.card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 8px);
  padding: var(--semantic-spacing-md, 1.5rem);
  box-shadow: var(--shadow-sm, 0 1px 2px 0 rgba(0, 0, 0, 0.05));
}

.filter-panel {
  display: flex;
  gap: var(--semantic-spacing-md, 1.5rem);
  align-items: flex-end;
}

.filter-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.filter-group label {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--color-text-muted);
}

.filter-group input {
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--color-border);
  border-radius: var(--radius-sm, 4px);
  font-size: 0.875rem;
}

.overview-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--semantic-spacing-sm, 1rem);
}

.metric-card {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.metric-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-muted);
  text-transform: uppercase;
}

.metric-value {
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.metric-sub {
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.card-excursion {
  border-left: 4px solid var(--semantic-color-error);
}

.lifecycle-ribbon {
  display: flex;
  flex-direction: column;
  gap: var(--semantic-spacing-sm, 1rem);
}

.section-title {
  font-size: 1rem;
  font-weight: 600;
  margin: 0;
  color: var(--semantic-color-text);
}

.stages-track {
  display: flex;
  justify-content: space-between;
  position: relative;
  overflow-x: auto;
  padding: var(--semantic-spacing-xs, 0.5rem) 0;
}

.stage-node {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
  min-width: 100px;
  text-align: center;
}

.node-circle {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 0.75rem;
  background: var(--color-border);
  color: var(--color-text-muted);
}

.stage-complete .node-circle {
  background: var(--color-success-bg);
  color: var(--color-success);
}

.stage-current .node-circle {
  background: var(--color-primary);
  color: var(--semantic-color-surface);
  box-shadow: 0 0 0 3px rgba(59, 130, 246, 0.3);
}

.node-label {
  font-size: 0.75rem;
  font-weight: 500;
}

.action-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: var(--semantic-spacing-sm, 1rem);
}

.toolbar-title {
  font-size: 0.95rem;
  font-weight: 600;
  margin: 0;
}

.toolbar-hint {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  margin: 0;
}

.toolbar-buttons {
  display: flex;
  gap: var(--semantic-spacing-xs, 0.5rem);
  flex-wrap: wrap;
}

.barcode-verification-card {
  display: grid;
  grid-template-columns: 3fr 2fr;
  gap: var(--semantic-spacing-md, 1.5rem);
}

.barcode-meta {
  font-size: 0.8rem;
  color: var(--color-text-muted);
}

.hash-display {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  font-size: 0.8rem;
}

.hash-code {
  font-family: monospace;
  background: var(--semantic-color-surface-muted);
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.payload-textarea {
  width: 100%;
  font-family: monospace;
  font-size: 0.75rem;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 4px;
  padding: 0.5rem;
}

.scan-input {
  width: 100%;
  padding: 0.5rem;
  border: 1px solid var(--color-border);
  border-radius: 4px;
  font-size: 0.85rem;
  margin-bottom: 0.5rem;
}

.scan-result {
  margin-top: 0.75rem;
  padding: 0.5rem;
  border-radius: 4px;
  font-size: 0.8rem;
}

.scan-valid {
  background: var(--color-success-bg);
  color: var(--semantic-color-success);
}

.scan-invalid {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

.table th,
.table td {
  padding: 0.6rem 0.75rem;
  border-bottom: 1px solid var(--color-border);
  text-align: left;
}

.table th {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--color-text-muted);
  background: var(--semantic-color-surface-muted);
}

.badge {
  display: inline-block;
  padding: 0.2rem 0.5rem;
  border-radius: 9999px;
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
}

.badge-cgt {
  background: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}
.badge-gxp {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}
.badge-isbt {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
}
.badge-unlocked {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}
.badge-quarantine {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}
.badge-stage {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.btn {
  padding: 0.5rem 1rem;
  border-radius: var(--radius-sm, 4px);
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease-in-out;
}

.btn-primary {
  background: var(--color-primary);
  color: var(--semantic-color-surface);
}
.btn-secondary {
  background: var(--color-surface);
  border-color: var(--color-border);
  color: var(--semantic-color-text);
}
.btn-outline {
  background: transparent;
  border-color: var(--color-border);
  color: var(--semantic-color-text);
}
.btn-stepup {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}
.btn-infuse {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}
.btn-danger {
  background: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}
.btn-danger-outline {
  background: transparent;
  border-color: var(--semantic-color-error);
  color: var(--semantic-color-error);
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 50;
}

.modal-card {
  background: var(--color-surface);
  border-radius: var(--radius-md, 8px);
  padding: var(--semantic-spacing-md, 1.5rem);
  width: 100%;
  max-width: 540px;
  max-height: 90vh;
  overflow-y: auto;
}

.modal-lg {
  max-width: 760px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 1rem;
}

.col-full {
  grid-column: span 2;
}
.col-half {
  grid-column: span 1;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.form-group label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-muted);
}

.form-group input,
.form-group select,
.form-group textarea {
  padding: 0.5rem;
  border: 1px solid var(--color-border);
  border-radius: 4px;
  font-size: 0.85rem;
}

.verifier-panel {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.75rem;
}

.verifier-title {
  font-size: 0.85rem;
  font-weight: 600;
  margin: 0 0 0.5rem 0;
  color: var(--semantic-color-text-secondary);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  margin-top: 1.5rem;
}

.code-font {
  font-family: monospace;
}
.text-danger {
  color: var(--semantic-color-error);
}
.text-success {
  color: var(--semantic-color-success);
}
.text-muted {
  color: var(--semantic-color-text-secondary);
}
.font-bold {
  font-weight: 700;
}

.modal-xl {
  max-width: 900px;
}

.modal-subtitle {
  font-size: 0.8rem;
  color: var(--color-text-muted);
  margin: 0.2rem 0 0 0;
}

.milestone-modal-content {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.dropzone-section {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.section-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--color-text-muted);
  text-transform: uppercase;
}

.dropzone-box {
  border: 2px dashed var(--color-border);
  border-radius: 8px;
  padding: 1.5rem;
  text-align: center;
  background: var(--semantic-color-surface-muted);
  cursor: pointer;
  transition: all 0.2s ease;
}

.dropzone-box:hover,
.dropzone-active {
  border-color: var(--color-primary);
  background: var(--semantic-color-info-bg);
}

.dropzone-has-file {
  border-style: solid;
  border-color: var(--color-success);
  background: var(--semantic-color-success-bg-soft);
}

.hidden-file-input {
  display: none;
}

.dropzone-prompt .dropzone-text {
  font-size: 0.9rem;
  margin: 0 0 0.25rem 0;
  color: var(--semantic-color-text);
}

.dropzone-prompt .dropzone-sub {
  font-size: 0.75rem;
  color: var(--color-text-muted);
  margin: 0;
}

.excursion-drawer {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-left: 4px solid var(--color-primary);
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.drawer-title {
  font-size: 1rem;
  font-weight: 700;
  margin: 0;
}

.drawer-title-group {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.drawer-stats-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.75rem;
  background: var(--semantic-color-surface-muted);
  padding: 0.75rem;
  border-radius: 6px;
}

.drawer-stat {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.stat-label {
  font-size: 0.7rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--color-text-muted);
}

.stat-value {
  font-size: 1.1rem;
  font-weight: 700;
}

.sub-title {
  font-size: 0.85rem;
  font-weight: 700;
  margin: 0 0 0.5rem 0;
}

.row-excursion-event {
  background: var(--semantic-color-error-bg-soft);
}

.row-excursion-highlight {
  background: var(--semantic-color-error-bg-row);
}

.preview-scroll {
  max-height: 180px;
  overflow-y: auto;
}

.alert {
  padding: 0.75rem 1rem;
  border-radius: 6px;
  font-size: 0.85rem;
}

.alert-danger {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  border: 1px solid var(--semantic-color-error-border);
}

.alert-info {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
  border: 1px solid var(--semantic-color-divider-accent);
}

.btn-xs {
  padding: 0.25rem 0.5rem;
  font-size: 0.75rem;
}
</style>
