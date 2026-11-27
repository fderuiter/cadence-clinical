<template>
  <div v-if="totalItems > 0" class="pagination-controls">
    <div class="pagination-info">
      Showing
      <span class="font-semibold">{{
        Math.min((currentPage - 1) * itemsPerPage + 1, totalItems)
      }}</span>
      to
      <span class="font-semibold">{{
        Math.min(currentPage * itemsPerPage, totalItems)
      }}</span>
      of
      <span class="font-semibold">{{ totalItems }}</span> documents
    </div>

    <div class="pagination-buttons">
      <button
        type="button"
        class="btn btn-sm btn-outline-secondary prev-btn"
        :disabled="currentPage === 1"
        aria-label="Previous page"
        @click="emit('prev')"
      >
        <span aria-hidden="true"
          ><ClinicalIcon name="chevron-left" :size="16"
        /></span>
        Previous
      </button>

      <div class="page-numbers">
        <button
          v-for="page in totalPages"
          :key="page"
          type="button"
          class="btn btn-sm page-num-btn"
          :class="{ active: currentPage === page }"
          :aria-label="'Go to page ' + page"
          :aria-current="currentPage === page ? 'page' : undefined"
          @click="emit('goTo', page)"
        >
          {{ page }}
        </button>
      </div>

      <button
        type="button"
        class="btn btn-sm btn-outline-secondary next-btn"
        :disabled="currentPage === totalPages"
        aria-label="Next page"
        @click="emit('next')"
      >
        Next
        <span aria-hidden="true"><ClinicalIcon name="video" :size="16" /></span>
      </button>
    </div>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";

defineProps({
  totalItems: {
    type: Number,
    default: 0,
  },
  currentPage: {
    type: Number,
    default: 1,
  },
  itemsPerPage: {
    type: Number,
    default: 20,
  },
  totalPages: {
    type: Number,
    default: 1,
  },
});

const emit = defineEmits(["prev", "next", "goTo"]);
</script>

<style scoped>
.pagination-controls {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 20px;
  padding-top: 16px;
  border-top: 1px solid var(--semantic-color-border); /* deid-ignore */
}

.pagination-info {
  font-size: 0.875rem;
  color: var(--semantic-color-text-secondary);
  font-variant-numeric: tabular-nums;
}

.font-semibold {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.pagination-buttons {
  display: flex;
  align-items: center;
  gap: 8px;
}

.page-numbers {
  display: flex;
  gap: 4px;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  padding: 4px 10px;
  font-size: 0.8rem;
  font-weight: 500;
  border-radius: 6px;
  cursor: pointer;
}

.btn-outline-secondary {
  background-color: transparent;
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-outline-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-muted);
}

.page-num-btn {
  min-width: 32px;
  padding: 4px 8px;
  background-color: transparent;
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
  font-variant-numeric: tabular-nums;
}

.page-num-btn:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-header); /* deid-ignore */
  border-color: var(--semantic-color-border-control);
}

.page-num-btn.active {
  background-color: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
}

.prev-btn:disabled,
.next-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
