<template>
  <ClinicalToastHost
    :toasts="toastStore.toasts"
    aria-label="Notifications"
    :manage-timers="false"
    @action="handleAction"
    @dismiss="toastStore.dismiss"
    @pause="toastStore.pause"
    @resume="toastStore.resume"
  />
</template>

<script setup>
import { getCurrentInstance } from "vue";
import { ClinicalToastHost } from "ui";
import { useToastStore } from "../stores/toasts.js";

const toastStore = useToastStore();
const instance = getCurrentInstance();

function handleAction(toast) {
  const target = toast?.action?.to;
  toastStore.dismiss(toast.id);
  if (!target) return;
  const router =
    instance?.proxy?.$router ??
    instance?.appContext?.config?.globalProperties?.$router;
  if (router && typeof router.push === "function") {
    router.push(target);
  } else if (typeof window !== "undefined") {
    window.location.assign(target);
  }
}
</script>
