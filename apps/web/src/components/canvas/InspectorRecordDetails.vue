<template>
  <dl v-if="protocolVersion" id="inspector-version" class="details">
    <dt>Status</dt>
    <dd id="inspector-version-status">
      {{ protocolVersion.status }}
    </dd>
    <dt>Amends</dt>
    <dd>
      <NodeRefChip
        v-if="protocolVersion.parent_version"
        :node-ref="protocolVersion.parent_version"
      />
      <template v-else>Nothing: this is the study's first version</template>
    </dd>
    <dt>Amendment intent</dt>
    <dd>{{ protocolVersion.amendment_intent || "None recorded" }}</dd>
    <template v-if="protocolVersion.approval_attestation">
      <dt>Approved by</dt>
      <dd>
        <NodeRefChip :node-ref="protocolVersion.approval_attestation" />
      </dd>
    </template>
    <template v-if="protocolVersion.withdrawal_reason">
      <dt>Withdrawn because</dt>
      <dd>{{ protocolVersion.withdrawal_reason }}</dd>
    </template>
    <dt>Verification</dt>
    <dd>
      <details
        v-if="protocolVersion.content_hash"
        class="inspector-ref-details"
      >
        <summary class="inspector-ref-summary">Content digest</summary>
        <code class="inspector-ref-code">{{
          protocolVersion.content_hash
        }}</code>
      </details>
      <template v-else>Not sealed yet</template>
    </dd>
    <template v-if="designSummary">
      <dt>Design forms</dt>
      <dd id="inspector-version-forms" class="numeric">
        {{ designSummary.form_count }}
        {{ designSummary.form_count === 1 ? "form" : "forms" }} ({{
          designSummary.item_count
        }}
        {{ designSummary.item_count === 1 ? "item" : "items" }})
      </dd>
      <template
        v-if="
          designSummary.import_notes && designSummary.import_notes.length > 0
        "
      >
        <dt>Import notes</dt>
        <dd id="inspector-version-notes" class="numeric">
          {{ designSummary.import_notes.length }} recorded
        </dd>
      </template>
    </template>
  </dl>

  <template v-else-if="genericDisplay">
    <p
      v-if="genericNodeLoading"
      id="inspector-generic-loading"
      class="inspector-empty"
      role="status"
    >
      Loading record details…
    </p>
    <p
      v-else-if="genericDisplay.masked"
      id="inspector-generic-masked"
      class="inspector-empty"
    >
      Details for this {{ genericDisplay.kindLabel.toLowerCase() }} are hidden
      ({{ genericDisplay.maskReason }}).
    </p>
    <dl v-else id="inspector-generic" class="details">
      <dt>Record type</dt>
      <dd id="inspector-generic-kind">{{ genericDisplay.kindLabel }}</dd>
      <template v-if="genericDisplay.code">
        <dt>Code</dt>
        <dd id="inspector-generic-code" class="inspector-mono">
          {{ genericDisplay.code }}
        </dd>
      </template>
      <template
        v-if="
          genericDisplay.title && genericDisplay.title !== genericDisplay.code
        "
      >
        <dt>Title</dt>
        <dd id="inspector-generic-title">{{ genericDisplay.title }}</dd>
      </template>
      <template v-if="genericDisplay.status">
        <dt>Status</dt>
        <dd id="inspector-generic-status">{{ genericDisplay.status }}</dd>
      </template>
      <dt>Study</dt>
      <dd class="inspector-mono">{{ studyCode }}</dd>
    </dl>
  </template>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import {
  kernelApi,
  type KernelProtocolDraftSummary,
  type KernelProtocolVersion,
} from "../../api/kernelApi";
import type { getNodeDisplay } from "../../canvas/nodeResolver";
import NodeRefChip from "./NodeRefChip.vue";

const props = defineProps<{
  studyCode: string;
  protocolVersion?: KernelProtocolVersion | null;
  genericDisplay?: ReturnType<typeof getNodeDisplay> | null;
  genericNodeLoading?: boolean;
}>();

const designSummary = ref<KernelProtocolDraftSummary | null>(null);
let designSummaryReq = 0;

watch(
  () => props.protocolVersion?.ref,
  async (versionRef) => {
    const req = ++designSummaryReq;
    designSummary.value = null;
    if (
      !versionRef ||
      typeof kernelApi.getProtocolDesignSummary !== "function"
    ) {
      return;
    }
    try {
      const summary = await kernelApi.getProtocolDesignSummary(versionRef);
      if (req === designSummaryReq) designSummary.value = summary;
    } catch {
      // Optional summary when protocol content exists.
    }
  },
  { immediate: true }
);
</script>

<style scoped>
.inspector-mono {
  font-family: var(--semantic-font-mono);
}

.numeric,
#inspector-version-forms,
#inspector-version-notes {
  font-variant-numeric: tabular-nums;
}

.inspector-empty {
  margin: 0;
  color: var(--neutral-dark);
}

.inspector-ref-code {
  font-family: var(--semantic-font-mono);
  font-size: 0.75rem;
  color: var(--neutral-dark);
  overflow-wrap: anywhere;
}
</style>
