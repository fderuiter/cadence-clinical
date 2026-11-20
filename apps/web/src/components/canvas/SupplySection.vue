<template>
  <section
    id="supply-section"
    class="supply-section"
    aria-labelledby="supply-section-title"
    :aria-busy="loading"
  >
    <header class="supply-header">
      <div>
        <h2 id="supply-section-title">Investigational supply</h2>
        <p class="section-subtitle">
          Site kit inventory, blinded dispensing stock, and returned kit
          accountability for {{ studyCode }}.
        </p>
      </div>
      <div class="supply-actions">
        <button
          v-if="canSeedInventory && !seeding"
          id="btn-open-seed-kits"
          type="button"
          class="btn btn-secondary"
          @click="openSeedForm"
        >
          Seed kit inventory
        </button>
        <button
          v-if="canReconcileInventory"
          id="btn-open-supply-reconciliation"
          type="button"
          class="btn btn-primary"
          @click="reconcileOpen = true"
        >
          Reconcile returned kits
        </button>
      </div>
    </header>

    <form
      id="supply-scope-form"
      class="scope-bar"
      aria-label="Site inventory scope"
      @submit.prevent="loadInventory"
    >
      <label for="supply-site-id">Site</label>
      <input
        id="supply-site-id"
        v-model="siteId"
        type="text"
        autocomplete="off"
        placeholder="e.g. SITE-101"
      />
      <button
        id="btn-load-supply-inventory"
        type="submit"
        class="btn btn-secondary"
        :disabled="loading || !siteId.trim()"
      >
        {{ loading ? "Loading..." : "Load site inventory" }}
      </button>
    </form>

    <form
      v-if="canSeedInventory && seeding"
      id="seed-kits-form"
      class="card seed-form"
      aria-labelledby="seed-kits-heading"
      @submit.prevent="submitSeed"
    >
      <h3 id="seed-kits-heading">Seed site kit inventory</h3>
      <p class="seed-note">
        Treatment arm assignments are transmitted only to Execution to populate
        blinded allocation stock and are cleared from this form on submission.
      </p>

      <div class="field field-site">
        <label for="seed-kits-site">Destination site</label>
        <input
          id="seed-kits-site"
          v-model="seedSiteId"
          type="text"
          autocomplete="off"
          placeholder="e.g. SITE-101"
        />
      </div>

      <fieldset class="fieldset">
        <legend>Kits to seed</legend>
        <div
          v-for="(row, index) in kitRows"
          :key="`kit-${index}`"
          class="kit-row"
        >
          <div class="field">
            <label :for="`seed-kit-number-${index}`">Kit number</label>
            <input
              :id="`seed-kit-number-${index}`"
              v-model="row.kitNumber"
              type="text"
              autocomplete="off"
              placeholder="e.g. KIT-0101"
            />
          </div>
          <div class="field">
            <label :for="`seed-kit-arm-${index}`">Treatment arm</label>
            <input
              :id="`seed-kit-arm-${index}`"
              v-model="row.treatmentArm"
              type="text"
              autocomplete="off"
              placeholder="e.g. ACTIVE"
            />
          </div>
          <div class="field">
            <label :for="`seed-kit-lot-${index}`">Lot number</label>
            <input
              :id="`seed-kit-lot-${index}`"
              v-model="row.lotNumber"
              type="text"
              autocomplete="off"
              placeholder="e.g. LOT-A1"
            />
          </div>
          <div class="field">
            <label :for="`seed-kit-expiry-${index}`">Expiration date</label>
            <input
              :id="`seed-kit-expiry-${index}`"
              v-model="row.expirationDate"
              type="date"
            />
          </div>
          <button
            v-if="kitRows.length > 1"
            :id="`btn-remove-seed-kit-${index}`"
            type="button"
            class="btn btn-secondary"
            :aria-label="`Remove kit row ${index + 1}`"
            @click="kitRows.splice(index, 1)"
          >
            Remove
          </button>
        </div>
        <button
          id="btn-add-seed-kit-row"
          type="button"
          class="btn btn-secondary btn-self-start"
          @click="kitRows.push(emptyKitRow())"
        >
          Add another kit
        </button>
      </fieldset>

      <div class="field">
        <label for="seed-kits-reason">Reason for seeding</label>
        <input
          id="seed-kits-reason"
          v-model="seedReason"
          type="text"
          autocomplete="off"
          placeholder="Initial depot release to clinical site"
        />
      </div>

      <p v-if="seedError" id="seed-kits-error" class="error-note" role="alert">
        {{ seedError }}
      </p>

      <div class="form-actions">
        <button
          id="btn-submit-seed-kits"
          type="submit"
          class="btn btn-primary"
          :disabled="seedingBusy || !canSubmitSeed"
        >
          {{ seedingBusy ? "Seeding..." : "Seed kits" }}
        </button>
        <button
          id="btn-cancel-seed-kits"
          type="button"
          class="btn btn-secondary"
          :disabled="seedingBusy"
          @click="closeSeedForm"
        >
          Cancel
        </button>
      </div>
    </form>

    <p id="supply-status-line" class="status-line" role="status">
      {{ statusLine }}
    </p>

    <table
      v-if="inventory.length"
      id="supply-inventory-table"
      class="supply-table"
    >
      <thead>
        <tr>
          <th scope="col">Kit number</th>
          <th scope="col">Lot number</th>
          <th scope="col">Expiration</th>
          <th scope="col">Status</th>
          <th scope="col" class="numeric">Residual volume</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="item in inventory" :key="item.kit_number">
          <th scope="row">{{ item.kit_number }}</th>
          <td>{{ item.lot_number }}</td>
          <td>{{ formatDate(item.expiration_date) }}</td>
          <td>{{ item.status }}</td>
          <td class="numeric">
            {{ item.residual_volume_ml }} mL ({{
              item.residual_volume_percentage
            }}%)
          </td>
        </tr>
      </tbody>
    </table>

    <BulkIpKitReconciliationDrawer
      v-if="canReconcileInventory"
      :is-open="reconcileOpen"
      :study-id="studyCode"
      :site-id="siteId.trim() || defaultSiteId || ''"
      :pharmacist-name="pharmacistName || ''"
      @close="reconcileOpen = false"
      @reconciled="onReconciled"
    />
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import { executionApi } from "../../api/executionApi";
import type { SiteInventoryItem } from "../../api/types";
import { useToastStore } from "../../stores/toasts.js";
import BulkIpKitReconciliationDrawer from "../clinical/BulkIpKitReconciliationDrawer.vue";

interface SeedKitRow {
  kitNumber: string;
  treatmentArm: string;
  lotNumber: string;
  expirationDate: string;
}

const props = defineProps<{
  studyCode: string;
  defaultSiteId?: string;
  canSeedInventory?: boolean;
  canReconcileInventory?: boolean;
  pharmacistName?: string;
}>();

const toasts = useToastStore();

const siteId = ref(props.defaultSiteId || "");
const inventory = ref<SiteInventoryItem[]>([]);
const checkedSite = ref("");
const loading = ref(false);
const loadError = ref(false);

const seeding = ref(false);
const seedingBusy = ref(false);
const seedSiteId = ref(props.defaultSiteId || "");
const seedReason = ref("");
const seedError = ref("");
const kitRows = ref<SeedKitRow[]>([emptyKitRow()]);

const reconcileOpen = ref(false);

function emptyKitRow(): SeedKitRow {
  return {
    kitNumber: "",
    treatmentArm: "",
    lotNumber: "",
    expirationDate: "",
  };
}

const statusLine = computed(() => {
  if (!checkedSite.value) {
    return "Enter a site code to inspect blind-safe investigational supply.";
  }
  if (loading.value) {
    return `Loading kit inventory for site ${checkedSite.value}...`;
  }
  if (loadError.value) {
    return `Inventory for site ${checkedSite.value} couldn't be loaded.`;
  }
  if (!inventory.value.length) {
    return `No kits recorded at site ${checkedSite.value} for ${props.studyCode}.`;
  }
  return `${inventory.value.length} kit(s) recorded at site ${checkedSite.value} (treatment arms masked).`;
});

const validKitRows = computed(() =>
  kitRows.value.filter(
    (r) =>
      r.kitNumber.trim() &&
      r.treatmentArm.trim() &&
      r.lotNumber.trim() &&
      r.expirationDate.trim()
  )
);

const canSubmitSeed = computed(
  () =>
    Boolean(seedSiteId.value.trim()) &&
    validKitRows.value.length > 0 &&
    Boolean(seedReason.value.trim())
);

async function loadInventory() {
  const targetSite = siteId.value.trim();
  if (
    !props.studyCode ||
    !targetSite ||
    typeof executionApi.getSiteIpKitInventory !== "function"
  ) {
    return;
  }
  checkedSite.value = targetSite;
  loading.value = true;
  loadError.value = false;
  try {
    inventory.value = await executionApi.getSiteIpKitInventory(
      props.studyCode,
      targetSite
    );
  } catch (err) {
    loadError.value = true;
    toasts.apiError(`Couldn't load supply for site ${targetSite}`, err);
  } finally {
    loading.value = false;
  }
}

watch(
  () => [props.studyCode, props.defaultSiteId],
  ([, nextDefaultSite]) => {
    if (nextDefaultSite && !siteId.value) {
      siteId.value = nextDefaultSite;
    }
    if (siteId.value.trim()) {
      loadInventory();
    } else {
      inventory.value = [];
      checkedSite.value = "";
    }
  },
  { immediate: true }
);

function openSeedForm() {
  seedSiteId.value = siteId.value.trim() || props.defaultSiteId || "";
  seedError.value = "";
  kitRows.value = [emptyKitRow()];
  seeding.value = true;
}

function closeSeedForm() {
  kitRows.value = [emptyKitRow()];
  seedReason.value = "";
  seedError.value = "";
  seeding.value = false;
}

function formatDate(raw: string | undefined): string {
  if (!raw) return "—";
  const idx = raw.indexOf("T");
  return idx > 0 ? raw.slice(0, idx) : raw;
}

async function submitSeed() {
  if (!canSubmitSeed.value) return;
  const targetSite = seedSiteId.value.trim();
  const reason = seedReason.value.trim();
  const kits = validKitRows.value.map((row) => ({
    kit_number: row.kitNumber.trim(),
    treatment_arm: row.treatmentArm.trim(),
    lot_number: row.lotNumber.trim(),
    expiration_date: row.expirationDate.includes("T")
      ? row.expirationDate
      : `${row.expirationDate}T00:00:00Z`,
  }));

  seedingBusy.value = true;
  seedError.value = "";
  try {
    await executionApi.seedIpKitInventory(
      {
        study_id: props.studyCode,
        site_id: targetSite,
        kits,
      },
      reason
    );
    // Wipe treatment arm values from client memory immediately after seeding (PX-20).
    kitRows.value = [emptyKitRow()];
    seedReason.value = "";
    seeding.value = false;
    siteId.value = targetSite;
    toasts.push({
      tone: "success",
      title: `Seeded ${kits.length} kit(s) at site ${targetSite}`,
      message: "Site inventory updated.",
    });
    await loadInventory();
  } catch (err) {
    seedError.value =
      err instanceof Error ? err.message : "Kit inventory could not be seeded.";
    toasts.apiError(`Kits were not seeded at site ${targetSite}`, err);
  } finally {
    seedingBusy.value = false;
  }
}

async function onReconciled() {
  if (siteId.value.trim()) {
    await loadInventory();
  }
}
</script>

<style scoped>
.supply-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.supply-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
}

.supply-header h2 {
  margin: 0;
  font-size: 1.15rem;
}

.section-subtitle,
.status-line,
.seed-note {
  margin: 4px 0 0;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.supply-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.scope-bar {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.scope-bar label {
  font-weight: 600;
}

.scope-bar input {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg-card);
  color: var(--text-main);
}

.seed-form {
  display: flex;
  flex-direction: column;
  gap: 14px;
  padding: 16px;
}

.seed-form h3 {
  margin: 0;
  font-size: 1rem;
}

.field {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-site {
  max-width: 240px;
}

.field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.field input {
  padding: 6px 8px;
  border: 1px solid var(--border);
  border-radius: 4px;
  background: var(--bg-card);
  color: var(--text-main);
}

.fieldset {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.fieldset legend {
  padding: 0 6px;
  font-size: 0.85rem;
  font-weight: 600;
}

.kit-row {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 10px;
}

.btn-self-start {
  align-self: flex-start;
}

.form-actions {
  display: flex;
  gap: 8px;
}

.supply-table {
  width: 100%;
  border-collapse: collapse;
}

.supply-table th,
.supply-table td {
  padding: 10px 8px;
  border-bottom: 1px solid var(--border);
  text-align: left;
  font-size: 0.9rem;
}

.numeric {
  font-variant-numeric: tabular-nums;
}

.error-note {
  margin: 0;
  color: var(--danger);
  font-size: 0.85rem;
}
</style>
