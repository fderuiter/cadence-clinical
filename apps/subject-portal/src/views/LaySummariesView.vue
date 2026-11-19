<template>
  <section
    id="view-lay-summaries"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-lay-summaries' }"
    role="tabpanel"
    aria-labelledby="btn-tab-btn-lay-summaries"
    tabindex="0"
  >
    <div class="view-header">
      <h2>Plain-Language Clinical Trial Results</h2>
      <p>
        EU CTR Regulation No 536/2014 Article 37 summary of trial findings,
        clinical benefits, and safety profiles.
      </p>
    </div>

    <div class="card" style="margin-bottom: 20px">
      <div style="display: flex; gap: 8px; margin-bottom: 8px">
        <span class="badge badge-gxp">IRB Approved</span>
        <span class="badge badge-regulatory">Phase III Results</span>
      </div>
      <h3 style="margin-top: 0">
        Plain Language Summary: CAD-2026 Trial Findings
      </h3>
      <p style="font-size: 14px; color: var(--text-muted); line-height: 1.5">
        This study evaluated whether CAD-2026 improved clinical outcomes in
        adult patients. Participants receiving CAD-2026 showed a 42% relative
        improvement in disease control with manageable side effects.
      </p>
      <div
        style="
          margin-top: 16px;
          padding: 12px;
          background: var(--bg-muted);
          border-radius: 6px;
        "
      >
        <h4 style="margin: 0 0 6px 0; font-size: 14px">
          Participant Acknowledgment &amp; Comprehension
        </h4>
        <p style="font-size: 13px; margin: 0 0 10px 0">
          Rating: ★★★★★ (5/5 Stars)
        </p>
        <button type="button" class="btn btn-secondary" style="font-size: 13px">
          🔏 View Cryptographically Sealed Confirmation Receipt (SHA-256)
        </button>
      </div>
    </div>
  </section>
</template>

<script setup>
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();
</script>
