import { computed, ref, type Ref } from "vue";
import { kernelApi, type KernelChild } from "../../api/kernelApi";
import { parseNodeRef } from "../../canvas/nodeRef";
import { seedNodeSummary } from "../../canvas/nodeResolver";

const SCOPE_LABELS: Record<string, string> = {
  study: "study",
  study_site: "site",
  enrollment: "participant",
  collection_instance: "form",
  observation: "observation",
};

export interface PickerOption {
  key: string;
  ref: string;
  label: string;
  masked: boolean;
}

export function scopeLabel(kind: string): string {
  return SCOPE_LABELS[kind] ?? kind;
}

export function unversionedNodeRef(raw: string | null | undefined): string {
  if (!raw) return "";
  const parsed = parseNodeRef(raw);
  if (parsed) return `${parsed.kind}:${parsed.id}`;
  const at = raw.indexOf("@");
  return at < 0 ? raw : raw.slice(0, at);
}

export function seedVisibleChildren(children: KernelChild[]) {
  for (const child of children) {
    if (child.node.ref && !child.node.mask) {
      const summary = {
        kind: child.node.kind,
        code: child.node.code,
        title: child.node.title,
        status: child.node.status,
      };
      seedNodeSummary(child.node.ref, summary);
      const unversioned = unversionedNodeRef(child.node.ref);
      if (unversioned && unversioned !== child.node.ref) {
        seedNodeSummary(unversioned, summary);
      }
    }
  }
}

const STANDALONE_OBSERVATIONS_REF = "__standalone_observations__";

function buildPickerOptions(
  children: KernelChild[],
  expectedKind: string
): PickerOption[] {
  return children
    .filter((child) => child.node.kind === expectedKind)
    .map((child, idx) => {
      if (child.node.mask || !child.node.ref) {
        return {
          key: `masked-${expectedKind}-${idx}`,
          ref: `__masked_${expectedKind}_${idx}__`,
          label: `Masked (${child.node.mask ?? "Restricted"}): ${scopeLabel(expectedKind)}`,
          masked: true,
        };
      }
      const cleanRef = unversionedNodeRef(child.node.ref);
      const primary = child.node.code || child.node.title || cleanRef;
      const detail =
        child.node.title &&
        child.node.code &&
        child.node.title !== child.node.code
          ? ` — ${child.node.title}`
          : "";
      const statusSuffix = child.node.status ? ` (${child.node.status})` : "";
      return {
        key: cleanRef,
        ref: cleanRef,
        label: `${primary}${detail}${statusSuffix}`,
        masked: false,
      };
    });
}

function firstUnmaskedRef(options: PickerOption[]): string {
  return options.find((opt) => !opt.masked)?.ref ?? "";
}

export function useLockHierarchyPickers(studyRef: Ref<string | null>) {
  const studyChildren = ref<KernelChild[]>([]);
  const siteEnrollments = ref<KernelChild[] | null>(null);
  const enrollmentsLoading = ref(false);
  const enrollmentForms = ref<KernelChild[]>([]);
  const formsLoading = ref(false);
  const formObservations = ref<KernelChild[]>([]);
  const observationsLoading = ref(false);

  const applyScopeKind = ref<string>("study");
  const selectedSiteRef = ref("");
  const selectedEnrollmentRef = ref("");
  const selectedFormRef = ref("");
  const selectedObservationRef = ref("");

  let siteSeq = 0;
  let enrollmentSeq = 0;
  let formSeq = 0;

  const needsSitePicker = computed(() => applyScopeKind.value !== "study");
  const needsEnrollmentPicker = computed(
    () =>
      applyScopeKind.value === "enrollment" ||
      applyScopeKind.value === "collection_instance" ||
      applyScopeKind.value === "observation"
  );
  const needsFormPicker = computed(
    () =>
      applyScopeKind.value === "collection_instance" ||
      applyScopeKind.value === "observation"
  );
  const needsObservationPicker = computed(
    () => applyScopeKind.value === "observation"
  );

  const sitePickerOptions = computed(() =>
    buildPickerOptions(studyChildren.value, "study_site")
  );

  const enrollmentPickerOptions = computed(() => {
    const source =
      selectedSiteRef.value && siteEnrollments.value !== null
        ? siteEnrollments.value
        : studyChildren.value;
    return buildPickerOptions(source, "enrollment");
  });

  const formPickerOptions = computed(() => {
    const forms = buildPickerOptions(
      enrollmentForms.value,
      "collection_instance"
    );
    if (
      applyScopeKind.value === "observation" &&
      enrollmentForms.value.some((child) => child.node.kind === "observation")
    ) {
      return [
        ...forms,
        {
          key: STANDALONE_OBSERVATIONS_REF,
          ref: STANDALONE_OBSERVATIONS_REF,
          label: "Standalone observations (not in a form)",
          masked: false,
        },
      ];
    }
    return forms;
  });

  const observationPickerOptions = computed(() => {
    if (selectedFormRef.value === STANDALONE_OBSERVATIONS_REF) {
      return buildPickerOptions(enrollmentForms.value, "observation");
    }
    return buildPickerOptions(formObservations.value, "observation");
  });

  const applyScopeRef = computed(() => {
    switch (applyScopeKind.value) {
      case "study":
        return unversionedNodeRef(studyRef.value);
      case "study_site":
        return selectedSiteRef.value.startsWith("study_site:")
          ? selectedSiteRef.value
          : "";
      case "enrollment":
        return selectedEnrollmentRef.value.startsWith("enrollment:")
          ? selectedEnrollmentRef.value
          : "";
      case "collection_instance":
        return selectedFormRef.value.startsWith("collection_instance:")
          ? selectedFormRef.value
          : "";
      case "observation":
        return selectedObservationRef.value.startsWith("observation:")
          ? selectedObservationRef.value
          : "";
      default:
        return "";
    }
  });

  function resetHierarchyPickers() {
    siteSeq++;
    enrollmentSeq++;
    formSeq++;
    studyChildren.value = [];
    siteEnrollments.value = null;
    enrollmentForms.value = [];
    formObservations.value = [];
    enrollmentsLoading.value = false;
    formsLoading.value = false;
    observationsLoading.value = false;
    selectedSiteRef.value = "";
    selectedEnrollmentRef.value = "";
    selectedFormRef.value = "";
    selectedObservationRef.value = "";
  }

  function setStudyChildren(children: KernelChild[]) {
    studyChildren.value = children;
    seedVisibleChildren(studyChildren.value);
  }

  async function loadEnrollmentsForSelectedSite(siteRef: string) {
    const seq = ++siteSeq;
    selectedEnrollmentRef.value = "";
    selectedFormRef.value = "";
    selectedObservationRef.value = "";
    enrollmentForms.value = [];
    formObservations.value = [];

    if (!siteRef || typeof kernelApi.getChildren !== "function") {
      siteEnrollments.value = null;
      if (needsEnrollmentPicker.value) {
        const autoEnrollment = firstUnmaskedRef(enrollmentPickerOptions.value);
        if (autoEnrollment) {
          selectedEnrollmentRef.value = autoEnrollment;
          await onEnrollmentPickerChange();
        }
      }
      return;
    }

    enrollmentsLoading.value = true;
    try {
      const children = await kernelApi.getChildren(unversionedNodeRef(siteRef));
      if (seq !== siteSeq) return;
      siteEnrollments.value = children ?? [];
      seedVisibleChildren(siteEnrollments.value);
      if (needsEnrollmentPicker.value) {
        const autoEnrollment = firstUnmaskedRef(enrollmentPickerOptions.value);
        if (autoEnrollment) {
          selectedEnrollmentRef.value = autoEnrollment;
          await onEnrollmentPickerChange();
        }
      }
    } catch {
      if (seq === siteSeq) siteEnrollments.value = [];
    } finally {
      if (seq === siteSeq) enrollmentsLoading.value = false;
    }
  }

  async function loadFormsForSelectedEnrollment(enrollmentRef: string) {
    const seq = ++enrollmentSeq;
    selectedFormRef.value = "";
    selectedObservationRef.value = "";
    enrollmentForms.value = [];
    formObservations.value = [];

    if (
      !enrollmentRef ||
      !needsFormPicker.value ||
      typeof kernelApi.getChildren !== "function"
    ) {
      return;
    }

    formsLoading.value = true;
    try {
      const children = await kernelApi.getChildren(
        unversionedNodeRef(enrollmentRef)
      );
      if (seq !== enrollmentSeq) return;
      enrollmentForms.value = children ?? [];
      seedVisibleChildren(enrollmentForms.value);
      const autoForm = firstUnmaskedRef(formPickerOptions.value);
      if (autoForm) {
        selectedFormRef.value = autoForm;
        await onFormPickerChange();
      }
    } catch {
      if (seq === enrollmentSeq) enrollmentForms.value = [];
    } finally {
      if (seq === enrollmentSeq) formsLoading.value = false;
    }
  }

  async function loadObservationsForSelectedForm(formRef: string) {
    const seq = ++formSeq;
    selectedObservationRef.value = "";
    formObservations.value = [];

    if (
      !formRef ||
      !needsObservationPicker.value ||
      typeof kernelApi.getChildren !== "function"
    ) {
      return;
    }

    if (formRef === STANDALONE_OBSERVATIONS_REF) {
      const autoStandalone = firstUnmaskedRef(observationPickerOptions.value);
      if (autoStandalone) {
        selectedObservationRef.value = autoStandalone;
      }
      return;
    }

    observationsLoading.value = true;
    try {
      const children = await kernelApi.getChildren(unversionedNodeRef(formRef));
      if (seq !== formSeq) return;
      formObservations.value = children ?? [];
      seedVisibleChildren(formObservations.value);
      const autoObs = firstUnmaskedRef(observationPickerOptions.value);
      if (autoObs) {
        selectedObservationRef.value = autoObs;
      }
    } catch {
      if (seq === formSeq) formObservations.value = [];
    } finally {
      if (seq === formSeq) observationsLoading.value = false;
    }
  }

  async function onScopeKindChange() {
    if (applyScopeKind.value === "study") return;
    if (applyScopeKind.value === "study_site") {
      if (!selectedSiteRef.value) {
        selectedSiteRef.value = firstUnmaskedRef(sitePickerOptions.value);
      }
      return;
    }
    if (!selectedEnrollmentRef.value) {
      const firstEnrollment = firstUnmaskedRef(enrollmentPickerOptions.value);
      if (firstEnrollment) {
        selectedEnrollmentRef.value = firstEnrollment;
      } else if (!selectedSiteRef.value) {
        const firstSite = firstUnmaskedRef(sitePickerOptions.value);
        if (firstSite) {
          selectedSiteRef.value = firstSite;
          await loadEnrollmentsForSelectedSite(firstSite);
          return;
        }
      }
    }
    if (needsFormPicker.value && selectedEnrollmentRef.value) {
      await loadFormsForSelectedEnrollment(selectedEnrollmentRef.value);
    }
  }

  async function onSitePickerChange() {
    if (needsEnrollmentPicker.value) {
      await loadEnrollmentsForSelectedSite(selectedSiteRef.value);
    }
  }

  async function onEnrollmentPickerChange() {
    if (needsFormPicker.value) {
      await loadFormsForSelectedEnrollment(selectedEnrollmentRef.value);
    }
  }

  async function onFormPickerChange() {
    if (needsObservationPicker.value) {
      await loadObservationsForSelectedForm(selectedFormRef.value);
    }
  }

  return {
    enrollmentsLoading,
    formsLoading,
    observationsLoading,
    applyScopeKind,
    selectedSiteRef,
    selectedEnrollmentRef,
    selectedFormRef,
    selectedObservationRef,
    needsSitePicker,
    needsEnrollmentPicker,
    needsFormPicker,
    needsObservationPicker,
    sitePickerOptions,
    enrollmentPickerOptions,
    formPickerOptions,
    observationPickerOptions,
    applyScopeRef,
    resetHierarchyPickers,
    setStudyChildren,
    onScopeKindChange,
    onSitePickerChange,
    onEnrollmentPickerChange,
    onFormPickerChange,
  };
}
