import { ref, computed } from "vue";

export function useSchemaIngestion(store) {
  const fileInputRef = ref(null);
  const selectedFileName = ref("");
  const editingItemId = ref(null);
  const editItemValue = ref("");
  const editItemReason = ref("");
  const rejectingItemId = ref(null);
  const rejectItemReason = ref("");
  const promoteChangeReason = ref("");

  const unreviewedCount = computed(() => {
    if (!store.candidateDraft || !store.candidateDraft.items) return 0;
    return Object.values(store.candidateDraft.items).filter(
      (item) => item.review_status === "PENDING"
    ).length;
  });

  function triggerFileSelect() {
    if (fileInputRef.value) {
      fileInputRef.value.click();
    }
  }

  async function triggerDocumentUpload(event) {
    const file = event.target.files[0];
    if (!file) return;

    selectedFileName.value = file.name;
    try {
      await store.uploadProtocolDocument(
        file,
        "Uploader triggers protocol ingestion scan."
      );
      alert("Protocol Document ingested successfully. Candidate draft loaded.");
    } catch (err) {
      alert("Ingestion failed: " + err.message);
    }
  }

  function getConfidenceClass(level) {
    if (level === "auto") return "lookup-valid";
    if (level === "needs-review") return "lookup-degraded";
    return "lookup-invalid";
  }

  function getStatusClass(status) {
    if (status === "ACCEPTED") return "lookup-valid";
    if (status === "EDITED") return "lookup-degraded";
    if (status === "REJECTED") return "lookup-invalid";
    return "";
  }

  async function acceptItem(itemId) {
    try {
      await store.transitionCandidateItemState(
        store.candidateDraft.id,
        itemId,
        "ACCEPTED",
        "Accepted by clinical reviewer"
      );
    } catch (err) {
      alert("Transition failed: " + err.message);
    }
  }

  function startEditItem(item) {
    editingItemId.value = item.id;
    editItemValue.value = item.type === "visit" ? item.name : item.label;
    editItemReason.value = "";
  }

  function cancelEditItem() {
    editingItemId.value = null;
    editItemValue.value = "";
    editItemReason.value = "";
  }

  async function saveEditItem(itemId) {
    if (!editItemReason.value.trim()) {
      alert("Change reason justification is mandatory for edits!");
      return;
    }
    const item = store.candidateDraft.items[itemId];
    const payload =
      item.type === "visit"
        ? { name: editItemValue.value }
        : { label: editItemValue.value };

    try {
      await store.transitionCandidateItemState(
        store.candidateDraft.id,
        itemId,
        "EDITED",
        editItemReason.value,
        payload
      );
      editingItemId.value = null;
      editItemValue.value = "";
      editItemReason.value = "";
    } catch (err) {
      alert("Transition failed: " + err.message);
    }
  }

  function startRejectItem(itemId) {
    rejectingItemId.value = itemId;
    rejectItemReason.value = "";
  }

  function cancelRejectItem() {
    rejectingItemId.value = null;
    rejectItemReason.value = "";
  }

  async function confirmRejectItem(itemId) {
    if (!rejectItemReason.value.trim()) {
      alert("Change reason justification is mandatory for rejection!");
      return;
    }
    try {
      await store.transitionCandidateItemState(
        store.candidateDraft.id,
        itemId,
        "REJECTED",
        rejectItemReason.value
      );
      rejectingItemId.value = null;
      rejectItemReason.value = "";
    } catch (err) {
      alert("Transition failed: " + err.message);
    }
  }

  async function promoteCandidate() {
    if (!promoteChangeReason.value.trim()) {
      alert("Promotion change reason justification is mandatory!");
      return;
    }
    try {
      await store.promoteCandidateDraft(
        store.candidateDraft.id,
        promoteChangeReason.value
      );
      alert("Candidate promoted successfully into formal DRAFT version!");
      promoteChangeReason.value = "";
    } catch (err) {
      alert("Promotion failed: " + err.message);
    }
  }

  return {
    fileInputRef,
    selectedFileName,
    editingItemId,
    editItemValue,
    editItemReason,
    rejectingItemId,
    rejectItemReason,
    promoteChangeReason,
    unreviewedCount,
    triggerFileSelect,
    triggerDocumentUpload,
    getConfidenceClass,
    getStatusClass,
    acceptItem,
    startEditItem,
    cancelEditItem,
    saveEditItem,
    startRejectItem,
    cancelRejectItem,
    confirmRejectItem,
    promoteCandidate,
  };
}
