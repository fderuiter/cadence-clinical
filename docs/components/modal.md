# ClinicalModal & ClinicalDrawer

The `ClinicalModal` and `ClinicalDrawer` primitives render accessible modal overlays with shared reactive focus trapping (`useFocusTrap`), LIFO `Escape` key dismissal, background sibling `inert` and `aria-hidden="true"` isolation (preserving `[data-focus-trap-exempt="true"]` live alert regions such as `ToastHost`), trigger focus restoration, `prefers-reduced-motion` support via `ui/tokens.css`, and ARIA attributes for Part 11 electronic signature prompts, form review dialogs, confirmation workflows, and side-over drawers.

## ClinicalModal Props

| Prop                                        | Type                  | Default                    | Description                                                                                                        |
| :------------------------------------------ | :-------------------- | :------------------------- | :----------------------------------------------------------------------------------------------------------------- |
| `modelValue` / `open` / `isOpen`            | `Boolean`             | `false`                    | Controls open/close visibility across `v-model` and prop bindings.                                                 |
| `title`                                     | `String`              | `'Clinical Action Dialog'` | Dialog header title and programmatic accessible name (`aria-labelledby`).                                          |
| `size`                                      | `String`              | `'md'`                     | Dialog width (`sm`, `md`, `lg`, `xl`).                                                                             |
| `role`                                      | `String`              | `'dialog'`                 | Dialog ARIA role (`dialog`, `alertdialog`).                                                                        |
| `dismissible`                               | `Boolean`             | `true`                     | When `false`, locks `Escape`, close button, and backdrop dismissal.                                                |
| `closeOnBackdrop`                           | `Boolean`             | `true`                     | Whether clicking the backdrop dismisses an unlocked modal.                                                         |
| `overlayId`                                 | `String`              | `undefined`                | Optional `id` attribute applied to the `.clinical-modal-backdrop` container.                                       |
| `ariaLabel` / `ariaLabelledby`              | `String`              | `undefined`                | Explicit accessible name or heading ID override when `showHeader` is `false`.                                      |
| `ariaDescribedby`                           | `String`              | `undefined`                | Optional element ID(s) describing the dialog (`aria-describedby`), e.g. for `role="alertdialog"` warning messages. |
| `showHeader` / `showClose` / `showFooter`   | `Boolean`             | `true`                     | Toggles built-in header, header close button, or footer chrome when custom slot markup is used.                    |
| `headerClass` / `bodyClass` / `footerClass` | `String`              | `''`                       | Optional custom CSS classes appended to header, body, or footer containers.                                        |
| `initialFocus`                              | `String \| Object`    | `null`                     | Optional CSS selector, ref, or getter for initial focus target on open.                                            |
| `restoreFocus`                              | `Boolean \| Function` | `true`                     | Restores focus to the triggering element (or a visible shell/main fallback if hidden or detached) on close.        |
| `teleport`                                  | `Boolean`             | `true`                     | Teleports the overlay to `document.body` when `true`.                                                              |

On viewports with `max-height: 480px` (such as `320x256` WCAG 2.1 SC 1.4.10 reflow viewports), `ClinicalModal` switches from `.modal-body`-only scrolling to full `.clinical-modal-dialog` box scrolling while `.modal-footer` wraps action buttons across lines on narrow widths.

## ClinicalDrawer Props

| Prop                                        | Type                        | Default                   | Description                                                                                                  |
| :------------------------------------------ | :-------------------------- | :------------------------ | :----------------------------------------------------------------------------------------------------------- |
| `modelValue` / `open` / `isOpen`            | `Boolean`                   | `false`                   | Controls slide-over visibility across `v-model` and prop bindings.                                           |
| `docked`                                    | `Boolean`                   | `false`                   | Renders a non-modal desktop inspector panel (`>= 1280px`) without backdrop dimming or focus trap (ADR-2663). |
| `keepMounted`                               | `Boolean`                   | `false`                   | Keeps closed drawer DOM mounted (`display: none`, `inert`, `aria-hidden="true"`).                            |
| `title` / `subtitle`                        | `String`                    | `''`                      | Built-in `#header` title and optional subtitle (hidden on `@media (max-height: 540px)`).                     |
| `ariaLabel` / `ariaLabelledby`              | `String`                    | `undefined`               | Explicit accessible name or custom `#header` heading ID override.                                            |
| `id` / `overlayId`                          | `String`                    | `undefined`               | Optional DOM IDs applied to `.clinical-drawer-panel` and `.clinical-drawer-backdrop`.                        |
| `overlayClass` / `panelClass` / `bodyClass` | `String \| Array \| Object` | `''`                      | Optional CSS classes appended to backdrop, panel, or `.clinical-drawer-body`.                                |
| `size`                                      | `String`                    | `'md'`                    | Width tier (`sm`, `md`, `lg`, `xl`) mapped to `--drawer-width-*` tokens.                                     |
| `position`                                  | `String`                    | `'right'`                 | Slide-in edge (`right`, `left`, or `bottom`).                                                                |
| `mobileBottomSheet`                         | `Boolean`                   | `false`                   | Adds mobile bottom-sheet drag handle styling on narrow viewports (ADR-2657).                                 |
| `showHeader` / `closeLabel`                 | `Boolean` / `String`        | `true` / `'Close drawer'` | Toggles header chrome or customizes the `44x44px` Lucide `X` close button `aria-label`.                      |
| `dismissible` / `closeOnBackdrop`           | `Boolean`                   | `true`                    | Locks `Escape`, close button, and backdrop dismissal during Part 11 / GxP submissions.                       |
| `teleport`                                  | `Boolean \| String`         | `true`                    | Teleports the drawer overlay to `document.body` (or inline when `false`).                                    |

`ClinicalDrawer` bounds persistent chrome to a single `#header` slot and one optional `#footer` slot (plus `#after-panel` for nested `ClinicalModal` overlays), sizes panels with `100dvh` (`100vh` fallback) and `min-height: 0` on `.clinical-drawer-body`, presents as a `90dvh` bottom sheet with `env(safe-area-inset-bottom)` at `<= 480px` width, and switches to panel-level vertical scrolling with compact `<= 52px` headers on `@media (max-height: 540px)` short viewports (ADR-2674).

## Overlay & Inline Focus Composables (`useFocusTrap`, `hasActiveModalFocusTrap`, `trapTabFocus`, `handoffFocus`, `useInlineFocusHandoff`, `handoffRetryFocus`, `useRetryFocusHandoff` & `useEscapeClose`)

- **`useFocusTrap(targetRef, options)`**: Dynamically activates focus containment whenever conditional (`v-if`) or persistent (`isOpen`) overlays open, traps `Tab` / `Shift+Tab` inside the topmost active overlay while bridging focus into interactive controls inside `[data-focus-trap-exempt="true"]` regions (such as `ClinicalToastHost`), observes subtree child unmounts on the active top modal and recovers dialog focus after `nextTick` if `document.activeElement` hits `document.body` (yielding to explicit `handoffFocus` targets and stacked child modals), marks background page siblings `inert` and `aria-hidden="true"` while keeping `[data-focus-trap-exempt="true"]` live alert hosts audible and visible, skips `Escape` events with `defaultPrevented === true` when unlocked, and restores focus to the trigger, `options.fallbackFocus`, parent trap, or a visible fallback (`[aria-controls]` controller, `#btn-open-palette`, `#btn-mobile-menu-toggle`, or `#main-content` / `<main>`) before unmounting.
- **`hasActiveModalFocusTrap(options)` / `getTopActiveModalContainer()`**: Read-only stack introspection helpers allowing global shell shortcuts (such as `Ctrl+K` / `Cmd+K` in `AppShell.vue` and `StudyCanvasView.vue`) to detect active modal/drawer traps (with optional `excludeId` / `excludeElement` filters) and avoid opening occluded overlays behind active dialogs.
- **`trapTabFocus(event, container)`**: Imperative helper (paired with `getFocusableElements(container)`) for standalone non-Vue DOM dialogs such as `#portal-sign-modal`, wrapping `Tab` / `Shift+Tab` across `container` and any focusable controls inside `[data-focus-trap-exempt="true"]` live-alert hosts while deduplicating `KeyboardEvent` instances via a `WeakSet` across `window` and `document` listeners.
- **`handoffFocus(targets, options)`**: Non-trapping inline focus handoff helper for `v-if` swaps, ephemeral buttons, and portal `.portal-view` transitions. Waits for `nextTick`, skips hidden (`display: none`, `visibility: hidden`), `inert`, `aria-hidden="true"`, disabled, or disconnected candidates, ensures programmatic heading/region targets carry `tabindex="-1"`, scrolls only when `scrollIfOffscreen` is enabled and off-screen, and falls back to `#canvas-workspace` or `#main-content`.
- **`useInlineFocusHandoff(options)`**: Composable pairing trigger memory (`rememberTrigger`, `restoreTrigger`, `handoff`) with optional reactive `isOpen` tracking for inline disclosures without trapping unmodified `Tab` or marking background regions `inert`.
- **`handoffRetryFocus(action, options)` / `useRetryFocusHandoff(options)`**: Executes sync or async retry callbacks and hands off focus to the retry button when the error state persists or to recovered interactive controls / `tabindex="-1"` headings on success without trapping `Tab` or setting `inert`.
- **`useEscapeClose(onClose, options)`**: Registers non-trapped inline popovers, navigation menus, or anchored tour steps on a shared LIFO stack so pressing `Escape` dismisses only the topmost unlocked overlay.

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
