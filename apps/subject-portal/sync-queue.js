export function openDatabase() {
  return new Promise((resolve, reject) => {
    const request = indexedDB.open("SubjectPortalSyncDB", 1);
    request.onupgradeneeded = (event) => {
      const db = event.target.result;
      if (!db.objectStoreNames.contains("submissions")) {
        db.createObjectStore("submissions", { keyPath: "sequence_number" });
      }
      if (!db.objectStoreNames.contains("config")) {
        db.createObjectStore("config", { keyPath: "key" });
      }
    };
    request.onsuccess = (event) => {
      resolve(event.target.result);
    };
    request.onerror = (event) => {
      reject(event.target.error);
    };
  });
}

export async function getClientId() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const transaction = db.transaction("config", "readonly");
    const store = transaction.objectStore("config");
    const request = store.get("client_id");
    request.onsuccess = () => {
      if (request.result) {
        resolve(request.result.value);
      } else {
        const newId =
          typeof crypto !== "undefined" && crypto.randomUUID
            ? crypto.randomUUID()
            : "client-" +
              Math.random().toString(36).substring(2, 15) +
              "-" +
              Date.now();

        const writeTx = db.transaction("config", "readwrite");
        const writeStore = writeTx.objectStore("config");
        writeStore.put({ key: "client_id", value: newId });
        writeTx.oncomplete = () => {
          resolve(newId);
        };
        writeTx.onerror = () => {
          reject(writeTx.error);
        };
      }
    };
    request.onerror = () => {
      reject(request.error);
    };
  });
}

export async function getNextSequenceNumber() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readonly");
    const store = tx.objectStore("submissions");
    const req = store.openCursor(null, "prev");
    req.onsuccess = (event) => {
      const cursor = event.target.result;
      if (cursor) {
        resolve(cursor.key + 1);
      } else {
        resolve(1);
      }
    };
    req.onerror = () => {
      reject(req.error);
    };
  });
}

export async function queueSubmission({
  subject_id,
  diary_id,
  assignment_id,
  answers,
  change_reason,
  username,
}) {
  const db = await openDatabase();
  const sequence_number = await getNextSequenceNumber();
  const client_id = await getClientId();
  const device_timestamp = new Date().toISOString();

  const submission = {
    sequence_number,
    client_id,
    subject_id,
    diary_id,
    assignment_id,
    device_timestamp,
    answers,
    change_reason,
    username,
    status: "QUEUED",
    resolved_answers: null,
    resolved_at: null,
    error: null,
  };

  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readwrite");
    const store = tx.objectStore("submissions");
    store.put(submission);
    tx.oncomplete = () => {
      resolve(submission);
    };
    tx.onerror = () => {
      reject(tx.error);
    };
  });
}

export async function getQueuedSubmissions() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readonly");
    const store = tx.objectStore("submissions");
    const req = store.getAll();
    req.onsuccess = () => {
      const all = req.result || [];
      const queued = all.filter((s) => s.status === "QUEUED");
      queued.sort((a, b) => a.sequence_number - b.sequence_number);
      resolve(queued);
    };
    req.onerror = () => {
      reject(req.error);
    };
  });
}

export async function getAllSubmissions() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readonly");
    const store = tx.objectStore("submissions");
    const req = store.getAll();
    req.onsuccess = () => {
      const all = req.result || [];
      all.sort((a, b) => b.sequence_number - a.sequence_number);
      resolve(all);
    };
    req.onerror = () => {
      reject(req.error);
    };
  });
}

export async function updateSubmissionStatus(
  sequence_number,
  status,
  additionalFields = {}
) {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readwrite");
    const store = tx.objectStore("submissions");
    const getReq = store.get(sequence_number);
    getReq.onsuccess = () => {
      const sub = getReq.result;
      if (!sub) {
        reject(new Error(`Submission ${sequence_number} not found`));
        return;
      }
      sub.status = status;
      sub.resolved_at = new Date().toISOString();
      Object.assign(sub, additionalFields);
      const putReq = store.put(sub);
      putReq.onsuccess = () => {
        resolve(sub);
      };
      putReq.onerror = () => {
        reject(putReq.error);
      };
    };
    getReq.onerror = () => {
      reject(getReq.error);
    };
  });
}

export async function clearAllSubmissions() {
  const db = await openDatabase();
  return new Promise((resolve, reject) => {
    const tx = db.transaction("submissions", "readwrite");
    const store = tx.objectStore("submissions");
    store.clear();
    tx.oncomplete = () => {
      resolve();
    };
    tx.onerror = () => {
      reject(tx.error);
    };
  });
}
