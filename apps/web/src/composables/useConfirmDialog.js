import { useConfirmStore } from "../stores/confirm.js";

export function confirmDialog(options) {
  const store = useConfirmStore();
  return store.confirm(options);
}

export function promptDialog(options, defaultValue = "") {
  const store = useConfirmStore();
  return store.prompt(options, defaultValue);
}

export function useConfirmDialog() {
  const confirm = (options) => useConfirmStore().confirm(options);
  const confirmDlg = (options) => useConfirmStore().confirm(options);
  const prompt = (options, defaultValue = "") =>
    useConfirmStore().prompt(options, defaultValue);
  const promptDlg = (options, defaultValue = "") =>
    useConfirmStore().prompt(options, defaultValue);

  return {
    confirm,
    confirmDialog: confirmDlg,
    prompt,
    promptDialog: promptDlg,
    cancelConfirm: () => useConfirmStore().cancelConfirm(),
    resolveConfirm: (value) => useConfirmStore().resolveConfirm(value),
  };
}

export default useConfirmDialog;
