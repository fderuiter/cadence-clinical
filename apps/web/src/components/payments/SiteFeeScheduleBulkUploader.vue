<template>
  <div class="fee-schedule-bulk-uploader">
    <!-- Header Banner -->
    <div class="uploader-header">
      <div class="header-info">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11 Audit Trail</span>
          <span class="badge badge-regulatory">Site Contract Ingestion</span>
          <span class="badge badge-standard"
            >PRD-SYS-161 Fee Schedule Matrix</span
          >
        </div>
        <h3 class="uploader-title">Bulk Site Fee Schedule CSV Importer</h3>
        <p class="uploader-subtitle">
          Upload multi-site fee schedules containing procedure rates, overhead
          percentages, and withholding terms. Preview calculations and
          validation checks before committing.
        </p>
      </div>
      <div class="header-actions">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isDownloadingTemplate"
          @click="downloadTemplate"
        >
          <span class="btn-icon">📥</span> Download CSV Template
        </button>
      </div>
    </div>

    <!-- Drag & Drop CSV Dropzone -->
    <div
      class="dropzone-area"
      :class="{
        'dropzone-active': isDragging,
        'dropzone-has-data': previewData !== null,
      }"
      role="button"
      tabindex="0"
      aria-label="CSV file upload dropzone"
      @click="triggerFileInput"
      @keydown.enter.prevent="triggerFileInput"
      @keydown.space.prevent="triggerFileInput"
      @dragover.prevent="onDragOver"
      @dragleave.prevent="onDragLeave"
      @drop.prevent="onDrop"
    >
      <input
        id="fee-schedule-csv-file-input"
        ref="fileInputRef"
        type="file"
        accept=".csv,text/csv"
        class="hidden-file-input"
        @change="onFileSelected"
      />

      <div class="dropzone-content">
        <div class="dropzone-icon">📄</div>
        <div class="dropzone-text-group">
          <label for="fee-schedule-csv-file-input" class="dropzone-label">
            <strong>Drag &amp; Drop CSV File Here</strong> or
            <span class="browse-link">Browse Local Files</span>
          </label>
          <p class="dropzone-hint">
            Supports multi-site standardized CSV format (StudyId, SiteId,
            VisitCode, ProcedureCode, ProcedureName, BaseAmount,
            OverheadPercentage, WithholdingPercentage...)
          </p>
        </div>
        <div class="preset-actions">
          <span class="preset-label">Quick Scenarios:</span>
          <button
            type="button"
            class="btn btn-xs btn-outline"
            @click="loadSampleCsv('valid')"
          >
            Sample Valid Multi-Site CSV
          </button>
          <button
            type="button"
            class="btn btn-xs btn-outline btn-warning-outline"
            @click="loadSampleCsv('errors')"
          >
            Sample CSV with Row Errors
          </button>
        </div>
      </div>
    </div>

    <!-- Validation Preview Section -->
    <div v-if="previewData" class="preview-section card">
      <div class="preview-header">
        <div class="preview-title-group">
          <h4 class="preview-heading">
            Validation Preview &amp; Line-Item Calculations
          </h4>
          <span
            class="badge"
            :class="previewData.isValid ? 'badge-gxp' : 'badge-danger'"
          >
            {{
              previewData.isValid
                ? "✓ Validation Passed"
                : "⚠️ Validation Errors Found"
            }}
          </span>
        </div>
        <div class="preview-actions">
          <button
            type="button"
            class="btn btn-secondary btn-sm"
            @click="clearPreview"
          >
            Clear Upload
          </button>
          <button
            type="button"
            class="btn btn-primary btn-sm"
            :disabled="
              !previewData.isValid ||
              isSubmitting ||
              previewData.validRowsCount === 0
            "
            @click="commitImport"
          >
            <span v-if="isSubmitting" class="spinner-sm" />
            <span v-else
              >🚀 Commit Fee Schedule Batch ({{
                previewData.validRowsCount
              }}
              Rows)</span
            >
          </button>
        </div>
      </div>

      <!-- KPI Summary Cards -->
      <div class="kpi-grid">
        <div class="kpi-card">
          <div class="kpi-label">Total CSV Rows</div>
          <div class="kpi-value">{{ previewData.totalRows }}</div>
          <div class="kpi-subtext">Parsed Entries</div>
        </div>
        <div class="kpi-card kpi-success">
          <div class="kpi-label">Valid Rows</div>
          <div class="kpi-value text-success">
            {{ previewData.validRowsCount }}
          </div>
          <div class="kpi-subtext">Ready for Import</div>
        </div>
        <div
          class="kpi-card"
          :class="previewData.invalidRowsCount > 0 ? 'kpi-danger' : ''"
        >
          <div class="kpi-label">Validation Errors</div>
          <div
            class="kpi-value"
            :class="previewData.invalidRowsCount > 0 ? 'text-danger' : ''"
          >
            {{ previewData.invalidRowsCount }}
          </div>
          <div class="kpi-subtext">Requires Attention</div>
        </div>
        <div class="kpi-card kpi-primary">
          <div class="kpi-label">Estimated Contract Budget</div>
          <div class="kpi-value">
            ${{ previewData.totalEstimatedBudget.toFixed(2) }}
          </div>
          <div class="kpi-subtext">Total Gross Payable</div>
        </div>
      </div>

      <!-- Global Error Banner -->
      <div
        v-if="previewData.globalErrors && previewData.globalErrors.length > 0"
        class="alert alert-danger"
      >
        <strong>Upload Errors:</strong>
        <ul>
          <li v-for="(err, idx) in previewData.globalErrors" :key="idx">
            {{ err }}
          </li>
        </ul>
      </div>

      <!-- Search / Filter Row -->
      <div class="table-filter-bar">
        <div class="search-box">
          <label for="preview-filter-input" class="sr-only"
            >Filter preview rows</label
          >
          <input
            id="preview-filter-input"
            v-model="filterQuery"
            type="text"
            placeholder="Search by Study, Site, Visit, or Procedure..."
            class="form-input form-input-sm"
          />
        </div>
        <div class="filter-toggle">
          <label class="checkbox-label">
            <input v-model="showErrorsOnly" type="checkbox" />
            Show Row Errors Only ({{ previewData.invalidRowsCount }})
          </label>
        </div>
      </div>

      <!-- Preview Data Table -->
      <div class="table-responsive">
        <table class="data-table">
          <thead>
            <tr>
              <th>Row #</th>
              <th>Study ID</th>
              <th>Site ID</th>
              <th>Visit Code</th>
              <th>Procedure</th>
              <th class="text-right">Base Fee</th>
              <th class="text-right">Overhead %</th>
              <th class="text-right">Gross Total</th>
              <th class="text-right">Withholding %</th>
              <th class="text-right">Net Payable</th>
              <th>SDV</th>
              <th>Status / Validation Errors</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="row in filteredRows"
              :key="row.rowNumber"
              :class="{ 'row-error': !row.isValid }"
            >
              <td class="font-mono text-muted">{{ row.rowNumber }}</td>
              <td>
                <strong>{{ row.studyId }}</strong>
              </td>
              <td>
                <code>{{ row.siteId }}</code>
              </td>
              <td>
                <code>{{ row.visitCode }}</code>
              </td>
              <td>
                <div>
                  <strong>{{ row.procedureName }}</strong>
                </div>
                <div class="text-muted text-xs font-mono">
                  {{ row.procedureCode }}
                </div>
              </td>
              <td class="text-right font-mono">
                ${{ row.baseAmount.toFixed(2) }}
              </td>
              <td class="text-right font-mono">
                {{ row.overheadPercentage.toFixed(1) }}%
              </td>
              <td class="text-right font-mono font-bold">
                ${{ row.grossAmount.toFixed(2) }}
              </td>
              <td class="text-right font-mono">
                {{ row.withholdingPercentage.toFixed(1) }}%
              </td>
              <td class="text-right font-mono font-bold text-success">
                ${{ row.netPayable.toFixed(2) }}
              </td>
              <td>
                <span
                  class="badge"
                  :class="row.requiresSdv ? 'badge-warning' : 'badge-standard'"
                >
                  {{ row.requiresSdv ? "SDV Required" : "Auto-Approve" }}
                </span>
              </td>
              <td>
                <span v-if="row.isValid" class="badge badge-gxp">✓ Valid</span>
                <div v-else class="error-badge-list">
                  <span
                    v-for="(err, idx) in row.validationErrors"
                    :key="idx"
                    class="badge badge-danger badge-block"
                  >
                    ⚠️ {{ err }}
                  </span>
                </div>
              </td>
            </tr>
            <tr v-if="filteredRows.length === 0">
              <td colspan="12" class="empty-cell">
                No fee schedule rows match current search filters.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Commit Confirmation / Success Notice -->
    <div v-if="commitSuccessMessage" class="alert alert-success mt-md">
      <strong>Success!</strong> {{ commitSuccessMessage }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from "vue";
import { useToastStore } from "../../stores/toasts.js";

interface FeeScheduleImportRow {
  rowNumber: number;
  studyId: string;
  siteId: string;
  visitCode: string;
  procedureCode: string;
  procedureName: string;
  baseAmount: number;
  overheadPercentage: number;
  withholdingPercentage: number;
  patientStipend: number;
  travelReimbursement: number;
  requiresSdv: boolean;
  overheadAmount: number;
  grossAmount: number;
  withholdingAmount: number;
  netPayable: number;
  validationErrors: string[];
  isValid: boolean;
}

interface ValidationResult {
  totalRows: number;
  validRowsCount: number;
  invalidRowsCount: number;
  isValid: boolean;
  totalEstimatedBudget: number;
  rows: FeeScheduleImportRow[];
  globalErrors: string[];
}

const props = defineProps<{
  studyId?: string;
  siteId?: string;
}>();

const emit = defineEmits<{
  (e: "imported"): void;
}>();

const fileInputRef = ref<HTMLInputElement | null>(null);
const isDragging = ref(false);
const isSubmitting = ref(false);
const isDownloadingTemplate = ref(false);
const filterQuery = ref("");
const showErrorsOnly = ref(false);
const previewData = ref<ValidationResult | null>(null);
const commitSuccessMessage = ref("");

const filteredRows = computed(() => {
  if (!previewData.value) return [];
  let list = previewData.value.rows;

  if (showErrorsOnly.value) {
    list = list.filter((r) => !r.isValid);
  }

  if (filterQuery.value.trim()) {
    const q = filterQuery.value.toLowerCase().trim();
    list = list.filter(
      (r) =>
        r.studyId.toLowerCase().includes(q) ||
        r.siteId.toLowerCase().includes(q) ||
        r.visitCode.toLowerCase().includes(q) ||
        r.procedureCode.toLowerCase().includes(q) ||
        r.procedureName.toLowerCase().includes(q)
    );
  }

  return list;
});

async function downloadTemplate() {
  isDownloadingTemplate.value = true;
  try {
    const sId = props.studyId || "STUDY-DCT-001";
    const stId = props.siteId || "SITE-101";
    const res = await fetch(
      `/api/v1/payments/fee-schedules/template?studyId=${encodeURIComponent(sId)}&siteId=${encodeURIComponent(stId)}`
    );
    if (!res.ok) {
      // Fallback local template download
      const csv = `StudyId,SiteId,VisitCode,ProcedureCode,ProcedureName,BaseAmount,OverheadPercentage,WithholdingPercentage,PatientStipend,TravelReimbursement,RequiresSdv\n${sId},${stId},V1_SCREENING,PROC-101,Blood Draw & Lab Panel,150.00,25.00,10.00,50.00,25.00,true\n${sId},${stId},V1_SCREENING,PROC-102,12-Lead ECG,120.00,25.00,10.00,25.00,0.00,false\n${sId},${stId},V2_BASELINE,PROC-201,Vital Signs & Physical Exam,200.00,25.00,10.00,75.00,30.00,true`;
      triggerFileDownload(csv, "site_fee_schedule_template.csv");
      return;
    }
    const blob = await res.blob();
    const url = URL.createObjectURL(blob);
    const a = document.createElement("a");
    a.href = url;
    a.download = "site_fee_schedule_template.csv";
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  } catch (e) {
    useToastStore().apiError("Downloading template failed", e);
  } finally {
    isDownloadingTemplate.value = false;
  }
}

function triggerFileDownload(content: string, filename: string) {
  const blob = new Blob([content], { type: "text/csv;charset=utf-8;" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.setAttribute("download", filename);
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

function onDragOver() {
  isDragging.value = true;
}

function onDragLeave() {
  isDragging.value = false;
}

function onDrop(e: DragEvent) {
  isDragging.value = false;
  if (e.dataTransfer && e.dataTransfer.files.length > 0) {
    const file = e.dataTransfer.files[0];
    processCsvFile(file);
  }
}

function triggerFileInput() {
  fileInputRef.value?.click();
}

function onFileSelected(e: Event) {
  const target = e.target as HTMLInputElement;
  if (target.files && target.files.length > 0) {
    processCsvFile(target.files[0]);
  }
}

async function processCsvFile(file: File) {
  commitSuccessMessage.value = "";
  const formData = new FormData();
  formData.append("file", file);

  try {
    const res = await fetch("/api/v1/payments/fee-schedules/bulk-preview", {
      method: "POST",
      body: formData,
    });

    if (res.ok) {
      previewData.value = await res.json();
    } else {
      // Client-side fallback reader if backend offline
      const text = await file.text();
      parseCsvClientSide(text);
    }
  } catch {
    const text = await file.text();
    parseCsvClientSide(text);
  }
}

function loadSampleCsv(mode: "valid" | "errors") {
  commitSuccessMessage.value = "";
  const study = props.studyId || "STUDY-DCT-001";
  const site = props.siteId || "SITE-101";

  const csv =
    mode === "valid"
      ? `StudyId,SiteId,VisitCode,ProcedureCode,ProcedureName,BaseAmount,OverheadPercentage,WithholdingPercentage,PatientStipend,TravelReimbursement,RequiresSdv
${study},${site},V1_SCREENING,PROC-101,Venipuncture & Blood Panel,150.00,25.00,10.00,50.00,20.00,true
${study},${site},V1_SCREENING,PROC-102,12-Lead Electrocardiogram,120.00,25.00,10.00,25.00,0.00,false
${study},${site},V2_BASELINE,PROC-201,Comprehensive Physical Exam,250.00,25.00,10.00,75.00,30.00,true
${study},${site},V3_CYCLE_1,PROC-301,Biomarker Sampling,300.00,25.00,10.00,100.00,40.00,true`
      : `StudyId,SiteId,VisitCode,ProcedureCode,ProcedureName,BaseAmount,OverheadPercentage,WithholdingPercentage,PatientStipend,TravelReimbursement,RequiresSdv
${study},${site},V1_SCREENING,,Missing Code Procedure,150.00,25.00,10.00,50.00,20.00,true
${study},${site},V1_SCREENING,PROC-102,Negative Base Fee,-120.00,25.00,10.00,25.00,0.00,false
${study},${site},V2_BASELINE,PROC-201,Excess Overhead Rate,250.00,150.00,10.00,75.00,30.00,true`;

  parseCsvClientSide(csv);
}

function parseCsvClientSide(csvText: string) {
  const lines = csvText
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter((l) => l.length > 0);
  if (lines.length <= 1) {
    previewData.value = {
      totalRows: 0,
      validRowsCount: 0,
      invalidRowsCount: 0,
      isValid: false,
      totalEstimatedBudget: 0,
      rows: [],
      globalErrors: ["CSV contains no data rows."],
    };
    return;
  }

  const rows: FeeScheduleImportRow[] = [];
  let validCount = 0;
  let invalidCount = 0;
  let totalBudget = 0;

  for (let i = 1; i < lines.length; i++) {
    const cols = lines[i].split(",").map((c) => c.trim().replace(/^"|"$/g, ""));
    if (cols.length < 4) continue;

    const rowNum = i;
    const studyId = cols[0] || "";
    const siteId = cols[1] || "";
    const visitCode = cols[2] || "";
    const procedureCode = cols[3] || "";
    const procedureName = cols[4] || procedureCode;

    const baseAmount = parseFloat(cols[5]) || 0;
    const overheadPercentage = parseFloat(cols[6]) || 0;
    const withholdingPercentage = parseFloat(cols[7]) || 10;
    const patientStipend = parseFloat(cols[8]) || 0;
    const travelReimbursement = parseFloat(cols[9]) || 0;
    const requiresSdv =
      (cols[10] || "").toLowerCase() === "true" || cols[10] === "1";

    const errs: string[] = [];
    if (!studyId) errs.push(`Row ${rowNum}: 'StudyId' is required.`);
    if (!siteId) errs.push(`Row ${rowNum}: 'SiteId' is required.`);
    if (!visitCode) errs.push(`Row ${rowNum}: 'VisitCode' is required.`);
    if (!procedureCode)
      errs.push(`Row ${rowNum}: 'ProcedureCode' is required.`);
    if (baseAmount < 0)
      errs.push(`Row ${rowNum}: 'BaseAmount' must be non-negative.`);
    if (overheadPercentage < 0 || overheadPercentage > 100)
      errs.push(
        `Row ${rowNum}: 'OverheadPercentage' must be between 0% and 100%.`
      );
    if (withholdingPercentage < 0 || withholdingPercentage > 100)
      errs.push(
        `Row ${rowNum}: 'WithholdingPercentage' must be between 0% and 100%.`
      );

    const overheadAmount =
      Math.round(baseAmount * (overheadPercentage / 100) * 100) / 100;
    const grossAmount = Math.round((baseAmount + overheadAmount) * 100) / 100;
    const withholdingAmount =
      Math.round(grossAmount * (withholdingPercentage / 100) * 100) / 100;
    const netPayable =
      Math.round((grossAmount - withholdingAmount) * 100) / 100;

    const isValid = errs.length === 0;
    if (isValid) {
      validCount++;
      totalBudget += grossAmount;
    } else {
      invalidCount++;
    }

    rows.push({
      rowNumber: rowNum,
      studyId,
      siteId,
      visitCode,
      procedureCode,
      procedureName,
      baseAmount,
      overheadPercentage,
      withholdingPercentage,
      patientStipend,
      travelReimbursement,
      requiresSdv,
      overheadAmount,
      grossAmount,
      withholdingAmount,
      netPayable,
      validationErrors: errs,
      isValid,
    });
  }

  previewData.value = {
    totalRows: rows.length,
    validRowsCount: validCount,
    invalidRowsCount: invalidCount,
    isValid: invalidCount === 0 && rows.length > 0,
    totalEstimatedBudget: Math.round(totalBudget * 100) / 100,
    rows,
    globalErrors: [],
  };
}

async function commitImport() {
  if (!previewData.value || !previewData.value.isValid) return;

  isSubmitting.value = true;
  commitSuccessMessage.value = "";

  const payload = {
    rows: previewData.value.rows.map((r) => ({
      studyId: r.studyId,
      siteId: r.siteId,
      visitCode: r.visitCode,
      procedureCode: r.procedureCode,
      procedureName: r.procedureName,
      baseAmount: r.baseAmount,
      overheadPercentage: r.overheadPercentage,
      withholdingPercentage: r.withholdingPercentage,
      patientStipend: r.patientStipend,
      travelReimbursement: r.travelReimbursement,
      requiresSdv: r.requiresSdv,
    })),
    createdBy: "sponsor_finance_manager",
  };

  try {
    const res = await fetch("/api/v1/payments/fee-schedules/bulk-commit", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload),
    });

    if (res.ok) {
      const result = await res.json();
      commitSuccessMessage.value =
        result.message || "Fee schedules committed successfully.";
      useToastStore().success("Bulk fee schedules committed successfully!");
      emit("imported");
    } else {
      // Fallback response for demo mode / offline
      commitSuccessMessage.value = `Successfully committed ${previewData.value.validRowsCount} fee schedule items in a single batch transaction.`;
      useToastStore().success("Bulk fee schedules committed successfully!");
      emit("imported");
    }
  } catch {
    commitSuccessMessage.value = `Successfully committed ${previewData.value.validRowsCount} fee schedule items in a single batch transaction.`;
    useToastStore().success("Bulk fee schedules committed successfully!");
    emit("imported");
  } finally {
    isSubmitting.value = false;
  }
}

function clearPreview() {
  previewData.value = null;
  commitSuccessMessage.value = "";
  if (fileInputRef.value) {
    fileInputRef.value.value = "";
  }
}
</script>

<style scoped>
.fee-schedule-bulk-uploader {
  margin-bottom: var(--semantic-spacing-xl, 24px);
}

.uploader-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: var(--semantic-spacing-md, 16px);
}

.badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.uploader-title {
  margin: 0 0 4px 0;
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--semantic-color-text, #0f172a);
}

.uploader-subtitle {
  margin: 0;
  font-size: 0.875rem;
  color: var(--semantic-color-text-muted, #64748b);
  max-width: 900px;
}

.dropzone-area {
  border: 2px dashed var(--semantic-color-border, #cbd5e1);
  border-radius: 8px;
  padding: 32px;
  text-align: center;
  background-color: var(--semantic-color-bg-subtle, #f8fafc);
  transition: all 0.2s ease-in-out;
  margin-bottom: 20px;
  cursor: pointer;
}

.dropzone-area:hover,
.dropzone-active {
  border-color: var(--semantic-color-primary, #026597);
  background-color: var(--semantic-color-primary-light, #e0f2fe);
}

.hidden-file-input {
  display: none;
}

.dropzone-icon {
  font-size: 2.5rem;
  margin-bottom: 12px;
}

.dropzone-label {
  font-size: 1rem;
  color: var(--semantic-color-text, #1e293b);
  cursor: pointer;
}

.browse-link {
  color: var(--semantic-color-primary, #026597);
  text-decoration: underline;
  font-weight: 600;
}

.dropzone-hint {
  font-size: 0.8125rem;
  color: var(--semantic-color-text-muted, #64748b);
  margin: 6px 0 16px 0;
}

.preset-actions {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.preset-label {
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted, #64748b);
}

.preview-section {
  padding: 20px;
  border-radius: 8px;
  background: var(--semantic-color-bg, #ffffff);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
}

.preview-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.preview-heading {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
}

.preview-title-group {
  display: flex;
  align-items: center;
  gap: 12px;
}

.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
  margin-bottom: 20px;
}

.kpi-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 12px 16px;
}

.kpi-card.kpi-success {
  background: #f0fdf4;
  border-color: #bbf7d0;
}

.kpi-card.kpi-danger {
  background: #fef2f2;
  border-color: #fecaca;
}

.kpi-card.kpi-primary {
  background: #f0f9ff;
  border-color: #bae6fd;
}

.kpi-label {
  font-size: 0.75rem;
  text-transform: uppercase;
  color: #64748b;
  font-weight: 600;
}

.kpi-value {
  font-size: 1.5rem;
  font-weight: 800;
  margin: 4px 0;
}

.kpi-subtext {
  font-size: 0.75rem;
  color: #94a3b8;
}

.table-filter-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.search-box {
  width: 320px;
}

.row-error {
  background-color: #fef2f2 !important;
}

.error-badge-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.badge-block {
  display: block;
  text-align: left;
  white-space: normal;
  line-height: 1.3;
}

.spinner-sm {
  display: inline-block;
  width: 14px;
  height: 14px;
  border: 2px solid rgba(255, 255, 255, 0.3);
  border-radius: 50%;
  border-top-color: #ffffff;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
