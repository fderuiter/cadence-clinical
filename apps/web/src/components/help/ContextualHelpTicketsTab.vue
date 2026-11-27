<template>
  <div
    id="panel-contextual-tickets"
    role="tabpanel"
    aria-labelledby="tab-contextual-tickets"
    class="drawer-tabpanel tickets-panel"
  >
    <div class="tickets-toolbar">
      <div class="tickets-scope-summary">
        <span class="tickets-scope-label">Scope:</span>
        <span class="tickets-scope-value">{{ activeScopeSummary }}</span>
      </div>
      <button
        id="btn-refresh-drawer-tickets"
        type="button"
        class="btn btn-secondary btn-compact"
        :disabled="knowledgeStore.isTicketsLoading"
        @click="handleRefreshDrawerTickets"
      >
        <RefreshCw
          :size="16"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
        <span>Refresh</span>
      </button>
    </div>

    <div
      v-if="knowledgeStore.selectedTicket"
      id="drawer-ticket-detail-card"
      class="ticket-detail-card"
    >
      <div class="ticket-detail-nav">
        <button
          id="btn-back-to-ticket-list"
          type="button"
          class="btn-back"
          @click="handleBackToTicketList"
        >
          <ArrowLeft
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Back to Scoped Tickets</span>
        </button>
        <span
          v-if="knowledgeStore.isTicketDetailLoading"
          class="detail-sync-indicator"
        >
          Refreshing...
        </span>
      </div>

      <div
        v-if="knowledgeStore.ticketDetailError"
        id="drawer-ticket-detail-error"
        class="help-error-alert"
        role="alert"
      >
        <div class="error-alert-header">
          <AlertTriangle
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <h4 class="error-alert-title">Unable to Refresh Ticket Details</h4>
        </div>
        <p class="error-alert-message">
          {{ knowledgeStore.ticketDetailError }}
        </p>
      </div>

      <div class="ticket-detail-header">
        <span class="ticket-ref-code">
          {{
            knowledgeStore.selectedTicket.reference ||
            knowledgeStore.selectedTicket.id
          }}
        </span>
        <span class="badge badge-status">
          {{ knowledgeStore.selectedTicket.status || "OPEN" }}
        </span>
        <span
          v-if="knowledgeStore.selectedTicket.priority"
          class="badge badge-priority"
        >
          {{ knowledgeStore.selectedTicket.priority }}
        </span>
        <span
          v-if="knowledgeStore.selectedTicket.gxp_severity"
          class="badge badge-severity"
        >
          GxP: {{ knowledgeStore.selectedTicket.gxp_severity }}
        </span>
      </div>

      <h4 class="ticket-detail-title">
        {{ knowledgeStore.selectedTicket.title }}
      </h4>

      <p
        v-if="knowledgeStore.selectedTicket.description"
        class="ticket-detail-description"
      >
        {{ knowledgeStore.selectedTicket.description }}
      </p>

      <dl class="ticket-meta-grid">
        <div class="ticket-meta-item">
          <dt>Assigned Queue</dt>
          <dd class="mono-val tabular-nums">
            {{ knowledgeStore.selectedTicket.assigned_queue || "UNASSIGNED" }}
          </dd>
        </div>
        <div class="ticket-meta-item">
          <dt>Category</dt>
          <dd class="mono-val tabular-nums">
            {{ knowledgeStore.selectedTicket.category || "SYSTEM_SUPPORT" }}
          </dd>
        </div>
        <div class="ticket-meta-item">
          <dt>Study / Site</dt>
          <dd class="mono-val tabular-nums">
            {{
              formatTicketScope(
                knowledgeStore.selectedTicket.study_id,
                knowledgeStore.selectedTicket.site_id
              )
            }}
          </dd>
        </div>
        <div class="ticket-meta-item">
          <dt>Subject ID</dt>
          <dd class="mono-val tabular-nums">
            {{ knowledgeStore.selectedTicket.subject_id || "—" }}
          </dd>
        </div>
        <div class="ticket-meta-item">
          <dt>Acknowledge Target</dt>
          <dd class="mono-val tabular-nums">
            <Clock
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>{{
              formatTimestamp(
                knowledgeStore.selectedTicket.acknowledge_target_at
              )
            }}</span>
          </dd>
        </div>
        <div class="ticket-meta-item">
          <dt>Resolution SLA Target</dt>
          <dd class="mono-val tabular-nums">
            <Clock
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>{{
              formatTimestamp(knowledgeStore.selectedTicket.sla_target_at)
            }}</span>
          </dd>
        </div>
      </dl>
    </div>

    <div v-else-if="knowledgeStore.isTicketsLoading" class="loading-state">
      <div class="spinner" />
      <p>Loading scoped support tickets...</p>
    </div>

    <div
      v-else-if="knowledgeStore.ticketsError"
      id="drawer-tickets-error-alert"
      class="help-error-alert"
      role="alert"
    >
      <div class="error-alert-header">
        <AlertTriangle
          :size="20"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
        <h4 class="error-alert-title">Unable to Load Support Tickets</h4>
      </div>
      <p class="error-alert-message">{{ knowledgeStore.ticketsError }}</p>
      <div class="error-alert-actions">
        <button
          id="btn-retry-drawer-tickets"
          type="button"
          class="btn btn-secondary btn-retry"
          @click="handleRefreshDrawerTickets"
        >
          <RefreshCw
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Retry</span>
        </button>
      </div>
    </div>

    <div
      v-else-if="knowledgeStore.drawerTickets.length === 0"
      id="drawer-tickets-empty-state"
      class="empty-state"
    >
      <Ticket
        class="empty-icon"
        :size="20"
        aria-hidden="true"
        :stroke-width="1.75"
        focusable="false"
      />
      <h4>No Support Tickets in Scope</h4>
      <p>
        No persisted support tickets currently match
        <strong>{{ activeScopeSummary }}</strong>.
      </p>
    </div>

    <div v-else id="drawer-tickets-list" class="tickets-list">
      <button
        v-for="tkt in knowledgeStore.drawerTickets"
        :key="tkt.id || tkt.reference"
        type="button"
        class="drawer-ticket-card"
        :data-ticket-ref="tkt.reference || tkt.id"
        @click="handleSelectDrawerTicket(tkt)"
      >
        <div class="drawer-ticket-card-top">
          <span class="ticket-ref-code">{{ tkt.reference || tkt.id }}</span>
          <div class="drawer-ticket-badges">
            <span class="badge badge-status">{{ tkt.status || "OPEN" }}</span>
            <span v-if="tkt.priority" class="badge badge-priority">
              {{ tkt.priority }}
            </span>
          </div>
        </div>
        <div class="drawer-ticket-title">{{ tkt.title }}</div>
        <div class="drawer-ticket-meta">
          <span class="mono-val tabular-nums">{{
            tkt.assigned_queue || "UNASSIGNED"
          }}</span>
          <span
            v-if="tkt.study_id || tkt.site_id"
            class="ticket-meta-sep"
            aria-hidden="true"
            >·</span
          >
          <span
            v-if="tkt.study_id || tkt.site_id"
            class="mono-val tabular-nums"
          >
            {{ formatTicketScope(tkt.study_id, tkt.site_id) }}
          </span>
        </div>
      </button>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import {
  AlertTriangle,
  ArrowLeft,
  Clock,
  RefreshCw,
  Ticket,
} from "lucide-vue-next";
import { handoffFocus, handoffRetryFocus } from "ui";
import { useKnowledgeStore } from "../../stores/knowledge.js";
import {
  escapeTicketSelectorValue,
  formatTicketScope,
  formatTimestamp,
} from "./contextualHelpUtils.js";

const props = defineProps({
  getDrawerContainer: { type: Function, required: true },
});

const knowledgeStore = useKnowledgeStore();
const lastSelectedTicketRef = ref("");

const activeScopeSummary = computed(() => {
  const scope = knowledgeStore.activeRouteScope || {};
  const parts = [];
  if (scope.study_id) parts.push(`Study ${scope.study_id}`);
  if (scope.site_id) parts.push(`Site ${scope.site_id}`);
  return parts.length > 0
    ? parts.join(" · ")
    : "All authorized studies & sites";
});

async function handleRefreshDrawerTickets() {
  await handoffRetryFocus(
    async () => {
      if (knowledgeStore.selectedTicketId) {
        await knowledgeStore.selectDrawerTicket(
          knowledgeStore.selectedTicketId
        );
      }
      await knowledgeStore.fetchDrawerTickets();
    },
    {
      container: props.getDrawerContainer,
      includeDefaultFallbacks: false,
      isError: () =>
        Boolean(!knowledgeStore.selectedTicket && knowledgeStore.ticketsError),
      errorTargets: [
        "#btn-retry-drawer-tickets",
        "#btn-refresh-drawer-tickets",
        "#tab-contextual-tickets",
      ],
      successTargets: [
        () =>
          knowledgeStore.selectedTicket ? "#btn-back-to-ticket-list" : null,
        ".drawer-ticket-card",
        "#btn-refresh-drawer-tickets",
        "#tab-contextual-tickets",
      ],
    }
  );
}

async function handleSelectDrawerTicket(ticket) {
  const refValue = String(ticket?.reference || ticket?.id || "");
  if (refValue) lastSelectedTicketRef.value = refValue;
  const selectPromise = knowledgeStore.selectDrawerTicket(ticket);
  const targets = [
    "#btn-back-to-ticket-list",
    "#btn-refresh-drawer-tickets",
    "#tab-contextual-tickets",
  ];
  const opts = {
    container: props.getDrawerContainer,
    includeDefaultFallbacks: false,
  };
  await handoffFocus(targets, opts);
  await selectPromise;
  if (
    typeof document !== "undefined" &&
    (!document.activeElement || document.activeElement === document.body)
  ) {
    await handoffFocus(targets, opts);
  }
}

async function handleBackToTicketList() {
  const previousRef =
    String(
      knowledgeStore.selectedTicket?.reference ||
        knowledgeStore.selectedTicket?.id ||
        lastSelectedTicketRef.value ||
        ""
    ) || "";
  knowledgeStore.clearSelectedDrawerTicket();
  if (!knowledgeStore.hasLoadedDrawerTickets) {
    await knowledgeStore.fetchDrawerTickets();
  }
  await handoffFocus(
    [
      () => {
        if (!previousRef) return null;
        const root = props.getDrawerContainer();
        return root?.querySelector?.(
          `.drawer-ticket-card[data-ticket-ref="${escapeTicketSelectorValue(previousRef)}"]`
        );
      },
      ".drawer-ticket-card",
      "#btn-retry-drawer-tickets",
      "#btn-refresh-drawer-tickets",
      "#tab-contextual-tickets",
    ],
    {
      container: props.getDrawerContainer,
      includeDefaultFallbacks: false,
    }
  );
}
</script>

<style scoped src="./contextualHelpPanel.css"></style>
