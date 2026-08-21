<template>
  <div
    id="section-datalock"
    class="dashboard-section active"
  >
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">GxP Lock Governance</span>
          <span class="badge badge-standard">6-Tier Hierarchical Gating</span>
        </div>
        <h2 class="view-title">
          Granular Data Lock &amp; Freeze Console
        </h2>
        <p class="view-subtitle">
          Manage regulatory lock governance across Study, Site, Subject, Visit,
          Form, and Field scopes. Enforces multi-tier hierarchical inheritance,
          step-up dual signatures (<code>X-Sig-Token</code>), and strict
          mandatory &ge;50-character GxP unlock justification audit trails.
        </p>
      </div>
      <div class="header-action-group">
        <button
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="refreshLocks"
        >
          <span class="btn-icon">🔄</span> Refresh Status
        </button>
      </div>
    </div>

    <!-- Quick Stats Cards Grid -->
    <div class="stats-grid">
      <div class="stat-card">
        <div class="stat-icon locked-icon">
          🔒
        </div>
        <div class="stat-details">
          <div class="stat-value">
            {{ activeLocksCount }}
          </div>
          <div class="stat-label">
            Active Hard Locks
          </div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon frozen-icon">
          ❄️
        </div>
        <div class="stat-details">
          <div class="stat-value">
            {{ frozenScopesCount }}
          </div>
          <div class="stat-label">
            Frozen Monitoring Scopes
          </div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon unlocked-icon">
          🔓
        </div>
        <div class="stat-details">
          <div class="stat-value">
            {{ unlockedEventsCount }}
          </div>
          <div class="stat-label">
            Audit Unlock Overrides
          </div>
        </div>
      </div>
      <div class="stat-card">
        <div class="stat-icon tree-icon">
          🌳
        </div>
        <div class="stat-details">
          <div class="stat-value">
            6 Tiers
          </div>
          <div class="stat-label">
            Inheritance Depth
          </div>
        </div>
      </div>
    </div>

    <!-- Main Workspace: Split View with Tree Navigator & Detail Panel -->
    <div class="datalock-workspace-grid">
      <!-- Left Column: Hierarchical Scope Explorer Tree -->
      <div class="card tree-explorer-card">
        <div class="card-header-flex">
          <h3 class="card-title">
            <span class="title-icon">🗂️</span> Clinical Scope Hierarchy
          </h3>
          <div class="tree-filter-box">
            <input
              v-model="searchQuery"
              type="text"
              class="form-input search-tree-input"
              placeholder="Filter nodes..."
            >
          </div>
        </div>

        <div class="tree-container">
          <div
            v-if="isLoading"
            class="tree-loading-state"
          >
            <div class="spinner" />
            <span>Loading lock hierarchy...</span>
          </div>

          <div
            v-else
            class="tree-root"
          >
            <!-- Study Level Node -->
            <div class="tree-node study-node">
              <div
                class="node-row"
                :class="{ selected: selectedNode?.id === studyTree.id }"
                @click="selectNode(studyTree)"
              >
                <button
                  type="button"
                  class="expand-btn"
                  @click.stop="toggleExpand(studyTree)"
                >
                  {{ studyTree.expanded ? "▼" : "▶" }}
                </button>
                <span class="node-icon">🏛️</span>
                <span class="node-name">{{ studyTree.name }}</span>
                <span
                  class="node-badge"
                  :class="getBadgeClass(studyTree.status)"
                >
                  {{ studyTree.status }}
                </span>
                <div class="node-actions">
                  <button
                    v-if="studyTree.status === 'UNLOCKED'"
                    class="btn-mini btn-mini-freeze"
                    title="Freeze Study"
                    @click.stop="openLockDialog(studyTree, 'FREEZE')"
                  >
                    ❄️
                  </button>
                  <button
                    v-if="studyTree.status === 'UNLOCKED'"
                    class="btn-mini btn-mini-lock"
                    title="Hard Lock Study"
                    @click.stop="openLockDialog(studyTree, 'HARD_LOCK')"
                  >
                    🔒
                  </button>
                  <button
                    v-else
                    class="btn-mini btn-mini-unlock"
                    title="Unlock Study Override"
                    @click.stop="openUnlockDialog(studyTree)"
                  >
                    🔓
                  </button>
                </div>
              </div>

              <!-- Sites List -->
              <div
                v-if="studyTree.expanded"
                class="tree-children"
              >
                <div
                  v-for="site in filteredSites"
                  :key="site.id"
                  class="tree-node site-node"
                >
                  <div
                    class="node-row"
                    :class="{ selected: selectedNode?.id === site.id }"
                    @click="selectNode(site)"
                  >
                    <button
                      type="button"
                      class="expand-btn"
                      @click.stop="toggleExpand(site)"
                    >
                      {{ site.expanded ? "▼" : "▶" }}
                    </button>
                    <span class="node-icon">🏥</span>
                    <span class="node-name">{{ site.name }}</span>
                    <span
                      class="node-badge"
                      :class="getBadgeClass(site.status)"
                    >
                      {{ site.status }}
                    </span>
                    <div class="node-actions">
                      <button
                        v-if="site.status === 'UNLOCKED'"
                        class="btn-mini btn-mini-freeze"
                        title="Freeze Site"
                        @click.stop="openLockDialog(site, 'FREEZE')"
                      >
                        ❄️
                      </button>
                      <button
                        v-if="site.status === 'UNLOCKED'"
                        class="btn-mini btn-mini-lock"
                        title="Hard Lock Site"
                        @click.stop="openLockDialog(site, 'HARD_LOCK')"
                      >
                        🔒
                      </button>
                      <button
                        v-else
                        class="btn-mini btn-mini-unlock"
                        title="Unlock Site"
                        @click.stop="openUnlockDialog(site)"
                      >
                        🔓
                      </button>
                    </div>
                  </div>

                  <!-- Subjects List -->
                  <div
                    v-if="site.expanded"
                    class="tree-children"
                  >
                    <div
                      v-for="subject in site.subjects"
                      :key="subject.id"
                      class="tree-node subject-node"
                    >
                      <div
                        class="node-row"
                        :class="{ selected: selectedNode?.id === subject.id }"
                        @click="selectNode(subject)"
                      >
                        <button
                          type="button"
                          class="expand-btn"
                          @click.stop="toggleExpand(subject)"
                        >
                          {{ subject.expanded ? "▼" : "▶" }}
                        </button>
                        <span class="node-icon">👤</span>
                        <span class="node-name">{{ subject.name }}</span>
                        <span
                          class="node-badge"
                          :class="getBadgeClass(subject.status)"
                        >
                          {{ subject.status }}
                        </span>
                        <div class="node-actions">
                          <button
                            v-if="subject.status === 'UNLOCKED'"
                            class="btn-mini btn-mini-lock"
                            title="Hard Lock Subject"
                            @click.stop="openLockDialog(subject, 'HARD_LOCK')"
                          >
                            🔒
                          </button>
                          <button
                            v-else
                            class="btn-mini btn-mini-unlock"
                            title="Unlock Subject"
                            @click.stop="openUnlockDialog(subject)"
                          >
                            🔓
                          </button>
                        </div>
                      </div>

                      <!-- Visits List -->
                      <div
                        v-if="subject.expanded"
                        class="tree-children"
                      >
                        <div
                          v-for="visit in subject.visits"
                          :key="visit.id"
                          class="tree-node visit-node"
                        >
                          <div
                            class="node-row"
                            :class="{ selected: selectedNode?.id === visit.id }"
                            @click="selectNode(visit)"
                          >
                            <button
                              type="button"
                              class="expand-btn"
                              @click.stop="toggleExpand(visit)"
                            >
                              {{ visit.expanded ? "▼" : "▶" }}
                            </button>
                            <span class="node-icon">📅</span>
                            <span class="node-name">{{ visit.name }}</span>
                            <span
                              class="node-badge"
                              :class="getBadgeClass(visit.status)"
                            >
                              {{ visit.status }}
                            </span>
                            <div class="node-actions">
                              <button
                                v-if="visit.status === 'UNLOCKED'"
                                class="btn-mini btn-mini-lock"
                                title="Lock Visit"
                                @click.stop="openLockDialog(visit, 'HARD_LOCK')"
                              >
                                🔒
                              </button>
                              <button
                                v-else
                                class="btn-mini btn-mini-unlock"
                                title="Unlock Visit"
                                @click.stop="openUnlockDialog(visit)"
                              >
                                🔓
                              </button>
                            </div>
                          </div>

                          <!-- Forms List -->
                          <div
                            v-if="visit.expanded"
                            class="tree-children"
                          >
                            <div
                              v-for="form in visit.forms"
                              :key="form.id"
                              class="tree-node form-node"
                            >
                              <div
                                class="node-row"
                                :class="{
                                  selected: selectedNode?.id === form.id,
                                }"
                                @click="selectNode(form)"
                              >
                                <button
                                  type="button"
                                  class="expand-btn"
                                  @click.stop="toggleExpand(form)"
                                >
                                  {{ form.expanded ? "▼" : "▶" }}
                                </button>
                                <span class="node-icon">📋</span>
                                <span class="node-name">{{ form.name }}</span>
                                <span
                                  class="node-badge"
                                  :class="getBadgeClass(form.status)"
                                >
                                  {{ form.status }}
                                </span>
                                <div class="node-actions">
                                  <button
                                    v-if="form.status === 'UNLOCKED'"
                                    class="btn-mini btn-mini-lock"
                                    title="Lock Form"
                                    @click.stop="
                                      openLockDialog(form, 'HARD_LOCK')
                                    "
                                  >
                                    🔒
                                  </button>
                                  <button
                                    v-else
                                    class="btn-mini btn-mini-unlock"
                                    title="Unlock Form"
                                    @click.stop="openUnlockDialog(form)"
                                  >
                                    🔓
                                  </button>
                                </div>
                              </div>

                              <!-- Fields List -->
                              <div
                                v-if="form.expanded"
                                class="tree-children"
                              >
                                <div
                                  v-for="field in form.fields"
                                  :key="field.id"
                                  class="tree-node field-node"
                                >
                                  <div
                                    class="node-row"
                                    :class="{
                                      selected: selectedNode?.id === field.id,
                                    }"
                                    @click="selectNode(field)"
                                  >
                                    <span class="tree-leaf-spacer" />
                                    <span class="node-icon">🏷️</span>
                                    <span class="node-name">{{
                                      field.name
                                    }}</span>
                                    <span
                                      class="node-badge"
                                      :class="getBadgeClass(field.status)"
                                    >
                                      {{ field.status }}
                                    </span>
                                    <div class="node-actions">
                                      <button
                                        v-if="field.status === 'UNLOCKED'"
                                        class="btn-mini btn-mini-lock"
                                        title="Lock Field"
                                        @click.stop="
                                          openLockDialog(field, 'HARD_LOCK')
                                        "
                                      >
                                        🔒
                                      </button>
                                      <button
                                        v-else
                                        class="btn-mini btn-mini-unlock"
                                        title="Unlock Field"
                                        @click.stop="openUnlockDialog(field)"
                                      >
                                        🔓
                                      </button>
                                    </div>
                                  </div>
                                </div>
                              </div>
                            </div>
                          </div>
                        </div>
                      </div>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Right Column: Node Details & Inspection Pane -->
      <div class="card detail-inspection-card">
        <div class="card-header-flex">
          <h3 class="card-title">
            <span class="title-icon">🔍</span> Scope Governance Inspector
          </h3>
          <span
            v-if="selectedNode"
            class="scope-type-pill"
          >
            {{ selectedNode.type }} Level
          </span>
        </div>

        <div
          v-if="!selectedNode"
          class="empty-selection-state"
        >
          <span class="empty-icon">👈</span>
          <h4>Select a Node from Hierarchy</h4>
          <p>
            Choose any Study, Site, Subject, Visit, Form, or Field node to view
            its active lock parameters or perform governance actions.
          </p>
        </div>

        <div
          v-else
          class="inspection-body"
        >
          <div
            class="scope-summary-banner"
            :class="selectedNode.status.toLowerCase()"
          >
            <div class="banner-status-icon">
              {{
                selectedNode.status === "LOCKED" ||
                  selectedNode.status === "HARD_LOCK"
                  ? "🔒"
                  : selectedNode.status === "FROZEN"
                    ? "❄️"
                    : "🔓"
              }}
            </div>
            <div class="banner-text">
              <h4>{{ selectedNode.name }}</h4>
              <p class="node-id-sub">
                Identifier: <code>{{ selectedNode.id }}</code>
              </p>
              <p class="node-status-text">
                Current Status: <strong>{{ selectedNode.status }}</strong>
              </p>
            </div>
          </div>

          <!-- Hierarchy Inheritance Info -->
          <div class="info-group">
            <h5 class="info-group-title">
              Hierarchical Inheritance Chain
            </h5>
            <div class="inheritance-breadcrumbs">
              <span class="crumb">Study: {{ studyTree.id }}</span>
              <span class="crumb-arrow">&gt;</span>
              <span class="crumb">Site: {{ selectedNode.site_id || "Inherited" }}</span>
              <span class="crumb-arrow">&gt;</span>
              <span class="crumb">Subject: {{ selectedNode.subject_id || "Inherited" }}</span>
              <span class="crumb-arrow">&gt;</span>
              <span class="crumb">Form: {{ selectedNode.form_id || "Inherited" }}</span>
            </div>
          </div>

          <!-- Action Buttons Area -->
          <div class="action-panel">
            <h5 class="info-group-title">
              Governance Operations
            </h5>
            <div class="action-btn-row">
              <button
                v-if="selectedNode.status === 'UNLOCKED'"
                class="btn btn-secondary"
                @click="openLockDialog(selectedNode, 'FREEZE')"
              >
                ❄️ Soft Freeze Data
              </button>
              <button
                v-if="selectedNode.status === 'UNLOCKED'"
                class="btn btn-primary"
                @click="openLockDialog(selectedNode, 'HARD_LOCK')"
              >
                🔒 Execute Hard Lock (Dual Sig)
              </button>
              <button
                v-if="selectedNode.status !== 'UNLOCKED'"
                class="btn btn-danger"
                @click="openUnlockDialog(selectedNode)"
              >
                🔓 Request Unlock Override (&ge;50 chars)
              </button>
            </div>
          </div>

          <!-- Active Lock Records Table for this Scope -->
          <div class="active-records-section">
            <h5 class="info-group-title">
              Scope Audit Records
            </h5>
            <div class="table-responsive">
              <table class="table audit-table">
                <thead>
                  <tr>
                    <th>Lock ID</th>
                    <th>Scope</th>
                    <th>Status</th>
                    <th>Created By</th>
                    <th>Reason / Justification</th>
                    <th>Timestamp</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="rec in matchingRecords"
                    :key="rec.lock_id"
                  >
                    <td>
                      <code>{{ rec.lock_id }}</code>
                    </td>
                    <td>{{ rec.scope_type || rec.scope }}</td>
                    <td>
                      <span
                        class="node-badge"
                        :class="getBadgeClass(rec.status)"
                      >
                        {{ rec.status }}
                      </span>
                    </td>
                    <td>{{ rec.created_by || rec.locked_by }}</td>
                    <td class="text-truncate-cell">
                      {{ rec.unlock_justification || rec.reason_for_change }}
                    </td>
                    <td>{{ formatDate(rec.created_at || rec.locked_at) }}</td>
                  </tr>
                  <tr v-if="matchingRecords.length === 0">
                    <td
                      colspan="6"
                      class="text-center text-muted"
                    >
                      No active data locks recorded for this specific scope
                      node.
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Lock Action Modal Dialog -->
    <div
      v-if="isLockModalOpen"
      class="modal-overlay"
      @click.self="isLockModalOpen = false"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h3>
            {{
              lockForm.action === "FREEZE"
                ? "❄️ Soft Freeze Data Scope"
                : "🔒 Execute Cryptographic Hard Lock"
            }}
          </h3>
          <button
            class="modal-close-btn"
            @click="isLockModalOpen = false"
          >
            &times;
          </button>
        </div>
        <div class="modal-body">
          <div class="modal-target-info">
            <p>
              <strong>Target Scope:</strong> {{ lockForm.scope_type }} (<code>{{
                lockForm.scope_id
              }}</code>)
            </p>
            <p><strong>Study ID:</strong> {{ lockForm.study_id }}</p>
          </div>

          <div class="form-group">
            <label class="form-label">Lock Mode:</label>
            <div class="radio-row">
              <label class="radio-label">
                <input
                  v-model="lockForm.action"
                  type="radio"
                  value="FREEZE"
                >
                <span>FREEZE (Monitoring Hold)</span>
              </label>
              <label class="radio-label">
                <input
                  v-model="lockForm.action"
                  type="radio"
                  value="HARD_LOCK"
                >
                <span>HARD_LOCK (21 CFR Part 11 Seal)</span>
              </label>
            </div>
          </div>

          <div
            v-if="lockForm.action === 'HARD_LOCK'"
            class="form-group"
          >
            <label class="form-label">Step-Up Dual Signature Token (<code>X-Sig-Token</code>):</label>
            <input
              v-model="lockForm.sig_token"
              type="password"
              class="form-input"
              placeholder="Enter re-authentication JWT or passkey token..."
            >
            <small class="form-help">
              Required for Part 11 Hard Lock authorization.
            </small>
          </div>

          <div class="form-group">
            <label class="form-label">GxP Reason for Change:
              <span class="required-star">*</span></label>
            <textarea
              v-model="lockForm.reason"
              class="form-textarea"
              rows="3"
              placeholder="Enter formal reason for locking this data scope..."
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            class="btn btn-secondary"
            @click="isLockModalOpen = false"
          >
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="!lockForm.reason.trim() || isSubmitting"
            @click="submitLock"
          >
            {{ isSubmitting ? "Locking..." : "Confirm Lock Operation" }}
          </button>
        </div>
      </div>
    </div>

    <!-- Unlock Justification Modal Dialog (Strict >= 50 Chars) -->
    <div
      v-if="isUnlockModalOpen"
      class="modal-overlay"
      @click.self="isUnlockModalOpen = false"
    >
      <div class="modal-card unlock-modal">
        <div class="modal-header">
          <h3>🔓 GxP Unlock Override Justification</h3>
          <button
            class="modal-close-btn"
            @click="isUnlockModalOpen = false"
          >
            &times;
          </button>
        </div>
        <div class="modal-body">
          <div class="alert-warning-banner">
            <span class="banner-icon">⚠️</span>
            <div>
              <strong>Regulatory Integrity Warning (21 CFR Part 11)</strong>
              <p>
                Unlocking previously locked clinical data requires a detailed
                justification of at least
                <strong>50 characters</strong> detailing clinical necessity,
                PI/CRA approval, and discrepancy scope.
              </p>
            </div>
          </div>

          <div class="modal-target-info">
            <p>
              <strong>Unlocking Scope:</strong>
              {{ unlockForm.scope_type }} (<code>{{ unlockForm.scope_id }}</code>)
            </p>
          </div>

          <div class="form-group">
            <label class="form-label">
              Mandatory Unlock Justification (&ge; 50 Characters):
              <span class="required-star">*</span>
            </label>
            <textarea
              v-model="unlockForm.justification"
              class="form-textarea"
              :class="{
                'input-valid': isJustificationValid,
                'input-invalid':
                  unlockForm.justification.length > 0 && !isJustificationValid,
              }"
              rows="4"
              placeholder="Describe the clinical rationale, CRA monitor approval, and specific queries necessitating this unlock override..."
            />
            <div class="char-counter-row">
              <span
                class="char-counter"
                :class="{
                  'counter-met': isJustificationValid,
                  'counter-unmet': !isJustificationValid,
                }"
              >
                {{ unlockForm.justification.length }} / 50 characters required
              </span>
              <span
                v-if="isJustificationValid"
                class="counter-status valid"
              >
                ✓ Justification length criteria satisfied
              </span>
              <span
                v-else
                class="counter-status invalid"
              >
                Minimum 50 characters required to unlock
              </span>
            </div>
          </div>

          <div class="form-group">
            <label class="form-label">GxP Reason for Change Code:</label>
            <select
              v-model="unlockForm.reason_code"
              class="form-select"
            >
              <option value="CRA_QUERY_CORRECTION">
                CRA Monitor Query Resolution
              </option>
              <option value="SAFETY_EVENT_UPDATE">
                Adverse Event / SAE Timely Update
              </option>
              <option value="PROTOCOL_DEVIATION_CORRECTION">
                Protocol Deviation Correction
              </option>
              <option value="AUDITOR_OVERRIDE">
                Quality / Auditor Requested Re-entry
              </option>
            </select>
          </div>
        </div>
        <div class="modal-footer">
          <button
            class="btn btn-secondary"
            @click="isUnlockModalOpen = false"
          >
            Cancel
          </button>
          <button
            class="btn btn-danger"
            :disabled="!isJustificationValid || isSubmitting"
            @click="submitUnlock"
          >
            {{ isSubmitting ? "Unlocking..." : "Confirm Unlock Override" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
export default {
  name: "DataLockView",
  data() {
    return {
      isLoading: false,
      isSubmitting: false,
      searchQuery: "",
      selectedNode: null,
      activeLocksCount: 0,
      frozenScopesCount: 0,
      unlockedEventsCount: 0,
      allLockRecords: [],

      // Modal Dialog States
      isLockModalOpen: false,
      isUnlockModalOpen: false,

      lockForm: {
        scope_type: "FORM",
        scope_id: "",
        study_id: "STUDY-001",
        site_id: "",
        subject_id: "",
        visit_id: "",
        form_id: "",
        field_name: "",
        action: "HARD_LOCK",
        sig_token: "",
        reason: "",
      },

      unlockForm: {
        lock_id: "",
        scope_type: "FORM",
        scope_id: "",
        study_id: "STUDY-001",
        site_id: "",
        subject_id: "",
        form_id: "",
        justification: "",
        reason_code: "CRA_QUERY_CORRECTION",
      },

      // Hierarchical Tree Data Model
      studyTree: {
        id: "STUDY-001",
        name: "Protocol CAD-001: Oncology Solid Tumor Phase II",
        type: "STUDY",
        status: "UNLOCKED",
        expanded: true,
        sites: [
          {
            id: "SITE-101",
            name: "Site 101 - Memorial Research Hospital",
            type: "SITE",
            site_id: "SITE-101",
            status: "UNLOCKED",
            expanded: true,
            subjects: [
              {
                id: "SUBJ-101-001",
                name: "Subject 101-001 (Cohort A)",
                type: "SUBJECT",
                site_id: "SITE-101",
                subject_id: "SUBJ-101-001",
                status: "UNLOCKED",
                expanded: true,
                visits: [
                  {
                    id: "VISIT-V1-SCR",
                    name: "Visit 1: Screening & Baseline",
                    type: "VISIT",
                    site_id: "SITE-101",
                    subject_id: "SUBJ-101-001",
                    visit_id: "VISIT-V1-SCR",
                    status: "UNLOCKED",
                    expanded: true,
                    forms: [
                      {
                        id: "FORM-DM-01",
                        name: "Demographics & Medical History",
                        type: "FORM",
                        site_id: "SITE-101",
                        subject_id: "SUBJ-101-001",
                        visit_id: "VISIT-V1-SCR",
                        form_id: "FORM-DM-01",
                        status: "UNLOCKED",
                        expanded: false,
                        fields: [
                          {
                            id: "FIELD-BRTHDTC",
                            name: "Birth Date (BRTHDTC)",
                            type: "FIELD",
                            form_id: "FORM-DM-01",
                            status: "UNLOCKED",
                          },
                          {
                            id: "FIELD-SEX",
                            name: "Biological Sex (SEX)",
                            type: "FIELD",
                            form_id: "FORM-DM-01",
                            status: "UNLOCKED",
                          },
                          {
                            id: "FIELD-RACE",
                            name: "Race / Ethnicity (RACE)",
                            type: "FIELD",
                            form_id: "FORM-DM-01",
                            status: "UNLOCKED",
                          },
                        ],
                      },
                      {
                        id: "FORM-VS-01",
                        name: "Vital Signs & Physical Exam",
                        type: "FORM",
                        site_id: "SITE-101",
                        subject_id: "SUBJ-101-001",
                        visit_id: "VISIT-V1-SCR",
                        form_id: "FORM-VS-01",
                        status: "UNLOCKED",
                        expanded: false,
                        fields: [
                          {
                            id: "FIELD-SYSBP",
                            name: "Systolic Blood Pressure (SYSBP)",
                            type: "FIELD",
                            form_id: "FORM-VS-01",
                            status: "UNLOCKED",
                          },
                          {
                            id: "FIELD-DIABP",
                            name: "Diastolic Blood Pressure (DIABP)",
                            type: "FIELD",
                            form_id: "FORM-VS-01",
                            status: "UNLOCKED",
                          },
                          {
                            id: "FIELD-PULSE",
                            name: "Heart Rate Pulse (PULSE)",
                            type: "FIELD",
                            form_id: "FORM-VS-01",
                            status: "UNLOCKED",
                          },
                        ],
                      },
                    ],
                  },
                ],
              },
            ],
          },
        ],
      },
    };
  },

  computed: {
    isJustificationValid() {
      return this.unlockForm.justification.trim().length >= 50;
    },

    filteredSites() {
      if (!this.searchQuery.trim()) {
        return this.studyTree.sites;
      }
      const q = this.searchQuery.toLowerCase();
      return this.studyTree.sites.filter(
        (s) =>
          s.name.toLowerCase().includes(q) || s.id.toLowerCase().includes(q)
      );
    },

    matchingRecords() {
      if (!this.selectedNode) return [];
      const nid = this.selectedNode.id;
      return this.allLockRecords.filter(
        (r) =>
          r.scope_id === nid ||
          r.form_id === nid ||
          r.subject_id === nid ||
          r.site_id === nid ||
          r.study_id === nid
      );
    },
  },

  mounted() {
    this.refreshLocks();
  },

  methods: {
    getBadgeClass(status) {
      if (!status) return "badge-unlocked";
      const s = status.toUpperCase();
      if (s === "LOCKED" || s === "HARD_LOCK") return "badge-locked";
      if (s === "FROZEN") return "badge-frozen";
      return "badge-unlocked";
    },

    formatDate(iso) {
      if (!iso) return "—";
      try {
        const d = new Date(iso);
        return d.toLocaleString("en-US", {
          month: "short",
          day: "2-digit",
          hour: "2-digit",
          minute: "2-digit",
        });
      } catch {
        return iso;
      }
    },

    toggleExpand(node) {
      node.expanded = !node.expanded;
    },

    selectNode(node) {
      this.selectedNode = node;
    },

    async refreshLocks() {
      this.isLoading = true;
      try {
        const response = await fetch(
          "/api/v1/execution/locks?study_id=STUDY-001"
        );
        if (response.ok) {
          const data = await response.json();
          this.allLockRecords = data || [];
          this.calculateStats();
          this.syncTreeStatuses();
        }
      } catch (err) {
        console.warn(
          "Failed to fetch locks from API, operating with local state:",
          err
        );
      } finally {
        this.isLoading = false;
      }
    },

    calculateStats() {
      let active = 0;
      let frozen = 0;
      let unlocked = 0;

      for (const r of this.allLockRecords) {
        if (r.is_active) {
          if (r.lock_type === "FROZEN") frozen++;
          else active++;
        } else {
          unlocked++;
        }
      }

      this.activeLocksCount = active;
      this.frozenScopesCount = frozen;
      this.unlockedEventsCount = unlocked;
    },

    syncTreeStatuses() {
      // Helper to update status by finding matching active lock
      const findStatus = (scopeType, scopeId) => {
        const match = this.allLockRecords.find(
          (r) =>
            r.is_active &&
            (r.scope_type === scopeType || r.scope === scopeType) &&
            r.scope_id === scopeId
        );
        return match ? match.lock_type || match.status : "UNLOCKED";
      };

      this.studyTree.status = findStatus("STUDY", this.studyTree.id);

      for (const site of this.studyTree.sites) {
        site.status = findStatus("SITE", site.id);
        for (const sub of site.subjects || []) {
          sub.status = findStatus("SUBJECT", sub.id);
          for (const visit of sub.visits || []) {
            visit.status = findStatus("VISIT", visit.id);
            for (const form of visit.forms || []) {
              form.status = findStatus("FORM", form.id);
              for (const field of form.fields || []) {
                field.status = findStatus("FIELD", field.id);
              }
            }
          }
        }
      }
    },

    openLockDialog(node, action) {
      this.lockForm = {
        scope_type: node.type,
        scope_id: node.id,
        study_id: "STUDY-001",
        site_id: node.site_id || "",
        subject_id: node.subject_id || "",
        visit_id: node.visit_id || "",
        form_id: node.form_id || "",
        field_name: node.type === "FIELD" ? node.id : "",
        action: action,
        sig_token: "",
        reason: "",
      };
      this.isLockModalOpen = true;
    },

    openUnlockDialog(node) {
      const match = this.allLockRecords.find(
        (r) => r.is_active && (r.scope_id === node.id || r.form_id === node.id)
      );

      this.unlockForm = {
        lock_id: match ? match.lock_id : "",
        scope_type: node.type,
        scope_id: node.id,
        study_id: "STUDY-001",
        site_id: node.site_id || "",
        subject_id: node.subject_id || "",
        form_id: node.form_id || "",
        justification: "",
        reason_code: "CRA_QUERY_CORRECTION",
      };
      this.isUnlockModalOpen = true;
    },

    async submitLock() {
      this.isSubmitting = true;
      try {
        const payload = {
          scope_type: this.lockForm.scope_type,
          scope_id: this.lockForm.scope_id,
          study_id: this.lockForm.study_id,
          site_id: this.lockForm.site_id || null,
          subject_id: this.lockForm.subject_id || null,
          visit_id: this.lockForm.visit_id || null,
          form_id: this.lockForm.form_id || null,
          field_name: this.lockForm.field_name || null,
          action: this.lockForm.action,
          lock_type: this.lockForm.action === "FREEZE" ? "FROZEN" : "HARD_LOCK",
          reason_for_change: this.lockForm.reason,
        };

        const headers = { "Content-Type": "application/json" };
        if (this.lockForm.sig_token) {
          headers["X-Sig-Token"] = this.lockForm.sig_token;
        }

        const res = await fetch("/api/v1/execution/locks/lock", {
          method: "POST",
          headers,
          body: JSON.stringify(payload),
        });

        if (res.ok) {
          const result = await res.json();
          this.allLockRecords.unshift(
            result.record || {
              lock_id: result.lock_id,
              scope_type: payload.scope_type,
              scope_id: payload.scope_id,
              status: result.status,
              lock_type: result.status,
              is_active: true,
              created_by: "Current User",
              reason_for_change: payload.reason_for_change,
              created_at: new Date().toISOString(),
            }
          );
          this.calculateStats();
          this.syncTreeStatuses();
          this.isLockModalOpen = false;
        }
      } catch (err) {
        console.error("Lock operation failed:", err);
      } finally {
        this.isSubmitting = false;
      }
    },

    async submitUnlock() {
      if (!this.isJustificationValid) return;
      this.isSubmitting = true;
      try {
        const payload = {
          lock_id: this.unlockForm.lock_id || undefined,
          scope_type: this.unlockForm.scope_type,
          scope_id: this.unlockForm.scope_id,
          justification: this.unlockForm.justification,
          reason_for_change: `${this.unlockForm.reason_code}: ${this.unlockForm.justification}`,
        };

        const res = await fetch("/api/v1/execution/locks/unlock", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        });

        if (res.ok) {
          const result = await res.json();
          const target = this.allLockRecords.find(
            (r) =>
              r.lock_id === result.lock_id ||
              r.scope_id === this.unlockForm.scope_id
          );
          if (target) {
            target.is_active = false;
            target.status = "UNLOCKED";
            target.unlock_justification = payload.justification;
          }
          this.calculateStats();
          this.syncTreeStatuses();
          this.isUnlockModalOpen = false;
        }
      } catch (err) {
        console.error("Unlock operation failed:", err);
      } finally {
        this.isSubmitting = false;
      }
    },
  },
};
</script>

<style scoped>
.dashboard-section {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 24px;
  gap: 16px;
}

.header-badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.badge-gxp {
  background-color: #0284c7;
  color: #ffffff;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
}

.badge-regulatory {
  background-color: #0f172a;
  color: #38bdf8;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.badge-standard {
  background-color: #e2e8f0;
  color: #334155;
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.75rem;
}

.view-title {
  font-size: 1.5rem;
  font-weight: 800;
  color: #0f172a;
  margin: 0 0 4px 0;
}

.view-subtitle {
  color: #64748b;
  font-size: 0.9rem;
  max-width: 900px;
  line-height: 1.4;
  margin: 0;
}

/* Stats Cards */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.stat-card {
  background: #ffffff;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  align-items: center;
  gap: 16px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.stat-icon {
  font-size: 2rem;
  width: 48px;
  height: 48px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  background-color: #f1f5f9;
}

.stat-value {
  font-size: 1.5rem;
  font-weight: 800;
  color: #0f172a;
  line-height: 1.2;
}

.stat-label {
  font-size: 0.8rem;
  color: #64748b;
  font-weight: 500;
}

/* Split Workspace */
.datalock-workspace-grid {
  display: grid;
  grid-template-columns: 440px 1fr;
  gap: 20px;
  align-items: start;
}

.card {
  background: #ffffff;
  border: 1px solid var(--border);
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  overflow: hidden;
}

.card-header-flex {
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: #f8fafc;
}

.card-title {
  font-size: 1.05rem;
  font-weight: 700;
  color: #0f172a;
  margin: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}

.tree-container {
  padding: 16px;
  max-height: 680px;
  overflow-y: auto;
}

.tree-node {
  margin-bottom: 4px;
}

.tree-children {
  padding-left: 20px;
  border-left: 1px dashed #cbd5e1;
  margin-left: 10px;
  margin-top: 4px;
}

.node-row {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  border-radius: 6px;
  cursor: pointer;
  transition: background-color 0.15s ease;
}

.node-row:hover {
  background-color: #f1f5f9;
}

.node-row.selected {
  background-color: #e0f2fe;
  border-left: 3px solid #0284c7;
}

.expand-btn {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 0.75rem;
  color: #64748b;
  width: 18px;
  height: 18px;
  display: flex;
  align-items: center;
  justify-content: center;
}

.node-name {
  font-size: 0.88rem;
  font-weight: 600;
  color: #1e293b;
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.node-badge {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-locked {
  background-color: #fee2e2;
  color: #991b1b;
}

.badge-frozen {
  background-color: #e0f2fe;
  color: #0369a1;
}

.badge-unlocked {
  background-color: #dcfce7;
  color: #166534;
}

.node-actions {
  display: flex;
  gap: 4px;
}

.btn-mini {
  background: none;
  border: 1px solid var(--border);
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.8rem;
  padding: 2px 6px;
  background-color: #ffffff;
  transition: transform 0.1s;
}

.btn-mini:hover {
  transform: scale(1.1);
}

/* Inspection Pane */
.detail-inspection-card {
  min-height: 520px;
}

.empty-selection-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 60px 24px;
  text-align: center;
  color: #64748b;
}

.empty-icon {
  font-size: 2.5rem;
  margin-bottom: 12px;
}

.inspection-body {
  padding: 20px;
}

.scope-summary-banner {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px;
  border-radius: 8px;
  margin-bottom: 20px;
  border: 1px solid var(--border);
}

.scope-summary-banner.locked,
.scope-summary-banner.hard_lock {
  background-color: #fef2f2;
  border-color: #fca5a5;
}

.scope-summary-banner.frozen {
  background-color: #f0f9ff;
  border-color: #bae6fd;
}

.scope-summary-banner.unlocked {
  background-color: #f0fdf4;
  border-color: #bbf7d0;
}

.banner-status-icon {
  font-size: 2.2rem;
}

.banner-text h4 {
  margin: 0 0 4px 0;
  font-size: 1.15rem;
  color: #0f172a;
}

.node-id-sub {
  margin: 0 0 4px 0;
  font-size: 0.85rem;
  color: #64748b;
}

.node-status-text {
  margin: 0;
  font-size: 0.9rem;
  color: #334155;
}

.info-group {
  margin-bottom: 20px;
}

.info-group-title {
  font-size: 0.85rem;
  font-weight: 700;
  text-transform: uppercase;
  color: #64748b;
  letter-spacing: 0.04em;
  margin-bottom: 8px;
}

.inheritance-breadcrumbs {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  background-color: #f8fafc;
  padding: 10px 14px;
  border-radius: 6px;
  border: 1px solid var(--border);
  font-size: 0.85rem;
}

.crumb-arrow {
  color: #94a3b8;
}

.action-btn-row {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
  margin-bottom: 24px;
}

/* Modals */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  backdrop-filter: blur(2px);
}

.modal-card {
  background: #ffffff;
  border-radius: 10px;
  width: 90%;
  max-width: 580px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2);
  overflow: hidden;
}

.modal-header {
  padding: 16px 20px;
  background: #0f172a;
  color: #ffffff;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
}

.modal-close-btn {
  background: none;
  border: none;
  color: #94a3b8;
  font-size: 1.5rem;
  cursor: pointer;
}

.modal-body {
  padding: 20px;
}

.alert-warning-banner {
  display: flex;
  gap: 12px;
  background-color: #fffbeb;
  border: 1px solid #fde68a;
  color: #92400e;
  padding: 12px;
  border-radius: 6px;
  margin-bottom: 16px;
  font-size: 0.85rem;
}

.modal-footer {
  padding: 16px 20px;
  background: #f8fafc;
  border-top: 1px solid var(--border);
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}

.form-group {
  margin-bottom: 16px;
}

.form-label {
  display: block;
  font-size: 0.85rem;
  font-weight: 600;
  color: #334155;
  margin-bottom: 6px;
}

.form-input,
.form-select,
.form-textarea {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-family: inherit;
  font-size: 0.9rem;
}

.form-textarea {
  resize: vertical;
}

.input-valid {
  border-color: #22c55e;
  background-color: #f0fdf4;
}

.input-invalid {
  border-color: #ef4444;
  background-color: #fef2f2;
}

.char-counter-row {
  display: flex;
  justify-content: space-between;
  margin-top: 4px;
  font-size: 0.8rem;
}

.counter-met {
  color: #16a34a;
  font-weight: 700;
}

.counter-unmet {
  color: #dc2626;
  font-weight: 700;
}

.counter-status.valid {
  color: #16a34a;
}

.counter-status.invalid {
  color: #dc2626;
}

.radio-row {
  display: flex;
  gap: 16px;
}

.radio-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.88rem;
  cursor: pointer;
}

.btn {
  padding: 8px 16px;
  border-radius: 6px;
  font-weight: 600;
  font-size: 0.88rem;
  cursor: pointer;
  border: none;
  transition: background-color 0.15s ease;
}

.btn-primary {
  background-color: #0284c7;
  color: #ffffff;
}

.btn-primary:hover:not(:disabled) {
  background-color: #0369a1;
}

.btn-secondary {
  background-color: #e2e8f0;
  color: #1e293b;
}

.btn-secondary:hover:not(:disabled) {
  background-color: #cbd5e1;
}

.btn-danger {
  background-color: #dc2626;
  color: #ffffff;
}

.btn-danger:hover:not(:disabled) {
  background-color: #b91c1c;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
}

.table th,
.table td {
  padding: 8px 12px;
  border-bottom: 1px solid var(--border);
  text-align: left;
}

.table th {
  background-color: #f8fafc;
  font-weight: 600;
  color: #475569;
}

.text-truncate-cell {
  max-width: 200px;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
</style>
