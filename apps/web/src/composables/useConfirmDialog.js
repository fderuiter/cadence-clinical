import { useConfirmStore } from "../stores/confirm.js";

export function confirmDialog(options) {
  const store = useConfirmStore();
  return store.confirm(options);
}

export function useConfirmDialog() {
  const store = useConfirmStore();

  const confirm = (options) => store.confirm(options);
  const confirmDlg = (options) => store.confirm(options);

  return {
    confirm,
    confirmDialog: confirmDlg,
    cancelConfirm: () => store.cancelConfirm(),
    resolveConfirm: () => store.resolveConfirm(),
  };
}

export default useConfirmDialog;
