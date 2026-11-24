let activePeekHandler = null;

export function registerEntityPeekHandler(handler) {
  activePeekHandler = typeof handler === "function" ? handler : null;
  return () => {
    if (activePeekHandler === handler) {
      activePeekHandler = null;
    }
  };
}

export function invokeEntityPeekHandler(payload) {
  if (typeof activePeekHandler === "function") {
    activePeekHandler(payload);
    return true;
  }
  return false;
}
