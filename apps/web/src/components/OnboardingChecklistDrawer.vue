<template>
  <div>
    <ClinicalDrawer
      :is-open="onboardingStore.isDrawerOpen"
      :teleport="false"
      size="sm"
      id="onboarding-checklist-drawer"
      overlay-id="onboarding-checklist-drawer-overlay"
      overlay-class="drawer-overlay"
      panel-class="drawer-panel"
      body-class="drawer-body"
      aria-labelledby="checklist-drawer-title"
      @close="onboardingStore.closeDrawer()"
    >
      <template #header>
        <!-- Compact Single-Row Header -->
        <header class="drawer-header">
          <div class="header-title-group">
            <span class="header-icon" aria-hidden="true">
              <Rocket :size="18" aria-hidden="true" />
            </span>
            <h3 id="checklist-drawer-title" class="drawer-title">
              Workspace Setup Checklist
            </h3>
          </div>
          <button
            type="button"
            class="btn-close"
            aria-label="Close Setup Checklist Drawer"
            @click="onboardingStore.closeDrawer()"
          >
            <X :size="18" aria-hidden="true" />
          </button>
        </header>
      </template>

      <p class="drawer-subtitle">
        Complete core study configuration milestones
      </p>

      <!-- Overall Progress Bar Card inside Scroll Body -->
      <div class="progress-section">
        <div class="progress-info-row">
          <span class="progress-percentage">
            {{ onboardingStore.progressPercentage }}% Complete
          </span>
          <span class="progress-counts">
            {{ onboardingStore.completedTasksCount }} of
            {{ onboardingStore.totalTasksCount }} Tasks Completed
          </span>
        </div>
        <div
          class="progress-bar-track"
          role="progressbar"
          :aria-valuenow="onboardingStore.progressPercentage"
          aria-valuemin="0"
          aria-valuemax="100"
          :aria-label="`Setup Progress: ${onboardingStore.progressPercentage}%`"
        >
          <div
            class="progress-bar-fill"
            :style="{ width: onboardingStore.progressPercentage + '%' }"
          />
        </div>
        <div v-if="onboardingStore.isChecklistComplete" class="complete-banner">
          <CheckCircle2 :size="15" aria-hidden="true" />
          All setup milestones completed! Study configuration ready.
        </div>
      </div>

      <!-- Body Content / Task List -->
      <div class="task-list-container">
        <div
          v-for="task in onboardingStore.visibleTasks"
          :key="task.id"
          class="task-card"
          :class="{ completed: task.completed }"
        >
          <div class="task-header-row">
            <div class="task-check-wrapper">
              <input
                :id="'task-check-' + task.id"
                type="checkbox"
                class="task-checkbox"
                :checked="task.completed"
                :aria-label="'Mark ' + task.title + ' as completed'"
                @change="
                  onboardingStore.toggleTask(task.id, $event.target.checked)
                "
              />
              <label :for="'task-check-' + task.id" class="task-title">
                {{ task.title }}
              </label>
            </div>
            <span
              class="task-status-badge"
              :class="task.completed ? 'status-complete' : 'status-pending'"
            >
              {{ task.completed ? "✓ Done" : "Pending" }}
            </span>
          </div>

          <p class="task-desc">
            {{ task.description }}
          </p>

          <div class="task-footer">
            <button
              type="button"
              class="btn btn-sm btn-navigate-task"
              :aria-label="'Go to Task — ' + task.title"
              @click="navigateToTask(task)"
            >
              <ArrowUpRight :size="14" aria-hidden="true" />
              Go to Task
            </button>
          </div>
        </div>
      </div>

      <!-- Drawer Footer Controls -->
      <template #footer>
        <div class="drawer-footer">
          <div class="footer-actions">
            <button
              id="btn-open-orientation-hub"
              type="button"
              class="btn btn-sm btn-secondary"
              @click="openOrientationHub"
            >
              Orientation Hub
            </button>
            <button
              v-if="!onboardingStore.isActive"
              id="btn-drawer-start-tour"
              type="button"
              class="btn btn-sm btn-secondary"
              @click="startGuidedTour"
            >
              Start Tour
            </button>
            <button
              type="button"
              class="btn btn-sm btn-secondary btn-reset-checklist"
              @click="onboardingStore.resetChecklist()"
            >
              Reset Progress
            </button>
            <button
              type="button"
              class="btn btn-sm btn-primary"
              @click="onboardingStore.closeDrawer()"
            >
              Close
            </button>
          </div>
        </div>
      </template>
    </ClinicalDrawer>
  </div>
</template>

<script setup>
import { computed } from "vue";
import { getActivePinia } from "pinia";
import { useRoute, useRouter } from "vue-router";
import { ArrowUpRight, CheckCircle2, Rocket, X } from "lucide-vue-next";
import { ClinicalDrawer } from "ui";
import {
  buildScopedWorkflowPath,
  resolveWorkspaceNavigationScope,
} from "../canvas/palette";
import { useActiveStudyScope } from "../composables/useActiveStudyScope";
import {
  resolveAuthorizedTaskRoute,
  useOnboardingStore,
} from "../stores/onboarding";
import { PERSONA_PRESETS, useAuthStore } from "../stores/auth";
import { useStudyCanvasStore } from "../stores/studyCanvas";
import { isScreenAvailable } from "../router/availability";
import { hasRequiredRole } from "../router";

const onboardingStore = useOnboardingStore();
const authStore = useAuthStore();
const activeCanvasStore = computed(() => {
  const pinia = getActivePinia();
  return pinia ? useStudyCanvasStore(pinia) : null;
});
const router = useRouter();
let activeRoute = null;
try {
  activeRoute = useRoute();
} catch {
  activeRoute = null;
}
const { activeStudyCode } = useActiveStudyScope();

const workspaceScope = computed(() =>
  resolveWorkspaceNavigationScope(
    activeRoute,
    activeCanvasStore.value,
    undefined,
    activeStudyCode.value
  )
);

const effectiveRoles = computed(() => {
  if (authStore.rawRoles?.length > 0) {
    return authStore.normalizedRoles || [];
  }
  const personaKey =
    authStore.currentPersona || onboardingStore.currentPersona || "super_admin";
  const preset = PERSONA_PRESETS.find((p) => p.key === personaKey);
  return preset ? [...preset.requires] : ["sponsor_admin"];
});

function canPushResolvedPath(targetPath) {
  if (!targetPath || !router) return false;
  if (typeof router.resolve === "function") {
    const resolved = router.resolve(targetPath);
    if (!resolved?.name || !isScreenAvailable(String(resolved.name))) {
      return false;
    }
    const matchedRoles = (resolved.matched || []).flatMap(
      (record) => record.meta?.requiresRole || []
    );
    const requiredRoles =
      matchedRoles.length > 0
        ? matchedRoles
        : resolved.meta?.requiresRole || [];
    if (
      requiredRoles.length > 0 &&
      !hasRequiredRole(effectiveRoles.value, requiredRoles)
    ) {
      return false;
    }
  }
  return true;
}

function navigateToTask(task) {
  if (!task) return;
  const authorizedRoute = resolveAuthorizedTaskRoute(
    task,
    effectiveRoles.value,
    workspaceScope.value
  );
  if (authorizedRoute && canPushResolvedPath(authorizedRoute)) {
    router.push(authorizedRoute);
    onboardingStore.closeDrawer();
  }
}

function openOrientationHub() {
  const targetPath = buildScopedWorkflowPath("/overview", {
    studyCode: workspaceScope.value.studyCode,
    siteId: workspaceScope.value.siteId,
    subjectCode: workspaceScope.value.subjectCode,
  });
  if (isScreenAvailable("overview") && canPushResolvedPath(targetPath)) {
    router.push(targetPath);
    onboardingStore.closeDrawer();
  }
}

function startGuidedTour() {
  onboardingStore.startTour();
  onboardingStore.closeDrawer();
}
</script>

<style scoped>
:deep(.drawer-overlay) {
  z-index: 1050;
}

:deep(.drawer-panel) {
  font-family: var(--semantic-font-family);
  color: var(--semantic-color-text);
}

.drawer-header {
  padding: 8px 16px;
  min-height: 48px;
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  box-sizing: border-box;
}

.header-title-group {
  display: flex;
  align-items: center;
  gap: 10px;
  min-width: 0;
  flex: 1;
}

.header-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  color: var(--semantic-color-interactive-primary);
}

.drawer-title {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.drawer-subtitle {
  margin: 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.btn-close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  min-width: 44px;
  min-height: 44px;
  background: none;
  border: none;
  font-size: 1.1rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 6px;
  border-radius: 6px;
  transition:
    background-color 0.15s,
    color 0.15s;
}

.btn-close:hover {
  background-color: var(--semantic-color-border);
  color: var(--semantic-color-text);
}

.progress-section {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 12px;
}

.progress-info-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-size: 0.8rem;
  margin-bottom: 8px;
}

.progress-percentage {
  font-weight: 700;
  color: var(--semantic-color-interactive-primary);
}

.progress-counts {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}

.progress-bar-track {
  height: 8px;
  background-color: var(--semantic-color-border);
  border-radius: 4px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background-color: var(--semantic-color-interactive-primary);
  border-radius: 4px;
  transition: width 0.3s ease;
}

.complete-banner {
  margin-top: 10px;
  padding: 8px 10px;
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
  font-size: 0.78rem;
  font-weight: 600;
  border-radius: 6px;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  text-align: center;
}

:deep(.drawer-body) {
  padding: 14px 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.task-list-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.task-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 14px;
  transition: all 0.2s ease;
}

.task-card:hover {
  border-color: var(--semantic-color-border-muted);
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.04);
}

.task-card.completed {
  background-color: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border);
}

.task-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  margin-bottom: 6px;
}

.task-check-wrapper {
  display: flex;
  align-items: center;
  gap: 8px;
}

.task-checkbox {
  width: var(--semantic-control-target-min, 24px);
  height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  accent-color: var(--semantic-color-interactive-primary);
  cursor: pointer;
  flex-shrink: 0;
}

.task-title {
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--semantic-color-text);
  cursor: pointer;
}

.task-card.completed .task-title {
  text-decoration: line-through;
  color: var(--semantic-color-text-secondary);
}

.task-status-badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 12px;
  white-space: nowrap;
}

.status-complete {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.status-pending {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.task-desc {
  margin: 0 0 10px 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.35;
  padding-left: 24px;
}

.task-footer {
  display: flex;
  justify-content: flex-end;
  padding-left: 24px;
}

.btn-navigate-task {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-height: 44px;
  font-size: 0.78rem;
  padding: 6px 12px;
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  cursor: pointer;
  font-weight: 500;
  transition: all 0.15s ease;
}

.btn-navigate-task:hover {
  background-color: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
}

:deep(.clinical-drawer-footer) {
  padding: 0;
  border-top: none;
}

.drawer-footer {
  width: 100%;
  padding: 10px 16px;
  background-color: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
  box-sizing: border-box;
}

.footer-actions {
  display: flex;
  justify-content: flex-end;
  flex-wrap: wrap;
  gap: 8px;
}

.footer-actions .btn {
  min-height: 44px;
}

@media (prefers-reduced-motion: reduce) {
  :deep(.drawer-panel) {
    animation: none;
  }

  .btn-close,
  .progress-bar-fill,
  .task-card,
  .btn-navigate-task {
    transition: none;
  }
}
</style>
