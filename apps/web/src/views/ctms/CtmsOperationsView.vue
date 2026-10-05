<template>
  <div class="ctms-subview ctms-operations-view">
    <div class="grid-2-responsive">
      <!-- Site Milestones Card -->
      <div class="card">
        <div class="card-title">Site Operational Milestones</div>
        <div id="ctms-milestones-container">
          <table class="clinical-visit-matrix">
            <thead>
              <tr>
                <th scope="col">Milestone Type</th>
                <th scope="col">Planned Date</th>
                <th scope="col">Actual Date</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="m in milestones" :key="m.id">
                <td>
                  <strong>{{ m.milestone_type }}</strong>
                </td>
                <td>
                  {{ m.planned_date ? m.planned_date.slice(0, 10) : "N/A" }}
                </td>
                <td>
                  {{ m.actual_date ? m.actual_date.slice(0, 10) : "Pending" }}
                </td>
                <td>
                  <span
                    class="badge"
                    :class="{ gxp: m.status === 'ACHIEVED' }"
                    >{{ m.status }}</span
                  >
                </td>
              </tr>
              <tr v-if="milestones.length === 0">
                <td
                  colspan="4"
                  style="text-align: center; color: #64748b; padding: 12px"
                >
                  No milestones found. Create one to begin tracking.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Monitoring Visits Card -->
      <div class="card">
        <div class="card-title">CRA Site Monitoring Visits (MVR)</div>
        <div id="ctms-visits-container">
          <table class="clinical-visit-matrix">
            <thead>
              <tr>
                <th scope="col">Visit Type</th>
                <th scope="col">Scheduled Date</th>
                <th scope="col">Actual Date</th>
                <th scope="col">CRA Assigned</th>
                <th scope="col">Status</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="v in visits" :key="v.id">
                <td>
                  <strong>{{ v.visit_type }}</strong>
                </td>
                <td>
                  {{ v.scheduled_date ? v.scheduled_date.slice(0, 10) : "N/A" }}
                </td>
                <td>
                  {{ v.actual_date ? v.actual_date.slice(0, 10) : "Pending" }}
                </td>
                <td>{{ v.cra_user_id || "Unassigned" }}</td>
                <td>
                  <span
                    class="badge"
                    :class="{ gxp: v.status === 'COMPLETED' }"
                    >{{ v.status }}</span
                  >
                </td>
              </tr>
              <tr v-if="visits.length === 0">
                <td
                  colspan="5"
                  style="text-align: center; color: #64748b; padding: 12px"
                >
                  No monitoring visits scheduled.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { useClinicalStore } from "../../stores/clinical";
import apiClient from "../../services/api";

const store = useClinicalStore();
const milestones = ref([]);
const visits = ref([]);

onMounted(async () => {
  const siteId = store.activeSiteId || "SITE-001";
  try {
    const [mRes, vRes] = await Promise.all([
      apiClient
        .get(`/ctms/sites/${siteId}/milestones`)
        .catch(() => ({ data: [] })),
      apiClient.get(`/ctms/sites/${siteId}/visits`).catch(() => ({ data: [] })),
    ]);
    milestones.value = mRes.data || [];
    visits.value = vRes.data || [];
  } catch (err) {
    console.error("Error loading CTMS Operations sub-view data:", err);
  }
});
</script>
