<template>
  <ClinicalThemeToggle v-bind="$attrs" />
</template>

<script setup>
import { ClinicalThemeToggle } from "ui";

defineOptions({ inheritAttrs: false });
</script>
