<template>
  <div>
    <ClinicalDrawer
      :is-open="onboardingStore.isDrawerOpen"
      :teleport="false"
      id="onboarding-checklist-drawer"
      overlay-id="onboarding-checklist-drawer-overlay"
      overlay-class="drawer-overlay"
      panel-class="drawer-panel"
      body-class="drawer-body"
      aria-labelledby="checklist-drawer-title"
      @close="onboardingStore.closeDrawer()"
    >
      <template #header>
        <!-- Header -->
        <div class="drawer-header">
          <div class="header-main-row">
            <div class="header-title-group">
              <span class="header-icon">🚀</span>
              <div>
                <h3 id="checklist-drawer-title" class="drawer-title">
                  Workspace Setup Checklist
                </h3>
                <p class="drawer-subtitle">
                  Complete core study configuration milestones
                </p>
              </div>
            </div>
            <button
              type="button"
              class="btn-close"
              aria-label="Close Setup Checklist Drawer"
              @click="onboardingStore.closeDrawer()"
            >
              ✕
            </button>
          </div>

          <!-- Overall Progress Bar Card -->
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
            <div
              v-if="onboardingStore.isChecklistComplete"
              class="complete-banner"
            >
              🎉 All setup milestones completed! Study configuration ready.
            </div>
          </div>
        </div>
      </template>

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
              Go to Task ↗
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
import { computed, watch } from "vue";
import { useRoute, useRouter } from "vue-router";
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
import { resolveStorePinia } from "../stores/onboardingSummary.js";
import { PERSONA_PRESETS, useAuthStore } from "../stores/auth";
import { isScreenAvailable } from "../router/availability";
import { hasRequiredRole } from "../router";

const props = defineProps({
  openOnMount: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["mounted-open"]);

const onboardingStore = useOnboardingStore();
const authStore = useAuthStore();
const activeCanvasStore = computed(
  () => resolveStorePinia(authStore)?.state?.value?.studyCanvas || null
);

watch(
  () => props.openOnMount,
  (shouldOpen) => {
    if (shouldOpen) {
      if (!onboardingStore.isDrawerOpen) {
        onboardingStore.openDrawer();
      }
      emit("mounted-open");
    }
  },
  { immediate: true }
);
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

async function startGuidedTour() {
  onboardingStore.startTour();
  onboardingStore.closeDrawer();
  const initialRouteName = onboardingStore.currentStepConfig?.routeName;
  if (
    initialRouteName &&
    router &&
    activeRoute?.name !== initialRouteName &&
    (activeRoute?.name === "forbidden" ||
      activeRoute?.path === "/forbidden" ||
      activeRoute?.name === "unavailable" ||
      activeRoute?.path === "/unavailable")
  ) {
    const resolved =
      typeof router.resolve === "function"
        ? router.resolve({ name: initialRouteName })
        : null;
    const basePath = resolved?.path || resolved?.fullPath;
    const targetPath = basePath
      ? buildScopedWorkflowPath(basePath, {
          studyCode: workspaceScope.value.studyCode,
          siteId: workspaceScope.value.siteId,
          subjectCode: workspaceScope.value.subjectCode,
        })
      : null;
    if (targetPath && canPushResolvedPath(targetPath)) {
      await router.push(targetPath);
    }
  }
}
</script>

<style scoped>
:deep(.drawer-overlay) {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.4);
  backdrop-filter: blur(2px);
  z-index: 1050;
  display: flex;
  justify-content: flex-end;
}

:deep(.drawer-panel) {
  width: 420px;
  max-width: 100vw;
  height: 100%;
  background-color: var(--semantic-color-surface);
  box-shadow: -10px 0 30px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  z-index: 1051;
  font-family: var(--semantic-font-family);
  color: var(--semantic-color-text);
  animation: slideInRight 0.25s cubic-bezier(0.16, 1, 0.3, 1);
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  padding: 20px;
  background-color: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.header-main-row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 16px;
}

.header-title-group {
  display: flex;
  align-items: center;
  gap: 12px;
}

.header-icon {
  font-size: 1.6rem;
  line-height: 1;
}

.drawer-title {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.drawer-subtitle {
  margin: 2px 0 0 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.btn-close {
  background: none;
  border: none;
  font-size: 1.25rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
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
  text-align: center;
}

:deep(.drawer-body) {
  flex: 1;
  overflow-y: auto;
  padding: 16px 20px;
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
  font-size: 0.75rem;
  padding: 4px 10px;
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
  padding: 16px 20px;
  background-color: var(--semantic-color-surface-muted);
  border-top: 1px solid var(--semantic-color-border);
}

.footer-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
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
