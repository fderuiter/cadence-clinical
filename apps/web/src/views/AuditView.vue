<template>
  <div id="section-audit" class="dashboard-section active">
    <div class="section-header">
      <h2>Cryptographic Audit Log Inspector</h2>
      <p>
        Visual blockchain-style ledger feed showing live SHA-256 signature
        stamping as forms are loaded, fields are modified, or queries are
        transitioned.
      </p>
    </div>

    <div class="card">
      <div class="card-title">
        <span>21 CFR Part 11 Cryptographic Audit Ledger</span>
        <button
          id="btn-clear-ledger"
          class="badge"
          style="
            cursor: pointer;
            background-color: var(--error);
            color: white;
            border: none;
          "
          @click="purgeLedger"
        >
          Purge Audit Trail
        </button>
      </div>
      <div id="ledger-timeline-container" class="ledger-container">
        <div v-if="store.ledgerBlocks.length === 0" class="empty-ledger">
          No ledger entries recorded yet. Make some changes on the tabs above!
        </div>
        <template v-else>
          <div
            v-for="block in reversedLedger"
            :key="block.index"
            class="ledger-block signed"
          >
            <span class="verified-stamp">Verified Ledger Block</span>
            <div class="ledger-block-header">
              <span class="ledger-block-index">BLOCK #{{ block.index }}</span>
              <span class="ledger-block-timestamp">{{ block.timestamp }}</span>
            </div>
            <div class="ledger-block-title">
              {{ block.action }}
            </div>
            <div class="ledger-block-details">
              <template v-for="(val, key) in block.details" :key="key">
                <span class="ledger-lbl">{{ key }}:</span>
                <span class="ledger-val">{{
                  typeof val === "object" ? JSON.stringify(val) : val
                }}</span>
              </template>
              <span class="ledger-lbl">reason:</span>
              <span
                class="ledger-val"
                style="color: var(--accent); font-weight: 600"
                >{{ block.reason }}</span
              >
            </div>
            <div class="ledger-block-crypto">
              <div class="crypto-row">
                <span class="crypto-lbl">prevHash:</span>
                <span class="crypto-hash prev">{{ block.prevHash }}</span>
              </div>
              <div class="crypto-row">
                <span class="crypto-lbl">blockHash:</span>
                <span class="crypto-hash">{{ block.hash }}</span>
              </div>
            </div>
          </div>
        </template>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { useClinicalStore } from "../stores/clinical";

const store = useClinicalStore();

const reversedLedger = computed(() => {
  return [...store.ledgerBlocks].reverse();
});

function purgeLedger() {
  if (
    confirm(
      "Are you sure you want to purge the local cryptographic audit trail? This is a non-GxP compliance violation!"
    )
  ) {
    store.clearLedger();
    alert("Audit trail purged!");
  }
}
</script>
