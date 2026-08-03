import { onMounted, onUnmounted } from "vue";

export function useEscapeClose(onCloseCallback) {
  const handleKeyDown = (e) => {
    if (e.key === "Escape") {
      if (typeof onCloseCallback === "function") {
        onCloseCallback();
      }
    }
  };

  onMounted(() => {
    document.addEventListener("keydown", handleKeyDown);
  });

  onUnmounted(() => {
    document.removeEventListener("keydown", handleKeyDown);
  });
}
