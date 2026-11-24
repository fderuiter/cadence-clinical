<template>
  <section
    id="ctms-milestones-section"
    class="card ctms-section"
    aria-labelledby="ctms-milestones-title"
  >
    <div class="section-header-row">
      <div>
        <h3 id="ctms-milestones-title" class="card-title" tabindex="-1">
          Site milestones
        </h3>
        <p class="section-subtitle">
          Track planned and achieved site startup and conduct milestones.
        </p>
      </div>
    </div>

    <CtmsSectionScopeBar
      v-model:study="studyInput"
      v-model:site="siteInput"
      prefix="milestones"
      form-id="milestones-scope-form"
      aria-label="Site milestones scope"
      :show-study-input="!props.studyCode"
      site-placeholder="Site code (optional)"
      :assigned-studies="assignedStudies"
      :assigned-sites="assignedSites"
      submit-id="btn-load-milestones"
      :submit-label="loading ? 'Loading...' : 'Load milestones'"
      :submit-disabled="!studyInput.trim() || loading"
      :scope-guidance="scopeGuidance"
      :load-error="loadError"
      :idle-message="
        !loaded && !loading
          ? 'Select a study to load site milestones within your assigned scope.'
          : ''
      "
      @submit="loadMilestones"
    />

    <template v-if="loaded">
      <p v-if="!milestones.length" class="muted-status">
        No site milestones recorded yet for this scope.
      </p>
      <div v-else class="table-wrap">
        <table id="milestones-table" class="data-table">
          <thead>
            <tr>
              <th scope="col">Site</th>
              <th scope="col">Milestone</th>
              <th scope="col">Status</th>
              <th scope="col">Planned date</th>
              <th scope="col">Actual date</th>
              <th v-if="canManage" scope="col">Action</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="item in milestones" :key="item.id">
              <td>
                <strong>{{ item.site_id }}</strong>
              </td>
              <td>{{ formatClinicalEnum(item.milestone_type) }}</td>
              <td>
                <span
                  class="status-pill"
                  :class="`ms-${item.status.toLowerCase()}`"
                >
                  {{ formatClinicalStatus(item.status) }}
                </span>
              </td>
              <td>{{ formatIsoDateShort(item.planned_date) || "—" }}</td>
              <td>{{ formatIsoDateShort(item.actual_date) || "—" }}</td>
              <td v-if="canManage">
                <button
                  v-if="item.status !== 'ACHIEVED'"
                  :id="`btn-achieve-milestone-${item.id}`"
                  type="button"
                  class="btn btn-secondary btn-sm"
                  :disabled="saving"
                  @click="prepareAchieve(item)"
                >
                  Mark achieved<span class="sr-only">
                    {{ item.milestone_type }} for {{ item.site_id }}</span
                  >
                </button>
                <span v-else class="text-ok">Completed</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div
        v-if="canManage && activeAchieveMilestone"
        id="achieve-milestone-form"
        class="mutation-card"
      >
        <h4>
          Mark {{ formatClinicalEnum(activeAchieveMilestone.milestone_type) }}
          achieved for
          {{ activeAchieveMilestone.site_id }}
        </h4>
        <div class="form-row">
          <label for="achieve-milestone-date">Actual date</label>
          <input
            id="achieve-milestone-date"
            v-model="achieveDate"
            type="date"
          />
          <label for="achieve-milestone-reason">Reason for change</label>
          <input
            id="achieve-milestone-reason"
            v-model="achieveReason"
            type="text"
            placeholder="Clinical evidence for achieving milestone"
          />
          <button
            id="btn-confirm-achieve-milestone"
            type="button"
            class="btn btn-primary"
            :disabled="!achieveDate || !achieveReason.trim() || saving"
            @click="confirmAchieve"
          >
            {{ saving ? "Saving..." : "Confirm achieved" }}
          </button>
          <button
            type="button"
            class="btn btn-secondary"
            @click="cancelAchieve"
          >
            Cancel
          </button>
        </div>
      </div>

      <form
        v-if="canManage"
        id="create-milestone-form"
        class="mutation-card"
        @submit.prevent="createMilestone"
      >
        <h4>Add site milestone</h4>
        <div class="form-row">
          <label for="new-ms-site">Site</label>
          <input
            id="new-ms-site"
            v-model="newSiteId"
            type="text"
            placeholder="Site code"
          />
          <label for="new-ms-type">Milestone</label>
          <select id="new-ms-type" v-model="newType">
            <option value="SITE_SELECTION">Site Selection</option>
            <option value="INITIATION_VISIT">Initiation Visit</option>
            <option value="SITE_ACTIVATION">Site Activation</option>
            <option value="FIRST_SUBJECT_ENROLLED">
              First Participant Enrolled
            </option>
            <option value="DB_LOCK">Database Lock</option>
          </select>
          <label for="new-ms-planned">Planned date</label>
          <input id="new-ms-planned" v-model="newPlannedDate" type="date" />
          <label for="new-ms-reason">Reason for change</label>
          <input
            id="new-ms-reason"
            v-model="newReason"
            type="text"
            placeholder="Justification for milestone creation"
          />
          <button
            id="btn-create-milestone"
            type="submit"
            class="btn btn-primary"
            :disabled="
              !studyInput.trim() ||
              !newSiteId.trim() ||
              !newPlannedDate ||
              !newReason.trim() ||
              saving
            "
          >
            {{ saving ? "Saving..." : "Add milestone" }}
          </button>
        </div>
      </form>
    </template>
  </section>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { useInlineFocusHandoff } from "ui";
import { ctmsApi, type SiteMilestone } from "../../api/ctmsApi";
import { canManageMilestones } from "../../canvas/authority";
import {
  formatIsoDateShort,
  useScopedWorkspaceSection,
} from "../../composables/useScopedWorkspaceSection";
import {
  formatClinicalEnum,
  formatClinicalStatus,
} from "../../utils/clinicalLabels";
import CtmsSectionScopeBar from "./CtmsSectionScopeBar.vue";
import "./ctmsWorkspaceSection.css";

const props = withDefaults(
  defineProps<{
    studyCode?: string;
    siteId?: string;
  }>(),
  {
    studyCode: "",
    siteId: "",
  }
);

const newSiteId = ref("");
const {
  authStore,
  toasts,
  assignedStudies,
  assignedSites,
  studyInput,
  siteInput,
  loading,
  loaded,
  saving,
  loadError,
  scopeGuidance,
  syncActiveScope,
  recordLoadFailure,
} = useScopedWorkspaceSection(props, {
  onStudyPropChange: () => {
    void loadMilestones();
  },
  onSitePropChange: (val) => {
    if (val) newSiteId.value = val;
  },
  fallbackErrorTitle: "Site milestones could not be loaded",
  fallbackErrorMessage: "Site milestones could not be loaded.",
});

newSiteId.value = siteInput.value;
const canManage = computed(() => canManageMilestones(authStore));
const milestones = ref<SiteMilestone[]>([]);
const newType = ref("SITE_ACTIVATION");
const newPlannedDate = ref("");
const newReason = ref("");

const activeAchieveMilestone = ref<SiteMilestone | null>(null);
const achieveDate = ref("");
const achieveReason = ref("");
const achieveHandoff = useInlineFocusHandoff({
  openTarget: "#achieve-milestone-date",
  closeFallback: "#ctms-milestones-title",
  scrollIfOffscreen: true,
});

async function loadMilestones() {
  const study = studyInput.value.trim();
  const site = siteInput.value.trim();
  if (!study) return;

  syncActiveScope(study, site);
  if (site) newSiteId.value = site;

  loading.value = true;
  loadError.value = "";
  scopeGuidance.value = "";

  try {
    milestones.value = await ctmsApi.listSiteMilestones({
      studyId: study,
      siteId: site || undefined,
    });
    loaded.value = true;
  } catch (err) {
    milestones.value = [];
    recordLoadFailure(err, study, site);
  } finally {
    loading.value = false;
  }
}

async function createMilestone() {
  const study = studyInput.value.trim();
  const site = newSiteId.value.trim();
  const reason = newReason.value.trim();
  if (!study || !site || !reason || !canManage.value) return;

  saving.value = true;
  try {
    await ctmsApi.createSiteMilestone(
      {
        study_id: study,
        site_id: site,
        milestone_type: newType.value,
        planned_date: newPlannedDate.value
          ? `${newPlannedDate.value}T00:00:00Z`
          : null,
        status: "PLANNED",
        reason_for_change: reason,
      },
      reason
    );
    newReason.value = "";
    toasts.push({
      tone: "success",
      title: "Milestone added",
      message: `${formatClinicalEnum(newType.value)} added for ${site}.`,
    });
    await loadMilestones();
  } catch (err) {
    toasts.apiError("Milestone could not be created", err);
  } finally {
    saving.value = false;
  }
}

function prepareAchieve(item: SiteMilestone) {
  achieveHandoff.rememberTrigger(`[id="btn-achieve-milestone-${item.id}"]`);
  activeAchieveMilestone.value = item;
  achieveReason.value = "";
  void achieveHandoff.handoff("#achieve-milestone-date", {
    scrollIfOffscreen: true,
  });
}

function cancelAchieve() {
  const itemId = activeAchieveMilestone.value?.id;
  activeAchieveMilestone.value = null;
  achieveReason.value = "";
  void achieveHandoff.restoreTrigger([
    itemId ? `[id="btn-achieve-milestone-${itemId}"]` : null,
    "#ctms-milestones-title",
  ]);
}

async function confirmAchieve() {
  const item = activeAchieveMilestone.value;
  const reason = achieveReason.value.trim();
  if (!item || !reason || !canManage.value) return;

  saving.value = true;
  try {
    await ctmsApi.updateSiteMilestone(
      item.id,
      {
        status: "ACHIEVED",
        actual_date: achieveDate.value
          ? `${achieveDate.value}T00:00:00Z`
          : null,
        reason_for_change: reason,
      },
      reason
    );
    activeAchieveMilestone.value = null;
    achieveReason.value = "";
    toasts.push({
      tone: "success",
      title: "Milestone achieved",
      message: `${formatClinicalEnum(item.milestone_type)} marked achieved for ${item.site_id}.`,
    });
    await loadMilestones();
    saving.value = false;
    void achieveHandoff.handoff([
      () =>
        document.querySelector<HTMLElement>(
          "#milestones-table button:not([disabled])"
        ),
      "#ctms-milestones-title",
    ]);
  } catch (err) {
    toasts.apiError("Milestone could not be updated", err);
  } finally {
    saving.value = false;
  }
}

onMounted(() => {
  if (studyInput.value.trim()) {
    void loadMilestones();
  }
});
</script>

<style scoped>
.ms-achieved {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.ms-planned {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
}
</style>
