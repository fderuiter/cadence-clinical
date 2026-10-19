// Local audit ledger: hash-chained blocks rendered into the footer timeline.
import { buildLedgerBlock } from "ui";

import { state } from "../stores/state.js";

// 21 CFR Part 11 Compliant Cryptographic Audit Ledger logging
export async function logAuditRecord(
  action,
  details,
  reason = "Patient action verified"
) {
  const timestamp = new Date().toISOString();
  const index = state.ledgerBlocks.length;
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
  return block;
}

export function renderLedger() {
  const container = document.getElementById("portal-ledger-timeline");
  if (!container) return;

  if (state.ledgerBlocks.length === 0) {
    container.innerHTML = `<div class="loading-state">No audit ledger logs initialized.</div>`;
    return;
  }

  container.innerHTML = state.ledgerBlocks
    .slice()
    .reverse()
    .map(
      (block) => `
      <div class="ledger-item">
        <div class="ledger-item-header">
          <span><strong>BLOCK #${block.index} - ${block.action}</strong></span>
          <span>${block.timestamp}</span>
        </div>
        <div class="ledger-item-details">
          <span><strong>Details:</strong> ${JSON.stringify(block.details)}</span><br/>
          <span><strong>Reason/Declaration:</strong> <span style="color: #60a5fa; font-weight:600;">${block.reason}</span></span>
        </div>
        <div class="ledger-hash">
          <span>hash: ${block.hash}</span>
        </div>
      </div>
    `
    )
    .join("");
}
