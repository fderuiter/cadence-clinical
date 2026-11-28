import { computed, onMounted, ref, watch } from "vue";
import { requestStepUpSignature } from "../../../api/signatureVerification";
import { trainingApi } from "../../../api/training";
import { canAssignTrainingCurriculum } from "../../../canvas/authority";
import { useActiveStudyScope } from "../../../composables/useActiveStudyScope";
import { useAuthStore } from "../../../stores/auth";
import {
  TRAINING_ASSIGNMENT_COMPLETE_SEMANTIC_ACTION,
  trainingAssignmentCompleteAction,
} from "../../../utils/signatureBinding";

const distinctSorted = (values) =>
  [...new Set(values.filter(Boolean))].sort((a, b) => a.localeCompare(b));

export function normalizePercentScore(rawScore) {
  if (rawScore === null || rawScore === undefined || rawScore === "") {
    return null;
  }
  const num = Number(rawScore);
  if (!Number.isFinite(num)) return null;
  return num <= 1 ? Math.round(num * 100) : Math.round(num);
}

export function useProtocolTrainingConsole(props, emit) {
  const authStore = useAuthStore();
  const { activeStudyCode } = useActiveStudyScope();
  const effectiveStudyId = computed(
    () => (props.studyId || "").trim() || activeStudyCode.value
  );
  const canAssignCurriculum = computed(() =>
    canAssignTrainingCurriculum(authStore)
  );

  const loading = ref(false);
  const assignments = ref([]);
  const curricula = ref([]);
  const studyStaff = ref([]);
  const selectedAssignmentId = ref("");
  const selectedCurriculumId = ref("");
  const activeAssignment = ref(null);
  const activeCurriculum = ref(null);
  const activeTab = ref("modules");
  const selectedModuleIndex = ref(0);
  const isGated = ref(true);

  const quizAnswers = ref({});
  const attestationForm = ref({
    meaning: "I have read, understood, and completed protocol training",
    reasonForChange: "Initial protocol qualification attestation",
    signerCredential: "",
  });
  const submittingCompletion = ref(false);
  const completionResult = ref(null);
  const completionError = ref("");

  const showAuthorModal = ref(false);
  const submittingCurriculum = ref(false);
  const authorError = ref("");
  const authorForm = ref({
    title: "",
    protocolVersion: props.protocolVersion || "2.0",
    passingPercent: 80,
    moduleTitle: "Protocol Synopsis & Eligibility Deltas",
    moduleSummary: "",
    quizPrompt: "",
    correctOptionLabel: "",
    distractorOptionLabel: "",
    reasonForChange: "Publish protocol amendment training curriculum",
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
      label: `Procedural Modules & Quiz (${activeCurriculum.value?.modules?.length || 0})`,
    },
    { id: "batch", label: "Batch Assignments (Kernel Site Grants)" },
  ]);

  const availableSites = computed(() =>
    distinctSorted(studyStaff.value.map((s) => s.site_id))
  );
  const availableRoles = computed(() =>
    distinctSorted(studyStaff.value.map((s) => s.role))
  );

  const computedBatchCount = computed(() => {
    const sites = batchForm.value.selectedSites;
    const roles = batchForm.value.selectedRoles;
    if (sites.length === 0 && roles.length === 0) return 0;
    const reached = new Set(
      studyStaff.value
        .filter((s) => sites.length === 0 || sites.includes(s.site_id))
        .filter((s) => roles.length === 0 || roles.includes(s.role))
        .map((s) => `${s.user_id}|${s.site_id}`)
    );
    return reached.size;
  });

  const quizQuestions = computed(
    () => activeCurriculum.value?.quiz_questions || []
  );

  const quizReady = computed(() => {
    if (quizQuestions.value.length === 0) return true;
    return quizQuestions.value.every((q) => Boolean(quizAnswers.value[q.id]));
  });

  const liveQuizPercent = computed(() => {
    const questions = quizQuestions.value;
    if (questions.length === 0) return null;
    const answered = questions.filter((q) => Boolean(quizAnswers.value[q.id]));
    if (answered.length === 0) return null;
    const correct = questions.filter(
      (q) => quizAnswers.value[q.id] === q.correct_option_id
    ).length;
    return Math.round((correct / questions.length) * 100);
  });

  function resetAttestationState(curriculum) {
    quizAnswers.value = {};
    completionError.value = "";
    completionResult.value = null;
    const version = curriculum?.protocol_version || props.protocolVersion || "";
    attestationForm.value.reasonForChange = version
      ? `Completed protocol v${version} qualification training`
      : "Completed protocol qualification training";
  }

  async function loadCurriculumDetails(curriculumId) {
    try {
      const detail = await trainingApi.getCurriculum(curriculumId);
      activeCurriculum.value = detail;
      resetAttestationState(detail);
    } catch (err) {
      console.error("Error loading curriculum detail:", err);
    }
  }

  async function loadAssignmentDetails(asnId) {
    try {
      const detail = await trainingApi.getAssignmentDetail(asnId);
      activeAssignment.value = detail;
      isGated.value = detail.status !== "COMPLETED";
      const embedded = detail.curriculum || null;
      if (embedded) {
        activeCurriculum.value = embedded;
        selectedCurriculumId.value = embedded.id || detail.curriculum_id || "";
        resetAttestationState(embedded);
      } else if (detail.curriculum_id) {
        selectedCurriculumId.value = detail.curriculum_id;
        await loadCurriculumDetails(detail.curriculum_id);
      } else {
        activeCurriculum.value = null;
      }
    } catch (err) {
      console.error("Error loading assignment detail:", err);
    }
  }

  async function loadStudyStaff() {
    const studyId = effectiveStudyId.value || activeCurriculum.value?.study_id;
    if (!studyId) {
      studyStaff.value = [];
      return;
    }
    try {
      const query = { study_id: studyId };
      if (props.siteId) {
        query.site_id = props.siteId;
      }
      const res = await trainingApi.listStudyStaff(query);
      studyStaff.value = Array.isArray(res) ? res : [];
    } catch (err) {
      console.error("Failed to load study staff grants:", err);
      studyStaff.value = [];
    }
  }

  async function refreshData() {
    loading.value = true;
    try {
      const userId =
        authStore.identity?.sub ||
        authStore.identity?.username ||
        authStore.user?.username ||
        authStore.userId;
      if (!userId) {
        assignments.value = [];
        curricula.value = [];
        studyStaff.value = [];
        activeAssignment.value = null;
        activeCurriculum.value = null;
        return;
      }

      const asnQuery = { user_id: userId };
      if (effectiveStudyId.value) asnQuery.study_id = effectiveStudyId.value;
      if (props.siteId) asnQuery.site_id = props.siteId;

      const curPromise = effectiveStudyId.value
        ? trainingApi.listCurricula({
            study_id: effectiveStudyId.value,
            ...(props.protocolVersion
              ? { protocol_version: props.protocolVersion }
              : {}),
          })
        : Promise.resolve([]);

      const [asnList, curList] = await Promise.all([
        trainingApi.listAssignments(asnQuery),
        curPromise,
      ]);

      assignments.value = Array.isArray(asnList) ? asnList : [];
      curricula.value = Array.isArray(curList) ? curList : [];

      if (assignments.value.length > 0 && !selectedAssignmentId.value) {
        const pending = assignments.value.find((a) => a.status !== "COMPLETED");
        selectedAssignmentId.value = (pending || assignments.value[0]).id;
      }

      if (selectedAssignmentId.value) {
        await loadAssignmentDetails(selectedAssignmentId.value);
      } else if (curricula.value.length > 0) {
        if (!selectedCurriculumId.value) {
          selectedCurriculumId.value = curricula.value[0].id;
        }
        await loadCurriculumDetails(selectedCurriculumId.value);
      } else {
        activeAssignment.value = null;
        activeCurriculum.value = null;
      }

      await loadStudyStaff();
    } catch (err) {
      console.error("Failed to load training assignments:", err);
    } finally {
      loading.value = false;
    }
  }

  function handleTrainingTabChange(tabId) {
    if (tabId === "batch") {
      void loadStudyStaff();
    }
    activeTab.value = tabId;
  }

  function onAssignmentChange() {
    if (selectedAssignmentId.value) {
      void loadAssignmentDetails(selectedAssignmentId.value);
      selectedModuleIndex.value = 0;
    }
  }

  function onCurriculumChange() {
    if (selectedCurriculumId.value) {
      activeAssignment.value =
        assignments.value.find(
          (a) => a.curriculum_id === selectedCurriculumId.value
        ) || null;
      selectedAssignmentId.value = activeAssignment.value?.id || "";
      isGated.value = activeAssignment.value?.status !== "COMPLETED";
      void loadCurriculumDetails(selectedCurriculumId.value);
      selectedModuleIndex.value = 0;
    }
  }

  function setQuizAnswer(questionId, optionId) {
    quizAnswers.value = {
      ...quizAnswers.value,
      [questionId]: optionId,
    };
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
      emit?.("training-updated");
    } catch (err) {
      batchError.value = err?.message || "The batch assignment was refused.";
      console.error("Batch assignment error:", err);
    } finally {
      submittingBatch.value = false;
    }
  }

  async function handleAuthorCurriculum() {
    const studyId = effectiveStudyId.value || activeCurriculum.value?.study_id;
    if (!studyId) return;
    submittingCurriculum.value = true;
    authorError.value = "";
    try {
      const f = authorForm.value;
      const passingFraction = Math.min(
        1,
        Math.max(0.5, Number(f.passingPercent || 80) / 100)
      );
      const parsedVersionNum =
        Number.parseInt(String(f.protocolVersion), 10) || 1;
      const questions =
        f.quizPrompt.trim() && f.correctOptionLabel.trim()
          ? [
              {
                id: "q-1",
                prompt: f.quizPrompt.trim(),
                options: [
                  { id: "opt-a", label: f.correctOptionLabel.trim() },
                  {
                    id: "opt-b",
                    label:
                      f.distractorOptionLabel.trim() ||
                      "Do not apply amendment protocol update",
                  },
                ],
                correct_option_id: "opt-a",
              },
            ]
          : [];

      const created = await trainingApi.createCurriculum({
        study_id: studyId,
        protocol_version: f.protocolVersion.trim(),
        protocol_version_number: parsedVersionNum,
        title: f.title.trim(),
        description: f.moduleSummary.trim(),
        qualification_code: "protocol_training",
        passing_score: passingFraction,
        modules: [
          {
            id: "mod-1",
            title: f.moduleTitle.trim(),
            section_ref: `Protocol v${f.protocolVersion.trim()}`,
            summary: f.moduleSummary.trim(),
            key_changes: [f.moduleSummary.trim()],
            estimated_minutes: 5,
          },
        ],
        quiz_questions: questions,
        reason_for_change: f.reasonForChange.trim(),
      });

      showAuthorModal.value = false;
      selectedAssignmentId.value = "";
      selectedCurriculumId.value = created.id;
      await refreshData();
      emit?.("training-updated");
    } catch (err) {
      authorError.value =
        err?.message || "Protocol curriculum could not be published.";
    } finally {
      submittingCurriculum.value = false;
    }
  }

  async function handleCompleteAssignment() {
    const asn = activeAssignment.value;
    const credential = attestationForm.value.signerCredential;
    const meaning = attestationForm.value.meaning.trim();
    const reason = attestationForm.value.reasonForChange.trim();
    if (!asn || !credential || !meaning || !reason) return;

    submittingCompletion.value = true;
    completionError.value = "";
    try {
      const signerUsername =
        authStore.identity?.username ||
        authStore.user?.username ||
        authStore.userId ||
        asn.user_id;
      const verification = await requestStepUpSignature({
        username: signerUsername,
        password: credential,
        totp: null,
        action: trainingAssignmentCompleteAction(asn.id),
        semanticAction: TRAINING_ASSIGNMENT_COMPLETE_SEMANTIC_ACTION,
      });
      attestationForm.value.signerCredential = "";

      const answersPayload = Object.entries(quizAnswers.value).map(
        ([questionId, selectedOptionId]) => ({
          question_id: questionId,
          selected_option_id: String(selectedOptionId),
        })
      );
      const estimatedSeconds =
        (activeCurriculum.value?.modules?.length || 1) * 120;

      const result = await trainingApi.completeAssignment(
        asn.id,
        {
          quiz_answers: answersPayload,
          time_spent_seconds: estimatedSeconds,
          signature_payload: {
            meaning,
            reason_for_change: reason,
          },
        },
        verification.sig_token
      );
      completionResult.value = result;
      activeAssignment.value = result.assignment;
      isGated.value = false;
      await refreshData();
      completionResult.value = result;
      emit?.("training-updated");
    } catch (err) {
      completionError.value =
        err?.message || "Qualification attestation could not be completed.";
    } finally {
      submittingCompletion.value = false;
    }
  }

  watch(
    () => [effectiveStudyId.value, props.siteId, props.protocolVersion],
    () => {
      selectedAssignmentId.value = "";
      selectedCurriculumId.value = "";
      void refreshData();
    }
  );

  onMounted(() => {
    void refreshData();
  });

  return {
    loading,
    effectiveStudyId,
    assignments,
    curricula,
    studyStaff,
    selectedAssignmentId,
    selectedCurriculumId,
    activeAssignment,
    activeCurriculum,
    activeTab,
    selectedModuleIndex,
    isGated,
    canAssignCurriculum,
    trainingTabs,
    availableSites,
    availableRoles,
    batchForm,
    showBatchModal,
    submittingBatch,
    batchResult,
    batchError,
    computedBatchCount,
    quizAnswers,
    quizQuestions,
    quizReady,
    liveQuizPercent,
    attestationForm,
    submittingCompletion,
    completionResult,
    completionError,
    showAuthorModal,
    submittingCurriculum,
    authorError,
    authorForm,
    refreshData,
    loadStudyStaff,
    handleTrainingTabChange,
    onAssignmentChange,
    onCurriculumChange,
    setQuizAnswer,
    handleExecuteBatchAssign,
    handleAuthorCurriculum,
    handleCompleteAssignment,
  };
}
