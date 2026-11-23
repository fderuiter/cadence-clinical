import {
  createPinia,
  defineStore,
  getActivePinia,
  setActivePinia,
} from "pinia";

let nextRequestId = 1;

const rawUseConfirmStore = defineStore("confirm", {
  state: () => ({
    activeRequest: null,
    queue: [],
  }),
  actions: {
    _enqueueOrActivate(entry) {
      if (this.activeRequest) {
        this.queue.push(entry);
      } else {
        this.activeRequest = entry;
      }
    },
    _advanceQueue() {
      this.activeRequest = this.queue.length > 0 ? this.queue.shift() : null;
    },
    confirm(options = {}) {
      if (typeof options === "string") {
        options = { message: options };
      }
      return new Promise((resolve) => {
        const normalizedOptions = {
          id: nextRequestId++,
          mode: "confirm",
          title: options.title || "Confirm Action",
          message: options.message || options.text || "",
          confirmLabel:
            options.confirmLabel || options.confirmText || "Confirm",
          cancelLabel: options.cancelLabel || options.cancelText || "Cancel",
          variant: options.variant || options.tone || "info",
          size: options.size || "md",
        };

        this._enqueueOrActivate({
          ...normalizedOptions,
          resolve,
        });
      });
    },
    prompt(options = {}, defaultValue = "") {
      if (typeof options === "string") {
        options = { message: options, defaultValue };
      }
      return new Promise((resolve) => {
        const inputLabel =
          options.inputLabel ||
          options.label ||
          options.message ||
          options.title ||
          "Value";
        const initialVal =
          options.defaultValue ?? options.initialValue ?? defaultValue ?? "";

        const normalizedOptions = {
          id: nextRequestId++,
          mode: "prompt",
          title: options.title || "Enter Value",
          message: options.message || options.text || "",
          inputLabel,
          placeholder: options.placeholder || options.inputPlaceholder || "",
          inputValue: String(initialVal),
          confirmLabel: options.confirmLabel || options.confirmText || "Submit",
          cancelLabel: options.cancelLabel || options.cancelText || "Cancel",
          variant: options.variant || options.tone || "info",
          size: options.size || "md",
          required: options.required !== false,
          requiredMessage: options.requiredMessage || "",
          validate:
            typeof options.validate === "function" ? options.validate : null,
          validationError: "",
        };

        this._enqueueOrActivate({
          ...normalizedOptions,
          resolve,
        });
      });
    },
    setInputValue(value) {
      if (this.activeRequest && this.activeRequest.mode === "prompt") {
        this.activeRequest.inputValue = String(value ?? "");
        if (this.activeRequest.validationError) {
          this.activeRequest.validationError = "";
        }
      }
    },
    open(options) {
      return options?.mode === "prompt"
        ? this.prompt(options)
        : this.confirm(options);
    },
    resolveConfirm(submittedValue) {
      if (!this.activeRequest) {
        return false;
      }

      if (this.activeRequest.mode === "prompt") {
        const rawValue =
          submittedValue !== undefined
            ? String(submittedValue)
            : String(this.activeRequest.inputValue ?? "");
        const trimmed = rawValue.trim();

        if (this.activeRequest.required && !trimmed) {
          this.activeRequest.validationError =
            this.activeRequest.requiredMessage ||
            `${this.activeRequest.inputLabel} is required.`;
          return false;
        }

        if (this.activeRequest.validate) {
          const validationResult = this.activeRequest.validate(trimmed);
          if (
            validationResult === false ||
            (typeof validationResult === "string" &&
              validationResult.trim().length > 0)
          ) {
            this.activeRequest.validationError =
              typeof validationResult === "string"
                ? validationResult
                : "Please enter a valid value.";
            return false;
          }
        }

        const resolve = this.activeRequest.resolve;
        this._advanceQueue();
        if (resolve) resolve(trimmed);
        return true;
      }

      const resolve = this.activeRequest.resolve;
      this._advanceQueue();
      if (resolve) resolve(true);
      return true;
    },
    cancelConfirm() {
      if (this.activeRequest) {
        const { resolve, mode } = this.activeRequest;
        this._advanceQueue();
        if (resolve) resolve(mode === "prompt" ? null : false);
      }
    },
  },
});

/** @type {typeof rawUseConfirmStore} */
export const useConfirmStore = Object.assign((pinia) => {
  if (!pinia && !getActivePinia()) {
    setActivePinia(createPinia());
  }
  return rawUseConfirmStore(pinia);
}, rawUseConfirmStore);
