<template>
  <div
    v-if="isW9HoldActive"
    id="wallet-w9-hold-banner"
    class="wallet-w9-hold"
    role="alert"
    aria-live="assertive"
  >
    <strong class="wallet-w9-hold-heading">
      <ClinicalIcon name="alert-circle" :size="16" />
      <span>IRS $600.00 Form W-9 Verification Hold Active</span>
    </strong>
    <span>
      Your year-to-date taxable study compensation ({{ taxableYtdFormatted }})
      has reached the $600.00 IRS Form 1099 reporting threshold while your Form
      W-9 status is <strong>{{ w9StatusLabel }}</strong>. On-demand payouts and
      automated expense disbursements are paused until site staff verify your
      Form W-9.
    </span>
    <div class="w9-resolution-links">
      <a
        id="link-w9-coordinator-support"
        href="#wallet-w9-support"
        class="w9-banner-link"
        @click.prevent="$emit('jump-support')"
      >
        Contact Study Coordinator to Verify Form W-9
      </a>
      <a
        id="link-w9-email-coordinator"
        :href="siteContact.emailHref"
        class="w9-banner-link"
      >
        Email Coordinator ({{ siteContact.emailDisplay }})
      </a>
      <a
        id="link-w9-call-coordinator"
        :href="siteContact.phoneHref"
        class="w9-banner-link"
      >
        Call Coordinator ({{ siteContact.phoneDisplay }})
      </a>
    </div>
  </div>

  <div
    v-else-if="isApproachingW9Threshold"
    id="wallet-w9-warning-banner"
    class="wallet-w9-warning"
    role="status"
    aria-live="polite"
  >
    <div class="wallet-w9-warning-row">
      <ClinicalIcon name="info" :size="16" />
      <span>
        Year-to-date taxable compensation ({{ taxableYtdFormatted }}) is
        approaching the $600.00 IRS Form W-9 threshold (current status:
        {{ w9StatusLabel }}). Submit Form W-9 with your study coordinator to
        avoid payout holds.
      </span>
    </div>
    <div class="w9-resolution-links">
      <a
        id="link-w9-warning-support"
        href="#wallet-w9-support"
        class="w9-banner-link"
        @click.prevent="$emit('jump-support')"
      >
        Contact Study Coordinator for Form W-9
      </a>
    </div>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";

defineProps({
  isW9HoldActive: { type: Boolean, required: true },
  isApproachingW9Threshold: { type: Boolean, required: true },
  taxableYtdFormatted: { type: String, required: true },
  w9StatusLabel: { type: String, required: true },
  siteContact: { type: Object, required: true },
});

defineEmits(["jump-support"]);
</script>

<style scoped src="./walletView.css"></style>
<style scoped>
.wallet-w9-hold-heading {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
}

.wallet-w9-warning-row {
  display: flex;
  align-items: flex-start;
  gap: 8px;
}

#wallet-w9-hold-banner,
#wallet-w9-warning-banner {
  font-variant-numeric: tabular-nums;
}
</style>
