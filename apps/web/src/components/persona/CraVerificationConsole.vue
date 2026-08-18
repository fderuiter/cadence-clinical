<template>
  <div class="cra-verification-console">
    <!-- Batch Verification Action Bar -->
    <div
      v-if="selectedBatchFields.length > 0"
      id="batch-sdv-bar"
      style="
        background-color: #eff6ff;
        border: 1px solid #bfdbfe;
        border-radius: 8px;
        padding: 12px 16px;
        margin-bottom: var(--spacing-md);
        display: flex;
        justify-content: space-between;
        align-items: center;
        flex-wrap: wrap;
        gap: var(--spacing-sm);
      "
    >
      <div style="font-size: 0.9rem; font-weight: 600; color: #1e40af">
        Selected {{ selectedBatchFields.length }} fields for Batch Source Data
        Verification
      </div>
      <button
        id="btn-batch-verify"
        class="btn btn-primary"
        style="
          background-color: #2563eb;
          color: white;
          font-weight: bold;
          padding: 6px 12px;
          font-size: 0.85rem;
        "
        @click="$emit('initiate-batch-verify')"
      >
        Batch Verify Selected ({{ selectedBatchFields.length }})
      </button>
    </div>
  </div>
</template>

<script setup>
defineProps({
  selectedBatchFields: {
    type: Array,
    required: true,
  },
  isAuthorizedForBulkSdv: {
    type: Boolean,
    default: true,
  },
});

defineEmits(["initiate-batch-verify"]);
</script>
