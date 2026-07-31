<template>
  <div class="trivial-component">
    <p>{{ message }}</p>
  </div>
</template>

<script setup>
import { ref } from "vue";
const message = ref("Hello Cadence!");
</script>
