import { computed, onMounted, ref, watch } from "vue";
import { requestStepUpSignature } from "../../../api/signatureVerification";
import { trainingApi } from "../../../api/training";
import {
  canAssignTrainingCurriculum,
  canSignTrainingDelegations,
} from "../../../canvas/authority";
import { useActiveStudyScope } from "../../../composables/useActiveStudyScope";
import { useAuthStore } from "../../../stores/auth";
import { formatRoleLabel } from "../../../utils/clinicalLabels";
import {
  DELEGATION_SIGN_OFF_ACTION,
  DELEGATION_SIGN_OFF_SEMANTIC_ACTION,
  delegationBatchBindingId,
} from "../../../utils/signatureBinding";

const distinctSorted = (values) =>
  [...new Set(values.filter(Boolean))].sort((a, b) => a.localeCompare(b));

export function useProtocolTrainingConsole(props) {
  const authStore = useAuthStore();
  const { activeStudyCode } = useActiveStudyScope();
  const effectiveStudyId = computed(
    () => (props.studyId || "").trim() || activeStudyCode.value
  );
  const canAssignCurriculum = computed(() =>
    canAssignTrainingCurriculum(authStore)
  );
  const canSignDelegations = computed(() =>
    canSignTrainingDelegations(authStore)
  );

  const loading = ref(false);
  const assignments = ref([]);
  const curricula = ref([]);
  const selectedAssignmentId = ref("");
  const selectedCurriculumId = ref("");
  const activeAssignment = ref(null);
  const activeCurriculum = ref(null);
  const delegations = ref([]);
  const activeTab = ref("modules");
  const selectedModuleIndex = ref(0);
  const isGated = ref(true);

  const selectedDelegationIds = ref([]);
  const loadingDelegations = ref(false);
  const showBulkSignModal = ref(false);
  const submittingBulkSign = ref(false);
  const bulkSignResult = ref(null);
  const bulkSignError = ref("");
  const bulkSignForm = ref({
    signerName: authStore.identity?.username || "",
    signerRole: authStore.activeRole || "",
    password: "",
    reasonForChange:
      "21 CFR Part 11 Principal Investigator bulk delegation sign-off",
  });

  const batchForm = ref({
    selectedSites: [],
    selectedRoles: [],
    reasonForChange: "",
  });
  const showBatchModal = ref(false);
  const submittingBatch = ref(false);
  const batchResult = ref(null);
  const batchError = ref("");

  const trainingTabs = computed(() => [
    {
      id: "modules",
      label: `Procedural Modules (${activeCurriculum.value?.modules?.length || 0})`,
    },
    { id: "batch", label: "Batch Assignments" },
    { id: "delegations", label: "Delegation Bulk Sign-Off" },
  ]);

  const availableSites = computed(() =>
    distinctSorted(delegations.value.map((d) => d.site_id))
  );
  const availableRoles = computed(() =>
    distinctSorted(delegations.value.map((d) => d.role))
  );

  const computedBatchCount = computed(() => {
    const sites = batchForm.value.selectedSites;
    const roles = batchForm.value.selectedRoles;
    if (sites.length === 0 && roles.length === 0) return 0;
    const reached = new Set(
      delegations.value
        .filter((d) => sites.length === 0 || sites.includes(d.site_id))
        .filter((d) => roles.length === 0 || roles.includes(d.role))
        .map((d) => `${d.staff_user_id}|${d.site_id}`)
    );
    return reached.size;
  });

  const isAllDelegationsSelected = computed(() => {
    const pending = delegations.value.filter((d) => d.status !== "COMPLETED");
    return (
      pending.length > 0 &&
      selectedDelegationIds.value.length === pending.length
    );
  });

  async function loadAssignmentDetails(asnId) {
    try {
      const detail = await trainingApi.getAssignmentDetail(asnId);
      activeAssignment.value = detail;
      activeCurriculum.value = detail.curriculum || null;
      isGated.value = detail.status !== "COMPLETED";
    } catch (err) {
      console.error("Error loading assignment detail:", err);
    }
  }

  async function loadCurriculumDetails(curriculumId) {
    try {
      const detail = await trainingApi.getCurriculum(curriculumId);
      activeCurriculum.value = detail;
    } catch (err) {
      console.error("Error loading curriculum detail:", err);
    }
  }

  async function loadDelegations() {
    const studyId = effectiveStudyId.value || activeCurriculum.value?.study_id;
    if (!studyId) {
      delegations.value = [];
      return;
    }
    loadingDelegations.value = true;
    try {
      const delQuery = { study_id: studyId };
      if (props.siteId) {
        delQuery.site_id = props.siteId;
      }
      const res = await trainingApi.listDelegations(delQuery);
      delegations.value = Array.isArray(res) ? res : [];
    } catch (err) {
      console.error("Failed to load delegations:", err);
    } finally {
      loadingDelegations.value = false;
    }
  }

  async function refreshData() {
    loading.value = true;
    try {
      const userId = authStore.identity?.sub || authStore.identity?.username;
      bulkSignForm.value.signerName = authStore.identity?.username || "";
      bulkSignForm.value.signerRole = formatRoleLabel(
        authStore.activeRole || ""
      );
      if (!userId) {
        assignments.value = [];
        curricula.value = [];
        activeAssignment.value = null;
        activeCurriculum.value = null;
        return;
      }

      const asnQuery = { user_id: userId };
      if (effectiveStudyId.value) asnQuery.study_id = effectiveStudyId.value;
      if (props.siteId) asnQuery.site_id = props.siteId;

      const asnList = await trainingApi.listAssignments(asnQuery);
      assignments.value = Array.isArray(asnList) ? asnList : [];

      if (assignments.value.length > 0 && !selectedAssignmentId.value) {
        selectedAssignmentId.value = assignments.value[0].id;
      }

      if (selectedAssignmentId.value) {
        await loadAssignmentDetails(selectedAssignmentId.value);
      } else if (effectiveStudyId.value) {
        const curQuery = { study_id: effectiveStudyId.value };
        if (props.protocolVersion) {
          curQuery.protocol_version = props.protocolVersion;
        }
        const curList = await trainingApi.listCurricula(curQuery);
        curricula.value = Array.isArray(curList) ? curList : [];
        if (curricula.value.length > 0) {
          selectedCurriculumId.value = curricula.value[0].id;
          await loadCurriculumDetails(selectedCurriculumId.value);
        }
      }

      await loadDelegations();
    } catch (err) {
      console.error("Failed to load training assignments:", err);
    } finally {
      loading.value = false;
    }
  }

  function handleTrainingTabChange(tabId) {
    if (tabId === "delegations") {
      loadDelegations();
    }
    activeTab.value = tabId;
  }

  function onAssignmentChange() {
    if (selectedAssignmentId.value) {
      loadAssignmentDetails(selectedAssignmentId.value);
      selectedModuleIndex.value = 0;
    }
  }

  function onCurriculumChange() {
    if (selectedCurriculumId.value) {
      loadCurriculumDetails(selectedCurriculumId.value);
      selectedModuleIndex.value = 0;
    }
  }

  async function handleExecuteBatchAssign() {
    if (!activeCurriculum.value) return;
    submittingBatch.value = true;
    batchError.value = "";
    try {
      const res = await trainingApi.batchAssignCurriculum({
        curriculum_id: activeCurriculum.value.id,
        study_id: activeCurriculum.value.study_id,
        site_ids: batchForm.value.selectedSites,
        roles: batchForm.value.selectedRoles,
        reason_for_change: batchForm.value.reasonForChange,
      });
      batchResult.value = res;
      showBatchModal.value = false;
      await refreshData();
    } catch (err) {
      batchError.value = err?.message || "The batch assignment was refused.";
      console.error("Batch assignment error:", err);
    } finally {
      submittingBatch.value = false;
    }
  }

  function toggleSelectAllDelegations() {
    const pending = delegations.value.filter((d) => d.status !== "COMPLETED");
    if (selectedDelegationIds.value.length === pending.length) {
      selectedDelegationIds.value = [];
    } else {
      selectedDelegationIds.value = pending.map((d) => d.id);
    }
  }

  function singleSite(delegationIds) {
    const sites = new Set(
      delegations.value
        .filter((d) => delegationIds.includes(d.id))
        .map((d) => d.site_id)
    );
    return sites.size === 1 ? [...sites][0] : null;
  }

  async function handleExecuteBulkSignOff() {
    if (selectedDelegationIds.value.length === 0) return;
    submittingBulkSign.value = true;
    bulkSignError.value = "";
    try {
      const delegationIds = [...selectedDelegationIds.value];
      const verification = await requestStepUpSignature({
        username: authStore.identity?.username || "",
        password: bulkSignForm.value.password,
        action: DELEGATION_SIGN_OFF_ACTION,
        semanticAction: DELEGATION_SIGN_OFF_SEMANTIC_ACTION,
        batchId: await delegationBatchBindingId(delegationIds),
      });
      bulkSignForm.value.password = "";
      const res = await trainingApi.bulkSignOffDelegations(
        {
          delegation_ids: delegationIds,
          study_id:
            effectiveStudyId.value || activeCurriculum.value?.study_id || null,
          site_id: singleSite(delegationIds),
          signature_payload: {
            meaning:
              "I confirm that I have reviewed and approved the delegated duties for the selected site staff personnel in accordance with 21 CFR Part 11 and ICH GCP E6(R2).",
            reason_for_change: bulkSignForm.value.reasonForChange,
          },
        },
        verification.sig_token
      );
      bulkSignResult.value = res;
      showBulkSignModal.value = false;
      selectedDelegationIds.value = [];
      await loadDelegations();
    } catch (err) {
      bulkSignError.value = err?.message || "Bulk delegation sign-off failed.";
      console.error("Bulk sign-off error:", err);
    } finally {
      submittingBulkSign.value = false;
    }
  }

  watch(
    () => [effectiveStudyId.value, props.siteId, props.protocolVersion],
    () => {
      selectedAssignmentId.value = "";
      selectedCurriculumId.value = "";
      refreshData();
    }
  );

  onMounted(() => {
    refreshData();
  });

  return {
    loading,
    assignments,
    curricula,
    selectedAssignmentId,
    selectedCurriculumId,
    activeAssignment,
    activeCurriculum,
    delegations,
    activeTab,
    selectedModuleIndex,
    isGated,
    canAssignCurriculum,
    canSignDelegations,
    trainingTabs,
    availableSites,
    availableRoles,
    batchForm,
    showBatchModal,
    submittingBatch,
    batchResult,
    batchError,
    computedBatchCount,
    selectedDelegationIds,
    showBulkSignModal,
    submittingBulkSign,
    bulkSignResult,
    bulkSignError,
    bulkSignForm,
    isAllDelegationsSelected,
    refreshData,
    loadDelegations,
    handleTrainingTabChange,
    onAssignmentChange,
    onCurriculumChange,
    handleExecuteBatchAssign,
    toggleSelectAllDelegations,
    handleExecuteBulkSignOff,
  };
}
