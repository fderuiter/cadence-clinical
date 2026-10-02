<template>
  <div class="ctms-subview ctms-financials-view">
    <div class="card" style="margin-bottom: 24px">
      <div class="card-title">Site Budget & Financial Grants Console</div>
      <p style="color: #64748b; font-size: 14px; margin-bottom: 16px">
        Manage investigator grant agreements, milestone payment schedules, overhead withholding, and financial invoicing.
      </p>
      <div id="ctms-financials-container">
        <div class="stats-grid" style="display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 16px; margin-bottom: 20px">
          <div class="card" style="padding: 16px">
            <div style="font-size: 0.8rem; font-weight: 700; color: #64748b">TOTAL BUDGET</div>
            <div style="font-size: 1.5rem; font-weight: 800; color: #0f172a; margin-top: 4px">$125,000.00</div>
          </div>
          <div class="card" style="padding: 16px">
            <div style="font-size: 0.8rem; font-weight: 700; color: #64748b">DISBURSED TO DATE</div>
            <div style="font-size: 1.5rem; font-weight: 800; color: #166534; margin-top: 4px">$42,500.00</div>
          </div>
          <div class="card" style="padding: 16px">
            <div style="font-size: 0.8rem; font-weight: 700; color: #64748b">PENDING INVOICES</div>
            <div style="font-size: 1.5rem; font-weight: 800; color: #b45309; margin-top: 4px">$18,200.00</div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
</script>
