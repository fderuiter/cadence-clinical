<template>
  <dl id="inspector-version" class="details">
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
        <code class="inspector-ref">{{ protocolVersion.content_hash }}</code>
      </details>
      <template v-else>Not sealed yet</template>
    </dd>
    <template v-if="designSummary">
      <dt>Design forms</dt>
      <dd id="inspector-version-forms">
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
        <dd id="inspector-version-notes">
          {{ designSummary.import_notes.length }} recorded
        </dd>
      </template>
    </template>
  </dl>
</template>

<script setup lang="ts">
import { ref, watch } from "vue";
import {
  kernelApi,
  type KernelProtocolDraftSummary,
  type KernelProtocolVersion,
} from "../../api/kernelApi";
import NodeRefChip from "./NodeRefChip.vue";

const props = defineProps<{
  protocolVersion: KernelProtocolVersion;
}>();

const designSummary = ref<KernelProtocolDraftSummary | null>(null);
let designSummaryReq = 0;

watch(
  () => props.protocolVersion.ref,
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
