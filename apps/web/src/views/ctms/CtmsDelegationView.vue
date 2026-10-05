<template>
  <div class="ctms-subview ctms-delegation-view">
    <div class="card" style="margin-bottom: 24px">
      <div class="card-title">Delegation of Authority (DOA) Log</div>
      <p style="color: #64748b; font-size: 14px; margin-bottom: 16px">
        Manage site investigator duties, trial responsibilities, delegation
        start/end dates, and electronic sign-offs.
      </p>
      <div id="ctms-delegations-container">
        <table class="clinical-visit-matrix">
          <thead>
            <tr>
              <th scope="col">Staff Member</th>
              <th scope="col">Role</th>
              <th scope="col">Assigned Duties</th>
              <th scope="col">Start Date</th>
              <th scope="col">End Date</th>
              <th scope="col">Status</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="s in delegatedStaff" :key="s.id">
              <td>
                <strong>{{ s.user_name || s.user_id }}</strong>
              </td>
              <td>{{ s.role }}</td>
              <td>
                {{
                  s.assigned_duties
                    ? s.assigned_duties.join(", ")
                    : "All Duties"
                }}
              </td>
              <td>{{ s.start_date ? s.start_date.slice(0, 10) : "N/A" }}</td>
              <td>{{ s.end_date ? s.end_date.slice(0, 10) : "Active" }}</td>
              <td>
                <span class="badge" :class="{ gxp: s.status === 'ACTIVE' }">{{
                  s.status || "ACTIVE"
                }}</span>
              </td>
            </tr>
            <tr v-if="delegatedStaff.length === 0">
              <td
                colspan="6"
                style="text-align: center; color: #64748b; padding: 12px"
              >
                No delegated staff members found for site.
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from "vue";
import { useClinicalStore } from "../../stores/clinical";
import apiClient from "../../services/api";

const store = useClinicalStore();
const delegatedStaff = ref([]);

onMounted(async () => {
  const siteId = store.activeSiteId || "SITE-001";
  try {
    const res = await apiClient
      .get(`/ctms/sites/${siteId}/delegations`)
      .catch(() => ({ data: [] }));
    delegatedStaff.value = res.data || [];
  } catch (err) {
    console.error("Error loading DOA log:", err);
  }
});
</script>
