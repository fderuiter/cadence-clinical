<template>
  <div
    id="section-forbidden"
    class="dashboard-section active"
    style="
      max-width: 600px;
      margin: 40px auto;
      padding: 0 12px;
      box-sizing: border-box;
      text-align: center;
    "
  >
    <div
      class="card"
      style="
        border-top: 4px solid var(--error);
        padding: clamp(16px, 5vw, 30px);
      "
    >
      <div style="color: var(--error); margin-bottom: 16px" aria-hidden="true">
        <ClinicalIcon name="shield-alert" :size="20" />
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
          overflow-wrap: anywhere;
        "
      >
        <div style="margin-bottom: 6px; overflow-wrap: anywhere">
          <strong>Active User:</strong> {{ authStore.identity?.username }}
        </div>
        <div style="overflow-wrap: anywhere">
          <strong>Your Roles:</strong>
          {{ authStore.normalizedRoles.join(", ") || "None" }}
        </div>
      </div>

      <div
        style="
          display: flex;
          flex-wrap: wrap;
          gap: 12px;
          justify-content: center;
        "
      >
        <button
          class="btn btn-primary"
          style="padding: 10px 20px; max-width: 100%; white-space: normal"
          @click="goHome"
        >
          Return to Dashboard
        </button>
        <button
          class="btn btn-secondary"
          style="
            padding: 10px 20px;
            max-width: 100%;
            white-space: normal;
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
import { ClinicalIcon } from "ui";
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
