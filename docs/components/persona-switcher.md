# PersonaSwitcher

The `PersonaSwitcher` component enables multi-persona simulation across clinical operations workflows. Users and automated test harnesses can dynamically toggle between clinical roles (`super_admin`, `sponsor_designer`, `site_crc`, `cra_monitor`, `data_manager`, `auditor`).

## Usage Example

```vue
<script setup>
import { ref } from "vue";
import { PersonaSwitcher } from "@cadence/ui";

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
