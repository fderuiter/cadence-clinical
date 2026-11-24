<template>
  <ClinicalDrawer
    id="portal-peek-drawer"
    overlay-id="portal-peek-overlay"
    :model-value="isPeekDrawerOpen"
    position="bottom"
    mobile-bottom-sheet
    :restore-focus="false"
    :title="ui.peekRecord?.title || 'Item Details'"
    :subtitle="ui.peekRecord?.subtitle || ''"
    close-label="Close item details"
    @close="handleClose"
  >
    <div v-if="ui.peekRecord" class="peek-sheet-content">
      <div class="peek-sheet-status-row">
        <span
          class="status-pill"
          :class="resolveStatusClass(ui.peekRecord.statusTone)"
        >
          {{ ui.peekRecord.statusLabel || "Recorded" }}
        </span>
      </div>

      <p id="portal-peek-explanation" class="peek-sheet-explanation">
        {{ ui.peekRecord.explanation }}
      </p>

      <dl
        v-if="
          Array.isArray(ui.peekRecord.details) &&
          ui.peekRecord.details.length > 0
        "
        class="peek-sheet-details"
      >
        <div
          v-for="detail in ui.peekRecord.details"
          :key="detail.label"
          class="peek-sheet-detail-row"
        >
          <dt class="peek-sheet-detail-label">{{ detail.label }}</dt>
          <dd
            class="peek-sheet-detail-value"
            :class="{ 'tabular-nums': detail.tabular }"
          >
            {{ detail.value }}
          </dd>
        </div>
      </dl>

      <SiteContactLinks
        v-if="ui.peekRecord.showCoordinatorHelp"
        id="portal-peek-coordinator-links"
        compact
        heading="Need Coordinator Help?"
        region-label="Coordinator contact options for this item"
      />
    </div>

    <template #footer>
      <button
        id="btn-peek-close"
        type="button"
        class="btn btn-secondary"
        @click="handleClose"
      >
        Close
      </button>
      <button
        id="btn-peek-open-full-view"
        type="button"
        class="btn btn-primary peek-open-full-btn"
        @click="handleOpenFullView"
      >
        <ClinicalIcon name="external-link" :size="16" />
        <span>{{ ui.peekRecord?.fullViewLabel || "Open full view" }}</span>
      </button>
    </template>
  </ClinicalDrawer>
</template>

<script setup>
import { computed, inject, watch } from "vue";
import { routeLocationKey } from "vue-router";
import { ClinicalDrawer, ClinicalIcon } from "ui";

import {
  PEEK_QUERY_KEYS,
  closePeekRecord,
  openPeekFullView,
  syncPeekFromQuery,
} from "../features/peek.js";
import { useAuditStore } from "../stores/audit.js";
import { useComplianceStore } from "../stores/compliance.js";
import { useConsentStore } from "../stores/consent.js";
import { useInboxStore } from "../stores/inbox.js";
import { useSessionStore } from "../stores/session.js";
import { useSyncStore } from "../stores/sync.js";
import { useUiStore } from "../stores/ui.js";
import { useWalletStore } from "../stores/wallet.js";
import SiteContactLinks from "./SiteContactLinks.vue";

const route = inject(routeLocationKey, null);
const ui = useUiStore();
const session = useSessionStore();
const consent = useConsentStore();
const wallet = useWalletStore();
const inbox = useInboxStore();
const compliance = useComplianceStore();
const audit = useAuditStore();
const sync = useSyncStore();

const isPeekDrawerOpen = computed(
  () =>
    Boolean(ui.peekRecord) &&
    !session.pinSetup?.isOpen &&
    !session.pinUnlock?.isOpen &&
    !session.sessionWarning?.isOpen &&
    !consent.reconsentModalOpen &&
    !ui.signatureModalOpen
);

watch(
  () => [
    route?.query,
    wallet.expenseClaims?.length || 0,
    wallet.recentTransactions?.length || 0,
    inbox.notifications?.length || 0,
    compliance.compliance,
    audit.ledgerBlocks?.length || 0,
    sync.submissions?.length || 0,
  ],
  () => {
    if (ui.peekRecord) return;
    const query = route?.query;
    if (query && PEEK_QUERY_KEYS.some((key) => Boolean(query[key]))) {
      syncPeekFromQuery(query);
    }
  },
  { immediate: true }
);

function resolveStatusClass(tone) {
  if (tone === "success") return "completed";
  if (tone === "warning") return "overdue";
  return "pending";
}

function handleClose() {
  if (ui.peekRecord) {
    closePeekRecord({ restoreFocus: true, syncUrl: true });
  }
}

function handleOpenFullView() {
  void openPeekFullView();
}
</script>
