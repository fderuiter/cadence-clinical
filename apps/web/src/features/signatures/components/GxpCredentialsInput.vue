<template>
  <div class="gxp-credentials-input">
    <!-- Username Field -->
    <div v-if="showUsername" class="form-group" :style="groupStyle">
      <label :for="usernameId" :style="labelStyle">{{ usernameLabel }}</label>
      <input
        :id="usernameId"
        :value="username"
        type="text"
        :placeholder="usernamePlaceholder"
        :disabled="disabled"
        :class="usernameClass"
        :style="inputStyle"
        @input="$emit('update:username', $event.target.value)"
      />
    </div>

    <!-- Password Field -->
    <div class="form-group" :style="groupStyle">
      <label :for="passwordId" :style="labelStyle">{{ passwordLabel }}</label>
      <input
        :id="passwordId"
        :value="password"
        v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
        :placeholder="passwordPlaceholder"
        :disabled="disabled"
        :class="passwordClass"
        :style="inputStyle"
        :required="passwordRequired"
        :aria-invalid="passwordReentryRequired ? 'true' : undefined"
        :aria-describedby="
          passwordReentryRequired ? `${passwordId}-reentry-cue` : undefined
        "
        @input="$emit('update:password', $event.target.value)"
        @keyup.enter="$emit('keyup-enter')"
      />
      <p
        v-if="passwordReentryRequired"
        :id="`${passwordId}-reentry-cue`"
        data-testid="sig-password-reentry-cue"
        class="password-reentry-cue"
        role="status"
        aria-live="polite"
      >
        {{ passwordReentryMessage }}
      </p>
    </div>

    <!-- TOTP Field -->
    <div v-if="showTotp" class="form-group" :style="groupStyle">
      <label :for="totpId" :style="labelStyle">{{ totpLabel }}</label>
      <input
        :id="totpId"
        :value="totp"
        type="text"
        :placeholder="totpPlaceholder"
        :disabled="disabled"
        :class="totpClass"
        :style="inputStyle"
        @input="$emit('update:totp', $event.target.value)"
      />
    </div>
  </div>
</template>

<script setup>
import { SIGNATURE_PASSWORD_INPUT_ATTRS } from "ui";

defineProps({
  username: {
    type: String,
    default: "",
  },
  password: {
    type: String,
    default: "",
  },
  totp: {
    type: String,
    default: "",
  },
  showUsername: {
    type: Boolean,
    default: true,
  },
  showTotp: {
    type: Boolean,
    default: true,
  },
  usernameLabel: {
    type: String,
    default: "Username",
  },
  passwordLabel: {
    type: String,
    default: "Password",
  },
  totpLabel: {
    type: String,
    default: "MFA/TOTP Token (Optional)",
  },
  usernamePlaceholder: {
    type: String,
    default: "Username",
  },
  passwordPlaceholder: {
    type: String,
    default: "Enter your password to confirm identity...",
  },
  totpPlaceholder: {
    type: String,
    default: "Enter 6-digit TOTP code...",
  },
  usernameId: {
    type: String,
    default: "sig-username",
  },
  passwordId: {
    type: String,
    default: "sig-password",
  },
  totpId: {
    type: String,
    default: "sig-totp",
  },
  usernameClass: {
    type: String,
    default: "",
  },
  passwordClass: {
    type: String,
    default: "password-input",
  },
  totpClass: {
    type: String,
    default: "totp-input",
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  passwordRequired: {
    type: Boolean,
    default: false,
  },
  passwordReentryRequired: {
    type: Boolean,
    default: false,
  },
  passwordReentryMessage: {
    type: String,
    default:
      "For 21 CFR Part 11 security, your password was cleared. Re-enter your password to retry.",
  },
  groupStyle: {
    type: [Object, String],
    default: () => ({}),
  },
  labelStyle: {
    type: [Object, String],
    default: () => ({}),
  },
  inputStyle: {
    type: [Object, String],
    default: () => ({}),
  },
});

defineEmits([
  "update:username",
  "update:password",
  "update:totp",
  "keyup-enter",
]);
</script>

<style scoped>
.form-group {
  margin-bottom: 12px;
}

label {
  display: block;
  font-weight: 500;
  margin-bottom: 4px;
  font-size: 13px;
  color: var(--semantic-color-text-secondary);
}

input {
  width: 100%;
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 13px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  box-sizing: border-box;
}

input:focus:not(:focus-visible) {
  outline: none;
}

input:focus-visible {
  outline: 3px solid var(--accent) !important;
  outline-offset: 2px !important;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid) !important;
}

.password-reentry-cue {
  margin: 6px 0 0;
  padding: 6px 10px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-orange-border);
  background: var(--semantic-color-orange-bg);
  color: var(--semantic-color-orange-text);
  font-size: 12px;
  font-weight: 500;
  line-height: 1.4;
}

@media (max-width: 1024px) {
  input {
    min-height: 48px;
    padding: 10px 14px;
    font-size: 16px;
  }
}
</style>
