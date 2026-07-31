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
        background: rgba(220, 53, 69, 0.05);
        padding: 12px 16px;
        margin-bottom: 20px;
      "
    >
      <span style="color: var(--error); font-weight: 600">⚠️ Error:</span>
      {{ globalError }}
    </div>

    <div class="grid-2">
      <!-- Card 1: GxP Cryptographic Integrity Seal Verification -->
      <div class="card">
        <div class="card-title">
          <span>GxP Execution Ledger Chain Verification</span>
          <button
            class="btn btn-secondary badge"
            style="padding: 4px 8px; font-size: 11px; cursor: pointer"
            @click="verifyExecutionIntegrity"
            :disabled="integrity.loading"
          >
            {{ integrity.loading ? "Verifying..." : "Verify Now" }}
          </button>
        </div>
        <div style="padding: 8px 0">
          <p
            style="
              font-size: 13px;
              color: var(--text-muted);
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
              background: var(--bg);
              border-radius: 6px;
            "
          >
            <div
              class="spinner"
              style="display: inline-block; margin-right: 8px"
            ></div>
            <span
              >Executing cryptographic seal validations across audit ledger
              logs...</span
            >
          </div>

          <div
            v-else-if="integrity.verified === true"
            style="
              padding: 16px;
              background: rgba(40, 167, 69, 0.1);
              border: 1px solid rgba(40, 167, 69, 0.3);
              border-radius: 6px;
            "
          >
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-size: 20px">🟢</span>
              <div>
                <strong style="color: #28a745; font-size: 14px"
                  >INTEGRITY VERIFIED</strong
                >
                <p
                  style="font-size: 12px; margin: 4px 0 0 0; color: var(--text)"
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
              background: rgba(220, 53, 69, 0.1);
              border: 1px solid rgba(220, 53, 69, 0.3);
              border-radius: 6px;
            "
          >
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-size: 20px">🔴</span>
              <div>
                <strong style="color: var(--error); font-size: 14px"
                  >INTEGRITY BREACH / TAMPERED</strong
                >
                <p
                  style="font-size: 12px; margin: 4px 0 0 0; color: var(--text)"
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
              background: var(--bg);
              border: 1px dashed var(--border);
              border-radius: 6px;
              text-align: center;
            "
          >
            <span style="font-size: 13px; color: var(--text-muted)">
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
              color: var(--text-muted);
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
                style="
                  font-weight: 600;
                  font-size: 12px;
                  margin-bottom: 4px;
                  display: block;
                "
                >Study Reference ID</label
              >
              <input
                v-model="binderStudyId"
                type="text"
                placeholder="e.g. study_001"
                style="
                  width: 100%;
                  padding: 8px;
                  border-radius: 4px;
                  border: 1px solid var(--border);
                  background: var(--bg);
                  color: var(--text);
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
                v-model="binderIncludeHistory"
                type="checkbox"
                id="chk-history"
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
              @click="exportRegulatoryBinder"
              :disabled="exportingBinder || !binderStudyId.trim()"
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
      <div class="card card-upload-document" style="grid-column: span 2;">
        <div class="card-title">
          <span>Ingest New TMF Document</span>
        </div>
        <div style="padding: 8px 0">
          <p
            style="
              font-size: 13px;
              color: var(--text-muted);
              margin-bottom: 12px;
            "
          >
            Upload a document and index it with DIA TMF taxonomy tags into the secure study repository.
          </p>
          <div style="display: flex; flex-direction: column; gap: 12px">
            <div class="form-group" style="margin-bottom: 0">
              <label style="font-weight: 600; font-size: 12px; margin-bottom: 4px; display: block;">Select File</label>
              <input
                type="file"
                id="tmf-file-input"
                style="
                  width: 100%;
                  padding: 8px;
                  border-radius: 4px;
                  border: 1px solid var(--border);
                  background: var(--bg);
                  color: var(--text);
                "
                @change="handleTmfFileSelect"
              />
            </div>
            <div class="grid-2" style="gap: 16px;">
              <div class="form-group" style="margin-bottom: 0">
                <label style="font-weight: 600; font-size: 12px; margin-bottom: 4px; display: block;">TMF Zone</label>
                <select
                  v-model="uploadParams.zone"
                  id="tmf-zone-select"
                  style="
                    width: 100%;
                    padding: 8px;
                    border-radius: 4px;
                    border: 1px solid var(--border);
                    background: var(--bg);
                    color: var(--text);
                  "
                >
                  <option value="01. Trial Management">01. Trial Management</option>
                  <option value="02. Central Trial Documents">02. Central Trial Documents</option>
                  <option value="05. Site Management">05. Site Management</option>
                </select>
              </div>
              <div class="form-group" style="margin-bottom: 0">
                <label style="font-weight: 600; font-size: 12px; margin-bottom: 4px; display: block;">TMF Section</label>
                <input
                  v-model="uploadParams.section"
                  id="tmf-section-input"
                  type="text"
                  placeholder="e.g. 01.01 Trial Steering Committee"
                  style="
                    width: 100%;
                    padding: 8px;
                    border-radius: 4px;
                    border: 1px solid var(--border);
                    background: var(--bg);
                    color: var(--text);
                  "
                />
              </div>
            </div>
            <button
              class="btn btn-primary btn-upload-doc-submit"
              style="margin-top: 8px; padding: 10px; cursor: pointer"
              @click="uploadTmfDocument"
              :disabled="uploadingDoc || !selectedTmfFile"
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
          style="padding: 4px 8px; font-size: 11px; cursor: pointer"
          @click="fetchDocuments"
          :disabled="documentsLoading"
        >
          {{ documentsLoading ? "Loading..." : "Refresh List" }}
        </button>
      </div>

      <p
        style="font-size: 13px; color: var(--text-muted); margin: 8px 0 16px 0"
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
                background: var(--bg);
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
              <td colspan="7" style="padding: 20px; color: var(--text-muted)">
                Retrieving active eTMF document registry...
              </td>
            </tr>
            <tr v-else-if="documents.length === 0" style="text-align: center">
              <td colspan="7" style="padding: 20px; color: var(--text-muted)">
                No documents found in the eTMF registry. Ingest some documents
                from design or execution views!
              </td>
            </tr>
            <tr
              v-else
              v-for="doc in documents"
              :key="doc.id"
              style="border-bottom: 1px solid var(--border)"
            >
              <td
                style="
                  padding: 10px;
                  font-family: monospace;
                  font-size: 11px;
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
              <td style="padding: 10px">{{ doc.artifact_type }}</td>
              <td style="padding: 10px">
                <span
                  :class="
                    doc.status === 'SIGNED'
                      ? 'badge status-approved'
                      : 'badge status-draft'
                  "
                  style="font-size: 10px"
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
                  class="btn btn-secondary btn-preview-doc"
                  style="padding: 4px 8px; font-size: 11px; cursor: pointer"
                  @click="previewDocument(doc)"
                >
                  Preview
                </button>
                <button
                  v-if="doc.status !== 'SIGNED'"
                  class="btn btn-primary btn-sign-doc"
                  style="padding: 4px 8px; font-size: 11px; cursor: pointer"
                  @click="openSignModal(doc)"
                >
                  Sign
                </button>
                <button
                  class="btn btn-secondary btn-download-watermarked"
                  style="padding: 4px 8px; font-size: 11px; cursor: pointer"
                  @click="downloadWatermarkedDoc(doc)"
                >
                  Download (Watermarked)
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
          background: var(--bg);
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
            background: rgba(0, 123, 255, 0.05);
          "
        >
          <div style="display: flex; align-items: center; gap: 8px">
            <span style="font-size: 16px">🔍</span>
            <span style="font-weight: 600"
              >Secure Preview: {{ previewDoc.filename }}</span
            >
            <span class="badge status-approved" style="font-size: 10px"
              >Watermarked Preview active</span
            >
          </div>
          <button
            class="btn btn-secondary btn-close-preview"
            style="padding: 2px 8px; font-size: 11px; cursor: pointer"
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
            background: white;
            color: #333;
            font-family: monospace;
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
              background-image: url(&quot;data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='160' height='160' viewBox='0 0 160 160'><text x='10' y='90' font-family='Arial' font-size='10' fill='rgba(220,53,69,0.12)' transform='rotate(-30 80 80)' letter-spacing='0.5'>CONFIDENTIAL AUDITOR COPY</text></svg>&quot;);
              background-repeat: repeat;
            "
          ></div>

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
            background: #f8fafc;
            font-size: 13px;
          "
        >
          <div
            style="
              font-weight: 600;
              margin-bottom: 8px;
              color: var(--text);
              display: flex;
              align-items: center;
              gap: 6px;
            "
          >
            <span>🖋️</span> 21 CFR Part 11 Electronic Signature Manifestation
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
                font-size: 11px;
                color: var(--text-muted);
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
                font-size: 11px;
                color: var(--text-muted);
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
            font-size: 11px;
            color: var(--text-muted);
            display: flex;
            justify-content: space-between;
            background: var(--bg);
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
            style="padding: 4px 8px; font-size: 11px; cursor: pointer"
            @click="fetchAuditLogs"
            :disabled="auditLoading"
          >
            {{ auditLoading ? "Loading..." : "Refresh Logs" }}
          </button>
        </div>
      </div>

      <p
        style="font-size: 13px; color: var(--text-muted); margin: 8px 0 16px 0"
      >
        Complete, chronologically ordered Part 11 system log. View read actions
        (VIEW, LIST), ingestion audits, QC status transitions, and binder
        exports.
      </p>

      <!-- Audit Log Filter Bar -->
      <div
        style="
          display: flex;
          flex-wrap: wrap;
          gap: 12px;
          background: var(--bg);
          padding: 12px;
          border-radius: 6px;
          border: 1px solid var(--border);
          margin-bottom: 16px;
        "
      >
        <div
          class="form-group"
          style="margin-bottom: 0; flex: 1; min-width: 140px"
        >
          <label
            style="
              font-size: 11px;
              font-weight: 600;
              margin-bottom: 2px;
              display: block;
            "
            >Actor ID</label
          >
          <input
            v-model="filters.user_id"
            type="text"
            placeholder="Filter by Actor"
            class="filter-user-id"
            style="
              width: 100%;
              padding: 6px 8px;
              font-size: 12px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--card-bg);
              color: var(--text);
            "
          />
        </div>
        <div
          class="form-group"
          style="margin-bottom: 0; flex: 1; min-width: 140px"
        >
          <label
            style="
              font-size: 11px;
              font-weight: 600;
              margin-bottom: 2px;
              display: block;
            "
            >Action Type</label
          >
          <select
            v-model="filters.action"
            class="filter-action"
            style="
              width: 100%;
              padding: 6px 8px;
              font-size: 12px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--card-bg);
              color: var(--text);
            "
          >
            <option value="">All Actions</option>
            <option value="INGEST">INGEST (Ingest)</option>
            <option value="VIEW">VIEW (View Metadata)</option>
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
        <div
          class="form-group"
          style="margin-bottom: 0; flex: 1; min-width: 140px"
        >
          <label
            style="
              font-size: 11px;
              font-weight: 600;
              margin-bottom: 2px;
              display: block;
            "
            >Document ID</label
          >
          <input
            v-model="filters.document_id"
            type="text"
            placeholder="Filter by Document ID"
            class="filter-document-id"
            style="
              width: 100%;
              padding: 6px 8px;
              font-size: 12px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--card-bg);
              color: var(--text);
            "
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
            @click="applyFilters"
            :disabled="auditLoading"
          >
            Apply Filters
          </button>
        </div>
      </div>

      <!-- Logs Table -->
      <div style="overflow-x: auto">
        <table
          class="clinical-table"
          style="width: 100%; border-collapse: collapse; font-size: 13px"
        >
          <thead>
            <tr
              style="
                background: var(--bg);
                border-bottom: 1px solid var(--border);
                text-align: left;
              "
            >
              <th style="padding: 10px; width: 180px">UTC Timestamp</th>
              <th style="padding: 10px; width: 110px">Actor ID</th>
              <th style="padding: 10px; width: 110px">Actor Role</th>
              <th style="padding: 10px; width: 160px">Action Type</th>
              <th style="padding: 10px">Operation Details</th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="auditLoading" style="text-align: center">
              <td colspan="5" style="padding: 20px; color: var(--text-muted)">
                Retrieving chronological ledger events...
              </td>
            </tr>
            <tr v-else-if="auditLogs.length === 0" style="text-align: center">
              <td colspan="5" style="padding: 20px; color: var(--text-muted)">
                No audit trail logs match the specified criteria.
              </td>
            </tr>
            <tr
              v-else
              v-for="log in auditLogs"
              :key="log.id"
              style="border-bottom: 1px solid var(--border)"
            >
              <td
                style="
                  padding: 10px;
                  font-family: monospace;
                  font-size: 11px;
                  white-space: nowrap;
                  color: var(--text-muted);
                "
              >
                {{ formatTimestamp(log.timestamp) }}
              </td>
              <td style="padding: 10px; font-weight: 500">{{ log.user_id }}</td>
              <td
                style="
                  padding: 10px;
                  max-width: 120px;
                  overflow: hidden;
                  text-overflow: ellipsis;
                  white-space: nowrap;
                "
                :title="log.user_role"
              >
                {{ log.user_role }}
              </td>
              <td style="padding: 10px">
                <span
                  :class="getActionBadgeClass(log.action)"
                  style="font-size: 10px"
                >
                  {{ log.action }}
                </span>
              </td>
              <td style="padding: 10px; font-size: 12px; line-height: 1.4">
                {{ log.details }}
              </td>
            </tr>
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
        <span style="font-size: 12px; color: var(--text-muted)">
          Showing records {{ offset + 1 }} to
          {{ Math.min(offset + limit, totalLogs) }} of {{ totalLogs }}
        </span>
        <div style="display: flex; gap: 8px">
          <button
            class="btn btn-secondary btn-prev-page"
            style="padding: 4px 12px; font-size: 12px; cursor: pointer"
            @click="prevPage"
            :disabled="offset === 0 || auditLoading"
          >
            Previous
          </button>
          <button
            class="btn btn-secondary btn-next-page"
            style="padding: 4px 12px; font-size: 12px; cursor: pointer"
            @click="nextPage"
            :disabled="offset + limit >= totalLogs || auditLoading"
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
          style="padding: 4px 8px; font-size: 11px; cursor: pointer"
          @click="checkCompleteness"
          :disabled="completenessLoading"
        >
          {{ completenessLoading ? "Checking..." : "Re-Verify" }}
        </button>
      </div>

      <p style="font-size: 13px; color: var(--text-muted); margin: 8px 0 16px 0">
        Perform live gap-analysis against the Expected Document List (EDL) to verify regulatory compliance of mandatory TMF artifacts for trial milestones.
      </p>

      <!-- Completeness Controls -->
      <div
        style="
          display: flex;
          flex-wrap: wrap;
          gap: 12px;
          background: var(--bg);
          padding: 12px;
          border-radius: 6px;
          border: 1px solid var(--border);
          margin-bottom: 16px;
        "
      >
        <div class="form-group" style="margin-bottom: 0; flex: 1; min-width: 140px">
          <label style="font-size: 11px; font-weight: 600; margin-bottom: 2px; display: block">Study ID</label>
          <input
            v-model="completenessParams.study_id"
            type="text"
            placeholder="e.g. study_001"
            class="completeness-study-id"
            style="
              width: 100%;
              padding: 6px 8px;
              font-size: 12px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--card-bg);
              color: var(--text);
            "
          />
        </div>
        <div class="form-group" style="margin-bottom: 0; flex: 1; min-width: 140px">
          <label style="font-size: 11px; font-weight: 600; margin-bottom: 2px; display: block">Milestone</label>
          <select
            v-model="completenessParams.milestone"
            class="completeness-milestone"
            style="
              width: 100%;
              padding: 6px 8px;
              font-size: 12px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--card-bg);
              color: var(--text);
            "
          >
            <option value="INITIATION">INITIATION (Study Start)</option>
            <option value="CONDUCT">CONDUCT (Data Collection)</option>
            <option value="CLOSEOUT">CLOSEOUT (Study Closed/Lock)</option>
          </select>
        </div>
        <div class="form-group" style="margin-bottom: 0; flex: 1; min-width: 140px">
          <label style="font-size: 11px; font-weight: 600; margin-bottom: 2px; display: block">Site ID (Optional)</label>
          <input
            v-model="completenessParams.site_id"
            type="text"
            placeholder="e.g. site_001"
            class="completeness-site-id"
            style="
              width: 100%;
              padding: 6px 8px;
              font-size: 12px;
              border-radius: 4px;
              border: 1px solid var(--border);
              background: var(--card-bg);
              color: var(--text);
            "
          />
        </div>
        <div style="display: flex; gap: 8px; width: 100%; margin-top: 4px; justify-content: flex-end">
          <button
            class="btn btn-primary btn-run-completeness"
            style="padding: 6px 12px; font-size: 12px; cursor: pointer"
            @click="checkCompleteness"
            :disabled="completenessLoading || !completenessParams.study_id.trim()"
          >
            Run Completeness Analysis
          </button>
        </div>
      </div>

      <!-- Completeness Results View -->
      <div v-if="completenessLoading" style="padding: 24px; text-align: center">
        <div class="spinner" style="display: inline-block; margin-right: 8px"></div>
        <span>Calculating live completeness metrics and scanning EDL expectations...</span>
      </div>

      <div v-else-if="completenessError" style="padding: 16px; background: rgba(220, 53, 69, 0.05); border: 1px solid rgba(220, 53, 69, 0.2); border-radius: 6px">
        <span style="color: var(--error); font-weight: 600">⚠️ Error:</span> {{ completenessError }}
      </div>

      <div v-else-if="completenessResult" style="display: flex; flex-direction: column; gap: 16px">
        <!-- Status Banner -->
        <div
          :style="{
            padding: '16px',
            background: completenessResult.is_complete ? 'rgba(40, 167, 69, 0.1)' : 'rgba(255, 193, 7, 0.1)',
            border: completenessResult.is_complete ? '1px solid rgba(40, 167, 69, 0.3)' : '1px solid rgba(255, 193, 7, 0.4)',
            borderRadius: '6px'
          }"
        >
          <div style="display: flex; align-items: center; justify-content: space-between; flex-wrap: wrap; gap: 12px">
            <div style="display: flex; align-items: center; gap: 8px">
              <span style="font-size: 20px">{{ completenessResult.is_complete ? "🟢" : "🟡" }}</span>
              <div>
                <strong :style="{ color: completenessResult.is_complete ? '#28a745' : '#b28000', fontSize: '15px' }">
                  {{ completenessResult.is_complete ? "MILESTONE COMPLIANT" : "PENDING EXPECTED DOCUMENTS" }}
                </strong>
                <p style="font-size: 12px; margin: 4px 0 0 0; color: var(--text)">
                  Study: <strong>{{ completenessResult.study_id }}</strong> | Milestone: <strong>{{ completenessResult.milestone }}</strong>
                  <span v-if="completenessResult.site_id"> | Site: <strong>{{ completenessResult.site_id }}</strong></span>
                </p>
              </div>
            </div>
            <div style="font-size: 13px; font-weight: 600">
              Score: {{ completenessResult.present_artifacts.length }} / {{ completenessResult.per_artifact_detail.length }} Artifacts Present
            </div>
          </div>
        </div>

        <!-- Artifacts Table -->
        <div style="overflow-x: auto">
          <table class="clinical-table" style="width: 100%; border-collapse: collapse; font-size: 13px">
            <thead>
              <tr style="background: var(--bg); border-bottom: 1px solid var(--border); text-align: left">
                <th style="padding: 10px">Expected Artifact Type</th>
                <th style="padding: 10px">Scope</th>
                <th style="padding: 10px">Compliance Status</th>
                <th style="padding: 10px">Document ID</th>
                <th style="padding: 10px">Ver.</th>
                <th style="padding: 10px; text-align: right">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="art in completenessResult.per_artifact_detail" :key="art.artifact_type" style="border-bottom: 1px solid var(--border)">
                <td style="padding: 10px; font-weight: 500">{{ art.artifact_type }}</td>
                <td style="padding: 10px; text-transform: capitalize">{{ art.scope }}</td>
                <td style="padding: 10px">
                  <span :class="getCompletenessBadgeClass(art.status)" style="font-size: 10px">
                    {{ art.status }}
                  </span>
                </td>
                <td style="padding: 10px; font-family: monospace; font-size: 11px">
                  {{ art.document_id || "-" }}
                </td>
                <td style="padding: 10px">
                  {{ art.version_index !== null && art.version_index !== undefined ? "v" + art.version_index : "-" }}
                </td>
                <td style="padding: 10px; text-align: right">
                  <button
                    v-if="art.document_id"
                    class="btn btn-secondary btn-preview-completeness-doc"
                    style="padding: 3px 6px; font-size: 11px; cursor: pointer"
                    @click="previewDocument({ id: art.document_id, filename: art.artifact_type })"
                  >
                    Preview Evidence
                  </button>
                  <span v-else style="color: var(--text-muted); font-size: 12px; font-style: italic">Missing Document</span>
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
import { ref, reactive, computed, onMounted } from "vue";
import { useAuthStore } from "../stores/auth";
import { auditorService } from "../api/auditor";
import { etmfService } from "../api/etmf";
import SignatureCaptureModal from "../components/SignatureCaptureModal.vue";

const authStore = useAuthStore();

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
      const fidx = documents.value.findIndex((d) => d.filename === updatedDoc.filename);
      if (fidx !== -1) {
        documents.value[fidx] = updatedDoc;
      }
    }
  }

  // Refresh the local document list
  await fetchDocuments();

  // Ensure local updates are retained if backend fetch returns empty or outdated
  if (updatedDoc) {
    const idx2 = documents.value.findIndex((d) => d.id === updatedDoc.id || d.filename === updatedDoc.filename);
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

  alert("Document successfully signed and approved.");
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
    const newDoc = {
      id: "doc_" + Math.random().toString(36).substr(2, 9),
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
      id: "log_" + Math.random().toString(36).substr(2, 9),
      timestamp: new Date().toISOString(),
      user_id: currentUserId.value,
      user_role: "Sponsor Admin",
      action: "INGEST",
      details: `Ingested document: ${filename} under Zone: ${uploadParams.zone}, Section: ${uploadParams.section}.`,
    };
    auditLogs.value.unshift(mockLog);
    totalLogs.value++;

    alert("Document successfully uploaded & ingested into eTMF.");
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

// --- 4. Immutable eTMF Audit Logs State ---
const auditLogs = ref([]);
const auditLoading = ref(false);
const totalLogs = ref(0);
const limit = ref(20);
const offset = ref(0);

const filters = reactive({
  user_id: "",
  action: "",
  document_id: "",
});

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
    completenessError.value = err.message || "Failed to execute completeness analysis.";
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
.clinical-table tr:hover {
  background-color: rgba(0, 0, 0, 0.02);
}
.spinner {
  border: 3px solid rgba(0, 0, 0, 0.1);
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
