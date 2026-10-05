import { defineStore } from "pinia";

export const useConfirmStore = defineStore("confirm", {
  state: () => ({
    activeRequest: null,
  }),
  actions: {
    confirm(options = {}) {
      if (typeof options === "string") {
        options = { message: options };
      }
      return new Promise((resolve) => {
        if (this.activeRequest) {
          this.cancelConfirm();
        }

        const normalizedOptions = {
          title: options.title || "Confirm Action",
          message: options.message || options.text || "",
          confirmLabel:
            options.confirmLabel || options.confirmText || "Confirm",
          cancelLabel: options.cancelLabel || options.cancelText || "Cancel",
          variant: options.variant || options.tone || "danger",
          size: options.size || "md",
        };

        this.activeRequest = {
          ...normalizedOptions,
          resolve,
        };
      });
    },
    open(options) {
      return this.confirm(options);
    },
    resolveConfirm() {
      if (this.activeRequest) {
        const resolve = this.activeRequest.resolve;
        this.activeRequest = null;
        if (resolve) resolve(true);
      }
    },
    cancelConfirm() {
      if (this.activeRequest) {
        const resolve = this.activeRequest.resolve;
        this.activeRequest = null;
        if (resolve) resolve(false);
      }
    },
  },
});
