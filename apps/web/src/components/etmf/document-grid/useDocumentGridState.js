import { computed, ref, watch } from "vue";
import {
  canExportEtmf,
  canStageEtmfQc,
  canTagEtmf,
} from "../../../canvas/authority.ts";
import { use2DGrid } from "../../../composables/use2DGrid";
import { useAuthStore } from "../../../stores/auth";
import { useEtmfStore } from "../../../stores/etmf";
import { formatDate } from "../../../utils/formatDate";

let cachedGridActionsPromise =
  import.meta.env?.MODE === "test" ? import("./documentGridActions.js") : null;

function loadGridActions() {
  if (!cachedGridActionsPromise) {
    cachedGridActionsPromise = import("./documentGridActions.js");
  }
  return cachedGridActionsPromise;
}

export function getZoneAndSectionFromCode(code) {
  if (!code) return { zone: 1, section: "01.01" };
  const parts = code.split(".");
  const zone = parseInt(parts[0]) || 1;
  const section = parts.slice(0, 2).join(".");
  return { zone, section };
}

export function getStatusClass(status) {
  const s = (status || "").toLowerCase();
  if (s.includes("draft")) return "status-draft";
  if (s.includes("review") || s.includes("pending")) return "status-pending";
  if (s.includes("approved") || s.includes("active")) return "status-approved";
  if (s.includes("archive")) return "status-archived";
  return "status-default";
}

export function formatStatus(status) {
  const s = status || "DRAFT";
  return s.toUpperCase().replace(/_/g, " ");
}

export function useDocumentGridState(props, onStartEditing) {
  const etmfStore = useEtmfStore();
  const authStore = useAuthStore();

  const hasActiveAuth = computed(() =>
    Boolean(
      authStore.isAuthenticated ||
      (Array.isArray(authStore.rawRoles) && authStore.rawRoles.length > 0)
    )
  );
  const canStage = computed(
    () => !hasActiveAuth.value || canStageEtmfQc(authStore)
  );
  const canTag = computed(() => !hasActiveAuth.value || canTagEtmf(authStore));
  const canExport = computed(
    () => !hasActiveAuth.value || canExportEtmf(authStore)
  );

  const containerRef = ref(null);
  const tempTaxonomyCode = ref("");
  const inlineSelectRef = ref(null);

  const currentPage = ref(1);
  const itemsPerPage = 20;

  const formattedDocuments = computed(() =>
    props.documents.map((doc) => ({
      ...doc,
      formattedCreatedAt: formatDate(doc.created_at),
    }))
  );

  const totalItems = computed(() => formattedDocuments.value.length);
  const totalPages = computed(
    () => Math.ceil(totalItems.value / itemsPerPage) || 1
  );

  const paginatedDocuments = computed(() => {
    const start = (currentPage.value - 1) * itemsPerPage;
    return formattedDocuments.value.slice(start, start + itemsPerPage);
  });

  const grid = use2DGrid(() => paginatedDocuments.value.length, 7, {
    containerRef,
    initialRow: 0,
    initialCol: 0,
    onCopyCell: (_rIndex, _cIndex, doc) => copyActiveCellValue(doc),
    onCellAction: (rIndex, cIndex, doc) => {
      if (cIndex === 1) {
        onStartEditing(rIndex, doc);
      }
    },
  });

  const {
    activeRowIndex,
    activeColIndex,
    isEditing,
    isCellActive,
    getCellTabindex,
    selectCell,
    handleCellKeyDown,
    exitEditing,
  } = grid;

  watch(
    () => props.documents,
    (nextDocs) => {
      currentPage.value = 1;
      const hasRows = Array.isArray(nextDocs) && nextDocs.length > 0;
      activeRowIndex.value = hasRows ? 0 : -1;
      activeColIndex.value = hasRows ? 0 : -1;
      isEditing.value = false;
    },
    { deep: true }
  );

  watch(currentPage, () => {
    const hasRows = paginatedDocuments.value.length > 0;
    activeRowIndex.value = hasRows ? 0 : -1;
    activeColIndex.value = hasRows ? 0 : -1;
    isEditing.value = false;
  });

  const taxonomyOptions = computed(() => {
    const allNodes = Object.values(etmfStore.folderLookup);
    const seenCodes = new Set();
    const options = [];
    for (const node of allNodes) {
      if (node.type === "artifact" && node.code && !seenCodes.has(node.code)) {
        seenCodes.add(node.code);
        options.push(node);
      }
    }
    return options;
  });

  function cancelEditing() {
    setTimeout(() => {
      if (isEditing.value) exitEditing();
    }, 150);
  }

  async function commitTaxonomy(doc) {
    if (!isEditing.value) return;
    const targetCode = tempTaxonomyCode.value;
    const { executeCommitTaxonomy } = await loadGridActions();
    await executeCommitTaxonomy({
      doc,
      targetCode,
      etmfStore,
      getZoneAndSectionFromCode,
      cancelEditing,
      exitEditing,
    });
  }

  function copyActiveCellValue(doc) {
    const colIndex = activeColIndex.value;
    void loadGridActions().then(({ executeCopyCellValue }) => {
      executeCopyCellValue(colIndex, doc, formatStatus);
    });
  }

  function handleSelectKeyDown(event, doc) {
    if (event.key === "Enter") {
      event.preventDefault();
      event.stopPropagation();
      commitTaxonomy(doc);
    } else if (event.key === "Escape") {
      event.preventDefault();
      event.stopPropagation();
      cancelEditing();
    }
  }

  const selectedArtifactCode = ref("");
  const showUploadModal = ref(false);
  const isSubmitting = ref(false);
  const uploadError = ref("");

  const selectedDocumentIds = computed(() => etmfStore.selectedDocumentIds);

  function isDocumentSelected(docId) {
    return etmfStore.selectedDocumentIds.includes(docId);
  }

  const isAllSelected = computed(
    () =>
      paginatedDocuments.value.length > 0 &&
      paginatedDocuments.value.every((d) =>
        etmfStore.selectedDocumentIds.includes(d.id)
      )
  );

  function toggleSelectAll() {
    if (isAllSelected.value) {
      const paginatedIds = new Set(paginatedDocuments.value.map((d) => d.id));
      etmfStore.setSelectedDocumentIds(
        etmfStore.selectedDocumentIds.filter((id) => !paginatedIds.has(id))
      );
    } else {
      const selectedSet = new Set(etmfStore.selectedDocumentIds);
      paginatedDocuments.value.forEach((d) => selectedSet.add(d.id));
      etmfStore.setSelectedDocumentIds(Array.from(selectedSet));
    }
  }

  const selectedFile = ref(null);
  const uploadModalRef = ref(null);
  const studyId = ref(etmfStore.currentStudyId || "");
  const siteId = ref(etmfStore.currentSiteId || "");
  const artifactCode = ref("");
  const artifactType = ref("");
  const reasonForChange = ref("");

  watch(
    () => [etmfStore.currentStudyId, etmfStore.currentSiteId],
    ([nextStudy, nextSite]) => {
      studyId.value = nextStudy || "";
      siteId.value = nextSite || "";
    }
  );

  function prepareUploadModal() {
    if (!selectedArtifactCode.value) return;
    void loadGridActions();
    studyId.value = etmfStore.currentStudyId || "";
    siteId.value = etmfStore.currentSiteId || "";
    selectedFile.value = null;
    reasonForChange.value = "";
    uploadError.value = "";
    showUploadModal.value = true;
  }

  function closeUploadModal() {
    showUploadModal.value = false;
    selectedFile.value = null;
  }

  watch(
    () => etmfStore.selectedArtifactId,
    (newCode) => {
      selectedArtifactCode.value = newCode || "";
      if (newCode) {
        artifactCode.value = newCode;
        const node = etmfStore.folderLookup[newCode];
        artifactType.value = node ? node.name : "Clinical Trial Document";
      }
    },
    { immediate: true }
  );

  function prevPage() {
    if (currentPage.value > 1) currentPage.value--;
  }

  function nextPage() {
    if (currentPage.value < totalPages.value) currentPage.value++;
  }

  function goToPage(page) {
    if (page >= 1 && page <= totalPages.value) currentPage.value = page;
  }

  function onRegulatedFileSelected(file) {
    if (file) selectedFile.value = file;
  }

  async function submitUpload() {
    if (!selectedFile.value || !reasonForChange.value.trim()) {
      uploadError.value = "Please select a file and provide a change reason.";
      return;
    }

    isSubmitting.value = true;
    uploadError.value = "";

    try {
      const { executeSubmitDocumentUpload } = await loadGridActions();
      await executeSubmitDocumentUpload({
        file: selectedFile.value,
        studyId: studyId.value,
        siteId: siteId.value,
        artifactCode: artifactCode.value,
        artifactType: artifactType.value,
        reasonForChange: reasonForChange.value,
        etmfStore,
        getZoneAndSectionFromCode,
        closeUploadModal,
        uploadError,
      });
    } finally {
      isSubmitting.value = false;
    }
  }

  return {
    etmfStore,
    canStage,
    canTag,
    canExport,
    containerRef,
    tempTaxonomyCode,
    inlineSelectRef,
    grid,
    isEditing,
    isCellActive,
    getCellTabindex,
    selectCell,
    handleCellKeyDown,
    taxonomyOptions,
    cancelEditing,
    commitTaxonomy,
    handleSelectKeyDown,
    selectedArtifactCode,
    showUploadModal,
    isSubmitting,
    uploadError,
    selectedDocumentIds,
    isDocumentSelected,
    isAllSelected,
    toggleSelectAll,
    selectedFile,
    uploadModalRef,
    studyId,
    siteId,
    artifactCode,
    artifactType,
    reasonForChange,
    currentPage,
    itemsPerPage,
    totalItems,
    totalPages,
    paginatedDocuments,
    prevPage,
    nextPage,
    goToPage,
    prepareUploadModal,
    closeUploadModal,
    onRegulatedFileSelected,
    submitUpload,
  };
}
