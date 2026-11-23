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
          :disabled="isSubmitting"
          @click="verifyW9Form(selectedWallet.id)"
        >
          Verify W-9 (CRC)
        </button>
      </div>
    </div>

    <!-- Navigation Tabs -->
    <div
      ref="tablistRef"
      class="view-tabs-bar"
      role="tablist"
      aria-label="Participant stipend workspaces"
    >
      <button
        type="button"
        role="tab"
        class="tab-btn"
        :class="{ active: activeTab === 'wallets' }"
        :aria-selected="activeTab === 'wallets'"
        :tabindex="activeTab === 'wallets' ? 0 : -1"
        @click="activeTab = 'wallets'"
      >
        👛 Participant Wallets ({{ wallets.length }})
      </button>
      <button
        type="button"
        role="tab"
        class="tab-btn"
        :class="{ active: activeTab === 'stipends' }"
        :aria-selected="activeTab === 'stipends'"
        :tabindex="activeTab === 'stipends' ? 0 : -1"
        @click="activeTab = 'stipends'"
      >
        📋 SoA Stipends &amp; Milestone Trigger
      </button>
      <button
        type="button"
        role="tab"
        class="tab-btn"
        :class="{ active: activeTab === 'ocr' }"
        :aria-selected="activeTab === 'ocr'"
        :tabindex="activeTab === 'ocr' ? 0 : -1"
        @click="activeTab = 'ocr'"
      >
        📷 Mobile Receipt OCR Scanner
      </button>
      <button
        type="button"
        role="tab"
        class="tab-btn"
        :class="{ active: activeTab === 'approvals' }"
        :aria-selected="activeTab === 'approvals'"
        :tabindex="activeTab === 'approvals' ? 0 : -1"
        @click="activeTab = 'approvals'"
      >
        ⚖️ Coordinator Review Queue ({{ pendingClaims.length }})
      </button>
      <button
        type="button"
        role="tab"
        class="tab-btn"
        :class="{ active: activeTab === 'disbursements' }"
        :aria-selected="activeTab === 'disbursements'"
        :tabindex="activeTab === 'disbursements' ? 0 : -1"
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
                        :id="`btn-inspect-wallet-${w.id}`"
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
                        :disabled="isSubmitting"
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
                <h4
                  id="selected-wallet-heading"
                  class="card-heading"
                  tabindex="-1"
                >
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
                  id="btn-close-selected-wallet"
                  class="btn btn-sm btn-secondary"
                  @click="closeSelectedWallet"
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
                  {{ selectedWallet.virtual_card_mask || "Not issued" }}
                </div>
                <div class="card-exp">
                  EXP: {{ selectedWallet.virtual_card_expiry || "—" }} | Status:
                  {{ selectedWallet.card_status || "ACTIVE" }}
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
                  {{ selectedWallet.ach_routing_mask || "Not configured" }}
                </div>
                <div class="ach-row">
                  <strong>Account:</strong>
                  {{ selectedWallet.ach_account_mask || "Not configured" }}
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
            <label
              id="staff-receipt-dropzone"
              for="staff-receipt-file-input"
              class="ocr-dropzone"
            >
              <input
                id="staff-receipt-file-input"
                type="file"
                accept="image/png,image/jpeg,image/webp,image/bmp,image/svg+xml,application/pdf"
                class="sr-only-file-input"
                :disabled="isOcrExtracting"
                @change="handleReceiptFileUpload"
              />
              <span class="dropzone-icon">📷</span>
              <span class="dropzone-text">
                <strong>{{
                  isOcrExtracting
                    ? "Extracting Receipt Metadata..."
                    : "Click to Upload Receipt Image or PDF"
                }}</strong>
                <span class="dropzone-subtext">
                  Uploads receipt bytes to the OCR service for Mileage, Fuel,
                  Parking, Transit, Meals, or Lodging
                </span>
              </span>
            </label>

            <div
              v-if="ocrWarning"
              id="staff-ocr-warning-banner"
              role="status"
              class="ocr-warning-box"
            >
              ⚠️ {{ ocrWarning }}
            </div>

            <div
              v-if="ocrResult"
              id="staff-ocr-result-card"
              class="ocr-result-card"
            >
              <div class="ocr-header">
                <h4>
                  Extracted Metadata (Confidence:
                  {{ (ocrResult.confidence_score * 100).toFixed(0) }}%)
                </h4>
                <span class="badge badge-gxp">{{ ocrResult.category }}</span>
              </div>
              <div class="ocr-grid">
                <div>
                  <strong>Merchant:</strong>
                  {{ ocrResult.merchant_name || "—" }}
                </div>
                <div>
                  <strong>Date:</strong> {{ ocrResult.expense_date || "—" }}
                </div>
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
              <div v-if="ocrResult.raw_text" class="ocr-raw">
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
                <option value="MILEAGE">Mileage</option>
                <option value="FUEL">Fuel / Service Station</option>
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
          <h3 id="wallet-approvals-heading" class="panel-title" tabindex="-1">
            1-Click Coordinator Expense Claim Review Queue
          </h3>
          <span class="badge badge-regulatory"
            >{{ pendingClaims.length }} Pending Decisions</span
          >
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
                        :id="`btn-approve-claim-${c.id}`"
                        class="btn btn-xs btn-success pending-claim-approve-btn"
                        :disabled="isSubmitting"
                        @click="reviewClaim(c.id, 'APPROVED')"
                      >
                        ✓ 1-Click Approve
                      </button>
                      <button
                        :id="`btn-reject-claim-${c.id}`"
                        class="btn btn-xs btn-danger pending-claim-reject-btn"
                        :disabled="isSubmitting"
                        @click="reviewClaim(c.id, 'REJECTED')"
                      >
                        ✕ Reject
                      </button>
                    </div>
                  </td>
                </tr>
                <tr v-if="pendingClaims.length === 0">
                  <td colspan="10" class="empty-cell">
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
          <h3
            id="wallet-disbursements-heading"
            class="panel-title"
            tabindex="-1"
          >
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
                    {{ tx.amount >= 0 ? "+" : "-" }}${{
                      Math.abs(tx.amount).toFixed(2)
                    }}
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
    <ClinicalModal
      :open="showW9Modal"
      :teleport="false"
      :dismissible="!isSubmitting"
      title="IRS Form W-9 Taxpayer Certification"
      dialog-class="modal-card"
      @close="showW9Modal = false"
    >
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
      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isSubmitting"
          @click="showW9Modal = false"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="
            isSubmitting ||
            !w9Form.certify_accurate ||
            w9Form.tax_id_last_four.length !== 4
          "
          @click="submitW9Form"
        >
          Submit W-9 Certification
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from "vue";
import {
  ClinicalModal,
  handoffFocus,
  useInlineFocusHandoff,
  useRovingTabindex,
} from "ui";
import { useToastStore } from "../stores/toasts.js";
import { ensureOk } from "../utils/apiErrors.js";
import { isDemoBuild } from "../utils/demoMode.js";

const activeTab = ref("wallets");
const tablistRef = ref<HTMLElement | null>(null);
const walletTabs = ["wallets", "stipends", "ocr", "approvals", "disbursements"];
useRovingTabindex(tablistRef, (idx: number) => {
  if (walletTabs[idx]) {
    activeTab.value = walletTabs[idx];
  }
});
const isLoading = ref(false);
const isSubmitting = ref(false);
const isOcrExtracting = ref(false);
const showW9Modal = ref(false);
const visitTriggerSuccess = ref("");
const claimSubmitSuccess = ref("");
const ocrWarning = ref("");

const selectedWallet = ref<any>(null);
const selectedWalletHandoff = useInlineFocusHandoff({
  openTarget: "#selected-wallet-heading",
  scrollIfOffscreen: true,
});
const selectedWalletTaxStatus = ref<any>(null);
const balanceInquiryData = ref<any>(null);
const wallets = ref<any[]>([]);
const stipendRules = ref<any[]>([]);
const expenseClaims = ref<any[]>([]);
const ledgerTransactions = ref<any[]>([]);
const ocrResult = ref<any>(null);
const receiptImageBase64 = ref<string | null>(null);
const ocrToken = ref<string | null>(null);

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
  merchant_name: "",
  gross_amount: 0,
  is_meal_allowance: false,
  expense_date: "",
  ocr_confidence: 0,
  receipt_image_name: "",
});

function maskRoutingValue(raw?: string | null): string | null {
  if (!raw || typeof raw !== "string") return null;
  const trimmed = raw.trim();
  if (!trimmed) return null;
  if (trimmed.includes("*")) return trimmed;
  return trimmed.length > 4 ? `*****${trimmed.slice(-4)}` : `*****${trimmed}`;
}

function normalizeWallet(w: any) {
  if (!w || typeof w !== "object") return w;
  const routingMask = maskRoutingValue(
    w.ach_routing_mask ??
      w.achRoutingMask ??
      w.ach_routing_number ??
      w.achRoutingNumber ??
      null
  );
  return {
    ...w,
    id: w.id ?? w.Id,
    subject_id: w.subject_id ?? w.subjectId ?? "",
    study_id: w.study_id ?? w.studyId ?? "",
    site_id: w.site_id ?? w.siteId ?? "",
    current_balance: Number(w.current_balance ?? w.currentBalance ?? 0),
    lifetime_stipends: Number(w.lifetime_stipends ?? w.lifetimeStipends ?? 0),
    lifetime_reimbursements: Number(
      w.lifetime_reimbursements ?? w.lifetimeReimbursements ?? 0
    ),
    lifetime_disbursements: Number(
      w.lifetime_disbursements ?? w.lifetimeDisbursements ?? 0
    ),
    taxable_ytd: Number(w.taxable_ytd ?? w.taxableYtd ?? 0),
    is_1099_threshold_exceeded: Boolean(
      w.is_1099_threshold_exceeded ?? w.is1099ThresholdExceeded
    ),
    w9_status: w.w9_status ?? w.w9Status ?? "NOT_REQUIRED",
    currency: w.currency ?? w.Currency ?? "USD",
    status: w.status ?? w.Status ?? "ACTIVE",
    card_type: w.card_type ?? w.cardType ?? "RELOADABLE_DEBIT",
    card_status:
      w.card_status ?? w.cardStatus ?? w.debit_card_status ?? "ACTIVE",
    virtual_card_mask: w.virtual_card_mask ?? w.virtualCardMask ?? null,
    virtual_card_expiry: w.virtual_card_expiry ?? w.virtualCardExpiry ?? null,
    virtual_card_token: w.virtual_card_token ?? w.virtualCardToken ?? null,
    ach_routing_mask: routingMask,
    ach_account_mask: w.ach_account_mask ?? w.achAccountMask ?? null,
    ach_account_holder: w.ach_account_holder ?? w.achAccountHolder ?? null,
    anti_coercion_cap: w.anti_coercion_cap ?? w.antiCoercionCap ?? null,
    tax_id_last_four: w.tax_id_last_four ?? w.taxIdLastFour ?? null,
  };
}

function normalizeStipendRule(r: any) {
  if (!r || typeof r !== "object") return r;
  return {
    ...r,
    id: r.id ?? r.Id,
    visit_code: r.visit_code ?? r.visitCode ?? "",
    visit_name: r.visit_name ?? r.visitName ?? "",
    amount: Number(r.amount ?? r.Amount ?? 0),
    currency: r.currency ?? r.Currency ?? "USD",
    taxable: Boolean(r.taxable ?? r.Taxable),
    anti_coercion_cap: r.anti_coercion_cap ?? r.antiCoercionCap ?? null,
    is_milestone: Boolean(r.is_milestone ?? r.isMilestone),
    milestone_code: r.milestone_code ?? r.milestoneCode ?? null,
  };
}

function normalizeExpenseClaim(c: any) {
  if (!c || typeof c !== "object") return c;
  return {
    ...c,
    id: c.id ?? c.Id ?? "",
    subject_id: c.subject_id ?? c.subjectId ?? "",
    expense_date: c.expense_date ?? c.expenseDate ?? "",
    category: c.category ?? c.Category ?? "OTHER",
    merchant_name: c.merchant_name ?? c.merchantName ?? "",
    gross_amount: Number(c.gross_amount ?? c.grossAmount ?? 0),
    tax_deduction: Number(c.tax_deduction ?? c.taxDeduction ?? 0),
    net_amount: Number(c.net_amount ?? c.netAmount ?? 0),
    status: c.status ?? c.Status ?? "PENDING_REVIEW",
    receipt_image_name: c.receipt_image_name ?? c.receiptImageName ?? null,
  };
}

function normalizeTransaction(tx: any) {
  if (!tx || typeof tx !== "object") return tx;
  return {
    ...tx,
    id: tx.id ?? tx.Id ?? "",
    created_at: tx.created_at ?? tx.createdAt ?? "",
    subject_id: tx.subject_id ?? tx.subjectId ?? "",
    study_id: tx.study_id ?? tx.studyId ?? "",
    transaction_type: tx.transaction_type ?? tx.transactionType ?? "",
    amount: Number(tx.amount ?? tx.Amount ?? 0),
    balance_after: Number(tx.balance_after ?? tx.balanceAfter ?? 0),
    description: tx.description ?? tx.Description ?? "",
  };
}

function normalizeTaxStatus(t: any) {
  if (!t || typeof t !== "object") return t;
  const taxableYtd = Number(t.taxable_ytd ?? t.taxableYtd ?? 0);
  const threshold = Number(
    t.threshold_amount ?? t.thresholdAmount ?? t.threshold ?? 600
  );
  const computedPct = threshold > 0 ? (taxableYtd / threshold) * 100 : 0;
  return {
    ...t,
    taxable_ytd: taxableYtd,
    threshold_amount: threshold,
    threshold_percentage: Number(
      t.threshold_percentage ?? t.thresholdPercentage ?? computedPct
    ),
    warning_level: t.warning_level ?? t.warningLevel ?? "NORMAL",
    w9_status: t.w9_status ?? t.w9Status ?? "NOT_REQUIRED",
  };
}

function normalizeBalanceInquiry(b: any) {
  if (!b || typeof b !== "object") return b;
  return {
    ...b,
    available_balance: Number(
      b.available_balance ??
        b.availableBalance ??
        b.current_balance ??
        b.currentBalance ??
        0
    ),
    pending_claims_total: Number(
      b.pending_claims_total ?? b.pendingClaimsTotal ?? 0
    ),
    lifetime_stipends: Number(b.lifetime_stipends ?? b.lifetimeStipends ?? 0),
    lifetime_reimbursements: Number(
      b.lifetime_reimbursements ?? b.lifetimeReimbursements ?? 0
    ),
    lifetime_disbursements: Number(
      b.lifetime_disbursements ?? b.lifetimeDisbursements ?? 0
    ),
    taxable_ytd: Number(b.taxable_ytd ?? b.taxableYtd ?? 0),
  };
}

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

function formatDate(iso: string) {
  if (!iso) return "-";
  return new Date(iso).toLocaleString();
}

async function selectWallet(w: any) {
  const normalized = normalizeWallet(w);
  selectedWalletHandoff.rememberTrigger(`#btn-inspect-wallet-${normalized.id}`);
  selectedWallet.value = normalized;
  disburseForm.value.amount = normalized.current_balance;
  void selectedWalletHandoff.handoff("#selected-wallet-heading", {
    scrollIfOffscreen: true,
  });
  await Promise.all([
    fetchTaxStatus(normalized.id),
    fetchBalanceInquiry(normalized.id),
  ]);
}

function closeSelectedWallet() {
  const prevId = selectedWallet.value?.id;
  selectedWallet.value = null;
  void selectedWalletHandoff.restoreTrigger([
    prevId ? `#btn-inspect-wallet-${prevId}` : null,
    () =>
      document.querySelector<HTMLElement>(
        "#section-participant-wallet .view-tabs-bar .tab-btn.active"
      ),
  ]);
}

async function fetchTaxStatus(walletId: string) {
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${walletId}/tax-1099-status`
    );
    if (res.ok) {
      selectedWalletTaxStatus.value = normalizeTaxStatus(await res.json());
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
      balanceInquiryData.value = normalizeBalanceInquiry(await res.json());
    }
  } catch (e) {
    console.error("Error fetching balance inquiry:", e);
  }
}

async function issueReloadableCard(walletId: string) {
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${walletId}/issue-card`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          cardType: "RELOADABLE_DEBIT",
          card_type: "RELOADABLE_DEBIT",
        }),
      }
    );
    await ensureOk(res);
    await loadAllData();
    if (selectedWallet.value?.id === walletId) {
      const refreshed = wallets.value.find((w) => w.id === walletId);
      if (refreshed) await selectWallet(refreshed);
    }
  } catch (e) {
    useToastStore().apiError("Issuing reloadable card failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

async function submitW9Form() {
  if (!selectedWallet.value) return;
  const targetWalletId = selectedWallet.value.id;
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/payments/participant/wallets/${targetWalletId}/submit-w9`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          fullLegalName: w9Form.value.full_legal_name,
          taxIdLastFour: w9Form.value.tax_id_last_four,
          address: w9Form.value.address,
          certifyAccurate: w9Form.value.certify_accurate,
          full_legal_name: w9Form.value.full_legal_name,
          tax_id_last_four: w9Form.value.tax_id_last_four,
          certify_accurate: w9Form.value.certify_accurate,
        }),
      }
    );
    await ensureOk(res);
    showW9Modal.value = false;
    await loadAllData();
    const refreshed = wallets.value.find((w) => w.id === targetWalletId);
    if (refreshed) {
      await selectWallet(refreshed);
    }
  } catch (e) {
    useToastStore().apiError("Submitting W-9 form failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

async function verifyW9Form(walletId: string) {
  isSubmitting.value = true;
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
    await ensureOk(res);
    await loadAllData();
    if (selectedWallet.value?.id === walletId) {
      const refreshed = wallets.value.find((w) => w.id === walletId);
      if (refreshed) await selectWallet(refreshed);
    }
  } catch (e) {
    useToastStore().apiError("Verifying W-9 form failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

function bytesToBase64(uint8: Uint8Array): string {
  let binary = "";
  const chunkSize = 0x8000;
  for (let i = 0; i < uint8.length; i += chunkSize) {
    const sub = uint8.subarray(i, i + chunkSize);
    binary += String.fromCharCode(...sub);
  }
  return btoa(binary);
}

async function readReceiptAsBase64(file: File): Promise<string> {
  const mime = (file.type || "").toLowerCase();
  if (
    (mime === "image/jpeg" || mime === "image/webp") &&
    typeof createImageBitmap === "function"
  ) {
    try {
      const bitmap = await createImageBitmap(file);
      const canvas = document.createElement("canvas");
      canvas.width = bitmap.width;
      canvas.height = bitmap.height;
      const ctx = canvas.getContext("2d");
      if (ctx) {
        ctx.drawImage(bitmap, 0, 0);
        if (typeof bitmap.close === "function") bitmap.close();
        const dataUrl = canvas.toDataURL("image/png");
        if (dataUrl.startsWith("data:image/png;base64,")) {
          return dataUrl;
        }
      }
    } catch {
      // Fall back to raw file bytes
    }
  }
  const buffer = await file.arrayBuffer();
  return bytesToBase64(new Uint8Array(buffer));
}

async function extractReceiptFromFile(file: File) {
  isOcrExtracting.value = true;
  ocrWarning.value = "";
  ocrResult.value = null;
  receiptImageBase64.value = null;
  ocrToken.value = null;
  expenseForm.value.receipt_image_name = file.name || "";

  try {
    const imageBase64 = await readReceiptAsBase64(file);
    receiptImageBase64.value = imageBase64;
    const res = await fetch(
      "/api/v1/payments/participant/expenses/ocr-extract",
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          imageBase64,
          filename: file.name || null,
        }),
      }
    );
    await ensureOk(res);
    const data = await res.json();

    const ocrReadable = Boolean(
      data.ocrReadable ??
      data.ocr_readable ??
      Boolean(
        (data.merchantName || data.merchant_name) &&
        (data.grossAmount || data.gross_amount)
      )
    );
    const confidenceScore = ocrReadable
      ? Number(data.confidenceScore ?? data.confidence_score ?? 0)
      : 0;
    const merchantName = ocrReadable
      ? String(data.merchantName ?? data.merchant_name ?? "")
      : "";
    const expenseDate = String(data.expenseDate ?? data.expense_date ?? "");
    const grossAmount = ocrReadable
      ? Number(data.grossAmount ?? data.gross_amount ?? 0)
      : 0;
    const taxDeduction = ocrReadable
      ? Number(data.taxDeduction ?? data.tax_deduction ?? 0)
      : 0;
    const netAmount = ocrReadable
      ? Number(data.netAmount ?? data.net_amount ?? grossAmount)
      : 0;
    const category = String(data.category || "OTHER");
    const rawText = String(data.rawText ?? data.raw_text ?? "");
    const resolvedToken = data.ocrToken ?? data.ocr_token ?? null;
    const receiptSha256 = data.receiptSha256 ?? data.receipt_sha256 ?? null;
    ocrToken.value = typeof resolvedToken === "string" ? resolvedToken : null;

    ocrResult.value = {
      ocrReadable,
      ocr_readable: ocrReadable,
      merchant_name: merchantName,
      expense_date: expenseDate,
      gross_amount: grossAmount,
      tax_deduction: taxDeduction,
      net_amount: netAmount,
      category,
      confidence_score: confidenceScore,
      raw_text: rawText,
      ocrToken: ocrToken.value,
      ocr_token: ocrToken.value,
      receipt_sha256: receiptSha256,
    };

    ocrWarning.value =
      data.warning ||
      (!ocrReadable
        ? "Receipt text could not be read automatically. Verify the receipt image or enter claim fields manually below."
        : "");

    expenseForm.value.merchant_name = merchantName;
    expenseForm.value.gross_amount = grossAmount;
    expenseForm.value.expense_date = expenseDate;
    expenseForm.value.category = category;
    expenseForm.value.is_meal_allowance = category === "MEAL";
    expenseForm.value.ocr_confidence = confidenceScore;
  } catch (e) {
    useToastStore().apiError("Extracting receipt metadata failed", e);
  } finally {
    isOcrExtracting.value = false;
  }
}

async function handleReceiptFileUpload(event: Event) {
  const input = event.target as HTMLInputElement | null;
  const file = input?.files?.[0];
  if (!file) return;
  await extractReceiptFromFile(file);
}

async function loadAllData() {
  isLoading.value = true;
  try {
    const [wRes, rRes, cRes, tRes] = await Promise.all([
      fetch("/api/v1/payments/participant/wallets"),
      fetch("/api/v1/payments/participant/stipend-rules"),
      fetch("/api/v1/payments/participant/expenses"),
      fetch("/api/v1/payments/participant/transactions"),
    ]);

    // #4827: outside demo builds a failed request is an error, never an empty or sample wallet.
    if (!isDemoBuild()) {
      await Promise.all([wRes, rRes, cRes, tRes].map(ensureOk));
    }
    if (wRes.ok) {
      const rawWallets = await wRes.json();
      wallets.value = Array.isArray(rawWallets)
        ? rawWallets.map(normalizeWallet)
        : [];
    }
    if (rRes.ok) {
      const rawRules = await rRes.json();
      stipendRules.value = Array.isArray(rawRules)
        ? rawRules.map(normalizeStipendRule)
        : [];
    }
    if (cRes.ok) {
      const rawClaims = await cRes.json();
      expenseClaims.value = Array.isArray(rawClaims)
        ? rawClaims.map(normalizeExpenseClaim)
        : [];
    }
    if (tRes.ok) {
      const rawTxs = await tRes.json();
      ledgerTransactions.value = Array.isArray(rawTxs)
        ? rawTxs.map(normalizeTransaction)
        : [];
    }

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
          ach_routing_mask: "*****0021",
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
          ach_routing_mask: "*****0021",
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
          studyId: visitEventForm.value.study_id,
          siteId: visitEventForm.value.site_id,
          subjectId: visitEventForm.value.subject_id,
          visitCode: visitEventForm.value.visit_code,
          eventId: `EVT-${Date.now()}`,
          study_id: visitEventForm.value.study_id,
          site_id: visitEventForm.value.site_id,
          subject_id: visitEventForm.value.subject_id,
          visit_code: visitEventForm.value.visit_code,
          event_id: `EVT-${Date.now()}`,
        }),
      }
    );
    await ensureOk(res);
    const data = await res.json();
    const creditedAmount = data.amount ?? data.Amount;
    const updatedBal = data.current_balance ?? data.currentBalance;
    // Report only the amounts the service returned (#4827).
    visitTriggerSuccess.value =
      typeof creditedAmount === "number" && typeof updatedBal === "number"
        ? `Credited $${creditedAmount.toFixed(2)} stipend for visit ${visitEventForm.value.visit_code}. New balance: $${updatedBal.toFixed(2)}`
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
        subjectId: expenseForm.value.subject_id,
        studyId: expenseForm.value.study_id,
        siteId: expenseForm.value.site_id,
        category: expenseForm.value.category,
        merchantName: expenseForm.value.merchant_name,
        grossAmount: expenseForm.value.gross_amount,
        isMealAllowance: expenseForm.value.is_meal_allowance,
        expenseDate: expenseForm.value.expense_date,
        receiptImageName:
          expenseForm.value.receipt_image_name || "receipt_uploaded",
        receiptOcrData: ocrResult.value || {},
        ocrToken: ocrToken.value || undefined,
        receiptImageBase64: receiptImageBase64.value || undefined,
        subject_id: expenseForm.value.subject_id,
        study_id: expenseForm.value.study_id,
        site_id: expenseForm.value.site_id,
        merchant_name: expenseForm.value.merchant_name,
        gross_amount: expenseForm.value.gross_amount,
        is_meal_allowance: expenseForm.value.is_meal_allowance,
        expense_date: expenseForm.value.expense_date,
        receipt_image_name:
          expenseForm.value.receipt_image_name || "receipt_uploaded",
        receipt_ocr_data: ocrResult.value || {},
        ocr_token: ocrToken.value || undefined,
        receipt_image_base64: receiptImageBase64.value || undefined,
      }),
    });
    await ensureOk(res);
    ocrToken.value = null;
    receiptImageBase64.value = null;
    claimSubmitSuccess.value = `Expense claim of $${expenseForm.value.gross_amount.toFixed(2)} submitted.`;
    await loadAllData();
    activeTab.value = "approvals";
    void handoffFocus([
      "#wallet-approvals-heading",
      () =>
        document.querySelector<HTMLElement>(
          "#section-participant-wallet .pending-claim-approve-btn:not([disabled])"
        ),
    ]);
  } catch (e) {
    useToastStore().apiError("Submitting the expense claim failed", e);
  } finally {
    isSubmitting.value = false;
  }
}

async function reviewClaim(claimId: string, decision: string) {
  const currentIndex = pendingClaims.value.findIndex((c) => c.id === claimId);
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/payments/participant/expenses/${claimId}/review`,
      {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          status: decision,
          reviewNotes:
            decision === "APPROVED"
              ? "Verified valid clinical travel receipt."
              : "Non-compliant receipt documentation.",
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
  const remaining = pendingClaims.value;
  const nextClaim =
    remaining.length > 0
      ? remaining[Math.min(Math.max(currentIndex, 0), remaining.length - 1)]
      : null;
  void handoffFocus([
    nextClaim
      ? decision === "REJECTED"
        ? `#btn-reject-claim-${nextClaim.id}`
        : `#btn-approve-claim-${nextClaim.id}`
      : null,
    nextClaim ? `#btn-approve-claim-${nextClaim.id}` : null,
    "#wallet-approvals-heading",
  ]);
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
          disbursementType: disburseForm.value.type,
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
    void handoffFocus("#wallet-disbursements-heading");
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
  color: var(--semantic-color-text);
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: var(--semantic-spacing-xl, 24px);
  border-bottom: 1px solid var(--semantic-color-border);
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
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
  border: 1px solid var(--semantic-color-success-border);
}

.badge-regulatory {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-info-border);
}

.badge-warning {
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
  border: 1px solid var(--semantic-color-warning-border);
}

.badge-danger {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
  border: 1px solid var(--semantic-color-error-border);
}

.badge-standard {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-muted);
  border: 1px solid var(--semantic-color-border);
}

.view-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 6px 0;
}

.view-subtitle {
  font-size: 0.88rem;
  color: var(--semantic-color-text-muted);
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

.stat-label {
  font-size: 0.8rem;
  color: var(--semantic-color-text-muted);
  margin-top: 2px;
}

.tax-threshold-banner {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-left: 4px solid var(--semantic-color-primary);
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
  color: var(--semantic-color-text-muted);
}

.tax-badge-group {
  display: flex;
  gap: var(--semantic-spacing-xs, 8px);
}

.progress-bar-track {
  width: 100%;
  height: 10px;
  background-color: var(--semantic-color-surface-muted);
  border-radius: 6px;
  overflow: hidden;
  margin: var(--semantic-spacing-xs, 8px) 0;
}

.progress-bar-fill {
  height: 100%;
  transition: width 0.3s ease;
}

.fill-normal {
  background-color: var(--semantic-color-success);
}

.fill-approaching {
  background-color: var(--semantic-color-warning);
}

.fill-exceeded {
  background-color: var(--semantic-color-error);
}

.tax-legend {
  display: flex;
  justify-content: space-between;
  font-size: 0.78rem;
  color: var(--semantic-color-text-muted);
  margin-bottom: var(--semantic-spacing-xs, 8px);
}

.alert-tax-warning {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--semantic-color-error-bg);
  border: 1px solid var(--semantic-color-error);
  color: var(--semantic-color-error);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  margin-top: 8px;
}

.alert-tax-info {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--semantic-color-warning-bg);
  border: 1px solid var(--semantic-color-warning);
  color: var(--semantic-color-warning);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  margin-top: 8px;
}

.view-tabs-bar {
  display: flex;
  gap: var(--semantic-spacing-xs, 8px);
  border-bottom: 2px solid var(--semantic-color-border);
  margin-bottom: var(--semantic-spacing-lg, 20px);
}

.tab-btn {
  padding: 10px 16px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--semantic-color-text-muted);
  cursor: pointer;
  margin-bottom: -2px;
  transition: all 0.15s ease;
}

.tab-btn:hover {
  color: var(--semantic-color-text);
}

.tab-btn.active {
  color: var(--semantic-color-primary);
  border-bottom-color: var(--semantic-color-primary);
}

.panel-box {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  margin-bottom: var(--semantic-spacing-lg, 20px);
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 18px;
  border-bottom: 1px solid var(--semantic-color-border);
  background-color: var(--semantic-color-surface-muted);
  border-top-left-radius: 8px;
  border-top-right-radius: 8px;
}

.panel-title {
  font-size: 1rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  margin: 0;
}

.panel-tag {
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted);
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
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-muted);
  font-weight: 600;
  border-bottom: 1px solid var(--semantic-color-border);
}

.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
}

.data-table tr:hover {
  background-color: var(--semantic-color-surface-muted);
}

.row-highlight {
  background-color: var(--semantic-color-primary-light) !important;
}

.empty-cell {
  text-align: center;
  padding: 24px;
  color: var(--semantic-color-text-secondary);
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
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
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
  color: var(--semantic-color-text-muted);
}

.balance-inquiry-box {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
  gap: 10px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
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
  color: var(--semantic-color-text-muted);
  text-transform: uppercase;
}

.inquiry-val {
  font-size: 1rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.credentials-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 16px;
}

.virtual-card-preview {
  background: linear-gradient(
    135deg,
    var(--semantic-color-surface-elevated) 0%,
    var(--semantic-color-surface-inverse) 100%
  );
  color: var(--semantic-color-text-inverse);
  padding: 16px;
  border-radius: 8px;
  box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
}

.ach-preview {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  padding: 16px;
  border-radius: 8px;
}

.card-chip {
  font-size: 0.8rem;
  font-weight: 600;
  letter-spacing: 0.05em;
  margin-bottom: 12px;
  color: var(--semantic-color-divider-accent);
}

.card-number {
  font-family: monospace;
  font-size: 1.1rem;
  letter-spacing: 0.1em;
  margin-bottom: 8px;
}

.card-exp {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
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
  color: var(--semantic-color-text);
}

.disburse-action-bar {
  display: flex;
  gap: 12px;
  align-items: flex-end;
  background-color: var(--semantic-color-surface);
  padding: 14px;
  border: 1px solid var(--semantic-color-border);
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
  color: var(--semantic-color-text-muted);
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
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover {
  background-color: var(--semantic-color-primary-dark);
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text-muted);
  border-color: var(--semantic-color-border);
}

.btn-secondary:hover {
  background-color: var(--semantic-color-surface-muted);
}

.btn-success {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.btn-success:hover {
  background-color: var(--semantic-color-success);
}

.btn-danger {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.btn-danger:hover {
  background-color: var(--semantic-color-error);
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
  color: var(--semantic-color-text-muted);
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
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  font-size: 0.88rem;
  color: var(--semantic-color-text);
  background-color: var(--semantic-color-surface);
  box-sizing: border-box;
}

.form-row-2 {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 10px;
}

.ocr-dropzone {
  display: flex;
  flex-direction: column;
  align-items: center;
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 24px;
  text-align: center;
  cursor: pointer;
  background-color: var(--semantic-color-surface-muted);
  transition: all 0.15s ease;
  margin-bottom: 12px;
}

.sr-only-file-input {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.dropzone-subtext {
  display: block;
  margin-top: 4px;
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted);
}

.ocr-warning-box {
  background-color: var(--semantic-color-warning-bg);
  border: 1px solid var(--semantic-color-warning);
  color: var(--semantic-color-warning);
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  margin-bottom: 12px;
}

.ocr-dropzone:hover {
  border-color: var(--semantic-color-primary);
  background-color: var(--semantic-color-primary-light);
}

.dropzone-icon {
  font-size: 2rem;
  margin-bottom: 6px;
}

.dropzone-text strong {
  font-size: 0.95rem;
  color: var(--semantic-color-text);
}

.dropzone-text p {
  margin: 4px 0 0 0;
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted);
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
  color: var(--semantic-color-text-muted);
}

.ocr-result-card {
  padding: 14px;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
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
  background-color: var(--semantic-color-warning-bg);
  border: 1px solid var(--semantic-color-warning);
  color: var(--semantic-color-warning);
  padding: 6px 10px;
  border-radius: 4px;
  font-size: 0.78rem;
  margin-bottom: 8px;
}

.ocr-raw {
  background-color: var(--semantic-color-surface);
  padding: 8px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 4px;
}

.ocr-raw pre {
  margin: 4px 0 0 0;
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted);
  white-space: pre-wrap;
}

.alert-success-box {
  margin-top: 12px;
  padding: 10px 14px;
  background-color: var(--semantic-color-success-bg);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success);
  border-radius: 6px;
  font-size: 0.85rem;
}

.action-btn-group {
  display: flex;
  gap: 6px;
}

.text-success {
  color: var(--semantic-color-success);
}
.text-warning {
  color: var(--semantic-color-warning);
}
.text-danger {
  color: var(--semantic-color-error);
}
.text-muted {
  color: var(--semantic-color-text-muted);
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
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
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
  border-bottom: 1px solid var(--semantic-color-border);
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
  color: var(--semantic-color-text-muted);
  cursor: pointer;
}

.modal-body {
  padding: 20px;
}

.modal-desc {
  font-size: 0.85rem;
  color: var(--semantic-color-text-muted);
  margin-bottom: 16px;
  line-height: 1.4;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 20px;
  background-color: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
}
</style>
