<template>
  <div
    id="section-forbidden"
    class="dashboard-section active"
    style="max-width: 600px; margin: 40px auto; text-align: center"
  >
    <div
      class="card"
      style="border-top: 4px solid var(--error); padding: 30px"
    >
      <div style="font-size: 4rem; color: var(--error); margin-bottom: 16px">
        🛑
      </div>
      <h2 style="color: #1e293b; margin-bottom: 8px">
        403 - Access Denied
      </h2>
      <p style="color: #64748b; font-size: 1.1rem; margin-bottom: 24px">
        Your security credentials do not authorize access to this specific GxP
        module.
      </p>

      <div
        style="
          background-color: #f8fafc;
          border: 1px solid #e2e8f0;
          padding: 16px;
          border-radius: 8px;
          margin-bottom: 24px;
          text-align: left;
          font-size: 0.9rem;
          color: #475569;
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
            background-color: #f1f5f9;
            border: 1px solid #cbd5e1;
            color: #334155;
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
