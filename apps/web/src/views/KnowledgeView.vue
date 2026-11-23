<template>
  <div id="section-knowledge-hub" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH GCP E6(R3)</span>
          <span class="badge badge-standard">GxP Controlled Documents</span>
          <span class="badge badge-standard">Tamper-Evident Ledger</span>
        </div>
        <h2 class="view-title">Knowledge &amp; Support Hub</h2>
        <p class="view-subtitle">
          Centralized, role-aware clinical standard operating procedure (SOP)
          repository, regulatory guidance, and contextual platform documentation
          with 21 CFR Part 11 immutable versioning, four-eyes approval controls,
          and full-text search.
        </p>
      </div>

      <div v-if="demoBuild && isSuperAdmin" class="header-action-group">
        <button
          id="btn-create-category"
          class="btn btn-secondary"
          title="Create New Knowledge Category"
          @click="openCreateCategoryModal"
        >
          <span class="btn-icon">📁</span> New Category
        </button>
        <button
          id="btn-create-article"
          class="btn btn-primary"
          title="Author New Knowledge SOP Article"
          @click="openCreateArticleModal"
        >
          <span class="btn-icon">➕</span> New Article
        </button>
      </div>
    </div>

    <!-- Metric KPI Summary Cards -->
    <div class="stats-grid">
      <button
        type="button"
        class="stat-card"
        :class="{ 'stat-card-active': activeStatusFilter === 'ALL' }"
        :aria-pressed="activeStatusFilter === 'ALL'"
        @click="setStatusFilter('ALL')"
        @keydown.enter.prevent="setStatusFilter('ALL')"
        @keydown.space.prevent="setStatusFilter('ALL')"
      >
        <span class="stat-icon alert-icon" aria-hidden="true">📚</span>
        <span class="stat-details">
          <span class="stat-value">
            {{ totalArticlesCount }}
          </span>
          <span class="stat-label">Total SOPs &amp; Guides</span>
        </span>
      </button>

      <button
        type="button"
        class="stat-card"
        :class="{ 'stat-card-active': activeStatusFilter === 'PUBLISHED' }"
        :aria-pressed="activeStatusFilter === 'PUBLISHED'"
        @click="setStatusFilter('PUBLISHED')"
        @keydown.enter.prevent="setStatusFilter('PUBLISHED')"
        @keydown.space.prevent="setStatusFilter('PUBLISHED')"
      >
        <span class="stat-icon capa-icon" aria-hidden="true">✅</span>
        <span class="stat-details">
          <span class="stat-value text-success">
            {{ publishedArticlesCount }}
          </span>
          <span class="stat-label">Active Published SOPs</span>
        </span>
      </button>

      <button
        v-if="demoBuild"
        type="button"
        class="stat-card"
        :class="{ 'stat-card-active': activeStatusFilter === 'IN_REVIEW' }"
        :aria-pressed="activeStatusFilter === 'IN_REVIEW'"
        @click="setStatusFilter('IN_REVIEW')"
        @keydown.enter.prevent="setStatusFilter('IN_REVIEW')"
        @keydown.space.prevent="setStatusFilter('IN_REVIEW')"
      >
        <span class="stat-icon rbqm-icon" aria-hidden="true">🔍</span>
        <span class="stat-details">
          <span class="stat-value text-warning">
            {{ inReviewArticlesCount }}
          </span>
          <span class="stat-label">Under Four-Eyes Review</span>
        </span>
      </button>

      <button
        v-if="demoBuild"
        type="button"
        class="stat-card"
        :class="{ 'stat-card-active': activeStatusFilter === 'DRAFT' }"
        :aria-pressed="activeStatusFilter === 'DRAFT'"
        @click="setStatusFilter('DRAFT')"
        @keydown.enter.prevent="setStatusFilter('DRAFT')"
        @keydown.space.prevent="setStatusFilter('DRAFT')"
      >
        <span class="stat-icon breach-icon" aria-hidden="true">📝</span>
        <span class="stat-details">
          <span class="stat-value">
            {{ draftArticlesCount }}
          </span>
          <span class="stat-label">Working Draft Revisions</span>
        </span>
      </button>
    </div>

    <!-- Main 3-Column Knowledge Workspace -->
    <div class="knowledge-layout">
      <!-- Column 1: Categories & Taxonomy Sidebar -->
      <aside id="knowledge-sidebar" class="knowledge-sidebar">
        <div class="sidebar-box-header">
          <h4 class="sidebar-heading">
            <span class="heading-icon">🗂️</span> Categories
          </h4>
          <span
            v-if="!isSuperAdmin"
            class="badge badge-persona-filter"
            :title="`Filtered by ${activePersonaLabel}`"
          >
            {{ activePersonaKey }}
          </span>
        </div>

        <div class="category-list">
          <button
            type="button"
            class="category-item-btn"
            :class="{ active: selectedCategoryId === null }"
            @click="selectCategory(null)"
          >
            <span class="category-icon">📂</span>
            <span class="category-title">All Categories</span>
            <span class="category-count">{{
              filteredByPersonaArticles.length
            }}</span>
          </button>

          <button
            v-for="cat in visibleCategories"
            :key="cat.id"
            type="button"
            class="category-item-btn"
            :class="{ active: selectedCategoryId === cat.id }"
            @click="selectCategory(cat.id)"
          >
            <span class="category-icon">{{ getCategoryIcon(cat.slug) }}</span>
            <div class="category-text-block">
              <span class="category-title">{{ cat.name }}</span>
              <span v-if="cat.description" class="category-desc">{{
                cat.description
              }}</span>
            </div>
            <span class="category-count">{{
              getArticleCountForCategory(cat.id)
            }}</span>
          </button>
        </div>

        <!-- Filter by Status Selector -->
        <div class="sidebar-filter-section">
          <label for="status-filter-select" class="filter-label">
            Lifecycle Status Filter
          </label>
          <select
            id="status-filter-select"
            v-model="activeStatusFilter"
            class="form-control form-select-sm"
          >
            <option value="ALL">All Lifecycle States</option>
            <option value="PUBLISHED">Published Only</option>
            <option v-if="demoBuild" value="IN_REVIEW">In Review</option>
            <option v-if="demoBuild" value="APPROVED">Approved</option>
            <option v-if="demoBuild" value="DRAFT">Working Drafts</option>
            <option v-if="demoBuild" value="REJECTED">Rejected</option>
            <option v-if="demoBuild" value="SUPERSEDED">Superseded</option>
            <option v-if="demoBuild" value="ARCHIVED">Archived</option>
          </select>
        </div>

        <div class="sidebar-filter-section">
          <label for="knowledge-study-filter" class="filter-label">
            Study Scope
          </label>
          <input
            id="knowledge-study-filter"
            v-model="scopeStudyId"
            type="text"
            class="form-control form-select-sm knowledge-scope-input"
            placeholder="All studies"
          />
          <label for="knowledge-site-filter" class="filter-label">
            Site Scope
          </label>
          <input
            id="knowledge-site-filter"
            v-model="scopeSiteId"
            type="text"
            class="form-control form-select-sm knowledge-scope-input"
            placeholder="All sites"
          />
          <label for="knowledge-subject-filter" class="filter-label">
            Participant Scope
          </label>
          <input
            id="knowledge-subject-filter"
            v-model="scopeSubjectId"
            type="text"
            class="form-control form-select-sm knowledge-scope-input"
            placeholder="All participants"
          />
        </div>
      </aside>

      <!-- Column 2: Article Directory & Search List -->
      <section id="knowledge-article-list-panel" class="article-list-panel">
        <!-- Search and Tag Bar -->
        <div class="list-search-bar">
          <div class="search-input-wrapper">
            <span class="search-symbol" aria-hidden="true">🔍</span>
            <label for="knowledge-search-input" class="sr-only"
              >Search SOPs by title, tag, or content</label
            >
            <input
              id="knowledge-search-input"
              v-model="searchQuery"
              type="text"
              class="knowledge-search-input"
              placeholder="Search SOPs by title, tag, or content..."
              @input="onSearchInput"
            />
            <ClinicalIconButton
              v-if="searchQuery"
              class="btn-clear-search"
              title="Clear search"
              label="Clear search"
              @click="clearSearch"
            >
              ✕
            </ClinicalIconButton>
          </div>

          <!-- Quick Tag Filter Chips -->
          <div
            v-if="availableTags.length > 0"
            class="tag-chips-scroll"
            tabindex="0"
            role="region"
            aria-label="Filter SOPs by tag"
          >
            <button
              v-for="tag in availableTags"
              :key="tag"
              type="button"
              class="tag-chip"
              :class="{ 'tag-chip-active': selectedTag === tag }"
              :aria-pressed="selectedTag === tag"
              @click="toggleTag(tag)"
            >
              #{{ tag }}
            </button>
          </div>
        </div>

        <!-- Articles Stream -->
        <div v-if="loading" class="articles-loading-state">
          <div class="spinner" />
          <p>Loading clinical SOP repository...</p>
        </div>

        <div
          v-else-if="displayArticles.length === 0"
          class="articles-empty-state"
        >
          <div class="empty-icon">📄</div>
          <h4>No SOPs Found</h4>
          <p v-if="searchQuery || selectedTag || activeStatusFilter !== 'ALL'">
            No articles match your current search or status filters.
          </p>
          <p v-else>No SOPs are currently visible for the active persona.</p>
          <button
            v-if="searchQuery || selectedTag || activeStatusFilter !== 'ALL'"
            type="button"
            class="btn btn-secondary btn-sm"
            @click="resetFilters"
          >
            Reset Filters
          </button>
        </div>

        <div
          v-else
          class="articles-stream"
          tabindex="0"
          role="region"
          aria-label="Standard operating procedure articles"
        >
          <button
            v-for="art in displayArticles"
            :key="art.id"
            type="button"
            class="article-card"
            :class="{ 'article-card-selected': selectedArticle?.id === art.id }"
            :aria-pressed="selectedArticle?.id === art.id"
            :aria-current="selectedArticle?.id === art.id ? 'true' : undefined"
            @click="selectArticle(art)"
            @keydown.enter.prevent="selectArticle(art)"
            @keydown.space.prevent="selectArticle(art)"
          >
            <span class="article-card-header">
              <span class="status-pill" :class="getStatusPillClass(art.status)">
                {{ art.status }}
              </span>
              <span v-if="art.version_label" class="badge badge-version">
                v{{ art.version_label }}
              </span>
            </span>

            <span class="article-card-title">
              {{ art.title }}
            </span>

            <span class="article-card-meta">
              <span class="meta-category"
                >📁 {{ art.category_name || "General" }}</span
              >
              <span v-if="art.updated_at" class="meta-date"
                >📅 {{ formatDate(art.updated_at) }}</span
              >
            </span>

            <span
              v-if="art.tags && art.tags.length > 0"
              class="article-card-tags"
            >
              <span v-for="t in getTagList(art.tags)" :key="t" class="mini-tag">
                #{{ t }}
              </span>
            </span>
          </button>
        </div>
      </section>

      <!-- Column 3: Article Reader & GxP Inspection Pane -->
      <main id="knowledge-reader-pane" class="article-reader-pane">
        <div v-if="!selectedArticle" class="reader-empty-state">
          <div class="empty-icon">📖</div>
          <h3>Select an SOP or Guideline</h3>
          <p>
            Choose an article from the directory on the left to read guidance,
            inspect historical version snapshots, or review the 21 CFR Part 11
            audit trail.
          </p>
        </div>

        <div v-else class="reader-content-wrap">
          <!-- Article Top Title & Metadata Bar -->
          <div class="reader-header-card">
            <div class="reader-meta-top">
              <div class="breadcrumbs">
                <span class="crumb-category"
                  >📁
                  {{ selectedArticle.category_name || "General SOPs" }}</span
                >
                <span class="crumb-separator">/</span>
                <span class="crumb-slug">{{ selectedArticle.slug }}</span>
              </div>
              <div class="reader-badges">
                <span
                  class="status-pill status-pill-lg"
                  :class="getStatusPillClass(selectedArticle.status)"
                >
                  {{ selectedArticle.status }}
                </span>
                <span
                  v-if="selectedArticle.version_label"
                  class="badge badge-version-lg"
                >
                  v{{ selectedArticle.version_label }}
                </span>
              </div>
            </div>

            <h2 class="reader-article-title">
              {{ selectedArticle.title }}
            </h2>

            <div class="reader-author-row">
              <div class="author-item">
                <span class="author-label">Author:</span>
                <span class="author-value">{{
                  selectedArticle.author_user_id || "system"
                }}</span>
              </div>
              <div v-if="selectedArticle.last_edited_by" class="author-item">
                <span class="author-label">Last Edited By:</span>
                <span class="author-value">{{
                  selectedArticle.last_edited_by
                }}</span>
              </div>
              <div v-if="selectedArticle.approved_by" class="author-item">
                <span class="author-label">Approved By:</span>
                <span class="author-value text-success"
                  >✓ {{ selectedArticle.approved_by }}</span
                >
              </div>
              <div v-if="selectedArticle.updated_at" class="author-item">
                <span class="author-label">Last Revised:</span>
                <span class="author-value">{{
                  formatDate(selectedArticle.updated_at)
                }}</span>
              </div>
            </div>

            <!-- Tags Row -->
            <div
              v-if="selectedArticle.tags && selectedArticle.tags.length > 0"
              class="reader-tags-row"
            >
              <span
                v-for="tag in getTagList(selectedArticle.tags)"
                :key="tag"
                class="tag-badge"
              >
                #{{ tag }}
              </span>
            </div>

            <!-- Admin Authoring & Lifecycle Controls (demo builds + super_admin only; seven-state authoring out of live scope) -->
            <div
              v-if="demoBuild && isSuperAdmin"
              id="admin-authoring-toolbar"
              class="admin-lifecycle-toolbar"
            >
              <span class="toolbar-label">👑 Admin Controls:</span>

              <!-- Draft & Rejected State Actions -->
              <template
                v-if="
                  selectedArticle.status === 'DRAFT' ||
                  selectedArticle.status === 'REJECTED'
                "
              >
                <button
                  id="btn-edit-draft"
                  class="btn btn-secondary btn-sm"
                  title="Edit Working Markdown Draft"
                  @click="openEditDraftModal"
                >
                  ✏️ Edit Draft
                </button>
                <button
                  id="btn-submit-review"
                  class="btn btn-primary btn-sm"
                  title="Submit Draft for Peer Review (DRAFT -> IN_REVIEW)"
                  @click="promptSubmitReview"
                >
                  📨 Submit for Review
                </button>
              </template>

              <!-- In Review State Actions -->
              <template v-else-if="selectedArticle.status === 'IN_REVIEW'">
                <button
                  id="btn-approve-article"
                  class="btn btn-success btn-sm"
                  title="Approve Article (Enforces Four-Eyes Principle)"
                  @click="promptApproveArticle"
                >
                  ✓ Approve Article
                </button>
                <button
                  id="btn-reject-article"
                  class="btn btn-danger btn-sm"
                  title="Reject Article Back to Author"
                  @click="promptRejectArticle"
                >
                  ✕ Reject Draft
                </button>
                <button
                  class="btn btn-secondary btn-sm"
                  title="Edit Working Draft"
                  @click="openEditDraftModal"
                >
                  ✏️ Edit Draft
                </button>
              </template>

              <!-- Approved State Actions -->
              <template v-else-if="selectedArticle.status === 'APPROVED'">
                <button
                  id="btn-publish-article"
                  class="btn btn-primary btn-sm"
                  title="Publish Article (Auto-supersedes prior version)"
                  @click="promptPublishArticle"
                >
                  🚀 Publish Live
                </button>
                <button
                  class="btn btn-secondary btn-sm"
                  title="Create New Revision Draft"
                  @click="openEditDraftModal"
                >
                  📝 New Revision Draft
                </button>
              </template>

              <!-- Published State Actions -->
              <template v-else-if="selectedArticle.status === 'PUBLISHED'">
                <button
                  id="btn-new-revision"
                  class="btn btn-secondary btn-sm"
                  title="Start New Draft Revision"
                  @click="openEditDraftModal"
                >
                  📝 Create Amendment Draft
                </button>
                <button
                  id="btn-archive-article"
                  class="btn btn-danger btn-sm"
                  title="Archive SOP Document"
                  @click="promptArchiveArticle"
                >
                  📦 Archive
                </button>
              </template>
            </div>
          </div>

          <!-- Reader Tabs (SOP Prose / Version History / Audit Ledger) -->
          <ClinicalTabs
            :model-value="activeReaderTab"
            :tabs="readerTabs"
            aria-label="SOP document inspection tabs"
            id-prefix="knowledge-reader"
            list-class="reader-tab-nav"
            tab-class="reader-tab-btn"
            @update:model-value="handleReaderTabChange"
          />

          <!-- Tab Content 1: SOP Markdown Prose -->
          <div
            v-if="activeReaderTab === 'content'"
            id="sop-prose-viewer"
            role="tabpanel"
            aria-labelledby="knowledge-reader-tab-content"
            class="reader-body-card"
            tabindex="0"
          >
            <div
              v-if="selectedArticle.body_html"
              class="prose-content"
              v-html="selectedArticle.body_html"
            />
            <div
              v-else-if="selectedArticle.body_markdown"
              class="prose-content markdown-fallback"
            >
              <pre>{{ selectedArticle.body_markdown }}</pre>
            </div>
            <div v-else class="prose-empty">
              <p>No content has been authored for this SOP draft.</p>
            </div>
          </div>

          <!-- Tab Content 2: Immutable Version History -->
          <div
            v-else-if="activeReaderTab === 'versions'"
            id="sop-versions-viewer"
            role="tabpanel"
            aria-labelledby="knowledge-reader-tab-versions"
            class="reader-body-card"
            tabindex="0"
          >
            <div class="tab-section-header">
              <h4>Immutable Snapshot Versions</h4>
              <p class="section-desc">
                Controlled snapshot archive of all historical versions under 21
                CFR Part 11.
              </p>
            </div>

            <div v-if="versionsLoading" class="loading-box">
              <div class="spinner" />
              <p>Loading version snapshots...</p>
            </div>

            <div v-else-if="versionsList.length === 0" class="empty-box">
              <p>No snapshot versions recorded for this document.</p>
            </div>

            <div v-else class="versions-table-wrap">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Version</th>
                    <th>Index</th>
                    <th>Status at Snapshot</th>
                    <th>Author / Editor</th>
                    <th>Reason for Change</th>
                    <th>Timestamp</th>
                    <th>Locked</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="ver in versionsList" :key="ver.id">
                    <td>
                      <span class="badge badge-version"
                        >v{{ ver.version_label }}</span
                      >
                    </td>
                    <td>#{{ ver.version_index }}</td>
                    <td>
                      <span
                        class="status-pill"
                        :class="getStatusPillClass(ver.status_at_snapshot)"
                      >
                        {{ ver.status_at_snapshot }}
                      </span>
                    </td>
                    <td>{{ ver.created_by || "system" }}</td>
                    <td>
                      {{ ver.reason_for_change || "Baseline initial version" }}
                    </td>
                    <td>{{ formatDate(ver.created_at) }}</td>
                    <td>
                      <span
                        class="badge"
                        :class="
                          ver.is_locked ? 'badge-success' : 'badge-neutral'
                        "
                      >
                        {{ ver.is_locked ? "🔒 Locked" : "📝 Draft" }}
                      </span>
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- Tab Content 3: Part 11 Audit Trail -->
          <div
            v-else-if="activeReaderTab === 'audit'"
            id="sop-audit-viewer"
            role="tabpanel"
            aria-labelledby="knowledge-reader-tab-audit"
            class="reader-body-card"
            tabindex="0"
          >
            <div class="tab-section-header">
              <h4>21 CFR Part 11 Electronic Audit Ledger</h4>
              <p class="section-desc">
                Append-only tamper-evident transaction log with SHA-256
                cryptographic hashes.
              </p>
            </div>

            <div v-if="auditLoading" class="loading-box">
              <div class="spinner" />
              <p>Fetching immutable audit trail...</p>
            </div>

            <div v-else-if="auditLogList.length === 0" class="empty-box">
              <p>No audit trail logs recorded yet.</p>
            </div>

            <div v-else class="audit-table-wrap">
              <table class="data-table">
                <thead>
                  <tr>
                    <th>Event Type</th>
                    <th>Actor</th>
                    <th>Target Status</th>
                    <th>Version</th>
                    <th>Reason for Change</th>
                    <th>SHA-256 Digest</th>
                    <th>Timestamp</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="log in auditLogList" :key="log.id">
                    <td>
                      <span class="badge badge-standard">{{
                        log.event_type
                      }}</span>
                    </td>
                    <td>
                      <strong>{{ log.actor_user_id }}</strong>
                    </td>
                    <td>
                      <span
                        v-if="log.target_status"
                        class="status-pill"
                        :class="getStatusPillClass(log.target_status)"
                      >
                        {{ log.target_status }}
                      </span>
                      <span v-else>—</span>
                    </td>
                    <td>v{{ log.version_label || "1.0" }}</td>
                    <td>
                      {{ log.reason_for_change || "Document lifecycle update" }}
                    </td>
                    <td>
                      <code class="hash-code" :title="log.sha256_hash">
                        {{
                          (log.sha256_hash || "sha256:e3b0c442").substring(
                            0,
                            12
                          )
                        }}...
                      </code>
                    </td>
                    <td>{{ formatDate(log.created_at) }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>
      </main>
    </div>

    <!-- Modal 1: Create New Article Modal -->
    <ClinicalModal
      v-model="isCreateArticleModalOpen"
      :teleport="false"
      :show-footer="false"
      size="lg"
      dialog-class="modal-card modal-lg"
      aria-labelledby="create-art-modal-title"
    >
      <template #header>
        <h3 id="create-art-modal-title">➕ Author New Knowledge SOP Article</h3>
      </template>

      <form @submit.prevent="handleCreateArticle">
        <div class="form-group">
          <label for="new-art-title">SOP Title *</label>
          <input
            id="new-art-title"
            v-model="newArticleForm.title"
            type="text"
            class="form-control"
            placeholder="e.g. SOP-ECRF-301: Adverse Event Severity Grading"
            required
            @input="autoGenerateSlug"
          />
        </div>

        <div class="form-row">
          <div class="form-group col">
            <label for="new-art-slug">Document Slug *</label>
            <input
              id="new-art-slug"
              v-model="newArticleForm.slug"
              type="text"
              class="form-control"
              placeholder="sop-ecrf-301-adverse-events"
              required
            />
          </div>
          <div class="form-group col">
            <label for="new-art-category">Category *</label>
            <select
              id="new-art-category"
              v-model="newArticleForm.category_id"
              class="form-control"
              required
            >
              <option
                v-for="cat in visibleCategories"
                :key="cat.id"
                :value="cat.id"
              >
                {{ cat.name }}
              </option>
            </select>
          </div>
        </div>

        <div class="form-row">
          <div class="form-group col">
            <label for="new-art-version">Initial Version Label</label>
            <input
              id="new-art-version"
              v-model="newArticleForm.version_label"
              type="text"
              class="form-control"
              placeholder="0.1"
            />
          </div>
          <div class="form-group col">
            <label for="new-art-tags">Tags (comma-separated)</label>
            <input
              id="new-art-tags"
              v-model="newArticleForm.tagsInput"
              type="text"
              class="form-control"
              placeholder="ecrf, safety, adverse-events"
            />
          </div>
        </div>

        <div class="form-group">
          <label for="new-art-body">Markdown SOP Body *</label>
          <textarea
            id="new-art-body"
            v-model="newArticleForm.body_markdown"
            class="form-control code-textarea"
            rows="8"
            placeholder="## Purpose & Scope&#10;Describe procedural guidance, regulatory backing (21 CFR Part 11), and responsibilities..."
            required
          />
        </div>

        <div class="form-group">
          <label for="new-art-reason">GxP Reason for Change *</label>
          <input
            id="new-art-reason"
            v-model="newArticleForm.reason_for_change"
            type="text"
            class="form-control"
            placeholder="Initial authoring of standard operating procedure"
            required
          />
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isCreateArticleModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">
            Save Working Draft
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Modal 2: Edit Working Draft Modal -->
    <ClinicalModal
      v-model="isEditDraftModalOpen"
      :teleport="false"
      :show-footer="false"
      size="lg"
      dialog-class="modal-card modal-lg"
      aria-labelledby="edit-draft-modal-title"
    >
      <template #header>
        <h3 id="edit-draft-modal-title">✏️ Edit Working Draft SOP</h3>
      </template>

      <form @submit.prevent="handleSaveDraft">
        <div class="form-group">
          <label for="edit-art-title">SOP Title *</label>
          <input
            id="edit-art-title"
            v-model="editDraftForm.title"
            type="text"
            class="form-control"
            required
          />
        </div>

        <div class="form-row">
          <div class="form-group col">
            <label for="edit-art-category">Category</label>
            <select
              id="edit-art-category"
              v-model="editDraftForm.category_id"
              class="form-control"
            >
              <option
                v-for="cat in visibleCategories"
                :key="cat.id"
                :value="cat.id"
              >
                {{ cat.name }}
              </option>
            </select>
          </div>
          <div class="form-group col">
            <label for="edit-art-tags">Tags</label>
            <input
              id="edit-art-tags"
              v-model="editDraftForm.tagsInput"
              type="text"
              class="form-control"
            />
          </div>
        </div>

        <div class="form-group">
          <div class="textarea-label-row">
            <label for="edit-art-body">Markdown SOP Content *</label>
            <button
              type="button"
              class="btn-text-toggle"
              @click="isPreviewMode = !isPreviewMode"
            >
              {{
                isPreviewMode ? "👁️ Show Raw Markdown" : "🔍 Show Live Preview"
              }}
            </button>
          </div>

          <div
            v-if="isPreviewMode"
            class="preview-container prose-content"
            v-html="renderPreviewHtml(editDraftForm.body_markdown)"
          />
          <textarea
            v-else
            id="edit-art-body"
            v-model="editDraftForm.body_markdown"
            class="form-control code-textarea"
            rows="10"
            required
          />
        </div>

        <div class="form-group">
          <label for="edit-art-reason">GxP Reason for Change *</label>
          <input
            id="edit-art-reason"
            v-model="editDraftForm.reason_for_change"
            type="text"
            class="form-control"
            placeholder="Updated procedural guidelines and regulatory notes"
            required
          />
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isEditDraftModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">
            Save Draft Changes
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Modal 3: Part 11 Action Justification / Reason Modal -->
    <ClinicalModal
      v-model="isReasonModalOpen"
      :teleport="false"
      :show-footer="false"
      dialog-class="modal-card"
      aria-labelledby="reason-modal-title"
    >
      <template #header>
        <h3 id="reason-modal-title">
          {{ actionModalTitle }}
        </h3>
      </template>

      <form @submit.prevent="executeActionWithReason">
        <p class="reason-modal-intro">
          {{ actionModalIntro }}
        </p>

        <div v-if="actionModalType === 'publish'" class="form-group">
          <label for="action-version-label">Published Version Label *</label>
          <input
            id="action-version-label"
            v-model="actionVersionLabel"
            type="text"
            class="form-control"
            placeholder="e.g. 1.0 or 2.0"
            required
          />
        </div>

        <div class="form-group">
          <label for="action-reason-input"
            >21 CFR Part 11 Reason for Change *</label
          >
          <textarea
            id="action-reason-input"
            v-model="actionReason"
            class="form-control"
            rows="3"
            placeholder="Mandatory regulatory reason for executing this document state change..."
            required
          />
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isReasonModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn" :class="actionButtonClass">
            {{ actionButtonLabel }}
          </button>
        </div>
      </form>
    </ClinicalModal>

    <!-- Modal 4: Create Category Modal -->
    <ClinicalModal
      v-model="isCreateCategoryModalOpen"
      :teleport="false"
      :show-footer="false"
      dialog-class="modal-card"
      aria-labelledby="create-cat-modal-title"
    >
      <template #header>
        <h3 id="create-cat-modal-title">📁 Create Knowledge Category</h3>
      </template>

      <form @submit.prevent="handleCreateCategory">
        <div class="form-group">
          <label for="new-cat-name">Category Name *</label>
          <input
            id="new-cat-name"
            v-model="newCategoryForm.name"
            type="text"
            class="form-control"
            placeholder="e.g. Safety & Pharmacovigilance"
            required
            @input="autoGenerateCategorySlug"
          />
        </div>

        <div class="form-group">
          <label for="new-cat-slug">Category Slug *</label>
          <input
            id="new-cat-slug"
            v-model="newCategoryForm.slug"
            type="text"
            class="form-control"
            placeholder="safety-pharmacovigilance"
            required
          />
        </div>

        <div class="form-group">
          <label for="new-cat-desc">Description</label>
          <input
            id="new-cat-desc"
            v-model="newCategoryForm.description"
            type="text"
            class="form-control"
            placeholder="Guidelines for safety event intake, triage, and expedited reporting."
          />
        </div>

        <fieldset class="form-group form-fieldset">
          <legend class="form-label">
            Persona Visibility (All by default if empty)
          </legend>
          <div class="persona-checkbox-grid">
            <label
              v-for="p in PERSONA_PRESETS"
              :key="p.key"
              class="checkbox-label"
            >
              <input
                v-model="newCategoryForm.persona_visibility"
                type="checkbox"
                :value="p.key"
              />
              <span>{{ p.label }}</span>
            </label>
          </div>
        </fieldset>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="isCreateCategoryModalOpen = false"
          >
            Cancel
          </button>
          <button type="submit" class="btn btn-primary">Create Category</button>
        </div>
      </form>
    </ClinicalModal>
  </div>
</template>

<script>
import { computed, onMounted, ref, watch } from "vue";
import { ClinicalIconButton, ClinicalModal, ClinicalTabs } from "ui";
import {
  knowledgeService,
  DEFAULT_CATEGORIES,
  DEFAULT_MOCK_ARTICLES,
} from "../api/knowledge.js";
import {
  readRouteScopeQuery,
  replaceRouteQueryParams,
  resolveRouteAndRouter,
} from "../composables/useRouteScopeSync";
import { useAuthStore, PERSONA_PRESETS } from "../stores/auth.js";
import { useClinicalScopeStore } from "../stores/clinicalScope";
import { useToastStore } from "../stores/toasts.js";
import { isDemoBuild } from "../utils/demoMode.js";

export default {
  name: "KnowledgeView",
  components: {
    ClinicalIconButton,
    ClinicalModal,
    ClinicalTabs,
  },
  setup() {
    const authStore = useAuthStore();
    const scopeStore = useClinicalScopeStore();
    const toastStore = useToastStore();
    const { route, router } = resolveRouteAndRouter();
    const initialRouteScope = readRouteScopeQuery(route);

    // Data State: bundled sample SOPs only in a demo build; otherwise the list comes from the server (#4523).
    const demoBuild = isDemoBuild();
    const categories = ref(demoBuild ? [...DEFAULT_CATEGORIES] : []);
    const articles = ref(demoBuild ? [...DEFAULT_MOCK_ARTICLES] : []);
    const selectedCategoryId = ref(null);
    const selectedArticle = ref(
      demoBuild ? DEFAULT_MOCK_ARTICLES[0] || null : null
    );
    const searchQuery = ref("");
    const selectedTag = ref(null);
    const activeStatusFilter = ref("ALL");
    const activeReaderTab = ref("content");
    const loading = ref(false);
    const scopeStudyId = ref(initialRouteScope.studyId);
    const scopeSiteId = ref(initialRouteScope.siteId);
    const scopeSubjectId = ref(initialRouteScope.subjectId);

    if (route?.query) {
      if (initialRouteScope.studyId) {
        scopeStore.activeStudyId = initialRouteScope.studyId;
      }
      if (initialRouteScope.siteId) {
        scopeStore.activeSiteId = initialRouteScope.siteId;
      }
      if (initialRouteScope.subjectId) {
        scopeStore.activeSubjectId = initialRouteScope.subjectId;
      }
    }

    // Version & Audit Log Lists for Tab Viewers
    const versionsList = ref([]);
    const versionsLoading = ref(false);
    const auditLogList = ref([]);
    const auditLoading = ref(false);

    // Modals
    const isCreateArticleModalOpen = ref(false);
    const isEditDraftModalOpen = ref(false);
    const isReasonModalOpen = ref(false);
    const isCreateCategoryModalOpen = ref(false);
    const isPreviewMode = ref(false);

    // Forms
    const newArticleForm = ref({
      title: "",
      slug: "",
      category_id: "",
      version_label: "0.1",
      tagsInput: "",
      body_markdown: "",
      reason_for_change: "Initial SOP document draft creation",
    });

    const editDraftForm = ref({
      title: "",
      category_id: "",
      tagsInput: "",
      body_markdown: "",
      reason_for_change: "",
    });

    const newCategoryForm = ref({
      name: "",
      slug: "",
      description: "",
      persona_visibility: [],
    });

    // Reason Modal Parameters
    const actionModalType = ref("");
    const actionModalTitle = ref("");
    const actionModalIntro = ref("");
    const actionReason = ref("");
    const actionVersionLabel = ref("");
    const actionButtonLabel = ref("Confirm");
    const actionButtonClass = ref("btn-primary");

    // Persona & Role Checking
    const activePersonaKey = computed(
      () => authStore.currentPersona || "super_admin"
    );
    const activePersonaLabel = computed(() => {
      const p = PERSONA_PRESETS.find(
        (preset) => preset.key === activePersonaKey.value
      );
      return p ? p.label : "Active Role";
    });

    const isSuperAdmin = computed(() => {
      const current = authStore.currentPersona;
      if (current && current !== "super_admin") {
        return false;
      }
      const roles = authStore.normalizedRoles || [];
      return (
        current === "super_admin" ||
        roles.includes("super_admin") ||
        roles.includes("sponsor_admin")
      );
    });

    // Filtered Categories based on Persona
    const visibleCategories = computed(() => {
      if (isSuperAdmin.value) {
        return categories.value.filter((c) => !c.is_deleted);
      }
      const role = activePersonaKey.value;
      return categories.value.filter((c) => {
        if (c.is_deleted) return false;
        if (!c.persona_visibility || c.persona_visibility.length === 0)
          return true;
        return (
          c.persona_visibility.includes(role) ||
          c.persona_visibility.includes("all")
        );
      });
    });

    // Filtered Articles based on Persona (via Category visibility)
    const filteredByPersonaArticles = computed(() => {
      if (isSuperAdmin.value) {
        return articles.value;
      }
      const allowedCatIds = new Set(visibleCategories.value.map((c) => c.id));
      return articles.value.filter((a) => allowedCatIds.has(a.category_id));
    });

    // Filtered Articles by Category, Status, Tag, and Search
    const displayArticles = computed(() => {
      let list = [...filteredByPersonaArticles.value];

      // Category filter
      if (selectedCategoryId.value) {
        list = list.filter((a) => a.category_id === selectedCategoryId.value);
      }

      // Status filter
      if (activeStatusFilter.value !== "ALL") {
        list = list.filter((a) => a.status === activeStatusFilter.value);
      }

      // Tag filter
      if (selectedTag.value) {
        list = list.filter((a) => {
          const tags = getTagList(a.tags);
          return tags.includes(selectedTag.value);
        });
      }

      // Search Query
      const q = searchQuery.value.trim().toLowerCase();
      if (q) {
        list = list.filter((a) => {
          const matchTitle = a.title?.toLowerCase().includes(q);
          const matchSlug = a.slug?.toLowerCase().includes(q);
          const matchBody = (a.body_markdown || a.body_html || "")
            .toLowerCase()
            .includes(q);
          const tags = getTagList(a.tags);
          const matchTags = tags.some((t) => t.toLowerCase().includes(q));
          return matchTitle || matchSlug || matchBody || matchTags;
        });
      }

      // Clinical study scope filter when articles carry an explicit study_id
      const scopedStudy = scopeStudyId.value.trim().toLowerCase();
      if (scopedStudy) {
        list = list.filter(
          (a) => !a.study_id || a.study_id.toLowerCase() === scopedStudy
        );
      }

      return list;
    });

    // Aggregations / Metrics
    const totalArticlesCount = computed(
      () => filteredByPersonaArticles.value.length
    );
    const publishedArticlesCount = computed(
      () =>
        filteredByPersonaArticles.value.filter((a) => a.status === "PUBLISHED")
          .length
    );
    const inReviewArticlesCount = computed(
      () =>
        filteredByPersonaArticles.value.filter((a) => a.status === "IN_REVIEW")
          .length
    );
    const draftArticlesCount = computed(
      () =>
        filteredByPersonaArticles.value.filter(
          (a) => a.status === "DRAFT" || a.status === "REJECTED"
        ).length
    );

    // Available Tags
    const availableTags = computed(() => {
      const tagSet = new Set();
      filteredByPersonaArticles.value.forEach((a) => {
        getTagList(a.tags).forEach((t) => tagSet.add(t));
      });
      return Array.from(tagSet).sort();
    });

    // Helper functions
    function getTagList(tags) {
      if (!tags) return [];
      if (Array.isArray(tags)) return tags;
      if (typeof tags === "string") {
        return tags
          .split(",")
          .map((t) => t.trim())
          .filter(Boolean);
      }
      return [];
    }

    function getCategoryIcon(slug) {
      if (!slug) return "📁";
      if (slug.includes("clinical") || slug.includes("ecrf")) return "🩺";
      if (slug.includes("authoring") || slug.includes("mdr")) return "📋";
      if (slug.includes("monitoring") || slug.includes("ctms")) return "📊";
      if (
        slug.includes("regulatory") ||
        slug.includes("compliance") ||
        slug.includes("audit")
      )
        return "🔒";
      if (slug.includes("data") || slug.includes("coding")) return "🏷️";
      return "📁";
    }

    function getArticleCountForCategory(catId) {
      return filteredByPersonaArticles.value.filter(
        (a) => a.category_id === catId
      ).length;
    }

    function getStatusPillClass(status) {
      switch (status) {
        case "PUBLISHED":
          return "status-published";
        case "IN_REVIEW":
          return "status-review";
        case "APPROVED":
          return "status-approved";
        case "DRAFT":
          return "status-draft";
        case "REJECTED":
          return "status-rejected";
        case "SUPERSEDED":
          return "status-superseded";
        case "ARCHIVED":
          return "status-archived";
        default:
          return "status-draft";
      }
    }

    function formatDate(isoStr) {
      if (!isoStr) return "—";
      try {
        const d = new Date(isoStr);
        return d.toLocaleDateString("en-US", {
          year: "numeric",
          month: "short",
          day: "numeric",
        });
      } catch {
        return isoStr;
      }
    }

    function renderPreviewHtml(markdown) {
      if (!markdown) return "<p>No content to preview.</p>";
      let html = markdown
        .replace(/^### (.*$)/gim, "<h3>$1</h3>")
        .replace(/^## (.*$)/gim, "<h2>$1</h2>")
        .replace(/^# (.*$)/gim, "<h1>$1</h1>")
        .replace(/\*\*(.*)\*\*/gim, "<strong>$1</strong>")
        .replace(/\*(.*)\*/gim, "<em>$1</em>")
        .replace(/`([^`]+)`/gim, "<code>$1</code>")
        .replace(/^\* (.*$)/gim, "<li>$1</li>")
        .replace(/^\d+\. (.*$)/gim, "<li>$1</li>")
        .replace(/\n\n+/g, "</p><p>")
        .replace(/\n/g, "<br>");
      return `<p>${html}</p>`;
    }

    // Actions & Selection
    function selectCategory(catId) {
      selectedCategoryId.value = catId;
      if (
        selectedArticle.value &&
        catId &&
        selectedArticle.value.category_id !== catId
      ) {
        const firstMatch = displayArticles.value[0] || null;
        selectedArticle.value = firstMatch;
      }
    }

    function selectArticle(article) {
      selectedArticle.value = article;
      activeReaderTab.value = "content";
      versionsList.value = [];
      auditLogList.value = [];
    }

    function setStatusFilter(status) {
      activeStatusFilter.value = status;
    }

    function toggleTag(tag) {
      if (selectedTag.value === tag) {
        selectedTag.value = null;
      } else {
        selectedTag.value = tag;
      }
    }

    function onSearchInput() {
      // Live search is reactive via displayArticles computed
    }

    function clearSearch() {
      searchQuery.value = "";
    }

    function resetFilters() {
      searchQuery.value = "";
      selectedTag.value = null;
      activeStatusFilter.value = "ALL";
      selectedCategoryId.value = null;
    }

    // Tab Loaders
    async function loadVersionHistory() {
      activeReaderTab.value = "versions";
      if (!selectedArticle.value) return;
      versionsLoading.value = true;
      try {
        const list = await knowledgeService.listArticleVersions(
          selectedArticle.value.id
        );
        versionsList.value = Array.isArray(list) ? list : [];
      } catch (err) {
        toastStore.apiError("Could not load version history", err);
        versionsList.value = [];
      } finally {
        versionsLoading.value = false;
      }
    }

    async function loadAuditTrail() {
      activeReaderTab.value = "audit";
      if (!selectedArticle.value) return;
      auditLoading.value = true;
      try {
        const list = await knowledgeService.getArticleAuditLog(
          selectedArticle.value.id
        );
        auditLogList.value = Array.isArray(list) ? list : [];
      } catch (err) {
        toastStore.apiError("Could not load audit trail", err);
        auditLogList.value = [];
      } finally {
        auditLoading.value = false;
      }
    }

    const readerTabs = computed(() => [
      {
        id: "content",
        label: "SOP Guidance",
        panelId: "sop-prose-viewer",
      },
      {
        id: "versions",
        label: `Version History (${versionsList.value.length})`,
        panelId: "sop-versions-viewer",
      },
      {
        id: "audit",
        label: `21 CFR Part 11 Audit Trail (${auditLogList.value.length})`,
        panelId: "sop-audit-viewer",
      },
    ]);

    function handleReaderTabChange(tabId) {
      if (tabId === "versions") {
        loadVersionHistory();
      } else if (tabId === "audit") {
        loadAuditTrail();
      } else {
        activeReaderTab.value = "content";
      }
    }

    // Lifecycle Modals & Triggers
    function openCreateArticleModal() {
      const defaultCat = visibleCategories.value[0]?.id || "cat-clinical-ops";
      newArticleForm.value = {
        title: "",
        slug: "",
        category_id: defaultCat,
        version_label: "0.1",
        tagsInput: "",
        body_markdown:
          "## Purpose & Scope\n\n### Responsibilities\n1. \n\n### Procedure\n1. ",
        reason_for_change: "Initial authoring of standard operating procedure",
      };
      isCreateArticleModalOpen.value = true;
    }

    function autoGenerateSlug() {
      if (newArticleForm.value.title) {
        newArticleForm.value.slug = newArticleForm.value.title
          .toLowerCase()
          .replace(/[^a-z0-9]+/g, "-")
          .replace(/^-+|-+$/g, "");
      }
    }

    function autoGenerateCategorySlug() {
      if (newCategoryForm.value.name) {
        newCategoryForm.value.slug = newCategoryForm.value.name
          .toLowerCase()
          .replace(/[^a-z0-9]+/g, "-")
          .replace(/^-+|-+$/g, "");
      }
    }

    async function handleCreateArticle() {
      try {
        if (!newArticleForm.value.slug && newArticleForm.value.title) {
          autoGenerateSlug();
        }
        const tags = newArticleForm.value.tagsInput
          ? newArticleForm.value.tagsInput
              .split(",")
              .map((t) => t.trim())
              .filter(Boolean)
          : [];
        const payload = {
          title: newArticleForm.value.title,
          slug:
            newArticleForm.value.slug ||
            newArticleForm.value.title
              .toLowerCase()
              .replace(/[^a-z0-9]+/g, "-"),
          category_id:
            newArticleForm.value.category_id ||
            visibleCategories.value[0]?.id ||
            "cat-clinical-ops",
          version_label: newArticleForm.value.version_label || "0.1",
          tags,
          body_markdown: newArticleForm.value.body_markdown,
          reason_for_change: newArticleForm.value.reason_for_change,
        };
        const created = await knowledgeService.createArticle(payload);
        articles.value.unshift(created);
        selectedArticle.value = created;
        isCreateArticleModalOpen.value = false;
      } catch (err) {
        toastStore.apiError("Could not create article", err);
      }
    }

    function openEditDraftModal() {
      if (!selectedArticle.value) return;
      const art = selectedArticle.value;
      editDraftForm.value = {
        title: art.title,
        category_id: art.category_id,
        tagsInput: getTagList(art.tags).join(", "),
        body_markdown: art.body_markdown || "",
        reason_for_change: `Updated draft for ${art.title}`,
      };
      isPreviewMode.value = false;
      isEditDraftModalOpen.value = true;
    }

    async function handleSaveDraft() {
      if (!selectedArticle.value) return;
      try {
        const tags = editDraftForm.value.tagsInput
          ? editDraftForm.value.tagsInput
              .split(",")
              .map((t) => t.trim())
              .filter(Boolean)
          : [];
        const payload = {
          title: editDraftForm.value.title,
          category_id: editDraftForm.value.category_id,
          tags,
          body_markdown: editDraftForm.value.body_markdown,
          reason_for_change: editDraftForm.value.reason_for_change,
        };
        const updated = await knowledgeService.updateArticleDraft(
          selectedArticle.value.id,
          payload
        );
        const idx = articles.value.findIndex(
          (a) => a.id === selectedArticle.value.id
        );
        if (idx !== -1) {
          articles.value[idx] = updated;
        }
        selectedArticle.value = updated;
        isEditDraftModalOpen.value = false;
      } catch (err) {
        toastStore.apiError("Could not save draft", err);
      }
    }

    function promptSubmitReview() {
      actionModalType.value = "submit_review";
      actionModalTitle.value = "📨 Submit Draft for Four-Eyes Review";
      actionModalIntro.value =
        "Submitting this working draft will transition the document to IN_REVIEW status. An independent peer reviewer must review and approve this revision.";
      actionReason.value =
        "Draft completed; submitted for formal quality & regulatory review.";
      actionButtonLabel.value = "Submit for Review";
      actionButtonClass.value = "btn-primary";
      isReasonModalOpen.value = true;
    }

    function promptApproveArticle() {
      actionModalType.value = "approve";
      actionModalTitle.value = "✓ Approve Article Revision";
      actionModalIntro.value =
        "Approving this article locks the working version snapshot as permanently immutable per 21 CFR Part 11 four-eyes principles.";
      actionReason.value =
        "Verified procedural accuracy and regulatory compliance.";
      actionButtonLabel.value = "Approve & Lock Version";
      actionButtonClass.value = "btn-success";
      isReasonModalOpen.value = true;
    }

    function promptRejectArticle() {
      actionModalType.value = "reject";
      actionModalTitle.value = "✕ Reject Article Revision";
      actionModalIntro.value =
        "Rejecting this draft returns it to REJECTED status. Please provide clear clinical or regulatory justification for rejection.";
      actionReason.value =
        "Protocol amendment alignment requires updates before approval.";
      actionButtonLabel.value = "Reject Draft";
      actionButtonClass.value = "btn-danger";
      isReasonModalOpen.value = true;
    }

    function promptPublishArticle() {
      actionModalType.value = "publish";
      actionModalTitle.value = "🚀 Publish Approved SOP Live";
      actionModalIntro.value =
        "Publishing will immediately make this document active across the platform and automatically supersede any previous published version.";
      actionReason.value =
        "Formal regulatory release for clinical site operations.";
      actionVersionLabel.value = selectedArticle.value?.version_label || "1.0";
      actionButtonLabel.value = "Publish Live";
      actionButtonClass.value = "btn-primary";
      isReasonModalOpen.value = true;
    }

    function promptArchiveArticle() {
      actionModalType.value = "archive";
      actionModalTitle.value = "📦 Archive Knowledge Article";
      actionModalIntro.value =
        "Archiving will deprecate this SOP document and remove it from active clinical workflows while retaining 21 CFR Part 11 audit records.";
      actionReason.value =
        "Replaced by updated master protocol and standard operating procedures.";
      actionButtonLabel.value = "Archive SOP";
      actionButtonClass.value = "btn-danger";
      isReasonModalOpen.value = true;
    }

    async function executeActionWithReason() {
      if (!selectedArticle.value) return;
      const artId = selectedArticle.value.id;
      const reason = actionReason.value;

      try {
        let updated = null;
        if (actionModalType.value === "submit_review") {
          updated = await knowledgeService.submitArticleReview(artId, {
            reason_for_change: reason,
          });
        } else if (actionModalType.value === "approve") {
          updated = await knowledgeService.approveArticle(artId, {
            reason_for_change: reason,
          });
        } else if (actionModalType.value === "reject") {
          updated = await knowledgeService.rejectArticle(artId, {
            reason_for_change: reason,
          });
        } else if (actionModalType.value === "publish") {
          updated = await knowledgeService.publishArticle(artId, {
            reason_for_change: reason,
            version_label: actionVersionLabel.value,
          });
        } else if (actionModalType.value === "archive") {
          updated = await knowledgeService.transitionArticle(artId, {
            target_status: "ARCHIVED",
            reason_for_change: reason,
          });
        }

        if (updated) {
          const idx = articles.value.findIndex((a) => a.id === artId);
          if (idx !== -1) {
            articles.value[idx] = updated;
          }
          selectedArticle.value = updated;
        }

        isReasonModalOpen.value = false;
      } catch (err) {
        toastStore.apiError("Could not update article status", err);
      }
    }

    function openCreateCategoryModal() {
      newCategoryForm.value = {
        name: "",
        slug: "",
        description: "",
        persona_visibility: [],
      };
      isCreateCategoryModalOpen.value = true;
    }

    async function handleCreateCategory() {
      try {
        if (!newCategoryForm.value.slug && newCategoryForm.value.name) {
          autoGenerateCategorySlug();
        }
        const payload = {
          name: newCategoryForm.value.name,
          slug:
            newCategoryForm.value.slug ||
            newCategoryForm.value.name
              .toLowerCase()
              .replace(/[^a-z0-9]+/g, "-"),
          description: newCategoryForm.value.description,
          persona_visibility: newCategoryForm.value.persona_visibility,
        };
        const created = await knowledgeService.createCategory(payload);
        categories.value.push(created);
        isCreateCategoryModalOpen.value = false;
      } catch (err) {
        toastStore.apiError("Could not create category", err);
      }
    }

    // Initial Data Fetch
    async function loadInitialData() {
      loading.value = true;
      try {
        const persona = activePersonaKey.value;
        const [cats, arts] = await Promise.all([
          knowledgeService.listCategories({ persona }),
          knowledgeService.listArticles({ persona }),
        ]);
        if (Array.isArray(cats)) categories.value = cats;
        if (Array.isArray(arts)) articles.value = arts;

        if (selectedArticle.value) {
          const updatedSelected = displayArticles.value.find(
            (a) => a.id === selectedArticle.value.id
          );
          selectedArticle.value =
            updatedSelected || displayArticles.value[0] || null;
        } else if (displayArticles.value.length > 0) {
          selectedArticle.value = displayArticles.value[0];
        }
      } catch (err) {
        toastStore.apiError("Could not load the knowledge base", err);
      } finally {
        loading.value = false;
      }
    }

    // Watchers
    watch(
      () => [
        route?.query?.studyId,
        route?.query?.siteId,
        route?.query?.site,
        route?.query?.subjectId,
        route?.query?.participant,
      ],
      () => {
        if (!route?.query) return;
        const nextScope = readRouteScopeQuery(route);
        scopeStudyId.value = nextScope.studyId;
        scopeSiteId.value = nextScope.siteId;
        scopeSubjectId.value = nextScope.subjectId;
        scopeStore.activeStudyId = nextScope.studyId;
        scopeStore.activeSiteId = nextScope.siteId;
        scopeStore.activeSubjectId = nextScope.subjectId;
      }
    );

    watch(
      [scopeStudyId, scopeSiteId, scopeSubjectId],
      ([nextStudy, nextSite, nextSubject]) => {
        const trimmedStudy = nextStudy.trim();
        const trimmedSite = nextSite.trim();
        const trimmedSubject = nextSubject.trim();
        scopeStore.activeStudyId = trimmedStudy;
        scopeStore.activeSiteId = trimmedSite;
        scopeStore.activeSubjectId = trimmedSubject;
        void replaceRouteQueryParams(router, route, {
          studyId: trimmedStudy || null,
          siteId: trimmedSite || null,
          subjectId: trimmedSubject || null,
        });
      }
    );

    watch(
      () => activePersonaKey.value,
      async () => {
        if (!demoBuild) {
          await loadInitialData();
          return;
        }
        if (selectedArticle.value) {
          const isStillVisible = displayArticles.value.some(
            (a) => a.id === selectedArticle.value.id
          );
          if (!isStillVisible) {
            selectedArticle.value = displayArticles.value[0] || null;
          }
        }
      }
    );

    onMounted(() => {
      loadInitialData();
    });

    return {
      // State
      demoBuild,
      categories,
      articles,
      selectedCategoryId,
      selectedArticle,
      searchQuery,
      selectedTag,
      activeStatusFilter,
      activeReaderTab,
      loading,
      versionsList,
      versionsLoading,
      auditLogList,
      auditLoading,
      scopeStudyId,
      scopeSiteId,
      scopeSubjectId,

      // Modals & Forms
      isCreateArticleModalOpen,
      isEditDraftModalOpen,
      isReasonModalOpen,
      isCreateCategoryModalOpen,
      isPreviewMode,
      newArticleForm,
      editDraftForm,
      newCategoryForm,
      actionModalType,
      actionModalTitle,
      actionModalIntro,
      actionReason,
      actionVersionLabel,
      actionButtonLabel,
      actionButtonClass,

      // Computeds
      PERSONA_PRESETS,
      activePersonaKey,
      activePersonaLabel,
      isSuperAdmin,
      visibleCategories,
      filteredByPersonaArticles,
      displayArticles,
      totalArticlesCount,
      publishedArticlesCount,
      inReviewArticlesCount,
      draftArticlesCount,
      availableTags,
      readerTabs,

      // Methods
      getTagList,
      getCategoryIcon,
      getArticleCountForCategory,
      getStatusPillClass,
      formatDate,
      renderPreviewHtml,
      selectCategory,
      selectArticle,
      setStatusFilter,
      toggleTag,
      onSearchInput,
      clearSearch,
      resetFilters,
      loadVersionHistory,
      loadAuditTrail,
      handleReaderTabChange,
      openCreateArticleModal,
      autoGenerateSlug,
      autoGenerateCategorySlug,
      handleCreateArticle,
      openEditDraftModal,
      handleSaveDraft,
      promptSubmitReview,
      promptApproveArticle,
      promptRejectArticle,
      promptPublishArticle,
      promptArchiveArticle,
      executeActionWithReason,
      openCreateCategoryModal,
      handleCreateCategory,
    };
  },
};
</script>

<style scoped>
/* ==========================================================================
   Knowledge Hub Browser View (Vanilla CSS & System Tokens)
   ========================================================================== */

.dashboard-section {
  display: flex;
  flex-direction: column;
  gap: 20px;
  width: 100%;
  max-width: 1600px;
  margin: 0 auto;
  padding: 8px 16px 32px 16px;
}

/* Header Architecture */
.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
  border-bottom: 1px solid var(--border);
  padding-bottom: 16px;
}

.header-content-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.header-badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 2px;
}

.view-title {
  font-size: 1.6rem;
  font-weight: 800;
  letter-spacing: -0.02em;
  color: var(--primary);
  margin: 0;
}

.view-subtitle {
  font-size: 0.88rem;
  color: var(--neutral-dark);
  max-width: 900px;
  line-height: 1.4;
  margin: 0;
}

.header-action-group {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-shrink: 0;
}

/* Metric KPI Summary Grid */
.stats-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.stat-card {
  width: 100%;
  font: inherit;
  color: inherit;
  text-align: left;
  display: flex;
  align-items: center;
  gap: 14px;
  background-color: var(--neutral-light);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 14px 18px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.stat-card:hover {
  transform: translateY(-2px);
  border-color: var(--accent);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.05);
}

.stat-card-active {
  border-color: var(--accent);
  background-color: var(--semantic-color-info-bg-soft);
  box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.2);
}

.stat-icon {
  font-size: 1.6rem;
  width: 44px;
  height: 44px;
  border-radius: 8px;
  display: flex;
  align-items: center;
  justify-content: center;
  background-color: rgba(255, 255, 255, 0.8);
  border: 1px solid var(--border);
  flex-shrink: 0;
}

.stat-details {
  display: flex;
  flex-direction: column;
}

.stat-value {
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--primary);
  line-height: 1.1;
}

.stat-label {
  font-size: 0.78rem;
  color: var(--neutral-dark);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.03em;
  margin-top: 2px;
}

.text-success {
  color: var(--semantic-color-success);
}

.text-warning {
  color: var(--semantic-color-warning);
}

.text-danger {
  color: var(--semantic-color-error);
}

/* 3-Column Knowledge Workspace */
.knowledge-layout {
  display: grid;
  grid-template-columns: 260px 360px 1fr;
  gap: 16px;
  min-height: 700px;
  align-items: stretch;
}

/* Column 1: Category Sidebar */
.knowledge-sidebar {
  background-color: var(--neutral-light);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.sidebar-box-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--border);
  padding-bottom: 8px;
}

.sidebar-heading {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--primary);
  margin: 0;
  display: flex;
  align-items: center;
  gap: 6px;
}

.badge-persona-filter {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-info-border-accent);
}

.category-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  flex: 1;
}

.category-item-btn {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  width: 100%;
  text-align: left;
  background-color: transparent;
  border: 1px solid transparent;
  border-radius: 6px;
  padding: 8px 10px;
  cursor: pointer;
  transition: all 0.15s ease;
  color: var(--primary);
}

.category-item-btn:hover {
  background-color: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
}

.category-item-btn.active {
  background-color: var(--semantic-color-primary-light);
  border-color: var(--semantic-color-divider-accent);
  color: var(--semantic-color-primary);
  font-weight: 600;
}

.category-icon {
  font-size: 1.1rem;
  line-height: 1.2;
}

.category-text-block {
  flex: 1;
  display: flex;
  flex-direction: column;
}

.category-title {
  font-size: 0.85rem;
  font-weight: 600;
}

.category-desc {
  font-size: 0.7rem;
  color: var(--neutral-dark);
  margin-top: 2px;
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
}

.category-count {
  font-size: 0.72rem;
  font-weight: 700;
  background-color: var(--semantic-color-border-muted);
  color: var(--semantic-color-text-body);
  padding: 2px 6px;
  border-radius: 10px;
  align-self: center;
}

.category-item-btn.active .category-count {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.sidebar-filter-section {
  border-top: 1px solid var(--border);
  padding-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.filter-label {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--neutral-dark);
  text-transform: uppercase;
  letter-spacing: 0.02em;
}

.knowledge-scope-input {
  font-family: var(--font-mono);
}

/* Column 2: Article Directory Panel */
.article-list-panel {
  background-color: var(--neutral-light);
  border: 1px solid var(--border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.list-search-bar {
  padding: 12px;
  border-bottom: 1px solid var(--border);
  background-color: var(--semantic-color-surface);
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.search-input-wrapper {
  position: relative;
  display: flex;
  align-items: center;
}

.search-symbol {
  position: absolute;
  left: 10px;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.knowledge-search-input {
  width: 100%;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 8px 32px 8px 32px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.82rem;
  transition: border-color 0.15s;
  box-sizing: border-box;
}

.knowledge-search-input:focus-visible {
  border-color: var(--accent);
}

.btn-clear-search {
  position: absolute;
  right: 6px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background: none;
  border: none;
  border-radius: 4px;
  cursor: pointer;
  color: var(--neutral-dark);
  font-size: 0.75rem;
  padding: 2px 4px;
  box-sizing: border-box;
}

.tag-chips-scroll {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  max-height: 68px;
  overflow-y: auto;
  padding: 4px 2px;
}

.tag-chip {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 12px;
  padding: 3px 10px;
  font-size: 0.72rem;
  font-weight: 600;
  color: var(--semantic-color-text);
  cursor: pointer;
  transition: all 0.15s;
  box-sizing: border-box;
}

.tag-chip:hover {
  background-color: var(--semantic-color-surface-header);
}

.tag-chip-active {
  background-color: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.articles-stream {
  flex: 1;
  overflow-y: auto;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  max-height: calc(100vh - 280px);
}

.article-card {
  width: 100%;
  font: inherit;
  color: inherit;
  text-align: left;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 12px;
  cursor: pointer;
  transition: all 0.15s ease;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.article-card:hover {
  border-color: var(--accent);
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.04);
}

.stat-card:focus-visible,
.tag-chips-scroll:focus-visible,
.article-card:focus-visible,
.articles-stream:focus-visible,
.reader-body-card:focus-visible {
  outline: 2px solid var(--accent);
  outline-offset: 2px;
}

.article-card-selected {
  border-color: var(--accent);
  background-color: var(--semantic-color-surface-muted);
  box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.2);
}

.article-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.article-card-title {
  display: block;
  font-size: 0.88rem;
  font-weight: 700;
  color: var(--primary);
  line-height: 1.3;
  margin: 0;
}

.article-card-meta {
  display: flex;
  justify-content: space-between;
  font-size: 0.72rem;
  color: var(--neutral-dark);
}

.article-card-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin-top: 2px;
}

.mini-tag {
  font-size: 0.65rem;
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border);
  border-radius: 4px;
  padding: 1px 5px;
  color: var(--semantic-color-text-secondary);
}

.articles-loading-state,
.articles-empty-state,
.reader-empty-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 40px 20px;
  color: var(--neutral-dark);
  gap: 10px;
  flex: 1;
}

.empty-icon {
  font-size: 2.2rem;
  margin-bottom: 4px;
}

/* Column 3: Article Reader Pane */
.article-reader-pane {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.reader-content-wrap {
  display: flex;
  flex-direction: column;
  height: 100%;
}

.reader-header-card {
  padding: 18px 24px;
  border-bottom: 1px solid var(--border);
  background-color: var(--semantic-color-surface);
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.reader-meta-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.breadcrumbs {
  font-size: 0.78rem;
  color: var(--neutral-dark);
  display: flex;
  align-items: center;
  gap: 6px;
}

.crumb-separator {
  color: var(--semantic-color-text-secondary);
}

.crumb-slug {
  font-family: monospace;
  background-color: var(--semantic-color-surface-header);
  padding: 1px 6px;
  border-radius: 4px;
}

.reader-badges {
  display: flex;
  align-items: center;
  gap: 8px;
}

.reader-article-title {
  font-size: 1.35rem;
  font-weight: 800;
  color: var(--primary);
  margin: 0;
  line-height: 1.3;
}

.reader-author-row {
  display: flex;
  flex-wrap: wrap;
  gap: 18px;
  font-size: 0.78rem;
  color: var(--neutral-dark);
  border-top: 1px solid var(--semantic-color-surface-header);
  padding-top: 8px;
}

.author-item {
  display: flex;
  align-items: center;
  gap: 4px;
}

.author-label {
  font-weight: 600;
}

.author-value {
  color: var(--primary);
}

.reader-tags-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.tag-badge {
  font-size: 0.72rem;
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-info-border-accent);
  border-radius: 4px;
  padding: 2px 8px;
  font-weight: 500;
}

/* Admin Lifecycle Toolbar */
.admin-lifecycle-toolbar {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 8px 12px;
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
  margin-top: 4px;
}

.toolbar-label {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
}

/* Reader Tab Navigation */
.reader-tab-nav {
  display: flex;
  gap: 4px;
  background-color: var(--semantic-color-surface-header);
  border-bottom: 1px solid var(--border);
  padding: 0 16px;
}

.reader-tab-btn {
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  padding: 10px 16px;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--neutral-dark);
  cursor: pointer;
  transition: all 0.15s;
}

.reader-tab-btn:hover {
  color: var(--primary);
}

.reader-tab-btn.active {
  color: var(--semantic-color-primary);
  border-bottom-color: var(--semantic-color-primary);
  background-color: var(--semantic-color-surface);
}

.reader-body-card {
  padding: 24px;
  flex: 1;
  overflow-y: auto;
  background-color: var(--semantic-color-surface);
}

.tab-section-header {
  margin-bottom: 16px;
}

.tab-section-header h4 {
  font-size: 1.05rem;
  font-weight: 700;
  color: var(--primary);
  margin: 0 0 4px 0;
}

.section-desc {
  font-size: 0.8rem;
  color: var(--neutral-dark);
  margin: 0;
}

/* Prose Typography */
.prose-content {
  font-size: 0.92rem;
  line-height: 1.7;
  color: var(--semantic-color-text-dark);
  max-width: 860px;
}

.prose-content h1,
.prose-content h2,
.prose-content h3,
.prose-content h4 {
  color: var(--semantic-color-text);
  margin-top: 1.4em;
  margin-bottom: 0.6em;
  font-weight: 700;
}

.prose-content h1 {
  font-size: 1.4rem;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 6px;
}

.prose-content h2 {
  font-size: 1.2rem;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  padding-bottom: 4px;
}

.prose-content h3 {
  font-size: 1.05rem;
}

.prose-content p {
  margin-bottom: 1em;
}

.prose-content ul,
.prose-content ol {
  margin-left: 20px;
  margin-bottom: 1em;
}

.prose-content li {
  margin-bottom: 6px;
}

.prose-content code {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.85em;
  font-family: monospace;
}

.prose-content pre {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  padding: 14px;
  border-radius: 6px;
  overflow-x: auto;
  font-size: 0.85rem;
  margin-bottom: 1em;
}

.markdown-fallback pre {
  white-space: pre-wrap;
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-body);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 16px;
  font-family: inherit;
}

/* Status Pills */
.status-pill {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 12px;
  font-size: 0.72rem;
  font-weight: 700;
  letter-spacing: 0.02em;
  text-transform: uppercase;
}

.status-pill-lg {
  font-size: 0.78rem;
  padding: 4px 10px;
}

.status-published {
  background-color: var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border);
}

.status-review {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
  border: 1px solid var(--semantic-color-warning-border);
}

.status-approved {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-info-border-accent);
}

.status-draft {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.status-rejected {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  border: 1px solid var(--semantic-color-error-border-soft);
}

.status-superseded {
  background-color: var(--semantic-color-orange-bg);
  color: var(--semantic-color-orange-text);
  border: 1px solid var(--semantic-color-orange-border);
}

.status-archived {
  background-color: var(--semantic-color-purple-bg);
  color: var(--semantic-color-purple-text);
  border: 1px solid var(--semantic-color-purple-border);
}

.badge-version {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-body);
  border: 1px solid var(--semantic-color-border-control);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.72rem;
  font-weight: 600;
  font-family: monospace;
}

.badge-version-lg {
  font-size: 0.8rem;
  padding: 3px 8px;
}

.badge-neutral {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

/* Data Tables (Versions & Audit Log) */
.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
  text-align: left;
}

.data-table th {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  font-weight: 700;
  padding: 10px 12px;
  border-bottom: 2px solid var(--border);
}

.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--border);
  color: var(--primary);
}

.data-table tr:hover td {
  background-color: var(--semantic-color-surface-muted);
}

.hash-code {
  font-family: monospace;
  font-size: 0.75rem;
  background-color: var(--semantic-color-surface-header);
  padding: 2px 5px;
  border-radius: 4px;
}

/* Modals */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background-color: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(2px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 16px;
}

.modal-card {
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
  width: 100%;
  max-width: 580px;
  display: flex;
  flex-direction: column;
  max-height: 90vh;
  overflow: hidden;
}

.modal-lg {
  max-width: 820px;
}

.modal-header {
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-header h3 {
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--primary);
  margin: 0;
}

.modal-close-btn {
  background: none;
  border: none;
  font-size: 1.1rem;
  color: var(--neutral-dark);
  cursor: pointer;
}

.modal-body {
  padding: 20px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.modal-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  display: flex;
  justify-content: flex-end;
  gap: 10px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--primary);
}

.form-row {
  display: flex;
  gap: 12px;
}

.form-row .col {
  flex: 1;
}

.form-control {
  width: 100%;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.85rem;
  box-sizing: border-box;
}

.form-control:focus-visible {
  border-color: var(--accent);
}

.code-textarea {
  font-family: monospace;
  font-size: 0.85rem;
  line-height: 1.4;
}

.textarea-label-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.btn-text-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 2px 6px;
  border-radius: 4px;
  background: none;
  border: none;
  color: var(--accent);
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  box-sizing: border-box;
}

.preview-container {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 12px;
  min-height: 200px;
  max-height: 300px;
  overflow-y: auto;
  background-color: var(--semantic-color-surface-muted);
}

.persona-checkbox-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  margin-top: 4px;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  cursor: pointer;
}

.reason-modal-intro {
  font-size: 0.85rem;
  color: var(--neutral-dark);
  margin-bottom: 6px;
}

/* Common Buttons */
.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn-sm {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 5px 10px;
  font-size: 0.75rem;
}

.btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover {
  background-color: var(--semantic-color-primary);
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  color: var(--primary);
  border-color: var(--border);
}

.btn-secondary:hover {
  background-color: var(--semantic-color-surface-header);
}

.btn-success {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.btn-success:hover {
  background-color: var(--semantic-color-success);
}

.btn-danger {
  background-color: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.btn-danger:hover {
  background-color: var(--semantic-color-error);
}

.spinner {
  width: 24px;
  height: 24px;
  border: 3px solid var(--semantic-color-border);
  border-top-color: var(--semantic-color-primary);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

/* Responsive adjustments */
@media (max-width: 1100px) {
  .knowledge-layout {
    grid-template-columns: 220px 1fr;
  }
  .knowledge-sidebar {
    grid-column: 1 / -1;
  }
}

.form-fieldset {
  border: none;
  padding: 0;
  margin: 0;
  min-width: 0;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}
</style>
