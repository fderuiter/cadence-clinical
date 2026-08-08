<template>
  <div id="section-admin" class="dashboard-section active">
    <div class="section-header">
      <h2>Dedicated Site Administration Suite</h2>
      <p>
        Centralized governance portal to configure clinical trial sites, populate personnel directories,
        and manage role delegations while strictly adhering to FDA 21 CFR Part 11 and GxP standards.
      </p>
    </div>

    <!-- Access Gating Check -->
    <div
      v-if="!hasAdminAccess"
      class="card admin-gating-banner"
      style="
        border-left: 4px solid var(--error);
        background-color: var(--error-bg);
        padding: 24px;
      "
    >
      <div
        class="admin-gating-content"
        style="display: flex; gap: 16px; align-items: flex-start"
      >
        <span class="admin-gating-icon" style="font-size: 2rem">🚫</span>
        <div>
          <h3 class="admin-gating-title">
            21 CFR Part 11 Role Gating - Access Denied
          </h3>
          <p class="admin-gating-text">
            You do not have the required <strong>sponsor_admin</strong> role to view or interact with administrative
            site configurations. Please authenticate with an authorized account or consult your system administrator.
          </p>
        </div>
      </div>
    </div>

    <div v-else>
      <!-- Sub Navigation Tabs inside Administration Suite -->
      <div
        class="tabs-navigation"
        style="
          display: flex;
          flex-wrap: wrap;
          gap: var(--spacing-sm);
          margin-bottom: var(--spacing-lg);
          border-bottom: 2px solid var(--border);
          padding-bottom: 10px;
        "
      >
        <button
          class="btn tab-btn-sites"
          :style="
            activeTab === 'sites'
              ? 'background-color: var(--primary); color: white;'
              : 'background-color: rgba(226, 232, 240, 1); color: #475569;'
          "
          @click="activeTab = 'sites'"
        >
          🏥 Clinical Sites
        </button>
        <button
          class="btn tab-btn-personnel"
          :style="
            activeTab === 'personnel'
              ? 'background-color: var(--primary); color: white;'
              : 'background-color: rgba(226, 232, 240, 1); color: #475569;'
          "
          @click="activeTab = 'personnel'"
        >
          👥 Personnel & Directory
        </button>
        <button
          class="btn tab-btn-assignments"
          :style="
            activeTab === 'assignments'
              ? 'background-color: var(--primary); color: white;'
              : 'background-color: rgba(226, 232, 240, 1); color: #475569;'
          "
          @click="activeTab = 'assignments'"
        >
          🔗 Site & Study Assignments
        </button>
        <button
          class="btn tab-btn-orgs"
          :style="
            activeTab === 'orgs'
              ? 'background-color: var(--primary); color: white;'
              : 'background-color: rgba(226, 232, 240, 1); color: #475569;'
          "
          @click="activeTab = 'orgs'"
        >
          🏢 Organizations
        </button>
      </div>

      <!-- General loading/error panel -->
      <div v-if="adminStore.loading" class="card" style="padding: 16px; text-align: center; color: #475569;">
        <span>⏳ Syncing directory context with organization service...</span>
      </div>

      <div v-if="successMessage" class="card" style="border-left: 4px solid #10b981; background-color: #ecfdf5; color: #065f46; padding: 12px; margin-bottom: 16px;">
        {{ successMessage }}
      </div>

      <div v-if="adminStore.error" class="card" style="border-left: 4px solid var(--error); background-color: var(--error-bg); color: #991b1b; padding: 12px; margin-bottom: 16px;">
        ⚠️ Error: {{ adminStore.error }}
      </div>

      <!-- Tab 1: Clinical Sites Management -->
      <div v-if="activeTab === 'sites'" class="grid-2-responsive" style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
        <!-- Sites List -->
        <div class="card" style="display: flex; flex-direction: column; height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            Active Clinical Trial Sites
          </div>
          <div v-if="adminStore.sites.length === 0" style="color: #64748b; font-style: italic; padding: 12px 0;">
            No trial sites registered. Use the configuration form to create one.
          </div>
          <div v-else style="display: flex; flex-direction: column; gap: 12px; max-height: 500px; overflow-y: auto;">
            <div
              v-for="site in adminStore.sites"
              :key="site.id"
              class="site-row"
              style="
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 12px;
                background-color: #f8fafc;
                display: flex;
                justify-content: space-between;
                align-items: center;
              "
            >
              <div>
                <div style="font-weight: 600; color: var(--primary);">{{ site.name }}</div>
                <div style="font-size: 0.8rem; color: #475569;">
                  ID: <code style="background-color: #e2e8f0; padding: 2px 4px; border-radius: 4px;">{{ site.site_id }}</code>
                  <span v-if="site.study_id"> | Study: {{ site.study_id }}</span>
                </div>
                <div style="font-size: 0.75rem; color: #64748b; margin-top: 4px;">
                  Version: {{ site.version_index }} | Reason: <span style="font-style: italic;">"{{ site.reason_for_change }}"</span>
                </div>
              </div>
              <button
                class="btn"
                style="padding: 4px 8px; font-size: 0.8rem; background-color: var(--accent); color: var(--primary); border: none; border-radius: 4px; cursor: pointer;"
                @click="selectSiteForEdit(site)"
              >
                ✏️ Edit
              </button>
            </div>
          </div>
        </div>

        <!-- Create/Edit Site Form -->
        <div class="card" style="height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            {{ isEditingSite ? "✏️ Update Site Details" : "➕ Register New Clinical Site" }}
          </div>

          <form @submit.prevent="submitSiteForm">
            <div style="display: flex; flex-direction: column; gap: 16px;">
              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Site Code Identifier <span style="color: var(--error);">*</span>
                </label>
                <input
                  v-model="siteForm.site_id"
                  type="text"
                  placeholder="e.g. SITE-001"
                  required
                  :disabled="isEditingSite"
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                />
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Site Name <span style="color: var(--error);">*</span>
                </label>
                <input
                  v-model="siteForm.name"
                  type="text"
                  placeholder="e.g. Boston Medical Research Center"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                />
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Parent Organization <span style="color: var(--error);">*</span>
                </label>
                <select
                  v-model="siteForm.organization_id"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                >
                  <option value="" disabled>-- Select affiliated organization --</option>
                  <option
                    v-for="org in adminStore.organizations"
                    :key="org.id"
                    :value="org.id"
                  >
                    {{ org.name }} ({{ org.org_type }})
                  </option>
                </select>
                <p style="font-size: 0.75rem; color: #64748b; margin-top: 4px;">
                  If the organization isn't listed, create it in the Organizations tab first.
                </p>
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Clinical Study ID (Optional)
                </label>
                <input
                  v-model="siteForm.study_id"
                  type="text"
                  placeholder="e.g. STUDY-USDM-001"
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                />
              </div>

              <!-- FDA 21 CFR Part 11 Audit Trail Justification -->
              <div style="background-color: rgba(248, 250, 252, 1); border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-top: 8px;">
                <label style="display: block; font-weight: 700; font-size: 0.85rem; margin-bottom: 6px; color: #1e293b;">
                  📝 Change Reason Justification <span style="color: var(--error);">*</span>
                </label>
                <textarea
                  v-model="siteForm.change_reason"
                  placeholder="Mandatory GxP audit description. E.g., Initial provisioning of Phase II site roster."
                  rows="3"
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-family: inherit; font-size: 0.85rem;"
                ></textarea>
                <p style="font-size: 0.75rem; color: #64748b; margin-top: 4px; font-style: italic;">
                  Submission is locked under Part 11 until a non-empty change justification is provided.
                </p>
              </div>

              <div style="display: flex; gap: 12px; margin-top: 8px;">
                <button
                  type="submit"
                  class="btn"
                  id="btn-save-site"
                  :disabled="!isSiteFormValid"
                  :style="
                    isSiteFormValid
                      ? 'background-color: var(--primary); color: white; cursor: pointer; flex: 1;'
                      : 'background-color: #cbd5e1; color: #94a3b8; cursor: not-allowed; flex: 1;'
                  "
                >
                  💾 {{ isEditingSite ? "Update Site Record" : "Register Site" }}
                </button>
                <button
                  v-if="isEditingSite"
                  type="button"
                  class="btn"
                  style="background-color: #f1f5f9; color: #475569; border: 1px solid var(--border);"
                  @click="resetSiteForm"
                >
                  Cancel
                </button>
              </div>
            </div>
          </form>
        </div>
      </div>

      <!-- Tab 2: Personnel & Directory -->
      <div v-if="activeTab === 'personnel'" class="grid-2-responsive" style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
        <!-- Personnel List -->
        <div class="card" style="display: flex; flex-direction: column; height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            Personnel Directory Roster
          </div>
          <div v-if="adminStore.personnel.length === 0" style="color: #64748b; font-style: italic; padding: 12px 0;">
            No staff records registered. Use the provisioning form to register personnel.
          </div>
          <div v-else style="display: flex; flex-direction: column; gap: 12px; max-height: 500px; overflow-y: auto;">
            <div
              v-for="person in adminStore.personnel"
              :key="person.id"
              class="personnel-row"
              style="
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 12px;
                background-color: #f8fafc;
                display: flex;
                justify-content: space-between;
                align-items: center;
              "
            >
              <div>
                <div style="font-weight: 600; color: var(--primary);">
                  {{ person.first_name }} {{ person.last_name }}
                </div>
                <div style="font-size: 0.8rem; color: #475569; margin-top: 2px;">
                  <span class="badge" style="background-color: var(--primary-light); color: white; padding: 2px 6px; border-radius: 4px; font-size: 0.75rem;">
                    {{ person.role }}
                  </span>
                  <span style="margin-left: 8px;">{{ person.email }}</span>
                </div>
                <div style="font-size: 0.75rem; color: #64748b; margin-top: 6px;">
                  Site scope: <code>{{ person.site_id || "None" }}</code>
                  <span v-if="person.organization_id"> | Org ID: {{ person.organization_id.slice(0, 8) }}...</span>
                </div>
                <div style="font-size: 0.7rem; color: #94a3b8; font-style: italic; margin-top: 4px;">
                  Reason: "{{ person.reason_for_change }}"
                </div>
              </div>
              <button
                class="btn"
                style="padding: 4px 8px; font-size: 0.8rem; background-color: var(--accent); color: var(--primary); border: none; border-radius: 4px; cursor: pointer;"
                @click="selectPersonnelForEdit(person)"
              >
                ✏️ Edit
              </button>
            </div>
          </div>
        </div>

        <!-- Provision Personnel Form -->
        <div class="card" style="height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            {{ isEditingPersonnel ? "✏️ Edit Staff Credentials" : "➕ Provision New Trial Staff" }}
          </div>

          <form @submit.prevent="submitPersonnelForm">
            <div style="display: flex; flex-direction: column; gap: 16px;">
              <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
                <div>
                  <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                    First Name <span style="color: var(--error);">*</span>
                  </label>
                  <input
                    v-model="personnelForm.first_name"
                    type="text"
                    placeholder="e.g. Jane"
                    required
                    style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                  />
                </div>
                <div>
                  <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                    Last Name <span style="color: var(--error);">*</span>
                  </label>
                  <input
                    v-model="personnelForm.last_name"
                    type="text"
                    placeholder="e.g. Doe"
                    required
                    style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                  />
                </div>
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Email Address <span style="color: var(--error);">*</span>
                </label>
                <input
                  v-model="personnelForm.email"
                  type="email"
                  placeholder="e.g. jane.doe@hospital.org"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                />
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Clinical Role Type <span style="color: var(--error);">*</span>
                </label>
                <select
                  v-model="personnelForm.role"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                >
                  <option value="" disabled>-- Select role --</option>
                  <option value="Principal Investigator">Principal Investigator</option>
                  <option value="Sub-Investigator">Sub-Investigator</option>
                  <option value="CRC">CRC</option>
                  <option value="CRA/Monitor">CRA/Monitor</option>
                  <option value="External Monitor">External Monitor</option>
                </select>
              </div>

              <div style="border-top: 1px solid var(--border); padding-top: 12px; margin-top: 4px;">
                <span style="font-size: 0.8rem; font-weight: bold; color: #475569; display: block; margin-bottom: 8px;">
                  Directory Assignments & Affiliations
                </span>

                <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px;">
                  <div>
                    <label style="display: block; font-weight: 600; font-size: 0.8rem; margin-bottom: 4px; color: #475569;">
                      Organization
                    </label>
                    <select
                      v-model="personnelForm.organization_id"
                      style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-size: 0.85rem;"
                    >
                      <option :value="null">-- None --</option>
                      <option
                        v-for="org in adminStore.organizations"
                        :key="org.id"
                        :value="org.id"
                      >
                        {{ org.name }}
                      </option>
                    </select>
                  </div>

                  <div>
                    <label style="display: block; font-weight: 600; font-size: 0.8rem; margin-bottom: 4px; color: #475569;">
                      Clinical Site Code
                    </label>
                    <select
                      v-model="personnelForm.site_id"
                      style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-size: 0.85rem;"
                    >
                      <option :value="null">-- None --</option>
                      <option
                        v-for="site in adminStore.sites"
                        :key="site.id"
                        :value="site.site_id"
                      >
                        {{ site.name }} ({{ site.site_id }})
                      </option>
                    </select>
                  </div>
                </div>

                <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-top: 12px;">
                  <div>
                    <label style="display: block; font-weight: 600; font-size: 0.8rem; margin-bottom: 4px; color: #475569;">
                      Clinical Study ID
                    </label>
                    <input
                      v-model="personnelForm.study_id"
                      type="text"
                      placeholder="e.g. STUDY-USDM-001"
                      style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-size: 0.85rem;"
                    />
                  </div>

                  <div>
                    <label style="display: block; font-weight: 600; font-size: 0.8rem; margin-bottom: 4px; color: #475569;">
                      Keycloak User ID
                    </label>
                    <input
                      v-model="personnelForm.keycloak_user_id"
                      type="text"
                      placeholder="e.g. user-id-999"
                      style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-size: 0.85rem;"
                    />
                  </div>
                </div>
              </div>

              <!-- FDA 21 CFR Part 11 Audit Trail Justification -->
              <div style="background-color: rgba(248, 250, 252, 1); border: 1px solid var(--border); border-radius: 6px; padding: 12px;">
                <label style="display: block; font-weight: 700; font-size: 0.85rem; margin-bottom: 6px; color: #1e293b;">
                  📝 Change Reason Justification <span style="color: var(--error);">*</span>
                </label>
                <textarea
                  v-model="personnelForm.change_reason"
                  placeholder="Mandatory GxP audit description. E.g., Adding newly certified investigator."
                  rows="3"
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-family: inherit; font-size: 0.85rem;"
                ></textarea>
                <p style="font-size: 0.75rem; color: #64748b; margin-top: 4px; font-style: italic;">
                  Submission is locked under Part 11 until a non-empty change justification is provided.
                </p>
              </div>

              <div style="display: flex; gap: 12px;">
                <button
                  type="submit"
                  class="btn"
                  id="btn-save-personnel"
                  :disabled="!isPersonnelFormValid"
                  :style="
                    isPersonnelFormValid
                      ? 'background-color: var(--primary); color: white; cursor: pointer; flex: 1;'
                      : 'background-color: #cbd5e1; color: #94a3b8; cursor: not-allowed; flex: 1;'
                  "
                >
                  💾 {{ isEditingPersonnel ? "Update Staff Record" : "Provision Staff" }}
                </button>
                <button
                  v-if="isEditingPersonnel"
                  type="button"
                  class="btn"
                  style="background-color: #f1f5f9; color: #475569; border: 1px solid var(--border);"
                  @click="resetPersonnelForm"
                >
                  Cancel
                </button>
              </div>
            </div>
          </form>
        </div>
      </div>

      <!-- Tab 3: Site & Study Assignments -->
      <div v-if="activeTab === 'assignments'" class="grid-2-responsive" style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
        <!-- Personnel Directory & Current Assignments -->
        <div class="card" style="display: flex; flex-direction: column; height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            Personnel Roster & Site Delegations
          </div>
          <div v-if="adminStore.personnel.length === 0" style="color: #64748b; font-style: italic; padding: 12px 0;">
            No personnel found to assign. Register personnel first.
          </div>
          <div v-else style="display: flex; flex-direction: column; gap: 16px; max-height: 550px; overflow-y: auto;">
            <div
              v-for="person in adminStore.personnel"
              :key="person.id"
              class="person-assignment-card"
              style="
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 12px;
                background-color: #f8fafc;
              "
            >
              <div style="display: flex; justify-content: space-between; align-items: flex-start; border-bottom: 1px dashed var(--border); padding-bottom: 8px; margin-bottom: 8px;">
                <div>
                  <strong style="color: var(--primary);">{{ person.first_name }} {{ person.last_name }}</strong>
                  <div style="font-size: 0.8rem; color: #64748b;">Role: {{ person.role }}</div>
                </div>
                <button
                  class="btn"
                  style="padding: 4px 8px; font-size: 0.75rem; background-color: var(--primary); color: white; border: none; border-radius: 4px; cursor: pointer;"
                  @click="initiateAssignment(person)"
                >
                  ➕ Assign Site
                </button>
              </div>

              <!-- Loaded assignments -->
              <div>
                <span style="font-size: 0.75rem; font-weight: bold; color: #475569; display: block; margin-bottom: 4px;">
                  Active Site & Study Assignments:
                </span>
                <div v-if="!adminStore.assignments[person.id]" style="font-size: 0.75rem; color: #94a3b8; font-style: italic;">
                  Click to query/load assignments.
                  <button
                    class="btn"
                    style="background: none; border: none; color: var(--primary-light); cursor: pointer; text-decoration: underline; font-size: 0.75rem; padding: 0 4px;"
                    @click="adminStore.fetchAssignments(person.id)"
                  >
                    🔍 Load Assignments
                  </button>
                </div>
                <div v-else-if="adminStore.assignments[person.id].length === 0" style="font-size: 0.75rem; color: #64748b; font-style: italic;">
                  No active assignments found.
                </div>
                <div v-else style="display: flex; flex-direction: column; gap: 4px;">
                  <div
                    v-for="asg in adminStore.assignments[person.id]"
                    :key="asg.id"
                    style="
                      font-size: 0.75rem;
                      background-color: white;
                      padding: 6px;
                      border-radius: 4px;
                      border: 1px solid var(--border);
                      display: flex;
                      justify-content: space-between;
                      align-items: center;
                    "
                  >
                    <div>
                      Site Code: <code style="font-weight: bold;">{{ asg.site_id }}</code> | Study ID: <code>{{ asg.study_id }}</code>
                      <span v-if="!asg.is_active" style="color: var(--error); margin-left: 4px; font-weight: bold;">(Inactive)</span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Add Assignment Form -->
        <div class="card" style="height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            🔗 Assign Staff member to Clinical Site
          </div>

          <form @submit.prevent="submitAssignmentForm">
            <div style="display: flex; flex-direction: column; gap: 16px;">
              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Select Staff member <span style="color: var(--error);">*</span>
                </label>
                <select
                  v-model="assignmentForm.personnel_id"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                >
                  <option value="" disabled>-- Select a personnel member --</option>
                  <option
                    v-for="person in adminStore.personnel"
                    :key="person.id"
                    :value="person.id"
                  >
                    {{ person.first_name }} {{ person.last_name }} ({{ person.role }})
                  </option>
                </select>
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Select Target Clinical Site <span style="color: var(--error);">*</span>
                </label>
                <select
                  v-model="assignmentForm.site_id"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                >
                  <option value="" disabled>-- Select clinical site --</option>
                  <option
                    v-for="site in adminStore.sites"
                    :key="site.id"
                    :value="site.site_id"
                  >
                    {{ site.name }} ({{ site.site_id }})
                  </option>
                </select>
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Clinical Study ID <span style="color: var(--error);">*</span>
                </label>
                <input
                  v-model="assignmentForm.study_id"
                  type="text"
                  placeholder="e.g. STUDY-USDM-001"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                />
              </div>

              <div style="display: flex; align-items: center; gap: 8px;">
                <input
                  v-model="assignmentForm.is_active"
                  type="checkbox"
                  id="chk-active"
                  style="cursor: pointer;"
                />
                <label for="chk-active" style="font-weight: 600; font-size: 0.85rem; color: var(--primary); cursor: pointer;">
                  Mark Assignment as Active
                </label>
              </div>

              <!-- FDA 21 CFR Part 11 Audit Trail Justification -->
              <div style="background-color: rgba(248, 250, 252, 1); border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-top: 8px;">
                <label style="display: block; font-weight: 700; font-size: 0.85rem; margin-bottom: 6px; color: #1e293b;">
                  📝 Change Reason Justification <span style="color: var(--error);">*</span>
                </label>
                <textarea
                  v-model="assignmentForm.change_reason"
                  placeholder="Mandatory GxP audit description. E.g., Assigning CRC to primary treatment site."
                  rows="3"
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-family: inherit; font-size: 0.85rem;"
                ></textarea>
                <p style="font-size: 0.75rem; color: #64748b; margin-top: 4px; font-style: italic;">
                  Submission is locked under Part 11 until a non-empty change justification is provided.
                </p>
              </div>

              <button
                type="submit"
                class="btn"
                id="btn-save-assignment"
                :disabled="!isAssignmentFormValid"
                :style="
                  isAssignmentFormValid
                    ? 'background-color: var(--primary); color: white; cursor: pointer; width: 100%;'
                    : 'background-color: #cbd5e1; color: #94a3b8; cursor: not-allowed; width: 100%;'
                "
              >
                💾 Provision Site Assignment
              </button>
            </div>
          </form>
        </div>
      </div>

      <!-- Tab 4: Organizations Directory -->
      <div v-if="activeTab === 'orgs'" class="grid-2-responsive" style="display: grid; grid-template-columns: 1fr 1fr; gap: 20px;">
        <!-- Organizations list -->
        <div class="card" style="display: flex; flex-direction: column; height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            Registered Sponsor & CRO Organizations
          </div>
          <div v-if="adminStore.organizations.length === 0" style="color: #64748b; font-style: italic; padding: 12px 0;">
            No organizations found. Register one using the form on the right.
          </div>
          <div v-else style="display: flex; flex-direction: column; gap: 12px; max-height: 500px; overflow-y: auto;">
            <div
              v-for="org in adminStore.organizations"
              :key="org.id"
              class="org-row"
              style="
                border: 1px solid var(--border);
                border-radius: 6px;
                padding: 12px;
                background-color: #f8fafc;
              "
            >
              <div style="font-weight: 600; color: var(--primary);">{{ org.name }}</div>
              <div style="font-size: 0.8rem; color: #475569; margin-top: 2px;">
                Type: <span class="badge" style="background-color: var(--accent); color: var(--primary); padding: 2px 6px; border-radius: 4px; font-size: 0.75rem; font-weight: 600;">{{ org.org_type }}</span>
                | ID: <code style="background-color: #e2e8f0; padding: 2px 4px; border-radius: 4px; font-size: 0.75rem;">{{ org.id }}</code>
              </div>
              <div style="font-size: 0.75rem; color: #64748b; margin-top: 6px;">
                Version index: {{ org.version_index }} | Reason: <span style="font-style: italic;">"{{ org.reason_for_change }}"</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Create Organization Form -->
        <div class="card" style="height: fit-content;">
          <div class="card-title" style="font-weight: bold; font-size: 1.1rem; margin-bottom: 16px; color: var(--primary);">
            ➕ Register New Organization
          </div>

          <form @submit.prevent="submitOrgForm">
            <div style="display: flex; flex-direction: column; gap: 16px;">
              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Organization Name <span style="color: var(--error);">*</span>
                </label>
                <input
                  v-model="orgForm.name"
                  type="text"
                  placeholder="e.g. Apex CRO Services"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                />
              </div>

              <div>
                <label style="display: block; font-weight: 600; font-size: 0.85rem; margin-bottom: 6px; color: var(--primary);">
                  Organization Type <span style="color: var(--error);">*</span>
                </label>
                <select
                  v-model="orgForm.org_type"
                  required
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px;"
                >
                  <option value="" disabled>-- Select type --</option>
                  <option value="sponsor">Sponsor</option>
                  <option value="CRO">CRO</option>
                  <option value="IRB/IEC">IRB/IEC</option>
                  <option value="central laboratory">Central Laboratory</option>
                  <option value="site">Site</option>
                </select>
              </div>

              <!-- FDA 21 CFR Part 11 Audit Trail Justification -->
              <div style="background-color: rgba(248, 250, 252, 1); border: 1px solid var(--border); border-radius: 6px; padding: 12px; margin-top: 8px;">
                <label style="display: block; font-weight: 700; font-size: 0.85rem; margin-bottom: 6px; color: #1e293b;">
                  📝 Change Reason Justification <span style="color: var(--error);">*</span>
                </label>
                <textarea
                  v-model="orgForm.change_reason"
                  placeholder="Mandatory GxP audit description. E.g., Onboarding coordinating CRO partner."
                  rows="3"
                  style="width: 100%; padding: 8px; border: 1px solid var(--border); border-radius: 4px; font-family: inherit; font-size: 0.85rem;"
                ></textarea>
                <p style="font-size: 0.75rem; color: #64748b; margin-top: 4px; font-style: italic;">
                  Submission is locked under Part 11 until a non-empty change justification is provided.
                </p>
              </div>

              <button
                type="submit"
                class="btn"
                id="btn-save-org"
                :disabled="!isOrgFormValid"
                :style="
                  isOrgFormValid
                    ? 'background-color: var(--primary); color: white; cursor: pointer; width: 100%;'
                    : 'background-color: #cbd5e1; color: #94a3b8; cursor: not-allowed; width: 100%;'
                "
              >
                💾 Register Organization
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { useAuthStore } from "../stores/auth";
import { useAdminStore } from "../stores/admin";
import { hasRequiredRole } from "../router";

const authStore = useAuthStore();
const adminStore = useAdminStore();

// Restrict to sponsor_admin role
const hasAdminAccess = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, ["sponsor_admin"]);
});

const activeTab = ref("sites");
const successMessage = ref("");

// Site forms state
const isEditingSite = ref(false);
const editingSiteId = ref(null);
const siteForm = ref({
  site_id: "",
  name: "",
  organization_id: "",
  study_id: "",
  change_reason: "",
});

const isSiteFormValid = computed(() => {
  return (
    siteForm.value.site_id.trim() !== "" &&
    siteForm.value.name.trim() !== "" &&
    siteForm.value.organization_id !== "" &&
    siteForm.value.change_reason.trim() !== ""
  );
});

// Personnel forms state
const isEditingPersonnel = ref(false);
const editingPersonnelId = ref(null);
const personnelForm = ref({
  first_name: "",
  last_name: "",
  email: "",
  role: "",
  organization_id: null,
  site_id: null,
  study_id: null,
  keycloak_user_id: null,
  change_reason: "",
});

const isPersonnelFormValid = computed(() => {
  return (
    personnelForm.value.first_name.trim() !== "" &&
    personnelForm.value.last_name.trim() !== "" &&
    personnelForm.value.email.trim() !== "" &&
    personnelForm.value.role !== "" &&
    personnelForm.value.change_reason.trim() !== ""
  );
});

// Assignment forms state
const assignmentForm = ref({
  personnel_id: "",
  site_id: "",
  study_id: "",
  is_active: true,
  change_reason: "",
});

const isAssignmentFormValid = computed(() => {
  return (
    assignmentForm.value.personnel_id !== "" &&
    assignmentForm.value.site_id !== "" &&
    assignmentForm.value.study_id.trim() !== "" &&
    assignmentForm.value.change_reason.trim() !== ""
  );
});

// Organization forms state
const orgForm = ref({
  name: "",
  org_type: "",
  change_reason: "",
});

const isOrgFormValid = computed(() => {
  return (
    orgForm.value.name.trim() !== "" &&
    orgForm.value.org_type !== "" &&
    orgForm.value.change_reason.trim() !== ""
  );
});

// Initialization
onMounted(async () => {
  if (hasAdminAccess.value) {
    await loadInitialData();
  }
});

async function loadInitialData() {
  await Promise.all([
    adminStore.fetchOrganizations(),
    adminStore.fetchSites(),
    adminStore.fetchPersonnel(),
  ]);
}

function showSuccess(msg) {
  successMessage.value = msg;
  setTimeout(() => {
    successMessage.value = "";
  }, 4000);
}

// Site operations
function selectSiteForEdit(site) {
  isEditingSite.value = true;
  editingSiteId.value = site.id;
  siteForm.value = {
    site_id: site.site_id,
    name: site.name,
    organization_id: site.organization_id,
    study_id: site.study_id || "",
    change_reason: "", // Mandatory reason required for updates
  };
}

function resetSiteForm() {
  isEditingSite.value = false;
  editingSiteId.value = null;
  siteForm.value = {
    site_id: "",
    name: "",
    organization_id: "",
    study_id: "",
    change_reason: "",
  };
}

async function submitSiteForm() {
  if (!isSiteFormValid.value) return;

  const payload = {
    site_id: siteForm.value.site_id,
    name: siteForm.value.name,
    organization_id: siteForm.value.organization_id,
    study_id: siteForm.value.study_id || null,
  };

  try {
    if (isEditingSite.value) {
      await adminStore.updateSite(editingSiteId.value, payload, siteForm.value.change_reason);
      showSuccess(`Site "${payload.name}" updated successfully.`);
    } else {
      await adminStore.createSite(payload, siteForm.value.change_reason);
      showSuccess(`Site "${payload.name}" registered successfully.`);
    }
    resetSiteForm();
  } catch (err) {
    console.error(err);
  }
}

// Personnel operations
function selectPersonnelForEdit(person) {
  isEditingPersonnel.value = true;
  editingPersonnelId.value = person.id;
  personnelForm.value = {
    first_name: person.first_name,
    last_name: person.last_name,
    email: person.email,
    role: person.role,
    organization_id: person.organization_id || null,
    site_id: person.site_id || null,
    study_id: person.study_id || null,
    keycloak_user_id: person.keycloak_user_id || null,
    change_reason: "", // Mandatory reason required for updates
  };
}

function resetPersonnelForm() {
  isEditingPersonnel.value = false;
  editingPersonnelId.value = null;
  personnelForm.value = {
    first_name: "",
    last_name: "",
    email: "",
    role: "",
    organization_id: null,
    site_id: null,
    study_id: null,
    keycloak_user_id: null,
    change_reason: "",
  };
}

async function submitPersonnelForm() {
  if (!isPersonnelFormValid.value) return;

  const payload = {
    first_name: personnelForm.value.first_name,
    last_name: personnelForm.value.last_name,
    email: personnelForm.value.email,
    role: personnelForm.value.role,
    organization_id: personnelForm.value.organization_id,
    site_id: personnelForm.value.site_id,
    study_id: personnelForm.value.study_id,
    keycloak_user_id: personnelForm.value.keycloak_user_id,
  };

  try {
    if (isEditingPersonnel.value) {
      await adminStore.updatePersonnel(editingPersonnelId.value, payload, personnelForm.value.change_reason);
      showSuccess(`Staff credentials for "${payload.first_name} ${payload.last_name}" updated.`);
    } else {
      await adminStore.createPersonnel(payload, personnelForm.value.change_reason);
      showSuccess(`Provisioned staff member "${payload.first_name} ${payload.last_name}" successfully.`);
    }
    resetPersonnelForm();
  } catch (err) {
    console.error(err);
  }
}

// Assignment operations
function initiateAssignment(person) {
  assignmentForm.value = {
    personnel_id: person.id,
    site_id: person.site_id || "",
    study_id: person.study_id || "",
    is_active: true,
    change_reason: "",
  };
  activeTab.value = "assignments";
}

async function submitAssignmentForm() {
  if (!isAssignmentFormValid.value) return;

  const payload = {
    site_id: assignmentForm.value.site_id,
    study_id: assignmentForm.value.study_id,
    is_active: assignmentForm.value.is_active,
  };

  try {
    await adminStore.createAssignment(
      assignmentForm.value.personnel_id,
      payload,
      assignmentForm.value.change_reason
    );
    showSuccess("Site assignment provisioned successfully.");
    assignmentForm.value = {
      personnel_id: "",
      site_id: "",
      study_id: "",
      is_active: true,
      change_reason: "",
    };
  } catch (err) {
    console.error(err);
  }
}

// Organization operations
async function submitOrgForm() {
  if (!isOrgFormValid.value) return;

  const payload = {
    name: orgForm.value.name,
    org_type: orgForm.value.org_type,
  };

  try {
    await adminStore.createOrganization(payload, orgForm.value.change_reason);
    showSuccess(`Organization "${payload.name}" registered.`);
    orgForm.value = {
      name: "",
      org_type: "",
      change_reason: "",
    };
  } catch (err) {
    console.error(err);
  }
}
</script>
