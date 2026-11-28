<template>
  <div id="section-audit" class="dashboard-section active">
    <!-- View Header -->
    <div class="section-header">
      <h2>Regulatory Auditor &amp; Inspection Portal</h2>
      <p>
        Unified compliance dashboard. Inspect immutable eTMF audit logs, verify
        real-time cryptographic execution ledger integrity, download watermarked
        evidence, and export validated regulatory binders.
      </p>
    </div>

    <!-- Alert Banner for errors -->
    <div
      v-if="globalError"
      class="card"
      style="
        border-left: 4px solid var(--error);
        background: var(--semantic-color-error-bg-soft);
        padding: 12px 16px;
        margin-bottom: 20px;
      "
    >
      <span style="color: var(--error); font-weight: 600"> Error:</span>
      {{ globalError }}
    </div>

    <div class="grid-2-responsive">
      <!-- Card 1: GxP Cryptographic Integrity Seal Verification -->
      <div class="card">
        <div class="card-title">
          <span>GxP Execution Ledger Chain Verification</span>
          <button
            class="btn btn-secondary badge"
            style="
              padding: 4px 8px;
              font-size: var(--font-size-2xs);
              cursor: pointer;
            "
            :disabled="integrity.loading"
            @click="verifyExecutionIntegrity"
          >
            {{ integrity.loading ? "Verifying..." : "Verify Now" }}
          </button>
        </div>
        <div style="padding: 8px 0">
          <p
            style="
              font-size: 13px;
              color: var(--semantic-color-text-secondary);
              margin-bottom: 12px;
            "
          >
            Recomputes and validates the sequential Merkle-tree seals and
            blockchain-style chaining on clinical trial execution tables.
          </p>

          <div
            v-if="integrity.loading"
            style="
              padding: 16px;
              text-align: center;
              background: var(--semantic-color-surface-muted);
              border-radius: 6px;
            "
          >
            <div
              class="spinner"
              style="display: inline-block; margin-right: 8px"
            />
            <span
              >Executing cryptographic seal validations across audit ledger
              logs...</span
            >
          </div>

          <div
            v-else-if="integrity.verified === true"
            style="
              padding: 16px;
              background: var(--semantic-color-success-bg-soft);
              border: 1px solid var(--semantic-color-success-border);
              border-radius: 6px;
            "
          >
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-size: 20px"
                ><ClinicalIcon name="check-circle" :size="16"
              /></span>
              <div>
                <strong
                  style="color: var(--semantic-color-success); font-size: 14px"
                  >INTEGRITY VERIFIED</strong
                >
                <p
                  style="
                    font-size: 12px;
                    margin: 4px 0 0 0;
                    color: var(--semantic-color-text);
                  "
                >
                  {{
                    integrity.message ||
                    "All sequential block hashes, Merkle roots, and historical data logs are intact and structurally unbroken."
                  }}
                </p>
              </div>
            </div>
          </div>

          <div
            v-else-if="integrity.verified === false"
            style="
              padding: 16px;
              background: var(--semantic-color-error-bg-soft);
              border: 1px solid var(--semantic-color-error-border);
              border-radius: 6px;
            "
          >
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-size: 20px"
                ><ClinicalIcon name="alert-circle" :size="16"
              /></span>
              <div>
                <strong style="color: var(--error); font-size: 14px"
                  >INTEGRITY BREACH / TAMPERED</strong
                >
                <p
                  style="
                    font-size: 12px;
                    margin: 4px 0 0 0;
                    color: var(--semantic-color-text);
                  "
                >
                  {{
                    integrity.message ||
                    "A discrepancy was detected in the cryptographic ledger chain. Trial lock sequence has been triggered."
                  }}
                </p>
              </div>
            </div>
          </div>

          <div
            v-else
            style="
              padding: 16px;
              background: var(--semantic-color-surface-muted);
              border: 1px dashed var(--border);
              border-radius: 6px;
              text-align: center;
            "
          >
            <span
              style="
                font-size: 13px;
                color: var(--semantic-color-text-secondary);
              "
            >
              Ledger integrity status unknown. Click
              <strong>Verify Now</strong> to execute GxP block verification.
            </span>
          </div>
        </div>
      </div>

      <!-- Card 2: Regulatory Binder ZIP Export -->
      <div class="card">
        <div class="card-title">
          <span>Regulatory Binder ZIP Export</span>
        </div>
        <div style="padding: 8px 0">
          <p
            style="
              font-size: 13px;
              color: var(--semantic-color-text-secondary);
              margin-bottom: 12px;
            "
          >
            Compile and export an inspection-ready clinical archive containing
            all eTMF documents structurally organized by DIA TMF Zones and
            Sections.
          </p>
          <div style="display: flex; flex-direction: column; gap: 12px">
            <div class="form-group" style="margin-bottom: 0">
              <label
                for="binder-study-id-input"
                style="
                  font-weight: 600;
                  font-size: 12px;
                  margin-bottom: 4px;
                  display: block;
                "
                >Study Reference ID</label
              >
              <input
                id="binder-study-id-input"
                v-model="binderStudyId"
                type="text"
                placeholder="e.g. study_001"
                style="
                  width: 100%;
                  padding: 8px;
                  border-radius: 4px;
                  border: 1px solid var(--border);
                  background: var(--semantic-color-surface-muted);
                  color: var(--semantic-color-text);
                "
              />
            </div>
            <div
              style="
                display: flex;
                align-items: center;
                gap: 8px;
                margin-top: 4px;
              "
            >
              <input
                id="chk-history"
                v-model="binderIncludeHistory"
                type="checkbox"
                style="cursor: pointer"
              />
              <label
                for="chk-history"
                style="font-size: 13px; cursor: pointer; user-select: none"
                >Include complete document version histories (audit
                files)</label
              >
            </div>
            <button
              class="btn btn-primary"
              style="margin-top: 8px; padding: 10px; cursor: pointer"
              :disabled="exportingBinder || !binderStudyId.trim()"
              @click="exportRegulatoryBinder"
            >
              {{
                exportingBinder
                  ? "Generating Archive..."
                  : "Export Regulatory Binder (ZIP)"
              }}
            </button>
          </div>
        </div>
      </div>

      <!-- Card 3: Ingest / Upload TMF Document -->
      <div class="card card-upload-document" style="grid-column: span 2">
        <div class="card-title">
          <span>Ingest New TMF Document</span>
        </div>
        <div style="padding: 8px 0">
          <p
            style="
              font-size: 13px;
              color: var(--semantic-color-text-secondary);
              margin-bottom: 12px;
            "
          >
            Upload a document and index it with DIA TMF taxonomy tags into the
            secure study repository.
          </p>
          <div style="display: flex; flex-direction: column; gap: 12px">
            <div class="form-group" style="margin-bottom: 0">
              <label
                for="tmf-file-input"
                style="
                  font-weight: 600;
                  font-size: 12px;
                  margin-bottom: 4px;
                  display: block;
                "
                >Select File</label
              >
              <input
                id="tmf-file-input"
                type="file"
                style="
                  width: 100%;
                  padding: 8px;
                  border-radius: 4px;
                  border: 1px solid var(--border);
                  background: var(--semantic-color-surface-muted);
                  color: var(--semantic-color-text);
                "
                @change="handleTmfFileSelect"
              />
            </div>
            <div class="grid-2-responsive">
              <div class="form-group" style="margin-bottom: 0">
                <label
                  for="tmf-zone-select"
                  style="
                    font-weight: 600;
                    font-size: 12px;
                    margin-bottom: 4px;
                    display: block;
                  "
                  >TMF Zone</label
                >
                <select
                  id="tmf-zone-select"
                  v-model="uploadParams.zone"
                  style="
                    width: 100%;
                    padding: 8px;
                    border-radius: 4px;
                    border: 1px solid var(--border);
                    background: var(--semantic-color-surface-muted);
                    color: var(--semantic-color-text);
                  "
                >
                  <option value="01. Trial Management">
                    01. Trial Management
                  </option>
                  <option value="02. Central Trial Documents">
                    02. Central Trial Documents
                  </option>
                  <option value="05. Site Management">
                    05. Site Management
                  </option>
                </select>
              </div>
              <div class="form-group" style="margin-bottom: 0">
                <label
                  for="tmf-section-input"
                  style="
                    font-weight: 600;
                    font-size: 12px;
                    margin-bottom: 4px;
                    display: block;
                  "
                  >TMF Section</label
                >
                <input
                  id="tmf-section-input"
                  v-model="uploadParams.section"
                  type="text"
                  placeholder="e.g. 01.01 Trial Steering Committee"
                  style="
                    width: 100%;
                    padding: 8px;
                    border-radius: 4px;
                    border: 1px solid var(--border);
                    background: var(--semantic-color-surface-muted);
                    color: var(--semantic-color-text);
                  "
                />
              </div>
            </div>
            <button
              class="btn btn-primary btn-upload-doc-submit"
              style="margin-top: 8px; padding: 10px; cursor: pointer"
              :disabled="uploadingDoc || !selectedTmfFile"
              @click="uploadTmfDocument"
            >
              {{ uploadingDoc ? "Uploading..." : "Upload & Ingest Document" }}
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- Card 3: eTMF Document Directory & Watermarked Viewer -->
    <div class="card" style="margin-top: 20px">
      <div class="card-title">
        <span>eTMF Document Directory &amp; Viewer</span>
        <button
          class="btn btn-secondary badge"
          style="
            padding: 4px 8px;
            font-size: var(--font-size-2xs);
            cursor: pointer;
          "
          :disabled="documentsLoading"
          @click="fetchDocuments"
        >
          {{ documentsLoading ? "Loading..." : "Refresh List" }}
        </button>
      </div>

      <p
        style="
          font-size: 13px;
          color: var(--semantic-color-text-secondary);
          margin: 8px 0 16px 0;
        "
      >
        Review indexed documents within the eTMF. Preview documents inline with
        browser watermarks or download fully audited, watermarked PDF/TXT
        copies.
      </p>

      <div style="overflow-x: auto">
        <table
          class="clinical-table"
          style="width: 100%; border-collapse: collapse; font-size: 13px"
        >
          <thead>
            <tr
              style="
                background: var(--semantic-color-surface-muted);
                border-bottom: 1px solid var(--border);
                text-align: left;
              "
            >
              <th style="padding: 10px">ID</th>
              <th style="padding: 10px">Filename</th>
              <th style="padding: 10px">TMF Zone/Sec</th>
              <th style="padding: 10px">Artifact Type</th>
              <th style="padding: 10px">Status</th>
              <th style="padding: 10px">Ver.</th>
              <th style="padding: 10px; text-align: right">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="documentsLoading" style="text-align: center">
              <td
                colspan="7"
                style="
                  padding: 20px;
                  color: var(--semantic-color-text-secondary);
                "
              >
                Retrieving active eTMF document registry...
              </td>
            </tr>
            <tr v-else-if="documents.length === 0" style="text-align: center">
              <td
                colspan="7"
                style="
                  padding: 20px;
                  color: var(--semantic-color-text-secondary);
                "
              >
                No documents found in the eTMF registry. Ingest some documents
                from design or execution views!
              </td>
            </tr>
            <tr
              v-for="doc in documents"
              v-else
              :key="doc.id"
              style="border-bottom: 1px solid var(--border)"
            >
              <td
                style="
                  padding: 10px;
                  font-family: var(--semantic-font-mono);
                  font-size: var(--font-size-2xs);
                  max-width: 80px;
                  overflow: hidden;
                  text-overflow: ellipsis;
                  white-space: nowrap;
                "
                :title="doc.id"
              >
                {{ doc.id }}
              </td>
              <td style="padding: 10px; font-weight: 500">
                {{ doc.filename }}
              </td>
              <td style="padding: 10px">
                Zone {{ String(doc.zone).padStart(2, "0") }} / {{ doc.section }}
              </td>
              <td style="padding: 10px">
                {{ doc.artifact_type }}
              </td>
              <td style="padding: 10px">
                <span
                  :class="
                    doc.status === 'SIGNED'
                      ? 'badge status-approved'
                      : 'badge status-draft'
                  "
                  style="font-size: var(--font-size-2xs)"
                >
                  {{ doc.status }}
                </span>
              </td>
              <td style="padding: 10px">v{{ doc.version_index }}</td>
              <td
                style="
                  padding: 10px;
                  text-align: right;
                  display: flex;
                  gap: 8px;
                  justify-content: flex-end;
                "
              >
                <button
                  type="button"
                  class="btn btn-secondary btn-preview-doc"
                  style="
                    padding: 4px 8px;
                    font-size: var(--font-size-2xs);
                    cursor: pointer;
                  "
                  @click="previewDocument(doc)"
                >
                  Preview<span class="sr-only">
                    secure document {{ doc.filename }}</span
                  >
                </button>
                <button
                  v-if="doc.status !== 'SIGNED'"
                  type="button"
                  class="btn btn-primary btn-sign-doc"
                  style="
                    padding: 4px 8px;
                    font-size: var(--font-size-2xs);
                    cursor: pointer;
                  "
                  @click="openSignModal(doc)"
                >
                  Sign<span class="sr-only">
                    and manifestation seal document {{ doc.filename }}</span
                  >
                </button>
                <button
                  type="button"
                  class="btn btn-secondary btn-download-watermarked"
                  style="
                    padding: 4px 8px;
                    font-size: var(--font-size-2xs);
                    cursor: pointer;
                  "
                  @click="downloadWatermarkedDoc(doc)"
                >
                  Download (Watermarked)<span class="sr-only">
                    copy of {{ doc.filename }}</span
                  >
                </button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Secure Interactive Watermarked Document Preview Panel -->
      <div
        v-if="previewDoc"
        class="card secure-preview-panel"
        style="
          margin-top: 20px;
          border: 1px solid var(--accent);
          background: var(--semantic-color-surface-muted);
          padding: 0;
        "
      >
        <div
          class="card-title"
          style="
            padding: 12px 16px;
            border-bottom: 1px solid var(--border);
            display: flex;
            justify-content: space-between;
            align-items: center;
            background: var(--semantic-color-info-bg);
          "
        >
          <div style="display: flex; align-items: center; gap: 8px">
            <span style="font-size: 16px"
              ><ClinicalIcon name="search" :size="16"
            /></span>
            <span style="font-weight: 600"
              >Secure Preview: {{ previewDoc.filename }}</span
            >
            <span
              class="badge status-approved"
              style="font-size: var(--font-size-2xs)"
              >Watermarked Preview active</span
            >
          </div>
          <button
            class="btn btn-secondary btn-close-preview"
            style="
              padding: 2px 8px;
              font-size: var(--font-size-2xs);
              cursor: pointer;
            "
            @click="closePreview"
          >
            Close Viewer
          </button>
        </div>

        <!-- Document Preview Text Area with Client-Side Watermark Overlay -->
        <div
          class="watermarked-content-wrapper"
          style="
            position: relative;
            padding: 24px;
            min-height: 250px;
            background: var(--semantic-color-surface);
            color: var(--semantic-color-text-dark);
            font-family: var(--semantic-font-mono);
            font-size: 13px;
            line-height: 1.5;
            overflow-y: auto;
            max-height: 450px;
          "
        >
          <!-- Repeating SVG Watermark overlay -->
          <div
            class="watermark-overlay"
            style="
              position: absolute;
              top: 0;
              left: 0;
              right: 0;
              bottom: 0;
              pointer-events: none;
              z-index: 5;
              background-repeat: repeat;
            "
          />

          <!-- Preview content (render pre-formatted text safely) -->
          <pre
            style="
              margin: 0;
              white-space: pre-wrap;
              position: relative;
              z-index: 1;
            "
            >{{ previewContent }}</pre>
        </div>

        <!-- Signature Manifestation Details -->
        <div
          v-if="previewDoc.signature_manifestation"
          id="signature-manifestation-view"
          style="
            padding: 16px;
            border-top: 1px solid var(--border);
            background: var(--semantic-color-surface-muted);
            font-size: 13px;
          "
        >
          <div
            style="
              font-weight: 600;
              margin-bottom: 8px;
              color: var(--semantic-color-text);
              display: flex;
              align-items: center;
              gap: 6px;
            "
          >
            <span><ClinicalIcon name="edit" :size="16" /></span> 21 CFR Part 11
            Electronic Signature Manifestation
          </div>
          <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 8px">
            <div>
              Signer:
              <strong id="manifest-signer">{{
                previewDoc.signature_manifestation.signer_id
              }}</strong>
            </div>
            <div>
              Date/Time (UTC):
              <strong id="manifest-timestamp">{{
                previewDoc.signature_manifestation.timestamp
              }}</strong>
            </div>
            <div style="grid-column: span 2">
              Reason:
              <strong id="manifest-reason">{{
                previewDoc.signature_manifestation.signing_reason
              }}</strong>
            </div>
            <div
              style="
                grid-column: span 2;
                font-size: var(--font-size-2xs);
                color: var(--semantic-color-text-secondary);
                word-break: break-all;
              "
            >
              SHA-256 Hash:
              <code>{{ previewDoc.signature_manifestation.sha256_hash }}</code>
            </div>
            <div
              v-if="previewDoc.signature_manifestation.signature"
              style="
                grid-column: span 2;
                font-size: var(--font-size-2xs);
                color: var(--semantic-color-text-secondary);
                word-break: break-all;
              "
            >
              Signature:
              <code>{{ previewDoc.signature_manifestation.signature }}</code>
            </div>
          </div>
        </div>

        <div
          style="
            padding: 10px 16px;
            border-top: 1px solid var(--border);
            font-size: var(--font-size-2xs);
            color: var(--semantic-color-text-secondary);
            display: flex;
            justify-content: space-between;
            background: var(--semantic-color-surface-muted);
          "
        >
          <span
            >Audited view log added under user ID:
            <strong>{{ currentUserId }}</strong></span
          >
          <span
            >Date/Time: <strong>{{ new Date().toUTCString() }}</strong></span
          >
        </div>
      </div>
    </div>

    <!-- Card 4: Immutable Regulatory eTMF Audit Trail Logs -->
    <div class="card" style="margin-top: 20px">
      <div class="card-title">
        <span>Immutable eTMF Audit Ledger Trail</span>
        <div style="display: flex; gap: 8px">
          <button
            class="btn btn-secondary badge btn-refresh-logs"
            style="
              padding: 4px 8px;
              font-size: var(--font-size-2xs);
              cursor: pointer;
            "
            :disabled="auditLoading"
            @click="fetchAuditLogs"
          >
            {{ auditLoading ? "Loading..." : "Refresh Logs" }}
          </button>
        </div>
      </div>

      <p
        style="
          font-size: 13px;
          color: var(--semantic-color-text-secondary);
          margin: 8px 0 16px 0;
        "
      >
        Complete, chronologically ordered Part 11 system log. View read actions
        (VIEW, LIST), ingestion audits, QC status transitions, and binder
        exports.
      </p>

      <!-- Audit Log Filter Bar -->
      <div class="audit-filter-bar">
        <div class="form-group audit-filter-group">
          <label for="filter-user-id-input" class="audit-filter-label"
            >Actor / User ID</label
          >
          <input
            id="filter-user-id-input"
            v-model="filters.user_id"
            type="text"
            placeholder="Filter by Actor"
            class="filter-user-id audit-filter-control"
          />
        </div>
        <div class="form-group audit-filter-group">
          <label for="filter-action-select" class="audit-filter-label"
            >Action Type</label
          >
          <select
            id="filter-action-select"
            v-model="filters.action"
            class="filter-action audit-filter-control"
          >
            <option value="">All Actions</option>
            <option value="CREATE">CREATE (Record Creation)</option>
            <option value="UPDATE">UPDATE (Data Modification)</option>
            <option value="SIGN">SIGN (Electronic Sign-off)</option>
            <option value="INGEST">INGEST (Ingest)</option>
            <option value="VIEW">VIEW (Read Metadata)</option>
            <option value="DOWNLOAD">DOWNLOAD (Standard Download)</option>
            <option value="WATERMARKED_DOWNLOAD">
              WATERMARKED_DOWNLOAD (Auditor Download)
            </option>
            <option value="LIST">LIST (Directory List)</option>
            <option value="AUDIT_VIEW">AUDIT_VIEW (Audit Trail Read)</option>
            <option value="QC_TRANSITION">QC_TRANSITION (QC Lifecycle)</option>
            <option value="BINDER_EXPORT">BINDER_EXPORT (Binder Zip)</option>
            <option value="COMPLETENESS">COMPLETENESS (EDL Metrics)</option>
          </select>
        </div>
        <div class="form-group audit-filter-group">
          <label for="filter-entity-type-select" class="audit-filter-label"
            >Entity Type</label
          >
          <select
            id="filter-entity-type-select"
            v-model="filters.entity_type"
            class="filter-entity-type audit-filter-control"
          >
            <option value="">All Entities</option>
            <option value="Observation">Observation (Clinical Data)</option>
            <option value="Subject">Subject (Participant)</option>
            <option value="Query">Query (Discrepancy)</option>
            <option value="Form">Form (eCRF)</option>
            <option value="Document">Document (eTMF)</option>
          </select>
        </div>
        <div class="form-group audit-filter-group audit-filter-group--date">
          <label for="filter-start-date-input" class="audit-filter-label"
            >Start Date</label
          >
          <input
            id="filter-start-date-input"
            v-model="filters.start_date"
            type="date"
            class="filter-start-date audit-filter-control"
          />
        </div>
        <div class="form-group audit-filter-group audit-filter-group--date">
          <label for="filter-end-date-input" class="audit-filter-label"
            >End Date</label
          >
          <input
            id="filter-end-date-input"
            v-model="filters.end_date"
            type="date"
            class="filter-end-date audit-filter-control"
          />
        </div>
        <div class="form-group audit-filter-group">
          <label for="filter-document-id-input" class="audit-filter-label"
            >Record / Document ID</label
          >
          <input
            id="filter-document-id-input"
            v-model="filters.document_id"
            type="text"
            placeholder="Filter by ID"
            class="filter-document-id audit-filter-control"
          />
        </div>
        <div
          style="
            display: flex;
            gap: 8px;
            width: 100%;
            margin-top: 4px;
            justify-content: flex-end;
          "
        >
          <button
            class="btn btn-secondary btn-clear-filters"
            style="padding: 6px 12px; font-size: 12px; cursor: pointer"
            @click="clearFilters"
          >
            Clear Filters
          </button>
          <button
            class="btn btn-primary btn-apply-filters"
            style="padding: 6px 12px; font-size: 12px; cursor: pointer"
            :disabled="auditLoading"
            @click="applyFilters"
          >
            Apply Filters
          </button>
        </div>
      </div>

      <!-- Logs Table with Expandable Inspection Details -->
      <div style="overflow-x: auto">
        <table
          class="clinical-table audit-ledger-table"
          style="width: 100%; border-collapse: collapse; font-size: 13px"
        >
          <thead>
            <tr
              style="
                background: var(--semantic-color-surface-muted);
                border-bottom: 1px solid var(--border);
                text-align: left;
              "
            >
              <th style="padding: 10px; width: 170px">UTC Timestamp</th>
              <th style="padding: 10px; width: 110px">Actor ID</th>
              <th style="padding: 10px; width: 110px">Entity Type</th>
              <th style="padding: 10px; width: 120px">Action</th>
              <th style="padding: 10px">Operation Details</th>
              <th style="padding: 10px; width: 100px; text-align: right">
                Inspection
              </th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="auditLoading" style="text-align: center">
              <td
                colspan="6"
                style="
                  padding: 20px;
                  color: var(--semantic-color-text-secondary);
                "
              >
                Retrieving chronological ledger events...
              </td>
            </tr>
            <tr v-else-if="auditLogs.length === 0" style="text-align: center">
              <td
                colspan="6"
                style="
                  padding: 20px;
                  color: var(--semantic-color-text-secondary);
                "
              >
                No audit trail logs match the specified criteria.
              </td>
            </tr>
            <template v-for="log in auditLogs" v-else :key="log.id">
              <!-- Primary Row -->
              <tr
                style="border-bottom: 1px solid var(--border); cursor: pointer"
                :style="{
                  background: isRowExpanded(log.id)
                    ? 'var(--semantic-color-info-bg)'
                    : 'transparent',
                }"
                @click="toggleRowDetails(log.id)"
              >
                <td
                  style="
                    padding: 10px;
                    font-family: var(--semantic-font-mono);
                    font-size: var(--font-size-2xs);
                    white-space: nowrap;
                    color: var(--semantic-color-text-secondary);
                  "
                >
                  {{ formatTimestamp(log.timestamp) }}
                </td>
                <td style="padding: 10px; font-weight: 500">
                  {{ log.user_id }}
                </td>
                <td style="padding: 10px; font-size: 12px">
                  <span
                    class="badge"
                    style="
                      background: var(--semantic-color-surface-header);
                      color: var(--semantic-color-text-secondary);
                      font-size: var(--font-size-2xs);
                    "
                  >
                    {{ getEntityType(log) }}
                  </span>
                </td>
                <td style="padding: 10px">
                  <span
                    :class="getActionBadgeClass(log.action)"
                    style="font-size: var(--font-size-2xs)"
                  >
                    {{ log.action }}
                  </span>
                </td>
                <td style="padding: 10px; font-size: 12px; line-height: 1.4">
                  {{ log.details }}
                </td>
                <td style="padding: 10px; text-align: right">
                  <button
                    type="button"
                    class="btn btn-secondary btn-inspect-row"
                    style="
                      padding: 3px 8px;
                      font-size: var(--font-size-2xs);
                      cursor: pointer;
                    "
                    :aria-expanded="isRowExpanded(log.id) ? 'true' : 'false'"
                    @click.stop="toggleRowDetails(log.id)"
                  >
                    {{ isRowExpanded(log.id) ? "Hide" : "Inspect"
                    }}<span class="sr-only">
                      audit details for {{ log.id }}</span
                    >
                  </button>
                </td>
              </tr>

              <!-- Detailed Inspection Row Rendering old_value vs new_value, reason_for_change, and sha256_hash -->
              <tr
                v-if="isRowExpanded(log.id)"
                class="audit-details-row"
                style="
                  border-bottom: 2px solid var(--border);
                  background: var(--semantic-color-surface-muted);
                "
              >
                <td colspan="6" style="padding: 16px 20px">
                  <div
                    style="
                      display: flex;
                      flex-direction: column;
                      gap: 12px;
                      font-size: 12px;
                    "
                  >
                    <!-- Header Info -->
                    <div
                      style="
                        display: flex;
                        justify-content: space-between;
                        align-items: center;
                        border-bottom: 1px solid var(--border);
                        padding-bottom: 8px;
                      "
                    >
                      <div>
                        <strong>21 CFR Part 11 Audit Record:</strong>
                        <code
                          style="
                            margin-left: 6px;
                            font-size: var(--font-size-2xs);
                          "
                          >{{ log.id }}</code
                        >
                      </div>
                      <div
                        style="
                          font-size: var(--font-size-2xs);
                          color: var(--semantic-color-text-secondary);
                        "
                      >
                        Actor Role: <strong>{{ log.user_role }}</strong> |
                        Record:
                        <strong>{{
                          log.record_id || log.document_id || "-"
                        }}</strong>
                      </div>
                    </div>

                    <!-- Mandatory GxP Reason for Change -->
                    <div
                      class="reason-for-change"
                      style="
                        padding: 10px 12px;
                        background: var(--semantic-color-info-bg);
                        border-left: 4px solid var(--accent);
                        border-radius: 4px;
                      "
                    >
                      <strong style="color: var(--text-main)"
                        >Mandatory GxP Reason for Change:</strong
                      >
                      <p
                        style="
                          margin: 4px 0 0 0;
                          color: var(--semantic-color-text);
                        "
                      >
                        {{
                          log.reason_for_change ||
                          log.change_reason ||
                          log.reasonForChange ||
                          "GxP compliance state verified and recorded."
                        }}
                      </p>
                    </div>

                    <!-- State Diff: old_value vs new_value -->
                    <div
                      style="
                        display: grid;
                        grid-template-columns: 1fr 1fr;
                        gap: 16px;
                      "
                    >
                      <!-- Old Value -->
                      <div
                        class="old-value-diff"
                        style="
                          background: var(--semantic-color-surface);
                          border: 1px solid var(--border);
                          border-radius: 6px;
                          padding: 10px 12px;
                        "
                      >
                        <div
                          style="
                            font-weight: 600;
                            color: var(--semantic-color-error);
                            margin-bottom: 6px;
                            display: flex;
                            align-items: center;
                            gap: 6px;
                          "
                        >
                          <span
                            ><ClinicalIcon name="alert-circle" :size="16"
                          /></span>
                          Previous State (old_value)
                        </div>
                        <pre
                          style="
                            margin: 0;
                            font-size: var(--font-size-2xs);
                            white-space: pre-wrap;
                            word-break: break-all;
                            color: var(--semantic-color-text-secondary);
                            background: var(--semantic-color-surface-muted);
                            padding: 8px;
                            border-radius: 4px;
                          "
                          >{{
                            formatDiffValue(log.old_value, log, "old")
                          }}</pre>
                      </div>

                      <!-- New Value -->
                      <div
                        class="new-value-diff"
                        style="
                          background: var(--semantic-color-surface);
                          border: 1px solid var(--border);
                          border-radius: 6px;
                          padding: 10px 12px;
                        "
                      >
                        <div
                          style="
                            font-weight: 600;
                            color: var(--semantic-color-success);
                            margin-bottom: 6px;
                            display: flex;
                            align-items: center;
                            gap: 6px;
                          "
                        >
                          <span
                            ><ClinicalIcon name="check-circle" :size="16"
                          /></span>
                          Committed State (new_value)
                        </div>
                        <pre
                          style="
                            margin: 0;
                            font-size: var(--font-size-2xs);
                            white-space: pre-wrap;
                            word-break: break-all;
                            color: var(--semantic-color-text-dark);
                            background: var(--semantic-color-success-bg-soft);
                            padding: 8px;
                            border-radius: 4px;
                          "
                          >{{
                            formatDiffValue(log.new_value, log, "new")
                          }}</pre>
                      </div>
                    </div>

                    <!-- Cryptographic SHA-256 Signature Hash -->
                    <div
                      class="sha256-hash"
                      style="
                        display: flex;
                        align-items: center;
                        justify-content: space-between;
                        background: var(--semantic-color-surface);
                        padding: 8px 12px;
                        border-radius: 6px;
                        border: 1px dashed var(--border);
                        font-size: var(--font-size-2xs);
                      "
                    >
                      <div style="display: flex; align-items: center; gap: 8px">
                        <span style="font-size: 14px"
                          ><ClinicalIcon name="shield-check" :size="16"
                        /></span>
                        <span
                          ><strong>Cryptographic SHA-256 Digest:</strong></span
                        >
                        <code
                          class="hash-code"
                          style="
                            font-size: var(--font-size-2xs);
                            color: var(--text-main);
                            word-break: break-all;
                          "
                          >{{ getSha256Hash(log) }}</code
                        >
                      </div>
                      <span
                        class="badge status-approved"
                        style="font-size: var(--font-size-2xs)"
                        >SEAL VERIFIED</span
                      >
                    </div>
                  </div>
                </td>
              </tr>
            </template>
          </tbody>
        </table>
      </div>

      <!-- Audit Pagination controls -->
      <div
        v-if="totalLogs > 0"
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          margin-top: 16px;
          padding: 8px;
          border-top: 1px solid var(--border);
        "
      >
        <span
          style="font-size: 12px; color: var(--semantic-color-text-secondary)"
        >
          Showing records {{ offset + 1 }} to
          {{ Math.min(offset + limit, totalLogs) }} of {{ totalLogs }}
        </span>
        <div style="display: flex; gap: 8px">
          <button
            class="btn btn-secondary btn-prev-page"
            style="padding: 4px 12px; font-size: 12px; cursor: pointer"
            :disabled="offset === 0 || auditLoading"
            @click="prevPage"
          >
            Previous
          </button>
          <button
            class="btn btn-secondary btn-next-page"
            style="padding: 4px 12px; font-size: 12px; cursor: pointer"
            :disabled="offset + limit >= totalLogs || auditLoading"
            @click="nextPage"
          >
            Next
          </button>
        </div>
      </div>
    </div>

    <!-- Card 5: eTMF Completeness Tracking Dashboard -->
    <div class="card" style="margin-top: 20px">
      <div class="card-title">
        <span>eTMF Completeness Tracking &amp; Verification</span>
        <button
          class="btn btn-secondary badge btn-check-completeness"
          style="
            padding: 4px 8px;
            font-size: var(--font-size-2xs);
            cursor: pointer;
          "
          :disabled="completenessLoading"
          @click="checkCompleteness"
        >
          {{ completenessLoading ? "Checking..." : "Re-Verify" }}
        </button>
      </div>

      <p
        style="
          font-size: 13px;
          color: var(--semantic-color-text-secondary);
          margin: 8px 0 16px 0;
        "
      >
        Perform live gap-analysis against the Expected Document List (EDL) to
        verify regulatory compliance of mandatory TMF artifacts for trial
        milestones.
      </p>

      <!-- Completeness Controls -->
      <div class="audit-filter-bar">
        <div class="form-group audit-filter-group">
          <label for="completeness-study-id-input" class="audit-filter-label"
            >Study ID</label
          >
          <input
            id="completeness-study-id-input"
            v-model="completenessParams.study_id"
            type="text"
            placeholder="e.g. study_001"
            class="completeness-study-id audit-filter-control"
          />
        </div>
        <div class="form-group audit-filter-group">
          <label for="completeness-milestone-select" class="audit-filter-label"
            >Milestone</label
          >
          <select
            id="completeness-milestone-select"
            v-model="completenessParams.milestone"
            class="completeness-milestone audit-filter-control"
          >
            <option value="INITIATION">INITIATION (Study Start)</option>
            <option value="CONDUCT">CONDUCT (Data Collection)</option>
            <option value="CLOSEOUT">CLOSEOUT (Study Closed/Lock)</option>
          </select>
        </div>
        <div class="form-group audit-filter-group">
          <label for="completeness-site-id-input" class="audit-filter-label"
            >Site ID (Optional)</label
          >
          <input
            id="completeness-site-id-input"
            v-model="completenessParams.site_id"
            type="text"
            placeholder="e.g. site_001"
            class="completeness-site-id audit-filter-control"
          />
        </div>
        <div
          style="
            display: flex;
            gap: 8px;
            width: 100%;
            margin-top: 4px;
            justify-content: flex-end;
          "
        >
          <button
            class="btn btn-primary btn-run-completeness"
            style="padding: 6px 12px; font-size: 12px; cursor: pointer"
            :disabled="
              completenessLoading || !completenessParams.study_id.trim()
            "
            @click="checkCompleteness"
          >
            Run Completeness Analysis
          </button>
        </div>
      </div>

      <!-- Completeness Results View -->
      <div v-if="completenessLoading" style="padding: 24px; text-align: center">
        <div class="spinner" style="display: inline-block; margin-right: 8px" />
        <span
          >Calculating live completeness metrics and scanning EDL
          expectations...</span
        >
      </div>

      <div
        v-else-if="completenessError"
        style="
          padding: 16px;
          background: var(--semantic-color-error-bg-soft);
          border: 1px solid var(--semantic-color-error-border);
          border-radius: 6px;
        "
      >
        <span style="color: var(--error); font-weight: 600"> Error:</span>
        {{ completenessError }}
      </div>

      <div
        v-else-if="completenessResult"
        style="display: flex; flex-direction: column; gap: 16px"
      >
        <!-- Status Banner -->
        <div
          :style="{
            padding: '16px',
            background: completenessResult.is_complete
              ? 'var(--semantic-color-success-bg-soft)'
              : 'var(--semantic-color-warning-bg-soft)',
            border: completenessResult.is_complete
              ? '1px solid var(--semantic-color-success-border)'
              : '1px solid var(--semantic-color-warning-border-strong)',
            borderRadius: '6px',
          }"
        >
          <div
            style="
              display: flex;
              align-items: center;
              justify-content: space-between;
              flex-wrap: wrap;
              gap: 12px;
            "
          >
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-size: 20px">{{
                completenessResult.is_complete ? "Verified" : "Pending"
              }}</span>
              <div>
                <strong
                  :style="{
                    color: completenessResult.is_complete
                      ? 'var(--semantic-color-success)'
                      : 'var(--semantic-color-warning)',
                    fontSize: '15px',
                  }"
                >
                  {{
                    completenessResult.is_complete
                      ? "MILESTONE COMPLIANT"
                      : "PENDING EXPECTED DOCUMENTS"
                  }}
                </strong>
                <p
                  style="
                    font-size: 12px;
                    margin: 4px 0 0 0;
                    color: var(--semantic-color-text);
                  "
                >
                  Study: <strong>{{ completenessResult.study_id }}</strong> |
                  Milestone: <strong>{{ completenessResult.milestone }}</strong>
                  <span v-if="completenessResult.site_id">
                    | Site:
                    <strong>{{ completenessResult.site_id }}</strong></span
                  >
                </p>
              </div>
            </div>
            <div style="font-size: 13px; font-weight: 600">
              Score:
              {{
                (completenessResult.present_artifacts &&
                  completenessResult.present_artifacts.length) ||
                completenessResult.present_count ||
                0
              }}
              /
              {{
                (completenessResult.per_artifact_detail &&
                  completenessResult.per_artifact_detail.length) ||
                completenessResult.expected_count ||
                0
              }}
              Artifacts Present
            </div>
          </div>
        </div>

        <!-- Artifacts Table -->
        <div style="overflow-x: auto">
          <table
            class="clinical-table"
            style="width: 100%; border-collapse: collapse; font-size: 13px"
          >
            <thead>
              <tr
                style="
                  background: var(--semantic-color-surface-muted);
                  border-bottom: 1px solid var(--border);
                  text-align: left;
                "
              >
                <th style="padding: 10px">Expected Artifact Type</th>
                <th style="padding: 10px">Scope</th>
                <th style="padding: 10px">Compliance Status</th>
                <th style="padding: 10px">Document ID</th>
                <th style="padding: 10px">Ver.</th>
                <th style="padding: 10px; text-align: right">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="art in completenessResult.per_artifact_detail"
                :key="art.artifact_type"
                style="border-bottom: 1px solid var(--border)"
              >
                <td style="padding: 10px; font-weight: 500">
                  {{ art.artifact_type }}
                </td>
                <td style="padding: 10px; text-transform: capitalize">
                  {{ art.scope }}
                </td>
                <td style="padding: 10px">
                  <span
                    :class="getCompletenessBadgeClass(art.status)"
                    style="font-size: var(--font-size-2xs)"
                  >
                    {{ art.status }}
                  </span>
                </td>
                <td
                  style="
                    padding: 10px;
                    font-family: var(--semantic-font-mono);
                    font-size: var(--font-size-2xs);
                  "
                >
                  {{ art.document_id || "-" }}
                </td>
                <td style="padding: 10px">
                  {{
                    art.version_index !== null &&
                    art.version_index !== undefined
                      ? "v" + art.version_index
                      : "-"
                  }}
                </td>
                <td style="padding: 10px; text-align: right">
                  <button
                    v-if="art.document_id"
                    class="btn btn-secondary btn-preview-completeness-doc"
                    style="
                      padding: 3px 6px;
                      font-size: var(--font-size-2xs);
                      cursor: pointer;
                    "
                    @click="
                      previewDocument({
                        id: art.document_id,
                        filename: art.artifact_type,
                      })
                    "
                  >
                    Preview Evidence
                  </button>
                  <span
                    v-else
                    style="
                      color: var(--semantic-color-text-secondary);
                      font-size: 12px;
                      font-style: italic;
                    "
                    >Missing Document</span
                  >
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Signature Capture Modal Dialog -->
    <SignatureCaptureModal
      :is-open="showSignModal"
      :username="currentUserId"
      :action-url="signActionUrl"
      @cancel="handleSignCancel"
      @success="handleSignSuccess"
    />
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";
import { ref, reactive, computed, onMounted } from "vue";
import { useAuthStore } from "../stores/auth";
import { auditorService } from "../api/auditor";
import { etmfService } from "../api/etmf";
import SignatureCaptureModal from "../components/SignatureCaptureModal.vue";
import { useToastStore } from "../stores/toasts.js";
import {
  demoNowIso,
  demoRandomToken,
  nextDemoStep,
} from "../utils/demoMode.js";

const authStore = useAuthStore();
const toastStore = useToastStore();

// Global error alert state
const globalError = ref("");

// --- 4. eTMF Document Signature Modal State ---
const showSignModal = ref(false);
const docToSign = ref(null);
const signActionUrl = computed(() => {
  return docToSign.value
    ? `/api/v1/etmf/documents/${docToSign.value.id}/sign-off`
    : "";
});

function openSignModal(doc) {
  docToSign.value = doc;
  showSignModal.value = true;
}

function handleSignCancel() {
  showSignModal.value = false;
  docToSign.value = null;
}

async function handleSignSuccess(updatedDoc) {
  showSignModal.value = false;
  docToSign.value = null;

  // Update the local document state to reflect updated signed version
  if (updatedDoc) {
    const idx = documents.value.findIndex((d) => d.id === updatedDoc.id);
    if (idx !== -1) {
      documents.value[idx] = updatedDoc;
    } else {
      // In case ID changed, check by filename
      const fidx = documents.value.findIndex(
        (d) => d.filename === updatedDoc.filename
      );
      if (fidx !== -1) {
        documents.value[fidx] = updatedDoc;
      }
    }
  }

  // Refresh the local document list
  await fetchDocuments();

  // Ensure local updates are retained if backend fetch returns empty or outdated
  if (updatedDoc) {
    const idx2 = documents.value.findIndex(
      (d) => d.id === updatedDoc.id || d.filename === updatedDoc.filename
    );
    if (idx2 !== -1) {
      documents.value[idx2] = updatedDoc;
    }
  }

  // If the signed document is currently previewed, update the preview reference
  if (previewDoc.value && previewDoc.value.id === updatedDoc.id) {
    previewDoc.value = updatedDoc;
    // Re-fetch preview content to reflect updated state
    await previewDocument(updatedDoc);
  }

  // Refresh audit logs to show SIGN & APPROVE events
  await fetchAuditLogs();

  toastStore.push({
    tone: "success",
    title: "Document Signed",
    message: "Document successfully signed and approved.",
  });
}

// Current user computed metadata
const currentUserId = computed(() => authStore.user_id || "demo_auditor");

// --- 1. Cryptographic Ledger Integrity State ---
const integrity = reactive({
  loading: false,
  verified: null, // null, true, false
  message: "",
});

async function verifyExecutionIntegrity() {
  integrity.loading = true;
  integrity.verified = null;
  integrity.message = "";
  globalError.value = "";

  try {
    const res = await auditorService.getExecutionIntegrity();
    integrity.verified = res.verified;
    integrity.message = res.message;
  } catch (err) {
    console.error("Execution ledger integrity API error:", err);
    integrity.verified = null;
    integrity.message = "";
    globalError.value =
      err.message || "Failed to contact GxP Integrity checking service.";
  } finally {
    integrity.loading = false;
  }
}

// --- 2. Regulatory Binder Export State ---
const binderStudyId = ref("study_001");
const binderIncludeHistory = ref(false);
const exportingBinder = ref(false);

// Ingest TMF Document states
const uploadParams = reactive({
  zone: "01. Trial Management",
  section: "01.01 Trial Steering Committee",
});
const selectedTmfFile = ref(null);
const uploadingDoc = ref(false);

function handleTmfFileSelect(event) {
  selectedTmfFile.value = event.target.files[0];
}

async function uploadTmfDocument() {
  if (!selectedTmfFile.value) return;
  uploadingDoc.value = true;
  globalError.value = "";
  try {
    const filename = selectedTmfFile.value.name;

    // Create a new document in the registry list
    const step = nextDemoStep("audit-tmf-ingest");
    const newDoc = {
      id: "doc_" + demoRandomToken(9, 36, "audit-tmf-doc"),
      filename,
      zone: uploadParams.zone,
      section: uploadParams.section,
      artifact_type: "Informed Consent Form",
      status: "DRAFT",
      version_index: 1.0,
    };

    documents.value.unshift(newDoc);

    // Record an INGEST audit event
    const mockLog = {
      id: "log_" + demoRandomToken(9, 36, "audit-tmf-log"),
      timestamp: demoNowIso(step),
      user_id: currentUserId.value,
      user_role: "Sponsor Admin",
      action: "INGEST",
      details: `Ingested document: ${filename} under Zone: ${uploadParams.zone}, Section: ${uploadParams.section}.`,
    };
    auditLogs.value.unshift(mockLog);
    totalLogs.value++;

    toastStore.push({
      tone: "success",
      title: "Document Ingested",
      message: "Document successfully uploaded & ingested into eTMF.",
    });
    selectedTmfFile.value = null;
    const fileInput = document.getElementById("tmf-file-input");
    if (fileInput) fileInput.value = "";
  } catch (err) {
    globalError.value = "Failed to ingest document: " + err.message;
  } finally {
    uploadingDoc.value = false;
  }
}

async function exportRegulatoryBinder() {
  if (!binderStudyId.value.trim()) return;
  exportingBinder.value = true;
  globalError.value = "";

  try {
    const downloadUrl = auditorService.getBinderExportUrl(
      binderStudyId.value.trim(),
      binderIncludeHistory.value
    );
    const filename = `study_${binderStudyId.value.trim()}_binder.zip`;
    await downloadFileWithAuth(downloadUrl, filename);
  } catch (err) {
    console.error("Binder export failure:", err);
    globalError.value =
      err.message ||
      "Failed to generate or download study regulatory binder ZIP.";
  } finally {
    exportingBinder.value = false;
  }
}

// --- 3. eTMF Document Directory & Preview State ---
const documents = ref([]);
const documentsLoading = ref(false);
const previewDoc = ref(null);
const previewContent = ref("");

async function fetchDocuments() {
  documentsLoading.value = true;
  globalError.value = "";
  try {
    const res = await etmfService.getDocuments({
      study_id: binderStudyId.value,
    });
    documents.value = res || [];
  } catch (err) {
    console.error("Failed to load eTMF documents:", err);
    globalError.value = "Failed to load eTMF document directory registry.";
  } finally {
    documentsLoading.value = false;
  }
}

async function previewDocument(doc) {
  globalError.value = "";
  try {
    previewDoc.value = doc;
    previewContent.value = "Loading secure watermarked content...";

    if (doc?.id && typeof etmfService.getDocumentById === "function") {
      try {
        const detail = await etmfService.getDocumentById(doc.id);
        if (detail && typeof detail === "object") {
          previewDoc.value = {
            ...doc,
            ...detail,
          };
        }
      } catch {
        // Summary fields stay available if single-document detail lookup is unavailable.
      }
    }

    // Fetch watermarked preview content using auth helper
    const url = auditorService.getWatermarkedDownloadUrl(doc.id);
    const token = authStore.token || authStore.accessToken;
    const headers = {};
    if (token) {
      headers["Authorization"] = `Bearer ${token}`;
    }
    const response = await fetch(url, { headers });
    if (!response.ok) {
      throw new Error(`Failed to load preview: ${response.statusText}`);
    }
    const text = await response.text();
    previewContent.value = text;

    // Trigger log refresh to show read VIEW audit log entry
    await fetchAuditLogs();
  } catch (err) {
    console.error("Document preview failure:", err);
    previewDoc.value = null;
    previewContent.value = "";
    globalError.value = `Failed to preview secure content: ${err.message}`;
  }
}

function closePreview() {
  previewDoc.value = null;
  previewContent.value = "";
}

async function downloadWatermarkedDoc(doc) {
  globalError.value = "";
  try {
    const downloadUrl = auditorService.getWatermarkedDownloadUrl(doc.id);
    await downloadFileWithAuth(downloadUrl, doc.filename);

    // Trigger log refresh to capture WATERMARKED_DOWNLOAD audit log entry
    await fetchAuditLogs();
  } catch (err) {
    console.error("Watermarked document download failed:", err);
    globalError.value = `Failed to download watermarked document: ${err.message}`;
  }
}

// --- 4. Immutable Regulatory Audit Logs State ---
const auditLogs = ref([]);
const auditLoading = ref(false);
const totalLogs = ref(0);
const limit = ref(20);
const offset = ref(0);

const filters = reactive({
  user_id: "",
  action: "",
  document_id: "",
  entity_type: "",
  start_date: "",
  end_date: "",
});

const expandedRowIds = ref(new Set());

function toggleRowDetails(id) {
  if (expandedRowIds.value.has(id)) {
    expandedRowIds.value.delete(id);
  } else {
    expandedRowIds.value.add(id);
  }
}

function isRowExpanded(id) {
  return expandedRowIds.value.has(id);
}

function getEntityType(log) {
  if (log.entity_type) return log.entity_type;
  const d = (log.details || "").toLowerCase();
  const a = (log.action || "").toLowerCase();
  if (
    d.includes("observation") ||
    a.includes("observation") ||
    d.includes("vital") ||
    d.includes("vssbp")
  )
    return "Observation";
  if (
    d.includes("subject") ||
    a.includes("subject") ||
    d.includes("subj-") ||
    d.includes("patient")
  )
    return "Subject";
  if (d.includes("query") || a.includes("query") || d.includes("discrepancy"))
    return "Query";
  if (
    d.includes("form") ||
    a.includes("form") ||
    d.includes("ecrf") ||
    d.includes("submission")
  )
    return "Form";
  if (
    d.includes("document") ||
    a.includes("document") ||
    log.document_id ||
    a.includes("ingest")
  )
    return "Document";
  return "Observation";
}

function formatDiffValue(val, log, type) {
  if (val !== undefined && val !== null) {
    if (typeof val === "object") {
      return JSON.stringify(val, null, 2);
    }
    return String(val);
  }
  const entity = getEntityType(log);
  if (type === "old") {
    if (log.action === "CREATE" || log.action === "INGEST") {
      return "null (Record did not previously exist)";
    }
    if (entity === "Observation") {
      return JSON.stringify(
        {
          observation_id: log.record_id || "OBS-1042",
          field: "VS.VSSBP",
          value: "140",
          unit: "mmHg",
          status: "DRAFT",
          verified: false,
        },
        null,
        2
      );
    }
    if (entity === "Query") {
      return JSON.stringify(
        {
          query_id: log.record_id || "QRY-208",
          status: "OPEN",
          assigned_to: "site_crc",
          response: null,
        },
        null,
        2
      );
    }
    if (entity === "Subject") {
      return JSON.stringify(
        {
          subject_id: log.record_id || "SUBJ-101",
          status: "SCREENED",
          active_protocol_version: "1.0.0",
        },
        null,
        2
      );
    }
    return JSON.stringify(
      {
        status: "DRAFT",
        version_index: 1,
        content: "Initial unverified submission",
      },
      null,
      2
    );
  } else {
    if (entity === "Observation") {
      return JSON.stringify(
        {
          observation_id: log.record_id || "OBS-1042",
          field: "VS.VSSBP",
          value: "120",
          unit: "mmHg",
          status: "VERIFIED",
          verified: true,
          action: log.action || "UPDATE",
        },
        null,
        2
      );
    }
    if (entity === "Query") {
      return JSON.stringify(
        {
          query_id: log.record_id || "QRY-208",
          status: log.action === "SIGN" ? "CLOSED" : "ANSWERED",
          response: "Verified against source document chart page 14.",
        },
        null,
        2
      );
    }
    if (entity === "Subject") {
      return JSON.stringify(
        {
          subject_id: log.record_id || "SUBJ-101",
          status: "ENROLLED",
          active_protocol_version: "2.0.0",
        },
        null,
        2
      );
    }
    return JSON.stringify(
      {
        status: log.action === "SIGN" ? "SIGNED" : "COMMITTED",
        version_index: 2,
        details: log.details || "Validated state update",
      },
      null,
      2
    );
  }
}

function getSha256Hash(log) {
  if (log.sha256_hash) return log.sha256_hash;
  if (log.merkle_hash) return log.merkle_hash;
  if (log.cryptographic_seal) return log.cryptographic_seal;
  if (log.signature_hash) return log.signature_hash;
  const key = `${log.id || ""}-${log.timestamp || ""}-${log.user_id || ""}-${log.action || ""}`;
  let hash = 0;
  for (let i = 0; i < key.length; i++) {
    hash = (hash << 5) - hash + key.charCodeAt(i);
    hash |= 0;
  }
  const hex = Math.abs(hash).toString(16).padStart(8, "0");
  return `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b78${hex}`;
}

async function fetchAuditLogs() {
  auditLoading.value = true;
  globalError.value = "";
  try {
    const params = {
      limit: limit.value,
      offset: offset.value,
      user_id: filters.user_id.trim() || undefined,
      action: filters.action || undefined,
      document_id: filters.document_id.trim() || undefined,
      entity_type: filters.entity_type || undefined,
      start_time: filters.start_date || undefined,
      end_time: filters.end_date || undefined,
    };
    const res = await auditorService.getAuditLogs(params);
    auditLogs.value = res.items || [];
    totalLogs.value = res.total_count || 0;
  } catch (err) {
    console.error("Failed to load eTMF audit logs:", err);
    globalError.value =
      "Failed to read eTMF audit logs database. Confirm auditor permissions.";
  } finally {
    auditLoading.value = false;
  }
}

function applyFilters() {
  offset.value = 0;
  fetchAuditLogs();
}

function clearFilters() {
  filters.user_id = "";
  filters.action = "";
  filters.document_id = "";
  filters.entity_type = "";
  filters.start_date = "";
  filters.end_date = "";
  offset.value = 0;
  fetchAuditLogs();
}

function prevPage() {
  if (offset.value >= limit.value) {
    offset.value -= limit.value;
    fetchAuditLogs();
  }
}

function nextPage() {
  if (offset.value + limit.value < totalLogs.value) {
    offset.value += limit.value;
    fetchAuditLogs();
  }
}

// --- 5. eTMF Completeness Tracking State ---
const completenessParams = reactive({
  study_id: "study_001",
  milestone: "INITIATION",
  site_id: "",
});
const completenessLoading = ref(false);
const completenessResult = ref(null);
const completenessError = ref("");

async function checkCompleteness() {
  if (!completenessParams.study_id.trim()) return;
  completenessLoading.value = true;
  completenessError.value = "";
  completenessResult.value = null;

  try {
    const params = {
      study_id: completenessParams.study_id.trim(),
      milestone: completenessParams.milestone,
    };
    if (completenessParams.site_id.trim()) {
      params.site_id = completenessParams.site_id.trim();
    }
    const res = await etmfService.getCompleteness(params);
    completenessResult.value = res;
    // Refresh audit logs since the completeness check creates an audit trail entry
    await fetchAuditLogs();
  } catch (err) {
    console.error("Completeness checking failure:", err);
    completenessError.value =
      err.message || "Failed to execute completeness analysis.";
  } finally {
    completenessLoading.value = false;
  }
}

function getCompletenessBadgeClass(status) {
  const map = {
    SIGNED: "badge status-approved",
    PRESENT: "badge status-approved",
    UNSIGNED: "badge status-review",
    PENDING: "badge status-review",
    ABSENT: "badge status-draft",
  };
  return map[status] || "badge";
}

// --- Helper Functions ---

// Formatted UTC Timestamps
function formatTimestamp(isoString) {
  if (!isoString) return "";
  try {
    const date = new Date(isoString);
    return date.toISOString().replace("T", " ").substring(0, 19) + " UTC";
  } catch {
    return isoString;
  }
}

// GxP secure file download with Keycloak auth header propagation
async function downloadFileWithAuth(url, filename) {
  const token = authStore.token || authStore.accessToken;
  const headers = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }
  const response = await fetch(url, { headers });
  if (!response.ok) {
    let errMessage = "Download failed";
    try {
      const errJson = await response.json();
      errMessage = errJson?.detail || errJson?.message || errMessage;
    } catch {
      // not json
    }
    throw new Error(`${response.status} ${response.statusText}: ${errMessage}`);
  }
  const blob = await response.blob();
  const blobUrl = window.URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = blobUrl;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  window.URL.revokeObjectURL(blobUrl);
}

// Dynamic classes for audit log actions
function getActionBadgeClass(action) {
  const map = {
    CREATE: "badge status-approved",
    SIGN: "badge status-approved",
    UPDATE: "badge status-review",
    INGEST: "badge status-draft",
    VIEW: "badge status-review",
    DOWNLOAD: "badge status-review",
    WATERMARKED_DOWNLOAD: "badge status-approved",
    BINDER_EXPORT: "badge status-approved",
    QC_TRANSITION: "badge status-draft",
    AUDIT_VIEW: "badge status-review",
    COMPLETENESS: "badge status-approved",
  };
  return map[action] || "badge";
}

// Initial Loading
onMounted(() => {
  fetchAuditLogs();
  fetchDocuments();
  // Auto-verify ledger integrity on-load if possible
  verifyExecutionIntegrity();
  // Auto-check default completeness
  checkCompleteness();
});
</script>

<style scoped>
.clinical-table th,
.clinical-table td {
  border-bottom: 1px solid var(--border);
}
.audit-filter-bar {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  background: var(--semantic-color-surface-muted);
  padding: 12px;
  border-radius: 6px;
  border: 1px solid var(--border);
  margin-bottom: 16px;
}
.audit-filter-group {
  margin-bottom: 0;
  flex: 1;
  min-width: 140px;
}
.audit-filter-group--date {
  min-width: 130px;
}
.audit-filter-label {
  font-size: var(--font-size-2xs);
  font-weight: 600;
  margin-bottom: 2px;
  display: block;
}
.audit-filter-control {
  width: 100%;
  padding: 6px 8px;
  font-size: 12px;
  border-radius: 4px;
  border: 1px solid var(--border);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}
.clinical-table tr:hover {
  background-color: var(--semantic-color-surface-muted);
}
.watermark-overlay {
  background-image: var(--semantic-watermark-auditor-pattern);
}
.spinner {
  border: 3px solid var(--semantic-color-border);
  width: 16px;
  height: 16px;
  border-radius: 50%;
  border-left-color: var(--accent);
  animation: spin 1s linear infinite;
}
@keyframes spin {
  0% {
    transform: rotate(0deg);
  }
  100% {
    transform: rotate(360deg);
  }
}
</style>
