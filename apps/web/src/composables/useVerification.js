import { ref, reactive, computed } from "vue";
import { useNotificationsStore } from "../stores/notifications";

export function useVerification(activeUserRole) {
  const sdvStates = reactive({}); // keyed by `${subjectId}:${visitId}:${fieldId}`
  const selectedBatchFields = ref([]);
  const pendingSdvToggle = ref(null);

  const isCraUser = computed(() => {
    return activeUserRole.value === "cra";
  });

  const isAuthorizedForBulkSdv = computed(() => {
    const role = activeUserRole.value;
    return role === "cra" || role === "monitor" || role === "data_manager";
  });

  function getSdvKey(subjectId, visitId, fieldId) {
    return `${subjectId}:${visitId}:${fieldId}`;
  }

  function handleSdvToggle(fieldId, checked, showReasonModalFn) {
    pendingSdvToggle.value = { fieldId, checked };
    if (showReasonModalFn) {
      showReasonModalFn();
    }
  }

  function initiateBatchVerify(openReauthModalFn, store, authStore) {
    if (selectedBatchFields.value.length === 0) {
      alert("No fields selected for batch verification!");
      return;
    }
    if (openReauthModalFn) {
      openReauthModalFn({
        action: "BULK_SDV",
        username: store.user?.username || authStore.identity?.username || "fderuiter",
      });
    }
  }

  function handleVerificationInvalidationOnEdit(field, oldValue, newValue, selectedSubjectId, selectedVisitId, store) {
    const sKey = getSdvKey(selectedSubjectId, selectedVisitId, field.id);
    if (sdvStates[sKey] === true) {
      sdvStates[sKey] = false;

      if (store && store.addLedgerBlock) {
        store.addLedgerBlock(
          "SDV_CLEAR",
          {
            fieldId: field.id,
            label: field.label,
            subjectId: selectedSubjectId,
            visitId: selectedVisitId,
            oldValue,
            newValue,
          },
          "Verification cleared automatically due to field value modification"
        );
      }

      try {
        const notifStore = useNotificationsStore();
        const newNotif = {
          id:
            "notif-sdv-clear-" +
            Date.now() +
            "-" +
            Math.random().toString(36).substr(2, 4),
          recipient_user_id: store.user?.username || "fderuiter",
          recipient_role: "monitor",
          category: "ALERTS",
          priority: "HIGH",
          channels: "IN_APP",
          message_content: `Verification cleared automatically: Field "${field.label}" was modified from "${oldValue}" to "${newValue}" for Subject ${selectedSubjectId}.`,
          related_entity_id: field.id,
          related_entity_type: "FIELD",
          status: "OPEN",
          delivery_state: "DELIVERED",
          created_at: new Date().toISOString(),
          created_by: "system",
        };
        notifStore.notifications.unshift(newNotif);
      } catch (e) {
        console.error("Failed to append notification alert", e);
      }
    }
  }

  return {
    sdvStates,
    selectedBatchFields,
    pendingSdvToggle,
    isCraUser,
    isAuthorizedForBulkSdv,
    getSdvKey,
    handleSdvToggle,
    initiateBatchVerify,
    handleVerificationInvalidationOnEdit,
  };
}
