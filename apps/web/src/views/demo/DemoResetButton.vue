<template>
  <div v-if="isDemoBuild() && variant === 'login'" class="demo-reset-login">
    <button
      id="btn-reset-demo"
      type="button"
      class="btn btn-secondary demo-reset-login-btn"
      @click="handleReset"
    >
      Reset Demo Sandbox
    </button>
    <p v-if="resetMessage" role="status" class="demo-reset-status">
      {{ resetMessage }}
    </p>
  </div>
  <button
    v-else-if="isDemoBuild()"
    id="btn-reset-demo"
    type="button"
    class="btn btn-sm btn-secondary demo-reset-shell-btn"
    title="Reset offline demo data, notifications, documents, and local drafts"
    @click="handleReset"
  >
    Reset<span class="demo-reset-shell-suffix"> Demo Sandbox</span>
  </button>
</template>

<script setup>
import { ref } from "vue";
import { useAuthStore } from "../../stores/auth";
import { useOnboardingStore } from "../../stores/onboarding";
import { isDemoBuild } from "../../utils/demoMode.js";

const props = defineProps({
  variant: {
    type: String,
    default: "shell",
  },
});

const emit = defineEmits(["reset"]);

const authStore = useAuthStore();
const onboardingStore = useOnboardingStore();
const resetMessage = ref("");

async function handleReset() {
  if (!isDemoBuild()) {
    return;
  }
  const { resetDemoSandbox } = await import("../../utils/demoReset.js");
  await resetDemoSandbox({ pinia: authStore._p });
  if (authStore.isAuthenticated && authStore.currentPersona) {
    authStore.switchPersona(authStore.currentPersona);
    onboardingStore.loadPersonaState(authStore.currentPersona);
  }
  if (props.variant === "login") {
    resetMessage.value = "Demo sandbox reset to initial sample data.";
  }
  emit("reset", "Demo sandbox reset to initial baseline state.");
}
</script>

<style scoped>
.demo-reset-login {
  margin-top: 18px;
  padding-top: 16px;
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
}

.demo-reset-login-btn {
  padding: 8px 16px;
  font-size: var(--font-size-sm);
  font-weight: 600;
  cursor: pointer;
  border-radius: 4px;
}

.demo-reset-status {
  margin: 0;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-success);
  font-weight: 600;
}

.demo-reset-shell-btn {
  position: relative;
}

@media (min-width: 768px) and (max-width: 1400px) {
  .demo-reset-shell-suffix {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }
}
</style>
