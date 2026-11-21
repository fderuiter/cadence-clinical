<template>
  <div
    id="section-forbidden"
    class="dashboard-section active"
    style="max-width: 600px; margin: 40px auto; text-align: center"
  >
    <div class="card" style="border-top: 4px solid var(--error); padding: 30px">
      <div style="font-size: 4rem; color: var(--error); margin-bottom: 16px">
        🛑
      </div>
      <h2 style="color: var(--semantic-color-text-dark); margin-bottom: 8px">
        403 - Access Denied
      </h2>
      <p
        style="
          color: var(--semantic-color-text-secondary);
          font-size: 1.1rem;
          margin-bottom: 24px;
        "
      >
        Your security credentials do not authorize access to this specific GxP
        module.
      </p>

      <div
        style="
          background-color: var(--semantic-color-surface-muted);
          border: 1px solid var(--semantic-color-border);
          padding: 16px;
          border-radius: 8px;
          margin-bottom: 24px;
          text-align: left;
          font-size: 0.9rem;
          color: var(--semantic-color-text-secondary);
        "
      >
        <div style="margin-bottom: 6px">
          <strong>Active User:</strong> {{ authStore.identity?.username }}
        </div>
        <div>
          <strong>Your Roles:</strong>
          {{ authStore.normalizedRoles.join(", ") || "None" }}
        </div>
      </div>

      <div style="display: flex; gap: 12px; justify-content: center">
        <button
          class="btn btn-primary"
          style="padding: 10px 20px"
          @click="goHome"
        >
          Return to Dashboard
        </button>
        <button
          class="btn btn-secondary"
          style="
            padding: 10px 20px;
            background-color: var(--semantic-color-surface-header);
            border: 1px solid var(--semantic-color-border-control);
            color: var(--semantic-color-text-body);
          "
          @click="logout"
        >
          Sign Out / Change User
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { useAuthStore } from "../stores/auth";
import { useRouter } from "vue-router";

const authStore = useAuthStore();
const router = useRouter();

function goHome() {
  router.push("/");
}

async function logout() {
  await authStore.logout();
  router.push("/login");
}
</script>
