// Local audit ledger: hash-chained blocks rendered reactively into the footer timeline.
import { createApp, computed, h, nextTick } from "vue";
import { buildLedgerBlock } from "ui";

import { state } from "../stores/state.js";
import { demoNowIso } from "../utils/demoMode.js";
import { humanizeExpenseReviewNotes } from "../utils/participantCopy.js";
import { openLedgerPeek } from "./peek.js";
import { getInstrumentDisplayName } from "./site-support.js";

function humanizeActionCode(action) {
  const words = String(action || "ACTIVITY")
    .replace(/[._-]+/g, " ")
    .trim()
    .toLowerCase();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : "Activity";
}

function resolveActivityRoute(action) {
  const upper = String(action || "").toUpperCase();
  if (
    upper === "SIGN_CONSENT" ||
    upper === "RECONSENT_EXECUTED" ||
    upper.includes("CONSENT")
  ) {
    return { route: "/consent", linkLabel: "Review consent" };
  }
  if (
    upper === "EPRO_SUBMIT" ||
    upper.includes("EPRO") ||
    upper.includes("SURVEY") ||
    upper.includes("DIARY") ||
    upper.includes("QUESTIONNAIRE")
  ) {
    return { route: "/compliance", linkLabel: "Open surveys" };
  }
  if (
    upper === "OCR_RECEIPT_EXTRACTED" ||
    upper === "EXPENSE_CLAIM_AUTO_APPROVED" ||
    upper === "EXPENSE_CLAIM_HELD_FOR_REVIEW" ||
    upper === "DIGITAL_DISBURSEMENT_DISPATCHED" ||
    upper === "AUTOMATIC_STIPEND_DISBURSED" ||
    upper.includes("RECEIPT") ||
    upper.includes("EXPENSE") ||
    upper.includes("STIPEND") ||
    upper.includes("DISBURSEMENT") ||
    upper.includes("PAYOUT") ||
    upper.includes("WALLET")
  ) {
    return { route: "/wallet", linkLabel: "Open wallet" };
  }
  return { route: null, linkLabel: null };
}

export function describeLedgerBlock(block) {
  const action = String(block?.action || "");
  const details =
    block?.details && typeof block.details === "object" ? block.details : {};
  const nav = resolveActivityRoute(action);

  switch (action) {
    case "GENESIS":
      return {
        title: "Study portal opened",
        summary: "Your companion session is ready on this device.",
        ...nav,
      };
    case "EPRO_SUBMIT": {
      const surveyTitle = details.diary_id
        ? getInstrumentDisplayName(details.diary_id)
        : "Study survey";
      return {
        title: "Survey answers signed",
        summary: `${surveyTitle} was signed and saved for your study team.`,
        ...nav,
      };
    }
    case "SIGN_CONSENT":
      return {
        title: "Study consent signed",
        summary: "Your signed informed consent form has been recorded.",
        ...nav,
      };
    case "RECONSENT_EXECUTED":
      return {
        title: "Updated study consent signed",
        summary: "You reviewed and signed the updated study consent.",
        ...nav,
      };
    case "ACKNOWLEDGE_NOTIFICATION":
      return {
        title: "Reminder marked as read",
        summary: "You confirmed a study reminder.",
        ...nav,
      };
    case "PIN_WRAPPER_RESET":
      return {
        title: "Security PIN reset",
        summary:
          "You verified your identity and set up a new security PIN on this device.",
        ...nav,
      };
    case "OCR_RECEIPT_EXTRACTED":
      return {
        title: "Receipt scanned",
        summary: details.merchant_name
          ? `Read receipt details from ${details.merchant_name}.`
          : "Receipt details were read from your uploaded image.",
        ...nav,
      };
    case "EXPENSE_CLAIM_AUTO_APPROVED":
      return {
        title: "Travel expense approved",
        summary:
          typeof details.net_amount === "number"
            ? `Approved $${details.net_amount.toFixed(2)} for reimbursement.`
            : "Your travel expense claim was approved.",
        ...nav,
      };
    case "EXPENSE_CLAIM_HELD_FOR_REVIEW":
      return {
        title: "Travel expense sent for review",
        summary:
          humanizeExpenseReviewNotes(details.review_notes) ||
          "Your study coordinator will review this receipt.",
        ...nav,
      };
    case "DIGITAL_DISBURSEMENT_DISPATCHED":
      return {
        title: "Payout sent",
        summary:
          typeof details.amount === "number"
            ? `Sent $${details.amount.toFixed(2)} to your linked payout method.`
            : "Your balance payout has been sent.",
        ...nav,
      };
    case "AUTOMATIC_STIPEND_DISBURSED":
      return {
        title: "Study stipend added",
        summary:
          typeof details.stipend_amount === "number"
            ? `Added $${details.stipend_amount.toFixed(2)} to your wallet.`
            : "Your study stipend was added to your wallet.",
        ...nav,
      };
    default:
      return {
        title: humanizeActionCode(action),
        summary: block?.reason || "Recorded in your activity history.",
        ...nav,
      };
  }
}

export function formatLedgerTime(isoTimestamp) {
  if (!isoTimestamp) return "";
  const parsed = new Date(isoTimestamp);
  return Number.isNaN(parsed.getTime())
    ? String(isoTimestamp)
    : parsed.toLocaleString();
}

export function getLedgerBlockKey(block, fallbackIndex) {
  if (block?.hash) return String(block.hash);
  const idx = block?.index ?? fallbackIndex;
  return `${idx}-${block?.timestamp ?? ""}-${block?.action ?? ""}`;
}

export function buildReactiveLedgerItems(blocks = []) {
  const reversed = Array.isArray(blocks) ? blocks.slice().reverse() : [];
  return reversed.map((block, idx) => {
    const fallbackIndex = reversed.length - 1 - idx;
    const blockIndex = String(block?.index ?? fallbackIndex);
    const key = getLedgerBlockKey(block, fallbackIndex);
    const { title, summary, route, linkLabel } = describeLedgerBlock(block);
    return {
      key,
      block,
      blockIndex,
      title,
      summary,
      route,
      linkLabel,
      formattedTime: formatLedgerTime(block?.timestamp),
      hashText: String(block?.hash ?? ""),
    };
  });
}

const standaloneApps = new WeakMap();

const StandaloneLedgerList = {
  name: "StandaloneLedgerList",
  setup() {
    const items = computed(() => buildReactiveLedgerItems(state.ledgerBlocks));
    return () => {
      if (items.value.length === 0) {
        return h(
          "div",
          { class: "loading-state", "data-ledger-empty": "true" },
          "No recent activity yet."
        );
      }
      return items.value.map((item) =>
        h(
          "div",
          {
            id: `ledger-row-${item.blockIndex}`,
            key: item.key,
            class: "ledger-item",
            "data-ledger-key": item.key,
            "data-ledger-index": item.blockIndex,
            onClick: (event) => {
              if (event?.target?.closest?.("button, a, details, summary")) {
                return;
              }
              const rowEl = event?.currentTarget || null;
              const triggerEl =
                rowEl?.querySelector?.(".btn-peek-ledger") || rowEl;
              const idx = Number(item.blockIndex);
              const targetBlock =
                item.block ||
                (Number.isFinite(idx) ? state.ledgerBlocks[idx] : null);
              if (targetBlock) {
                openLedgerPeek(targetBlock, idx, triggerEl);
              }
            },
          },
          [
            h("div", { class: "ledger-item-header" }, [
              h("span", [
                item.route
                  ? h(
                      "a",
                      {
                        href: item.route,
                        class: "ledger-activity-link",
                        "data-ledger-route": item.route,
                      },
                      [h("strong", { "data-ledger-title": "" }, item.title)]
                    )
                  : h("strong", { "data-ledger-title": "" }, item.title),
              ]),
              h("span", { class: "ledger-header-meta" }, [
                h(
                  "span",
                  { class: "tabular-nums", "data-ledger-time": "" },
                  item.formattedTime
                ),
                h(
                  "button",
                  {
                    id: `btn-peek-ledger-${item.blockIndex}`,
                    type: "button",
                    class: "btn btn-secondary btn-peek-ledger",
                    "data-id": item.blockIndex,
                    onClick: (event) => {
                      event.stopPropagation();
                      const idx = Number(item.blockIndex);
                      const targetBlock =
                        item.block ||
                        (Number.isFinite(idx) ? state.ledgerBlocks[idx] : null);
                      if (targetBlock) {
                        openLedgerPeek(targetBlock, idx, event.currentTarget);
                      }
                    },
                  },
                  [
                    "View Details",
                    h("span", { class: "sr-only" }, `: ${item.title}`),
                  ]
                ),
              ]),
            ]),
            h("div", { class: "ledger-item-details" }, [
              h("span", { "data-ledger-summary": "" }, item.summary),
              item.route && item.linkLabel
                ? h(
                    "a",
                    {
                      href: item.route,
                      class: "ledger-activity-cta",
                      "data-ledger-cta": item.route,
                    },
                    item.linkLabel
                  )
                : null,
            ]),
            h("details", { class: "ledger-verification-details" }, [
              h("summary", "Verification code"),
              h("div", { class: "ledger-hash" }, [
                h("code", { "data-ledger-hash": "" }, item.hashText),
              ]),
            ]),
          ]
        )
      );
    };
  },
};

export async function logAuditRecord(
  action,
  details,
  reason = "Patient action verified"
) {
  const index = state.ledgerBlocks.length;
  const timestamp = demoNowIso(index);
  const prevHash =
    index === 0
      ? "0000000000000000000000000000000000000000000000000000000000000000" // deid-ignore
      : state.ledgerBlocks[index - 1].hash;

  const block = await buildLedgerBlock(
    index,
    timestamp,
    action,
    details,
    reason,
    prevHash
  );

  state.ledgerBlocks.push(block);
  renderLedger();
  await nextTick();
  return block;
}

export function renderLedger() {
  if (typeof document === "undefined") return;
  const container = document.getElementById("portal-ledger-timeline");
  if (!container) return;
  if (container.getAttribute("data-vue-ledger") === "true") {
    return;
  }
  if (!standaloneApps.has(container)) {
    while (container.firstChild) {
      container.removeChild(container.firstChild);
    }
    const app = createApp(StandaloneLedgerList);
    app.mount(container);
    standaloneApps.set(container, app);
  }
}
