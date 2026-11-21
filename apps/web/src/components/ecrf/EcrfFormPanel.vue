<template>
  <section id="ecrf-form-instance" class="card ecrf-form-panel">
    <div class="card-title">Case report form</div>

    <!-- Open a new form or reuse one by id (ADR-2622). -->
    <div v-if="!form" class="ecrf-form-open">
      <p class="ecrf-form-hint">
        <template v-if="enrollmentContext">
          Choose a form specification and visit for
          {{ enrollmentContext.subjectCode || "this participant" }}, then enter
          when the values were collected.
        </template>
        <template v-else>
          Each form is opened once for a participant's visit and then saved a
          page at a time. Reuse a form by its id, or open a new one.
        </template>
      </p>
      <form
        v-if="!enrollmentContext"
        class="ecrf-form-row"
        @submit.prevent="reuse"
      >
        <label for="ecrf-form-id">Form id</label>
        <input
          id="ecrf-form-id"
          v-model.trim="reuseId"
          type="text"
          autocomplete="off"
          placeholder="Form id (UUID)"
        />
        <button
          id="ecrf-form-load"
          class="btn btn-secondary"
          type="submit"
          :disabled="loading || !reuseId"
        >
          Load form
        </button>
      </form>

      <div
        v-if="isScheduleLoading"
        id="ecrf-schedule-skeleton"
        data-testid="ecrf-schedule-skeleton"
        role="status"
        aria-label="Loading form schedule options"
      >
        <SkeletonLoader :lines="3" />
      </div>

      <div
        v-else-if="effectiveScheduleError"
        id="ecrf-schedule-error"
        data-testid="ecrf-schedule-error"
        class="ecrf-form-outcome ecrf-form-outcome--failed"
        role="alert"
      >
        <span>
          Form schedule options could not be loaded:
          {{ effectiveScheduleError }}
        </span>
        <button
          id="btn-retry-schedule"
          class="btn btn-secondary"
          type="button"
          @click="emit('retry-schedule')"
        >
          Retry
        </button>
      </div>

      <form
        v-else
        id="ecrf-form-new"
        class="ecrf-form-grid"
        @submit.prevent="openNew"
      >
        <template v-if="!enrollmentContext">
          <label for="ecrf-enrollment-id">Enrollment id</label>
          <input
            id="ecrf-enrollment-id"
            v-model.trim="enrollmentId"
            type="text"
            autocomplete="off"
          />
        </template>
        <label for="ecrf-collection-spec">Collection spec</label>
        <select
          v-if="enrollmentContext && structuredSpecs.length > 0"
          id="ecrf-collection-spec"
          v-model="collectionSpecId"
        >
          <option value="">Select form specification…</option>
          <option
            v-for="spec in structuredSpecs"
            :key="spec.id"
            :value="spec.id"
          >
            {{ spec.name ? `${spec.name} (${spec.id})` : spec.id }}
          </option>
        </select>
        <input
          v-else
          id="ecrf-collection-spec"
          v-model.trim="collectionSpecId"
          type="text"
          autocomplete="off"
        />
        <label for="ecrf-planned-activity">Visit (planned activity)</label>
        <select
          v-if="enrollmentContext && hasProtocolSchedule"
          id="ecrf-planned-activity"
          v-model="plannedActivityId"
        >
          <option value="">Unscheduled</option>
          <option
            v-for="activity in structuredActivities"
            :key="activity.id"
            :value="activity.id"
          >
            {{ activityLabel(activity) }}
          </option>
        </select>
        <input
          v-else
          id="ecrf-planned-activity"
          v-model.trim="plannedActivityId"
          type="text"
          autocomplete="off"
          placeholder="None for an unscheduled form"
        />
        <label for="ecrf-collected-at">Collected at</label>
        <input
          id="ecrf-collected-at"
          v-model="collectedAt"
          type="datetime-local"
        />
        <button
          id="ecrf-form-open"
          class="btn btn-primary"
          type="submit"
          :disabled="opening || !canOpen"
        >
          {{ opening ? "Opening…" : "Open form" }}
        </button>
      </form>

      <p v-if="loading" role="status">Loading form…</p>
      <p v-if="loadError" class="ecrf-form-error" role="alert">
        The form could not be loaded: {{ loadError }}
        <button
          id="ecrf-form-retry"
          class="btn btn-secondary"
          type="button"
          :disabled="loading"
          @click="retryLoad"
        >
          Try again
        </button>
      </p>
      <p v-if="openError" class="ecrf-form-error" role="alert">
        The form could not be opened: {{ openError }}
      </p>
    </div>

    <template v-else>
      <dl class="ecrf-form-summary" data-testid="ecrf-form-summary">
        <dt>Form</dt>
        <dd data-testid="ecrf-form-id">
          {{ form.form_id }}
        </dd>
        <dt>Collects</dt>
        <dd>{{ form.collection_spec_id }}</dd>
        <dt>Visit</dt>
        <dd>{{ form.planned_activity_id ?? "Unscheduled" }}</dd>
        <dt>Collected</dt>
        <dd>{{ form.collected_at }}</dd>
        <dt>Version</dt>
        <dd data-testid="ecrf-form-version">
          {{ form.version }}
        </dd>
        <dt>Status</dt>
        <dd data-testid="ecrf-form-status">
          <span v-if="form.complete" class="ecrf-form-complete">Complete</span>
          <span v-else class="ecrf-form-incomplete">
            Incomplete: {{ form.missing_required.length }} required
            {{ form.missing_required.length === 1 ? "item" : "items" }}
            without a value ({{ form.missing_required.join(", ") }})
          </span>
        </dd>
      </dl>

      <div
        v-if="locked"
        class="ecrf-form-locks"
        data-testid="ecrf-form-locks"
        role="status"
      >
        <strong>Locked.</strong> Values cannot be changed while a lock is in
        force.
        <span data-testid="ecrf-lock-scope-note">
          <template v-if="assertionsLocked">
            Source verifications, verification withdrawals, and query changes
            are also blocked while a Locked assertion is in force.
          </template>
          <template v-else>
            Source verification and queries remain available while Frozen.
          </template>
        </span>
        <ul>
          <li v-for="held in form.locks" :key="held.lock_id">
            {{ held.kind }} on {{ held.scope }} by {{ held.locked_by }} at
            {{ held.locked_at }}: {{ held.reason }}
          </li>
        </ul>
      </div>

      <div
        v-if="canVerifySource && unverifiedItems.length > 0"
        class="ecrf-bulk-sdv"
        data-testid="ecrf-bulk-sdv"
      >
        <div class="ecrf-form-row">
          <strong
            >Source verification ({{ unverifiedItems.length }} unverified
            {{ unverifiedItems.length === 1 ? "item" : "items" }})</strong
          >
          <button
            id="ecrf-sdv-select-all"
            class="btn btn-secondary"
            type="button"
            :disabled="sdvBusy || assertionsLocked"
            @click="selectAllUnverifiedSdv"
          >
            Select all unverified
          </button>
          <button
            v-if="selectedSdvItems.length > 0"
            id="ecrf-sdv-clear-selection"
            class="btn btn-secondary"
            type="button"
            :disabled="sdvBusy"
            @click="selectedSdvItems = []"
          >
            Clear selection
          </button>
        </div>
        <p
          v-if="assertionsLocked"
          class="ecrf-form-hint"
          data-testid="ecrf-sdv-locked-note"
        >
          This scope is Locked, so source verifications cannot be recorded or
          withdrawn until the lock is released.
        </p>
        <div v-else-if="selectedSdvItems.length > 0" class="ecrf-form-row">
          <label for="ecrf-bulk-sdv-reason"
            >Verification reason ({{ selectedSdvItems.length }} selected)</label
          >
          <input
            id="ecrf-bulk-sdv-reason"
            v-model="bulkSdvReason"
            type="text"
            autocomplete="off"
            placeholder="Verified against source medical record"
            :disabled="sdvBusy"
          />
          <button
            id="ecrf-bulk-sdv-submit"
            class="btn btn-primary"
            type="button"
            :disabled="sdvBusy || !bulkSdvReason.trim()"
            @click="submitBulkSdv"
          >
            {{
              sdvBusy
                ? "Verifying…"
                : `Verify selected (${selectedSdvItems.length})`
            }}
          </button>
        </div>
        <ul
          v-if="bulkSdvResults.length > 0"
          class="ecrf-bulk-sdv-results"
          data-testid="ecrf-bulk-sdv-results"
          aria-live="polite"
        >
          <li
            v-for="res in bulkSdvResults"
            :key="res.itemId"
            :data-sdv-result="res.itemId"
            :data-ok="String(res.ok)"
            :class="
              res.ok
                ? 'ecrf-bulk-sdv-result--ok'
                : 'ecrf-bulk-sdv-result--failed'
            "
          >
            <strong>{{ res.itemId }}:</strong> {{ res.message }}
          </li>
        </ul>
      </div>

      <div
        v-if="sdvOutcome"
        data-testid="ecrf-sdv-outcome"
        :class="[
          'ecrf-form-outcome',
          sdvOutcome.kind === 'done'
            ? 'ecrf-form-outcome--saved'
            : 'ecrf-form-outcome--failed',
        ]"
        aria-live="polite"
        :role="sdvOutcome.kind === 'done' ? 'status' : 'alert'"
      >
        {{ sdvOutcome.message }}
        <button
          v-if="sdvOutcome.kind === 'stale'"
          id="ecrf-sdv-reload"
          class="btn btn-secondary"
          type="button"
          @click="reloadAfterSdvStale"
        >
          Reload form
        </button>
      </div>

      <form id="ecrf-form-page" @submit.prevent="submit">
        <div
          class="ecrf-table-scroll"
          data-testid="ecrf-form-items-scroll"
          role="region"
          aria-label="Case report form items"
          tabindex="0"
        >
          <table class="ecrf-form-items" data-testid="ecrf-form-items">
            <thead>
              <tr>
                <th scope="col">Item</th>
                <th scope="col">Value</th>
                <th scope="col">Or missing because</th>
                <th scope="col">Revision</th>
                <th scope="col">Verification</th>
                <th scope="col">Queries</th>
              </tr>
            </thead>
            <tbody>
              <template v-for="item in items" :key="item.item_id">
                <tr
                  :id="`ecrf-row-${item.item_id}`"
                  :data-item="item.item_id"
                  :data-highlighted="
                    highlightItemId === item.item_id ? 'true' : undefined
                  "
                  :class="{
                    'ecrf-item-edited': isEdited(item),
                    'ecrf-item-highlighted': highlightItemId === item.item_id,
                  }"
                >
                  <th scope="row">
                    <label :for="`ecrf-item-${item.item_id}`">
                      {{ item.item_id }}
                      <abbr
                        v-if="item.required"
                        class="ecrf-required"
                        title="Required"
                        >*</abbr
                      >
                    </label>
                    <div v-if="item.concept" class="ecrf-item-concept">
                      {{ item.concept }}
                    </div>
                  </th>
                  <td>
                    <input
                      :id="`ecrf-item-${item.item_id}`"
                      v-model="entries[item.item_id].text"
                      type="text"
                      autocomplete="off"
                      :disabled="
                        locked || saving || !!entries[item.item_id].missing
                      "
                      :aria-invalid="!!itemErrors[item.item_id]"
                      :aria-describedby="`ecrf-item-${item.item_id}-error`"
                    />
                    <span v-if="item.unit" class="ecrf-item-unit">{{
                      item.unit
                    }}</span>
                    <p
                      :id="`ecrf-item-${item.item_id}-error`"
                      class="ecrf-form-error"
                    >
                      {{ (itemErrors[item.item_id] ?? []).join(" ") }}
                    </p>
                  </td>
                  <td>
                    <select
                      v-model="entries[item.item_id].missing"
                      :aria-label="`Why ${item.item_id} is missing`"
                      :disabled="locked || saving"
                    >
                      <option value="">Not missing</option>
                      <option
                        v-for="option in MISSING_REASONS"
                        :key="option.reason"
                        :value="option.reason"
                      >
                        {{ option.text }}
                      </option>
                    </select>
                  </td>
                  <td class="ecrf-item-revision">
                    <template
                      v-if="item.value && !isUnrecordedValue(item.value)"
                    >
                      <component
                        :is="routerLinkTag"
                        v-if="itemObservationLink(item.observation_id)"
                        :id="`ecrf-item-observation-${item.item_id}`"
                        class="ecrf-observation-link"
                        :to="itemObservationLink(item.observation_id)!"
                        >{{ item.revision }}</component
                      >
                      <template v-else>{{ item.revision }}</template>
                      by {{ item.recorded_by }} at {{ item.recorded_at }}
                    </template>
                    <template v-else> No value </template>
                  </td>
                  <td>
                    <div class="ecrf-item-sdv">
                      <span
                        v-if="activeVerification(item)"
                        class="ecrf-sdv-badge ecrf-sdv-badge--verified"
                        :data-testid="`ecrf-sdv-status-${item.item_id}`"
                      >
                        Verified (rev {{ item.revision }})
                      </span>
                      <span
                        v-else-if="needsReverification(item)"
                        class="ecrf-sdv-badge ecrf-sdv-badge--reverify"
                        :data-testid="`ecrf-sdv-status-${item.item_id}`"
                      >
                        Needs re-verification
                      </span>
                      <span
                        v-else-if="hasRecordedValue(item)"
                        class="ecrf-sdv-badge ecrf-sdv-badge--unverified"
                        :data-testid="`ecrf-sdv-status-${item.item_id}`"
                      >
                        Unverified
                      </span>
                      <span
                        v-else
                        class="ecrf-sdv-none"
                        :data-testid="`ecrf-sdv-status-${item.item_id}`"
                      >
                        No value
                      </span>
                      <div
                        v-if="canVerifySource && hasRecordedValue(item)"
                        class="ecrf-sdv-actions"
                      >
                        <template v-if="!activeVerification(item)">
                          <input
                            :id="`ecrf-sdv-select-${item.item_id}`"
                            v-model="selectedSdvItems"
                            type="checkbox"
                            :value="item.item_id"
                            :disabled="sdvBusy || assertionsLocked"
                            :aria-label="`Select ${item.item_id} for source verification`"
                          />
                          <button
                            :id="`ecrf-verify-${item.item_id}`"
                            class="btn btn-secondary"
                            type="button"
                            :disabled="sdvBusy || assertionsLocked"
                            @click="beginVerify(item)"
                          >
                            Verify
                          </button>
                        </template>
                        <button
                          v-else
                          :id="`ecrf-unverify-${item.item_id}`"
                          class="btn btn-secondary"
                          type="button"
                          :disabled="sdvBusy || assertionsLocked"
                          @click="beginRetract(item, activeVerification(item)!)"
                        >
                          Withdraw verification
                        </button>
                      </div>
                    </div>
                  </td>
                  <td>
                    <div class="ecrf-item-queries">
                      <span
                        v-if="liveOn(item).length"
                        class="ecrf-query-badge"
                        :data-testid="`ecrf-query-badge-${item.item_id}`"
                      >
                        {{ liveOn(item).length }} unresolved
                        {{ liveOn(item).length === 1 ? "query" : "queries" }}
                      </span>
                      <span
                        v-if="liveOn(item).some((q) => q.value_changed)"
                        class="ecrf-query-changed"
                        :data-testid="`ecrf-query-changed-${item.item_id}`"
                      >
                        Value changed since queried
                      </span>
                      <button
                        v-if="canManageQueries"
                        :id="`ecrf-raise-${item.item_id}`"
                        class="btn btn-secondary"
                        type="button"
                        :disabled="queries.busy || assertionsLocked"
                        @click="beginRaise(item)"
                      >
                        Raise query
                      </button>
                    </div>
                  </td>
                </tr>
                <tr
                  v-if="pendingSdv?.item_id === item.item_id"
                  class="ecrf-sdv-row"
                >
                  <td colspan="6">
                    <div class="ecrf-form-row">
                      <label for="ecrf-sdv-reason">
                        <template v-if="pendingSdv.mode === 'verify'">
                          Reason for verifying
                          {{ pendingSdv.item_id }} (revision
                          {{ pendingSdv.revision }})
                        </template>
                        <template v-else>
                          Reason for withdrawing verification on
                          {{ pendingSdv.item_id }}
                        </template>
                      </label>
                      <input
                        id="ecrf-sdv-reason"
                        v-model="sdvReason"
                        type="text"
                        autocomplete="off"
                        :disabled="sdvBusy || assertionsLocked"
                      />
                      <button
                        id="ecrf-sdv-confirm"
                        class="btn btn-primary"
                        type="button"
                        :disabled="
                          sdvBusy || assertionsLocked || !sdvReason.trim()
                        "
                        @click="confirmSdv"
                      >
                        {{
                          pendingSdv.mode === "verify"
                            ? "Confirm verification"
                            : "Withdraw verification"
                        }}
                      </button>
                      <button
                        id="ecrf-sdv-cancel"
                        class="btn btn-secondary"
                        type="button"
                        :disabled="sdvBusy"
                        @click="pendingSdv = null"
                      >
                        Back
                      </button>
                    </div>
                  </td>
                </tr>
                <tr
                  v-if="raising?.item_id === item.item_id"
                  class="ecrf-raise-row"
                >
                  <td colspan="6">
                    <div class="ecrf-form-row">
                      <label for="ecrf-raise-text">
                        <template v-if="raising.unrecorded">
                          Missing-data query on {{ raising.item_id }} (no value
                          recorded)
                        </template>
                        <template v-else>
                          Query on {{ raising.item_id }} (revision
                          {{ raising.revision }})
                        </template>
                      </label>
                      <textarea
                        id="ecrf-raise-text"
                        v-model="raiseText"
                        rows="2"
                      />
                      <button
                        id="ecrf-raise-confirm"
                        class="btn btn-primary"
                        type="button"
                        :disabled="queries.busy || !raiseText.trim()"
                        @click="confirmRaise"
                      >
                        Raise query
                      </button>
                      <button
                        class="btn btn-secondary"
                        type="button"
                        @click="raising = null"
                      >
                        Back
                      </button>
                    </div>
                  </td>
                </tr>
              </template>
            </tbody>
          </table>
        </div>

        <div v-if="!locked" class="ecrf-form-row">
          <label for="ecrf-save-reason">Reason for change</label>
          <input
            id="ecrf-save-reason"
            v-model="reason"
            type="text"
            autocomplete="off"
            placeholder="Initial data entry"
          />
          <button
            id="ecrf-form-save"
            class="btn btn-primary"
            type="submit"
            :disabled="saving || !dirty || !reason.trim()"
          >
            {{ saving ? "Saving…" : `Save page (${editedItems.length})` }}
          </button>
          <button
            id="ecrf-form-close"
            class="btn btn-secondary"
            type="button"
            :disabled="saving"
            @click="closeForm"
          >
            Close form
          </button>
        </div>
      </form>

      <div
        v-if="outcome"
        data-testid="ecrf-form-outcome"
        :class="['ecrf-form-outcome', `ecrf-form-outcome--${outcome.kind}`]"
        :role="outcome.kind === 'saved' ? 'status' : 'alert'"
      >
        <template v-if="outcome.kind === 'saved'">
          Page saved: {{ outcome.changed }}
          {{ outcome.changed === 1 ? "value" : "values" }} recorded, form
          version {{ outcome.version }}.
        </template>
        <template v-else>
          {{ outcome.message }}
        </template>
        <div
          v-if="outcome.kind === 'locked' && outcome.unsaved.length > 0"
          class="ecrf-locked-unsaved"
          data-testid="ecrf-locked-unsaved"
        >
          <span>Unsaved values not recorded:</span>
          <ul>
            <li v-for="entry in outcome.unsaved" :key="entry.item_id">
              <strong>{{ entry.item_id }}:</strong>
              {{ formatUnsavedEntry(entry) }}
            </li>
          </ul>
        </div>
        <button
          v-if="outcome.kind === 'stale'"
          id="ecrf-form-reload"
          class="btn btn-secondary"
          type="button"
          @click="reload"
        >
          Reload form
        </button>
      </div>

      <section
        id="ecrf-form-queries"
        aria-labelledby="ecrf-form-queries-title"
        :aria-busy="queries.loading"
      >
        <h3 id="ecrf-form-queries-title">Queries</h3>
        <p v-if="queries.loading" role="status">Loading queries…</p>
        <p v-else-if="queries.loadError" class="ecrf-form-error" role="alert">
          The queries could not be loaded: {{ queries.loadError }}
          <button
            id="ecrf-queries-retry"
            class="btn btn-secondary"
            type="button"
            :disabled="queries.loading"
            @click="queries.reload"
          >
            Try again
          </button>
        </p>
        <p
          v-else-if="queries.queries.length === 0"
          data-testid="ecrf-no-queries"
        >
          No queries on this form.
        </p>
        <FormQueryList
          v-else
          :queries="queries.queries"
          :can-manage="!!canManageQueries"
          :busy="queries.busy"
          :assertions-locked="assertionsLocked"
          :act="queries.act"
          :act-many="queries.actMany"
          :form-to="queryFormLink"
          :observation-to="queryObservationLink"
        />
        <div
          data-testid="ecrf-query-outcome"
          :class="[
            'ecrf-form-outcome',
            queries.outcome
              ? `ecrf-form-outcome--${queries.outcome.kind === 'done' ? 'saved' : 'failed'}`
              : '',
          ]"
          aria-live="polite"
          role="status"
        >
          <template v-if="queries.outcome">
            {{ queries.outcome.message }}
            <button
              v-if="queries.outcome.kind === 'stale'"
              id="ecrf-query-reload"
              class="btn btn-secondary"
              type="button"
              @click="reloadAfterStale"
            >
              Reload form
            </button>
          </template>
        </div>
      </section>

      <h3>Verifications, queries and answers</h3>
      <p v-if="form.assertions.length === 0" data-testid="ecrf-no-assertions">
        None recorded.
      </p>
      <ul v-else data-testid="ecrf-form-assertions">
        <li v-for="assertion in form.assertions" :key="assertion.assertion_id">
          {{ assertion.kind }} on
          <component
            :is="routerLinkTag"
            v-if="assertionObservationLink(assertion.target)"
            class="ecrf-observation-link"
            :to="assertionObservationLink(assertion.target)!"
            >{{ assertion.target }}</component
          >
          <template v-else>{{ assertion.target }}</template>
          <template v-if="assertion.revision !== null">
            (revision {{ assertion.revision }})
          </template>
          by {{ assertion.asserted_by }} at {{ assertion.asserted_at }}
          <template v-if="assertion.text"> : “{{ assertion.text }}” </template>
          <em v-if="assertion.retracted_at">
            (retracted {{ assertion.retracted_at }})</em
          >
        </li>
      </ul>

      <section
        id="ecrf-form-signatures-section"
        aria-labelledby="ecrf-form-signatures-title"
      >
        <h3 id="ecrf-form-signatures-title">Signatures</h3>
        <p v-if="form.signatures.length === 0" data-testid="ecrf-no-signatures">
          Not signed. Signing records a kernel attestation over the form's
          current values; a later correction to a signed value supersedes the
          signature.
        </p>
        <ul v-else class="ecrf-signatures" data-testid="ecrf-form-signatures">
          <li
            v-for="signature in form.signatures"
            :key="signature.attestation_id"
            :class="[
              'ecrf-signature',
              signature.superseded
                ? 'ecrf-signature--superseded'
                : 'ecrf-signature--current',
            ]"
            :data-signature="signature.attestation_id"
          >
            <div>
              <strong>{{ meaningLabel(signature.meaning) }}</strong> by
              {{ signature.signed_by }} at {{ signature.signed_at }}:
              {{ signature.reason }}
              <span
                v-if="signature.superseded"
                class="ecrf-signature-state ecrf-signature-state--superseded"
                data-testid="ecrf-signature-superseded"
                >Superseded by a later correction</span
              >
              <span v-else class="ecrf-signature-state">Current</span>
            </div>
            <table
              v-if="signature.superseded && signature.signed_values?.length"
              class="ecrf-signed-values"
              data-testid="ecrf-signed-values"
            >
              <caption>
                What this signature signed, beside the current values
              </caption>
              <thead>
                <tr>
                  <th scope="col">Item</th>
                  <th scope="col">Signed value</th>
                  <th scope="col">Signed revision</th>
                  <th scope="col">Current value</th>
                  <th scope="col">Current revision</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="signed in signature.signed_values"
                  :key="signed.item_id"
                  :data-signed-item="signed.item_id"
                  :class="{
                    'ecrf-signed-changed': changedSince(signed),
                  }"
                >
                  <th scope="row">
                    {{ signed.item_id }}
                  </th>
                  <td>{{ formatObservationValue(signed.value) }}</td>
                  <td>
                    <component
                      :is="routerLinkTag"
                      v-if="itemObservationLink(signed.observation_id)"
                      class="ecrf-observation-link"
                      :to="itemObservationLink(signed.observation_id)!"
                      >{{ signed.revision }}</component
                    >
                    <template v-else>{{ signed.revision }}</template>
                  </td>
                  <td>{{ currentValueText(signed.item_id) }}</td>
                  <td>{{ currentOf(signed.item_id)?.revision ?? "None" }}</td>
                </tr>
              </tbody>
            </table>
          </li>
        </ul>

        <div class="ecrf-form-row">
          <label for="ecrf-sign-meaning">Meaning</label>
          <select
            id="ecrf-sign-meaning"
            v-model="signMeaning"
            :disabled="signing"
          >
            <option value="approved">Approved</option>
            <option value="responsibility">Responsibility</option>
          </select>
          <button
            id="ecrf-form-sign"
            class="btn btn-primary"
            type="button"
            :disabled="!canSign"
            :aria-describedby="signHint ? 'ecrf-sign-hint' : undefined"
            @click="showSignModal = true"
          >
            {{ signing ? "Signing…" : "Sign form" }}
          </button>
          <span
            v-if="signHint"
            id="ecrf-sign-hint"
            class="ecrf-form-hint"
            data-testid="ecrf-sign-hint"
            >{{ signHint }}</span
          >
        </div>

        <div
          v-if="signOutcome"
          data-testid="ecrf-sign-outcome"
          :class="[
            'ecrf-form-outcome',
            `ecrf-form-outcome--${signOutcome.kind === 'signed' ? 'saved' : 'failed'}`,
          ]"
          :role="signOutcome.kind === 'signed' ? 'status' : 'alert'"
        >
          <template v-if="signOutcome.kind === 'signed'">
            Form signed: {{ meaningLabel(signOutcome.meaning) }}.
          </template>
          <template v-else>
            {{ signOutcome.message }}
          </template>
          <button
            v-if="signOutcome.kind === 'changed'"
            id="ecrf-sign-reload"
            class="btn btn-secondary"
            type="button"
            @click="reloadAfterStale"
          >
            Reload form
          </button>
        </div>
      </section>

      <SignatureCaptureModal
        v-if="showSignModal"
        :is-open="showSignModal"
        :username="signerUsername"
        :signer-name="signerFullName"
        signer-role="Investigator"
        :reasons="ECRF_INVESTIGATOR_REASONS"
        :action-url="formSignatureAction(form.form_id)"
        :semantic-action="FORM_SIGN_SEMANTIC_ACTION"
        :on-sign="signWithToken"
        @cancel="showSignModal = false"
        @success="showSignModal = false"
      />
    </template>
  </section>
</template>

<script setup lang="ts">
import {
  computed,
  getCurrentInstance,
  nextTick,
  onMounted,
  onUnmounted,
  reactive,
  ref,
  watch,
} from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  formatObservationValue,
  isUnrecordedValue,
  MISSING_REASONS,
  type KernelObservationValue,
} from "../../api/observationValue";
import {
  FORM_SIGN_SEMANTIC_ACTION,
  formSignatureAction,
  type EcrfForm,
  type EcrfFormAssertion,
  type EcrfFormItem,
  type EcrfFormSignedValue,
  type EcrfSignatureMeaning,
} from "../../api/ecrfFormsApi";
import type { FormQueryResponse } from "../../api/ecrfQueriesApi";
import type {
  StudyCollectionSpecOption,
  StudyPlannedActivityOption,
} from "../../canvas/kernelStudy";
import { observationNodeRef } from "../../canvas/nodeRef";
import {
  ASSERTION_ALREADY_RETRACTED,
  ASSERTION_REVISION_STALE,
  assertionErrorCode,
  describeAssertionError,
  kernelApi,
} from "../../api/kernelApi";
import {
  useEcrfForm,
  type EcrfUnsavedItem,
} from "../../composables/useEcrfForm";
import { isLiveQuery, useFormQueries } from "../../composables/useFormQueries";
import { useAuthStore } from "../../stores/auth.js";
import SignatureCaptureModal from "../SignatureCaptureModal.vue";
import SkeletonLoader from "../common/SkeletonLoader.vue";
import FormQueryList from "./FormQueryList.vue";

export interface CanvasFormEnrollmentContext {
  enrollmentId: string;
  subjectCode?: string;
  collectionSpecs?: StudyCollectionSpecOption[];
  plannedActivities?: StudyPlannedActivityOption[];
  scheduleLoading?: boolean;
  scheduleError?: string | null;
}

const props = defineProps<{
  /** A form to load on mount, for example from the route. */
  formId?: string | null;
  enrollmentId?: string | null;
  collectionSpecId?: string | null;
  plannedActivityId?: string | null;
  /** Enrollment and structured protocol choices supplied when embedded in Clinical Canvas. */
  enrollmentContext?: CanvasFormEnrollmentContext | null;
  /** True while study Schedule of Activities options are loading for a new form. */
  scheduleLoading?: boolean;
  /** Error message when study Schedule of Activities options fail to load. */
  scheduleError?: string | null;
  /** Builds a route location for an observation inside the hosting workspace. */
  observationLocation?: (
    observationId: string,
    formId: string
  ) => RouteLocationRaw | null;
  /** Builds a route location for a form inside the hosting workspace. */
  formLocation?: (formId: string) => RouteLocationRaw | null;
  /** Offer raising, closing, reopening and cancelling queries (a monitor or data manager); the module still checks. */
  canManageQueries?: boolean;
  /** Offer verifying recorded values against source or withdrawing verifications (a monitor or data manager). */
  canVerifySource?: boolean;
  /** Item id to highlight and scroll into view when opened from a query worklist link. */
  highlightItemId?: string | null;
}>();

const emit = defineEmits<{
  (e: "dirty-change", dirty: boolean): void;
  (e: "form-loaded", loaded: EcrfForm): void;
  (e: "closed"): void;
  (e: "retry-schedule"): void;
}>();

const instance = getCurrentInstance();
const hasRouter = Boolean(instance?.appContext.config.globalProperties.$router);
const routerLinkTag = instance?.appContext.components.RouterLink ?? "a";

const {
  form,
  items,
  entries,
  itemErrors,
  loading,
  loadError,
  opening,
  openError,
  saving,
  outcome,
  signing,
  signOutcome,
  canSign,
  locked,
  assertionsLocked,
  dirty,
  editedItems,
  isEdited,
  clearDraft,
  load,
  retryLoad,
  reload,
  open,
  save,
  sign,
  dismiss,
  close,
} = useEcrfForm();

const reuseId = ref(props.formId ?? "");
const enrollmentId = ref(
  props.enrollmentContext?.enrollmentId ?? props.enrollmentId ?? ""
);
const collectionSpecId = ref(props.collectionSpecId ?? "");
const plannedActivityId = ref(props.plannedActivityId ?? "");
const collectedAt = ref("");
const reason = ref("");

const isScheduleLoading = computed(
  () =>
    !props.formId &&
    Boolean(props.scheduleLoading || props.enrollmentContext?.scheduleLoading)
);
const effectiveScheduleError = computed(() =>
  !props.formId
    ? (props.scheduleError ?? props.enrollmentContext?.scheduleError ?? null)
    : null
);

const structuredSpecs = computed(
  () => props.enrollmentContext?.collectionSpecs ?? []
);
const hasProtocolSchedule = computed(
  () => (props.enrollmentContext?.plannedActivities?.length ?? 0) > 0
);
const structuredActivities = computed(() => {
  const all = props.enrollmentContext?.plannedActivities ?? [];
  if (!all.length) return [];
  const selectedSpec = structuredSpecs.value.find(
    (s) => s.id === collectionSpecId.value
  );
  if (!selectedSpec || !Array.isArray(selectedSpec.plannedActivityIds)) {
    return all;
  }
  if (selectedSpec.plannedActivityIds.length === 0) return [];
  const scheduled = new Set(selectedSpec.plannedActivityIds);
  return all.filter((a) => scheduled.has(a.id));
});

function activityLabel(activity: StudyPlannedActivityOption): string {
  const base = activity.name
    ? `${activity.name} (${activity.id})`
    : activity.id;
  return activity.studyDay !== null
    ? `${base} · Day ${activity.studyDay}`
    : base;
}

watch(
  () => props.enrollmentContext?.enrollmentId ?? props.enrollmentId ?? "",
  (next) => {
    enrollmentId.value = next;
    openError.value = null;
  }
);

watch(
  () => props.collectionSpecId,
  (next) => {
    collectionSpecId.value = next ?? "";
  }
);

watch(
  () => props.plannedActivityId,
  (next) => {
    plannedActivityId.value = next ?? "";
  }
);

watch(structuredActivities, (allowed) => {
  if (
    plannedActivityId.value &&
    hasProtocolSchedule.value &&
    !allowed.some((a) => a.id === plannedActivityId.value)
  ) {
    plannedActivityId.value = "";
  }
});

function itemObservationLink(
  observationId: string | null | undefined
): RouteLocationRaw | null {
  if (!hasRouter || !observationId || !form.value) return null;
  if (props.observationLocation) {
    return props.observationLocation(observationId, form.value.form_id);
  }
  const ref = observationNodeRef(observationId);
  return ref ? { name: "observation", params: { observationRef: ref } } : null;
}

function assertionObservationLink(target: string): RouteLocationRaw | null {
  if (!target.startsWith("observation:")) return null;
  const ref = observationNodeRef(target);
  if (!ref) return null;
  const id = ref.slice("observation:".length);
  return itemObservationLink(id);
}

const queryObservationLink = (query: FormQueryResponse) =>
  itemObservationLink(query.observation_id);
const queryFormLink = computed(() =>
  props.formLocation
    ? (q: FormQueryResponse) => props.formLocation!(q.form_id)
    : undefined
);

const canOpen = computed(
  () =>
    enrollmentId.value.length > 0 &&
    collectionSpecId.value.length > 0 &&
    collectedAt.value.length > 0
);

function reuse() {
  if (reuseId.value) void load(reuseId.value);
}

function openNew() {
  if (!canOpen.value) return;
  void open({
    enrollmentId: enrollmentId.value,
    collectionSpecId: collectionSpecId.value,
    plannedActivityId: plannedActivityId.value || null,
    collectedAt: new Date(collectedAt.value).toISOString(),
  });
}

// The form's queries (#5461), read again whenever the form is read, so `value_changed` follows a correction.
// reactive() unwraps the composable's refs for the template.
const queries = reactive(useFormQueries());
const raising = ref<{
  item_id: string;
  observation_id: string | null;
  revision: number | null;
  unrecorded: boolean;
} | null>(null);
const raiseText = ref("");

// Source Data Verification (SDV) state: append-only kernel assertions (`VerifyObservation`, `RetractAssertion`).
const pendingSdv = ref<
  | {
      mode: "verify";
      item_id: string;
      observation_id: string;
      revision: number;
    }
  | {
      mode: "retract";
      item_id: string;
      assertion_id: string;
    }
  | null
>(null);
const sdvReason = ref("");
const sdvBusy = ref(false);
const sdvOutcome = ref<{
  kind: "done" | "stale" | "failed";
  message: string;
} | null>(null);
const selectedSdvItems = ref<string[]>([]);
const bulkSdvReason = ref("");
const bulkSdvResults = ref<
  Array<{ itemId: string; ok: boolean; message: string }>
>([]);

function hasRecordedValue(item: EcrfFormItem): boolean {
  return Boolean(item.observation_id) && !isUnrecordedValue(item.value);
}

function matchingTarget(
  target: string | null | undefined,
  observationId: string
): boolean {
  if (!target) return false;
  const prefix = `observation:${observationId}`;
  return target === prefix || target.startsWith(`${prefix}@`);
}

function activeVerification(item: EcrfFormItem): EcrfFormAssertion | null {
  if (!hasRecordedValue(item) || !item.observation_id) return null;
  const assertions = form.value?.assertions ?? [];
  for (let i = assertions.length - 1; i >= 0; i--) {
    const a = assertions[i];
    if (
      a.kind.toLowerCase() === "verified" &&
      matchingTarget(a.target, item.observation_id) &&
      !a.retracted_at &&
      Number(a.revision) === Number(item.revision)
    ) {
      return a;
    }
  }
  return null;
}

function needsReverification(item: EcrfFormItem): boolean {
  if (!hasRecordedValue(item) || !item.observation_id) return false;
  if (activeVerification(item)) return false;
  return (form.value?.assertions ?? []).some(
    (a) =>
      a.kind.toLowerCase() === "verified" &&
      matchingTarget(a.target, item.observation_id as string) &&
      !a.retracted_at &&
      a.revision !== null &&
      Number(a.revision) < Number(item.revision)
  );
}

const unverifiedItems = computed(() =>
  items.value.filter(
    (item) => hasRecordedValue(item) && !activeVerification(item)
  )
);

function selectAllUnverifiedSdv() {
  selectedSdvItems.value = unverifiedItems.value.map((item) => item.item_id);
}

function beginVerify(item: EcrfFormItem) {
  if (!item.observation_id || !hasRecordedValue(item)) return;
  pendingSdv.value = {
    mode: "verify",
    item_id: item.item_id,
    observation_id: item.observation_id,
    revision: Number(item.revision),
  };
  sdvReason.value = "";
  sdvOutcome.value = null;
}

function beginRetract(item: EcrfFormItem, assertion: EcrfFormAssertion) {
  pendingSdv.value = {
    mode: "retract",
    item_id: item.item_id,
    assertion_id: assertion.assertion_id,
  };
  sdvReason.value = "";
  sdvOutcome.value = null;
}

async function confirmSdv() {
  const current = pendingSdv.value;
  const text = sdvReason.value.trim();
  if (!form.value || !current || !text || sdvBusy.value) return;
  sdvBusy.value = true;
  sdvOutcome.value = null;
  bulkSdvResults.value = [];
  const enrollmentRef = `enrollment:${form.value.enrollment_id}`;
  try {
    if (current.mode === "verify") {
      await kernelApi.verifyObservation({
        observationRef: `observation:${current.observation_id}`,
        enrollmentRef,
        revision: current.revision,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      pendingSdv.value = null;
      sdvReason.value = "";
      selectedSdvItems.value = selectedSdvItems.value.filter(
        (id) => id !== current.item_id
      );
      await reload();
      sdvOutcome.value = {
        kind: "done",
        message: `Verified ${current.item_id} (revision ${current.revision}).`,
      };
    } else {
      await kernelApi.retractAssertion({
        assertionRef: `assertion:${current.assertion_id}`,
        enrollmentRef,
        reason: text,
        idempotencyKey: globalThis.crypto.randomUUID(),
      });
      pendingSdv.value = null;
      sdvReason.value = "";
      await reload();
      sdvOutcome.value = {
        kind: "done",
        message: `Verification withdrawn on ${current.item_id}.`,
      };
    }
  } catch (error) {
    const code = assertionErrorCode(error);
    const status = (error as { status?: number | null } | null)?.status ?? null;
    const isStale =
      code === ASSERTION_REVISION_STALE ||
      code === ASSERTION_ALREADY_RETRACTED ||
      status === 409;
    sdvOutcome.value = {
      kind: isStale ? "stale" : "failed",
      message: describeAssertionError(error),
    };
  } finally {
    sdvBusy.value = false;
  }
}

async function submitBulkSdv() {
  const text = bulkSdvReason.value.trim();
  if (
    !form.value ||
    !text ||
    sdvBusy.value ||
    selectedSdvItems.value.length === 0
  )
    return;
  const selected = new Set(selectedSdvItems.value);
  const targets = unverifiedItems.value.filter((i) => selected.has(i.item_id));
  if (targets.length === 0) return;

  sdvBusy.value = true;
  sdvOutcome.value = null;
  const results: Array<{ itemId: string; ok: boolean; message: string }> = [];
  const enrollmentRef = `enrollment:${form.value.enrollment_id}`;
  try {
    for (const target of targets) {
      if (!target.observation_id) continue;
      try {
        await kernelApi.verifyObservation({
          observationRef: `observation:${target.observation_id}`,
          enrollmentRef,
          revision: Number(target.revision),
          reason: text,
          idempotencyKey: globalThis.crypto.randomUUID(),
        });
        results.push({
          itemId: target.item_id,
          ok: true,
          message: `Verified revision ${target.revision}.`,
        });
      } catch (error) {
        results.push({
          itemId: target.item_id,
          ok: false,
          message: describeAssertionError(error),
        });
      }
    }
    bulkSdvResults.value = results;
    const failed = new Set(results.filter((r) => !r.ok).map((r) => r.itemId));
    selectedSdvItems.value = selectedSdvItems.value.filter((id) =>
      failed.has(id)
    );
    if (failed.size === 0) {
      bulkSdvReason.value = "";
    }
    await reload();
  } finally {
    sdvBusy.value = false;
  }
}

watch(
  () => (form.value ? `${form.value.form_id}@${form.value.version}` : null),
  (loaded) => {
    raising.value = null;
    pendingSdv.value = null;
    if (loaded && form.value) {
      emit("form-loaded", form.value);
      void queries.load({ formId: form.value.form_id });
    } else {
      queries.clear();
    }
  }
);

watch(
  [() => form.value?.form_id, () => props.highlightItemId],
  async ([loadedId, itemId]) => {
    if (!loadedId || !itemId) return;
    await nextTick();
    const row = document.getElementById(`ecrf-row-${itemId}`);
    row?.scrollIntoView?.({ block: "center" });
    const input = document.getElementById(
      `ecrf-item-${itemId}`
    ) as HTMLInputElement | null;
    if (input && !input.disabled) {
      input.focus?.();
    }
  },
  { immediate: true }
);

function liveOn(item: EcrfFormItem) {
  return form.value
    ? queries.forItem(form.value.form_id, item.item_id).filter(isLiveQuery)
    : [];
}

/** Starts a query on the item's recorded value or on an unasked blank item. */
function beginRaise(item: EcrfFormItem) {
  const unrecorded = !item.observation_id || isUnrecordedValue(item.value);
  raising.value = {
    item_id: item.item_id,
    observation_id: item.observation_id ?? null,
    revision: item.observation_id ? Number(item.revision) : null,
    unrecorded,
  };
  raiseText.value = "";
}

async function confirmRaise() {
  const target = raising.value;
  const text = raiseText.value.trim();
  if (!form.value || !target || !text) return;
  const wasUnaskedWithoutObservation = !target.observation_id;
  const raised = await queries.raise(
    {
      formId: form.value.form_id,
      itemId: target.item_id,
      observationId: target.observation_id,
      revision: target.revision,
    },
    text
  );
  if (raised) {
    raising.value = null;
    if (wasUnaskedWithoutObservation) {
      await reload();
    }
  }
}

/** The value moved on since the form was read: read the form and its queries again. */
async function reloadAfterStale() {
  queries.outcome = null;
  await reload();
  await queries.reload();
}

async function reloadAfterSdvStale() {
  pendingSdv.value = null;
  sdvOutcome.value = null;
  await reload();
  await queries.reload();
}

// Signing the form (#5463): the user chooses the meaning, the signature dialog verifies them again at the gateway for
// this form's signing URL, and the signature binds the form's content hash as it was read.
const signMeaning = ref<EcrfSignatureMeaning>("approved");
const showSignModal = ref(false);
const authStore = useAuthStore();

const ECRF_INVESTIGATOR_REASONS = [
  "Investigator Certification",
  "INVESTIGATOR_SIGNATURE",
  "APPROVAL",
  "CLINICAL_QC",
  "REVIEW",
];

const signerUsername = computed(() => authStore.identity?.username ?? "");
const signerFullName = computed(() => {
  const identity = authStore.identity;
  if (!identity) return "";
  const fullName = [identity.firstName, identity.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return fullName || identity.username || "";
});

const MEANING_LABELS: Record<string, string> = {
  approved: "Approved",
  responsibility: "Responsibility",
};

function meaningLabel(meaning: string): string {
  return MEANING_LABELS[meaning.toLowerCase()] ?? meaning;
}

/** Why the form cannot be signed now, or null when it can. */
const signHint = computed(() => {
  if (!form.value || signing.value) return null;
  if (dirty.value) return "Save or discard your changes before signing.";
  if (!form.value.content_hash) return "The form has no values to sign yet.";
  return null;
});

function currentOf(itemId: string): EcrfFormItem | undefined {
  return items.value.find((i) => i.item_id === itemId);
}

function currentValueText(itemId: string): string {
  const value = currentOf(itemId)?.value;
  return value ? formatObservationValue(value) : "No value";
}

function changedSince(signed: EcrfFormSignedValue): boolean {
  const current = currentOf(signed.item_id);
  return !current || Number(current.revision) !== Number(signed.revision);
}

/** The signature dialog's callback: it has the step-up token and the signing reason the user chose. */
async function signWithToken(sigToken: string, signingReason: string) {
  const result = await sign(signMeaning.value, sigToken, signingReason);
  if (!result || result.kind !== "signed") {
    throw new Error(result?.message ?? "The form could not be signed.");
  }
  return result;
}

async function submit() {
  await save(reason.value.trim());
  if (outcome.value?.kind === "saved") reason.value = "";
}

function closeForm() {
  if (
    dirty.value &&
    !window.confirm(
      "You have unsaved changes on this form. Discard them and close?"
    )
  ) {
    return;
  }
  clearDraft();
  close();
  emit("closed");
}

function formatUnsavedEntry(entry: EcrfUnsavedItem): string {
  if (entry.missing) {
    return formatObservationValue({
      type: "missing",
      reason: entry.missing as Extract<
        KernelObservationValue,
        { type?: "missing" }
      >["reason"],
    });
  }
  const typed = entry.text.trim();
  return entry.unit ? `${typed} ${entry.unit}` : typed;
}

onMounted(() => {
  if (props.formId) void load(props.formId);
});

watch(
  () => props.formId,
  (next, prev) => {
    if (next) {
      reuseId.value = next;
      if (form.value?.form_id !== next) {
        void load(next);
      }
    } else if (prev) {
      reuseId.value = "";
      dismiss();
    }
  }
);

defineExpose({ dirty, form });

// The view guards navigation while a page has unsaved entries.
watch(dirty, (value) => emit("dirty-change", value));
onUnmounted(() => {
  if (dirty.value) emit("dirty-change", false);
});
</script>

<style scoped>
.ecrf-form-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 20px;
  min-width: 0;
  max-width: 100%;
}
.ecrf-form-hint {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}
.ecrf-form-row {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
  margin: 8px 0;
}
.ecrf-form-grid {
  display: grid;
  grid-template-columns: minmax(140px, max-content) 1fr;
  gap: 6px 12px;
  align-items: center;
}
.ecrf-form-grid button {
  grid-column: 2;
  justify-self: start;
}
.ecrf-form-summary {
  display: grid;
  grid-template-columns: max-content 1fr;
  gap: 4px 12px;
  font-size: 0.85rem;
}
.ecrf-form-summary dt {
  font-weight: 600;
}
.ecrf-table-scroll {
  overflow-x: auto;
  max-width: 100%;
  padding: 2px;
}
.ecrf-table-scroll:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
  border-radius: 4px;
}
.ecrf-form-items {
  width: 100%;
  min-width: 52rem;
  border-collapse: collapse;
  font-size: 0.85rem;
}
.ecrf-form-items th,
.ecrf-form-items td {
  border-bottom: 1px solid var(--border);
  padding: 6px;
  text-align: left;
  vertical-align: top;
}
.ecrf-form-items > thead > tr > th:nth-child(1),
.ecrf-form-items > tbody > tr > th:not([colspan]):nth-child(1) {
  min-width: 7.5rem;
}
.ecrf-form-items > thead > tr > th:nth-child(2),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(2) {
  min-width: 11rem;
}
.ecrf-form-items > thead > tr > th:nth-child(3),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(3) {
  min-width: 9.5rem;
}
.ecrf-form-items > thead > tr > th:nth-child(4),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(4) {
  min-width: 8rem;
}
.ecrf-form-items > thead > tr > th:nth-child(5),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(5) {
  min-width: 8.5rem;
}
.ecrf-form-items > thead > tr > th:nth-child(6),
.ecrf-form-items > tbody > tr > td:not([colspan]):nth-child(6) {
  min-width: 7.5rem;
}
.ecrf-required {
  color: var(--semantic-color-error);
  text-decoration: none;
  margin-left: 2px;
}
.ecrf-item-concept,
.ecrf-item-revision {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}
.ecrf-item-unit {
  margin-left: 4px;
  color: var(--semantic-color-text-secondary);
}
.ecrf-item-queries {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
}
.ecrf-query-badge {
  padding: 0 8px;
  border-radius: 999px;
  background-color: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-dark);
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-query-changed {
  color: var(--semantic-color-interactive-primary);
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-item-edited {
  background-color: var(--semantic-color-warning-bg-soft);
}
.ecrf-item-highlighted {
  background-color: var(--semantic-color-info-bg);
  outline: 2px solid var(--semantic-color-interactive-primary);
}
.ecrf-item-sdv {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
}
.ecrf-sdv-actions {
  display: flex;
  align-items: center;
  gap: 6px;
}
.ecrf-sdv-badge {
  padding: 0 8px;
  border-radius: 999px;
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-sdv-badge--verified {
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.ecrf-sdv-badge--reverify {
  background-color: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-dark);
}
.ecrf-sdv-badge--unverified {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}
.ecrf-sdv-none {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}
.ecrf-bulk-sdv {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
}
.ecrf-bulk-sdv-results {
  margin: 6px 0 0;
  padding-left: 18px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.ecrf-bulk-sdv-result--ok {
  color: var(--semantic-color-success-dark);
}
.ecrf-bulk-sdv-result--failed {
  color: var(--semantic-color-error-text-dark);
}
.ecrf-form-error {
  color: var(--semantic-color-error);
  font-size: 0.8rem;
  margin: 2px 0 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}
.ecrf-form-complete {
  color: var(--semantic-color-success-dark);
  font-weight: 600;
}
.ecrf-form-incomplete {
  color: var(--semantic-color-warning-dark);
}
.ecrf-form-locks {
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
}
.ecrf-form-outcome {
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}
.ecrf-locked-unsaved {
  flex-basis: 100%;
  margin-top: 4px;
}
.ecrf-locked-unsaved ul {
  margin: 4px 0 0;
  padding-left: 18px;
}
.ecrf-signatures {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 8px;
  font-size: 0.85rem;
}
.ecrf-signature {
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 8px 12px;
}
.ecrf-signature--superseded {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
}
.ecrf-signature-state {
  margin-left: 8px;
  padding: 0 8px;
  border-radius: 999px;
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  font-size: 0.75rem;
  font-weight: 600;
}
.ecrf-signature-state--superseded {
  background-color: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-dark);
}
.ecrf-signed-values {
  margin-top: 6px;
  border-collapse: collapse;
  font-size: 0.8rem;
}
.ecrf-signed-values caption {
  text-align: left;
  font-weight: 600;
  padding-bottom: 4px;
}
.ecrf-signed-values th,
.ecrf-signed-values td {
  border-bottom: 1px solid var(--border);
  padding: 4px 6px;
  text-align: left;
}
.ecrf-signed-changed {
  background-color: var(--semantic-color-warning-bg-soft);
}
.ecrf-form-outcome--saved {
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}
.ecrf-form-outcome--stale,
.ecrf-form-outcome--invalid,
.ecrf-form-outcome--locked,
.ecrf-form-outcome--failed {
  background-color: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error-text-dark);
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}
</style>
