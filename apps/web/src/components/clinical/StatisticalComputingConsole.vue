<template>
  <div
    class="sce-console"
    role="region"
    aria-label="Validated Containerized Statistical Computing Environment"
  >
    <!-- Header -->
    <header class="sce-header">
      <div class="header-main">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-reproducible"
            >100% Bit-for-Bit Reproducible</span
          >
          <span class="badge badge-sandbox"
            >Air-Gapped Sandbox (R &amp; Python)</span
          >
        </div>
        <h2 class="sce-title">
          Validated Statistical Computing Environment (SCE)
        </h2>
        <p class="sce-subtitle">
          Isolated execution workspace for biostatisticians with immutable
          package lockfile integrity (<code>uv.lock</code>,
          <code>renv.lock</code>), cryptographically signed run manifests, 21
          CFR Part 11 electronic signatures, and formal database lock
          authorization gating.
        </p>
      </div>

      <div class="header-actions">
        <div
          class="blinding-status-pill"
          :class="{ 'unblinded-active': isUnblindedMode }"
        >
          <span class="status-icon">{{ isUnblindedMode ? "🔓" : "🔒" }}</span>
          <div class="status-info">
            <span class="status-label">{{
              isUnblindedMode
                ? "Unblinded Analysis Active"
                : "Blinded Analysis (Masked)"
            }}</span>
            <span class="status-sub">{{
              isUnblindedMode
                ? activeLockTokenId || "Authorized Session"
                : "Treatment variables masked"
            }}</span>
          </div>
        </div>

        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="loadData"
        >
          <span class="icon">🔄</span> Refresh
        </button>

        <button
          type="button"
          class="btn btn-primary"
          @click="openNewProgramModal"
        >
          <span class="icon">➕</span> Author New Program
        </button>
      </div>
    </header>

    <!-- Navigation Sub-Tabs -->
    <nav
      class="sce-nav-tabs"
      role="tablist"
      aria-label="Statistical Computing Tabs"
    >
      <button
        role="tab"
        :aria-selected="activeTab === 'editor'"
        :class="['tab-btn', { active: activeTab === 'editor' }]"
        @click="activeTab = 'editor'"
      >
        💻 Statistical Workspace &amp; Runner
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'runs'"
        :class="['tab-btn', { active: activeTab === 'runs' }]"
        @click="activeTab = 'runs'"
      >
        📜 Execution Runs &amp; Signed Manifests ({{ runs.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'lockfiles'"
        :class="['tab-btn', { active: activeTab === 'lockfiles' }]"
        @click="activeTab = 'lockfiles'"
      >
        📦 Package Lockfiles (uv.lock / renv.lock)
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'tokens'"
        :class="['tab-btn', { active: activeTab === 'tokens' }]"
        @click="activeTab = 'tokens'"
      >
        🔑 Database Lock Authorization Tokens ({{ lockTokens.length }})
      </button>
    </nav>

    <!-- TAB 1: Editor & Sandbox Runner -->
    <div v-if="activeTab === 'editor'" class="tab-panel">
      <div class="workspace-grid">
        <!-- Left Panel: Program Selection & Editor -->
        <div class="card editor-card">
          <div class="card-header">
            <div class="header-left">
              <h3 class="card-title">Analysis Program Authoring</h3>
              <span class="version-tag"
                >Version: {{ selectedProgram?.version || "1.0.0" }}</span
              >
            </div>
            <div class="header-right">
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <select
                v-model="selectedProgramId"
                class="form-select program-selector"
                @change="onProgramSelected"
              >
                <option value="">-- Select or Template Program --</option>
                <option
                  v-for="prog in programs"
                  :key="prog.program_id"
                  :value="prog.program_id"
                >
                  [{{ prog.language }}] {{ prog.name }} - {{ prog.title }}
                </option>
              </select>
            </div>
          </div>

          <div class="editor-controls-bar">
            <div class="lang-switch-group">
              <button
                type="button"
                :class="['lang-btn', { active: currentLanguage === 'PYTHON' }]"
                @click="switchLanguage('PYTHON')"
              >
                🐍 Python 3.14
              </button>
              <button
                type="button"
                :class="['lang-btn', { active: currentLanguage === 'R' }]"
                @click="switchLanguage('R')"
              >
                📊 R 4.4.2
              </button>
            </div>

            <div class="template-selector">
              <span class="template-label">Load Template:</span>
              <button
                type="button"
                class="btn btn-xs btn-outline"
                @click="loadTemplate('demographics')"
              >
                Demographics (T-14.1)
              </button>
              <button
                type="button"
                class="btn btn-xs btn-outline"
                @click="loadTemplate('adverse_events')"
              >
                Adverse Events
              </button>
              <button
                type="button"
                class="btn btn-xs btn-outline"
                @click="loadTemplate('vitals_summary')"
              >
                Vital Signs Summary
              </button>
            </div>
          </div>

          <div class="meta-inputs-grid">
            <div class="form-group">
              <label class="form-label" for="prog-name">Script Filename</label>
              <input
                id="prog-name"
                v-model="scriptName"
                type="text"
                class="form-input"
                placeholder="t_14_1_demog.py"
              />
            </div>
            <div class="form-group">
              <label class="form-label" for="prog-title">Program Title</label>
              <input
                id="prog-title"
                v-model="scriptTitle"
                type="text"
                class="form-input"
                placeholder="Summary of Demographic Characteristics"
              />
            </div>
            <div class="form-group">
              <label class="form-label" for="prog-domain"
                >Target CDISC Domain</label
              >
              <input
                id="prog-domain"
                v-model="targetDomain"
                type="text"
                class="form-input"
                placeholder="ADSL / TLF-14.1"
              />
            </div>
          </div>

          <div class="code-editor-wrapper">
            <div class="editor-toolbar">
              <span class="editor-filename"
                >📝 {{ scriptName || "untitled_analysis.py" }}</span
              >
              <span v-if="computedCodeHash" class="code-digest-preview"
                >SHA-256:
                <code>{{ computedCodeHash.slice(0, 16) }}...</code></span
              >
            </div>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <textarea
              v-model="sourceCode"
              class="code-textarea"
              placeholder="# Author validated statistical analysis code here..."
              rows="18"
              spellcheck="false"
            />
          </div>

          <div class="card-footer actions-footer">
            <button
              type="button"
              class="btn btn-secondary"
              :disabled="isSaving"
              @click="saveProgramDraft"
            >
              💾 Save Program Definition
            </button>
            <span class="text-muted text-xs"
              >Immutable SHA-256 seal will be signed on execution dispatch</span
            >
          </div>
        </div>

        <!-- Right Panel: Sandbox Execution Launcher & Status -->
        <div class="card runner-card">
          <div class="card-header">
            <h3 class="card-title">Sandbox Execution Controls</h3>
            <span class="badge badge-sandbox">Air-Gapped Container</span>
          </div>

          <div class="runner-body">
            <!-- 1. Input Snapshot Selection -->
            <div class="form-group">
              <label class="form-label" for="exec-snap"
                >Input Version-Pinned Snapshot</label
              >
              <select
                id="exec-snap"
                v-model="selectedSnapshotId"
                class="form-select"
              >
                <option value="">-- Choose Sealed Snapshot --</option>
                <option
                  v-for="snap in snapshots"
                  :key="snap.snapshot_id"
                  :value="snap.snapshot_id"
                >
                  {{ snap.version_tag }} ({{ snap.milestone }}) -
                  {{ snap.record_count }} records
                </option>
              </select>
              <span v-if="selectedSnapshot" class="field-hint">
                Digest:
                <code
                  >{{ selectedSnapshot.sha256_digest.slice(0, 20) }}...</code
                >
              </span>
            </div>

            <!-- 2. Blinding & Database Lock Authorization -->
            <div
              class="unblinded-gating-box"
              :class="{ 'gating-active': requestUnblinded }"
            >
              <div class="toggle-row">
                <label class="checkbox-label" for="unblind-req-checkbox">
                  <input
                    id="unblind-req-checkbox"
                    v-model="requestUnblinded"
                    type="checkbox"
                    class="form-checkbox"
                  />
                  <span class="checkbox-text"
                    >Request Unblinded Treatment Arms Access</span
                  >
                </label>
                <span
                  class="badge"
                  :class="requestUnblinded ? 'badge-warn' : 'badge-neutral'"
                >
                  {{ requestUnblinded ? "UNBLINDED" : "BLINDED" }}
                </span>
              </div>

              <div v-if="requestUnblinded" class="token-selection-group">
                <label class="form-label" for="db-token-select"
                  >Database Lock Authorization Token (Required)</label
                >
                <select
                  id="db-token-select"
                  v-model="selectedTokenId"
                  class="form-select"
                >
                  <option value="">
                    -- Select Valid Authorization Token --
                  </option>
                  <option
                    v-for="tok in validTokens"
                    :key="tok.token_id"
                    :value="tok.token_id"
                  >
                    {{ tok.token_id }} ({{ tok.milestone }}) - Issued to:
                    {{ tok.issued_to }}
                  </option>
                </select>
                <p class="token-warning">
                  ⚠️ 21 CFR Part 11 Rule: Unblinded analysis requires active
                  database lock token authorization with cryptographic
                  signature.
                </p>
              </div>
            </div>

            <!-- 3. Resource Bounds & Configuration -->
            <div class="bounds-box">
              <h4 class="bounds-title">Sandbox Bounded Limits</h4>
              <div class="bounds-grid">
                <div class="bound-item">
                  <span class="bound-name">Timeout Limit:</span>
                  <span class="bound-val">{{ timeoutSeconds }} seconds</span>
                </div>
                <div class="bound-item">
                  <span class="bound-name">Memory Ceiling:</span>
                  <span class="bound-val">{{ memoryMb }} MB RAM</span>
                </div>
                <div class="bound-item">
                  <span class="bound-name">Network Egress:</span>
                  <span class="bound-val badge-green"
                    >BLOCKED (Air-Gapped)</span
                  >
                </div>
                <div class="bound-item">
                  <span class="bound-name">Filesystem:</span>
                  <span class="bound-val">Ephemeral Workspace</span>
                </div>
              </div>
            </div>

            <!-- 4. Execution Dispatch Button -->
            <div class="dispatch-group">
              <button
                type="button"
                class="btn btn-primary btn-lg btn-block"
                :disabled="isExecuting || !canExecute"
                @click="dispatchExecution"
              >
                <span v-if="isExecuting" class="spinner"
                  >⏳ Executing Sandbox Run...</span
                >
                <span v-else>🚀 Run Statistical Analysis in Sandbox</span>
              </button>
            </div>

            <!-- 5. Latest Execution Summary (if available) -->
            <div
              v-if="latestRun"
              class="latest-run-card"
              :class="`status-${latestRun.status.toLowerCase()}`"
            >
              <div class="run-header-row">
                <span class="run-id-tag"
                  >Run: <code>{{ latestRun.run_id }}</code></span
                >
                <span
                  class="status-pill"
                  :class="`pill-${latestRun.status.toLowerCase()}`"
                >
                  {{ latestRun.status }} ({{ latestRun.duration_ms }}ms)
                </span>
              </div>

              <div class="manifest-seal-row">
                <span class="seal-label">SHA-256 Manifest Seal:</span>
                <code class="seal-code">{{
                  latestRun.manifest_sha256_seal
                }}</code>
              </div>

              <div class="artifacts-summary">
                <span class="art-count"
                  >Generated Output Artifacts:
                  {{ latestRun.output_artifacts.length }}</span
                >
                <button
                  type="button"
                  class="btn btn-xs btn-outline"
                  @click="viewRunDetails(latestRun.run_id)"
                >
                  View Full Manifest &amp; Artifacts →
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 2: Historical Runs & Signed Manifests -->
    <div v-if="activeTab === 'runs'" class="tab-panel">
      <div class="card table-card">
        <div class="card-header">
          <h3 class="card-title">21 CFR Part 11 Execution Run Manifests</h3>
          <span class="total-badge">Total Sealed Runs: {{ runs.length }}</span>
        </div>

        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Run ID</th>
                <th>Program</th>
                <th>Lang</th>
                <th>Snapshot Version</th>
                <th>Blinding Mode</th>
                <th>Status</th>
                <th>Duration</th>
                <th>Artifacts</th>
                <th>Manifest Seal</th>
                <th>Executed At</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="run in runs"
                :key="run.run_id"
                :class="{
                  'selected-row': selectedRunDetail?.run_id === run.run_id,
                }"
              >
                <td class="cell-id">
                  <code>{{ run.run_id }}</code>
                </td>
                <td class="cell-bold">
                  {{ run.program_name }} (v{{ run.program_version }})
                </td>
                <td>
                  <span
                    class="lang-tag"
                    :class="`lang-${run.language.toLowerCase()}`"
                    >{{ run.language }}</span
                  >
                </td>
                <td>
                  <span class="version-pill">{{
                    run.snapshot_version_tag
                  }}</span>
                </td>
                <td>
                  <span v-if="run.is_unblinded_run" class="badge-unblind-tag"
                    >🔓 UNBLINDED</span
                  >
                  <span v-else class="badge-blinded-tag">BLINDED</span>
                </td>
                <td>
                  <span
                    class="status-pill"
                    :class="`pill-${run.status.toLowerCase()}`"
                    >{{ run.status }}</span
                  >
                </td>
                <td class="cell-num">{{ run.duration_ms }} ms</td>
                <td class="cell-num">
                  {{ run.output_artifacts.length }} items
                </td>
                <td class="cell-digest">
                  <span class="digest-pill" :title="run.manifest_sha256_seal">
                    🛡️ {{ run.manifest_sha256_seal.slice(0, 14) }}...
                  </span>
                </td>
                <td class="cell-date">
                  {{ formatDate(run.execution_end) }}
                </td>
                <td>
                  <div class="action-buttons">
                    <button
                      type="button"
                      class="btn btn-xs btn-outline"
                      @click="viewRunDetails(run.run_id)"
                    >
                      Inspect
                    </button>
                    <button
                      type="button"
                      class="btn btn-xs btn-primary"
                      @click="verifyManifest(run.run_id)"
                    >
                      Verify Seal
                    </button>
                  </div>
                </td>
              </tr>
              <tr v-if="runs.length === 0">
                <td colspan="11" class="text-center text-muted empty-cell">
                  No statistical runs executed yet. Launch a program from the
                  Statistical Workspace.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Detailed Run Manifest Inspector Modal / Drawer -->
      <div v-if="selectedRunDetail" class="detail-drawer card">
        <div class="card-header">
          <div class="header-left">
            <h3 class="card-title">
              Execution Run Manifest Inspector:
              <code>{{ selectedRunDetail.run_id }}</code>
            </h3>
            <span
              class="status-pill"
              :class="`pill-${selectedRunDetail.status.toLowerCase()}`"
              >{{ selectedRunDetail.status }}</span
            >
          </div>
          <ClinicalIconButton
            class="btn-close"
            label="Close execution run manifest inspector"
            @click="selectedRunDetail = null"
          >
            &times;
          </ClinicalIconButton>
        </div>

        <div class="drawer-content">
          <!-- Manifest Provenance Grid -->
          <div class="provenance-grid">
            <div class="prov-card">
              <span class="prov-label">Script Code Digest (SHA-256):</span>
              <code>{{ selectedRunDetail.script_code_digest }}</code>
            </div>
            <div class="prov-card">
              <span class="prov-label">Data Snapshot Digest (SHA-256):</span>
              <code>{{ selectedRunDetail.snapshot_digest }}</code>
            </div>
            <div class="prov-card">
              <span class="prov-label">Package Lockfile Digest (SHA-256):</span>
              <code>{{ selectedRunDetail.lockfile_digest }}</code>
            </div>
            <div class="prov-card">
              <span class="prov-label">Container Base Digest:</span>
              <code>{{ selectedRunDetail.container_image_digest }}</code>
            </div>
            <div class="prov-card">
              <span class="prov-label">Combined Output Artifacts Digest:</span>
              <code>{{ selectedRunDetail.output_artifacts_digest }}</code>
            </div>
            <div class="prov-card">
              <span class="prov-label">Root Manifest Seal (SHA-256):</span>
              <code class="text-bold text-success">{{
                selectedRunDetail.manifest_sha256_seal
              }}</code>
            </div>
          </div>

          <!-- Electronic Signature Block -->
          <div class="sig-block-card">
            <h4 class="sig-title">
              ✍️ 21 CFR Part 11 Electronic Signature Block
            </h4>
            <div class="sig-grid">
              <div class="sig-item">
                <span class="sig-lbl">Signer ID:</span>
                <span class="sig-val">{{
                  selectedRunDetail.signature_block.signer_id
                }}</span>
              </div>
              <div class="sig-item">
                <span class="sig-lbl">Regulatory Role:</span>
                <span class="sig-val">{{
                  selectedRunDetail.signature_block.signer_role
                }}</span>
              </div>
              <div class="sig-item">
                <span class="sig-lbl">Signed At (UTC):</span>
                <span class="sig-val">{{
                  formatDate(selectedRunDetail.signature_block.signed_at)
                }}</span>
              </div>
              <div class="sig-item full-width">
                <span class="sig-lbl">Signature Intent / Meaning:</span>
                <span class="sig-val">{{
                  selectedRunDetail.signature_block.signature_meaning
                }}</span>
              </div>
              <div class="sig-item full-width">
                <span class="sig-lbl">HMAC Cryptographic Signature:</span>
                <code class="sig-hmac">{{
                  selectedRunDetail.signature_block.hmac_signature
                }}</code>
              </div>
            </div>
          </div>

          <!-- Generated Output Artifacts -->
          <div class="artifacts-section">
            <h4 class="section-title">
              📊 Output Artifacts (Tables, Listings, Figures)
            </h4>
            <div
              v-if="selectedRunDetail.output_artifacts.length > 0"
              class="artifacts-list"
            >
              <div
                v-for="art in selectedRunDetail.output_artifacts"
                :key="art.artifact_id"
                class="artifact-card"
              >
                <div class="art-header">
                  <span class="art-icon">{{
                    getArtifactIcon(art.artifact_type)
                  }}</span>
                  <div class="art-meta">
                    <span class="art-name">{{ art.name }}</span>
                    <span class="art-sub"
                      >{{ art.artifact_type }} • {{ art.size_bytes }} bytes •
                      {{ art.mime_type }}</span
                    >
                  </div>
                  <span class="art-digest-pill" :title="art.sha256_digest">
                    SHA-256: {{ art.sha256_digest.slice(0, 16) }}...
                  </span>
                </div>
                <div v-if="art.content_preview" class="art-preview-box">
                  <pre class="preview-text">{{ art.content_preview }}</pre>
                </div>
              </div>
            </div>
            <div v-else class="text-muted text-xs">
              No output artifacts produced by this run.
            </div>
          </div>

          <!-- Captured Stdout & Stderr -->
          <div class="logs-section">
            <h4 class="section-title">
              🖥️ Execution Console Output (Stdout / Stderr)
            </h4>
            <div class="console-box">
              <div class="console-header-bar">
                STDOUT / STDERR (Sandbox Log)
              </div>
              <pre class="console-text">{{
                selectedRunDetail.stdout_capture ||
                selectedRunDetail.stderr_capture ||
                "[No log output generated]"
              }}</pre>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 3: Package Lockfiles -->
    <div v-if="activeTab === 'lockfiles'" class="tab-panel">
      <div class="card lockfile-card">
        <div class="card-header">
          <h3 class="card-title">
            Package Lockfile Integrity &amp; GxP Qualification Engine
          </h3>
          <span class="badge badge-reproducible">Strict Version Pinning</span>
        </div>

        <div class="lockfile-body">
          <p class="section-desc">
            Enforces immutable package lockfiles (<code>renv.lock</code> for R
            and <code>uv.lock</code> for Python), verifying cryptographic
            wheel/source digests and qualifying biostatistical packages for 100%
            reproducible regulatory submissions.
          </p>

          <div class="lock-test-grid">
            <div class="form-group">
              <label class="form-label" for="lock-format-select"
                >Lockfile Format</label
              >
              <select
                id="lock-format-select"
                v-model="testLockType"
                class="form-select"
              >
                <option value="UV_LOCK">uv.lock (Python TOML)</option>
                <option value="RENV_LOCK">renv.lock (R JSON)</option>
                <option value="REQUIREMENTS_TXT">
                  requirements.txt (Hashes)
                </option>
              </select>
            </div>

            <div class="form-group">
              <label class="form-label" for="lock-content-area"
                >Lockfile Content</label
              >
              <textarea
                id="lock-content-area"
                v-model="testLockContent"
                class="code-textarea lock-textarea"
                rows="10"
                placeholder="Paste lockfile content to assert cryptographic integrity..."
              />
            </div>

            <div class="lock-actions-row">
              <button
                type="button"
                class="btn btn-primary"
                :disabled="isValidatingLock"
                @click="assertLockfileIntegrity"
              >
                🛡️ Validate Lockfile Integrity
              </button>
              <button
                type="button"
                class="btn btn-secondary"
                @click="loadSampleLockfile"
              >
                Load Sample Qualified Lockfile
              </button>
            </div>

            <!-- Lockfile Validation Result Box -->
            <div
              v-if="lockValidationResult"
              class="validation-result-box"
              :class="`res-${lockValidationResult.status.toLowerCase()}`"
            >
              <div class="res-header">
                <span class="res-status-pill">{{
                  lockValidationResult.status
                }}</span>
                <span class="res-count"
                  >{{ lockValidationResult.package_count }} Pinned
                  Packages</span
                >
              </div>
              <div class="res-digest">
                <span class="digest-lbl">Canonical Lockfile Digest:</span>
                <code>{{ lockValidationResult.lock_digest }}</code>
              </div>
              <p class="res-details">
                {{ lockValidationResult.details }}
              </p>

              <div
                v-if="lockValidationResult.packages.length > 0"
                class="package-list-grid"
              >
                <div
                  v-for="pkg in lockValidationResult.packages"
                  :key="pkg.name"
                  class="pkg-card"
                >
                  <div class="pkg-header">
                    <span class="pkg-name">{{ pkg.name }}</span>
                    <span class="pkg-ver">v{{ pkg.version }}</span>
                  </div>
                  <div class="pkg-footer">
                    <span class="pkg-repo">{{
                      pkg.repository || "Standard"
                    }}</span>
                    <span
                      class="pkg-qual"
                      :class="pkg.is_qualified ? 'qual-yes' : 'qual-no'"
                    >
                      {{ pkg.is_qualified ? "✅ GxP Qualified" : "⚠️ Custom" }}
                    </span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- TAB 4: Database Lock Authorization Tokens -->
    <div v-if="activeTab === 'tokens'" class="tab-panel">
      <div class="card table-card">
        <div class="card-header">
          <div class="header-left">
            <h3 class="card-title">Database Lock Authorization Tokens</h3>
            <p class="card-sub">
              Formal cryptographic authorization tokens gating unblinded
              statistical analysis.
            </p>
          </div>
          <button
            type="button"
            class="btn btn-primary"
            @click="openIssueTokenModal"
          >
            🔑 Issue New Authorization Token
          </button>
        </div>

        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Token ID</th>
                <th>Lock Reference</th>
                <th>Milestone</th>
                <th>Issued To</th>
                <th>Permitted Roles</th>
                <th>Issued At</th>
                <th>Expires At</th>
                <th>Status</th>
                <th>Cryptographic Signature</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="tok in lockTokens" :key="tok.token_id">
                <td class="cell-id">
                  <code>{{ tok.token_id }}</code>
                </td>
                <td>
                  <span class="lock-ref">🔒 {{ tok.lock_id }}</span>
                </td>
                <td>
                  <span class="milestone-badge">{{ tok.milestone }}</span>
                </td>
                <td class="cell-bold">
                  {{ tok.issued_to }}
                </td>
                <td>
                  <span class="roles-pill">{{
                    tok.authorized_roles.join(", ")
                  }}</span>
                </td>
                <td class="cell-date">
                  {{ formatDate(tok.issued_at) }}
                </td>
                <td class="cell-date">
                  {{ formatDate(tok.expires_at) }}
                </td>
                <td>
                  <span v-if="tok.is_revoked" class="badge-revoked"
                    >REVOKED</span
                  >
                  <span
                    v-else-if="isTokenExpired(tok.expires_at)"
                    class="badge-expired"
                    >EXPIRED</span
                  >
                  <span v-else class="badge-active">ACTIVE</span>
                </td>
                <td class="cell-digest">
                  <span class="digest-pill" :title="tok.signature_hash">
                    🔑 {{ tok.signature_hash.slice(0, 14) }}...
                  </span>
                </td>
                <td>
                  <button
                    v-if="!tok.is_revoked && !isTokenExpired(tok.expires_at)"
                    type="button"
                    class="btn btn-xs btn-outline btn-danger"
                    @click="revokeToken(tok.token_id)"
                  >
                    Revoke
                  </button>
                  <span v-else class="text-muted">—</span>
                </td>
              </tr>
              <tr v-if="lockTokens.length === 0">
                <td colspan="10" class="text-center text-muted empty-cell">
                  No database lock authorization tokens issued yet.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Issue Token Modal -->
    <ClinicalModal
      v-model="showIssueTokenModal"
      :teleport="false"
      dialog-class="modal-dialog card"
      title="Issue Database Lock Authorization Token"
    >
      <div class="form-group">
        <label class="form-label" for="new-lock-id"
          >Database Lock Reference ID</label
        >
        <input
          id="new-lock-id"
          v-model="newTokenLockId"
          type="text"
          class="form-input"
          placeholder="LOCK-STUDY-901-FINAL"
        />
      </div>
      <div class="form-group">
        <label class="form-label" for="new-lock-milestone"
          >Milestone Tier</label
        >
        <select
          id="new-lock-milestone"
          v-model="newTokenMilestone"
          class="form-select"
        >
          <option value="DATABASE_LOCK">DATABASE_LOCK</option>
          <option value="INTERIM_ANALYSIS_1">INTERIM_ANALYSIS_1</option>
          <option value="INTERIM_ANALYSIS_2">INTERIM_ANALYSIS_2</option>
          <option value="SAFETY_REVIEW">SAFETY_REVIEW</option>
          <option value="REGULATORY_SUBMISSION">REGULATORY_SUBMISSION</option>
        </select>
      </div>
      <div class="form-group">
        <label class="form-label" for="new-token-user"
          >Issued To (User ID)</label
        >
        <input
          id="new-token-user"
          v-model="newTokenIssuedTo"
          type="text"
          class="form-input"
          placeholder="lead_unblinded_statistician"
        />
      </div>
      <div class="form-group">
        <label class="form-label" for="new-token-hours"
          >Validity Period (Hours)</label
        >
        <input
          id="new-token-hours"
          v-model.number="newTokenHours"
          type="number"
          class="form-input"
          min="1"
          max="720"
        />
      </div>
      <div class="form-group">
        <label class="form-label" for="new-token-reason"
          >Regulatory Justification (21 CFR Part 11)</label
        >
        <textarea
          id="new-token-reason"
          v-model="newTokenReason"
          class="form-input"
          rows="3"
          placeholder="Formally authorized by IDMC Charter for Interim Analysis 1 Unblinded Run"
        />
      </div>

      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          @click="showIssueTokenModal = false"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="!newTokenLockId || !newTokenIssuedTo || !newTokenReason"
          @click="confirmIssueToken"
        >
          Issue &amp; Cryptographically Sign Token
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup lang="ts">
import { ClinicalIconButton, ClinicalModal } from "ui";
import { ref, computed, onMounted } from "vue";
import { statisticalComputingService } from "../../api/statisticalComputing";
import { apiClient } from "../../api/apiClient";
import { useConfirmDialog } from "../../composables/useConfirmDialog.js";
import { useErrorPresenter } from "../../composables/useErrorPresenter";
import { useToastStore } from "../../stores/toasts.js";

const { confirmDialog } = useConfirmDialog();
const { presentError } = useErrorPresenter();
const toastStore = useToastStore();

const props = defineProps({
  studyId: {
    type: String,
    default: "STUDY-901",
  },
  initialUnblinded: {
    type: Boolean,
    default: false,
  },
});

// Navigation
const activeTab = ref("editor");
const isLoading = ref(false);
const isSaving = ref(false);
const isExecuting = ref(false);
const isValidatingLock = ref(false);

// State
const programs = ref<any[]>([]);
const snapshots = ref<any[]>([]);
const runs = ref<any[]>([]);
const lockTokens = ref<any[]>([]);
const selectedProgramId = ref("");
const selectedSnapshotId = ref("");
const selectedTokenId = ref("");
const selectedRunDetail = ref<any>(null);
const latestRun = ref<any>(null);
const lockValidationResult = ref<any>(null);

// Editor Form State
const currentLanguage = ref("PYTHON");
const scriptName = ref("t_14_1_demographics.py");
const scriptTitle = ref("Summary of Demographic Characteristics (Table 14.1)");
const targetDomain = ref("ADSL");
const sourceCode = ref("");
const timeoutSeconds = ref(300);
const memoryMb = ref(2048);
const requestUnblinded = ref(props.initialUnblinded);

// Issue Token Modal
const showIssueTokenModal = ref(false);
const newTokenLockId = ref("LOCK-STUDY-901-FINAL");
const newTokenMilestone = ref("DATABASE_LOCK");
const newTokenIssuedTo = ref("lead_unblinded_statistician");
const newTokenHours = ref(24);
const newTokenReason = ref(
  "Authorized for interim unblinded efficacy evaluation per protocol SOP."
);

// Lockfile Testing
const testLockType = ref("UV_LOCK");
const testLockContent = ref("");

// Computed
const selectedProgram = computed(() =>
  programs.value.find((p) => p.program_id === selectedProgramId.value)
);

const selectedSnapshot = computed(() =>
  snapshots.value.find((s) => s.snapshot_id === selectedSnapshotId.value)
);

const validTokens = computed(() =>
  lockTokens.value.filter((t) => !t.is_revoked && !isTokenExpired(t.expires_at))
);

const isUnblindedMode = computed(
  () => requestUnblinded.value && !!selectedTokenId.value
);

const activeLockTokenId = computed(() => selectedTokenId.value);

const canExecute = computed(() => {
  if (!selectedSnapshotId.value || !sourceCode.value) return false;
  if (requestUnblinded.value && !selectedTokenId.value) return false;
  return true;
});

const computedCodeHash = computed(() => {
  if (!sourceCode.value) return "";
  // Fallback hash representation for UI preview
  return "SHA256-PENDING-DISPATCH";
});

// Templates
const pythonTemplates: Record<string, string> = {
  demographics: `"""
Summary of Demographic Characteristics (Table 14.1)
Analysis Data Model: ADSL / Pooled Baseline Records
"""
import pandas as pd
import numpy as np

# Load harmonized clinical records
df = pd.read_csv("data_records.csv")
print(f"[SCE Python 3.14 Engine] Loaded {len(df)} observations.")

# Filter Demographics and Vitals
dm_df = df[df["domain"] == "DM"]
vs_df = df[df["domain"] == "VS"]

# Compute Summary Statistics
summary_data = {
    "METRIC": ["Total Participants", "Mean Systolic BP", "Mean Diastolic BP", "Active Records"],
    "N": [len(dm_df["subject_id"].unique()) if len(dm_df) else 0, len(vs_df), len(vs_df), len(df)],
    "VALUE": [
        len(df["subject_id"].unique()),
        vs_df[vs_df["item_code"] == "SYSBP"]["numeric_value"].mean() if len(vs_df) else 120.0,
        vs_df[vs_df["item_code"] == "DIABP"]["numeric_value"].mean() if len(vs_df) else 80.0,
        len(df),
    ]
}

res_df = pd.DataFrame(summary_data)
res_df.to_csv("t_14_1_demographics.csv", index=False)
print("Generated table: t_14_1_demographics.csv")
print("Analysis complete.")
`,
  adverse_events: `"""
Summary of Adverse Events by Preferred Term (Table 14.2)
Analysis Data Model: ADAE
"""
import pandas as pd

df = pd.read_csv("data_records.csv")
ae_df = df[df["domain"] == "AE"]

print(f"[SCE Python 3.14 Engine] Evaluating {len(ae_df)} adverse event records.")
ae_summary = ae_df.groupby("item_code").size().reset_index(name="COUNT")
ae_summary.to_csv("t_14_2_adverse_events.csv", index=False)
print("Generated table: t_14_2_adverse_events.csv")
`,
  vitals_summary: `"""
Vital Signs Baseline & Post-Baseline Summary (Table 14.3)
"""
import pandas as pd

df = pd.read_csv("data_records.csv")
vs_df = df[df["domain"] == "VS"]

stats = vs_df.groupby("item_code")["numeric_value"].agg(["count", "mean", "std", "min", "max"]).reset_index()
stats.to_csv("t_14_3_vitals_summary.csv", index=False)
print("Generated table: t_14_3_vitals_summary.csv")
`,
};

const rTemplates: Record<string, string> = {
  demographics: `# Cadence Clinical Statistical Computing Environment
# Protocol: Demographic Summary (Table 14.1)
library(dplyr)

data <- read.csv("data_records.csv")
cat(sprintf("[R 4.4.2 SCE Engine] Loaded %d observations.\\n", nrow(data)))

# Summary Table Output
summary_tbl <- data %>%
  group_by(domain) %>%
  summarise(N = n(), Distinct_Subjects = n_distinct(subject_id))

write.csv(summary_tbl, "t_14_1_demographics.csv", row.names = FALSE)
cat("Generated Table 14.1: t_14_1_demographics.csv\\n")
`,
  adverse_events: `# Adverse Event Frequencies by System Organ Class
library(dplyr)

data <- read.csv("data_records.csv")
ae_tbl <- data %>% filter(domain == "AE") %>% count(item_code, sort = TRUE)
write.csv(ae_tbl, "t_14_2_adverse_events.csv", row.names = FALSE)
cat("Generated AE Summary: t_14_2_adverse_events.csv\\n")
`,
  vitals_summary: `# Vital Signs Descriptive Statistics
library(dplyr)

data <- read.csv("data_records.csv")
vs_tbl <- data %>%
  filter(domain == "VS") %>%
  group_by(item_code) %>%
  summarise(Mean = mean(numeric_value, na.rm = TRUE), SD = sd(numeric_value, na.rm = TRUE))

write.csv(vs_tbl, "t_14_3_vitals_summary.csv", row.names = FALSE)
cat("Generated Vital Signs Summary: t_14_3_vitals_summary.csv\\n")
`,
};

// Actions
function switchLanguage(lang: string) {
  currentLanguage.value = lang;
  if (lang === "R") {
    scriptName.value = scriptName.value.replace(/\.py$/, ".R");
    if (!scriptName.value.endsWith(".R")) scriptName.value += ".R";
    sourceCode.value = rTemplates.demographics;
  } else {
    scriptName.value = scriptName.value.replace(/\.R$/, ".py");
    if (!scriptName.value.endsWith(".py")) scriptName.value += ".py";
    sourceCode.value = pythonTemplates.demographics;
  }
}

function loadTemplate(key: string) {
  if (currentLanguage.value === "R") {
    sourceCode.value = rTemplates[key] || rTemplates.demographics;
  } else {
    sourceCode.value = pythonTemplates[key] || pythonTemplates.demographics;
  }
}

function onProgramSelected() {
  const p = selectedProgram.value;
  if (p) {
    scriptName.value = p.name;
    scriptTitle.value = p.title;
    targetDomain.value = p.target_domain || "";
    currentLanguage.value = p.language;
    sourceCode.value = p.source_code;
  }
}

function openNewProgramModal() {
  selectedProgramId.value = "";
  scriptName.value = `t_14_${programs.value.length + 1}_analysis.py`;
  scriptTitle.value = "New Statistical Analysis";
  targetDomain.value = "ADSL";
  switchLanguage("PYTHON");
  activeTab.value = "editor";
}

async function saveProgramDraft() {
  isSaving.value = true;
  try {
    const payload = {
      study_id: props.studyId,
      name: scriptName.value,
      title: scriptTitle.value,
      description: `Analysis program for ${targetDomain.value}`,
      language: currentLanguage.value,
      version: "1.0.0",
      source_code: sourceCode.value,
      target_domain: targetDomain.value,
      reason_for_change: "Saved from Statistical Computing Console",
    };
    const res = await statisticalComputingService.createProgram(payload);
    await loadPrograms();
    if (res.data?.program?.program_id) {
      selectedProgramId.value = res.data.program.program_id;
    }
  } catch (err: any) {
    console.error("Failed to save program:", err);
    toastStore.push({
      tone: "error",
      title: "Program Save Failed",
      message: presentError(err, "Failed to save statistical program."),
    });
  } finally {
    isSaving.value = false;
  }
}

async function dispatchExecution() {
  if (!canExecute.value) return;
  isExecuting.value = true;

  try {
    // 1. Ensure program is registered or save first
    let progId = selectedProgramId.value;
    if (!progId) {
      const saveRes = await statisticalComputingService.createProgram({
        study_id: props.studyId,
        name: scriptName.value,
        title: scriptTitle.value,
        description: "Auto-saved program on execution",
        language: currentLanguage.value,
        version: "1.0.0",
        source_code: sourceCode.value,
        target_domain: targetDomain.value,
        reason_for_change: "Auto-saved on run execution",
      });
      progId = saveRes.data.program.program_id;
      selectedProgramId.value = progId;
      await loadPrograms();
    }

    // 2. Dispatch execution run
    const execPayload = {
      study_id: props.studyId,
      program_id: progId,
      snapshot_id: selectedSnapshotId.value,
      is_unblinded: requestUnblinded.value,
      database_lock_token_id: requestUnblinded.value
        ? selectedTokenId.value
        : null,
      sandbox_config: {
        timeout_seconds: timeoutSeconds.value,
        max_memory_mb: memoryMb.value,
        network_isolated: true,
      },
      reason_for_execution: "Dispatched from Statistical Computing Console",
    };

    const res = await statisticalComputingService.executeProgram(execPayload);
    latestRun.value = res.data.manifest;
    selectedRunDetail.value = res.data.manifest;
    await loadRuns();
  } catch (err: any) {
    console.error("Execution failed:", err);
    toastStore.push({
      tone: "error",
      title: "Execution Rejected",
      message: presentError(err, "Program execution was rejected."),
    });
  } finally {
    isExecuting.value = false;
  }
}

async function viewRunDetails(runId: string) {
  try {
    const res = await statisticalComputingService.getExecutionRun(runId);
    selectedRunDetail.value = res.data.run;
    activeTab.value = "runs";
  } catch (err) {
    console.error("Failed to load run details:", err);
  }
}

async function verifyManifest(runId: string) {
  try {
    const res = await statisticalComputingService.verifyRunManifest(runId);
    const vr = res.data.result;
    if (vr.is_valid) {
      toastStore.push({
        tone: "success",
        title: "Manifest Verified",
        message: `Seal: ${vr.stored_seal} · Signer: ${vr.signer_id} (${vr.signer_role}) · ${vr.details}`,
      });
    } else {
      toastStore.push({
        tone: "error",
        title: "Manifest Verification Failed",
        message: vr.details,
      });
    }
  } catch (err: any) {
    toastStore.push({
      tone: "error",
      title: "Verification Error",
      message: presentError(err, "Failed to verify run manifest."),
    });
  }
}

// Lockfiles
async function assertLockfileIntegrity() {
  if (!testLockContent.value) return;
  isValidatingLock.value = true;
  try {
    const res = await statisticalComputingService.validateLockfile({
      lock_type: testLockType.value,
      content: testLockContent.value,
      strict_qualification: true,
    });
    lockValidationResult.value = res.data.result;
  } catch (err: any) {
    toastStore.push({
      tone: "error",
      title: "Lockfile Validation Error",
      message: presentError(err, "Failed to validate lockfile."),
    });
  } finally {
    isValidatingLock.value = false;
  }
}

function loadSampleLockfile() {
  if (testLockType.value === "RENV_LOCK") {
    testLockContent.value = JSON.stringify(
      {
        R: { Version: "4.4.2" },
        Packages: {
          dplyr: {
            Package: "dplyr",
            Version: "1.1.4",
            Source: "CRAN",
            Hash: "9a77b8f9e1e2d3c4", // pragma: allowlist secret
          },
          rtables: {
            Package: "rtables",
            Version: "0.6.6",
            Source: "CRAN",
            Hash: "c4d3e2f1a8b7c6d5", // pragma: allowlist secret
          },
          survival: {
            Package: "survival",
            Version: "3.7-0",
            Source: "CRAN",
            Hash: "1122334455667788",
          },
        },
      },
      null,
      2
    );
  } else {
    testLockContent.value = `version = 1\nrequires-python = ">=3.14"\n\n[[package]]\nname = "pandas"\nversion = "2.2.0"\nwheels = [{ hash = "sha256:7f8a9b0c1d2e3f4a5b6c" }]\n\n[[package]]\nname = "numpy"\nversion = "1.26.4"\nwheels = [{ hash = "sha256:1a2b3c4d5e6f7a8b9c0d" }]\n\n[[package]]\nname = "scipy"\nversion = "1.12.0"\nwheels = [{ hash = "sha256:bb88cc99dd00ee11ff22" }]\n`;
  }
}

// Database Lock Tokens
function openIssueTokenModal() {
  showIssueTokenModal.value = true;
}

async function confirmIssueToken() {
  try {
    const payload = {
      study_id: props.studyId,
      lock_id: newTokenLockId.value,
      milestone: newTokenMilestone.value,
      issued_to: newTokenIssuedTo.value,
      reason: newTokenReason.value,
      expires_in_hours: newTokenHours.value,
    };
    const res = await statisticalComputingService.issueLockToken(payload);
    showIssueTokenModal.value = false;
    await loadTokens();
    if (res.data?.token?.token_id) {
      selectedTokenId.value = res.data.token.token_id;
    }
  } catch (err: any) {
    toastStore.push({
      tone: "error",
      title: "Token Issuance Failed",
      message: presentError(err, "Failed to issue database lock token."),
    });
  }
}

async function revokeToken(tokenId: string) {
  const confirmed = await confirmDialog({
    title: "Revoke Lock Token",
    message: `Are you sure you want to revoke token ${tokenId}?`,
    confirmLabel: "Revoke Token",
    cancelLabel: "Cancel",
    variant: "danger",
  });
  if (!confirmed) return;
  try {
    await statisticalComputingService.revokeLockToken(
      tokenId,
      "Revoked by user action"
    );
    await loadTokens();
  } catch (err: any) {
    toastStore.push({
      tone: "error",
      title: "Token Revocation Failed",
      message: presentError(err, "Failed to revoke database lock token."),
    });
  }
}

function isTokenExpired(expiresAtStr: string) {
  return new Date(expiresAtStr) < new Date();
}

function getArtifactIcon(type: string) {
  if (type === "TABLE") return "📊";
  if (type === "FIGURE") return "📈";
  if (type === "LISTING") return "📜";
  return "📁";
}

function formatDate(isoStr: string) {
  if (!isoStr) return "—";
  return new Date(isoStr).toLocaleString();
}

// Data loaders
async function loadPrograms() {
  try {
    const res = await statisticalComputingService.getPrograms(props.studyId);
    programs.value = res.data.programs || [];
  } catch (err) {
    console.error("Failed to load programs:", err);
  }
}

async function loadSnapshots() {
  try {
    const res = await apiClient.get("/api/v1/interop/lakehouse/snapshots", {
      params: { study_id: props.studyId },
    });
    snapshots.value = res.data.snapshots || [];
    if (snapshots.value.length > 0 && !selectedSnapshotId.value) {
      selectedSnapshotId.value = snapshots.value[0].snapshot_id;
    }
  } catch (err) {
    console.error("Failed to load snapshots:", err);
  }
}

async function loadRuns() {
  try {
    const res = await statisticalComputingService.getExecutionRuns(
      props.studyId
    );
    runs.value = res.data.runs || [];
  } catch (err) {
    console.error("Failed to load runs:", err);
  }
}

async function loadTokens() {
  try {
    const res = await statisticalComputingService.getLockTokens(props.studyId);
    lockTokens.value = res.data.tokens || [];
    const active = lockTokens.value.find(
      (t) => !t.is_revoked && !isTokenExpired(t.expires_at)
    );
    if (active && !selectedTokenId.value) {
      selectedTokenId.value = active.token_id;
    }
  } catch (err) {
    console.error("Failed to load tokens:", err);
  }
}

async function loadData() {
  isLoading.value = true;
  await Promise.all([
    loadPrograms(),
    loadSnapshots(),
    loadRuns(),
    loadTokens(),
  ]);
  if (!sourceCode.value) {
    sourceCode.value = pythonTemplates.demographics;
  }
  if (!testLockContent.value) {
    loadSampleLockfile();
  }
  isLoading.value = false;
}

onMounted(() => {
  loadData();
});
</script>

<style scoped>
.sce-console {
  display: flex;
  flex-direction: column;
  gap: 20px;
  width: 100%;
  color: var(--semantic-color-text);
}

/* Header */
.sce-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
  padding: 24px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-lg);
}

.header-main {
  flex: 1;
}

.badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.badge {
  font-size: 11px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: var(--radius-sm);
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

.badge-gxp {
  background: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}

.badge-reproducible {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.badge-sandbox {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.badge-warn {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.badge-neutral {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
}

.badge-active {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.badge-expired {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.badge-revoked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.badge-green {
  color: var(--semantic-color-success);
  font-weight: 700;
}

.sce-title {
  margin: 0 0 6px;
  font-size: 24px;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.sce-subtitle {
  margin: 0;
  font-size: 13px;
  color: var(--semantic-color-text-secondary);
  line-height: 1.5;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
}

.blinding-status-pill {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 14px;
  border-radius: var(--radius-md);
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
}

.blinding-status-pill.unblinded-active {
  background: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success-border);
}

.status-icon {
  font-size: 18px;
}

.status-info {
  display: flex;
  flex-direction: column;
}

.status-label {
  font-size: 12px;
  font-weight: 700;
}

.status-sub {
  font-size: 10px;
  color: var(--semantic-color-text-secondary);
}

/* Nav Tabs */
.sce-nav-tabs {
  display: flex;
  gap: 6px;
  border-bottom: 2px solid var(--border);
  padding-bottom: 2px;
}

.tab-btn {
  padding: 12px 18px;
  font-size: 13px;
  font-weight: 600;
  border: none;
  background: transparent;
  color: var(--semantic-color-text-secondary);
  border-bottom: 3px solid transparent;
  cursor: pointer;
  min-height: 48px;
  transition: all 0.2s ease;
}

.tab-btn:hover {
  color: var(--primary);
}

.tab-btn.active {
  color: var(--primary);
  border-bottom-color: var(--primary);
}

/* Common Card */
.card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-sm);
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.card-title {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
}

.card-sub {
  margin: 4px 0 0;
  font-size: 12px;
  color: var(--semantic-color-text-secondary);
}

.total-badge {
  font-size: 12px;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

/* Workspace Grid */
.workspace-grid {
  display: grid;
  grid-template-columns: 1.3fr 1fr;
  gap: 20px;
}

@media (max-width: 1200px) {
  .workspace-grid {
    grid-template-columns: 1fr;
  }
}

.editor-controls-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 12px 20px;
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--border);
}

.lang-switch-group {
  display: flex;
  gap: 6px;
}

.lang-btn {
  padding: 8px 14px;
  font-size: 12px;
  font-weight: 700;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface);
  cursor: pointer;
  min-height: 38px;
}

.lang-btn.active {
  background: var(--primary);
  color: var(--semantic-color-surface);
  border-color: var(--primary);
}

.template-selector {
  display: flex;
  align-items: center;
  gap: 8px;
}

.template-label {
  font-size: 11px;
  color: var(--semantic-color-text-secondary);
}

.meta-inputs-grid {
  display: grid;
  grid-template-columns: 1fr 1.5fr 1fr;
  gap: 12px;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-size: 12px;
  font-weight: 600;
}

.form-input,
.form-select {
  padding: 10px 12px;
  font-size: 13px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  min-height: 44px;
}

.code-editor-wrapper {
  display: flex;
  flex-direction: column;
}

.editor-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 16px;
  background: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse-muted);
  font-size: 12px;
  font-family: var(--font-mono);
}

.code-textarea {
  width: 100%;
  padding: 16px;
  font-family: var(--font-mono);
  font-size: 13px;
  line-height: 1.6;
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border: none;
  resize: vertical;
  min-height: 350px;
}

.actions-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 14px 20px;
  background: var(--semantic-color-surface-muted);
}

/* Runner Card */
.runner-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.field-hint {
  font-size: 11px;
  color: var(--semantic-color-text-secondary);
}

.unblinded-gating-box {
  padding: 16px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface-muted);
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.unblinded-gating-box.gating-active {
  border-color: var(--semantic-color-warning-border-strong);
  background: var(--semantic-color-warning-bg-soft);
}

.toggle-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 13px;
  font-weight: 600;
  cursor: pointer;
}

.form-checkbox {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
}

.token-selection-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.token-warning {
  margin: 0;
  font-size: 11px;
  color: var(--semantic-color-warning);
  line-height: 1.4;
}

.bounds-box {
  padding: 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface);
}

.bounds-title {
  margin: 0 0 10px;
  font-size: 12px;
  text-transform: uppercase;
  color: var(--semantic-color-text-secondary);
}

.bounds-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
}

.bound-item {
  display: flex;
  justify-content: space-between;
  font-size: 12px;
}

.bound-name {
  color: var(--semantic-color-text-secondary);
}

.bound-val {
  font-weight: 600;
}

.dispatch-group {
  margin-top: 8px;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 10px 18px;
  font-size: 13px;
  font-weight: 600;
  border-radius: var(--radius-sm);
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.2s ease;
  min-height: 44px;
}

.btn-lg {
  padding: 14px 24px;
  font-size: 14px;
  min-height: 50px;
}

.btn-block {
  width: 100%;
}

.btn-primary {
  background: var(--primary);
  color: var(--semantic-color-surface);
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--border);
  color: var(--semantic-color-text);
}

.btn-outline {
  background: transparent;
  border-color: var(--border);
  color: var(--semantic-color-text);
}

.btn-danger {
  color: var(--semantic-color-error);
  border-color: var(--semantic-color-error-border);
}

.btn-xs {
  padding: 4px 10px;
  font-size: 11px;
  min-height: 32px;
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 20px;
  cursor: pointer;
}

/* Latest Run Box */
.latest-run-card {
  padding: 14px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface-muted);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.latest-run-card.status-completed {
  border-color: var(--semantic-color-success-border);
  background: var(--semantic-color-success-bg-soft);
}

.latest-run-card.status-failed,
.latest-run-card.status-rejected {
  border-color: var(--semantic-color-error-border);
  background: var(--semantic-color-error-bg-soft);
}

.run-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.status-pill {
  padding: 3px 8px;
  border-radius: var(--radius-sm);
  font-size: 11px;
  font-weight: 700;
}

.pill-completed {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.pill-failed,
.pill-rejected {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.pill-timeout {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.manifest-seal-row {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.seal-label {
  font-size: 10px;
  color: var(--semantic-color-text-secondary);
}

.seal-code {
  font-size: 11px;
  word-break: break-all;
  font-family: var(--font-mono);
}

.artifacts-summary {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 4px;
  font-size: 12px;
  font-weight: 600;
}

/* Table Styles */
.table-container {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
}

.data-table th,
.data-table td {
  padding: 12px 16px;
  text-align: left;
  border-bottom: 1px solid var(--border);
}

.data-table th {
  background: var(--semantic-color-surface-muted);
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
  font-size: 11px;
}

.cell-id code {
  font-family: var(--font-mono);
  font-size: 11px;
}

.cell-bold {
  font-weight: 600;
}

.cell-num {
  font-family: var(--font-mono);
}

.cell-date {
  color: var(--semantic-color-text-secondary);
  font-size: 11px;
}

.digest-pill {
  font-family: var(--font-mono);
  font-size: 11px;
  background: var(--semantic-color-surface-muted);
  padding: 2px 6px;
  border-radius: var(--radius-sm);
}

.lang-tag {
  font-size: 10px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: var(--radius-sm);
}

.lang-python {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
}

.lang-r {
  background: var(--semantic-color-ai-bg);
  color: var(--semantic-color-ai-dark);
}

.version-pill {
  font-size: 11px;
  background: var(--semantic-color-surface-muted);
  padding: 2px 6px;
  border-radius: var(--radius-sm);
}

.badge-unblind-tag {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  font-size: 10px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: var(--radius-sm);
}

.badge-blinded-tag {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  font-size: 10px;
  padding: 2px 6px;
  border-radius: var(--radius-sm);
}

.action-buttons {
  display: flex;
  gap: 6px;
}

/* Detail Drawer */
.detail-drawer {
  margin-top: 20px;
  border: 2px solid var(--primary);
}

.drawer-content {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.provenance-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 12px;
}

@media (max-width: 900px) {
  .provenance-grid {
    grid-template-columns: 1fr;
  }
}

.prov-card {
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface-muted);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.prov-label {
  font-size: 11px;
  color: var(--semantic-color-text-secondary);
}

.prov-card code {
  font-size: 11px;
  word-break: break-all;
  font-family: var(--font-mono);
}

/* Signature Block Card */
.sig-block-card {
  padding: 16px;
  border: 1px solid var(--semantic-color-divider-accent);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-info-bg);
}

.sig-title {
  margin: 0 0 12px;
  font-size: 13px;
  font-weight: 700;
  color: var(--semantic-color-info);
}

.sig-grid {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr;
  gap: 12px;
}

.sig-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.sig-item.full-width {
  grid-column: span 3;
}

.sig-lbl {
  font-size: 11px;
  color: var(--semantic-color-text-secondary);
}

.sig-val {
  font-size: 12px;
  font-weight: 600;
}

.sig-hmac {
  font-size: 11px;
  font-family: var(--font-mono);
  word-break: break-all;
}

/* Artifacts */
.artifacts-list {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.artifact-card {
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  padding: 12px;
  background: var(--semantic-color-surface);
}

.art-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.art-icon {
  font-size: 20px;
}

.art-meta {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.art-name {
  font-weight: 700;
  font-size: 13px;
}

.art-sub {
  font-size: 11px;
  color: var(--semantic-color-text-secondary);
}

.art-preview-box {
  margin-top: 8px;
  padding: 8px;
  background: var(--semantic-color-surface-muted);
  border-radius: var(--radius-sm);
  overflow-x: auto;
}

.preview-text {
  margin: 0;
  font-size: 11px;
  font-family: var(--font-mono);
}

.console-box {
  border: 1px solid var(--semantic-color-text-body);
  border-radius: var(--radius-sm);
  overflow: hidden;
}

.console-header-bar {
  padding: 6px 12px;
  background: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse-muted);
  font-size: 11px;
  font-family: var(--font-mono);
}

.console-text {
  margin: 0;
  padding: 14px;
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-border);
  font-family: var(--font-mono);
  font-size: 12px;
  line-height: 1.5;
  max-height: 300px;
  overflow-y: auto;
}

/* Lockfile Tab */
.lockfile-body {
  padding: 20px;
}

.lock-test-grid {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.lock-textarea {
  min-height: 180px;
}

.lock-actions-row {
  display: flex;
  gap: 12px;
}

.validation-result-box {
  padding: 16px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.validation-result-box.res-valid {
  border-color: var(--semantic-color-success-border);
  background: var(--semantic-color-success-bg-soft);
}

.validation-result-box.res-tampered,
.validation-result-box.res-unqualified_package {
  border-color: var(--semantic-color-error-border);
  background: var(--semantic-color-error-bg-soft);
}

.package-list-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
  gap: 10px;
  margin-top: 8px;
}

.pkg-card {
  padding: 10px;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface);
}

.pkg-header {
  display: flex;
  justify-content: space-between;
  font-weight: 700;
  font-size: 12px;
}

.pkg-footer {
  display: flex;
  justify-content: space-between;
  font-size: 11px;
  margin-top: 4px;
}

.qual-yes {
  color: var(--semantic-color-success);
  font-weight: 600;
}

.qual-no {
  color: var(--semantic-color-error);
  font-weight: 600;
}

/* Modal Backdrop */
.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-dialog {
  width: 90%;
  max-width: 580px;
  background: var(--semantic-color-surface);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.modal-title {
  margin: 0;
  font-size: 16px;
  font-weight: 700;
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  background: var(--semantic-color-surface-muted);
}
</style>
