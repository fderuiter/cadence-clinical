<template>
  <div
    id="study-canvas"
    class="canvas"
    :class="{ 'canvas-in-shell': inAppShell }"
    :style="{ '--canvas-topbar-height': `${topbarHeight}px` }"
  >
    <a v-if="!inAppShell" class="skip-link" href="#canvas-workspace"
      >Skip to workspace</a
    >

    <component
      :is="inAppShell ? 'div' : 'header'"
      ref="topbarRef"
      class="topbar"
      :role="inAppShell ? 'region' : undefined"
      :aria-label="inAppShell ? 'Study context' : undefined"
    >
      <nav id="canvas-zoom-path" class="zoom-path" aria-label="Zoom path">
        <ol>
          <li>
            <router-link id="zoom-studies" :to="{ name: 'study' }">
              Studies
            </router-link>
          </li>
          <li v-if="studyCode">
            <router-link
              id="zoom-study"
              :to="studyLocation()"
              :aria-current="zoomCurrent === 'study' ? 'page' : undefined"
            >
              {{ studyCode }}
            </router-link>
          </li>
          <li v-if="zoomSite">
            <router-link
              id="zoom-site"
              :to="
                studyLocation({
                  stage: 'conduct',
                  section: 'participants',
                  site: zoomSite,
                })
              "
              :aria-current="zoomCurrent === 'site' ? 'page' : undefined"
            >
              Site {{ zoomSite }}
            </router-link>
          </li>
          <li v-if="zoomParticipantCode">
            <span
              v-if="zoomCurrent === 'participant'"
              id="zoom-participant"
              aria-current="page"
              >{{ zoomParticipantCode }}</span
            >
            <router-link
              v-else
              id="zoom-participant"
              :to="zoomParticipantLocation"
            >
              {{ zoomParticipantCode }}
            </router-link>
          </li>
          <li v-if="hasOpenForm">
            <span
              v-if="zoomCurrent === 'form'"
              id="zoom-form"
              aria-current="page"
              >{{ zoomFormLabel }}</span
            >
            <router-link v-else id="zoom-form" :to="zoomFormLocation">
              {{ zoomFormLabel }}
            </router-link>
          </li>
          <li v-if="openObservationRef">
            <span id="zoom-observation" aria-current="page">{{
              zoomObservationLabel
            }}</span>
          </li>
        </ol>
      </nav>

      <button
        v-if="!inAppShell"
        id="btn-open-palette"
        type="button"
        class="palette-trigger"
        aria-keyshortcuts="Meta+K Control+K"
        @click="paletteOpen = true"
      >
        <span>Go to…</span>
        <kbd>⌘K</kbd>
      </button>

      <button
        id="btn-ai-assistance"
        type="button"
        class="ai-toggle"
        :aria-pressed="aiEnabled"
        aria-describedby="ai-assistance-note"
        @click="setAiEnabled(!aiEnabled)"
      >
        AI assistance: {{ aiEnabled ? "on" : "off" }}
      </button>
      <span id="ai-assistance-note" class="sr-only">
        Shows or hides Assist and the palette's Ask entry. Proposals an agent
        already made still wait for a decision in Needs attention.
      </span>

      <div v-if="!inAppShell" class="user">
        <span id="canvas-user">{{
          authStore.identity?.username || "Signed in"
        }}</span>
        <button
          id="btn-canvas-logout"
          type="button"
          class="btn btn-secondary"
          @click="requestCanvasSignOut"
        >
          Sign out
        </button>
      </div>
    </component>

    <template v-if="studyCode">
      <div class="study-header">
        <h1 id="canvas-study-title" tabindex="-1">
          {{ studyCode }}
        </h1>
        <nav aria-label="Lifecycle">
          <ol class="lifecycle-rail">
            <li v-for="item in STAGES" :key="item.id">
              <router-link
                :id="`stage-${item.id}`"
                :to="scopedStudyLocation({ stage: item.id })"
                class="stage"
                :class="{ current: stage?.id === item.id }"
                :aria-current="stage?.id === item.id ? 'step' : undefined"
              >
                {{ item.label }}
              </router-link>
            </li>
          </ol>
        </nav>
      </div>

      <div
        class="panes"
        :class="{ 'panes--inspector-collapsed': inspectorCollapsed }"
      >
        <nav id="canvas-navigator" class="navigator" aria-label="Sections">
          <router-link
            id="nav-study-home"
            :to="scopedStudyLocation()"
            class="nav-link"
            :class="{ current: !stage }"
            :aria-current="!stage ? 'page' : undefined"
          >
            Study home
          </router-link>
          <div
            v-for="group in navigatorStages"
            :key="group.id"
            class="nav-group"
          >
            <h2 class="nav-group-title">
              {{ group.label }}
            </h2>
            <ul v-if="group.sections.length">
              <li v-for="item in group.sections" :key="item.id">
                <router-link
                  :id="`nav-${item.id}`"
                  :to="
                    scopedStudyLocation({ stage: group.id, section: item.id })
                  "
                  class="nav-link"
                  :class="{ current: section?.id === item.id }"
                  :aria-current="section?.id === item.id ? 'page' : undefined"
                >
                  {{ item.label }}
                </router-link>
              </li>
            </ul>
            <p v-else class="nav-empty">
              No sections are available in this stage yet.
            </p>
          </div>
        </nav>

        <component
          :is="inAppShell ? 'div' : 'main'"
          id="canvas-workspace"
          class="workspace"
          :role="inAppShell ? 'region' : undefined"
          tabindex="-1"
          :aria-label="workspaceLabel"
        >
          <ProposalReview
            v-if="proposalRef"
            :key="proposalRef"
            :study-code="studyCode"
            :proposal-ref="proposalRef"
            :reviewer="authStore.userId ?? ''"
          />

          <ObservationView
            v-else-if="openObservationRef"
            :key="openObservationRef"
            embedded
            :study-code="studyCode"
            :observation-ref="openObservationRef"
            :return-to="observationReturnLocation"
            :return-label="observationReturnLabel"
            @loaded="onObservationLoaded"
          />

          <template v-else-if="hasOpenForm">
            <p
              v-if="
                isNewForm &&
                recordRef &&
                !recordParticipant &&
                isRecordParticipantLoading
              "
              id="record-loading"
              class="stage-empty"
              role="status"
              tabindex="-1"
            >
              Loading participant…
            </p>
            <p
              v-else-if="isNewForm && recordRef && !recordParticipant"
              id="record-not-loaded"
              class="stage-empty"
              tabindex="-1"
            >
              This participant isn't among those loaded for {{ studyCode }}.
              <router-link
                :to="
                  studyLocation({ stage: 'conduct', section: 'participants' })
                "
              >
                Find it in Participants </router-link
              >.
            </p>
            <CanvasFormWorkspace
              v-else
              :study-code="studyCode"
              :participant="activeFormParticipant"
              :enrollment-id="activeEnrollmentId"
              :record-loading="canvas.record.loading"
              :form-id="openFormId"
              :collection-spec-id="initialSpecId || null"
              :planned-activity-id="initialVisitId || null"
              :form-label="
                loadedForm && loadedForm.form_id === openFormId
                  ? formatFormLabel(loadedForm)
                  : undefined
              "
              :can-manage-queries="canManage"
              :can-answer-queries="canAnswer"
              :can-verify-source="canVerify"
              :highlight-item-id="highlightItemQuery || null"
              :back-location="formBackLocation"
              :back-label="formBackLabel"
              :handoff-location="
                isDemoBuild() && activeFormParticipant
                  ? ecrfHandoffLocationFor(activeFormParticipant)
                  : null
              "
              :form-location="formLocationById"
              :new-form-cell-location="
                activeFormParticipant
                  ? (specId, visitId) =>
                      newFormCellLocationFor(
                        activeFormParticipant!,
                        specId,
                        visitId
                      )
                  : undefined
              "
              :observation-location="observationLocationFromForm"
              @dirty-change="formWorkspaceDirty = $event"
              @form-loaded="onFormLoaded"
              @closed="onFormClosed"
            />
          </template>

          <template v-else-if="isParticipantRecordOpen">
            <ParticipantRecord
              v-if="recordParticipant"
              :participant="recordParticipant"
              :can-manage="canManage"
              :form-location="formLocationById"
              :new-form-location="newFormLocationFor(recordParticipant)"
              :new-form-cell-location="
                (specId, visitId) =>
                  newFormCellLocationFor(recordParticipant!, specId, visitId)
              "
              :handoff-location="
                isDemoBuild() ? ecrfHandoffLocationFor(recordParticipant) : null
              "
              :observation-location="observationLocationFromRecord"
              @retry="canvas.loadRecord(recordParticipant)"
            />
            <p
              v-else-if="isRecordParticipantLoading"
              id="record-loading"
              class="stage-empty"
              role="status"
              tabindex="-1"
            >
              Loading participant…
            </p>
            <p v-else id="record-not-loaded" class="stage-empty" tabindex="-1">
              This participant isn't among those loaded for {{ studyCode }}.
              <router-link
                :to="
                  studyLocation({ stage: 'conduct', section: 'participants' })
                "
              >
                Find it in Participants </router-link
              >.
            </p>
          </template>

          <template v-else-if="isResolvingConductSubject">
            <p
              id="record-loading"
              class="stage-empty"
              role="status"
              tabindex="-1"
            >
              Loading participant…
            </p>
          </template>

          <template v-else-if="!stage">
            <StudyPulse @drill="drillToParticipants" />
            <StudySitesPanel
              v-if="hasStudySitesService"
              :key="`home-sites-${studyCode}`"
              :study-code="studyCode"
              :can-manage-sites="authStore.canManageWorkspaceSites"
              compact
              @open-participants="openParticipantsForSite"
            />
            <NeedsAttention
              :key="`attention-${studyCode}`"
              :study-code="studyCode"
            />
            <WorkQueuesPanel
              :key="`work-queues-${studyCode}`"
              :study-code="studyCode"
            />
            <StudyActivity :key="studyCode" :study-code="studyCode" />
          </template>

          <ParticipantsSection
            v-else-if="section?.id === 'participants'"
            :status="statusFilter"
            :site-id="siteFilter"
            :query="participantSearchFilter"
            :selected-id="(selectedNode ?? recordRef)?.id ?? ''"
            :can-manage-participants="canChangeParticipants"
            @select="selectParticipant"
            @filter="applyParticipantFilter"
          />

          <SitesSection
            v-else-if="section?.id === 'sites'"
            :key="`sites-${studyCode}`"
            :study-code="studyCode"
            :site-id="siteFilter"
            @open-participants="
              (code) =>
                router.push(
                  studyLocation({
                    stage: 'conduct',
                    section: 'participants',
                    site: code || undefined,
                  })
                )
            "
            @open-activation="
              (code) =>
                router.push(
                  studyLocation({
                    stage: 'start',
                    section: 'site-activation',
                    site: code || undefined,
                  })
                )
            "
          />

          <ProtocolVersionsSection
            v-else-if="section?.id === 'protocol-versions'"
            :key="studyCode"
            :study-code="studyCode"
            :selected-ref="selectedVersionText"
            @select="selectVersion"
          />

          <SiteActivationPanel
            v-else-if="section?.id === 'site-activation'"
            :key="studyCode"
            :study-code="studyCode"
            :site-id="siteFilter"
          />

          <CtmsGreenlightSection
            v-else-if="section?.id === 'startup-greenlight'"
            :key="`greenlight-${studyCode}`"
            :study-code="studyCode"
            :site-id="siteFilter"
          />

          <CtmsMilestonesSection
            v-else-if="section?.id === 'site-milestones'"
            :key="`milestones-${studyCode}`"
            :study-code="studyCode"
            :site-id="siteFilter"
          />

          <CtmsDelegationSection
            v-else-if="section?.id === 'delegation'"
            :key="`delegation-${studyCode}`"
            :study-code="studyCode"
            :site-id="siteFilter"
          />

          <CtmsMonitoringSection
            v-else-if="section?.id === 'monitoring-visits'"
            :key="`monitoring-${studyCode}`"
            :study-code="studyCode"
            :site-id="siteFilter"
          />

          <RtsmSupplySection
            v-else-if="section?.id === 'supply'"
            :key="`supply-${studyCode}`"
            :study-code="studyCode"
            :site-id="siteFilter"
          />

          <template v-else-if="section?.id === 'documents'">
            <DocumentManagerView
              v-if="canReadDocuments"
              :key="`docs-${studyCode}`"
              :study-id="studyCode"
              :site-id="siteFilter"
            />
            <p
              v-else
              id="etmf-read-role-guidance"
              class="stage-empty"
              role="status"
            >
              {{ ETMF_READ_ROLE_REASON }}
            </p>
          </template>

          <StudyTrainingView
            v-else-if="section?.id === 'training'"
            :key="`training-${studyCode}`"
            :study-id="studyCode"
            :site-id="siteFilter"
          />

          <QueriesSection
            v-else-if="section?.id === 'queries'"
            :key="`queries-${studyCode}`"
            :study-code="studyCode"
            :can-manage="canManage"
            :can-answer="canAnswer"
            :status="queryStatusFilter"
            :owner="queryOwnerFilter"
            :origin="queryOriginFilter"
            :priority="queryPriorityFilter"
            :site="siteFilter"
            :participant="queryParticipantFilter"
            :changed="queryChangedFilter"
            @filter="applyStudyQueryFilter"
          />

          <LocksSection
            v-else-if="section?.id === 'locks'"
            :key="`locks-${studyCode}`"
            :study-code="studyCode"
          />

          <ValidationSection
            v-else-if="section?.id === 'validation'"
            :key="`validation-${studyCode}`"
            :study-code="studyCode"
            :can-manage-rules="canManageRules"
          />

          <ExportsSection
            v-else-if="section?.id === 'exports'"
            :key="`exports-${studyCode}`"
            :study-code="studyCode"
          />

          <section
            v-else
            id="stage-overview"
            class="stage-overview"
            aria-labelledby="stage-overview-title"
          >
            <h2 id="stage-overview-title" tabindex="-1">
              {{ stage.label }}
            </h2>
            <p class="stage-scope">{{ stage.scope }}.</p>
            <ul v-if="stage.sections.length" class="stage-sections">
              <li v-for="item in stage.sections" :key="item.id">
                <router-link
                  :to="
                    scopedStudyLocation({ stage: stage.id, section: item.id })
                  "
                >
                  {{ item.label }}
                </router-link>
                <span class="stage-section-description">
                  {{ item.description }}.</span
                >
              </li>
            </ul>
            <p id="stage-empty" class="stage-empty">
              {{
                stage.sections.length
                  ? `The rest of ${stage.label} arrives as its services are built.`
                  : `No part of ${stage.label} has a service yet.`
              }}
              <router-link to="/plans">
                Compare product families &amp; pre-release roadmap </router-link
              >.
            </p>
          </section>
        </component>

        <CanvasInspector
          ref="inspectorRef"
          :study-code="studyCode"
          :participant="inspectedParticipant"
          :node-ref="inspectedNodeText"
          :missing="isSelectedParticipantMissing"
          :protocol-version="inspectedVersion"
          :protocol-version-error="inspectedVersionError"
          :generic-node-ref="selectedGenericNodeRef"
          :generic-node="inspectedGenericNode"
          :generic-node-loading="inspectedGenericLoading"
          :record-location="
            inspectorContextParticipant &&
            (!isParticipantRecordOpen ||
              inspectorContextParticipant !== recordParticipant ||
              hasOpenForm ||
              Boolean(openObservationRef))
              ? recordLocationFor(inspectorContextParticipant)
              : null
          "
          :data-entry-location="
            inspectorContextParticipant &&
            !(isNewForm && inspectorContextParticipant === recordParticipant)
              ? dataEntryLocationFor(inspectorContextParticipant)
              : null
          "
          :handoff-location="
            isDemoBuild() && inspectorContextParticipant
              ? ecrfHandoffLocationFor(inspectorContextParticipant)
              : null
          "
          :can-manage-participants="canChangeParticipants"
          :can-unblind-participants="canUnblindParticipants"
          :assist-prompt="askText"
          @update:collapsed="inspectorCollapsed = $event"
          @retry-protocol-version="retryProtocolVersionLoad"
        />
      </div>
    </template>

    <component
      :is="inAppShell ? 'div' : 'main'"
      v-else
      id="canvas-workspace"
      class="open-study"
      :role="inAppShell ? 'region' : undefined"
      :aria-label="inAppShell ? 'Open a study' : undefined"
      tabindex="-1"
    >
      <h1 id="canvas-study-title" tabindex="-1">Open a study</h1>
      <form
        id="open-study-form"
        class="open-study-form"
        @submit.prevent="openTypedStudy"
      >
        <label for="open-study-code">Study code</label>
        <input
          id="open-study-code"
          v-model="typedStudy"
          type="text"
          autocomplete="off"
          maxlength="255"
          placeholder="e.g. CADENCE-101"
        />
        <button
          id="btn-open-study"
          type="submit"
          class="btn btn-primary"
          :disabled="!typedStudy.trim()"
        >
          Open
        </button>
      </form>
      <p v-if="!hasStudyDirectoryService" class="open-study-note">
        Enter a study code such as CADENCE-101 to open its workspace.
      </p>

      <section
        v-if="hasStudyDirectoryService"
        id="visible-studies-section"
        class="directory-section"
        aria-labelledby="visible-studies-heading"
      >
        <div class="directory-header">
          <h2 id="visible-studies-heading">Visible studies in your tenant</h2>
          <span
            id="visible-studies-status"
            class="directory-status"
            role="status"
          >
            {{ studyDirectoryStatus }}
          </span>
        </div>

        <div
          v-if="studyDirectoryError"
          id="visible-studies-error"
          class="inline-banner inline-banner-error"
          role="alert"
        >
          {{ studyDirectoryError }}
        </div>

        <ul
          v-if="visibleStudies.length > 0"
          id="visible-studies-list"
          class="visible-studies-list"
        >
          <li
            v-for="item in visibleStudies"
            :id="`study-item-${item.code}`"
            :key="item.ref"
            class="study-directory-card"
          >
            <div class="study-card-main">
              <div class="study-card-top">
                <strong class="study-card-code">{{ item.code }}</strong>
                <span class="study-phase-badge">{{ item.phase }}</span>
              </div>
              <div class="study-card-title">
                {{ item.title }}
              </div>
              <div class="study-card-sponsor">
                Sponsor: {{ item.sponsor_name }} ({{ item.sponsor_code }}) ·
                {{
                  item.site_count === 1
                    ? "1 linked site"
                    : `${item.site_count ?? 0} linked sites`
                }}
              </div>
            </div>
            <div class="study-card-actions">
              <button
                :id="`btn-open-study-${item.code}`"
                type="button"
                class="btn btn-primary"
                @click="openStudyByCode(item.code)"
              >
                Open {{ item.code }}
              </button>
              <button
                :id="`btn-design-study-${item.code}`"
                type="button"
                class="btn btn-secondary"
                @click="openStudyDesign(item.code)"
              >
                Protocol drafts
              </button>
            </div>
          </li>
        </ul>
      </section>

      <section
        v-if="hasCreateStudyService"
        id="create-study-section"
        class="directory-section"
        aria-labelledby="create-study-heading"
      >
        <h2 id="create-study-heading">Create a new study</h2>
        <p class="open-study-note">
          Register a study with its unique code, title, clinical phase, sponsor,
          and signed reason for change.
        </p>

        <div
          v-if="creatingStudy"
          id="create-study-progress"
          class="inline-banner inline-banner-progress"
          role="status"
        >
          {{
            creatingStudyStep === "import"
              ? "Importing protocol draft and validating forms and items…"
              : "Registering study in your tenant…"
          }}
        </div>

        <div
          v-if="createStudySuccess"
          id="create-study-success"
          class="inline-banner inline-banner-success"
          role="status"
          tabindex="-1"
        >
          <span>{{ createStudySuccess }}</span>
          <button
            v-if="lastCreatedStudyCode"
            id="btn-open-created-study"
            type="button"
            class="btn btn-secondary"
            @click="openStudyDesign(lastCreatedStudyCode)"
          >
            Open {{ lastCreatedStudyCode }}
          </button>
        </div>

        <div
          v-if="createStudyError"
          id="create-study-error"
          class="inline-banner inline-banner-error"
          role="alert"
        >
          {{ createStudyError }}
        </div>

        <form
          id="create-study-form"
          class="create-study-form"
          @submit.prevent="submitCreateStudy"
        >
          <div class="create-study-grid">
            <div class="create-field">
              <label for="create-study-code">Study code</label>
              <input
                id="create-study-code"
                v-model="newStudyCode"
                type="text"
                autocomplete="off"
                maxlength="64"
                placeholder="e.g. CADENCE-101"
                :disabled="creatingStudy"
              />
            </div>

            <div class="create-field">
              <label for="create-study-phase">Clinical phase</label>
              <select
                id="create-study-phase"
                v-model="newStudyPhase"
                :disabled="creatingStudy"
              >
                <option value="Phase 1">Phase 1</option>
                <option value="Phase 1/2">Phase 1/2</option>
                <option value="Phase 2">Phase 2</option>
                <option value="Phase 2/3">Phase 2/3</option>
                <option value="Phase 3">Phase 3</option>
                <option value="Phase 4">Phase 4</option>
                <option value="Observational">Observational</option>
              </select>
            </div>

            <div class="create-field create-field-wide">
              <label for="create-study-title">Study title</label>
              <input
                id="create-study-title"
                v-model="newStudyTitle"
                type="text"
                autocomplete="off"
                maxlength="500"
                placeholder="e.g. Multicenter Cardiovascular Outcomes Study"
                :disabled="creatingStudy"
              />
            </div>

            <div class="create-field create-field-wide">
              <label for="create-study-sponsor">Sponsor</label>
              <select
                id="create-study-sponsor"
                v-model="selectedSponsorChoice"
                :disabled="creatingStudy"
              >
                <option
                  v-for="org in sponsorOrganizations"
                  :key="org.ref"
                  :value="org.ref"
                >
                  {{ org.name }} ({{ org.code }})
                </option>
                <option value="__new__">
                  Register new sponsor organization…
                </option>
              </select>
            </div>

            <template v-if="selectedSponsorChoice === '__new__'">
              <div class="create-field">
                <label for="create-study-sponsor-name">New sponsor name</label>
                <input
                  id="create-study-sponsor-name"
                  v-model="newSponsorName"
                  type="text"
                  autocomplete="off"
                  maxlength="200"
                  placeholder="e.g. Cadence Pharma"
                  :disabled="creatingStudy"
                />
              </div>
              <div class="create-field">
                <label for="create-study-sponsor-code">
                  Sponsor code (optional)
                </label>
                <input
                  id="create-study-sponsor-code"
                  v-model="newSponsorCode"
                  type="text"
                  autocomplete="off"
                  maxlength="64"
                  placeholder="e.g. CADENCE-PHARMA"
                  :disabled="creatingStudy"
                />
              </div>
            </template>

            <div class="create-field create-field-wide">
              <label for="create-study-file">
                Initial protocol draft file (optional — ODM v1.3.2 XML or USDM
                v4 JSON, up to 10 MB)
              </label>
              <input
                id="create-study-file"
                ref="createStudyFileInputRef"
                type="file"
                accept=".xml,.json,application/xml,text/xml,application/json"
                class="sr-only"
                :disabled="creatingStudy"
                @change="onCreateStudyFileSelected"
              />
              <div class="create-file-row">
                <button
                  id="create-study-choose-file"
                  type="button"
                  class="btn btn-secondary"
                  :disabled="creatingStudy"
                  @click="openCreateStudyFilePicker"
                >
                  Choose ODM XML or USDM JSON file
                </button>
                <button
                  v-if="newStudyImportFile"
                  id="create-study-clear-file"
                  type="button"
                  class="btn btn-secondary"
                  :disabled="creatingStudy"
                  @click="onClearCreateStudyFileClick"
                >
                  Remove file
                </button>
                <span
                  v-if="newStudyImportFile"
                  id="create-study-selected-file"
                  class="create-file-note"
                >
                  {{ newStudyImportFile.name }}
                </span>
              </div>
            </div>

            <div
              v-if="defaultStarterSite"
              class="create-field create-field-wide create-checkbox-field"
            >
              <input
                id="create-study-link-sandbox-site"
                v-model="linkSandboxSiteOnCreate"
                type="checkbox"
                :disabled="creatingStudy"
              />
              <label for="create-study-link-sandbox-site">
                Link default workspace site
                <strong>{{ defaultStarterSite.code }}</strong> ({{
                  defaultStarterSite.name
                }}, {{ defaultStarterSite.country }}) to this study
              </label>
            </div>

            <div class="create-field create-field-wide">
              <label for="create-study-reason">
                Reason for change (required)
              </label>
              <input
                id="create-study-reason"
                v-model="newStudyReason"
                type="text"
                autocomplete="off"
                maxlength="500"
                placeholder="e.g. Register new clinical study protocol workspace"
                :disabled="creatingStudy"
              />
            </div>
          </div>

          <button
            id="btn-create-study"
            type="submit"
            class="btn btn-primary"
            :disabled="!canCreateStudy"
          >
            {{ createStudyButtonLabel }}
          </button>
        </form>
      </section>
    </component>

    <CanvasPalette
      v-if="!inAppShell && paletteOpen"
      :study-code="studyCode"
      :participants="canvas.paletteParticipants"
      @close="paletteOpen = false"
      @go="goToPaletteTarget"
    />

    <SignOutConfirmModal
      v-if="!inAppShell"
      id="canvas-signout-confirm-modal"
      v-model="isCanvasSignOutConfirmOpen"
      @confirm="confirmCanvasSignOut"
      @cancel="cancelCanvasSignOut"
    />
  </div>
</template>

<script setup lang="ts">
import {
  computed,
  inject,
  nextTick,
  onMounted,
  onUnmounted,
  ref,
  watch,
} from "vue";
import {
  onBeforeRouteLeave,
  onBeforeRouteUpdate,
  useRoute,
  useRouter,
  type LocationQueryRaw,
  type RouteLocationNormalized,
} from "vue-router";
import type { EcrfForm } from "../api/ecrfFormsApi";
import type { SubjectSummary } from "../api/executionApi";
import { refreshParticipantDetailMutation } from "../stores/studyCanvasParticipantMutations";
import {
  describeKernelError,
  kernelApi,
  type KernelNode,
  type KernelObservation,
  type KernelOrganizationSummary,
  type KernelProtocolVersion,
  type KernelSiteSummary,
  type KernelStudySummary,
} from "../api/kernelApi";
import {
  canResolveNodesRemotely,
  resolveNode,
  seedNodeSummary,
} from "../canvas/nodeResolver";
import {
  STAGES,
  findSection,
  findStage,
  isParticipantState,
} from "../canvas/lifecycle";
import {
  collectionInstanceId,
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
  parseNodeRef,
  tryNodeRef,
} from "../canvas/nodeRef";
import {
  ETMF_READ_ROLE_REASON,
  canAnswerQueries,
  canManageParticipants,
  canManageQueries,
  canManageRulePacks,
  canReadEtmf,
  canUnblindParticipant,
  canVerifySource,
} from "../canvas/authority";
import {
  syncInstantiatedClinicalScope,
  type PaletteTarget,
} from "../canvas/palette";
import {
  SignOutConfirmModal,
  handoffFocus,
  hasActiveModalFocusTrap,
  useSignOutGuard,
} from "ui";
import { confirmDialog } from "../composables/useConfirmDialog.js";
import { useAuthStore } from "../stores/auth";
import { useCanvasSelection } from "../composables/useCanvasSelection";
import { useKnowledgeStore } from "../stores/knowledge.js";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import { hasUserScopedDrafts } from "../utils/userScopedStorage.js";
import CanvasFormWorkspace from "../components/canvas/CanvasFormWorkspace.vue";
import CanvasInspector from "../components/canvas/CanvasInspector.vue";
import CanvasPalette from "../components/canvas/CanvasPalette.vue";
import ExportsSection from "../components/canvas/ExportsSection.vue";
import LocksSection from "../components/canvas/LocksSection.vue";
import ValidationSection from "../components/canvas/ValidationSection.vue";
import ParticipantRecord from "../components/canvas/ParticipantRecord.vue";
import ParticipantsSection from "../components/canvas/ParticipantsSection.vue";
import ProtocolVersionsSection from "../components/canvas/ProtocolVersionsSection.vue";
import SitesSection from "../components/canvas/SitesSection.vue";
import QueriesSection, {
  type StudyQueriesFilterState,
} from "../components/canvas/QueriesSection.vue";
import StudyActivity from "../components/canvas/StudyActivity.vue";
import StudyPulse from "../components/canvas/StudyPulse.vue";
import StudySitesPanel from "../components/canvas/StudySitesPanel.vue";
import WorkQueuesPanel from "../components/canvas/WorkQueuesPanel.vue";
import SiteActivationPanel from "../components/clinical/SiteActivationPanel.vue";
import CtmsGreenlightSection from "../components/clinical/CtmsGreenlightSection.vue";
import CtmsMilestonesSection from "../components/clinical/CtmsMilestonesSection.vue";
import CtmsDelegationSection from "../components/clinical/CtmsDelegationSection.vue";
import CtmsMonitoringSection from "../components/clinical/CtmsMonitoringSection.vue";
import RtsmSupplySection from "../components/clinical/RtsmSupplySection.vue";
import NeedsAttention from "../components/ai/NeedsAttention.vue";
import ProposalReview from "../components/ai/ProposalReview.vue";
import DocumentManagerView from "./DocumentManagerView.vue";
import ObservationView from "./ObservationView.vue";
import StudyTrainingView from "./StudyTrainingView.vue";
import { useAiAssistance } from "../ai/aiAssistance";
import { demoRandomToken, isDemoBuild } from "../utils/demoMode";

/**
 * The Clinical Canvas (docs/ux/CLINICAL_CANVAS.md): one study at a time, navigated by lifecycle stage, in
 * three panes (navigator, workspace, inspector), with a ⌘K palette. Everything it shows is read from a
 * service; what no service serves yet is absent rather than mocked (§6.1).
 *
 * The URL holds the whole state, so every view is addressable (UX-2):
 * `/studies/:studyCode?stage=&section=&status=&site=&node=&participant=&form=&observation=&proposal=&ask=`. `node` (the
 * participant or protocol version in the inspector), `participant` (the participant whose data is open), `form` (a
 * `collection_instance` NodeRef or `new`), `observation` (an `observation` NodeRef) and `proposal` (the proposal under
 * review in the workspace, ADR-2635) are kernel NodeRefs (ADR-2481). `ask` is a question from the palette for the
 * inspector's Assist.
 */
interface StudyQuery {
  stage?: string;
  section?: string;
  status?: string;
  owner?: string;
  origin?: string;
  priority?: string;
  site?: string;
  q?: string;
  subject?: string;
  changed?: string;
  node?: string;
  participant?: string;
  form?: string;
  item?: string;
  spec?: string;
  visit?: string;
  observation?: string;
  proposal?: string;
  ask?: string;
}

const route = useRoute();
const router = useRouter();
const authStore = useAuthStore();
const knowledgeStore = useKnowledgeStore();
const canvas = useStudyCanvasStore();
const { setActiveStudyCode } = useActiveStudyScope();
const inAppShell = inject<boolean>("inAppShell", false);

const { enabled: aiEnabled, setEnabled: setAiEnabled } = useAiAssistance();

const paletteOpen = ref(false);
const typedStudy = ref("");
const inspectorCollapsed = ref(true);
const inspectorRef = ref<InstanceType<typeof CanvasInspector> | null>(null);
const topbarRef = ref<HTMLElement | null>(null);
const topbarHeight = ref(45);
let topbarResizeObserver: ResizeObserver | null = null;

function syncTopbarHeight() {
  const el = topbarRef.value;
  if (!el || typeof el.getBoundingClientRect !== "function") return;
  const measured = Math.round(el.getBoundingClientRect().height);
  if (measured > 0) {
    topbarHeight.value = measured;
  }
}

const hasStudyDirectoryService = computed(
  () => typeof kernelApi.listStudies === "function"
);
const hasCreateStudyService = computed(
  () => typeof kernelApi.createStudy === "function"
);
const hasStudySitesService = computed(
  () => typeof kernelApi.listStudySites === "function"
);

const visibleStudies = ref<KernelStudySummary[]>([]);
const studyDirectoryLoading = ref(false);
const studyDirectoryError = ref("");
const sponsorOrganizations = ref<KernelOrganizationSummary[]>([]);
const workspaceSites = ref<KernelSiteSummary[]>([]);
const linkSandboxSiteOnCreate = ref(true);

const defaultStarterSite = computed<KernelSiteSummary | null>(() => {
  if (workspaceSites.value.length === 0) return null;
  const assigned = authStore.assignedSiteIds ?? [];
  if (assigned.length > 0) {
    const matched = workspaceSites.value.find((site) =>
      assigned.some((code) => code.toLowerCase() === site.code.toLowerCase())
    );
    if (matched) return matched;
  }
  return workspaceSites.value[0];
});

const newStudyCode = ref("");
const newStudyTitle = ref("");
const newStudyPhase = ref("Phase 2");
const selectedSponsorChoice = ref("__new__");
const newSponsorName = ref("");
const newSponsorCode = ref("");
const newStudyReason = ref("");
const createStudyFileInputRef = ref<HTMLInputElement | null>(null);
const newStudyImportFile = ref<File | null>(null);
const createIdempotencyKey = ref("");
const createImportIdempotencyKey = ref("");
const pendingCreatedStudy = ref<KernelStudySummary | null>(null);
const creatingStudy = ref(false);
const creatingStudyStep = ref<"study" | "import">("study");
const createStudyError = ref("");
const createStudySuccess = ref("");
const lastCreatedStudyCode = ref("");
const MAX_CREATE_IMPORT_BYTES = 10 * 1024 * 1024;

const createStudyButtonLabel = computed(() => {
  if (creatingStudy.value) {
    return newStudyImportFile.value
      ? "Creating study & importing draft…"
      : "Creating study…";
  }
  return newStudyImportFile.value
    ? "Create study & import draft"
    : "Create study";
});

const studyDirectoryStatus = computed(() => {
  if (studyDirectoryLoading.value) return "Loading visible studies…";
  if (studyDirectoryError.value) return "Could not load study directory.";
  const count = visibleStudies.value.length;
  if (count === 0) return "No visible studies in your tenant yet.";
  return count === 1 ? "1 visible study" : `${count} visible studies`;
});

const canCreateStudy = computed(() => {
  if (creatingStudy.value) return false;
  if (!newStudyCode.value.trim() || !newStudyTitle.value.trim()) return false;
  if (!newStudyPhase.value.trim() || !newStudyReason.value.trim()) return false;
  if (selectedSponsorChoice.value === "__new__") {
    return Boolean(newSponsorName.value.trim());
  }
  return Boolean(selectedSponsorChoice.value);
});

function nextStudyIdempotencyKey(): string {
  if (isDemoBuild()) {
    return `study-${demoRandomToken(12, 36, "study-create.idempotency")}`;
  }
  if (typeof globalThis.crypto?.randomUUID === "function") {
    return globalThis.crypto.randomUUID();
  }
  return `study-${demoRandomToken(12, 36, "study-create.idempotency")}`;
}

watch(
  [
    newStudyCode,
    newStudyTitle,
    newStudyPhase,
    selectedSponsorChoice,
    newSponsorName,
    newSponsorCode,
  ],
  () => {
    createIdempotencyKey.value = "";
    pendingCreatedStudy.value = null;
  }
);

watch(newStudyReason, () => {
  createIdempotencyKey.value = "";
  createImportIdempotencyKey.value = "";
});

function openCreateStudyFilePicker() {
  createStudyError.value = "";
  createStudyFileInputRef.value?.click();
}

function clearCreateStudyFile() {
  newStudyImportFile.value = null;
  createImportIdempotencyKey.value = "";
  if (createStudyFileInputRef.value) {
    createStudyFileInputRef.value.value = "";
  }
}

function onClearCreateStudyFileClick() {
  clearCreateStudyFile();
  void handoffFocus("#create-study-choose-file");
}

function onCreateStudyFileSelected(event: Event) {
  const input = event.target as HTMLInputElement | null;
  const picked = input?.files?.[0] ?? null;
  createStudyError.value = "";
  createImportIdempotencyKey.value = "";
  if (!picked) {
    newStudyImportFile.value = null;
    return;
  }
  if (picked.size > MAX_CREATE_IMPORT_BYTES) {
    newStudyImportFile.value = null;
    if (input) input.value = "";
    createStudyError.value = `File '${picked.name}' exceeds the 10 MB synchronous upload limit.`;
    return;
  }
  newStudyImportFile.value = picked;
}

async function loadStudyDirectory() {
  if (typeof kernelApi.listStudies !== "function") return;
  studyDirectoryLoading.value = true;
  studyDirectoryError.value = "";
  try {
    visibleStudies.value = await kernelApi.listStudies();
    if (
      !studyCode.value &&
      (route.query.section === "locks" || route.query.section === "exports") &&
      visibleStudies.value.length > 0
    ) {
      await router.replace(
        studyLocation(
          {
            stage: stage.value?.id,
            section: section.value?.id,
          },
          visibleStudies.value[0].code
        )
      );
      return;
    }
  } catch (err) {
    studyDirectoryError.value = describeKernelError(err);
  } finally {
    studyDirectoryLoading.value = false;
  }

  if (typeof kernelApi.listOrganizations === "function") {
    try {
      const orgs = await kernelApi.listOrganizations("Sponsor");
      const sponsorsOnly = Array.isArray(orgs)
        ? orgs.filter((o) => !o.kind || o.kind === "Sponsor")
        : [];
      sponsorOrganizations.value = sponsorsOnly;
      if (
        sponsorsOnly.length > 0 &&
        selectedSponsorChoice.value === "__new__"
      ) {
        selectedSponsorChoice.value = sponsorsOnly[0].ref;
      }
    } catch {
      // Custom sponsor entry remains available if organization listing fails.
    }
  }

  if (typeof kernelApi.listSites === "function") {
    try {
      const sites = await kernelApi.listSites();
      workspaceSites.value = Array.isArray(sites) ? sites : [];
    } catch {
      workspaceSites.value = [];
    }
  }
}

function openStudyByCode(code: string) {
  const clean = code.trim();
  if (clean) {
    router.push(
      studyLocation(
        {
          stage: stage.value?.id,
          section: section.value?.id,
        },
        clean
      )
    );
  }
}

function openStudyDesign(code: string) {
  const clean = code.trim();
  if (clean) {
    router.push(
      studyLocation({ stage: "design", section: "protocol-versions" }, clean)
    );
  }
}

async function submitCreateStudy() {
  if (typeof kernelApi.createStudy !== "function") return;
  createStudyError.value = "";
  createStudySuccess.value = "";

  if (!createIdempotencyKey.value) {
    createIdempotencyKey.value = nextStudyIdempotencyKey();
  }

  creatingStudy.value = true;
  creatingStudyStep.value = "study";
  try {
    const requestedCode = newStudyCode.value.trim();
    let created =
      pendingCreatedStudy.value &&
      pendingCreatedStudy.value.code.toUpperCase() ===
        requestedCode.toUpperCase()
        ? pendingCreatedStudy.value
        : null;

    if (!created) {
      const isNewSponsor = selectedSponsorChoice.value === "__new__";
      const starterSite =
        linkSandboxSiteOnCreate.value && defaultStarterSite.value
          ? defaultStarterSite.value
          : null;
      created = await kernelApi.createStudy({
        code: requestedCode,
        title: newStudyTitle.value.trim(),
        phase: newStudyPhase.value.trim(),
        sponsorRef: isNewSponsor ? null : selectedSponsorChoice.value,
        sponsorName: isNewSponsor ? newSponsorName.value.trim() : null,
        sponsorCode:
          isNewSponsor && newSponsorCode.value.trim()
            ? newSponsorCode.value.trim()
            : null,
        linkSandboxSite: Boolean(starterSite),
        initialSiteRef: starterSite?.ref ?? null,
        reason: newStudyReason.value.trim(),
        idempotencyKey: createIdempotencyKey.value,
      });
      pendingCreatedStudy.value = created;

      try {
        await authStore.refresh(-1);
      } catch {
        // Non-OIDC test sessions continue with the existing in-memory session.
      }
      if (
        created?.code &&
        !authStore.studyIds.some(
          (code: string) => code.toUpperCase() === created.code.toUpperCase()
        )
      ) {
        authStore.studyIds = [...authStore.studyIds, created.code];
      }
      if (
        created?.code &&
        Array.isArray(authStore.assignedStudies) &&
        !authStore.assignedStudies.some(
          (code: string) => code.toUpperCase() === created.code.toUpperCase()
        )
      ) {
        authStore.assignedStudies = [
          ...authStore.assignedStudies,
          created.code,
        ];
      }
      if (
        starterSite?.code &&
        !authStore.siteIds.some(
          (code: string) =>
            code.toUpperCase() === starterSite.code.toUpperCase()
        )
      ) {
        authStore.siteIds = [...authStore.siteIds, starterSite.code];
      }
      if (
        starterSite?.code &&
        Array.isArray(authStore.assignedSites) &&
        !authStore.assignedSites.some(
          (code: string) =>
            code.toUpperCase() === starterSite.code.toUpperCase()
        )
      ) {
        authStore.assignedSites = [
          ...authStore.assignedSites,
          starterSite.code,
        ];
      }

      const existingIdx = visibleStudies.value.findIndex(
        (s) => s.code === created?.code
      );
      if (existingIdx >= 0) {
        visibleStudies.value.splice(existingIdx, 1, created);
      } else {
        visibleStudies.value = [created, ...visibleStudies.value];
      }
    }

    if (
      newStudyImportFile.value &&
      typeof kernelApi.importProtocolDraft === "function"
    ) {
      creatingStudyStep.value = "import";
      if (!createImportIdempotencyKey.value) {
        createImportIdempotencyKey.value = nextStudyIdempotencyKey();
      }
      const imported = await kernelApi.importProtocolDraft({
        studyRef: created.ref || created.code,
        file: newStudyImportFile.value,
        reason: newStudyReason.value.trim(),
        idempotencyKey: createImportIdempotencyKey.value,
      });

      newStudyCode.value = "";
      newStudyTitle.value = "";
      newSponsorName.value = "";
      newSponsorCode.value = "";
      newStudyReason.value = "";
      clearCreateStudyFile();
      createIdempotencyKey.value = "";
      pendingCreatedStudy.value = null;

      await router.push(
        studyLocation(
          {
            stage: "design",
            section: "protocol-versions",
            node: imported.version?.ref || undefined,
          },
          created.code
        )
      );
      return;
    }

    lastCreatedStudyCode.value = created.code;
    createStudySuccess.value = `Study ${created.code} (${created.title}) registered and ready in your visible study list.`;
    newStudyCode.value = "";
    newStudyTitle.value = "";
    newSponsorName.value = "";
    newSponsorCode.value = "";
    newStudyReason.value = "";
    clearCreateStudyFile();
    createIdempotencyKey.value = "";
    pendingCreatedStudy.value = null;

    await loadStudyDirectory();
    void handoffFocus([
      "#btn-open-created-study",
      "#create-study-success",
      "#create-study-code",
    ]);
  } catch (err) {
    createStudyError.value = describeKernelError(err);
  } finally {
    creatingStudy.value = false;
    creatingStudyStep.value = "study";
  }
}

const single = (value: unknown): string =>
  typeof value === "string" ? value : "";

const studyCode = computed(() => single(route.params.studyCode).trim());
const section = computed(() => findSection(route.query.section)?.section);
const stage = computed(
  () => findSection(route.query.section)?.stage ?? findStage(route.query.stage)
);
const statusFilter = computed(() => {
  const status = single(route.query.status);
  return isParticipantState(status) ? status : "";
});
const queryStatusFilter = computed(() => {
  const status = single(route.query.status).toLowerCase();
  return ["open", "answered", "closed", "cancelled"].includes(status)
    ? status
    : "";
});
const queryOwnerFilter = computed(() => {
  const owner = single(route.query.owner).toLowerCase();
  return ["site", "monitor", "data_manager"].includes(owner) ? owner : "";
});
const queryOriginFilter = computed(() => {
  const origin = single(route.query.origin).toLowerCase();
  return ["manual", "system"].includes(origin) ? origin : "";
});
const queryPriorityFilter = computed(() => {
  const priority = single(route.query.priority).toLowerCase();
  return ["low", "normal", "high"].includes(priority) ? priority : "";
});
const siteFilter = computed(() =>
  (single(route.query.site) || single(route.query.siteId)).trim()
);
const participantSearchFilter = computed(() => single(route.query.q).trim());
const queryParticipantFilter = computed(() =>
  single(route.query.subject).trim()
);
const queryChangedFilter = computed(() => {
  const changed = single(route.query.changed).toLowerCase();
  return changed === "true" || changed === "false" ? changed : "";
});

const loadedForm = ref<EcrfForm | null>(null);
const formWorkspaceDirty = ref(false);
const loadedObservation = ref<KernelObservation | null>(null);
const resolvedLineage = ref<{ siteCode: string; subjectCode: string } | null>(
  null
);
const resolvedParticipantId = ref("");
let lineageRequest = 0;

function isParticipantPending(id: string | undefined): boolean {
  if (!studyCode.value || !id) return false;
  if (canvas.knownParticipants[id]) return false;
  if (canvas.participantLookups[id]) return true;
  return !canvas.failedParticipantIds[id];
}

const loadedFormParticipant = computed<SubjectSummary | null>(() => {
  const pid = loadedForm.value?.participant_id;
  return pid ? (canvas.knownParticipants[pid] ?? null) : null;
});
const lineageParticipant = computed<SubjectSummary | null>(() =>
  resolvedParticipantId.value
    ? (canvas.knownParticipants[resolvedParticipantId.value] ?? null)
    : null
);

const {
  explicitNode,
  recordRef,
  recordParticipant: selectionRecordParticipant,
  activeFormParticipant: selectionActiveFormParticipant,
  activeParticipantNodeText: selectionActiveParticipantNodeText,
  formQuery,
  isNewForm,
  openFormId,
  openObservationRef,
  proposalRef,
  selectedNode,
  selectedParticipant,
  isSelectedParticipantLoading,
  selectedVersionText,
  selectedGenericNodeRef,
  inspectedParticipant: selectionInspectedParticipant,
  inspectedNodeText,
} = useCanvasSelection({
  query: () => route.query,
  knownParticipants: () => canvas.knownParticipants,
  loadedFormParticipant,
  lineageParticipant,
  isParticipantPending,
  canResolveRemotely: canResolveNodesRemotely,
});

const rawParticipantQuery = computed(() =>
  single(route.query.participant).trim()
);
const participantCodeQuery = computed(() =>
  !recordRef.value && !rawParticipantQuery.value.includes(":")
    ? rawParticipantQuery.value
    : ""
);
const codeQueryParticipant = computed<SubjectSummary | null>(() => {
  if (!participantCodeQuery.value) return null;
  return (
    Object.values(canvas.knownParticipants).find(
      (item) => item?.subject_id === participantCodeQuery.value
    ) ?? null
  );
});
const hasRecordScope = computed(() =>
  Boolean(recordRef.value || participantCodeQuery.value)
);
const isParticipantRecordOpen = computed(
  () => hasRecordScope.value && stage.value?.id === "conduct" && !section.value
);
const recordParticipant = computed<SubjectSummary | null>(
  () => selectionRecordParticipant.value ?? codeQueryParticipant.value
);
const activeFormParticipant = computed<SubjectSummary | null>(
  () => selectionActiveFormParticipant.value ?? codeQueryParticipant.value
);
const activeParticipantNodeText = computed(() => {
  if (selectionActiveParticipantNodeText.value) {
    return selectionActiveParticipantNodeText.value;
  }
  if (codeQueryParticipant.value) {
    const node = tryNodeRef("participant", codeQueryParticipant.value.id);
    return node ? formatNodeRef(node) : "";
  }
  return "";
});
const inspectedParticipant = computed<SubjectSummary | null>(
  () =>
    selectionInspectedParticipant.value ??
    (!explicitNode.value &&
    !openObservationRef.value &&
    !openFormId.value &&
    !proposalRef.value
      ? codeQueryParticipant.value
      : null)
);

const conductSubjectQuery = computed(() => {
  if (hasRecordScope.value || section.value?.id === "queries") return "";
  if (stage.value && stage.value.id !== "conduct") return "";
  return single(route.query.subject).trim();
});
const isResolvingConductSubject = ref(false);
let conductSubjectSeq = 0;

const inspectorContextParticipant = computed<SubjectSummary | null>(
  () =>
    inspectedParticipant.value ??
    (!explicitNode.value ? activeFormParticipant.value : null)
);

const highlightItemQuery = computed(() => single(route.query.item).trim());
const initialSpecId = computed(() => single(route.query.spec).trim());
const initialVisitId = computed(() => single(route.query.visit).trim());
const hasOpenForm = computed(
  () => isNewForm.value || Boolean(openFormId.value)
);
const askText = computed(() => single(route.query.ask).trim());

const isRecordParticipantLoading = computed(() =>
  recordRef.value
    ? isParticipantPending(recordRef.value.id)
    : Boolean(
        studyCode.value &&
        participantCodeQuery.value &&
        !codeQueryParticipant.value &&
        canvas.participantCodeLookups?.[participantCodeQuery.value]
      )
);

// The protocol version in the inspector, read from the kernel whenever the URL names one.
const inspectedVersion = ref<KernelProtocolVersion | null>(null);
const inspectedVersionError = ref("");
let versionRequest = 0;

async function loadInspectedProtocolVersion(text: string) {
  const request = ++versionRequest;
  inspectedVersion.value = null;
  inspectedVersionError.value = "";
  if (!text) return;
  try {
    const version = await kernelApi.getProtocolVersion(text);
    if (request === versionRequest) {
      inspectedVersion.value = version;
      seedNodeSummary(version.ref, {
        kind: "protocol_version",
        code: version.version,
        title: `Protocol ${version.version}`,
        status: version.status,
      });
    }
  } catch (e) {
    if (request === versionRequest) {
      inspectedVersionError.value = e instanceof Error ? e.message : String(e);
    }
  }
}

function retryProtocolVersionLoad() {
  if (selectedVersionText.value) {
    void loadInspectedProtocolVersion(selectedVersionText.value);
  }
}

watch(
  selectedVersionText,
  (text) => {
    void loadInspectedProtocolVersion(text);
  },
  { immediate: true }
);

const inspectedGenericNode = ref<KernelNode | null>(null);
const inspectedGenericLoading = ref(false);
let genericNodeRequest = 0;
watch(inspectedNodeText, (next, prev) => {
  if (next && next !== prev) {
    inspectorRef.value?.expand();
  }
});
watch(
  selectedGenericNodeRef,
  async (text) => {
    const request = ++genericNodeRequest;
    inspectedGenericNode.value = null;
    inspectedGenericLoading.value = false;
    if (!text) return;
    inspectedGenericLoading.value = true;
    const resolved = await resolveNode(text);
    if (request !== genericNodeRequest) return;
    inspectedGenericLoading.value = false;
    inspectedGenericNode.value = resolved;
  },
  { immediate: true }
);
const isSelectedParticipantMissing = computed(
  () =>
    Boolean(selectedNode.value) &&
    !selectedParticipant.value &&
    !isSelectedParticipantLoading.value &&
    !inspectedGenericLoading.value &&
    !inspectedGenericNode.value
);

const activeEnrollmentId = computed(() => {
  if (
    activeFormParticipant.value &&
    canvas.record.subjectCode === activeFormParticipant.value.subject_id
  ) {
    return canvas.record.enrollmentId;
  }
  if (loadedForm.value?.enrollment_id) return loadedForm.value.enrollment_id;
  return null;
});
const canManage = computed(() => canManageQueries(authStore));
const canAnswer = computed(() => canAnswerQueries(authStore));
const canManageRules = computed(() => canManageRulePacks(authStore));
const canVerify = computed(() => canVerifySource(authStore));
const canChangeParticipants = computed(() => canManageParticipants(authStore));
const canUnblindParticipants = computed(() => canUnblindParticipant(authStore));
const callerAuthKey = computed(() =>
  [
    authStore.isAuthenticated ? "1" : "0",
    authStore.user?.id ?? "",
    authStore.user?.username ?? "",
    ...(authStore.rawRoles ?? []),
  ].join("|")
);
const canReadDocuments = computed(() =>
  !authStore.isAuthenticated && (authStore.rawRoles?.length ?? 0) === 0
    ? true
    : canReadEtmf(authStore)
);

const navigatorStages = computed(() =>
  stage.value ? [stage.value] : STAGES.filter((s) => s.sections.length)
);

const zoomParticipant = computed(
  () =>
    recordParticipant.value ??
    loadedFormParticipant.value ??
    selectedParticipant.value ??
    lineageParticipant.value
);
const zoomParticipantCode = computed(
  () =>
    zoomParticipant.value?.subject_id ||
    participantCodeQuery.value ||
    resolvedLineage.value?.subjectCode ||
    ""
);
const zoomSite = computed(
  () =>
    zoomParticipant.value?.site_id ||
    resolvedLineage.value?.siteCode ||
    siteFilter.value ||
    ""
);
const zoomCurrent = computed(() => {
  if (openObservationRef.value) return "observation";
  if (hasOpenForm.value) return "form";
  if (zoomParticipantCode.value) {
    return isParticipantRecordOpen.value ? "participant" : "section";
  }
  return zoomSite.value ? "site" : "study";
});

function formatFormLabel(form: EcrfForm): string {
  const title = form.collection_title || form.collection_spec_id;
  return form.planned_activity_id
    ? `${title} · ${form.planned_activity_id}`
    : title;
}

const zoomFormLabel = computed(() => {
  if (isNewForm.value) {
    if (initialSpecId.value && initialVisitId.value) {
      return `Forms · ${initialSpecId.value} · ${initialVisitId.value}`;
    }
    if (initialSpecId.value) {
      return `Forms · ${initialSpecId.value}`;
    }
    return "Forms";
  }
  if (loadedForm.value && loadedForm.value.form_id === openFormId.value) {
    return formatFormLabel(loadedForm.value);
  }
  return "Case report form";
});

const zoomObservationLabel = computed(
  () => loadedObservation.value?.concept || "Observation"
);

const workspaceLabel = computed(() => {
  if (proposalRef.value) return "Proposal review";
  if (openObservationRef.value) {
    return `Observation ${zoomObservationLabel.value}`;
  }
  if (hasOpenForm.value) {
    return zoomFormLabel.value;
  }
  if (isParticipantRecordOpen.value) {
    return `${recordParticipant.value?.subject_id ?? (participantCodeQuery.value || "Participant")} data`;
  }
  if (!stage.value) return `${studyCode.value} home`;
  return section.value
    ? `${stage.value.label}: ${section.value.label}`
    : stage.value.label;
});

function studyLocation(query: StudyQuery = {}, code = studyCode.value) {
  const clean: LocationQueryRaw = {};
  for (const [key, value] of Object.entries(query)) {
    if (value) clean[key] = value;
  }
  return { name: "study", params: { studyCode: code }, query: clean };
}

const activeParticipantScopeParam = computed(() => {
  if (recordRef.value) return formatNodeRef(recordRef.value);
  return participantCodeQuery.value || "";
});

const activeInspectorParticipantNodeParam = computed(() => {
  if (selectedNode.value) return formatNodeRef(selectedNode.value);
  if (!activeParticipantScopeParam.value && activeParticipantNodeText.value) {
    return activeParticipantNodeText.value;
  }
  return "";
});

function scopedStudyLocation(query: StudyQuery = {}, code = studyCode.value) {
  const preservedSite = siteFilter.value || zoomSite.value || undefined;
  const preservedParticipant = activeParticipantScopeParam.value || undefined;
  const preservedNode = activeInspectorParticipantNodeParam.value || undefined;
  return studyLocation(
    {
      site: preservedSite,
      participant: preservedParticipant,
      node: preservedNode,
      ...query,
    },
    code
  );
}

function selectParticipant(participant: SubjectSummary) {
  const node = tryNodeRef("participant", participant.id);
  if (!node) return;
  inspectorRef.value?.expand();
  router.push(
    studyLocation({
      stage: "conduct",
      section: "participants",
      status: statusFilter.value,
      site: siteFilter.value,
      q: participantSearchFilter.value,
      node: formatNodeRef(node),
    })
  );
  // Stacked panes on a narrow screen: bring the inspector into view (§2.4).
  nextTick(() =>
    document.getElementById("canvas-inspector")?.scrollIntoView?.({
      block: "nearest",
    })
  );
}

/** The participant's data, opened at the participant zoom level with the participant in the inspector. */
function recordLocationFor(participant: SubjectSummary) {
  const node = tryNodeRef("participant", participant.id);
  const text = node ? formatNodeRef(node) : "";
  return studyLocation({ stage: "conduct", participant: text });
}

function newFormLocationFor(participant: SubjectSummary) {
  const node = tryNodeRef("participant", participant.id);
  const text = node ? formatNodeRef(node) : "";
  return studyLocation({ stage: "conduct", participant: text, form: "new" });
}

function newFormCellLocationFor(
  participant: SubjectSummary,
  specId: string,
  visitId?: string | null
) {
  const node = tryNodeRef("participant", participant.id);
  const text = node ? formatNodeRef(node) : "";
  return studyLocation({
    stage: "conduct",
    participant: text,
    form: "new",
    spec: specId || undefined,
    visit: visitId || undefined,
  });
}

const isDataWorklistSection = computed(
  () => section.value?.id === "queries" || section.value?.id === "validation"
);

function ecrfHandoffLocationFor(participant: SubjectSummary) {
  if (!isDemoBuild()) return null;
  const query: LocationQueryRaw = {
    studyId: studyCode.value,
    subjectId: participant.subject_id,
  };
  const activeParticipantMatch =
    activeFormParticipant.value?.id === participant.id;
  const siteIdForHandoff =
    participant.site_id || (activeParticipantMatch ? zoomSite.value : "");
  if (siteIdForHandoff) {
    query.siteId = siteIdForHandoff;
  }
  const enrollmentForParticipant =
    canvas.record.subjectCode === participant.subject_id
      ? canvas.record.enrollmentId
      : loadedForm.value?.participant_id === participant.id
        ? loadedForm.value.enrollment_id
        : null;
  if (enrollmentForParticipant) {
    query.enrollmentId = enrollmentForParticipant;
  }
  if (activeParticipantMatch && openFormId.value) {
    query.formId = openFormId.value;
  }
  const specId =
    activeParticipantMatch && loadedForm.value?.collection_spec_id
      ? loadedForm.value.collection_spec_id
      : activeParticipantMatch && initialSpecId.value
        ? initialSpecId.value
        : "";
  if (specId) {
    query.collectionSpecId = specId;
  }
  const visitId =
    activeParticipantMatch && loadedForm.value?.planned_activity_id
      ? loadedForm.value.planned_activity_id
      : activeParticipantMatch && initialVisitId.value
        ? initialVisitId.value
        : "";
  if (visitId) {
    query.plannedActivityId = visitId;
    query.visitId = visitId;
  }
  if (activeParticipantMatch && highlightItemQuery.value) {
    query.itemId = highlightItemQuery.value;
  }
  return { name: "ecrf", query };
}

function dataEntryLocationFor(participant: SubjectSummary) {
  return newFormLocationFor(participant);
}

function formLocationById(formId: string, itemId?: string | null) {
  const formRef = collectionInstanceRef(formId);
  const preserveDataSection = isDataWorklistSection.value;
  return studyLocation({
    stage: preserveDataSection ? "data" : "conduct",
    section: preserveDataSection ? section.value!.id : undefined,
    site: preserveDataSection
      ? siteFilter.value ||
        (!activeParticipantNodeText.value
          ? zoomSite.value || undefined
          : undefined)
      : !activeParticipantNodeText.value
        ? zoomSite.value || undefined
        : undefined,
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
    item: itemId || undefined,
  });
}

function observationLocationFromRecord(
  observationId: string,
  formId?: string,
  itemId?: string | null
) {
  const obsRef = observationNodeRef(observationId);
  const formRef = formId ? collectionInstanceRef(formId) : null;
  return studyLocation({
    stage: "conduct",
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
    item: itemId || undefined,
    observation: obsRef ?? undefined,
  });
}

function observationLocationFromForm(
  observationId: string,
  formId: string,
  itemId?: string | null
) {
  const obsRef = observationNodeRef(observationId);
  const formRef = collectionInstanceRef(formId || openFormId.value || "");
  const preserveDataSection = isDataWorklistSection.value;
  return studyLocation({
    stage: preserveDataSection ? "data" : "conduct",
    section: preserveDataSection ? section.value!.id : undefined,
    site: preserveDataSection
      ? siteFilter.value ||
        (!activeParticipantNodeText.value
          ? zoomSite.value || undefined
          : undefined)
      : !activeParticipantNodeText.value
        ? zoomSite.value || undefined
        : undefined,
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
    item: itemId || highlightItemQuery.value || undefined,
    observation: obsRef ?? undefined,
  });
}

const zoomParticipantLocation = computed(() => {
  if (zoomParticipant.value) {
    const base = recordLocationFor(zoomParticipant.value);
    if (siteFilter.value) {
      return studyLocation({
        stage: "conduct",
        site: siteFilter.value,
        participant: base.query.participant as string | undefined,
      });
    }
    return base;
  }
  if (participantCodeQuery.value) {
    return studyLocation({
      stage: "conduct",
      site: zoomSite.value || undefined,
      participant: participantCodeQuery.value,
    });
  }
  return studyLocation({
    stage: "conduct",
    section: "participants",
    site: zoomSite.value || undefined,
  });
});

const zoomFormLocation = computed(() => {
  if (openFormId.value) {
    return formLocationById(
      openFormId.value,
      highlightItemQuery.value || undefined
    );
  }
  return studyLocation({
    stage: "conduct",
    participant: activeParticipantNodeText.value || undefined,
    form: "new",
  });
});

const formBackLocation = computed(() => {
  if (section.value?.id === "queries" || section.value?.id === "validation") {
    return studyLocation({
      stage: "data",
      section: section.value.id,
      site: siteFilter.value || undefined,
    });
  }
  if (activeFormParticipant.value) {
    return recordLocationFor(activeFormParticipant.value);
  }
  return studyLocation({
    stage: "conduct",
    section: "participants",
    site: zoomSite.value || undefined,
  });
});

const formBackLabel = computed(() => {
  if (section.value?.id === "queries" || section.value?.id === "validation") {
    return `Back to ${section.value.label}`;
  }
  if (activeFormParticipant.value) {
    return `Back to ${activeFormParticipant.value.subject_id}`;
  }
  if (zoomSite.value) return `Back to Site ${zoomSite.value} participants`;
  return `Back to ${studyCode.value} participants`;
});

const observationReturnLocation = computed(() => {
  if (openFormId.value) {
    return formLocationById(
      openFormId.value,
      highlightItemQuery.value || undefined
    );
  }
  if (isDataWorklistSection.value) {
    return studyLocation({
      stage: "data",
      section: section.value!.id,
      site: siteFilter.value || undefined,
    });
  }
  if (activeFormParticipant.value) {
    return recordLocationFor(activeFormParticipant.value);
  }
  if (zoomSite.value) {
    return studyLocation({
      stage: "conduct",
      section: "participants",
      site: zoomSite.value,
    });
  }
  return studyLocation();
});

const observationReturnLabel = computed(() => {
  if (openFormId.value) {
    return loadedForm.value && loadedForm.value.form_id === openFormId.value
      ? `Back to ${formatFormLabel(loadedForm.value)}`
      : "Back to form";
  }
  if (isDataWorklistSection.value) {
    return `Back to ${section.value!.label}`;
  }
  if (activeFormParticipant.value) {
    return `Back to ${activeFormParticipant.value.subject_id}`;
  }
  if (zoomSite.value) return `Back to Site ${zoomSite.value} participants`;
  return `Back to ${studyCode.value} home`;
});

async function resolveEnrollmentLineage(
  enrollmentRef: string
): Promise<{ req: number; siteCode: string; subjectCode: string } | null> {
  if (!enrollmentRef || typeof kernelApi.getZoomPath !== "function") {
    return null;
  }
  const req = ++lineageRequest;
  try {
    const steps = await kernelApi.getZoomPath(enrollmentRef);
    if (req !== lineageRequest) return null;
    const siteStep = steps.find(
      (s) => s.level === "study_site" && s.mask === null && s.label
    );
    const enrollmentStep = steps.find(
      (s) => s.level === "enrollment" && s.mask === null && s.label
    );
    if (siteStep?.label || enrollmentStep?.label) {
      const lineage = {
        siteCode: siteStep?.label ?? "",
        subjectCode: enrollmentStep?.label ?? "",
      };
      resolvedLineage.value = lineage;
      return { req, ...lineage };
    }
  } catch {
    // Keep whatever scope context is already known in the URL.
  }
  return null;
}

function onFormLoaded(form: EcrfForm) {
  loadedForm.value = form;
  const loadedRef = collectionInstanceRef(form.form_id);
  if (loadedRef) {
    seedNodeSummary(loadedRef, {
      kind: "collection_instance",
      title: formatFormLabel(form),
      status: form.complete ? "Complete" : "In progress",
    });
  }
  for (const item of form.items ?? []) {
    const obsRef = item.observation_id
      ? observationNodeRef(item.observation_id)
      : null;
    if (obsRef && item.concept) {
      seedNodeSummary(obsRef, {
        kind: "observation",
        code: item.concept,
        title: item.concept,
      });
    }
  }
  if (isNewForm.value && form.form_id) {
    const formRef = collectionInstanceRef(form.form_id);
    if (formRef) {
      router.replace(
        studyLocation({
          stage: "conduct",
          site: !activeParticipantNodeText.value
            ? zoomSite.value || undefined
            : undefined,
          participant: activeParticipantNodeText.value || undefined,
          form: formRef,
        })
      );
    }
  }
  if (!zoomParticipant.value && form.enrollment_id) {
    void resolveEnrollmentLineage(`enrollment:${form.enrollment_id}`);
  }
}

const {
  isConfirmOpen: isCanvasSignOutConfirmOpen,
  requestSignOut: requestCanvasSignOut,
  confirmSignOut: confirmCanvasSignOut,
  cancelSignOut: cancelCanvasSignOut,
} = useSignOutGuard({
  hasExternalDrafts: () => hasUserScopedDrafts(),
  onConfirmSignOut: async () => {
    await authStore.logout();
    if (
      router &&
      typeof router.replace === "function" &&
      route?.path !== "/login" &&
      route?.name !== "login"
    ) {
      await router.replace("/login");
    }
  },
});

function onFormClosed() {
  formWorkspaceDirty.value = false;
  loadedForm.value = null;
  router.push(formBackLocation.value);
}

async function onObservationLoaded(obs: KernelObservation) {
  loadedObservation.value = obs;
  const targetObsRef = obs.ref || openObservationRef.value;
  if (targetObsRef) {
    seedNodeSummary(targetObsRef, {
      kind: "observation",
      code: obs.concept,
      title: obs.concept,
      status: obs.locks?.length ? "Locked" : "Recorded",
    });
  }
  if (!zoomParticipant.value && obs.enrollment_ref) {
    const lineage = await resolveEnrollmentLineage(obs.enrollment_ref);
    if (lineage?.subjectCode && !zoomParticipant.value) {
      const participant = await canvas.resolveParticipantByCode(
        lineage.subjectCode
      );
      if (lineage.req === lineageRequest && participant) {
        resolvedParticipantId.value = participant.id;
      }
    }
  }
}

function selectVersion(version: KernelProtocolVersion) {
  if (!version.ref) return;
  inspectorRef.value?.expand();
  router.push(
    studyLocation({
      stage: "design",
      section: "protocol-versions",
      node: version.ref,
    })
  );
  nextTick(() =>
    document.getElementById("canvas-inspector")?.scrollIntoView?.({
      block: "nearest",
    })
  );
}

function applyParticipantFilter(filter: {
  status: string;
  siteId: string;
  q?: string;
}) {
  const nextSearch = (filter.q ?? "").trim();
  const location = studyLocation({
    stage: "conduct",
    section: "participants",
    status: filter.status,
    site: filter.siteId,
    q: nextSearch,
    node: single(route.query.node),
  });
  const onlySearchChanged =
    filter.status === statusFilter.value &&
    filter.siteId === siteFilter.value &&
    nextSearch !== participantSearchFilter.value;
  if (onlySearchChanged) {
    router.replace(location);
  } else {
    router.push(location);
  }
}

function applyStudyQueryFilter(filter: StudyQueriesFilterState) {
  router.replace(
    studyLocation({
      stage: "data",
      section: "queries",
      status: filter.status,
      owner: filter.owner,
      origin: filter.origin,
      priority: filter.priority,
      site: filter.site,
      subject: filter.participant,
      changed: filter.changed,
    })
  );
}

function drillToParticipants(status: string) {
  router.push(
    studyLocation({ stage: "conduct", section: "participants", status })
  );
}

function openParticipantsForSite(siteCode: string) {
  router.push(
    studyLocation({
      stage: "conduct",
      section: "participants",
      site: siteCode,
    })
  );
}

function goToPaletteTarget(target: PaletteTarget) {
  paletteOpen.value = false;
  if (target.ask) {
    inspectorRef.value?.expand();
    // Ask keeps the view and its selection, and opens Assist with the question.
    router.push({
      name: "study",
      params: { studyCode: target.studyCode },
      query: { ...route.query, ask: target.ask },
    });
    return;
  }
  if (target.participant) {
    if (target.studyCode && target.studyCode !== canvas.studyCode) {
      canvas.openStudy(target.studyCode);
    }
    canvas.rememberParticipants([target.participant]);
    inspectorRef.value?.expand();
  }
  const node = target.participant
    ? tryNodeRef("participant", target.participant.id)
    : null;
  router.push(
    studyLocation(
      {
        stage: target.stage,
        section: target.section,
        node: node ? formatNodeRef(node) : "",
      },
      target.studyCode
    )
  );
}

function openTypedStudy() {
  const code = typedStudy.value.trim();
  if (code) router.push(studyLocation({}, code));
}

// Switching study drops everything read for the previous one.
watch(
  [studyCode, () => single(route.query.studyId ?? route.query.study_id).trim()],
  ([code, queryStudyCode]) => {
    if (!code && queryStudyCode) {
      const nextQuery: LocationQueryRaw = { ...route.query };
      delete nextQuery.studyId;
      delete nextQuery.study_id;
      router.replace({
        name: "study",
        params: { studyCode: queryStudyCode },
        query: nextQuery,
      });
      return;
    }
    formWorkspaceDirty.value = false;
    loadedForm.value = null;
    loadedObservation.value = null;
    resolvedLineage.value = null;
    resolvedParticipantId.value = "";
    lineageRequest++;
    canvas.openStudy(code);
    if (code) {
      setActiveStudyCode(code);
    } else {
      loadStudyDirectory();
    }
  },
  { immediate: true }
);

watch(callerAuthKey, (next, prev) => {
  if (prev !== undefined && next !== prev) {
    canvas.clearParticipantAllocationCache();
  }
});

watch(
  [
    studyCode,
    () => recordRef.value?.id,
    () => selectedNode.value?.id,
    () => loadedForm.value?.participant_id,
    () => codeQueryParticipant.value?.id,
    callerAuthKey,
  ],
  ([code, recordId, selectedId, formParticipantId, codeParticipantId]) => {
    if (!code) return;
    const ids = new Set(
      [recordId, selectedId, formParticipantId, codeParticipantId].filter(
        (id): id is string => Boolean(id)
      )
    );
    for (const id of ids) {
      const known = canvas.knownParticipants[id];
      if (!known) {
        void canvas.ensureParticipantById(id);
      } else if (
        known.treatment_group === undefined &&
        [
          "RANDOMIZED",
          "ACTIVE",
          "UNBLINDED",
          "RECONSENT_REQUIRED",
          "COMPLETED",
          "WITHDRAWN",
        ].includes(known.status)
      ) {
        void refreshParticipantDetailMutation(canvas, id);
      }
    }
  },
  { immediate: true }
);

watch(
  [studyCode, conductSubjectQuery],
  async ([code, subjectCode]) => {
    const seq = ++conductSubjectSeq;
    if (!code || !subjectCode) {
      isResolvingConductSubject.value = false;
      return;
    }
    isResolvingConductSubject.value = true;
    try {
      const resolved = await canvas.resolveParticipantByCode(subjectCode, code);
      if (seq !== conductSubjectSeq) return;
      const node = resolved ? tryNodeRef("participant", resolved.id) : null;
      if (node) {
        await router.replace(
          studyLocation(
            {
              stage: "conduct",
              site: siteFilter.value || undefined,
              participant: formatNodeRef(node),
              form: formQuery.value || undefined,
              spec: initialSpecId.value || undefined,
              visit: initialVisitId.value || undefined,
              item: highlightItemQuery.value || undefined,
            },
            code
          )
        );
      } else {
        await router.replace(
          studyLocation(
            {
              stage: "conduct",
              section: "participants",
              site: siteFilter.value || undefined,
              q: subjectCode,
            },
            code
          )
        );
      }
    } finally {
      if (seq === conductSubjectSeq) {
        isResolvingConductSubject.value = false;
      }
    }
  },
  { immediate: true }
);

watch([paletteOpen, studyCode], ([open, code]) => {
  if (!inAppShell && open && code) {
    void canvas.ensurePaletteParticipants(code);
  }
});

watch(openFormId, (id) => {
  if (!id) {
    loadedForm.value = null;
  } else if (loadedForm.value && loadedForm.value.form_id !== id) {
    loadedForm.value = null;
  }
});

watch(openObservationRef, (refText) => {
  resolvedParticipantId.value = "";
  lineageRequest++;
  if (!refText) {
    loadedObservation.value = null;
  }
});

watch([hasOpenForm, openObservationRef], ([formOpen, obsRef]) => {
  if (!formOpen && !obsRef) {
    resolvedLineage.value = null;
    resolvedParticipantId.value = "";
    lineageRequest++;
  }
});

// Open a participant's data once the canvas knows which participant it is.
watch(
  [studyCode, () => recordParticipant.value?.subject_id],
  ([, code]) => {
    const participant = recordParticipant.value;
    if (participant && code && canvas.record.subjectCode !== code)
      canvas.loadRecord(participant);
  },
  { immediate: true }
);

watch(
  [studyCode, participantCodeQuery],
  ([code, subjectCode]) => {
    if (!code || !subjectCode || codeQueryParticipant.value) return;
    void canvas.resolveParticipantByCode(subjectCode, code);
  },
  { immediate: true }
);

watch(
  [studyCode, zoomSite, zoomParticipantCode],
  ([code, site, subject]) => {
    syncInstantiatedClinicalScope(
      code
        ? {
            activeStudyId: code,
            activeSiteId: site || "",
            activeSubjectId: subject || "",
          }
        : {
            activeStudyId: "",
            activeSiteId: "",
            activeSubjectId: "",
          }
    );
  },
  { immediate: true }
);

// Load what the current view shows, once per study and filter.
watch(
  [
    studyCode,
    () => stage.value?.id,
    () => section.value?.id,
    statusFilter,
    siteFilter,
    participantSearchFilter,
    isParticipantRecordOpen,
  ],
  () => {
    if (
      !studyCode.value ||
      isParticipantRecordOpen.value ||
      hasOpenForm.value ||
      openObservationRef.value
    )
      return;
    if (!stage.value) {
      if (!canvas.pulse && !canvas.pulseLoading) canvas.loadPulse();
      return;
    }
    if (section.value?.id !== "participants") return;
    const filter = {
      status: statusFilter.value,
      siteId: siteFilter.value,
      q: participantSearchFilter.value,
    };
    const loaded = canvas.participantsFilter;
    const sameFilter =
      (loaded.status ?? "") === filter.status &&
      (loaded.siteId ?? "") === filter.siteId &&
      (loaded.q ?? "") === filter.q;
    const hasPage =
      canvas.participants.length > 0 || canvas.participantsTotal > 0;
    if (!sameFilter || (!hasPage && !canvas.participantsLoading)) {
      canvas.loadParticipants({
        status: filter.status || undefined,
        siteId: filter.siteId || undefined,
        q: filter.q || undefined,
      });
    }
  },
  { immediate: true }
);

const UNSAVED_FORM_CONFIRM_MESSAGE =
  "You have unsaved eCRF draft changes. Are you sure you want to leave?";

function leavesActiveForm(to: RouteLocationNormalized): boolean {
  const nextStudyCode = single(to.params.studyCode).trim();
  if (!nextStudyCode || nextStudyCode !== studyCode.value) return true;
  if (parseNodeRef(to.query.proposal)?.kind === "proposal") return true;
  if (observationNodeRef(to.query.observation)) return true;

  const nextFormQuery = single(to.query.form).trim();
  if (nextFormQuery === "new") {
    if (!isNewForm.value) return true;
    const nextSpec = single(to.query.spec).trim();
    const nextVisit = single(to.query.visit).trim();
    const nextParticipant = single(to.query.participant).trim();
    return (
      nextSpec !== initialSpecId.value ||
      nextVisit !== initialVisitId.value ||
      nextParticipant !== rawParticipantQuery.value
    );
  }
  const nextFormId = collectionInstanceId(nextFormQuery);
  if (!nextFormId) return true;

  const currentFormId = loadedForm.value?.form_id || openFormId.value;
  return Boolean(currentFormId && nextFormId !== currentFormId);
}

function shouldSuppressUnsavedGuard(to?: RouteLocationNormalized): boolean {
  if (
    !authStore.isAuthenticated ||
    authStore.sessionExpired ||
    authStore.isLoggingOut
  ) {
    return true;
  }
  if (
    to &&
    (to.name === "forbidden" ||
      to.path === "/forbidden" ||
      to.name === "login" ||
      to.path === "/login")
  ) {
    return true;
  }
  return false;
}

function handleBeforeUnload(event: BeforeUnloadEvent) {
  if (formWorkspaceDirty.value && !shouldSuppressUnsavedGuard()) {
    event.preventDefault();
    event.returnValue = "";
    return "";
  }
}

async function confirmDiscardUnsavedForm(): Promise<boolean> {
  return Boolean(
    await confirmDialog({
      title: "Unsaved Form Changes",
      message: UNSAVED_FORM_CONFIRM_MESSAGE,
      confirmLabel: "Leave Without Saving",
      cancelLabel: "Stay on Form",
      variant: "warning",
    })
  );
}

if (typeof onBeforeRouteLeave === "function") {
  onBeforeRouteLeave((to) => {
    if (formWorkspaceDirty.value && !shouldSuppressUnsavedGuard(to)) {
      return confirmDiscardUnsavedForm().then((confirmed) =>
        confirmed ? undefined : false
      );
    }
  });
}

if (typeof onBeforeRouteUpdate === "function") {
  onBeforeRouteUpdate((to) => {
    if (
      formWorkspaceDirty.value &&
      !shouldSuppressUnsavedGuard(to) &&
      leavesActiveForm(to)
    ) {
      return confirmDiscardUnsavedForm().then((confirmed) =>
        confirmed ? undefined : false
      );
    }
  });
}

function onGlobalKeydown(event: KeyboardEvent) {
  if (inAppShell) return;
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "e") {
    canvas.handleExplainShortcut(event);
    return;
  }
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    if (hasActiveModalFocusTrap({ excludeId: "canvas-palette" })) {
      return;
    }
    paletteOpen.value = !paletteOpen.value;
  }
}

watch(
  () => ({
    studyCode: studyCode.value,
    stage: stage.value?.id || "",
    section: section.value?.id || "",
    siteCode: zoomSite.value || "",
    subjectCode: zoomParticipantCode.value || canvas.record.subjectCode || "",
    nodeRef: inspectedNodeText.value || "",
    participantNodeRef: activeParticipantNodeText.value || "",
    form: formQuery.value || "",
    formId: openFormId.value || "",
    formSpecId:
      loadedForm.value?.collection_spec_id || initialSpecId.value || "",
    isNewForm: isNewForm.value,
    observationRef: openObservationRef.value || "",
    proposalRef: proposalRef.value || "",
  }),
  (scopeSnapshot) => {
    knowledgeStore.setCanvasEscalationContext(scopeSnapshot);
  },
  { immediate: true }
);

const SECTION_HEADING_TARGETS: Record<string, string[]> = {
  participants: ["#participants-title"],
  "protocol-versions": ["#protocol-versions-title"],
  sites: ["#sites-section-title", "#study-sites-title"],
  "site-activation": ["#site-activation-title"],
  "startup-greenlight": ["#ctms-greenlight-title"],
  "site-milestones": ["#ctms-milestones-title"],
  delegation: ["#ctms-delegation-title"],
  "monitoring-visits": ["#ctms-monitoring-title"],
  supply: ["#rtsm-supply-title"],
  documents: [
    "#document-grid-heading",
    "#cra-qc-queue-heading",
    "#btn-etmf-tab-binder",
  ],
  training: ["#study-training-title"],
  queries: ["#study-queries-title"],
  locks: ["#study-locks-title"],
  validation: ["#study-validation-title"],
  exports: ["#study-exports-title"],
};

interface WorkspaceBranchSnapshot {
  key: string;
  groupKey: string;
  depth: number;
  targets: Array<string | (() => HTMLElement | null)>;
  subjectCode: string;
  formId: string;
  isNewForm: boolean;
  specId: string;
  visitId: string;
  highlightItemId: string;
}

const workspaceBranch = computed<WorkspaceBranchSnapshot>(() => {
  const code = studyCode.value;
  const baseContext = {
    subjectCode: zoomParticipantCode.value,
    formId: openFormId.value || "",
    isNewForm: isNewForm.value,
    specId: initialSpecId.value,
    visitId: initialVisitId.value,
    highlightItemId: highlightItemQuery.value,
  };

  if (!code) {
    return {
      key: "open-study",
      groupKey: "open-study",
      depth: 0,
      targets: ["#canvas-study-title", "#open-study-code"],
      ...baseContext,
    };
  }

  if (proposalRef.value) {
    return {
      key: `study:${code}:proposal:${proposalRef.value}`,
      groupKey: `study:${code}:proposal`,
      depth: 4,
      targets: ["#proposal-review-title", "#proposal-review-status"],
      ...baseContext,
    };
  }

  if (openObservationRef.value) {
    return {
      key: `study:${code}:observation:${openObservationRef.value}`,
      groupKey: `study:${code}:observation`,
      depth: 4,
      targets: ["#observation-view-title"],
      ...baseContext,
    };
  }

  if (hasOpenForm.value) {
    if (
      isNewForm.value &&
      recordRef.value &&
      !recordParticipant.value &&
      isRecordParticipantLoading.value
    ) {
      return {
        key: `study:${code}:form:record-loading`,
        groupKey: `study:${code}:form`,
        depth: 3,
        targets: ["#record-loading"],
        ...baseContext,
      };
    }
    if (isNewForm.value && recordRef.value && !recordParticipant.value) {
      return {
        key: `study:${code}:form:record-not-loaded`,
        groupKey: `study:${code}:form`,
        depth: 3,
        targets: ["#record-not-loaded"],
        ...baseContext,
      };
    }
    const formTargetKey = openFormId.value || "new";
    return {
      key: `study:${code}:form:${formTargetKey}`,
      groupKey: `study:${code}:form`,
      depth: 3,
      targets: [
        ...(highlightItemQuery.value
          ? [`#ecrf-item-${highlightItemQuery.value}`]
          : []),
        "#canvas-form-workspace-title",
      ],
      ...baseContext,
    };
  }

  if (recordRef.value) {
    const recId = recordRef.value.id;
    if (recordParticipant.value) {
      return {
        key: `study:${code}:record:${recId}:ready`,
        groupKey: `study:${code}:record:${recId}`,
        depth: 2,
        targets: ["#participant-record-title"],
        ...baseContext,
      };
    }
    if (isRecordParticipantLoading.value) {
      return {
        key: `study:${code}:record:${recId}:loading`,
        groupKey: `study:${code}:record:${recId}`,
        depth: 2,
        targets: ["#record-loading"],
        ...baseContext,
      };
    }
    return {
      key: `study:${code}:record:${recId}:missing`,
      groupKey: `study:${code}:record:${recId}`,
      depth: 2,
      targets: ["#record-not-loaded"],
      ...baseContext,
    };
  }

  if (isResolvingConductSubject.value) {
    return {
      key: `study:${code}:resolving-subject`,
      groupKey: `study:${code}:resolving-subject`,
      depth: 2,
      targets: ["#record-loading"],
      ...baseContext,
    };
  }

  if (!stage.value) {
    return {
      key: `study:${code}:home`,
      groupKey: `study:${code}:home`,
      depth: 1,
      targets: ["#study-pulse-title"],
      ...baseContext,
    };
  }

  if (section.value?.id) {
    return {
      key: `study:${code}:section:${section.value.id}`,
      groupKey: `study:${code}:section:${section.value.id}`,
      depth: 1,
      targets: SECTION_HEADING_TARGETS[section.value.id] ?? [
        "#stage-overview-title",
      ],
      ...baseContext,
    };
  }

  return {
    key: `study:${code}:stage:${stage.value.id}`,
    groupKey: `study:${code}:stage:${stage.value.id}`,
    depth: 1,
    targets: ["#stage-overview-title"],
    ...baseContext,
  };
});

const savedBranchTriggers = new Map<string, string>();
let returnFocusObserver: MutationObserver | null = null;
let returnFocusTimer: ReturnType<typeof setTimeout> | null = null;

function stopReturnFocusObserver() {
  if (returnFocusObserver) {
    returnFocusObserver.disconnect();
    returnFocusObserver = null;
  }
  if (returnFocusTimer) {
    clearTimeout(returnFocusTimer);
    returnFocusTimer = null;
  }
}

function escapeId(id: string): string {
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
    return `#${CSS.escape(id)}`;
  }
  return `#${id.replace(/([^\w-])/g, "\\$1")}`;
}

function captureElementTriggerSelector(el: Element | null): string | null {
  if (!el || typeof document === "undefined") return null;
  const workspace = document.getElementById("canvas-workspace");
  const interactive = el.closest?.("a, button, [role='button']") ?? el;
  if (!workspace || !workspace.contains(interactive)) return null;
  const rawId = interactive.getAttribute("id");
  if (rawId) {
    return escapeId(rawId);
  }
  const testId = interactive.getAttribute("data-testid");
  if (testId) {
    return `[data-testid="${testId.replace(/"/g, '\\"')}"]`;
  }
  return null;
}

function onWorkspaceInteractionCapture(event: Event) {
  if (
    event.type === "keydown" &&
    event instanceof KeyboardEvent &&
    event.key !== "Enter" &&
    event.key !== " "
  ) {
    return;
  }
  const target = event.target instanceof Element ? event.target : null;
  const selector = captureElementTriggerSelector(target);
  if (selector) {
    savedBranchTriggers.set(workspaceBranch.value.groupKey, selector);
  }
}

watch(
  workspaceBranch,
  (next, prev) => {
    if (!prev || next.key === prev.key) return;
    stopReturnFocusObserver();

    if (typeof document !== "undefined") {
      const activeSelector = captureElementTriggerSelector(
        document.activeElement
      );
      if (activeSelector) {
        savedBranchTriggers.set(prev.groupKey, activeSelector);
      }
    }

    // Opening a new form inside CanvasFormWorkspace replaces ?form=new with ?form=collection_instance:...
    if (
      prev.groupKey === next.groupKey &&
      prev.isNewForm &&
      Boolean(next.formId)
    ) {
      void handoffFocus([
        ...(next.highlightItemId ? [`#ecrf-item-${next.highlightItemId}`] : []),
        () =>
          document.querySelector<HTMLElement>(
            "#ecrf-form-page input:not([disabled]), #ecrf-form-page select:not([disabled])"
          ),
        "#canvas-form-workspace-title",
      ]);
      return;
    }

    const isZoomOut = next.depth < prev.depth;
    const returnSelectors: string[] = [];
    if (isZoomOut) {
      const saved = savedBranchTriggers.get(next.groupKey);
      if (saved) returnSelectors.push(saved);
      if (prev.formId) {
        returnSelectors.push(`#record-form-link-${prev.formId}`);
      }
      if (prev.specId && prev.visitId) {
        returnSelectors.push(`#matrix-open-${prev.specId}-${prev.visitId}`);
      } else if (prev.specId) {
        returnSelectors.push(`#unscheduled-open-${prev.specId}`);
      }
      if (prev.highlightItemId && prev.depth === 4) {
        returnSelectors.push(
          `#ecrf-item-observation-${prev.highlightItemId}`,
          `#ecrf-item-${prev.highlightItemId}`
        );
      }
      if (prev.subjectCode) {
        returnSelectors.push(`#participant-${prev.subjectCode}`);
      }
    }

    void (async () => {
      const focused = await handoffFocus([...returnSelectors, ...next.targets]);
      if (
        !isZoomOut ||
        returnSelectors.length === 0 ||
        !focused ||
        typeof MutationObserver === "undefined" ||
        typeof document === "undefined"
      ) {
        return;
      }
      const matchedReturnImmediately = returnSelectors.some(
        (sel) => document.querySelector(sel) === focused
      );
      if (matchedReturnImmediately) return;

      const workspace = document.getElementById("canvas-workspace");
      if (!workspace) return;

      returnFocusObserver = new MutationObserver(() => {
        if (document.activeElement !== focused) {
          stopReturnFocusObserver();
          return;
        }
        for (const sel of returnSelectors) {
          const candidate = document.querySelector<HTMLElement>(sel);
          if (candidate && !candidate.hasAttribute("disabled")) {
            stopReturnFocusObserver();
            void handoffFocus(candidate, { awaitNextTick: false });
            return;
          }
        }
      });
      returnFocusObserver.observe(workspace, {
        childList: true,
        subtree: true,
      });
      returnFocusTimer = setTimeout(() => stopReturnFocusObserver(), 1500);
    })();
  },
  { flush: "sync" }
);

function handleDemoReset() {
  formWorkspaceDirty.value = false;
  loadedForm.value = null;
  loadedObservation.value = null;
  resolvedLineage.value = null;
  resolvedParticipantId.value = "";
  lineageRequest++;
  const code = studyCode.value;
  canvas.openStudy(code);
  if (!code) {
    loadStudyDirectory();
  } else if (!stage.value) {
    canvas.loadPulse();
  } else if (section.value?.id === "participants") {
    canvas.loadParticipants({
      status: statusFilter.value || undefined,
      siteId: siteFilter.value || undefined,
      q: participantSearchFilter.value || undefined,
    });
  }
}

let unregisterCanvasExplain: (() => void) | null = null;

onMounted(() => {
  unregisterCanvasExplain = canvas.registerExplainCoordinator((options) => {
    if (options?.fromShortcut) return false;
    const targetRef = options?.targetNodeRef;
    if (targetRef && parseNodeRef(targetRef)) {
      void router
        .push({
          name: "study",
          params: { studyCode: studyCode.value },
          query: {
            ...route.query,
            node: targetRef,
          },
        })
        .then(() => {
          void nextTick(() => {
            inspectorRef.value?.openExplain?.();
          });
        });
      return true;
    }
    return Boolean(inspectorRef.value?.openExplain?.(options));
  });
  window.addEventListener("beforeunload", handleBeforeUnload);
  window.addEventListener("cadence:demo-reset", handleDemoReset);
  document.addEventListener("click", onWorkspaceInteractionCapture, true);
  document.addEventListener("keydown", onWorkspaceInteractionCapture, true);
  if (!inAppShell) {
    document.addEventListener("keydown", onGlobalKeydown);
  }
  syncTopbarHeight();
  if (
    typeof ResizeObserver !== "undefined" &&
    topbarRef.value instanceof HTMLElement
  ) {
    topbarResizeObserver = new ResizeObserver(() => syncTopbarHeight());
    topbarResizeObserver.observe(topbarRef.value);
  }
});
onUnmounted(() => {
  knowledgeStore.clearCanvasEscalationContext();
  stopReturnFocusObserver();
  unregisterCanvasExplain?.();
  unregisterCanvasExplain = null;
  window.removeEventListener("beforeunload", handleBeforeUnload);
  window.removeEventListener("cadence:demo-reset", handleDemoReset);
  document.removeEventListener("click", onWorkspaceInteractionCapture, true);
  document.removeEventListener("keydown", onWorkspaceInteractionCapture, true);
  document.removeEventListener("keydown", onGlobalKeydown);
  if (topbarResizeObserver) {
    topbarResizeObserver.disconnect();
    topbarResizeObserver = null;
  }
});
</script>

<style scoped>
.canvas {
  --canvas-sticky-offset: 0px;
  --canvas-pane-top: calc(
    var(--canvas-sticky-offset) + var(--canvas-topbar-height, 45px)
  );
  display: flex;
  flex-direction: column;
  min-height: 100vh;
  background: var(--semantic-color-surface-muted);
  color: var(--primary);
  font-family: var(--font);
}

.canvas.canvas-in-shell {
  --canvas-sticky-offset: var(--shell-header-height, 57px);
  min-height: calc(100vh - var(--canvas-sticky-offset));
}

.skip-link {
  position: absolute;
  left: -9999px;
}

.skip-link:focus {
  left: 16px;
  top: 8px;
  z-index: 60;
}

.topbar {
  position: sticky;
  top: var(--canvas-sticky-offset);
  z-index: 20;
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 2px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--semantic-color-surface);
}

.zoom-path {
  flex: 1;
  min-width: 0;
}

.zoom-path ol {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.9rem;
}

.zoom-path li {
  display: inline-flex;
  align-items: center;
}

.zoom-path li + li::before {
  content: "/";
  margin-right: 4px;
  color: var(--neutral-dark);
}

.zoom-path a {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: 44px;
  min-width: 44px;
  padding: 0 6px;
  border-radius: 6px;
  color: inherit;
}

.zoom-path [aria-current="page"] {
  display: inline-flex;
  align-items: center;
  min-height: 44px;
  padding: 0 6px;
  font-weight: 600;
}

@media (min-width: 768px) and (max-width: 1280px) {
  .topbar {
    padding: 0 12px;
    gap: 8px;
  }

  .zoom-path ol {
    flex-wrap: nowrap;
    overflow-x: auto;
    font-size: 0.84rem;
  }

  .study-header {
    padding: 8px 12px;
    gap: 6px 16px;
  }
}

.palette-trigger {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.ai-toggle {
  padding: 6px 10px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.ai-toggle[aria-pressed="true"] {
  border-color: var(--semantic-color-info-border);
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-info);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
}

.palette-trigger kbd {
  font-family: inherit;
  font-size: 0.8rem;
}

.user {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.9rem;
}

.study-header {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 24px;
  padding: 12px 16px;
  border-bottom: 1px solid var(--border);
  background: var(--semantic-color-surface);
}

.study-header h1 {
  margin: 0;
  font-size: 1.25rem;
}

.lifecycle-rail {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.stage {
  display: inline-block;
  padding: 4px 12px;
  border-radius: 999px;
  color: var(--neutral-dark);
  text-decoration: none;
  font-size: 0.85rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.stage.current {
  background: var(--accent-bg);
  color: var(--primary);
  font-weight: 600;
}

.panes {
  flex: 1;
  display: grid;
  grid-template-columns: 14rem minmax(0, 1fr) 20rem;
  min-height: 0;
}

.panes.panes--inspector-collapsed {
  grid-template-columns: 14rem minmax(0, 1fr) 2.75rem;
}

.navigator {
  position: sticky;
  top: var(--canvas-pane-top);
  align-self: start;
  max-height: calc(100vh - var(--canvas-pane-top));
  overflow-y: auto;
  padding: 12px;
  background: var(--semantic-color-surface);
}

.nav-group-title {
  margin: 16px 0 4px;
  color: var(--neutral-dark);
  font-size: 0.75rem;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.navigator ul {
  margin: 0;
  padding: 0;
  list-style: none;
}

.nav-link {
  display: block;
  padding: 6px 8px;
  border-radius: 6px;
  color: inherit;
  text-decoration: none;
}

.nav-link.current {
  background: var(--accent-bg);
  font-weight: 600;
}

.nav-empty {
  margin: 0;
  padding: 0 8px;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.workspace {
  padding: 16px;
  min-width: 0;
  border-left: 1px solid var(--border);
  border-right: 1px solid var(--border);
}

.canvas :deep(#canvas-inspector) {
  position: sticky;
  top: var(--canvas-pane-top);
  align-self: start;
  max-height: calc(100vh - var(--canvas-pane-top));
  overflow-y: auto;
  background: var(--semantic-color-surface);
}

.stage-overview h2 {
  margin: 0 0 4px;
  font-size: 1rem;
}

.stage-scope,
.stage-empty,
.stage-section-description,
.open-study-note {
  color: var(--neutral-dark);
}

.stage-sections {
  padding-left: 18px;
}

.open-study {
  max-width: 54rem;
  padding: 32px 16px;
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.open-study h1 {
  margin: 0;
}

.open-study-form {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.open-study-form label {
  font-weight: 600;
}

.open-study-form input {
  flex: 1 1 12rem;
  min-width: 0;
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
}

.directory-section {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.directory-section h2 {
  margin: 0;
  font-size: 1.05rem;
}

.directory-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: baseline;
  gap: 8px;
}

.directory-status {
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.visible-studies-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.study-directory-card {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 12px;
  padding: 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: var(--semantic-color-surface-muted);
}

.study-card-main {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 0;
}

.study-card-top {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.study-card-code {
  font-size: 0.98rem;
}

.study-phase-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
  font-size: 0.75rem;
  font-weight: 600;
}

.study-card-title {
  font-size: 0.9rem;
}

.study-card-sponsor {
  font-size: 0.8rem;
  color: var(--neutral-dark);
}

.study-card-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.inline-banner {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}

.inline-banner-progress {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}

.inline-banner-success {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
}

.inline-banner-error {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
}

.create-study-form {
  display: flex;
  flex-direction: column;
  gap: 14px;
  align-items: flex-start;
}

.create-study-grid {
  width: 100%;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 220px), 1fr));
  gap: 12px;
}

.create-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  min-width: 0;
}

.create-field-wide {
  grid-column: 1 / -1;
}

.create-field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.create-field input:not(.sr-only),
.create-field select {
  padding: 8px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font: inherit;
  font-size: 0.9rem;
  width: 100%;
  box-sizing: border-box;
}

.create-file-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.create-file-note {
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

@media (max-width: 960px) {
  .panes,
  .panes.panes--inspector-collapsed {
    grid-template-columns: minmax(0, 1fr);
  }

  .navigator {
    position: static;
    max-height: none;
    overflow-y: visible;
    align-self: stretch;
    border-bottom: 1px solid var(--border);
  }

  .workspace {
    border-left: 0;
    border-right: 0;
  }

  .canvas :deep(#canvas-inspector) {
    position: static;
    max-height: none;
    overflow-y: visible;
    align-self: stretch;
    border-top: 1px solid var(--border);
  }
}
</style>
