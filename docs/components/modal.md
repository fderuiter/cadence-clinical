# ClinicalModal

The `ClinicalModal` component renders accessible dialogs with shared reactive focus trapping (`useFocusTrap`), `Escape` key dismissal, focus restoration to the triggering control, backdrop blur, `prefers-reduced-motion` support via `ui/tokens.css`, and ARIA attributes for Part 11 electronic signature prompts, form review dialogs, and confirmation workflows.

## Props

| Prop         | Type      | Default                    | Description                                 |
| :----------- | :-------- | :------------------------- | :------------------------------------------ |
| `modelValue` | `Boolean` | `false`                    | Controls open/close visibility.             |
| `title`      | `String`  | `'Clinical Action Dialog'` | Dialog header title and `aria-label`.       |
| `size`       | `String`  | `'md'`                     | Dialog width (`sm`, `md`, `lg`, `xl`).      |
| `role`       | `String`  | `'dialog'`                 | Dialog ARIA role (`dialog`, `alertdialog`). |

## Usage Example

```vue
<script setup>
import { ref } from "vue";
import { ClinicalModal } from "ui";

const showSignatureModal = ref(false);

const handleConfirm = () => {
  console.log("Confirmed signature");
  showSignatureModal.value = false;
};
</script>

<template>
  <button @click="showSignatureModal = true">Sign eCRF Submission</button>

  <ClinicalModal
    v-model="showSignatureModal"
    title="Electronic Signature Attestation (21 CFR Part 11)"
    size="md"
    @confirm="handleConfirm"
  >
    <p>
      By signing, I confirm that the recorded clinical data is accurate and
      complete.
    </p>
  </ClinicalModal>
</template>
```
