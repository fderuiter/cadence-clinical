<template>
  <div class="self-billing-invoicing-console">
    <!-- Header Controls -->
    <header class="console-header">
      <div class="header-title-block">
        <div class="header-icon-box">
          <span class="icon-symbol">⚖️</span>
        </div>
        <div>
          <h2 class="title">Self-Billing Invoicing &amp; Remittance Generator</h2>
          <p class="subtitle">
            Automated billing cycles, cryptographic SHA-256 PDF remittance advice, 21 CFR Part 11 e-signatures, and ERP settlement reconciliation.
          </p>
        </div>
      </div>
      <div class="header-actions">
        <span class="badge badge-gxp">21 CFR Part 11 Regulated</span>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="fetchInvoices"
        >
          🔄 Refresh
        </button>
        <button
          type="button"
          class="btn btn-primary"
          @click="showCompileModal = true"
        >
          + Compile Billing Cycle
        </button>
      </div>
    </header>

    <!-- KPI Summary Cards -->
    <div class="kpi-grid">
      <div class="kpi-card">
        <div class="kpi-label">Total Invoices</div>
        <div class="kpi-value">{{ invoices.length }}</div>
        <div class="kpi-subtext">Active Billing Records</div>
      </div>
      <div class="kpi-card">
        <div class="kpi-label">Total Gross Invoiced</div>
        <div class="kpi-value">${{ totalGrossSum.toFixed(2) }}</div>
        <div class="kpi-subtext">Procedures &amp; Overheads</div>
      </div>
      <div class="kpi-card kpi-warning">
        <div class="kpi-label">10% Holdback Reserves</div>
        <div class="kpi-value">${{ totalHoldbackSum.toFixed(2) }}</div>
        <div class="kpi-subtext">Retained in Escrow</div>
      </div>
      <div class="kpi-card kpi-success">
        <div class="kpi-label">Net Settlement Payable</div>
        <div class="kpi-value">${{ totalNetSum.toFixed(2) }}</div>
        <div class="kpi-subtext">Disbursed / Reconciled</div>
      </div>
    </div>

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
          <option value="MANUAL">Manual</option>
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
      <div class="filter-actions">
        <button
          type="button"
          class="btn btn-outline btn-sm"
          @click="resetFilters"
        >
          Clear Filters
        </button>
      </div>
    </section>

    <!-- Invoices Table -->
    <main class="invoices-table-container card">
      <div v-if="isLoading" class="loading-state">
        <span class="spinner" /> Loading self-billing invoices...
      </div>
      <div v-else-if="invoices.length === 0" class="empty-state">
        <p>No self-billing invoices found matching the current filter criteria.</p>
        <button type="button" class="btn btn-primary btn-sm" @click="showCompileModal = true">
          Compile First Billing Cycle
        </button>
      </div>
      <div v-else class="table-responsive">
        <table class="data-table">
          <thead>
            <tr>
              <th>Invoice #</th>
              <th>Study / Site</th>
              <th>Cycle &amp; Period</th>
              <th class="text-right">Gross Total</th>
              <th class="text-right">Holdback (10%)</th>
              <th class="text-right">Net Payable</th>
              <th>Status</th>
              <th>ERP Sync</th>
              <th>Remittance Advice</th>
              <th class="text-center">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="inv in invoices" :key="inv.id">
              <td class="font-mono font-bold">{{ inv.invoice_number }}</td>
              <td>
                <div><strong>{{ inv.study_id }}</strong></div>
                <div class="text-muted text-xs">{{ inv.site_id }}</div>
              </td>
              <td>
                <div><span class="badge badge-cycle">{{ inv.billing_cycle }}</span></div>
                <div class="text-muted text-xs">{{ inv.period_start }} to {{ inv.period_end }}</div>
              </td>
              <td class="text-right font-mono">{{ inv.currency }} {{ inv.total_gross_amount.toFixed(2) }}</td>
              <td class="text-right text-danger font-mono">-{{ inv.currency }} {{ inv.total_holdback_amount.toFixed(2) }}</td>
              <td class="text-right font-bold text-success font-mono">{{ inv.currency }} {{ inv.total_net_amount.toFixed(2) }}</td>
              <td>
                <span :class="['badge', `status-${inv.status.toLowerCase()}`]">
                  {{ inv.status }}
                </span>
              </td>
              <td>
                <span :class="['badge', `erp-${inv.erp_sync_status.toLowerCase()}`]">
                  {{ inv.erp_system || 'ERP' }}: {{ inv.erp_sync_status }}
                </span>
              </td>
              <td>
                <div v-if="inv.remittance_advice">
                  <span v-if="inv.remittance_advice.is_signed" class="badge badge-signed">
                    ✓ Signed (SHA-256)
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
      </div>
    </main>

    <!-- Detail Drawer / Modal for Selected Invoice -->
    <div v-if="selectedInvoice" class="modal-overlay" @click.self="selectedInvoice = null">
      <div class="modal-card">
        <header class="modal-header">
          <div>
            <h3>Invoice {{ selectedInvoice.invoice_number }}</h3>
            <p class="text-muted">{{ selectedInvoice.study_id }} | {{ selectedInvoice.site_id }} ({{ selectedInvoice.billing_cycle }} Cycle)</p>
          </div>
          <button type="button" class="btn-close" @click="selectedInvoice = null">&times;</button>
        </header>

        <div class="modal-body">
          <!-- Mini Stats Grid -->
          <div class="summary-cards-grid">
            <div class="mini-stat-card">
              <span class="stat-label">Base Procedures</span>
              <span class="stat-value">{{ selectedInvoice.currency }} {{ selectedInvoice.total_base_amount.toFixed(2) }}</span>
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
              <span class="stat-label">Net Disbursement Total</span>
              <span class="stat-value">{{ selectedInvoice.currency }} {{ selectedInvoice.total_net_amount.toFixed(2) }}</span>
            </div>
          </div>

          <!-- Line Items Table -->
          <h4 class="section-title">Itemized Fee Schedules &amp; Payables ({{ selectedInvoice.line_items.length }})</h4>
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
                  <td class="font-bold">{{ item.subject_id }}</td>
                  <td>{{ item.visit_code }}</td>
                  <td>{{ item.procedure_code ? `${item.procedure_code} - ` : '' }}{{ item.procedure_name || 'Procedure' }}</td>
                  <td class="text-right font-mono">{{ item.currency }} {{ item.base_amount.toFixed(2) }}</td>
                  <td class="text-right font-mono">{{ item.currency }} {{ item.overhead_amount.toFixed(2) }}</td>
                  <td class="text-right font-mono">{{ item.currency }} {{ item.gross_amount.toFixed(2) }}</td>
                  <td class="text-right text-danger font-mono">-{{ item.currency }} {{ item.withholding_amount.toFixed(2) }}</td>
                  <td class="text-right font-bold text-success font-mono">{{ item.currency }} {{ item.net_amount.toFixed(2) }}</td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Remittance Advice Section -->
          <div v-if="selectedInvoice.remittance_advice" class="remittance-section card">
            <div class="remittance-header">
              <h4 class="section-title">Cryptographic Remittance Advice Statement</h4>
              <button
                type="button"
                class="btn-sm btn-outline"
                @click="downloadRemittancePdf(selectedInvoice.id)"
              >
                📄 Download PDF
              </button>
            </div>
            <div class="remittance-meta">
              <div><strong>Remittance #:</strong> {{ selectedInvoice.remittance_advice.remittance_number }}</div>
              <div><strong>Status:</strong> {{ selectedInvoice.remittance_advice.is_signed ? '21 CFR Part 11 Signed' : 'Sealed Draft (Unsigned)' }}</div>
              <div class="full-width">
                <strong>SHA-256 Document Seal:</strong>
                <code class="hash-code">{{ selectedInvoice.remittance_advice.pdf_sha256_hash }}</code>
              </div>
              <div v-if="selectedInvoice.remittance_advice.signed_by">
                <strong>Signer:</strong> {{ selectedInvoice.remittance_advice.signed_by }} ({{ selectedInvoice.remittance_advice.signer_role }})
              </div>
              <div v-if="selectedInvoice.remittance_advice.signed_at">
                <strong>Signed At:</strong> {{ new Date(selectedInvoice.remittance_advice.signed_at).toLocaleString() }}
              </div>
              <div><strong>Dispatch Status:</strong> {{ selectedInvoice.remittance_advice.dispatch_status }}</div>
            </div>
          </div>

          <!-- Settlement Reconciliation Status Card -->
          <div v-if="selectedInvoice.status === 'RECONCILED' || selectedInvoice.reconciled_by" class="reconciliation-card card">
            <h4 class="section-title">Settlement &amp; ERP Reconciliation Status</h4>
            <div class="reconciliation-grid">
              <div><strong>Reconciled By:</strong> {{ selectedInvoice.reconciled_by }}</div>
              <div><strong>Reconciled At:</strong> {{ selectedInvoice.reconciled_at ? new Date(selectedInvoice.reconciled_at).toLocaleString() : 'N/A' }}</div>
              <div><strong>Settlement Reference:</strong> <span class="font-mono font-bold">{{ selectedInvoice.reconciliation_reference }}</span></div>
              <div><strong>ERP Reference:</strong> <span class="font-mono">{{ selectedInvoice.erp_reference_id || 'N/A' }}</span></div>
              <div class="full-width" v-if="selectedInvoice.reconciliation_notes">
                <strong>Notes:</strong> {{ selectedInvoice.reconciliation_notes }}
              </div>
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
            @click="openSignModal(selectedInvoice.id)"
          >
            ✍️ Sign Remittance (Part 11)
          </button>
          <button
            v-if="selectedInvoice.status !== 'DRAFT'"
            type="button"
            class="btn btn-info"
            @click="exportToErp(selectedInvoice.id)"
          >
            Export to ERP (SAP)
          </button>
          <button
            v-if="selectedInvoice.status === 'APPROVED' || selectedInvoice.status === 'PAID'"
            type="button"
            class="btn btn-warning"
            @click="openReconcileModal(selectedInvoice)"
          >
            ⚖️ Reconcile Settlement
          </button>
        </footer>
      </div>
    </div>

    <!-- Modal: Compile Billing Cycle -->
    <div v-if="showCompileModal" class="modal-overlay" @click.self="showCompileModal = false">
      <div class="modal-card modal-sm">
        <header class="modal-header">
          <h3>Compile Site Billing Cycle</h3>
          <button type="button" class="btn-close" @click="showCompileModal = false">&times;</button>
        </header>
        <div class="modal-body">
          <div class="form-group">
            <label for="compile-study">Study Protocol ID *</label>
            <input id="compile-study" v-model="compileForm.studyId" type="text" placeholder="e.g. STUDY-ONC-201" required>
          </div>
          <div class="form-group">
            <label for="compile-site">Site ID *</label>
            <input id="compile-site" v-model="compileForm.siteId" type="text" placeholder="e.g. SITE-101" required>
          </div>
          <div class="form-group">
            <label for="compile-cycle">Billing Cycle</label>
            <select id="compile-cycle" v-model="compileForm.billingCycle">
              <option value="MONTHLY">Monthly</option>
              <option value="QUARTERLY">Quarterly</option>
              <option value="MANUAL">Manual Settlement</option>
            </select>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label for="compile-start">Period Start</label>
              <input id="compile-start" v-model="compileForm.periodStart" type="date">
            </div>
            <div class="form-group">
              <label for="compile-end">Period End</label>
              <input id="compile-end" v-model="compileForm.periodEnd" type="date">
            </div>
          </div>
          <div class="form-group checkbox-group">
            <label>
              <input v-model="compileForm.autoApprove" type="checkbox">
              Auto-approve invoice upon compilation
            </label>
          </div>
        </div>
        <footer class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="showCompileModal = false">Cancel</button>
          <button type="button" class="btn btn-primary" :disabled="isSubmitting" @click="submitCompileBilling">
            {{ isSubmitting ? 'Compiling...' : 'Execute Compilation' }}
          </button>
        </footer>
      </div>
    </div>

    <!-- Modal: Part 11 Electronic Signature for Remittance Advice -->
    <div v-if="showSignModal" class="modal-overlay" @click.self="showSignModal = false">
      <div class="modal-card modal-sm">
        <header class="modal-header">
          <h3>21 CFR Part 11 Electronic Signature</h3>
          <button type="button" class="btn-close" @click="showSignModal = false">&times;</button>
        </header>
        <div class="modal-body">
          <p class="text-xs text-muted">
            Applying this digital signature binds the remittance advice statement to 21 CFR Part 11 cryptographic sealing.
          </p>
          <div class="form-group">
            <label for="sign-name">Full Signer Name *</label>
            <input id="sign-name" v-model="signForm.signerName" type="text" placeholder="e.g. Dr. Eleanor Vance" required>
          </div>
          <div class="form-group">
            <label for="sign-role">Signer Role *</label>
            <input id="sign-role" v-model="signForm.signerRole" type="text" placeholder="e.g. Director of Clinical Grants" required>
          </div>
          <div class="form-group">
            <label for="sign-reason">Signature Reason</label>
            <input id="sign-reason" v-model="signForm.signatureReason" type="text" placeholder="Authorized Self-Billing Remittance Statement">
          </div>
        </div>
        <footer class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="showSignModal = false">Cancel</button>
          <button type="button" class="btn btn-success" :disabled="isSubmitting || !signForm.signerName" @click="submitSignRemittance">
            {{ isSubmitting ? 'Signing...' : 'Sign &amp; Seal Statement' }}
          </button>
        </footer>
      </div>
    </div>

    <!-- Modal: Interactive Settlement Reconciliation -->
    <div v-if="showReconcileModal" class="modal-overlay" @click.self="showReconcileModal = false">
      <div class="modal-card modal-sm">
        <header class="modal-header">
          <h3>Reconcile Ledger Settlement</h3>
          <button type="button" class="btn-close" @click="showReconcileModal = false">&times;</button>
        </header>
        <div class="modal-body">
          <p class="text-xs text-muted">
            Confirm bank wire or ERP clearing reference to reconcile invoice and underlying payables.
          </p>
          <div class="form-group">
            <label for="recon-ref">Bank Wire / Settlement Reference *</label>
            <input id="recon-ref" v-model="reconcileForm.paymentReference" type="text" placeholder="e.g. WIRE-FED-998822" required>
          </div>
          <div class="form-group">
            <label for="recon-erp">ERP System</label>
            <select id="recon-erp" v-model="reconcileForm.erpSystem">
              <option value="SAP">SAP S/4HANA</option>
              <option value="NETSUITE">NetSuite SuiteTalk</option>
              <option value="ORACLE">Oracle Financials</option>
              <option value="DIRECT_WIRE">Direct Bank Settlement</option>
            </select>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label for="recon-amount">Amount Paid ($)</label>
              <input id="recon-amount" v-model.number="reconcileForm.amountPaid" type="number" step="0.01">
            </div>
            <div class="form-group">
              <label for="recon-date">Payment Date</label>
              <input id="recon-date" v-model="reconcileForm.paymentDate" type="date">
            </div>
          </div>
          <div class="form-group">
            <label for="recon-notes">Reconciliation Notes</label>
            <textarea id="recon-notes" v-model="reconcileForm.notes" rows="2" placeholder="Matched against institutional bank ledger statement..."></textarea>
          </div>
        </div>
        <footer class="modal-footer">
          <button type="button" class="btn btn-secondary" @click="showReconcileModal = false">Cancel</button>
          <button type="button" class="btn btn-primary" :disabled="isSubmitting || !reconcileForm.paymentReference" @click="submitReconcileInvoice">
            {{ isSubmitting ? 'Reconciling...' : 'Confirm Reconciliation' }}
          </button>
        </footer>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';

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
const isSubmitting = ref<boolean>(false);

const showCompileModal = ref<boolean>(false);
const showSignModal = ref<boolean>(false);
const showReconcileModal = ref<boolean>(false);
const signingInvoiceId = ref<string>('');

const filters = ref({
  studyId: '',
  siteId: '',
  billingCycle: '',
  status: '',
});

const compileForm = ref({
  studyId: '',
  siteId: '',
  billingCycle: 'MONTHLY',
  periodStart: '2026-08-01',
  periodEnd: '2026-08-31',
  autoApprove: false,
});

const signForm = ref({
  signerName: '',
  signerRole: 'Grants Lead',
  signatureReason: 'Authorized Self-Billing Remittance Disbursement Statement',
});

const reconcileForm = ref({
  invoiceId: '',
  paymentReference: '',
  erpSystem: 'SAP',
  amountPaid: 0,
  paymentDate: new Date().toISOString().split('T')[0],
  notes: '',
});

// Computed KPI Stats
const totalGrossSum = computed(() => {
  return invoices.value.reduce((acc, inv) => acc + (inv.total_gross_amount || 0), 0);
});

const totalHoldbackSum = computed(() => {
  return invoices.value.reduce((acc, inv) => acc + (inv.total_holdback_amount || 0), 0);
});

const totalNetSum = computed(() => {
  return invoices.value.reduce((acc, inv) => acc + (inv.total_net_amount || 0), 0);
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

function resetFilters() {
  filters.value = {
    studyId: '',
    siteId: '',
    billingCycle: '',
    status: '',
  };
  fetchInvoices();
}

function selectInvoice(inv: SelfBillingInvoice) {
  selectedInvoice.value = inv;
}

async function submitCompileBilling() {
  if (!compileForm.value.studyId || !compileForm.value.siteId) {
    alert('Please provide both Study ID and Site ID.');
    return;
  }

  isSubmitting.value = true;
  try {
    const res = await fetch('/api/v1/payments/invoices/compile', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        study_id: compileForm.value.studyId,
        site_id: compileForm.value.siteId,
        billing_cycle: compileForm.value.billingCycle,
        period_start: compileForm.value.periodStart,
        period_end: compileForm.value.periodEnd,
        auto_approve: compileForm.value.autoApprove,
      }),
    });
    if (res.ok) {
      showCompileModal.value = false;
      await fetchInvoices();
    } else {
      const err = await res.json();
      alert(`Error compiling billing cycle: ${err.detail || 'Unknown error'}`);
    }
  } catch (err) {
    console.error('Error compiling billing cycle:', err);
  } finally {
    isSubmitting.value = false;
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

function openSignModal(invoiceId: string) {
  signingInvoiceId.value = invoiceId;
  showSignModal.value = true;
}

async function submitSignRemittance() {
  if (!signingInvoiceId.value || !signForm.value.signerName) return;

  isSubmitting.value = true;
  try {
    const res = await fetch(`/api/v1/payments/invoices/${signingInvoiceId.value}/remittance-advice/sign`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        signer_name: signForm.value.signerName,
        signer_role: signForm.value.signerRole,
        signature_reason: signForm.value.signatureReason,
      }),
    });
    if (res.ok) {
      const updatedRemittance = await res.json();
      if (selectedInvoice.value && selectedInvoice.value.id === signingInvoiceId.value) {
        selectedInvoice.value.remittance_advice = updatedRemittance;
      }
      showSignModal.value = false;
      await fetchInvoices();
    }
  } catch (err) {
    console.error('Failed to sign remittance advice:', err);
  } finally {
    isSubmitting.value = false;
  }
}

function openReconcileModal(inv: SelfBillingInvoice) {
  reconcileForm.value = {
    invoiceId: inv.id,
    paymentReference: `WIRE-${Date.now().toString().slice(-6)}`,
    erpSystem: inv.erp_system || 'SAP',
    amountPaid: inv.total_net_amount,
    paymentDate: new Date().toISOString().split('T')[0],
    notes: `Settlement confirmation for invoice ${inv.invoice_number}`,
  };
  showReconcileModal.value = true;
}

async function submitReconcileInvoice() {
  if (!reconcileForm.value.invoiceId || !reconcileForm.value.paymentReference) return;

  isSubmitting.value = true;
  try {
    const res = await fetch(`/api/v1/payments/invoices/${reconcileForm.value.invoiceId}/reconcile`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        payment_reference: reconcileForm.value.paymentReference,
        erp_system: reconcileForm.value.erpSystem,
        amount_paid: reconcileForm.value.amountPaid,
        payment_date: reconcileForm.value.paymentDate,
        notes: reconcileForm.value.notes,
      }),
    });
    if (res.ok) {
      showReconcileModal.value = false;
      if (selectedInvoice.value && selectedInvoice.value.id === reconcileForm.value.invoiceId) {
        const invRes = await fetch(`/api/v1/payments/invoices/${reconcileForm.value.invoiceId}`);
        if (invRes.ok) {
          selectedInvoice.value = await invRes.json();
        }
      }
      await fetchInvoices();
    } else {
      const err = await res.json();
      alert(`Error reconciling invoice: ${err.detail || 'Unknown error'}`);
    }
  } catch (err) {
    console.error('Failed to reconcile invoice:', err);
  } finally {
    isSubmitting.value = false;
  }
}

async function exportToErp(invoiceId: string) {
  try {
    const res = await fetch(`/api/v1/payments/invoices/${invoiceId}/erp-export?erp_system=SAP`, {
      method: 'POST',
    });
    if (res.ok) {
      await fetchInvoices();
      if (selectedInvoice.value && selectedInvoice.value.id === invoiceId) {
        const invRes = await fetch(`/api/v1/payments/invoices/${invoiceId}`);
        if (invRes.ok) {
          selectedInvoice.value = await invRes.json();
        }
      }
    }
  } catch (err) {
    console.error('Failed to export to ERP:', err);
  }
}

function downloadRemittancePdf(invoiceId: string) {
  window.open(`/api/v1/payments/invoices/${invoiceId}/remittance-advice/pdf`, '_blank');
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
  width: 100%;
  color: var(--text, #1e293b);
  background: var(--bg, #f8fafc);
  padding: 16px;
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
}

.header-title-block {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-icon-box {
  width: 40px;
  height: 40px;
  border-radius: 8px;
  background: #ede9fe;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 20px;
}

.title {
  margin: 0;
  font-size: 20px;
  font-weight: 700;
  color: var(--text, #0f172a);
}

.subtitle {
  margin: 2px 0 0 0;
  font-size: 12px;
  color: var(--text-muted, #64748b);
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 8px;
}

.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.kpi-card {
  background: var(--card-bg, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 4px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.kpi-label {
  font-size: 11px;
  font-weight: 700;
  color: var(--text-muted, #64748b);
  text-transform: uppercase;
}

.kpi-value {
  font-size: 22px;
  font-weight: 800;
  color: var(--text, #0f172a);
}

.kpi-subtext {
  font-size: 11px;
  color: var(--text-muted, #64748b);
}

.kpi-warning .kpi-value {
  color: #b45309;
}

.kpi-success .kpi-value {
  color: #16a34a;
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

.filter-actions {
  display: flex;
  align-items: flex-end;
}

.table-responsive {
  width: 100%;
  overflow-x: auto;
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

.text-success {
  color: var(--success, #16a34a);
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

.badge-cycle {
  background: #f1f5f9;
  color: #334155;
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

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
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

.btn-warning {
  background: #d97706;
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

.modal-sm {
  width: 480px;
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

.remittance-section,
.reconciliation-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  background: #f8fafc;
}

.remittance-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.remittance-meta,
.reconciliation-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  font-size: 11px;
}

.full-width {
  grid-column: 1 / -1;
}

.hash-code {
  font-family: monospace;
  font-size: 10px;
  background: #e2e8f0;
  padding: 2px 4px;
  border-radius: 3px;
  word-break: break-all;
  display: inline-block;
  margin-top: 2px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-muted, #64748b);
}

.form-group input,
.form-group select,
.form-group textarea {
  padding: 8px 10px;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: 4px;
  background: #ffffff;
  font-size: 12px;
  color: var(--text, #0f172a);
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.checkbox-group label {
  display: flex;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.loading-state,
.empty-state {
  padding: 40px;
  text-align: center;
  color: var(--text-muted, #64748b);
  font-size: 13px;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
}
</style>
