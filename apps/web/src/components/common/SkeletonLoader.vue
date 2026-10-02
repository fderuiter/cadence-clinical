<template>
  <div
    class="skeleton-loader-container"
    :class="[`type-${type}`, { animated }]"
    role="status"
    aria-busy="true"
    aria-live="polite"
    aria-label="Loading content..."
  >
    <!-- Table Row Mode -->
    <template v-if="type === 'row'">
      <div v-for="r in count" :key="`row-${r}`" class="skeleton-row">
        <div
          v-for="c in columns"
          :key="`col-${c}`"
          class="skeleton-cell"
          :style="getCellStyle(r, c)"
        />
      </div>
    </template>

    <!-- Card Mode -->
    <template v-else-if="type === 'card'">
      <div v-for="c in count" :key="`card-${c}`" class="skeleton-card">
        <div class="skeleton-card-header">
          <div class="skeleton-line skeleton-title" style="width: 40%" />
          <div class="skeleton-line skeleton-badge" style="width: 20%" />
        </div>
        <div class="skeleton-card-body">
          <div class="skeleton-line" style="width: 85%" />
          <div class="skeleton-line" style="width: 65%" />
        </div>
        <div class="skeleton-card-footer">
          <div class="skeleton-line" style="width: 30%" />
        </div>
      </div>
    </template>

    <!-- Text Mode -->
    <template v-else-if="type === 'text'">
      <div v-for="t in count" :key="`text-${t}`" class="skeleton-text-group">
        <div
          class="skeleton-line"
          :style="{ width: `${100 - (t % 3) * 15}%`, height: height || '16px' }"
        />
      </div>
    </template>

    <!-- Timeline Mode -->
    <template v-else-if="type === 'timeline'">
      <div
        v-for="tm in count"
        :key="`timeline-${tm}`"
        class="skeleton-timeline-item"
      >
        <div class="skeleton-bullet" />
        <div class="skeleton-timeline-card">
          <div class="skeleton-line" style="width: 50%" />
          <div class="skeleton-line" style="width: 80%" />
        </div>
      </div>
    </template>
  </div>
</template>

<script setup>
defineProps({
  type: {
    type: String,
    default: "row", // 'row' | 'card' | 'text' | 'timeline'
  },
  count: {
    type: Number,
    default: 3,
  },
  columns: {
    type: Number,
    default: 5,
  },
  height: {
    type: String,
    default: "",
  },
  animated: {
    type: Boolean,
    default: true,
  },
});

function getCellStyle(rowIndex, colIndex) {
  const baseWidths = [35, 20, 10, 15, 20, 15, 10];
  const width = baseWidths[(colIndex - 1) % baseWidths.length] || 15;
  return {
    flex: `1 1 ${width}%`,
    width: `${width}%`,
  };
}
</script>

<style scoped>
.skeleton-loader-container {
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 100%;
  padding: 8px 0;
}

.skeleton-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: #f8fafc;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
}

.skeleton-cell {
  height: 16px;
  background: #e2e8f0;
  border-radius: 4px;
}

.skeleton-card {
  display: flex;
  flex-direction: column;
  gap: 12px;
  padding: 16px;
  background: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.skeleton-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.skeleton-card-body {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.skeleton-card-footer {
  display: flex;
  justify-content: flex-end;
}

.skeleton-line {
  height: 14px;
  background: #e2e8f0;
  border-radius: 4px;
}

.skeleton-title {
  height: 18px;
}

.skeleton-badge {
  height: 14px;
  border-radius: 12px;
}

.skeleton-text-group {
  margin-bottom: 4px;
}

.skeleton-timeline-item {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  position: relative;
  padding-left: 8px;
}

.skeleton-bullet {
  width: 12px;
  height: 12px;
  border-radius: 50%;
  background: #cbd5e1;
  margin-top: 4px;
  flex-shrink: 0;
}

.skeleton-timeline-card {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 12px;
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
}

.animated .skeleton-cell,
.animated .skeleton-line,
.animated .skeleton-bullet {
  animation: skeleton-pulse 1.5s ease-in-out infinite;
}

@keyframes skeleton-pulse {
  0% {
    opacity: 0.5;
    background-color: #e2e8f0;
  }
  50% {
    opacity: 1;
    background-color: #cbd5e1;
  }
  100% {
    opacity: 0.5;
    background-color: #e2e8f0;
  }
}
</style>
