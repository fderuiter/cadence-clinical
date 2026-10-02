<template>
  <div id="section-participant-wallet" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory"
            >Participant Financial Wallet</span
          >
          <span class="badge badge-standard"
            >PRD-SYS-182 Micro-Ledger Tracer</span
          >
        </div>
        <h2 class="view-title">
          Participant Financial Wallet &amp; Automated Stipend Micro-Ledger
        </h2>
        <p class="view-subtitle">
          Manage protocol-linked Schedule of Activities (SoA) stipends, mobile
          OCR travel/meal reimbursement claims, IRS Form 1099 ($600 cap) tax
          threshold monitoring, automated reloadable debit card issuing, and IRB
          anti-coercion limits.
        </p>
      </div>
      <div class="header-action-group">
        <button
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="loadAllData"
        >
          <span class="btn-icon">🔄</span> Refresh Ledger Data
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
        <div class="stat-icon tax-icon">🏛️</div>
        <div class="stat-details">
          <div class="stat-value">${{ totalTaxableYtd.toFixed(2) }}</div>
          <div class="stat-label">YTD 1099 Taxable Earnings</div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon pending-icon">⏳</div>
        <div class="stat-details">
          <div class="stat-value">
            {{ pendingClaimsCount }}
          </div>
          <div class="stat-label">Claims Pending Review</div>
        </div>
      </div>
    </div>

    <!-- 1099 Tax Threshold Progress Banner -->
    <div v-if="selectedWallet" class="tax-threshold-banner">
      <div class="tax-banner-header">
        <div class="tax-title-group">
          <span class="tax-icon-badge">📑</span>
          <div>
            <strong>IRS Form 1099 Tax Reporting Threshold Tracer</strong>
            <span class="tax-subtext"
              >Subject: {{ selectedWallet.subject_id }} | Annual Cap:
              $600.00</span
            >
          </div>
        </div>
        <div class="tax-badge-group">
          <span
            class="badge"
            :class="{
              'badge-gxp': selectedWalletTaxStatus?.warning_level === 'NORMAL',
              'badge-warning':
                selectedWalletTaxStatus?.warning_level === 'APPROACHING',
              'badge-danger':
                selectedWalletTaxStatus?.warning_level === 'EXCEEDED',
            }"
          >
            {{ selectedWalletTaxStatus?.warning_level || "NORMAL" }} ({{
              (selectedWalletTaxStatus?.threshold_percentage || 0).toFixed(1)
            }}%)
          </span>
          <span
            class="badge"
            :class="{
              'badge-gxp': selectedWallet.w9_status === 'VERIFIED',
              'badge-warning': selectedWallet.w9_status === 'SUBMITTED',
              'badge-danger': selectedWallet.w9_status === 'REQUIRED',
              'badge-standard': selectedWallet.w9_status === 'NOT_REQUIRED',
            }"
          >
            W-9: {{ selectedWallet.w9_status }}
          </span>
        </div>
      </div>

      <!-- Progress bar -->
      <div class="progress-bar-track">
        <div
          class="progress-bar-fill"
          :class="{
            'fill-normal':
              (selectedWalletTaxStatus?.threshold_percentage || 0) < 80,
            'fill-approaching':
              (selectedWalletTaxStatus?.threshold_percentage || 0) >= 80 &&
              (selectedWalletTaxStatus?.threshold_percentage || 0) < 100,
            'fill-exceeded':
              (selectedWalletTaxStatus?.threshold_percentage || 0) >= 100,
          }"
          :style="{
            width: `${Math.min(100, selectedWalletTaxStatus?.threshold_percentage || 0)}%`,
          }"
        />
      </div>
      <div class="tax-legend">
        <span
          >Current Taxable YTD: ${{
            (selectedWallet.taxable_ytd || 0).toFixed(2)
          }}</span
        >
        <span
          >Remaining to $600 Cap: ${{
            Math.max(0, 600 - (selectedWallet.taxable_ytd || 0)).toFixed(2)
          }}</span
        >
      </div>

      <!-- W-9 Warning Alert & Trigger -->
      <div
        v-if="selectedWallet.w9_status === 'REQUIRED'"
        class="alert-tax-warning"
      >
        <span
          >⚠️ <strong>W-9 Required:</strong> Subject has reached or exceeded the
          $600.00 IRS 1099 threshold. Taxpayer identification certification must
          be collected.</span
        >
        <button class="btn btn-xs btn-primary" @click="showW9Modal = true">
          Submit Form W-9
        </button>
      </div>
      <div
        v-else-if="selectedWallet.w9_status === 'SUBMITTED'"
        class="alert-tax-info"
      >
        <span
          >⏳ <strong>W-9 Pending Verification:</strong> Subject submitted TIN
          ending in {{ selectedWallet.tax_id_last_four || "****" }}. Awaiting
          coordinator validation.</span
        >
        <button
          class="btn btn-xs btn-success"
          @click="verifyW9Form(selectedWallet.id)"
        >
          Verify W-9 (CRC)
        </button>
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
        📋 SoA Stipends &amp; Milestone Trigger
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
          <h3 class="panel-title">
            Participant Wallet Directory &amp; 1099 Compliance State
          </h3>
          <span class="panel-tag"
            >{{ wallets.length }} Wallets Provisioned</span
          >
        </div>
        <div class="panel-body">
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Subject ID</th>
                  <th>Study / Site</th>
                  <th>Current Balance</th>
                  <th>Taxable YTD (1099)</th>
                  <th>W-9 Status</th>
                  <th>Card / Payout Method</th>
                  <th>Anti-Coercion Cap</th>
                  <th>Status</th>
                  <th>Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="w in wallets"
                  :key="w.id"
                  :class="{ 'row-highlight': selectedWallet?.id === w.id }"
                >
                  <td>
                    <strong>{{ w.subject_id }}</strong>
                  </td>
                  <td>{{ w.study_id }} / {{ w.site_id }}</td>
                  <td class="text-success font-bold">
                    ${{ w.current_balance.toFixed(2) }} {{ w.currency }}
                  </td>
                  <td>
                    <div class="tax-cell">
                      <span>${{ (w.taxable_ytd || 0).toFixed(2) }}</span>
                      <span
                        v-if="w.is_1099_threshold_exceeded"
                        class="badge badge-danger badge-xs"
                      >
                        &ge; $600 Cap
                      </span>
                    </div>
                  </td>
                  <td>
                    <span
                      class="badge"
                      :class="{
                        'badge-gxp': w.w9_status === 'VERIFIED',
                        'badge-warning': w.w9_status === 'SUBMITTED',
                        'badge-danger': w.w9_status === 'REQUIRED',
                        'badge-standard': w.w9_status === 'NOT_REQUIRED',
                      }"
                    >
                      {{ w.w9_status || "NOT_REQUIRED" }}
                    </span>
                  </td>
                  <td>
                    <span class="badge badge-standard">{{
                      w.virtual_card_mask || "Reloadable Debit Card"
                    }}</span>
                  </td>
                  <td>
                    <span
                      v-if="w.anti_coercion_cap"
                      class="badge badge-regulatory"
                      >${{ w.anti_coercion_cap.toFixed(2) }} max</span
                    >
                    <span v-else class="text-muted">Protocol Default</span>
                  </td>
                  <td>
                    <span class="badge badge-gxp">{{ w.status }}</span>
                  </td>
                  <td>
                    <div class="action-btn-group">
                      <button
                        class="btn btn-sm btn-primary"
                        @click="selectWallet(w)"
                      >
                        Inspect / Disburse
                      </button>
                      <button
                        v-if="
                          !w.virtual_card_token ||
                          w.virtual_card_token.startsWith('VTOK')
                        "
                        class="btn btn-sm btn-secondary"
                        @click="issueReloadableCard(w.id)"
                      >
                        Issue Card
                      </button>
                    </div>
                  </td>
                </tr>
                <tr v-if="wallets.length === 0">
                  <td colspan="9" class="empty-cell">
                    No participant wallets provisioned yet.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <!-- Selected Wallet Details Drawer & Real-Time Balance Inquiry -->
          <div v-if="selectedWallet" class="selected-wallet-card">
            <div class="card-header-flex">
              <div>
                <h4 class="card-heading">
                  Active Wallet: {{ selectedWallet.subject_id }}
                </h4>
                <p class="card-subheading">
                  Study: {{ selectedWallet.study_id }} | Site:
                  {{ selectedWallet.site_id }} | Currency:
                  {{ selectedWallet.currency }}
                </p>
              </div>
              <div class="card-header-actions">
                <button
                  class="btn btn-sm btn-secondary"
                  @click="fetchBalanceInquiry(selectedWallet.id)"
                >
                  Inquire Balance
                </button>
                <button
                  class="btn btn-sm btn-secondary"
                  @click="selectedWallet = null"
                >
                  Close
                </button>
              </div>
            </div>

            <!-- Balance Inquiry Stats -->
            <div v-if="balanceInquiryData" class="balance-inquiry-box">
              <div class="inquiry-stat">
                <span class="inquiry-label">Available Balance</span>
                <span class="inquiry-val text-success font-bold"
                  >${{ balanceInquiryData.available_balance.toFixed(2) }}</span
                >
              </div>
              <div class="inquiry-stat">
                <span class="inquiry-label">Pending Expense Claims</span>
                <span class="inquiry-val text-warning font-bold"
                  >${{
                    balanceInquiryData.pending_claims_total.toFixed(2)
                  }}</span
                >
              </div>
              <div class="inquiry-stat">
                <span class="inquiry-label">Lifetime Stipends</span>
                <span class="inquiry-val"
                  >${{ balanceInquiryData.lifetime_stipends.toFixed(2) }}</span
                >
              </div>
              <div class="inquiry-stat">
                <span class="inquiry-label">Lifetime Reimbursements</span>
                <span class="inquiry-val"
                  >${{
                    balanceInquiryData.lifetime_reimbursements.toFixed(2)
                  }}</span
                >
              </div>
              <div class="inquiry-stat">
                <span class="inquiry-label">1099 Taxable YTD</span>
                <span class="inquiry-val"
                  >${{ balanceInquiryData.taxable_ytd.toFixed(2) }}</span
                >
              </div>
            </div>

            <div class="credentials-grid">
              <div class="cred-card virtual-card-preview">
                <div class="card-chip">
                  💳 {{ selectedWallet.card_type || "Reloadable Debit Card" }}
                </div>
                <div class="card-number">
                  {{
                    selectedWallet.virtual_card_mask || "**** **** **** 4821"
                  }}
                </div>
                <div class="card-exp">
                  EXP: {{ selectedWallet.virtual_card_expiry || "12/29" }} |
                  Status: {{ selectedWallet.card_status || "ACTIVE" }}
                </div>
                <div class="card-holder">
                  {{
                    selectedWallet.ach_account_holder ||
                    "Participant " + selectedWallet.subject_id
                  }}
                </div>
              </div>
              <div class="cred-card ach-preview">
                <div class="card-chip">🏦 Direct ACH Payout Account</div>
                <div class="ach-row">
                  <strong>Routing:</strong>
                  {{ selectedWallet.ach_routing_number || "021000021" }}
                </div>
                <div class="ach-row">
                  <strong>Account:</strong>
                  {{ selectedWallet.ach_account_mask || "ACH-***4321" }}
                </div>
                <div class="ach-row">
                  <strong>Holder:</strong>
                  {{ selectedWallet.ach_account_holder }}
                </div>
                <div class="ach-row">
                  <strong>W-9 Form:</strong>
                  <span class="badge badge-gxp">{{
                    selectedWallet.w9_status
                  }}</span>
                </div>
              </div>
            </div>

            <div class="disburse-action-bar">
              <div class="disburse-input-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Payout Method:</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <select v-model="disburseForm.type" class="form-select">
                  <option value="VIRTUAL_CARD">
                    Instant Reloadable Debit Card
                  </option>
                  <option value="DIRECT_ACH">Direct Bank ACH Transfer</option>
                </select>
              </div>
              <div class="disburse-input-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Amount ($):</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
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
                🚀 Disburse Funds (${{
                  (
                    disburseForm.amount || selectedWallet.current_balance
                  ).toFixed(2)
                }})
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: SoA Protocol Stipend Rules & Milestone Triggers -->
    <div v-if="activeTab === 'stipends'" class="tab-content">
      <div class="grid-2-col">
        <!-- Stipend Rules Catalog -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">
              Schedule of Activities (SoA) Compensation Matrix
            </h3>
            <span class="panel-tag"
              >{{ stipendRules.length }} Active Rules</span
            >
          </div>
          <div class="panel-body">
            <div class="table-container">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Visit Code</th>
                    <th>Visit Name</th>
                    <th>Compensation</th>
                    <th>Milestone</th>
                    <th>Anti-Coercion Cap</th>
                    <th>Taxable</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in stipendRules" :key="r.id">
                    <td>
                      <code>{{ r.visit_code }}</code>
                    </td>
                    <td>
                      <strong>{{ r.visit_name }}</strong>
                    </td>
                    <td class="text-success font-bold">
                      ${{ r.amount.toFixed(2) }} {{ r.currency }}
                    </td>
                    <td>
                      <span
                        v-if="r.is_milestone"
                        class="badge badge-regulatory"
                        >{{ r.milestone_code || "Milestone" }}</span
                      >
                      <span v-else class="text-muted">Standard Visit</span>
                    </td>
                    <td>
                      <span
                        v-if="r.anti_coercion_cap"
                        class="badge badge-warning"
                        >${{ r.anti_coercion_cap.toFixed(2) }} limit</span
                      >
                      <span v-else class="text-muted">Standard</span>
                    </td>
                    <td>
                      <span class="badge badge-standard">{{
                        r.taxable ? "1099 Taxable" : "Non-Taxable"
                      }}</span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>

        <!-- Visit Trigger Simulator -->
        <div class="panel-box">
          <div class="panel-header">
            <h3 class="panel-title">
              ⚡ EDC Verified Visit Completion Trigger Simulator
            </h3>
          </div>
          <div class="panel-body">
            <p class="info-desc">
              Simulate an inbound webhook or coordinator confirmation from EDC.
              Upon verified clinic visit completion, the SoA protocol rule
              automatically credits the subject's wallet and updates IRS 1099
              tax counters.
            </p>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Study Protocol:</label>
              <input v-model="visitEventForm.study_id" class="form-input" />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Site ID:</label>
              <input v-model="visitEventForm.site_id" class="form-input" />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Subject ID / MRN:</label>
              <input v-model="visitEventForm.subject_id" class="form-input" />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Completed Visit:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <select v-model="visitEventForm.visit_code" class="form-select">
                <option
                  v-for="r in stipendRules"
                  :key="r.id"
                  :value="r.visit_code"
                >
                  {{ r.visit_name }} ({{ r.visit_code }}) - ${{
                    r.amount.toFixed(2)
                  }}
                  {{ r.taxable ? "[Taxable]" : "" }}
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
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <div class="ocr-dropzone" @click="simulateReceiptUpload">
              <div class="dropzone-icon">📷</div>
              <div class="dropzone-text">
                <strong>Click to Upload Receipt Image / Sample OCR</strong>
                <p>
                  Supports JPG, PNG, PDF receipts for Mileage, Parking, Transit,
                  Meals, Lodging
                </p>
              </div>
            </div>

            <div class="sample-receipt-presets">
              <span class="preset-label">Quick Sample Scenarios:</span>
              <button
                class="btn btn-xs btn-secondary"
                @click="loadSampleReceipt('transit')"
              >
                Metro Transit ($28.50)
              </button>
              <button
                class="btn btn-xs btn-secondary"
                @click="loadSampleReceipt('parking')"
              >
                Hospital Parking ($22.00)
              </button>
              <button
                class="btn btn-xs btn-secondary"
                @click="loadSampleReceipt('gas')"
              >
                Mileage / Gas ($45.00)
              </button>
              <button
                class="btn btn-xs btn-secondary"
                @click="loadSampleReceipt('meal_standard')"
              >
                Lunch Cafe ($18.75)
              </button>
              <button
                class="btn btn-xs btn-secondary"
                @click="loadSampleReceipt('meal_excess')"
              >
                Dinner Above $75 ($95.00)
              </button>
            </div>

            <div v-if="ocrResult" class="ocr-result-card">
              <div class="ocr-header">
                <h4>
                  Extracted Metadata (Confidence:
                  {{ (ocrResult.confidence_score * 100).toFixed(0) }}%)
                </h4>
                <span class="badge badge-gxp">{{ ocrResult.category }}</span>
              </div>
              <div class="ocr-grid">
                <div>
                  <strong>Merchant:</strong> {{ ocrResult.merchant_name }}
                </div>
                <div><strong>Date:</strong> {{ ocrResult.expense_date }}</div>
                <div>
                  <strong>Gross Amount:</strong> ${{
                    ocrResult.gross_amount.toFixed(2)
                  }}
                </div>
                <div>
                  <strong>Tax Offset:</strong> ${{
                    ocrResult.tax_deduction.toFixed(2)
                  }}
                </div>
                <div>
                  <strong>Net Reimbursement:</strong>
                  <span class="text-success font-bold"
                    >${{ ocrResult.net_amount.toFixed(2) }}</span
                  >
                </div>
              </div>
              <div v-if="ocrResult.tax_deduction > 0" class="tax-offset-notice">
                ⚠️ Gross amount exceeds the $75.00 meal per-diem cap. Tax
                withholding of ${{ ocrResult.tax_deduction.toFixed(2) }}
                deducted from reimbursement.
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
            <h3 class="panel-title">
              Submit Travel / Meal Reimbursement Claim
            </h3>
          </div>
          <div class="panel-body">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Participant Subject ID:</label>
              <input v-model="expenseForm.subject_id" class="form-input" />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Study ID / Site ID:</label>
              <div class="form-row-2">
                <input v-model="expenseForm.study_id" class="form-input" />
                <input v-model="expenseForm.site_id" class="form-input" />
              </div>
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Category:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
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
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Merchant / Vendor Name:</label>
              <input v-model="expenseForm.merchant_name" class="form-input" />
            </div>
            <div class="form-row-2">
              <div class="form-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Gross Amount ($):</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
                  v-model.number="expenseForm.gross_amount"
                  type="number"
                  step="0.01"
                  class="form-input"
                />
              </div>
              <div class="form-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Expense Date:</label>
                <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
                <input
                  v-model="expenseForm.expense_date"
                  type="date"
                  class="form-input"
                />
              </div>
            </div>
            <div v-if="expenseForm.category === 'MEAL'" class="form-group">
              <label class="checkbox-label">
                <input
                  v-model="expenseForm.is_meal_allowance"
                  type="checkbox"
                />
                Apply standard $75.00 daily per-diem cap
              </label>
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
          <h3 class="panel-title">
            1-Click Coordinator Expense Claim Review Queue
          </h3>
          <span class="badge badge-regulatory"
            >{{ pendingClaims.length }} Pending Decisions</span
          >
        </div>
        <div class="panel-body">
          <div class="batch-action-bar" v-if="pendingClaims.length > 0">
            <div class="batch-summary">
              <span v-if="selectedClaimIds.length === 0" class="text-muted">
                Select claims below for batch approval or rejection (up to 100).
              </span>
              <span v-else class="text-primary font-bold">
                Selected: {{ selectedClaimIds.length }} claim(s) | Total: ${{ selectedClaimsTotalAmount.toFixed(2) }}
              </span>
            </div>
            <div class="batch-controls">
              <input
                type="text"
                v-model="batchReviewNotes"
                placeholder="Optional batch review notes..."
                class="form-control form-control-sm batch-notes-input"
                :disabled="isSubmitting"
              />
              <button
                class="btn btn-sm btn-success"
                :disabled="isSubmitting || selectedClaimIds.length === 0 || selectedClaimIds.length > 100"
                @click="batchReviewClaims('APPROVED')"
              >
                ✓ Batch Approve ({{ selectedClaimIds.length }})
              </button>
              <button
                class="btn btn-sm btn-danger"
                :disabled="isSubmitting || selectedClaimIds.length === 0 || selectedClaimIds.length > 100"
                @click="batchReviewClaims('REJECTED')"
              >
                ✕ Batch Reject ({{ selectedClaimIds.length }})
              </button>
            </div>
          </div>
          <div class="table-container">
            <table class="data-table">
              <thead>
                <tr>
                  <th class="text-center" style="width: 40px">
                    <input
                      type="checkbox"
                      :checked="isAllSelected"
                      :disabled="isSubmitting || pendingClaims.length === 0"
                      @change="toggleSelectAll"
                      title="Select all pending claims"
                    />
                  </th>
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
                <tr
                  v-for="c in pendingClaims"
                  :key="c.id"
                  :class="{ 'row-selected': selectedClaimIds.includes(c.id) }"
                >
                  <td class="text-center">
                    <input
                      type="checkbox"
                      :value="c.id"
                      v-model="selectedClaimIds"
                      :disabled="isSubmitting"
                    />
                  </td>
                  <td>
                    <code>{{ c.id.substring(0, 8) }}...</code>
                  </td>
                  <td>
                    <strong>{{ c.subject_id }}</strong>
                  </td>
                  <td>{{ c.expense_date }}</td>
                  <td>
                    <span class="badge badge-standard">{{ c.category }}</span>
                  </td>
                  <td>{{ c.merchant_name }}</td>
                  <td>${{ c.gross_amount.toFixed(2) }}</td>
                  <td>${{ c.tax_deduction.toFixed(2) }}</td>
                  <td class="text-success font-bold">
                    ${{ c.net_amount.toFixed(2) }}
                  </td>
                  <td>
                    <span class="badge badge-gxp">{{
                      c.receipt_image_name || "Receipt.jpg"
                    }}</span>
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
                  <td colspan="11" class="empty-cell">
                    🎉 No pending expense claims in queue!
                  </td>
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
          <h3 class="panel-title">
            21 CFR Part 11 Immutable Participant Financial Ledger
          </h3>
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
                  <td>
                    <strong>{{ tx.subject_id }}</strong>
                  </td>
                  <td>{{ tx.study_id }}</td>
                  <td>
                    <span
                      class="badge"
                      :class="tx.amount >= 0 ? 'badge-gxp' : 'badge-regulatory'"
                    >
                      {{ tx.transaction_type }}
                    </span>
                  </td>
                  <td
                    :class="
                      tx.amount >= 0
                        ? 'text-success font-bold'
                        : 'text-danger font-bold'
                    "
                  >
                    {{ tx.amount >= 0 ? "+" : "" }}${{ tx.amount.toFixed(2) }}
                  </td>
                  <td>${{ tx.balance_after.toFixed(2) }}</td>
                  <td>{{ tx.description }}</td>
                </tr>
                <tr v-if="ledgerTransactions.length === 0">
                  <td colspan="7" class="empty-cell">
                    No transaction ledger records recorded yet.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>
    </div>

    <!-- Modal: Form W-9 Taxpayer Certification -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div
      v-if="showW9Modal"
      class="modal-backdrop"
      @click.self="showW9Modal = false"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h3>IRS Form W-9 Taxpayer Certification</h3>
          <button class="modal-close-btn" @click="showW9Modal = false">
            &times;
          </button>
        </div>
        <div class="modal-body">
          <p class="modal-desc">
            Under penalties of perjury, certify that the Taxpayer Identification
            Number (TIN) provided below is accurate.
          </p>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Legal Taxpayer Name:</label>
            <input
              v-model="w9Form.full_legal_name"
              class="form-input"
              placeholder="e.g. Jane Doe"
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Last 4 Digits of SSN / ITIN:</label>
            <input
              v-model="w9Form.tax_id_last_four"
              maxlength="4"
              class="form-input"
              placeholder="e.g. 1234"
            />
          </div>
          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label>Mailing Address:</label>
            <input
              v-model="w9Form.address"
              class="form-input"
              placeholder="123 Clinical Way, City, State"
            />
          </div>
          <div class="form-group">
            <label class="checkbox-label">
              <input v-model="w9Form.certify_accurate" type="checkbox" />
              I certify under penalty of perjury that the TIN entered above is
              correct.
            </label>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showW9Modal = false">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="
              !w9Form.certify_accurate || w9Form.tax_id_last_four.length !== 4
            "
            @click="submitW9Form"
          >
            Submit W-9 Certification
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import { useToastStore } from "../stores/toasts.js";
import { ensureOk } from "../utils/apiErrors.js";
import { isDemoBuild } from "../utils/demoMode.js";

const activeTab = ref("wallets");
const isLoading = ref(false);
const isSubmitting = ref(false);
const showW9Modal = ref(false);
const visitTriggerSuccess = ref("");
const claimSubmitSuccess = ref("");

const selectedWallet = ref<any>(null);
const selectedWalletTaxStatus = ref<any>(null);
const balanceInquiryData = ref<any>(null);
const wallets = ref<any[]>([]);
const stipendRules = ref<any[]>([]);
const expenseClaims = ref<any[]>([]);
const ledgerTransactions = ref<any[]>([]);
const ocrResult = ref<any>(null);

const disburseForm = ref({
  type: "VIRTUAL_CARD",
  amount: 0,
});

const w9Form = ref({
  full_legal_name: "",
  tax_id_last_four: "",
  address: "",
  certify_accurate: true,
});

const visitEventForm = ref({
  study_id: "STUDY-DCT-001",
  site_id: "SITE-101",
  subject_id: "SUBJ-1001",
  visit_code: "V1_SCREENING",
});

const expenseForm = ref({
  subject_id: "SUBJ-1001",
  study_id: "STUDY-DCT-001",
  site_id: "SITE-101",
  category: "TRANSIT",
  merchant_name: "Metro Transit Authority",
  gross_amount: 28.5,
  is_meal_allowance: false,
  expense_date: new Date().toISOString().substring(0, 10),
});

const totalBalance = computed(() =>
  wallets.value.reduce((acc, w) => acc + (w.current_balance || 0), 0)
);
const totalStipends = computed(() =>
  wallets.value.reduce((acc, w) => acc + (w.lifetime_stipends || 0), 0)
);
const totalReimbursements = computed(() =>
  wallets.value.reduce((acc, w) => acc + (w.lifetime_reimbursements || 0), 0)
);
const totalTaxableYtd = computed(() =>
  wallets.value.reduce((acc, w) => acc + (w.taxable_ytd || 0), 0)
);
const pendingClaims = computed(() =>
  expenseClaims.value.filter((c) => c.status === "PENDING_REVIEW")
);
const pendingClaimsCount = computed(() => pendingClaims.value.length);

const selectedClaimIds = ref<string[]>([]);
const batchReviewNotes = ref("");

const isAllSelected = computed(() => {
  return (
    pendingClaims.value.length > 0 &&
    selectedClaimIds.value.length === pendingClaims.value.length
  );
});

const selectedClaimsTotalAmount = computed(() => {
  const selectedSet = new Set(selectedClaimIds.value);
  return pendingClaims.value
    .filter((c) => selectedSet.has(c.id))
    .reduce(
      (sum, c) =>
        sum +
        (c.net_amount ??
          c.netAmount ??
          c.gross_amount ??
          c.grossAmount ??
          0),
      0
    );
});

function toggleSelectAll(event: Event) {
  const checked = (event.target as HTMLInputElement).checked;
  if (checked) {
    selectedClaimIds.value = pendingClaims.value.map((c) => c.id);
  } else {
    selectedClaimIds.value = [];
  }
}

function formatDate(iso: string) {
  if (!iso) return "-";
  return new Date(iso).toLocaleString();
}

async function selectWallet(w: any) {
  selectedWallet.value = w;
  disburseForm.value.amount = w.current_balance;
  await Promise.all([fetchTaxStatus(w.id), fetchBalanceInquiry(w.id)]);
}

async function fetchTaxStatus(walletId: string) {
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${walletId}/tax-1099-status`
    );
    if (res.ok) {
      selectedWalletTaxStatus.value = await res.json();
    }
  } catch (e) {
    console.error("Error fetching tax status:", e);
  }
}

async function fetchBalanceInquiry(walletId: string) {
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${walletId}/balance`
    );
    if (res.ok) {
      balanceInquiryData.value = await res.json();
    }
  } catch (e) {
    console.error("Error fetching balance inquiry:", e);
  }
}

async function issueReloadableCard(walletId: string) {
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${walletId}/issue-card`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ card_type: "RELOADABLE_DEBIT" }),
      }
    );
    if (res.ok) {
      await loadAllData();
    }
  } catch (e) {
    console.error("Error issuing reloadable card:", e);
  }
}

async function submitW9Form() {
  if (!selectedWallet.value) return;
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${selectedWallet.value.id}/submit-w9`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          full_legal_name: w9Form.value.full_legal_name,
          tax_id_last_four: w9Form.value.tax_id_last_four,
          address: w9Form.value.address,
          certify_accurate: w9Form.value.certify_accurate,
        }),
      }
    );
    if (res.ok) {
      showW9Modal.value = false;
      await loadAllData();
      if (selectedWallet.value) {
        await selectWallet(selectedWallet.value);
      }
    }
  } catch (e) {
    console.error("Error submitting W-9 form:", e);
  }
}

async function verifyW9Form(walletId: string) {
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${walletId}/verify-w9`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          notes: "TIN matched against IRS TIN verification ledger.",
        }),
      }
    );
    if (res.ok) {
      await loadAllData();
      if (selectedWallet.value) {
        await selectWallet(selectedWallet.value);
      }
    }
  } catch (e) {
    console.error("Error verifying W-9:", e);
  }
}

function simulateReceiptUpload() {
  loadSampleReceipt("transit");
}

function loadSampleReceipt(scenario: string) {
  if (scenario === "transit") {
    ocrResult.value = {
      merchant_name: "Metro Transit Authority",
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 28.5,
      tax_deduction: 0.0,
      net_amount: 28.5,
      category: "TRANSIT",
      confidence_score: 0.98,
      detected_keywords: ["transit", "ticket", "fare"],
      raw_text:
        "METRO TRANSIT AUTHORITY\nRoute: Clinical Trial Express\nDate: 2026-08-23\nFare Paid: $28.50\nCard: **** 4821",
    };
    expenseForm.value.merchant_name = "Metro Transit Authority";
    expenseForm.value.gross_amount = 28.5;
    expenseForm.value.category = "TRANSIT";
  } else if (scenario === "parking") {
    ocrResult.value = {
      merchant_name: "Medical Center Parking Garage",
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 22.0,
      tax_deduction: 0.0,
      net_amount: 22.0,
      category: "PARKING",
      confidence_score: 0.97,
      detected_keywords: ["parking", "garage", "valet"],
      raw_text:
        "MEDICAL CENTER PARKING GARAGE\nDate: 2026-08-23\nEntry: 08:30 Exit: 14:15\nTotal Due: $22.00\nPAID IN FULL",
    };
    expenseForm.value.merchant_name = "Medical Center Parking Garage";
    expenseForm.value.gross_amount = 22.0;
    expenseForm.value.category = "PARKING";
  } else if (scenario === "gas") {
    ocrResult.value = {
      merchant_name: "Shell Service Station",
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 45.0,
      tax_deduction: 0.0,
      net_amount: 45.0,
      category: "MILEAGE",
      confidence_score: 0.95,
      detected_keywords: ["gas", "fuel", "pump"],
      raw_text:
        "SHELL OIL STATION #4411\nPump 04 - Regular Unleaded\nGallons: 11.25\nTotal: $45.00",
    };
    expenseForm.value.merchant_name = "Shell Service Station";
    expenseForm.value.gross_amount = 45.0;
    expenseForm.value.category = "MILEAGE";
  } else if (scenario === "meal_excess") {
    ocrResult.value = {
      merchant_name: "Hospital Plaza Bistro",
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 95.0,
      tax_deduction: 3.0,
      net_amount: 92.0,
      category: "MEAL",
      confidence_score: 0.96,
      detected_keywords: ["bistro", "dinner", "taxable_meal_excess"],
      raw_text:
        "HOSPITAL PLAZA BISTRO\nExtended Observation Dinner\nTotal: $95.00",
    };
    expenseForm.value.merchant_name = "Hospital Plaza Bistro";
    expenseForm.value.gross_amount = 95.0;
    expenseForm.value.category = "MEAL";
    expenseForm.value.is_meal_allowance = true;
  } else {
    ocrResult.value = {
      merchant_name: "Hospital Plaza Cafe",
      expense_date: new Date().toISOString().substring(0, 10),
      gross_amount: 18.75,
      tax_deduction: 0.0,
      net_amount: 18.75,
      category: "MEAL",
      confidence_score: 0.96,
      detected_keywords: ["cafe", "meal", "lunch"],
      raw_text:
        "HOSPITAL PLAZA CAFE\n1x Patient Lunch Special\n1x Bottled Water\nTotal: $18.75",
    };
    expenseForm.value.merchant_name = "Hospital Plaza Cafe";
    expenseForm.value.gross_amount = 18.75;
    expenseForm.value.category = "MEAL";
  }
}

async function loadAllData() {
  isLoading.value = true;
  try {
    const [wRes, rRes, cRes] = await Promise.all([
      fetch("/api/v1/payments/participant/wallets"),
      fetch("/api/v1/payments/participant/stipend-rules"),
      fetch("/api/v1/payments/participant/expenses"),
    ]);

    // #4827: outside demo builds a failed request is an error, never an empty or sample wallet.
    if (!isDemoBuild()) {
      await Promise.all([wRes, rRes, cRes].map(ensureOk));
    }
    if (wRes.ok) wallets.value = await wRes.json();
    if (rRes.ok) stipendRules.value = await rRes.json();
    if (cRes.ok) expenseClaims.value = await cRes.json();

    // Sample stipend rules and wallets, in demo builds only.
    if (isDemoBuild() && stipendRules.value.length === 0) {
      stipendRules.value = [
        {
          id: "1",
          visit_code: "V1_SCREENING",
          visit_name: "Screening & Consent",
          amount: 75.0,
          currency: "USD",
          taxable: false,
          anti_coercion_cap: 100.0,
          is_milestone: false,
        },
        {
          id: "2",
          visit_code: "V2_BASELINE",
          visit_name: "Baseline & Randomization",
          amount: 100.0,
          currency: "USD",
          taxable: false,
          anti_coercion_cap: 150.0,
          is_milestone: false,
        },
        {
          id: "3",
          visit_code: "V3_CYCLE_1",
          visit_name: "Treatment Cycle 1 Visit",
          amount: 150.0,
          currency: "USD",
          taxable: true,
          anti_coercion_cap: 200.0,
          is_milestone: false,
        },
        {
          id: "4",
          visit_code: "V4_CYCLE_2",
          visit_name: "Treatment Cycle 2 Visit",
          amount: 150.0,
          currency: "USD",
          taxable: true,
          anti_coercion_cap: 200.0,
          is_milestone: false,
        },
        {
          id: "5",
          visit_code: "V5_CYCLE_3",
          visit_name: "Treatment Cycle 3 Visit",
          amount: 150.0,
          currency: "USD",
          taxable: true,
          anti_coercion_cap: 200.0,
          is_milestone: false,
        },
        {
          id: "6",
          visit_code: "V6_END_STUDY",
          visit_name: "End of Study Milestone",
          amount: 200.0,
          currency: "USD",
          taxable: true,
          anti_coercion_cap: 250.0,
          is_milestone: true,
          milestone_code: "MILESTONE_PRIMARY",
        },
      ];
    }

    if (isDemoBuild() && wallets.value.length === 0) {
      wallets.value = [
        {
          id: "w-101",
          subject_id: "SUBJ-1001",
          study_id: "STUDY-DCT-001",
          site_id: "SITE-101",
          current_balance: 325.0,
          lifetime_stipends: 250.0,
          lifetime_reimbursements: 75.0,
          lifetime_disbursements: 0.0,
          taxable_ytd: 450.0,
          is_1099_threshold_exceeded: false,
          w9_status: "NOT_REQUIRED",
          currency: "USD",
          status: "ACTIVE",
          card_type: "RELOADABLE_DEBIT",
          card_status: "ACTIVE",
          virtual_card_mask: "**** **** **** 4821",
          virtual_card_expiry: "12/29",
          ach_routing_number: "021000021",
          ach_account_mask: "ACH-***4321",
          ach_account_holder: "Participant SUBJ-1001",
        },
        {
          id: "w-102",
          subject_id: "SUBJ-1002",
          study_id: "STUDY-DCT-001",
          site_id: "SITE-101",
          current_balance: 175.0,
          lifetime_stipends: 650.0,
          lifetime_reimbursements: 0.0,
          lifetime_disbursements: 475.0,
          taxable_ytd: 650.0,
          is_1099_threshold_exceeded: true,
          w9_status: "REQUIRED",
          currency: "USD",
          status: "ACTIVE",
          card_type: "RELOADABLE_DEBIT",
          card_status: "ACTIVE",
          virtual_card_mask: "**** **** **** 9012",
          virtual_card_expiry: "06/28",
          ach_routing_number: "021000021",
          ach_account_mask: "ACH-***9012",
          ach_account_holder: "Participant SUBJ-1002",
        },
      ];
    }
  } catch (e) {
    useToastStore().apiError("Loading participant wallets failed", e);
  } finally {
    isLoading.value = false;
  }
}

async function triggerVisitVerified() {
  isSubmitting.value = true;
  visitTriggerSuccess.value = "";
  try {
    const res = await fetch(
      "/api/v1/payments/participant/events/visit-verified",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          study_id: visitEventForm.value.study_id,
          site_id: visitEventForm.value.site_id,
          subject_id: visitEventForm.value.subject_id,
          visit_code: visitEventForm.value.visit_code,
          event_id: `EVT-${Date.now()}`,
          verified_by: "coordinator_alice",
        }),
      }
    );
    await ensureOk(res);
    const data = await res.json();
    // Report only the amounts the service returned (#4827).
    visitTriggerSuccess.value =
      typeof data.amount === "number" &&
      typeof data.current_balance === "number"
        ? `Credited $${data.amount.toFixed(2)} stipend for visit ${visitEventForm.value.visit_code}. New balance: $${data.current_balance.toFixed(2)}`
        : `Stipend for visit ${visitEventForm.value.visit_code} recorded.`;
    await loadAllData();
  } catch (e) {
    useToastStore().apiError("Crediting the visit stipend failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

async function submitExpenseClaim() {
  isSubmitting.value = true;
  claimSubmitSuccess.value = "";
  try {
    const res = await fetch("/api/v1/payments/participant/expenses", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        subject_id: expenseForm.value.subject_id,
        study_id: expenseForm.value.study_id,
        site_id: expenseForm.value.site_id,
        category: expenseForm.value.category,
        merchant_name: expenseForm.value.merchant_name,
        gross_amount: expenseForm.value.gross_amount,
        is_meal_allowance: expenseForm.value.is_meal_allowance,
        per_diem_cap: expenseForm.value.is_meal_allowance ? 75.0 : undefined,
        expense_date: expenseForm.value.expense_date,
        receipt_image_name: "receipt_scanned.jpg",
        receipt_ocr_data: ocrResult.value || {},
      }),
    });
    await ensureOk(res);
    claimSubmitSuccess.value = `Expense claim of $${expenseForm.value.gross_amount.toFixed(2)} submitted.`;
    await loadAllData();
    activeTab.value = "approvals";
  } catch (e) {
    useToastStore().apiError("Submitting the expense claim failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

async function reviewClaim(claimId: string, decision: string) {
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/payments/participant/expenses/${claimId}/review`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          status: decision,
          review_notes:
            decision === "APPROVED"
              ? "Verified valid clinical travel receipt."
              : "Non-compliant receipt documentation.",
        }),
      }
    );
    await ensureOk(res);
    await loadAllData();
  } catch (e) {
    useToastStore().apiError("Reviewing the expense claim failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

async function batchReviewClaims(decision: string) {
  if (selectedClaimIds.value.length === 0) return;
  if (selectedClaimIds.value.length > 100) {
    useToastStore().warning(
      "Cannot review more than 100 claims in a single batch request."
    );
    return;
  }

  isSubmitting.value = true;
  try {
    const res = await fetch(
      "/api/v1/payments/participant/expenses/batch-review",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          claim_ids: selectedClaimIds.value,
          action: decision,
          review_notes:
            batchReviewNotes.value ||
            (decision === "APPROVED"
              ? "Batch approved by site coordinator."
              : "Batch rejected by site coordinator."),
        }),
      }
    );
    await ensureOk(res);
    selectedClaimIds.value = [];
    batchReviewNotes.value = "";
    await loadAllData();
  } catch (e) {
    useToastStore().apiError("Batch reviewing expense claims failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

async function triggerDisbursement() {
  if (!selectedWallet.value) return;
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${selectedWallet.value.id}/disburse`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          disbursement_type: disburseForm.value.type,
          amount:
            disburseForm.value.amount || selectedWallet.value.current_balance,
        }),
      }
    );
    await ensureOk(res);
    await loadAllData();
    selectedWallet.value = null;
    activeTab.value = "disbursements";
  } catch (e) {
    useToastStore().apiError("Disbursement failed. Nothing was paid out", e);
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
  padding: var(--semantic-spacing-xl, 24px);
  max-width: 1600px;
  margin: 0 auto;
  font-family: var(--semantic-font-family, sans-serif);
  color: var(--semantic-color-text, #0f172a);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: var(--semantic-spacing-xl, 24px);
  border-bottom: 1px solid var(--semantic-color-border, #e2e8f0);
  padding-bottom: var(--semantic-spacing-md, 16px);
}

.header-badge-row {
  display: flex;
  gap: var(--semantic-spacing-xs, 8px);
  margin-bottom: var(--semantic-spacing-xs, 8px);
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

.badge-xs {
  padding: 2px 4px;
  font-size: 0.68rem;
  margin-left: 4px;
}

.badge-gxp {
  background-color: var(--semantic-color-success-bg, #dcfce7);
  color: var(--semantic-color-success, #15803d);
  border: 1px solid #a7f3d0;
}

.badge-regulatory {
  background-color: var(--semantic-color-primary-light, #e0f2fe);
  color: var(--semantic-color-primary, #026597);
  border: 1px solid #bfdbfe;
}

.badge-warning {
  background-color: var(--semantic-color-warning-bg, #fef9c3);
  color: var(--semantic-color-warning, #854d0e);
  border: 1px solid #fde047;
}

.badge-danger {
  background-color: var(--semantic-color-error-bg, #fee2e2);
  color: var(--semantic-color-error, #b91c1c);
  border: 1px solid #fca5a5;
}

.badge-standard {
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  color: var(--semantic-color-text-muted, #475569);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
}

.view-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--semantic-color-text, #0f172a);
  margin: 0 0 6px 0;
}

.view-subtitle {
  font-size: 0.88rem;
  color: var(--semantic-color-text-muted, #475569);
  margin: 0;
  max-width: 900px;
  line-height: 1.4;
}

.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: var(--semantic-spacing-md, 16px);
  margin-bottom: var(--semantic-spacing-xl, 24px);
}

.stat-card {
  display: flex;
  align-items: center;
  gap: var(--semantic-spacing-md, 16px);
  padding: var(--semantic-spacing-md, 16px);
  background-color: var(--semantic-color-surface, #ffffff);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
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
  background-color: var(--semantic-color-surface-muted, #f8fafc);
}

.stat-value {
  font-size: 1.35rem;
  font-weight: 700;
  color: var(--semantic-color-text, #0f172a);
}

.stat-label {
  font-size: 0.8rem;
  color: var(--semantic-color-text-muted, #475569);
  margin-top: 2px;
}

.tax-threshold-banner {
  background-color: var(--semantic-color-surface, #ffffff);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
  border-left: 4px solid var(--semantic-color-primary, #026597);
  border-radius: 8px;
  padding: var(--semantic-spacing-md, 16px);
  margin-bottom: var(--semantic-spacing-xl, 24px);
}

.tax-banner-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: var(--semantic-spacing-xs, 8px);
}

.tax-title-group {
  display: flex;
  align-items: center;
  gap: var(--semantic-spacing-sm, 12px);
}

.tax-icon-badge {
  font-size: 1.5rem;
}

.tax-subtext {
  display: block;
  font-size: 0.78rem;
  color: var(--semantic-color-text-muted, #475569);
}

.tax-badge-group {
  display: flex;
  gap: var(--semantic-spacing-xs, 8px);
}

.progress-bar-track {
  width: 100%;
  height: 10px;
  background-color: var(--semantic-color-surface-muted, #f1f5f9);
  border-radius: 6px;
  overflow: hidden;
  margin: var(--semantic-spacing-xs, 8px) 0;
}

.progress-bar-fill {
  height: 100%;
  transition: width 0.3s ease;
}

.fill-normal {
  background-color: var(--semantic-color-success, #15803d);
}

.fill-approaching {
  background-color: var(--semantic-color-warning, #854d0e);
}

.fill-exceeded {
  background-color: var(--semantic-color-error, #b91c1c);
}

.tax-legend {
  display: flex;
  justify-content: space-between;
  font-size: 0.78rem;
  color: var(--semantic-color-text-muted, #475569);
  margin-bottom: var(--semantic-spacing-xs, 8px);
}

.alert-tax-warning {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--semantic-color-error-bg, #fee2e2);
  border: 1px solid var(--semantic-color-error, #b91c1c);
  color: var(--semantic-color-error, #b91c1c);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  margin-top: 8px;
}

.alert-tax-info {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--semantic-color-warning-bg, #fef9c3);
  border: 1px solid var(--semantic-color-warning, #854d0e);
  color: var(--semantic-color-warning, #854d0e);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  margin-top: 8px;
}

.view-tabs-bar {
  display: flex;
  gap: var(--semantic-spacing-xs, 8px);
  border-bottom: 2px solid var(--semantic-color-border, #e2e8f0);
  margin-bottom: var(--semantic-spacing-lg, 20px);
}

.tab-btn {
  padding: 10px 16px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--semantic-color-text-muted, #475569);
  cursor: pointer;
  margin-bottom: -2px;
  transition: all 0.15s ease;
}

.tab-btn:hover {
  color: var(--semantic-color-text, #0f172a);
}

.tab-btn.active {
  color: var(--semantic-color-primary, #026597);
  border-bottom-color: var(--semantic-color-primary, #026597);
}

.panel-box {
  background-color: var(--semantic-color-surface, #ffffff);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  margin-bottom: var(--semantic-spacing-lg, 20px);
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--semantic-color-border, #e2e8f0);
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  border-top-left-radius: 8px;
  border-top-right-radius: 8px;
}

.panel-title {
  font-size: 1rem;
  font-weight: 600;
  color: var(--semantic-color-text, #0f172a);
  margin: 0;
}

.panel-tag {
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted, #475569);
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
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  color: var(--semantic-color-text-muted, #475569);
  font-weight: 600;
  border-bottom: 1px solid var(--semantic-color-border, #e2e8f0);
}

.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid #f1f5f9;
  color: var(--semantic-color-text, #0f172a);
}

.data-table tr:hover {
  background-color: var(--semantic-color-surface-muted, #f8fafc);
}

.row-highlight {
  background-color: var(--semantic-color-primary-light, #e0f2fe) !important;
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
  gap: var(--semantic-spacing-lg, 20px);
}

@media (max-width: 1024px) {
  .grid-2-col {
    grid-template-columns: 1fr;
  }
}

.selected-wallet-card {
  margin-top: 20px;
  padding: 16px;
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  border: 1px solid #cbd5e1;
  border-radius: 8px;
}

.card-header-flex {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 14px;
}

.card-header-actions {
  display: flex;
  gap: 8px;
}

.card-heading {
  margin: 0 0 4px 0;
  font-size: 1.1rem;
  font-weight: 700;
}

.card-subheading {
  margin: 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-muted, #475569);
}

.balance-inquiry-box {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 10px;
  background-color: var(--semantic-color-surface, #ffffff);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
  border-radius: 6px;
  padding: 12px;
  margin-bottom: 16px;
}

.inquiry-stat {
  display: flex;
  flex-direction: column;
}

.inquiry-label {
  font-size: 0.72rem;
  color: var(--semantic-color-text-muted, #475569);
  text-transform: uppercase;
}

.inquiry-val {
  font-size: 1rem;
  font-weight: 600;
  color: var(--semantic-color-text, #0f172a);
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
  background-color: var(--semantic-color-surface, #ffffff);
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
  color: var(--semantic-color-text, #0f172a);
}

.disburse-action-bar {
  display: flex;
  gap: 12px;
  align-items: flex-end;
  background-color: var(--semantic-color-surface, #ffffff);
  padding: 14px;
  border: 1px solid var(--semantic-color-border, #e2e8f0);
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
  color: var(--semantic-color-text-muted, #475569);
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
  background-color: var(--semantic-color-primary, #026597);
  color: #ffffff;
}

.btn-primary:hover {
  background-color: var(--semantic-color-primary-dark, #014d76);
}

.btn-secondary {
  background-color: var(--semantic-color-surface, #ffffff);
  color: var(--semantic-color-text-muted, #475569);
  border-color: var(--semantic-color-border, #e2e8f0);
}

.btn-secondary:hover {
  background-color: var(--semantic-color-surface-muted, #f8fafc);
}

.btn-success {
  background-color: var(--semantic-color-success, #15803d);
  color: #ffffff;
}

.btn-success:hover {
  background-color: #059669;
}

.btn-danger {
  background-color: var(--semantic-color-error, #b91c1c);
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
  color: var(--semantic-color-text-muted, #475569);
  margin-bottom: 4px;
}

.checkbox-label {
  display: flex !important;
  align-items: center;
  gap: 8px;
  cursor: pointer;
}

.form-input,
.form-select {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border, #e2e8f0);
  border-radius: 6px;
  font-size: 0.88rem;
  color: var(--semantic-color-text, #0f172a);
  background-color: var(--semantic-color-surface, #ffffff);
  box-sizing: border-box;
}

.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.ocr-dropzone {
  border: 2px dashed var(--semantic-color-border, #e2e8f0);
  border-radius: 8px;
  padding: 24px;
  text-align: center;
  cursor: pointer;
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  transition: all 0.15s ease;
  margin-bottom: 12px;
}

.ocr-dropzone:hover {
  border-color: var(--semantic-color-primary, #026597);
  background-color: var(--semantic-color-primary-light, #e0f2fe);
}

.dropzone-icon {
  font-size: 2rem;
  margin-bottom: 6px;
}

.dropzone-text strong {
  font-size: 0.95rem;
  color: var(--semantic-color-text, #0f172a);
}

.dropzone-text p {
  margin: 4px 0 0 0;
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted, #475569);
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
  color: var(--semantic-color-text-muted, #475569);
}

.ocr-result-card {
  padding: 14px;
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
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

.tax-offset-notice {
  background-color: var(--semantic-color-warning-bg, #fef9c3);
  border: 1px solid var(--semantic-color-warning, #854d0e);
  color: var(--semantic-color-warning, #854d0e);
  padding: 6px 10px;
  border-radius: 4px;
  font-size: 0.78rem;
  margin-bottom: 8px;
}

.ocr-raw {
  background-color: var(--semantic-color-surface, #ffffff);
  padding: 8px;
  border: 1px solid var(--semantic-color-border, #e2e8f0);
  border-radius: 4px;
}

.ocr-raw pre {
  margin: 4px 0 0 0;
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted, #475569);
  white-space: pre-wrap;
}

.alert-success-box {
  margin-top: 12px;
  padding: 10px 14px;
  background-color: var(--semantic-color-success-bg, #dcfce7);
  border: 1px solid #a7f3d0;
  color: var(--semantic-color-success, #15803d);
  border-radius: 6px;
  font-size: 0.85rem;
}

.action-btn-group {
  display: flex;
  gap: 6px;
}

.text-success {
  color: var(--semantic-color-success, #15803d);
}
.text-warning {
  color: var(--semantic-color-warning, #854d0e);
}
.text-danger {
  color: var(--semantic-color-error, #b91c1c);
}
.text-muted {
  color: var(--semantic-color-text-muted, #475569);
}
.font-bold {
  font-weight: 700;
}

.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background-color: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 999;
}

.modal-card {
  background-color: var(--semantic-color-surface, #ffffff);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
  border-radius: 8px;
  width: 100%;
  max-width: 480px;
  box-shadow: 0 10px 25px -5px rgba(0, 0, 0, 0.2);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--semantic-color-border, #e2e8f0);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
}

.modal-close-btn {
  background: none;
  border: none;
  font-size: 1.4rem;
  color: var(--semantic-color-text-muted, #475569);
  cursor: pointer;
}

.modal-body {
  padding: 20px;
}

.modal-desc {
  font-size: 0.85rem;
  color: var(--semantic-color-text-muted, #475569);
  margin-bottom: 16px;
  line-height: 1.4;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 20px;
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  border-top: 1px solid var(--semantic-color-border, #e2e8f0);
}

.batch-action-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 10px 14px;
  background-color: var(--semantic-color-surface-muted, #f8fafc);
  border: 1px solid var(--semantic-color-border, #e2e8f0);
  border-radius: 6px;
  margin-bottom: 12px;
  gap: 12px;
  flex-wrap: wrap;
}

.batch-summary {
  font-size: 0.85rem;
}

.batch-controls {
  display: flex;
  align-items: center;
  gap: 8px;
}

.batch-notes-input {
  min-width: 220px;
  font-size: 0.82rem;
}

.row-selected {
  background-color: rgba(59, 130, 246, 0.08) !important;
}
</style>
