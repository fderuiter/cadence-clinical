# PersonaSwitcher

The `PersonaSwitcher` control (`#persona-select` in `AppShell.vue`) enables multi-persona orientation and evaluation across clinical operations workflows (`super_admin`, `sponsor_designer`, `data_manager`, `site_crc`, `site_investigator`, `cra_monitor`, `safety_officer`, `auditor`).

## Authorization and Build Isolation

- **Static demo builds (`isDemoBuild() === true`)**: All eight presets remain selectable in `availablePersonas` during an offline demo session while `authStore.startDemoSession()` and `authStore.switchPersona()` scope `rawRoles`, `normalizedRoles`, `siteIds`, and synthetic `user` identity (`id`, `username`, `siteId`) to the active preset. Outbound requests attach `X-Demo-Persona`, `X-User-Roles`, `X-User-Id`, and `X-Site-Id` headers so the MSW policy layer enforces 21 CFR Part 11 role boundaries, `K-AUTH-4` treatment arm blinding (`BLINDED` / `mask: "Blinded"`), and `PX-20` site visibility (`mask: "NotPermitted"`).
- **Production OIDC builds (`isDemoBuild() === false`)**: `availablePersonas` lists only presets entitled by the signed-in user's Keycloak `realm_access.roles`. Calling `switchPersona()` updates workspace orientation and contextual help without mutating `rawRoles` or `normalizedRoles`, and gateway clients never emit synthetic `X-Demo-*` or `X-User-Roles` headers (ADR-2493, ADR-2546).
- **Floating Menu Containment (`ClinicalPopover`)**: Standalone `PersonaSwitcher` menus anchor via `ClinicalPopover` (`align="end"`, `12px` visual-viewport clamping, `overflow-y: auto` vertical scrolling, vector `ClinicalIcon` chevrons, outside-pointer dismissal, and `Escape` dismissal with trigger focus restoration).

## Usage Example

```vue
<script setup>
import { ref } from "vue";
import { PersonaSwitcher } from "ui";

const currentPersona = ref("super_admin");

const onPersonaChange = (persona) => {
  console.log("Switched to role:", persona.role);
};
</script>

<template>
  <header class="top-nav">
    <div class="logo">Cadence Clinical Platform</div>
    <PersonaSwitcher v-model="currentPersona" @change="onPersonaChange" />
  </header>
</template>
```
