let hasPotentialUnsyncedLocalRecords = false;

export function hasTrackedLocalQueueActivity() {
  return hasPotentialUnsyncedLocalRecords;
}

export function setTrackedLocalQueueActivity(active) {
  hasPotentialUnsyncedLocalRecords = Boolean(active);
}
