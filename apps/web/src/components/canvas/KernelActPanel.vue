<template>
  <section id="kernel-act" class="act" aria-labelledby="kernel-act-title">
    <h3 id="kernel-act-title" tabindex="-1">Actions</h3>
    <p v-if="loading" id="kernel-act-status" class="act-note" role="status">
      Checking available actions…
    </p>
    <CanvasErrorAlert
      v-else-if="error"
      id="kernel-act-status"
      message="Available actions couldn't be loaded right now."
      :detail="error"
      @retry="load(props.nodeRef)"
      @explain="emit('openExplain')"
    />
    <p
      v-else-if="!commands.length"
      id="kernel-act-status"
      class="act-note"
      role="status"
    >
      No actions are available for this record.
    </p>
    <template v-else>
      <ul id="kernel-act-list" class="act-list">
        <li
          v-for="command in commands"
          :key="command.command"
          class="act-item"
          :data-command="command.command"
        >
          <div class="act-line">
            <span class="act-command">{{
              formatCommandTitle(command.command)
            }}</span>
            <span
              class="act-decision"
              :class="command.decision.permitted ? 'permitted' : 'denied'"
            >
              {{ command.decision.permitted ? "Permitted" : "Not permitted" }}
            </span>
          </div>
          <p v-if="command.decision.reason" class="act-reason">
            {{ command.decision.reason }}
          </p>
          <button
            type="button"
            class="btn btn-secondary act-explain"
            @click="emit('explain', command)"
          >
            Explain
          </button>
        </li>
      </ul>
      <p id="kernel-act-footnote" class="act-note">
        Preview of whether each action is currently permitted for your role.
        Actions are completed on their respective study screens.
      </p>
    </template>
  </section>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import { kernelApi, type KernelCommand } from "../../api/kernelApi";
import { formatCommandTitle } from "../../canvas/nodeResolver";
import CanvasErrorAlert from "./CanvasErrorAlert.vue";

/**
 * The Act verb (docs/ux/CLINICAL_CANVAS.md §2.4, ADR-2483): the kernel commands offered on the selected object,
 * each with the reader's dry-run decision and the kernel's one-line reason. It sends nothing. A refusal or an
 * unreachable kernel is reported as it is, never replaced with a guess.
 */
const props = defineProps<{ nodeRef: string }>();
const emit = defineEmits<{
  explain: [command: KernelCommand];
  openExplain: [];
}>();

const commands = ref<KernelCommand[]>([]);
const loading = ref(false);
const error = ref("");

let latest = 0;

async function load(nodeRef: string) {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  commands.value = [];
  try {
    const result = await kernelApi.getCommands(nodeRef);
    if (request === latest) commands.value = result;
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
}

watch(() => props.nodeRef, load, { immediate: true });
</script>

<style scoped>
.act {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.act h3 {
  margin: 0;
  font-size: 0.95rem;
}

.act-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin: 0;
  padding: 0;
  list-style: none;
}

.act-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
  padding: 8px;
  border: 1px solid var(--border);
  border-radius: 6px;
}

.act-line {
  display: flex;
  justify-content: space-between;
  gap: 8px;
}

.act-command {
  font-weight: 600;
  overflow-wrap: anywhere;
}

.act-decision.denied {
  color: var(--neutral-dark);
}

.act-reason,
.act-note {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.act-explain {
  align-self: flex-start;
}
</style>
