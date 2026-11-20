# ClinicalModal & ClinicalDrawer

The `ClinicalModal` and `ClinicalDrawer` primitives render accessible modal overlays with shared reactive focus trapping (`useFocusTrap`), LIFO `Escape` key dismissal, background sibling `inert` and `aria-hidden="true"` isolation (preserving `[data-focus-trap-exempt="true"]` live alert regions such as `ToastHost`), trigger focus restoration, `prefers-reduced-motion` support via `ui/tokens.css`, and ARIA attributes for Part 11 electronic signature prompts, form review dialogs, confirmation workflows, and side-over drawers.

## ClinicalModal Props

| Prop              | Type                  | Default                    | Description                                                               |
| :---------------- | :-------------------- | :------------------------- | :------------------------------------------------------------------------ |
| `modelValue`      | `Boolean`             | `false`                    | Controls open/close visibility (`open` alias also supported).             |
| `title`           | `String`              | `'Clinical Action Dialog'` | Dialog header title and programmatic accessible name (`aria-labelledby`). |
| `size`            | `String`              | `'md'`                     | Dialog width (`sm`, `md`, `lg`, `xl`).                                    |
| `role`            | `String`              | `'dialog'`                 | Dialog ARIA role (`dialog`, `alertdialog`).                               |
| `dismissible`     | `Boolean`             | `true`                     | When `false`, locks `Escape`, close button, and backdrop dismissal.       |
| `closeOnBackdrop` | `Boolean`             | `true`                     | Whether clicking the backdrop dismisses an unlocked modal.                |
| `initialFocus`    | `String \| Object`    | `null`                     | Optional CSS selector, ref, or getter for initial focus target on open.   |
| `restoreFocus`    | `Boolean \| Function` | `true`                     | Restores focus to the triggering element on close.                        |
| `teleport`        | `Boolean`             | `true`                     | Teleports the overlay to `document.body` when `true`.                     |

## ClinicalDrawer Props

| Prop              | Type      | Default   | Description                                                              |
| :---------------- | :-------- | :-------- | :----------------------------------------------------------------------- |
| `open` / `isOpen` | `Boolean` | `false`   | Controls slide-over visibility (`modelValue` also supported).            |
| `title`           | `String`  | `''`      | Drawer header title bound via `aria-labelledby`.                         |
| `size`            | `String`  | `'md'`    | Width tier (`sm`, `md`, `lg`, `xl`) mapped to `--drawer-width-*` tokens. |
| `position`        | `String`  | `'right'` | Slide-in edge (`right` or `left`).                                       |
| `dismissible`     | `Boolean` | `true`    | Locks `Escape`, close button, and backdrop dismissal when `false`.       |
| `teleport`        | `Boolean` | `true`    | Teleports the drawer overlay to `document.body` when `true`.             |

## Overlay Composables (`useFocusTrap`, `trapTabFocus` & `useEscapeClose`)

- **`useFocusTrap(targetRef, options)`**: Dynamically activates focus containment whenever conditional (`v-if`) or persistent (`isOpen`) overlays open, traps `Tab` / `Shift+Tab` inside the topmost active overlay, marks background page siblings `inert` and `aria-hidden="true"` while keeping `[data-focus-trap-exempt="true"]` live alert hosts audible and visible, skips `Escape` events with `defaultPrevented === true` when unlocked, and restores focus before unmounting.
- **`trapTabFocus(event, container)`**: Imperative helper (paired with `getFocusableElements(container)`) for standalone non-Vue DOM dialogs such as `#portal-sign-modal`, wrapping `Tab` / `Shift+Tab` within `container` while deduplicating `KeyboardEvent` instances via a `WeakSet` across `window` and `document` listeners.
- **`useEscapeClose(onClose, options)`**: Registers non-trapped inline popovers or anchored tour steps on a shared LIFO stack so pressing `Escape` dismisses only the topmost unlocked overlay.

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
