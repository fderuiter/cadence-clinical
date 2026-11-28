import { etmfService } from "../../../api/etmf";
import { arrayBufferToBase64 } from "../../../stores/etmf";
import { useToastStore } from "../../../stores/toasts.js";

export async function executeCommitTaxonomy({
  doc,
  targetCode,
  etmfStore,
  getZoneAndSectionFromCode,
  cancelEditing,
  exitEditing,
}) {
  const selectedNode = etmfStore.folderLookup[targetCode];
  if (!selectedNode) {
    cancelEditing();
    return;
  }
  const { zone, section } = getZoneAndSectionFromCode(targetCode);
  try {
    const updated = await etmfService.tagDocument(
      doc.id,
      { zone, section, artifact_code: targetCode },
      {
        changeReason:
          "Corrected taxonomy classification via interactive grid navigation",
      }
    );
    const serverFields =
      updated && typeof updated === "object" && "id" in updated ? updated : {};
    if (typeof etmfStore.reconcileDocument === "function") {
      etmfStore.reconcileDocument(
        {
          ...serverFields,
          zone: serverFields.zone ?? zone,
          section: serverFields.section ?? section,
          artifact_code: serverFields.artifact_code ?? targetCode,
          artifact_type: serverFields.artifact_type ?? selectedNode.name,
        },
        { fallbackId: doc.id, insertIfMissing: false }
      );
    }
  } catch (err) {
    useToastStore().apiError("Failed to update document taxonomy", err);
  } finally {
    exitEditing();
  }
}

export async function executeExportSelectedDocuments(etmfStore) {
  if (etmfStore.selectedDocumentIds.length === 0) return;
  try {
    await etmfStore.triggerExport({
      document_ids: [...etmfStore.selectedDocumentIds],
      is_full_binder: false,
    });
  } catch (err) {
    useToastStore().apiError("Export failed", err);
  }
}

export function executeCopyCellValue(colIndex, doc, formatStatus) {
  let val;
  switch (colIndex) {
    case 0:
      val = doc.filename || "";
      break;
    case 1:
      val = `Z${doc.zone} - S${doc.section} [${doc.artifact_code}]`;
      break;
    case 2:
      val = `v${doc.version_index}.0`;
      break;
    case 3:
      val = formatStatus(doc.status);
      break;
    case 4:
      val = doc.created_by || "";
      break;
    case 5:
      val = doc.formattedCreatedAt || "";
      break;
    case 6:
      val = "Preview";
      break;
    default:
      return;
  }

  if (
    navigator.clipboard &&
    typeof navigator.clipboard.writeText === "function"
  ) {
    navigator.clipboard.writeText(val).catch((err) => {
      console.error("Clipboard copy failed:", err);
    });
  } else {
    const textarea = document.createElement("textarea");
    textarea.value = val;
    textarea.style.position = "fixed";
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();
    try {
      document.execCommand("copy");
    } catch (err) {
      console.error("Fallback copy failed:", err);
    }
    document.body.removeChild(textarea);
  }
}

export async function executeSubmitDocumentUpload({
  file,
  studyId,
  siteId,
  artifactCode,
  artifactType,
  reasonForChange,
  etmfStore,
  getZoneAndSectionFromCode,
  closeUploadModal,
  uploadError,
}) {
  try {
    const fileContent = await new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = (e) => {
        const text = e.target?.result;
        if (text) {
          resolve(text);
        } else {
          reject(
            new Error(`"${file.name}" is empty; no document content to file.`)
          );
        }
      };
      reader.onerror = () => {
        reject(
          new Error(
            `"${file.name}" could not be read: ${reader.error?.message || "unknown read error"}`
          )
        );
      };
      reader.readAsText(file.slice(0, 65536));
    });

    let rawBuffer = null;
    if (typeof file.arrayBuffer === "function") {
      try {
        rawBuffer = await file.arrayBuffer();
      } catch {
        rawBuffer = null;
      }
    }

    const { zone: zoneInt, section: sectionStr } =
      getZoneAndSectionFromCode(artifactCode);

    const formData = new FormData();
    formData.append("study_id", studyId || etmfStore.currentStudyId || "");
    formData.append("site_id", siteId || etmfStore.currentSiteId || "");
    formData.append("artifact_type", artifactType);
    formData.append("filename", file.name);
    formData.append(
      "content",
      `Document content index for: ${file.name}. Raw plaintext: ${fileContent}`
    );
    if (rawBuffer && rawBuffer.byteLength > 0) {
      formData.append("content_base64", arrayBufferToBase64(rawBuffer));
    }
    formData.append("file", file);
    formData.append("mime_type", file.type || "application/pdf");
    formData.append("artifact_code", artifactCode);
    formData.append("zone", String(zoneInt));
    formData.append("section", sectionStr);
    formData.append("reason_for_change", reasonForChange);

    await etmfStore.uploadDocument(formData);
    closeUploadModal();
  } catch (err) {
    useToastStore().apiError("eTMF document upload failed", err);
    uploadError.value =
      err.message ||
      "Ingestion transaction rejected. Verify site/study lock status.";
  }
}
