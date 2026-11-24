<template>
  <footer class="portal-footer">
    <div class="footer-title">My Recent Activity</div>
    <div
      id="portal-ledger-timeline"
      ref="timelineRef"
      class="ledger-timeline"
      data-vue-ledger="true"
      role="region"
      aria-label="Recent activity audit ledger"
      tabindex="0"
    >
      <div
        v-if="ledgerItems.length === 0"
        class="loading-state"
        data-ledger-empty="true"
      >
        No recent activity yet.
      </div>
      <div
        v-for="item in ledgerItems"
        :id="`ledger-row-${item.blockIndex}`"
        :key="item.key"
        class="ledger-item"
        :data-ledger-key="item.key"
        :data-ledger-index="item.blockIndex"
      >
        <div class="ledger-item-header">
          <span>
            <component
              :is="linkComponent"
              v-if="item.route"
              v-bind="linkProps(item.route)"
              class="ledger-activity-link"
              :data-ledger-route="item.route"
            >
              <strong data-ledger-title>{{ item.title }}</strong>
            </component>
            <strong v-else data-ledger-title>{{ item.title }}</strong>
          </span>
          <span class="ledger-header-meta">
            <span class="tabular-nums" data-ledger-time>{{
              item.formattedTime
            }}</span>
            <button
              :id="`btn-peek-ledger-${item.blockIndex}`"
              type="button"
              class="btn btn-secondary btn-peek-ledger"
              :data-id="item.blockIndex"
              @click.stop="handlePeekClick(item, $event)"
            >
              View Details<span class="sr-only">: {{ item.title }}</span>
            </button>
          </span>
        </div>
        <div class="ledger-item-details">
          <span data-ledger-summary>{{ item.summary }}</span>
          <component
            :is="linkComponent"
            v-if="item.route && item.linkLabel"
            v-bind="linkProps(item.route)"
            class="ledger-activity-cta"
            :data-ledger-cta="item.route"
          >
            {{ item.linkLabel }}
          </component>
        </div>
        <details class="ledger-verification-details">
          <summary>Verification code</summary>
          <div class="ledger-hash">
            <code data-ledger-hash>{{ item.hashText }}</code>
          </div>
        </details>
      </div>
    </div>
  </footer>
</template>

<script setup>
import {
  computed,
  getCurrentInstance,
  onBeforeUnmount,
  onMounted,
  ref,
} from "vue";
import { buildReactiveLedgerItems } from "../features/audit.js";
import { openLedgerPeek } from "../features/peek.js";
import { state } from "../stores/state.js";

const timelineRef = ref(null);
const instance = getCurrentInstance();
const routerLink = computed(
  () => instance?.appContext?.components?.RouterLink ?? null
);
const currentRoute = computed(
  () => instance?.appContext?.config?.globalProperties?.$route ?? null
);
const hasRouter = computed(() =>
  Boolean(
    instance?.appContext?.config?.globalProperties?.$router && routerLink.value
  )
);
const linkComponent = computed(() =>
  hasRouter.value ? routerLink.value : "a"
);

function buildScopedTarget(routePath) {
  const query = {};
  const studyId = currentRoute.value?.query?.studyId || state.session?.studyId;
  const siteId = currentRoute.value?.query?.siteId || state.session?.siteId;
  if (typeof studyId === "string" && studyId.trim()) {
    query.studyId = studyId.trim();
  }
  if (typeof siteId === "string" && siteId.trim()) {
    query.siteId = siteId.trim();
  }
  return Object.keys(query).length > 0 ? { path: routePath, query } : routePath;
}

function linkProps(route) {
  return hasRouter.value ? { to: buildScopedTarget(route) } : { href: route };
}

function handlePeekClick(item, event) {
  const idx = Number(item.blockIndex);
  const targetBlock =
    item.block || (Number.isFinite(idx) ? state.ledgerBlocks[idx] : null);
  if (targetBlock) {
    openLedgerPeek(targetBlock, idx, event?.currentTarget || null);
  }
}

function handleTimelineClick(event) {
  const targetEl = event?.target;
  if (!targetEl?.closest || targetEl.closest("button, a, details, summary")) {
    return;
  }
  const rowEl = targetEl.closest(".ledger-item");
  if (!rowEl) return;
  const idx = Number(rowEl.getAttribute("data-ledger-index"));
  const matchedItem = ledgerItems.value.find(
    (entry) => Number(entry.blockIndex) === idx
  );
  const targetBlock =
    matchedItem?.block ||
    (Number.isFinite(idx) ? state.ledgerBlocks[idx] : null);
  if (targetBlock) {
    const triggerEl = rowEl.querySelector(".btn-peek-ledger") || rowEl;
    openLedgerPeek(targetBlock, idx, triggerEl);
  }
}

onMounted(() => {
  timelineRef.value?.addEventListener("click", handleTimelineClick);
});

onBeforeUnmount(() => {
  timelineRef.value?.removeEventListener("click", handleTimelineClick);
});

const ledgerItems = computed(() =>
  buildReactiveLedgerItems(state.ledgerBlocks)
);
</script>

<style scoped>
.ledger-activity-link {
  color: var(--semantic-color-primary, var(--primary));
  text-decoration: underline;
  text-underline-offset: 2px;
}

.ledger-activity-cta {
  margin-left: 8px;
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--semantic-color-primary, var(--primary));
  text-decoration: underline;
  text-underline-offset: 2px;
}
</style>
