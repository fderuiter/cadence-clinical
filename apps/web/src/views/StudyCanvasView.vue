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
        <h1 id="canvas-study-title">
          {{ studyCode }}
        </h1>
        <nav aria-label="Lifecycle">
          <ol class="lifecycle-rail">
            <li v-for="item in STAGES" :key="item.id">
              <router-link
                :id="`stage-${item.id}`"
                :to="studyLocation({ stage: item.id })"
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
            :to="studyLocation()"
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
                  :to="studyLocation({ stage: group.id, section: item.id })"
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
            >
              Loading participant…
            </p>
            <p
              v-else-if="isNewForm && recordRef && !recordParticipant"
              id="record-not-loaded"
              class="stage-empty"
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
                activeFormParticipant
                  ? ecrfHandoffLocationFor(activeFormParticipant)
                  : null
              "
              :form-location="formLocationById"
              :observation-location="observationLocationFromForm"
              @dirty-change="formWorkspaceDirty = $event"
              @form-loaded="onFormLoaded"
              @closed="onFormClosed"
            />
          </template>

          <template v-else-if="recordRef">
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
              :handoff-location="ecrfHandoffLocationFor(recordParticipant)"
              :observation-location="observationLocationFromRecord"
              @retry="canvas.loadRecord(recordParticipant)"
            />
            <p
              v-else-if="isRecordParticipantLoading"
              id="record-loading"
              class="stage-empty"
              role="status"
            >
              Loading participant…
            </p>
            <p v-else id="record-not-loaded" class="stage-empty">
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

          <template v-else-if="!stage">
            <StudyPulse @drill="drillToParticipants" />
            <NeedsAttention
              :key="`attention-${studyCode}`"
              :study-code="studyCode"
            />
            <StudyActivity :key="studyCode" :study-code="studyCode" />
          </template>

          <ParticipantsSection
            v-else-if="section?.id === 'participants'"
            :status="statusFilter"
            :site-id="siteFilter"
            :query="participantSearchFilter"
            :selected-id="selectedNode?.id ?? ''"
            :can-manage-participants="canChangeParticipants"
            @select="selectParticipant"
            @filter="applyParticipantFilter"
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

          <DocumentManagerView
            v-else-if="section?.id === 'documents'"
            :key="`docs-${studyCode}`"
            :study-id="studyCode"
          />

          <StudyTrainingView
            v-else-if="section?.id === 'training'"
            :key="`training-${studyCode}`"
            :study-id="studyCode"
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
            <h2 id="stage-overview-title">
              {{ stage.label }}
            </h2>
            <p class="stage-scope">{{ stage.scope }}.</p>
            <ul v-if="stage.sections.length" class="stage-sections">
              <li v-for="item in stage.sections" :key="item.id">
                <router-link
                  :to="studyLocation({ stage: stage.id, section: item.id })"
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
            inspectedParticipant &&
            (inspectedParticipant !== recordParticipant ||
              hasOpenForm ||
              Boolean(openObservationRef))
              ? recordLocationFor(inspectedParticipant)
              : null
          "
          :data-entry-location="
            inspectedParticipant &&
            !(isNewForm && inspectedParticipant === recordParticipant)
              ? dataEntryLocationFor(inspectedParticipant)
              : null
          "
          :handoff-location="
            inspectedParticipant
              ? ecrfHandoffLocationFor(inspectedParticipant)
              : null
          "
          :can-manage-participants="canChangeParticipants"
          :assist-prompt="askText"
          @update:collapsed="inspectorCollapsed = $event"
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
      <h1 id="canvas-study-title">Open a study</h1>
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
                Sponsor: {{ item.sponsor_name }} ({{ item.sponsor_code }})
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
                  @click="clearCreateStudyFile"
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
import {
  describeKernelError,
  kernelApi,
  type KernelNode,
  type KernelObservation,
  type KernelOrganizationSummary,
  type KernelProtocolVersion,
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
  type NodeRef,
} from "../canvas/nodeRef";
import {
  canAnswerQueries,
  canManageParticipants,
  canManageQueries,
  canManageRulePacks,
  canVerifySource,
} from "../canvas/authority";
import type { PaletteTarget } from "../canvas/palette";
import { SignOutConfirmModal, useSignOutGuard } from "ui";
import { useAuthStore } from "../stores/auth";
import { useStudyCanvasStore } from "../stores/studyCanvas";
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
import QueriesSection, {
  type StudyQueriesFilterState,
} from "../components/canvas/QueriesSection.vue";
import StudyActivity from "../components/canvas/StudyActivity.vue";
import StudyPulse from "../components/canvas/StudyPulse.vue";
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
const canvas = useStudyCanvasStore();
const inAppShell = inject<boolean>("inAppShell", false);

const { enabled: aiEnabled, setEnabled: setAiEnabled } = useAiAssistance();

const paletteOpen = ref(false);
const typedStudy = ref("");
const inspectorCollapsed = ref(true);
const inspectorRef = ref<InstanceType<typeof CanvasInspector> | null>(null);
const topbarRef = ref<HTMLElement | null>(null);
const topbarHeight = ref(0);
let topbarResizeObserver: ResizeObserver | null = null;

function applyRootTopbarHeight(pxValue: number) {
  if (typeof document !== "undefined" && document.documentElement) {
    document.documentElement.style.setProperty(
      "--canvas-topbar-height",
      `${pxValue}px`
    );
  }
}

function syncTopbarHeight() {
  const el = topbarRef.value;
  if (!el || typeof el.getBoundingClientRect !== "function") {
    topbarHeight.value = 0;
    applyRootTopbarHeight(0);
    return;
  }
  if (
    typeof window !== "undefined" &&
    typeof window.getComputedStyle === "function"
  ) {
    const pos = window.getComputedStyle(el).position;
    if (pos && pos !== "sticky") {
      topbarHeight.value = 0;
      applyRootTopbarHeight(0);
      return;
    }
  }
  const measured = Math.ceil(el.getBoundingClientRect().height);
  topbarHeight.value = measured > 0 ? measured : 0;
  applyRootTopbarHeight(topbarHeight.value);
}

const hasStudyDirectoryService = computed(
  () => typeof kernelApi.listStudies === "function"
);
const hasCreateStudyService = computed(
  () => typeof kernelApi.createStudy === "function"
);

const visibleStudies = ref<KernelStudySummary[]>([]);
const studyDirectoryLoading = ref(false);
const studyDirectoryError = ref("");
const sponsorOrganizations = ref<KernelOrganizationSummary[]>([]);

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
  } catch (err) {
    studyDirectoryError.value = describeKernelError(err);
  } finally {
    studyDirectoryLoading.value = false;
  }

  if (typeof kernelApi.listOrganizations === "function") {
    try {
      const orgs = await kernelApi.listOrganizations("Sponsor");
      sponsorOrganizations.value = orgs;
      if (orgs.length > 0 && selectedSponsorChoice.value === "__new__") {
        selectedSponsorChoice.value = orgs[0].ref;
      }
    } catch {
      // Custom sponsor entry remains available if organization listing fails.
    }
  }
}

function openStudyByCode(code: string) {
  const clean = code.trim();
  if (clean) router.push(studyLocation({}, clean));
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
        reason: newStudyReason.value.trim(),
        idempotencyKey: createIdempotencyKey.value,
      });
      pendingCreatedStudy.value = created;

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
const siteFilter = computed(() => single(route.query.site).trim());
const participantSearchFilter = computed(() => single(route.query.q).trim());
const queryParticipantFilter = computed(() =>
  single(route.query.subject).trim()
);
const queryChangedFilter = computed(() => {
  const changed = single(route.query.changed).toLowerCase();
  return changed === "true" || changed === "false" ? changed : "";
});
const anySelectedNode = computed<NodeRef | null>(() =>
  parseNodeRef(route.query.node)
);
const selectedNode = computed<NodeRef | null>(() =>
  anySelectedNode.value?.kind === "participant" ? anySelectedNode.value : null
);
const selectedVersionNode = computed<NodeRef | null>(() =>
  anySelectedNode.value?.kind === "protocol_version"
    ? anySelectedNode.value
    : null
);
const selectedVersionText = computed(() =>
  selectedVersionNode.value ? formatNodeRef(selectedVersionNode.value) : ""
);
// The protocol version in the inspector, read from the kernel whenever the URL names one.
const inspectedVersion = ref<KernelProtocolVersion | null>(null);
const inspectedVersionError = ref("");
let versionRequest = 0;
watch(
  selectedVersionText,
  async (text) => {
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
        inspectedVersionError.value =
          e instanceof Error ? e.message : String(e);
      }
    }
  },
  { immediate: true }
);
const selectedParticipant = computed<SubjectSummary | null>(() =>
  selectedNode.value
    ? (canvas.knownParticipants[selectedNode.value.id] ?? null)
    : null
);

const participantRef = (value: unknown): NodeRef | null => {
  const node = parseNodeRef(value);
  return node?.kind === "participant" ? node : null;
};
const recordRef = computed(() => participantRef(route.query.participant));
const formQuery = computed(() => single(route.query.form).trim());
const isNewForm = computed(() => formQuery.value === "new");
const openFormId = computed(() => collectionInstanceId(formQuery.value));
const highlightItemQuery = computed(() => single(route.query.item).trim());
const initialSpecId = computed(() => single(route.query.spec).trim());
const initialVisitId = computed(() => single(route.query.visit).trim());
const hasOpenForm = computed(
  () => isNewForm.value || Boolean(openFormId.value)
);
const openObservationRef = computed(
  () => observationNodeRef(route.query.observation) ?? ""
);
const proposalRef = computed(() => {
  const node = parseNodeRef(route.query.proposal);
  return node?.kind === "proposal" ? formatNodeRef(node) : "";
});
const askText = computed(() => single(route.query.ask).trim());
const recordParticipant = computed<SubjectSummary | null>(() =>
  recordRef.value
    ? (canvas.knownParticipants[recordRef.value.id] ?? null)
    : null
);

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

const isRecordParticipantLoading = computed(() =>
  isParticipantPending(recordRef.value?.id)
);
const isSelectedParticipantLoading = computed(() =>
  isParticipantPending(selectedNode.value?.id)
);

const selectedGenericNodeRef = computed(() => {
  const node = anySelectedNode.value;
  if (!node || node.kind === "protocol_version") return "";
  if (node.kind === "participant") {
    if (
      selectedParticipant.value ||
      isSelectedParticipantLoading.value ||
      !canResolveNodesRemotely()
    ) {
      return "";
    }
  }
  return formatNodeRef(node);
});
const inspectedGenericNode = ref<KernelNode | null>(null);
const inspectedGenericLoading = ref(false);
let genericNodeRequest = 0;
watch(
  () => (anySelectedNode.value ? formatNodeRef(anySelectedNode.value) : ""),
  (next, prev) => {
    if (next && next !== prev) {
      inspectorRef.value?.expand();
    }
  }
);
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

const loadedFormParticipant = computed<SubjectSummary | null>(() => {
  const pid = loadedForm.value?.participant_id;
  return pid ? (canvas.knownParticipants[pid] ?? null) : null;
});
const lineageParticipant = computed<SubjectSummary | null>(() =>
  resolvedParticipantId.value
    ? (canvas.knownParticipants[resolvedParticipantId.value] ?? null)
    : null
);
const activeFormParticipant = computed<SubjectSummary | null>(
  () =>
    recordParticipant.value ??
    loadedFormParticipant.value ??
    lineageParticipant.value
);
const activeParticipantNodeText = computed(() => {
  if (recordRef.value) return formatNodeRef(recordRef.value);
  const participant = activeFormParticipant.value;
  const node = participant ? tryNodeRef("participant", participant.id) : null;
  return node ? formatNodeRef(node) : "";
});
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

const inspectedParticipant = computed(() => {
  if (anySelectedNode.value) return selectedParticipant.value;
  return (
    recordParticipant.value ??
    loadedFormParticipant.value ??
    lineageParticipant.value
  );
});
const inspectedNodeText = computed(() => {
  if (anySelectedNode.value) return formatNodeRef(anySelectedNode.value);
  const participant = inspectedParticipant.value;
  const node = participant ? tryNodeRef("participant", participant.id) : null;
  return node ? formatNodeRef(node) : "";
});
const canManage = computed(() => canManageQueries(authStore));
const canAnswer = computed(() => canAnswerQueries(authStore));
const canManageRules = computed(() => canManageRulePacks(authStore));
const canVerify = computed(() => canVerifySource(authStore));
const canChangeParticipants = computed(() => canManageParticipants(authStore));

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
  if (zoomParticipantCode.value) return "participant";
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
  if (recordRef.value) {
    return `${recordParticipant.value?.subject_id ?? "Participant"} data`;
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

function ecrfHandoffLocationFor(participant: SubjectSummary) {
  const query: LocationQueryRaw = {
    studyId: studyCode.value,
    subjectId: participant.subject_id,
  };
  if (participant.site_id) {
    query.siteId = participant.site_id;
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
  const activeParticipantMatch =
    activeFormParticipant.value?.id === participant.id;
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
  return { name: "ecrf", query };
}

function dataEntryLocationFor(participant: SubjectSummary) {
  return newFormLocationFor(participant);
}

function formLocationById(formId: string) {
  const formRef = collectionInstanceRef(formId);
  return studyLocation({
    stage: "conduct",
    site: !activeParticipantNodeText.value
      ? zoomSite.value || undefined
      : undefined,
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
  });
}

function observationLocationFromRecord(observationId: string, formId?: string) {
  const obsRef = observationNodeRef(observationId);
  const formRef = formId ? collectionInstanceRef(formId) : null;
  return studyLocation({
    stage: "conduct",
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
    observation: obsRef ?? undefined,
  });
}

function observationLocationFromForm(observationId: string, formId: string) {
  const obsRef = observationNodeRef(observationId);
  const formRef = collectionInstanceRef(formId || openFormId.value || "");
  return studyLocation({
    stage: "conduct",
    site: !activeParticipantNodeText.value
      ? zoomSite.value || undefined
      : undefined,
    participant: activeParticipantNodeText.value || undefined,
    form: formRef ?? undefined,
    observation: obsRef ?? undefined,
  });
}

const zoomParticipantLocation = computed(() => {
  if (zoomParticipant.value) return recordLocationFor(zoomParticipant.value);
  return studyLocation({
    stage: "conduct",
    section: "participants",
    site: zoomSite.value || undefined,
  });
});

const zoomFormLocation = computed(() => {
  if (openFormId.value) return formLocationById(openFormId.value);
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
  if (activeFormParticipant.value) {
    return `Back to ${activeFormParticipant.value.subject_id}`;
  }
  if (zoomSite.value) return `Back to Site ${zoomSite.value} participants`;
  return `Back to ${studyCode.value} participants`;
});

const observationReturnLocation = computed(() => {
  if (openFormId.value) return formLocationById(openFormId.value);
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
  },
});

function onFormClosed() {
  formWorkspaceDirty.value = false;
  loadedForm.value = null;
  router.push(formBackLocation.value);
}

async function onObservationLoaded(obs: KernelObservation) {
  loadedObservation.value = obs;
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
  studyCode,
  (code) => {
    formWorkspaceDirty.value = false;
    loadedForm.value = null;
    loadedObservation.value = null;
    resolvedLineage.value = null;
    resolvedParticipantId.value = "";
    lineageRequest++;
    canvas.openStudy(code);
    if (!code) {
      loadStudyDirectory();
    }
  },
  { immediate: true }
);

watch(
  [
    studyCode,
    () => recordRef.value?.id,
    () => selectedNode.value?.id,
    () => loadedForm.value?.participant_id,
  ],
  ([code, recordId, selectedId, formParticipantId]) => {
    if (!code) return;
    const ids = new Set(
      [recordId, selectedId, formParticipantId].filter((id): id is string =>
        Boolean(id)
      )
    );
    for (const id of ids) {
      if (!canvas.knownParticipants[id]) {
        void canvas.ensureParticipantById(id);
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

// Load what the current view shows, once per study and filter.
watch(
  [
    studyCode,
    () => stage.value?.id,
    () => section.value?.id,
    statusFilter,
    siteFilter,
    participantSearchFilter,
  ],
  () => {
    if (
      !studyCode.value ||
      recordRef.value ||
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
  if (nextFormQuery === "new") return true;
  const nextFormId = collectionInstanceId(nextFormQuery);
  if (!nextFormId) return true;

  const currentFormId = loadedForm.value?.form_id || openFormId.value;
  return Boolean(currentFormId && nextFormId !== currentFormId);
}

function shouldSuppressUnsavedGuard(): boolean {
  return Boolean(authStore.sessionExpired || authStore.isLoggingOut);
}

function handleBeforeUnload(event: BeforeUnloadEvent) {
  if (formWorkspaceDirty.value && !shouldSuppressUnsavedGuard()) {
    event.preventDefault();
    event.returnValue = "";
    return "";
  }
}

function confirmDiscardUnsavedForm(): boolean {
  // eslint-disable-next-line no-alert -- Vue Router navigation guards require synchronous window.confirm.
  return window.confirm(UNSAVED_FORM_CONFIRM_MESSAGE);
}

if (typeof onBeforeRouteLeave === "function") {
  onBeforeRouteLeave(() => {
    if (
      formWorkspaceDirty.value &&
      !shouldSuppressUnsavedGuard() &&
      !confirmDiscardUnsavedForm()
    ) {
      return false;
    }
  });
}

if (typeof onBeforeRouteUpdate === "function") {
  onBeforeRouteUpdate((to) => {
    if (
      formWorkspaceDirty.value &&
      !shouldSuppressUnsavedGuard() &&
      leavesActiveForm(to) &&
      !confirmDiscardUnsavedForm()
    ) {
      return false;
    }
  });
}

function onGlobalKeydown(event: KeyboardEvent) {
  if (inAppShell) return;
  if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === "k") {
    event.preventDefault();
    paletteOpen.value = !paletteOpen.value;
  }
}

watch(topbarRef, (el) => {
  if (topbarResizeObserver) {
    topbarResizeObserver.disconnect();
    topbarResizeObserver = null;
  }
  syncTopbarHeight();
  if (typeof ResizeObserver !== "undefined" && el instanceof HTMLElement) {
    topbarResizeObserver = new ResizeObserver(() => syncTopbarHeight());
    topbarResizeObserver.observe(el);
  }
});

onMounted(() => {
  window.addEventListener("beforeunload", handleBeforeUnload);
  window.addEventListener("resize", syncTopbarHeight);
  if (!inAppShell) {
    document.addEventListener("keydown", onGlobalKeydown);
  }
  syncTopbarHeight();
  if (
    typeof ResizeObserver !== "undefined" &&
    topbarRef.value instanceof HTMLElement &&
    !topbarResizeObserver
  ) {
    topbarResizeObserver = new ResizeObserver(() => syncTopbarHeight());
    topbarResizeObserver.observe(topbarRef.value);
  }
});
onUnmounted(() => {
  window.removeEventListener("beforeunload", handleBeforeUnload);
  window.removeEventListener("resize", syncTopbarHeight);
  document.removeEventListener("keydown", onGlobalKeydown);
  if (topbarResizeObserver) {
    topbarResizeObserver.disconnect();
    topbarResizeObserver = null;
  }
  applyRootTopbarHeight(0);
});
</script>

<style scoped>
.canvas {
  --canvas-sticky-offset: 0px;
  --canvas-pane-top: calc(
    var(--canvas-sticky-offset) + var(--canvas-topbar-height, 0px)
  );
  display: flex;
  flex-direction: column;
  min-width: 0;
  min-height: 100vh;
  background: var(--semantic-color-surface-muted);
  color: var(--primary);
  font-family: var(--font);
}

.canvas.canvas-in-shell {
  --canvas-sticky-offset: var(--shell-header-height, 0px);
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
  flex-wrap: wrap;
  align-items: center;
  gap: 8px 16px;
  padding: 8px 16px;
  min-width: 0;
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
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.9rem;
}

.zoom-path li + li::before {
  content: "/";
  margin-right: 6px;
  color: var(--neutral-dark);
}

.zoom-path a {
  color: inherit;
}

.zoom-path [aria-current="page"] {
  font-weight: 600;
}

.palette-trigger {
  display: inline-flex;
  align-items: center;
  gap: 12px;
  padding: 6px 10px;
  font: inherit;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.ai-toggle {
  padding: 6px 10px;
  font: inherit;
  border: 1px solid var(--border);
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.ai-toggle[aria-pressed="true"] {
  border-color: var(--semantic-color-ai-border);
  background: var(--semantic-color-ai-bg);
  color: var(--semantic-color-ai);
}

.sr-only {
  position: absolute;
  top: 0;
  left: 0;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0 0 0 0);
  white-space: nowrap;
  border: 0;
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
  min-width: 0;
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 13.75rem), 1fr));
  gap: 12px;
}

.create-field {
  position: relative;
  display: flex;
  flex-direction: column;
  min-width: 0;
  gap: 4px;
}

.create-field
  input.sr-only:focus-visible
  ~ .create-file-row
  #create-study-choose-file {
  outline: 2px solid var(--semantic-color-interactive-primary);
  outline-offset: 2px;
}

.create-field-wide {
  grid-column: 1 / -1;
}

.create-field label {
  font-size: 0.85rem;
  font-weight: 600;
}

.create-field input,
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

@media (max-width: 767px), (max-height: 479px) {
  .topbar {
    position: static;
    padding: 8px 12px;
  }

  .workspace {
    padding: 12px;
  }

  .open-study {
    padding: 16px 12px;
  }
}
</style>
