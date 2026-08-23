<template>
  <div class="self-billing-invoicing-console">
    <!-- Header Controls -->
    <header class="console-header">
      <div class="header-title-block">
        <h2 class="title">Self-Billing Invoicing &amp; Remittance Generator</h2>
        <span class="badge badge-gxp">21 CFR Part 11 Regulated</span>
      </div>
      <div class="header-actions">
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="fetchInvoices"
        >
          Refresh
        </button>
        <button
          type="button"
          class="btn btn-primary"
          @click="openCompileModal"
        >
          + Compile Billing Cycle
        </button>
      </div>
    </header>

    <!-- Filter Toolbar -->
    <section class="filter-panel card">
      <div class="filter-group">
        <label for="filter-study">Study Protocol</label>
        <input
          id="filter-study"
          v-model="filters.studyId"
          type="text"
          placeholder="e.g. STUDY-ONC-201"
          @input="onFilterChange"
        >
      </div>
      <div class="filter-group">
        <label for="filter-site">Site ID</label>
        <input
          id="filter-site"
          v-model="filters.siteId"
          type="text"
          placeholder="e.g. SITE-101"
          @input="onFilterChange"
        >
      </div>
      <div class="filter-group">
        <label for="filter-cycle">Cycle</label>
        <select
          id="filter-cycle"
          v-model="filters.billingCycle"
          @change="onFilterChange"
        >
          <option value="">All Cycles</option>
          <option value="MONTHLY">Monthly</option>
          <option value="QUARTERLY">Quarterly</option>
        </select>
      </div>
      <div class="filter-group">
        <label for="filter-status">Invoice Status</label>
        <select
          id="filter-status"
          v-model="filters.status"
          @change="onFilterChange"
        >
          <option value="">All Statuses</option>
          <option value="DRAFT">Draft</option>
          <option value="APPROVED">Approved</option>
          <option value="PAID">Paid</option>
          <option value="RECONCILED">Reconciled</option>
        </select>
      </div>
    </section>

    <!-- Invoices Table -->
    <main class="invoices-table-container card">
      <div v-if="isLoading" class="loading-state">
        <span class="spinner" /> Loading invoices...
      </div>
      <div v-else-if="invoices.length === 0" class="empty-state">
        <p>No self-billing invoices found for the current filter criteria.</p>
      </div>
      <table v-else class="data-table">
        <thead>
          <tr>
            <th>Invoice #</th>
            <th>Study / Site</th>
            <th>Cycle &amp; Period</th>
            <th class="text-right">Gross Total</th>
            <th class="text-right">Holdback (10%)</th>
            <th class="text-right">Net Payable</th>
            <th>Status</th>
            <th>ERP Status</th>
            <th>Remittance Advice</th>
            <th class="text-center">Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="inv in invoices" :key="inv.id">
            <td class="font-mono font-bold">{{ inv.invoice_number }}</td>
            <td>
              <div><strong>{{ inv.study_id }}</strong></div>
              <div class="text-muted">{{ inv.site_id }}</div>
            </td>
            <td>
              <div>{{ inv.billing_cycle }}</div>
              <div class="text-muted text-xs">{{ inv.period_start }} to {{ inv.period_end }}</div>
            </td>
            <td class="text-right">{{ inv.currency }} {{ inv.total_gross_amount.toFixed(2) }}</td>
            <td class="text-right text-danger">-{{ inv.currency }} {{ inv.total_holdback_amount.toFixed(2) }}</td>
            <td class="text-right font-bold">{{ inv.currency }} {{ inv.total_net_amount.toFixed(2) }}</td>
            <td>
              <span :class="['badge', `status-${inv.status.toLowerCase()}`]">
                {{ inv.status }}
              </span>
            </td>
            <td>
              <span :class="['badge', `erp-${inv.erp_sync_status.toLowerCase()}`]">
                {{ inv.erp_system }}: {{ inv.erp_sync_status }}
              </span>
            </td>
            <td>
              <div v-if="inv.remittance_advice">
                <span v-if="inv.remittance_advice.is_signed" class="badge badge-signed">
                  Signed (SHA-256)
                </span>
                <span v-else class="badge badge-pending">
                  Sealed Draft
                </span>
              </div>
              <span v-else class="text-muted">None</span>
            </td>
            <td class="text-center actions-cell">
              <button
                type="button"
                class="btn-sm btn-outline"
                @click="selectInvoice(inv)"
              >
                Inspect
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </main>

    <!-- Detail Drawer / Modal for Selected Invoice -->
    <div v-if="selectedInvoice" class="modal-overlay" @click.self="selectedInvoice = null">
      <div class="modal-card">
        <header class="modal-header">
          <div>
            <h3>Invoice {{ selectedInvoice.invoice_number }}</h3>
            <p class="text-muted">{{ selectedInvoice.study_id }} | {{ selectedInvoice.site_id }} ({{ selectedInvoice.billing_cycle }})</p>
          </div>
          <button type="button" class="btn-close" @click="selectedInvoice = null">&times;</button>
        </header>

        <div class="modal-body">
          <div class="summary-cards-grid">
            <div class="mini-stat-card">
              <span class="stat-label">Gross Procedures</span>
              <span class="stat-value">{{ selectedInvoice.currency }} {{ selectedInvoice.total_gross_amount.toFixed(2) }}</span>
            </div>
            <div class="mini-stat-card">
              <span class="stat-label">Institutional Overhead</span>
              <span class="stat-value">{{ selectedInvoice.currency }} {{ selectedInvoice.total_overhead_amount.toFixed(2) }}</span>
            </div>
            <div class="mini-stat-card stat-danger">
              <span class="stat-label">10% Holdback Retention</span>
              <span class="stat-value">-{{ selectedInvoice.currency }} {{ selectedInvoice.total_holdback_amount.toFixed(2) }}</span>
            </div>
            <div class="mini-stat-card stat-success">
              <span class="stat-label">Net Payable Disbursement</span>
              <span class="stat-value">{{ selectedInvoice.currency }} {{ selectedInvoice.total_net_amount.toFixed(2) }}</span>
            </div>
          </div>

          <h4 class="section-title">Itemized Procedures ({{ selectedInvoice.line_items.length }})</h4>
          <div class="table-scroll">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Subject</th>
                  <th>Visit</th>
                  <th>Procedure</th>
                  <th class="text-right">Base</th>
                  <th class="text-right">Overhead</th>
                  <th class="text-right">Gross</th>
                  <th class="text-right">Holdback</th>
                  <th class="text-right">Net</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in selectedInvoice.line_items" :key="item.id">
                  <td>{{ item.subject_id }}</td>
                  <td>{{ item.visit_code }}</td>
                  <td>{{ item.procedure_code }} - {{ item.procedure_name }}</td>
                  <td class="text-right">{{ item.currency }} {{ item.base_amount.toFixed(2) }}</td>
                  <td class="text-right">{{ item.currency }} {{ item.overhead_amount.toFixed(2) }}</td>
                  <td class="text-right">{{ item.currency }} {{ item.gross_amount.toFixed(2) }}</td>
                  <td class="text-right text-danger">-{{ item.currency }} {{ item.withholding_amount.toFixed(2) }}</td>
                  <td class="text-right font-bold">{{ item.currency }} {{ item.net_amount.toFixed(2) }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Remittance Advice & ERP Sync Operations -->
          <div v-if="selectedInvoice.remittance_advice" class="remittance-section card">
            <h4 class="section-title">Cryptographic Remittance Advice Statement</h4>
            <div class="remittance-meta">
              <div><strong>Remittance #:</strong> {{ selectedInvoice.remittance_advice.remittance_number }}</div>
              <div><strong>SHA-256 Document Seal:</strong> <code class="hash-code">{{ selectedInvoice.remittance_advice.pdf_sha256_hash }}</code></div>
              <div><strong>Signature Status:</strong> {{ selectedInvoice.remittance_advice.is_signed ? '21 CFR Part 11 Certified Signed' : 'Sealed Draft (Unsigned)' }}</div>
              <div><strong>Dispatch Status:</strong> {{ selectedInvoice.remittance_advice.dispatch_status }}</div>
            </div>
          </div>
        </div>

        <footer class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="selectedInvoice = null">Close</button>
          <button
            v-if="selectedInvoice.status === 'DRAFT'"
            type="button"
            class="btn btn-primary"
            @click="approveInvoice(selectedInvoice.id)"
          >
            Approve Invoice
          </button>
          <button
            v-if="selectedInvoice.remittance_advice && !selectedInvoice.remittance_advice.is_signed"
            type="button"
            class="btn btn-success"
            @click="signRemittance(selectedInvoice.id)"
          >
            Sign Remittance (Part 11)
          </button>
          <button
            type="button"
            class="btn btn-info"
            @click="exportToErp(selectedInvoice.id)"
          >
            Export to ERP (SAP)
          </button>
        </footer>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue';

export interface InvoiceLineItem {
  id: string;
  invoice_id: string;
  payable_id: string | null;
  subject_id: string;
  visit_code: string;
  procedure_code: string | null;
  procedure_name: string | null;
  base_amount: number;
  overhead_amount: number;
  patient_stipend: number;
  travel_reimbursement: number;
  gross_amount: number;
  withholding_rate: number;
  withholding_amount: number;
  net_amount: number;
  currency: string;
  created_at: string;
  version_index: number;
}

export interface RemittanceAdvice {
  id: string;
  remittance_number: string;
  invoice_id: string;
  study_id: string;
  site_id: string;
  currency: string;
  gross_amount: number;
  overhead_amount: number;
  holdback_amount: number;
  net_amount: number;
  pdf_sha256_hash: string;
  is_sealed: boolean;
  is_signed: boolean;
  signed_by: string | null;
  signed_at: string | null;
  signer_role: string | null;
  dispatch_status: string;
  created_at: string;
  version_index: number;
}

export interface SelfBillingInvoice {
  id: string;
  invoice_number: string;
  study_id: string;
  site_id: string;
  contract_id: string;
  billing_cycle: string;
  period_start: string;
  period_end: string;
  currency: string;
  total_base_amount: number;
  total_overhead_amount: number;
  total_patient_stipend: number;
  total_travel_reimbursement: number;
  total_gross_amount: number;
  total_holdback_amount: number;
  total_net_amount: number;
  payable_count: number;
  status: string;
  approved_by: string | null;
  approved_at: string | null;
  erp_system: string | null;
  erp_reference_id: string | null;
  erp_sync_status: string;
  erp_synced_at: string | null;
  reconciled_by: string | null;
  reconciled_at: string | null;
  reconciliation_reference: string | null;
  reconciliation_notes: string | null;
  created_at: string;
  created_by: string;
  reason_for_change: string | null;
  version_index: number;
  line_items: InvoiceLineItem[];
  remittance_advice: RemittanceAdvice | null;
}

const invoices = ref<SelfBillingInvoice[]>([]);
const selectedInvoice = ref<SelfBillingInvoice | null>(null);
const isLoading = ref<boolean>(false);

const filters = ref({
  studyId: '',
  siteId: '',
  billingCycle: '',
  status: '',
});

async function fetchInvoices() {
  isLoading.value = true;
  try {
    const params = new URLSearchParams();
    if (filters.value.studyId) params.append('study_id', filters.value.studyId);
    if (filters.value.siteId) params.append('site_id', filters.value.siteId);
    if (filters.value.billingCycle) params.append('billing_cycle', filters.value.billingCycle);
    if (filters.value.status) params.append('status', filters.value.status);

    const res = await fetch(`/api/v1/payments/invoices?${params.toString()}`);
    if (res.ok) {
      invoices.value = await res.json();
    }
  } catch (err) {
    console.error('Failed to fetch self-billing invoices:', err);
  } finally {
    isLoading.value = false;
  }
}

function onFilterChange() {
  fetchInvoices();
}

function selectInvoice(inv: SelfBillingInvoice) {
  selectedInvoice.value = inv;
}

function openCompileModal() {
  // Can be extended with a compilation prompt or modal
  const study = prompt('Enter Study ID for billing compilation (e.g. STUDY-01):');
  const site = prompt('Enter Site ID (e.g. SITE-01):');
  if (study && site) {
    triggerCompilation(study, site);
  }
}

async function triggerCompilation(studyId: string, siteId: string) {
  try {
    const res = await fetch('/api/v1/payments/invoices/compile', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        study_id: studyId,
        site_id: siteId,
        billing_cycle: 'MONTHLY',
        period_start: '2026-08-01',
        period_end: '2026-08-31',
        auto_approve: false,
      }),
    });
    if (res.ok) {
      alert('Billing cycle successfully compiled.');
      await fetchInvoices();
    } else {
      const err = await res.json();
      alert(`Error compiling billing cycle: ${err.detail || 'Unknown error'}`);
    }
  } catch (err) {
    console.error('Error compiling billing cycle:', err);
  }
}

async function approveInvoice(invoiceId: string) {
  try {
    const res = await fetch(`/api/v1/payments/invoices/${invoiceId}/approve`, {
      method: 'POST',
    });
    if (res.ok) {
      const updated = await res.json();
      selectedInvoice.value = updated;
      await fetchInvoices();
    }
  } catch (err) {
    console.error('Failed to approve invoice:', err);
  }
}

async function signRemittance(invoiceId: string) {
  const signerName = prompt('Enter 21 CFR Part 11 Signer Name:');
  if (!signerName) return;
  try {
    const res = await fetch(`/api/v1/payments/invoices/${invoiceId}/remittance-advice/sign`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        signer_name: signerName,
        signer_role: 'Grants Lead',
        signature_reason: 'Authorized Self-Billing Remittance Disbursement Statement',
      }),
    });
    if (res.ok) {
      alert('Remittance advice successfully signed and cryptographically sealed.');
      if (selectedInvoice.value) {
        selectedInvoice.value.remittance_advice = await res.json();
      }
      await fetchInvoices();
    }
  } catch (err) {
    console.error('Failed to sign remittance advice:', err);
  }
}

async function exportToErp(invoiceId: string) {
  try {
    const res = await fetch(`/api/v1/payments/invoices/${invoiceId}/erp-export?erp_system=SAP`, {
      method: 'POST',
    });
    if (res.ok) {
      alert('Successfully synchronized invoice payload to SAP ERP.');
      await fetchInvoices();
    }
  } catch (err) {
    console.error('Failed to export to ERP:', err);
  }
}

onMounted(() => {
  fetchInvoices();
});
</script>

<style scoped>
.self-billing-invoicing-console {
  display: flex;
  flex-direction: column;
  gap: 16px;
  color: var(--text, #1e293b);
  background: var(--bg, #f8fafc);
  padding: 16px;
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.header-title-block {
  display: flex;
  align-items: center;
  gap: 12px;
}

.title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text, #0f172a);
}

.header-actions {
  display: flex;
  gap: 8px;
}

.card {
  background: var(--card-bg, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 16px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.filter-panel {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
  align-items: flex-end;
}

.filter-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
  flex: 1;
  min-width: 150px;
}

.filter-group label {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-muted, #64748b);
}

.filter-group input,
.filter-group select {
  padding: 8px 10px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: 4px;
  background: var(--bg, #ffffff);
  color: var(--text, #0f172a);
  font-size: 12px;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}

.data-table th,
.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--border, #e2e8f0);
  text-align: left;
}

.data-table th {
  background: var(--header-bg, #f1f5f9);
  font-weight: 600;
  color: var(--text, #334155);
}

.text-right {
  text-align: right;
}

.text-center {
  text-align: center;
}

.font-bold {
  font-weight: 600;
}

.font-mono {
  font-family: monospace;
}

.text-muted {
  color: var(--text-muted, #64748b);
}

.text-xs {
  font-size: 11px;
}

.text-danger {
  color: var(--danger, #dc2626);
}

.badge {
  display: inline-block;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 10px;
  font-weight: 600;
  text-transform: uppercase;
}

.badge-gxp {
  background: #ede9fe;
  color: #6d28d9;
}

.badge-signed {
  background: #dcfce7;
  color: #15803d;
}

.badge-pending {
  background: #fef3c7;
  color: #b45309;
}

.status-draft {
  background: #f1f5f9;
  color: #475569;
}

.status-approved {
  background: #dbeafe;
  color: #1d4ed8;
}

.status-paid {
  background: #dcfce7;
  color: #15803d;
}

.status-reconciled {
  background: #ccfbf1;
  color: #0f766e;
}

.erp-pending {
  background: #f1f5f9;
  color: #475569;
}

.erp-synced {
  background: #dbeafe;
  color: #1d4ed8;
}

.erp-reconciled {
  background: #ccfbf1;
  color: #0f766e;
}

.btn {
  padding: 8px 14px;
  border-radius: 4px;
  font-size: 12px;
  font-weight: 600;
  cursor: pointer;
  border: none;
  transition: opacity 0.2s;
}

.btn-primary {
  background: var(--primary, #2563eb);
  color: white;
}

.btn-secondary {
  background: #e2e8f0;
  color: #334155;
}

.btn-success {
  background: #16a34a;
  color: white;
}

.btn-info {
  background: #0891b2;
  color: white;
}

.btn-sm {
  padding: 4px 8px;
  font-size: 11px;
  border-radius: 4px;
  cursor: pointer;
}

.btn-outline {
  border: 1px solid var(--border, #cbd5e1);
  background: transparent;
  color: var(--text, #334155);
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.4);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.modal-card {
  background: white;
  width: 900px;
  max-width: 95vw;
  max-height: 90vh;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.15);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.modal-header h3 {
  margin: 0;
  font-size: 16px;
}

.modal-header p {
  margin: 4px 0 0 0;
  font-size: 12px;
}

.btn-close {
  background: none;
  border: none;
  font-size: 20px;
  cursor: pointer;
  color: #64748b;
}

.modal-body {
  padding: 16px 20px;
  overflow-y: auto;
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.modal-footer {
  padding: 12px 20px;
  border-top: 1px solid var(--border, #e2e8f0);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
}

.summary-cards-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}

.mini-stat-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.mini-stat-card .stat-label {
  font-size: 10px;
  text-transform: uppercase;
  color: #64748b;
  font-weight: 600;
}

.mini-stat-card .stat-value {
  font-size: 14px;
  font-weight: 700;
}

.stat-danger .stat-value {
  color: #dc2626;
}

.stat-success .stat-value {
  color: #16a34a;
}

.section-title {
  margin: 0;
  font-size: 13px;
  font-weight: 600;
  color: #0f172a;
}

.table-scroll {
  max-height: 200px;
  overflow-y: auto;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 4px;
}

.remittance-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.remittance-meta {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  font-size: 11px;
}

.hash-code {
  font-family: monospace;
  font-size: 10px;
  background: #e2e8f0;
  padding: 2px 4px;
  border-radius: 3px;
  word-break: break-all;
}

.loading-state,
.empty-state {
  padding: 40px;
  text-align: center;
  color: var(--text-muted, #64748b);
  font-size: 13px;
}
</style>
