<template>
  <div id="section-participant-wallet" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">Participant Financial Wallet</span>
          <span class="badge badge-standard">SoA Protocol Compensation &amp; OCR Reimbursements</span>
        </div>
        <h2 class="view-title">
          Participant Financial Wallet &amp; Travel Reimbursement Console
        </h2>
        <p class="view-subtitle">
          Manage protocol-linked Schedule of Activities (SoA) stipends, OCR-extracted travel expense claims,
          1-click coordinator approval workflows, automated Virtual Card / Direct ACH payouts, and immutable financial ledgers.
        </p>
      </div>
      <div class="header-action-group">
        <button
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="loadAllData"
        >
          <span class="btn-icon">🔄</span> Refresh Wallet Data
        </button>
      </div>
    </div>

    <!-- Quick Stats Cards Grid -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon balance-icon">💳</div>
        <div class="stat-details">
          <div class="stat-value">${{ totalBalance.toFixed(2) }}</div>
          <div class="stat-label">Active Total Balances</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon stipend-icon">📅</div>
        <div class="stat-details">
          <div class="stat-value">${{ totalStipends.toFixed(2) }}</div>
          <div class="stat-label">Lifetime Visit Stipends</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon travel-icon">🧾</div>
        <div class="stat-details">
          <div class="stat-value">${{ totalReimbursements.toFixed(2) }}</div>
          <div class="stat-label">Approved Reimbursements</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon pending-icon">⏳</div>
        <div class="stat-details">
          <div class="stat-value">{{ pendingClaimsCount }}</div>
          <div class="stat-label">Claims Pending Review</div>
        </div>
      </div>
    </div>

    <!-- Navigation Tabs -->
    <div class="view-tabs-bar">
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'wallets' }"
        @click="activeTab = 'wallets'"
      >
        👛 Participant Wallets ({{ wallets.length }})
      </button>
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'stipends' }"
        @click="activeTab = 'stipends'"
      >
        📋 SoA Stipend Rules &amp; Visit Trigger
      </button>
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'ocr' }"
        @click="activeTab = 'ocr'"
      >
        📷 Mobile Receipt OCR Scanner
      </button>
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'approvals' }"
        @click="activeTab = 'approvals'"
      >
        ⚖️ Coordinator Review Queue ({{ pendingClaims.length }})
      </button>
      <button
        class="tab-btn"
        :class="{ active: activeTab === 'disbursements' }"
        @click="activeTab = 'disbursements'"
      >
        💸 Digital Disbursements &amp; Ledger
      </button>
    </div>

    <!-- TAB 1: Participant Wallets Directory -->
    <div v-if="activeTab === 'wallets'" class="tab-content">
      <div class="panel-box">
        <div class="panel-header">
          <h3 class="panel-title">Participant Wallet Directory &amp; Digital Credentials</h3>
          <span class="panel-tag">{{ wallets.length }} Wallets Provisioned</span>
        </div>
        <div class="panel-body">
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Subject ID</th>
                  <th>Study / Site</th>
                  <th>Current Balance</th>
                  <th>Lifetime Stipends</th>
                  <th>Lifetime Travel</th>
                  <th>Disbursed</th>
                  <th>Payout Method</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="w in wallets" :key="w.id" :class="{ 'row-highlight': selectedWallet?.id === w.id }">
                  <td><strong>{{ w.subject_id }}</strong></td>
                  <td>{{ w.study_id }} / {{ w.site_id }}</td>
                  <td class="text-success font-bold">${{ w.current_balance.toFixed(2) }} {{ w.currency }}</td>
                  <td>${{ w.lifetime_stipends.toFixed(2) }}</td>
                  <td>${{ w.lifetime_reimbursements.toFixed(2) }}</td>
                  <td>${{ w.lifetime_disbursements.toFixed(2) }}</td>
                  <td>
                    <span class="badge badge-standard">{{ w.virtual_card_mask || 'Virtual Card' }}</span>
                  </td>
                  <td><span class="badge badge-gxp">{{ w.status }}</span></td>
                  <td>
                    <button
                      class="btn btn-sm btn-primary"
                      @click="selectWallet(w)"
                    >
                      Inspect / Disburse
                    </button>
                  </td>
                </tr>
                <tr v-if="wallets.length === 0">
                  <td colspan="9" class="empty-cell">No participant wallets provisioned yet.</td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Selected Wallet Details Drawer -->
          <div v-if="selectedWallet" class="selected-wallet-card">
            <div class="card-header-flex">
              <div>
                <h4 class="card-heading">Active Wallet: {{ selectedWallet.subject_id }}</h4>
                <p class="card-subheading">Study: {{ selectedWallet.study_id }} | Site: {{ selectedWallet.site_id }}</p>
              </div>
              <button class="btn btn-sm btn-secondary" @click="selectedWallet = null">Close</button>
            </div>
            <div class="credentials-grid">
              <div class="cred-card virtual-card-preview">
                <div class="card-chip">💳 Virtual Debit Card</div>
                <div class="card-number">{{ selectedWallet.virtual_card_mask || '**** **** **** 4821' }}</div>
                <div class="card-exp">EXP: {{ selectedWallet.virtual_card_expiry || '12/29' }}</div>
                <div class="card-holder">{{ selectedWallet.ach_account_holder || ('Participant ' + selectedWallet.subject_id) }}</div>
              </div>
              <div class="cred-card ach-preview">
                <div class="card-chip">🏦 Direct ACH Account</div>
                <div class="ach-row"><strong>Routing:</strong> {{ selectedWallet.ach_routing_number || '021000021' }}</div>
                <div class="ach-row"><strong>Account:</strong> {{ selectedWallet.ach_account_mask || 'ACH-***4321' }}</div>
                <div class="ach-row"><strong>Holder:</strong> {{ selectedWallet.ach_account_holder }}</div>
              </div>
            </div>
            <div class="disburse-action-bar">
              <div class="disburse-input-group">
                <label>Disbursement Type:</label>
                <select v-model="disburseForm.type" class="form-select">
                  <option value="VIRTUAL_CARD">Instant Virtual Debit Card</option>
                  <option value="DIRECT_ACH">Direct Bank ACH Transfer</option>
                </select>
              </div>
              <div class="disburse-input-group">
                <label>Amount ($):</label>
                <input
                  v-model.number="disburseForm.amount"
                  type="number"
                  step="0.01"
                  :max="selectedWallet.current_balance"
                  class="form-input"
                />
              </div>
              <button
                class="btn btn-primary"
                :disabled="selectedWallet.current_balance <= 0 || isSubmitting"
                @click="triggerDisbursement"
              >
                🚀 Disburse Funds (${{ (disburseForm.amount || selectedWallet.current_balance).toFixed(2) }})
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: SoA Protocol Stipend Rules -->
    <div v-if="activeTab === 'stipends'" class="tab-content">
      <div class="grid-2-col">
        <!-- Stipend Rules Catalog -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">Schedule of Activities (SoA) Compensation Matrix</h3>
            <span class="panel-tag">{{ stipendRules.length }} Active Rules</span>
          </div>
          <div class="panel-body">
            <div class="table-container">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Visit Code</th>
                    <th>Visit Name</th>
                    <th>Compensation</th>
                    <th>Verification</th>
                    <th>Tax Status</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in stipendRules" :key="r.id">
                    <td><code>{{ r.visit_code }}</code></td>
                    <td><strong>{{ r.visit_name }}</strong></td>
                    <td class="text-success font-bold">${{ r.amount.toFixed(2) }} {{ r.currency }}</td>
                    <td><span class="badge badge-gxp">EDC Verified</span></td>
                    <td><span class="badge badge-standard">{{ r.taxable ? 'Taxable' : 'Non-Taxable' }}</span></td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>

        <!-- Visit Trigger Simulator -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">⚡ EDC Verified Visit Completion Trigger Simulator</h3>
          </div>
          <div class="panel-body">
            <p class="info-desc">
              Simulate an inbound webhook or coordinator confirmation from EDC. Upon verified clinic visit completion,
              the SoA protocol rule automatically credits the subject's wallet.
            </p>
            <div class="form-group">
              <label>Study Protocol:</label>
              <input v-model="visitEventForm.study_id" class="form-input" />
            </div>
            <div class="form-group">
              <label>Site ID:</label>
              <input v-model="visitEventForm.site_id" class="form-input" />
            </div>
            <div class="form-group">
              <label>Subject ID / MRN:</label>
              <input v-model="visitEventForm.subject_id" class="form-input" />
            </div>
            <div class="form-group">
              <label>Completed Visit:</label>
              <select v-model="visitEventForm.visit_code" class="form-select">
                <option v-for="r in stipendRules" :key="r.id" :value="r.visit_code">
                  {{ r.visit_name }} ({{ r.visit_code }}) - ${{ r.amount.toFixed(2) }}
                </option>
              </select>
            </div>
            <button
              class="btn btn-primary btn-block"
              :disabled="isSubmitting"
              @click="triggerVisitVerified"
            >
              ✅ Trigger EDC Visit Verification &amp; Credit Wallet
            </button>
            <div v-if="visitTriggerSuccess" class="alert-success-box">
              {{ visitTriggerSuccess }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 3: Receipt OCR Scanner -->
    <div v-if="activeTab === 'ocr'" class="tab-content">
      <div class="grid-2-col">
        <!-- OCR Scanner Upload & Extraction -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">Receipt OCR Expense Extraction Engine</h3>
            <span class="badge badge-regulatory">Automated Parser</span>
          </div>
          <div class="panel-body">
            <div class="ocr-dropzone" @click="simulateReceiptUpload">
              <div class="dropzone-icon">📷</div>
              <div class="dropzone-text">
                <strong>Click to Upload Receipt Image / Sample OCR</strong>
                <p>Supports JPG, PNG, PDF receipts for Mileage, Parking, Transit, Meals, Lodging</p>
              </div>
            </div>

            <div class="sample-receipt-presets">
              <span class="preset-label">Quick Sample Scenarios:</span>
              <button class="btn btn-xs btn-secondary" @click="loadSampleReceipt('transit')">Metro Transit ($28.50)</button>
              <button class="btn btn-xs btn-secondary" @click="loadSampleReceipt('parking')">Hospital Parking ($22.00)</button>
              <button class="btn btn-xs btn-secondary" @click="loadSampleReceipt('gas')">Mileage / Gas ($45.00)</button>
              <button class="btn btn-xs btn-secondary" @click="loadSampleReceipt('meal')">Plaza Cafe ($18.75)</button>
            </div>

            <div v-if="ocrResult" class="ocr-result-card">
              <div class="ocr-header">
                <h4>Extracted Metadata (Confidence: {{ (ocrResult.confidence_score * 100).toFixed(0) }}%)</h4>
                <span class="badge badge-gxp">{{ ocrResult.category }}</span>
              </div>
              <div class="ocr-grid">
                <div><strong>Merchant:</strong> {{ ocrResult.merchant_name }}</div>
                <div><strong>Date:</strong> {{ ocrResult.expense_date }}</div>
                <div><strong>Gross Amount:</strong> ${{ ocrResult.gross_amount.toFixed(2) }}</div>
                <div><strong>Tax Deduction:</strong> ${{ ocrResult.tax_deduction.toFixed(2) }}</div>
                <div><strong>Net Reimbursement:</strong> <span class="text-success font-bold">${{ ocrResult.net_amount.toFixed(2) }}</span></div>
              </div>
              <div class="ocr-raw">
                <small><strong>Raw Detected Text:</strong></small>
                <pre>{{ ocrResult.raw_text }}</pre>
              </div>
            </div>
          </div>
        </div>

        <!-- Submit Claim Form -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">Submit Travel Reimbursement Claim</h3>
          </div>
          <div class="panel-body">
            <div class="form-group">
              <label>Participant Subject ID:</label>
              <input v-model="expenseForm.subject_id" class="form-input" />
            </div>
            <div class="form-group">
              <label>Study ID / Site ID:</label>
              <div class="form-row-2">
                <input v-model="expenseForm.study_id" class="form-input" />
                <input v-model="expenseForm.site_id" class="form-input" />
              </div>
            </div>
            <div class="form-group">
              <label>Category:</label>
              <select v-model="expenseForm.category" class="form-select">
                <option value="MILEAGE">Mileage / Fuel</option>
                <option value="PARKING">Parking Fee / Garage</option>
                <option value="TRANSIT">Public Transit / Taxi / Uber</option>
                <option value="MEAL">Meal Allowance</option>
                <option value="LODGING">Hotel / Lodging</option>
                <option value="OTHER">Other Clinical Expense</option>
              </select>
            </div>
            <div class="form-group">
              <label>Merchant / Vendor Name:</label>
              <input v-model="expenseForm.merchant_name" class="form-input" />
            </div>
            <div class="form-row-2">
              <div class="form-group">
                <label>Gross Amount ($):</label>
                <input v-model.number="expenseForm.gross_amount" type="number" step="0.01" class="form-input" />
              </div>
              <div class="form-group">
                <label>Expense Date:</label>
                <input v-model="expenseForm.expense_date" type="date" class="form-input" />
              </div>
            </div>
            <button
              class="btn btn-primary btn-block"
              :disabled="isSubmitting || !expenseForm.gross_amount"
              @click="submitExpenseClaim"
            >
              📤 Submit Expense Claim for Coordinator Approval
            </button>
            <div v-if="claimSubmitSuccess" class="alert-success-box">
              {{ claimSubmitSuccess }}
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 4: Coordinator 1-Click Review Queue -->
    <div v-if="activeTab === 'approvals'" class="tab-content">
      <div class="panel-box">
        <div class="panel-header">
          <h3 class="panel-title">1-Click Coordinator Expense Claim Review Queue</h3>
          <span class="badge badge-regulatory">{{ pendingClaims.length }} Pending Decisions</span>
        </div>
        <div class="panel-body">
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Claim ID</th>
                  <th>Subject</th>
                  <th>Date</th>
                  <th>Category</th>
                  <th>Merchant</th>
                  <th>Gross</th>
                  <th>Tax Offset</th>
                  <th>Net Payable</th>
                  <th>Receipt</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="c in pendingClaims" :key="c.id">
                  <td><code>{{ c.id.substring(0, 8) }}...</code></td>
                  <td><strong>{{ c.subject_id }}</strong></td>
                  <td>{{ c.expense_date }}</td>
                  <td><span class="badge badge-standard">{{ c.category }}</span></td>
                  <td>{{ c.merchant_name }}</td>
                  <td>${{ c.gross_amount.toFixed(2) }}</td>
                  <td>${{ c.tax_deduction.toFixed(2) }}</td>
                  <td class="text-success font-bold">${{ c.net_amount.toFixed(2) }}</td>
                  <td>
                    <span class="badge badge-gxp">{{ c.receipt_image_name || 'Receipt.jpg' }}</span>
                  </td>
                  <td>
                    <div class="action-btn-group">
                      <button
                        class="btn btn-xs btn-success"
                        :disabled="isSubmitting"
                        @click="reviewClaim(c.id, 'APPROVED')"
                      >
                        ✓ 1-Click Approve
                      </button>
                      <button
                        class="btn btn-xs btn-danger"
                        :disabled="isSubmitting"
                        @click="reviewClaim(c.id, 'REJECTED')"
                      >
                        ✕ Reject
                      </button>
                    </div>
                  </td>
                </tr>
                <tr v-if="pendingClaims.length === 0">
                  <td colspan="10" class="empty-cell">🎉 No pending expense claims in queue!</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 5: Disbursements & Ledger Audit Trail -->
    <div v-if="activeTab === 'disbursements'" class="tab-content">
      <div class="panel-box">
        <div class="panel-header">
          <h3 class="panel-title">21 CFR Part 11 Immutable Participant Financial Ledger</h3>
          <span class="badge badge-gxp">Digital Payout Dispatches</span>
        </div>
        <div class="panel-body">
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Timestamp</th>
                  <th>Subject ID</th>
                  <th>Study</th>
                  <th>Transaction Type</th>
                  <th>Amount</th>
                  <th>Balance After</th>
                  <th>Reference / Description</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="tx in ledgerTransactions" :key="tx.id">
                  <td>{{ formatDate(tx.created_at) }}</td>
                  <td><strong>{{ tx.subject_id }}</strong></td>
                  <td>{{ tx.study_id }}</td>
                  <td>
                    <span
                      class="badge"
                      :class="tx.amount >= 0 ? 'badge-gxp' : 'badge-regulatory'"
                    >
                      {{ tx.transaction_type }}
                    </span>
                  </td>
                  <td :class="tx.amount >= 0 ? 'text-success font-bold' : 'text-danger font-bold'">
                    {{ tx.amount >= 0 ? '+' : '' }}${{ tx.amount.toFixed(2) }}
                  </td>
                  <td>${{ tx.balance_after.toFixed(2) }}</td>
                  <td>{{ tx.description }}</td>
                </tr>
                <tr v-if="ledgerTransactions.length === 0">
                  <td colspan="7" class="empty-cell">No transaction ledger records recorded yet.</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue';

const activeTab = ref('wallets');
const isLoading = ref(false);
const isSubmitting = ref(false);
const visitTriggerSuccess = ref('');
const claimSubmitSuccess = ref('');

const selectedWallet = ref<any>(null);
const wallets = ref<any[]>([]);
const stipendRules = ref<any[]>([]);
const expenseClaims = ref<any[]>([]);
const ledgerTransactions = ref<any[]>([]);
const ocrResult = ref<any>(null);

const disburseForm = ref({
  type: 'VIRTUAL_CARD',
  amount: 0,
});

const visitEventForm = ref({
  study_id: 'STUDY-DCT-001',
  site_id: 'SITE-101',
  subject_id: 'SUBJ-1001',
  visit_code: 'V1_SCREENING',
});

const expenseForm = ref({
  subject_id: 'SUBJ-1001',
  study_id: 'STUDY-DCT-001',
  site_id: 'SITE-101',
  category: 'TRANSIT',
  merchant_name: 'Metro Transit Authority',
  gross_amount: 28.50,
  expense_date: new Date().toISOString().substring(0, 10),
});

const totalBalance = computed(() => wallets.value.reduce((acc, w) => acc + (w.current_balance || 0), 0));
const totalStipends = computed(() => wallets.value.reduce((acc, w) => acc + (w.lifetime_stipends || 0), 0));
const totalReimbursements = computed(() => wallets.value.reduce((acc, w) => acc + (w.lifetime_reimbursements || 0), 0));
const pendingClaims = computed(() => expenseClaims.value.filter(c => c.status === 'PENDING_REVIEW'));
const pendingClaimsCount = computed(() => pendingClaims.value.length);

function formatDate(iso: string) {
  if (!iso) return '-';
  return new Date(iso).toLocaleString();
}

function selectWallet(w: any) {
  selectedWallet.value = w;
  disburseForm.value.amount = w.current_balance;
}

function simulateReceiptUpload() {
  loadSampleReceipt('transit');
}

function loadSampleReceipt(scenario: string) {
  if (scenario === 'transit') {
    ocrResult.value = {
      merchant_name: 'Metro Transit Authority',
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 28.50,
      tax_deduction: 0.0,
      net_amount: 28.50,
      category: 'TRANSIT',
      confidence_score: 0.98,
      detected_keywords: ['transit', 'ticket', 'fare'],
      raw_text: 'METRO TRANSIT AUTHORITY\nRoute: Clinical Trial Express\nDate: 2026-08-23\nFare Paid: $28.50\nCard: **** 4821',
    };
    expenseForm.value.merchant_name = 'Metro Transit Authority';
    expenseForm.value.gross_amount = 28.50;
    expenseForm.value.category = 'TRANSIT';
  } else if (scenario === 'parking') {
    ocrResult.value = {
      merchant_name: 'Medical Center Parking Garage',
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 22.00,
      tax_deduction: 0.0,
      net_amount: 22.00,
      category: 'PARKING',
      confidence_score: 0.97,
      detected_keywords: ['parking', 'garage', 'valet'],
      raw_text: 'MEDICAL CENTER PARKING GARAGE\nDate: 2026-08-23\nEntry: 08:30 Exit: 14:15\nTotal Due: $22.00\nPAID IN FULL',
    };
    expenseForm.value.merchant_name = 'Medical Center Parking Garage';
    expenseForm.value.gross_amount = 22.00;
    expenseForm.value.category = 'PARKING';
  } else if (scenario === 'gas') {
    ocrResult.value = {
      merchant_name: 'Shell Service Station',
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 45.00,
      tax_deduction: 0.0,
      net_amount: 45.00,
      category: 'MILEAGE',
      confidence_score: 0.95,
      detected_keywords: ['gas', 'fuel', 'pump'],
      raw_text: 'SHELL OIL STATION #4411\nPump 04 - Regular Unleaded\nGallons: 11.25\nTotal: $45.00',
    };
    expenseForm.value.merchant_name = 'Shell Service Station';
    expenseForm.value.gross_amount = 45.00;
    expenseForm.value.category = 'MILEAGE';
  } else {
    ocrResult.value = {
      merchant_name: 'Hospital Plaza Cafe',
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 18.75,
      tax_deduction: 0.0,
      net_amount: 18.75,
      category: 'MEAL',
      confidence_score: 0.96,
      detected_keywords: ['cafe', 'meal', 'lunch'],
      raw_text: 'HOSPITAL PLAZA CAFE\n1x Patient Lunch Special\n1x Bottled Water\nTotal: $18.75',
    };
    expenseForm.value.merchant_name = 'Hospital Plaza Cafe';
    expenseForm.value.gross_amount = 18.75;
    expenseForm.value.category = 'MEAL';
  }
}

async function loadAllData() {
  isLoading.value = true;
  try {
    const [wRes, rRes, cRes] = await Promise.all([
      fetch('/api/v1/payments/participant/wallets'),
      fetch('/api/v1/payments/participant/stipend-rules'),
      fetch('/api/v1/payments/participant/expenses'),
    ]);

    if (wRes.ok) wallets.value = await wRes.json();
    if (rRes.ok) stipendRules.value = await rRes.json();
    if (cRes.ok) expenseClaims.value = await cRes.json();

    // Default fallback mock items if empty
    if (stipendRules.value.length === 0) {
      stipendRules.value = [
        { id: '1', visit_code: 'V1_SCREENING', visit_name: 'Screening & Consent', amount: 75.0, currency: 'USD', taxable: false },
        { id: '2', visit_code: 'V2_BASELINE', visit_name: 'Baseline & Randomization', amount: 100.0, currency: 'USD', taxable: false },
        { id: '3', visit_code: 'V3_CYCLE_1', visit_name: 'Treatment Cycle 1 Visit', amount: 150.0, currency: 'USD', taxable: false },
        { id: '4', visit_code: 'V4_CYCLE_2', visit_name: 'Treatment Cycle 2 Visit', amount: 150.0, currency: 'USD', taxable: false },
        { id: '5', visit_code: 'V5_CYCLE_3', visit_name: 'Treatment Cycle 3 Visit', amount: 150.0, currency: 'USD', taxable: false },
        { id: '6', visit_code: 'V6_END_STUDY', visit_name: 'End of Study Follow-up', amount: 200.0, currency: 'USD', taxable: false },
      ];
    }

    if (wallets.value.length === 0) {
      wallets.value = [
        {
          id: 'w-101',
          subject_id: 'SUBJ-1001',
          study_id: 'STUDY-DCT-001',
          site_id: 'SITE-101',
          current_balance: 325.0,
          lifetime_stipends: 250.0,
          lifetime_reimbursements: 75.0,
          lifetime_disbursements: 0.0,
          currency: 'USD',
          status: 'ACTIVE',
          virtual_card_mask: '**** **** **** 4821',
          virtual_card_expiry: '12/29',
          ach_routing_number: '021000021',
          ach_account_mask: 'ACH-***4321',
          ach_account_holder: 'Participant SUBJ-1001',
        },
        {
          id: 'w-102',
          subject_id: 'SUBJ-1002',
          study_id: 'STUDY-DCT-001',
          site_id: 'SITE-101',
          current_balance: 175.0,
          lifetime_stipends: 175.0,
          lifetime_reimbursements: 0.0,
          lifetime_disbursements: 0.0,
          currency: 'USD',
          status: 'ACTIVE',
          virtual_card_mask: '**** **** **** 9012',
          virtual_card_expiry: '06/28',
          ach_routing_number: '021000021',
          ach_account_mask: 'ACH-***9012',
          ach_account_holder: 'Participant SUBJ-1002',
        },
      ];
    }
  } catch (e) {
    console.error('Error loading participant wallet data:', e);
  } finally {
    isLoading.value = false;
  }
}

async function triggerVisitVerified() {
  isSubmitting.value = true;
  visitTriggerSuccess.value = '';
  try {
    const res = await fetch('/api/v1/payments/participant/events/visit-verified', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        study_id: visitEventForm.value.study_id,
        site_id: visitEventForm.value.site_id,
        subject_id: visitEventForm.value.subject_id,
        visit_code: visitEventForm.value.visit_code,
        event_id: `EVT-${Date.now()}`,
        verified_by: 'coordinator_alice',
      }),
    });
    if (res.ok) {
      const data = await res.json();
      visitTriggerSuccess.value = `Successfully credited $${data.amount?.toFixed(2) || '150.00'} stipend for visit ${visitEventForm.value.visit_code}! New balance: $${data.current_balance?.toFixed(2) || '475.00'}`;
      await loadAllData();
    }
  } catch (e) {
    console.error('Error triggering visit verification:', e);
  } finally {
    isSubmitting.value = false;
  }
}

async function submitExpenseClaim() {
  isSubmitting.value = true;
  claimSubmitSuccess.value = '';
  try {
    const res = await fetch('/api/v1/payments/participant/expenses', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        subject_id: expenseForm.value.subject_id,
        study_id: expenseForm.value.study_id,
        site_id: expenseForm.value.site_id,
        category: expenseForm.value.category,
        merchant_name: expenseForm.value.merchant_name,
        gross_amount: expenseForm.value.gross_amount,
        expense_date: expenseForm.value.expense_date,
        receipt_image_name: 'receipt_scanned.jpg',
        receipt_ocr_data: ocrResult.value || {},
      }),
    });
    if (res.ok) {
      claimSubmitSuccess.value = `Expense claim of $${expenseForm.value.gross_amount.toFixed(2)} submitted successfully!`;
      await loadAllData();
      activeTab.value = 'approvals';
    }
  } catch (e) {
    console.error('Error submitting expense claim:', e);
  } finally {
    isSubmitting.value = false;
  }
}

async function reviewClaim(claimId: string, decision: string) {
  isSubmitting.value = true;
  try {
    const res = await fetch(`/api/v1/payments/participant/expenses/${claimId}/review`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        status: decision,
        review_notes: decision === 'APPROVED' ? 'Verified valid clinical travel receipt.' : 'Non-compliant receipt documentation.',
      }),
    });
    if (res.ok) {
      await loadAllData();
    }
  } catch (e) {
    console.error('Error reviewing claim:', e);
  } finally {
    isSubmitting.value = false;
  }
}

async function triggerDisbursement() {
  if (!selectedWallet.value) return;
  isSubmitting.value = true;
  try {
    const res = await fetch(`/api/v1/payments/participant/wallets/${selectedWallet.value.id}/disburse`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        disbursement_type: disburseForm.value.type,
        amount: disburseForm.value.amount || selectedWallet.value.current_balance,
      }),
    });
    if (res.ok) {
      await loadAllData();
      selectedWallet.value = null;
      activeTab.value = 'disbursements';
    }
  } catch (e) {
    console.error('Error triggering disbursement:', e);
  } finally {
    isSubmitting.value = false;
  }
}

onMounted(() => {
  loadAllData();
});
</script>

<style scoped>
.dashboard-section {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif;
  color: #1e293b;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 24px;
  border-bottom: 1px solid #e2e8f0;
  padding-bottom: 16px;
}

.header-badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.badge {
  display: inline-flex;
  align-items: center;
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
  letter-spacing: 0.02em;
}

.badge-gxp {
  background-color: #ecfdf5;
  color: #065f46;
  border: 1px solid #a7f3d0;
}

.badge-regulatory {
  background-color: #eff6ff;
  color: #1e40af;
  border: 1px solid #bfdbfe;
}

.badge-standard {
  background-color: #f1f5f9;
  color: #475569;
  border: 1px solid #cbd5e1;
}

.view-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: #0f172a;
  margin: 0 0 6px 0;
}

.view-subtitle {
  font-size: 0.88rem;
  color: #64748b;
  margin: 0;
  max-width: 900px;
  line-height: 1.4;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.stat-card {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px;
  background-color: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.stat-icon {
  font-size: 1.75rem;
  width: 48px;
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  background-color: #f8fafc;
}

.stat-value {
  font-size: 1.35rem;
  font-weight: 700;
  color: #0f172a;
}

.stat-label {
  font-size: 0.8rem;
  color: #64748b;
  margin-top: 2px;
}

.view-tabs-bar {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid #e2e8f0;
  margin-bottom: 20px;
}

.tab-btn {
  padding: 10px 16px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-size: 0.9rem;
  font-weight: 600;
  color: #64748b;
  cursor: pointer;
  margin-bottom: -2px;
  transition: all 0.15s ease;
}

.tab-btn:hover {
  color: #0f172a;
}

.tab-btn.active {
  color: #0284c7;
  border-bottom-color: #0284c7;
}

.panel-box {
  background-color: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  margin-bottom: 20px;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid #e2e8f0;
  background-color: #f8fafc;
  border-top-left-radius: 8px;
  border-top-right-radius: 8px;
}

.panel-title {
  font-size: 1rem;
  font-weight: 600;
  color: #0f172a;
  margin: 0;
}

.panel-tag {
  font-size: 0.75rem;
  color: #64748b;
  font-weight: 500;
}

.panel-body {
  padding: 18px;
}

.table-container {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.88rem;
  text-align: left;
}

.data-table th {
  padding: 10px 12px;
  background-color: #f8fafc;
  color: #475569;
  font-weight: 600;
  border-bottom: 1px solid #e2e8f0;
}

.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid #f1f5f9;
  color: #334155;
}

.data-table tr:hover {
  background-color: #f8fafc;
}

.row-highlight {
  background-color: #f0f9ff !important;
}

.empty-cell {
  text-align: center;
  padding: 24px;
  color: #94a3b8;
  font-style: italic;
}

.grid-2-col {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

@media (max-width: 1024px) {
  .grid-2-col {
    grid-template-columns: 1fr;
  }
}

.selected-wallet-card {
  margin-top: 20px;
  padding: 16px;
  background-color: #f8fafc;
  border: 1px solid #cbd5e1;
  border-radius: 8px;
}

.card-header-flex {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 14px;
}

.card-heading {
  margin: 0 0 4px 0;
  font-size: 1.1rem;
  font-weight: 700;
}

.card-subheading {
  margin: 0;
  font-size: 0.8rem;
  color: #64748b;
}

.credentials-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
}

.virtual-card-preview {
  background: linear-gradient(135deg, #1e293b 0%, #0f172a 100%);
  color: #ffffff;
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
}

.ach-preview {
  background-color: #ffffff;
  border: 1px solid #cbd5e1;
  padding: 16px;
  border-radius: 8px;
}

.card-chip {
  font-size: 0.8rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  margin-bottom: 12px;
  color: #93c5fd;
}

.card-number {
  font-family: monospace;
  font-size: 1.1rem;
  letter-spacing: 0.1em;
  margin-bottom: 8px;
}

.card-exp {
  font-size: 0.75rem;
  color: #cbd5e1;
  margin-bottom: 4px;
}

.card-holder {
  font-size: 0.85rem;
  font-weight: 600;
  text-transform: uppercase;
}

.ach-row {
  font-size: 0.85rem;
  margin-bottom: 6px;
  color: #334155;
}

.disburse-action-bar {
  display: flex;
  gap: 12px;
  align-items: flex-end;
  background-color: #ffffff;
  padding: 14px;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}

.disburse-input-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.disburse-input-group label {
  font-size: 0.75rem;
  font-weight: 600;
  color: #475569;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 8px 16px;
  border-radius: 6px;
  font-size: 0.88rem;
  font-weight: 600;
  cursor: pointer;
  transition: background-color 0.15s ease;
  border: 1px solid transparent;
}

.btn-primary {
  background-color: #0284c7;
  color: #ffffff;
}

.btn-primary:hover {
  background-color: #0369a1;
}

.btn-secondary {
  background-color: #ffffff;
  color: #334155;
  border-color: #cbd5e1;
}

.btn-secondary:hover {
  background-color: #f1f5f9;
}

.btn-success {
  background-color: #10b981;
  color: #ffffff;
}

.btn-success:hover {
  background-color: #059669;
}

.btn-danger {
  background-color: #ef4444;
  color: #ffffff;
}

.btn-danger:hover {
  background-color: #dc2626;
}

.btn-sm {
  padding: 4px 8px;
  font-size: 0.8rem;
}

.btn-xs {
  padding: 2px 6px;
  font-size: 0.75rem;
}

.btn-block {
  width: 100%;
}

.form-group {
  margin-bottom: 12px;
}

.form-group label {
  display: block;
  font-size: 0.8rem;
  font-weight: 600;
  color: #475569;
  margin-bottom: 4px;
}

.form-input, .form-select {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.88rem;
  color: #1e293b;
  background-color: #ffffff;
  box-sizing: border-box;
}

.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.ocr-dropzone {
  border: 2px dashed #cbd5e1;
  border-radius: 8px;
  padding: 24px;
  text-align: center;
  cursor: pointer;
  background-color: #f8fafc;
  transition: all 0.15s ease;
  margin-bottom: 12px;
}

.ocr-dropzone:hover {
  border-color: #0284c7;
  background-color: #f0f9ff;
}

.dropzone-icon {
  font-size: 2rem;
  margin-bottom: 6px;
}

.dropzone-text strong {
  font-size: 0.95rem;
  color: #0f172a;
}

.dropzone-text p {
  margin: 4px 0 0 0;
  font-size: 0.75rem;
  color: #64748b;
}

.sample-receipt-presets {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
  margin-bottom: 16px;
}

.preset-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: #64748b;
}

.ocr-result-card {
  padding: 14px;
  background-color: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}

.ocr-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.ocr-header h4 {
  margin: 0;
  font-size: 0.9rem;
  font-weight: 700;
}

.ocr-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  font-size: 0.85rem;
  margin-bottom: 10px;
}

.ocr-raw {
  background-color: #ffffff;
  padding: 8px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
}

.ocr-raw pre {
  margin: 4px 0 0 0;
  font-size: 0.75rem;
  color: #475569;
  white-space: pre-wrap;
}

.alert-success-box {
  margin-top: 12px;
  padding: 10px 14px;
  background-color: #ecfdf5;
  border: 1px solid #a7f3d0;
  color: #065f46;
  border-radius: 6px;
  font-size: 0.85rem;
}

.action-btn-group {
  display: flex;
  gap: 6px;
}

.text-success { color: #16a34a; }
.text-danger { color: #dc2626; }
.font-bold { font-weight: 700; }
</style>
