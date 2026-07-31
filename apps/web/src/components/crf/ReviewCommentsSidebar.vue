<template>
  <div class="review-comments-sidebar">
    <header class="sidebar-header">
      <h3>Review Comments ({{ filteredComments.length }})</h3>
      <div v-if="fieldId" class="field-indicator">
        Selected Field: <strong>{{ fieldId }}</strong>
      </div>
    </header>

    <div class="comment-thread-list">
      <div
        v-for="c in filteredComments"
        :key="c.id"
        class="comment-card"
        :class="{ resolved: c.isResolved }"
        style="border: 1px solid #e2e8f0; padding: 12px; margin-bottom: 8px; border-radius: 6px; background-color: #f8fafc;"
      >
        <div class="comment-header" style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 6px;">
          <div style="display: flex; align-items: center; gap: 8px;">
            <div
              class="author-avatar"
              style="width: 24px; height: 24px; border-radius: 50%; background-color: #cbd5e1; display: flex; align-items: center; justify-content: center; font-size: 11px; font-weight: bold; color: #475569;"
            >
              {{ (c.authorName || 'U').substring(0, 2).toUpperCase() }}
            </div>
            <span class="comment-author" style="font-weight: 600; font-size: 13px; color: #1e293b;">
              {{ c.authorName }}
            </span>
          </div>
          <span
            class="status-badge"
            :class="c.isResolved ? 'badge-resolved' : 'badge-open'"
            style="font-size: 11px; padding: 2px 6px; border-radius: 4px; font-weight: 600;"
            :style="c.isResolved ? 'background-color: #d1fae5; color: #065f46;' : 'background-color: #fee2e2; color: #991b1b;'"
          >
            {{ c.isResolved ? 'Resolved' : 'Open' }}
          </span>
        </div>

        <p class="comment-text" style="font-size: 13px; color: #334155; margin: 4px 0 8px 0; word-break: break-all;">
          {{ c.text }}
        </p>

        <div style="display: flex; align-items: center; justify-content: space-between;">
          <span class="comment-timestamp" style="font-size: 11px; color: #64748b;">
            {{ formatTimestamp(c.createdAt) }}
          </span>
          <button
            v-if="!c.isResolved"
            class="btn-sm"
            style="font-size: 11px; padding: 2px 8px; border-radius: 4px; cursor: pointer; border: 1px solid #cbd5e1; background: white;"
            @click="resolveComment(c.id)"
          >
            Resolve
          </button>
        </div>
      </div>

      <div v-if="filteredComments.length === 0" class="empty-state" style="padding: 16px; text-align: center; color: #64748b; font-size: 13px;">
        No comments yet for this field. Use the box below to start a thread.
      </div>
    </div>

    <div class="add-comment-box" style="margin-top: 16px; border-top: 1px solid #e2e8f0; padding-top: 12px;">
      <textarea
        v-model="newComment"
        placeholder="Add review comment... Use @user to mention reviewers."
        style="width: 100%; min-height: 80px; padding: 8px; border-radius: 4px; border: 1px solid #cbd5e1; font-size: 13px; margin-bottom: 8px; resize: vertical;"
      ></textarea>
      <button
        class="btn-primary"
        style="padding: 6px 12px; font-size: 13px; border-radius: 4px; background-color: #2563eb; color: white; border: none; cursor: pointer; font-weight: 500;"
        @click="submitComment"
      >
        Post Comment
      </button>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from 'vue';

const props = defineProps({
  comments: {
    type: Array,
    required: true,
  },
  fieldId: {
    type: String,
    required: true,
  },
});

const emit = defineEmits(['post-comment', 'resolve']);
const newComment = ref('');

// Filter comments to only display those anchored to the selected eCRF field
const filteredComments = computed(() => {
  return props.comments.filter(c => c.field_id === props.fieldId);
});

const submitComment = () => {
  if (newComment.value.trim()) {
    emit('post-comment', { fieldId: props.fieldId, text: newComment.value });
    newComment.value = '';
  }
};

const resolveComment = (commentId) => {
  emit('resolve', commentId);
};

const formatTimestamp = (ts) => {
  if (!ts) return '';
  try {
    const d = new Date(ts);
    return d.toLocaleString();
  } catch {
    return ts;
  }
};
</script>
