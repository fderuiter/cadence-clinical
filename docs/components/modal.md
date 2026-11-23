# ClinicalModal & ClinicalDrawer

The `ClinicalModal` and `ClinicalDrawer` primitives render accessible modal
overlays with shared reactive focus trapping (`useFocusTrap`), LIFO `Escape` key
dismissal, background sibling `inert` and `aria-hidden="true"` isolation
(preserving `[data-focus-trap-exempt="true"]` live alert regions such as
`ToastHost`), trigger focus restoration, `prefers-reduced-motion` support via
`ui/tokens.css`, and ARIA attributes for Part 11 electronic signature prompts,
form review dialogs, confirmation workflows, and side-over drawers.

## ClinicalModal Props

| Prop                                        | Type                  | Default                    | Description                                                                                       |
| :------------------------------------------ | :-------------------- | :------------------------- | :------------------------------------------------------------------------------------------------ |
| `modelValue` / `open` / `isOpen`            | `Boolean`             | `false`                    | Controls open/close visibility across `v-model` and prop bindings.                                |
| `title`                                     | `String`              | `'Clinical Action Dialog'` | Dialog header title and programmatic accessible name (`aria-labelledby`).                         |
| `size`                                      | `String`              | `'md'`                     | Dialog width (`sm`, `md`, `lg`, `xl`).                                                            |
| `role`                                      | `String`              | `'dialog'`                 | Dialog ARIA role (`dialog`, `alertdialog`).                                                       |
| `dismissible`                               | `Boolean`             | `true`                     | When `false`, locks `Escape`, close button, and backdrop dismissal.                               |
| `closeOnBackdrop`                           | `Boolean`             | `true`                     | Whether clicking the backdrop dismisses an unlocked modal.                                        |
| `overlayId`                                 | `String`              | `undefined`                | Optional `id` attribute applied to the `.clinical-modal-backdrop` container.                      |
| `ariaLabel` / `ariaLabelledby`              | `String`              | `undefined`                | Explicit accessible name or heading ID override when `showHeader` is `false`.                     |
| `ariaDescribedby`                           | `String`              | `undefined`                | Element ID(s) describing the dialog (`aria-describedby`), e.g. for `role="alertdialog"` warnings. |
| `showHeader` / `showClose` / `showFooter`   | `Boolean`             | `true`                     | Toggles built-in header, close button, or footer chrome when custom slot markup is used.          |
| `headerClass` / `bodyClass` / `footerClass` | `String`              | `''`                       | Optional custom CSS classes appended to header, body, or footer containers.                       |
| `initialFocus`                              | `String \| Object`    | `null`                     | Optional CSS selector, ref, or getter for initial focus target on open.                           |
| `restoreFocus`                              | `Boolean \| Function` | `true`                     | Restores focus to the trigger (or visible shell/main fallback if hidden or detached) on close.    |
| `teleport`                                  | `Boolean`             | `true`                     | Teleports the overlay to `document.body` when `true`.                                             |

On viewports with `max-height: 480px` (such as `320x256` WCAG 2.1 SC 1.4.10
reflow viewports), `ClinicalModal` switches from `.modal-body`-only scrolling to
full `.clinical-modal-dialog` box scrolling while `.modal-footer` wraps action
buttons across lines on narrow widths.

## ClinicalDrawer Props

| Prop              | Type      | Default   | Description                                                              |
| :---------------- | :-------- | :-------- | :----------------------------------------------------------------------- |
| `open` / `isOpen` | `Boolean` | `false`   | Controls slide-over visibility (`modelValue` also supported).            |
| `title`           | `String`  | `''`      | Drawer header title bound via `aria-labelledby`.                         |
| `size`            | `String`  | `'md'`    | Width tier (`sm`, `md`, `lg`, `xl`) mapped to `--drawer-width-*` tokens. |
| `position`        | `String`  | `'right'` | Slide-in edge (`right` or `left`).                                       |
| `dismissible`     | `Boolean` | `true`    | Locks `Escape`, close button, and backdrop dismissal when `false`.       |
| `teleport`        | `Boolean` | `true`    | Teleports the drawer overlay to `document.body` when `true`.             |

## Overlay & Inline Focus Composables

- **`useFocusTrap(targetRef, options)`**: Dynamically activates focus
  containment whenever conditional (`v-if`) or persistent (`isOpen`) overlays
  open, traps `Tab` / `Shift+Tab` inside the topmost active overlay while
  bridging focus into interactive controls inside
  `[data-focus-trap-exempt="true"]` regions (such as `ClinicalToastHost`), marks
  background page siblings `inert` and `aria-hidden="true"` while keeping
  `[data-focus-trap-exempt="true"]` live alert hosts audible and visible, skips
  `Escape` events with `defaultPrevented === true` when unlocked, and restores
  focus to the trigger or a visible fallback (`[aria-controls]` controller,
  `#btn-open-palette`, `#btn-mobile-menu-toggle`, or `#main-content` / `<main>`)
  before unmounting.
- **`hasActiveModalFocusTrap(options)` / `getTopActiveModalContainer()`**:
  Read-only stack introspection helpers allowing global shell shortcuts (such as
  `Ctrl+K` / `Cmd+K` in `AppShell.vue` and `StudyCanvasView.vue`) to detect
  active modal/drawer traps (with optional `excludeId` / `excludeElement`
  filters) and avoid opening occluded overlays behind active dialogs.
- **`trapTabFocus(event, container)`**: Imperative helper (paired with
  `getFocusableElements(container)`) for standalone non-Vue DOM dialogs such as
  `#portal-sign-modal`, wrapping `Tab` / `Shift+Tab` across `container` and any
  focusable controls inside `[data-focus-trap-exempt="true"]` live-alert hosts
  while deduplicating `KeyboardEvent` instances via a `WeakSet` across `window`
  and `document` listeners.
- **`handoffFocus(targets, options)`**: Non-trapping inline focus handoff helper
  for `v-if` swaps, ephemeral buttons, and portal `.portal-view` transitions.
  Waits for `nextTick`, skips hidden (`display: none`, `visibility: hidden`),
  `inert`, `aria-hidden="true"`, disabled, or disconnected candidates, ensures
  programmatic heading/region targets carry `tabindex="-1"`, scrolls only when
  `scrollIfOffscreen` is enabled and off-screen, and falls back to
  `#canvas-workspace` or `#main-content`.
- **`useInlineFocusHandoff(options)`**: Composable pairing trigger memory
  (`rememberTrigger`, `restoreTrigger`, `handoff`) with optional reactive
  `isOpen` tracking for inline disclosures without trapping unmodified `Tab` or
  marking background regions `inert`.
- **`useEscapeClose(onClose, options)`**: Registers non-trapped inline popovers,
  navigation menus, or anchored tour steps on a shared LIFO stack so pressing
  `Escape` dismisses only the topmost unlocked overlay.

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
