import { DEMO_SITES, DEMO_SPONSOR, DEMO_STUDY } from "demo-fixtures";

const CADENCE_101_UUID =
  DEMO_STUDY.uuid || "00000000-0000-0000-0000-000000000101"; // deid-ignore
const SPONSOR_CADENCE_UUID =
  DEMO_SPONSOR.organizationUuid || "00000000-0000-0000-0000-000000000301"; // deid-ignore
const SPONSOR_METRO_UUID = "00000000-0000-0000-0000-000000000302"; // deid-ignore

const MAX_SYNC_IMPORT_BYTES = 10 * 1024 * 1024;

export const DEMO_ORGANIZATIONS = [
  {
    ref: `organization:${SPONSOR_CADENCE_UUID}@1`,
    code: DEMO_SPONSOR.code || "CADENCE-PHARMA",
    name: DEMO_SPONSOR.name || "Cadence Pharma (demo sponsor)",
    kind: "Sponsor",
  },
  {
    ref: `organization:${SPONSOR_METRO_UUID}@1`,
    code: "METRO-CRO",
    name: "Metropolitan Clinical Research Organization",
    kind: "Cro",
  },
];

export const DEMO_PROTOCOL_TEMPLATES = [
  {
    id: "vital-signs",
    category: "Findings",
    form_oid: "VS",
    form_name: "Vital Signs",
    file_name: "vital-signs.xml",
    item_count: 6,
    items: [
      {
        id: "VSPERF",
        display_order: 1,
        prompt: "Were vital signs assessed?",
        concept: "concept:00000000-0000-0000-0000-000000006001@1", // deid-ignore
        required: true,
        value_kind: "Coded",
        unit: null,
      },
      {
        id: "VSDAT",
        display_order: 2,
        prompt: "Assessment Date",
        concept: "concept:00000000-0000-0000-0000-000000006002@1", // deid-ignore
        required: true,
        value_kind: "DateTime",
        unit: null,
      },
      {
        id: "SYSBP",
        display_order: 3,
        prompt: "Systolic Blood Pressure",
        concept: "concept:00000000-0000-0000-0000-000000006003@1", // deid-ignore
        required: true,
        value_kind: "Quantity",
        unit: "mm[Hg]",
      },
      {
        id: "DIABP",
        display_order: 4,
        prompt: "Diastolic Blood Pressure",
        concept: "concept:00000000-0000-0000-0000-000000006004@1", // deid-ignore
        required: true,
        value_kind: "Quantity",
        unit: "mm[Hg]",
      },
      {
        id: "PULSE",
        display_order: 5,
        prompt: "Pulse Rate",
        concept: "concept:00000000-0000-0000-0000-000000006005@1", // deid-ignore
        required: false,
        value_kind: "Quantity",
        unit: "/min",
      },
      {
        id: "TEMP",
        display_order: 6,
        prompt: "Body Temperature",
        concept: "concept:00000000-0000-0000-0000-000000006006@1", // deid-ignore
        required: false,
        value_kind: "Quantity",
        unit: "Cel",
      },
    ],
  },
  {
    id: "demographics",
    category: "Special Purpose",
    form_oid: "DM",
    form_name: "Demographics",
    file_name: "demographics.xml",
    item_count: 4,
    items: [
      {
        id: "BRTHDAT",
        display_order: 1,
        prompt: "Date of Birth",
        concept: "concept:00000000-0000-0000-0000-000000006011@1", // deid-ignore
        required: true,
        value_kind: "DateTime",
        unit: null,
      },
      {
        id: "SEX",
        display_order: 2,
        prompt: "Sex",
        concept: "concept:00000000-0000-0000-0000-000000006012@1", // deid-ignore
        required: true,
        value_kind: "Coded",
        unit: null,
      },
      {
        id: "ETHNIC",
        display_order: 3,
        prompt: "Ethnicity",
        concept: "concept:00000000-0000-0000-0000-000000006013@1", // deid-ignore
        required: false,
        value_kind: "Coded",
        unit: null,
      },
      {
        id: "RACE",
        display_order: 4,
        prompt: "Race",
        concept: "concept:00000000-0000-0000-0000-000000006014@1", // deid-ignore
        required: false,
        value_kind: "Coded",
        unit: null,
      },
    ],
  },
  {
    id: "adverse-events",
    category: "Events",
    form_oid: "AE",
    form_name: "Adverse Events",
    file_name: "adverse-events.xml",
    item_count: 4,
    items: [
      {
        id: "AETERM",
        display_order: 1,
        prompt: "What is the adverse event term?",
        concept: "concept:00000000-0000-0000-0000-000000006021@1", // deid-ignore
        required: true,
        value_kind: "Text",
        unit: null,
      },
      {
        id: "AESTDAT",
        display_order: 2,
        prompt: "Start Date",
        concept: "concept:00000000-0000-0000-0000-000000006022@1", // deid-ignore
        required: true,
        value_kind: "DateTime",
        unit: null,
      },
      {
        id: "AESEV",
        display_order: 3,
        prompt: "Severity/Intensity",
        concept: "concept:00000000-0000-0000-0000-000000006023@1", // deid-ignore
        required: true,
        value_kind: "Coded",
        unit: null,
      },
      {
        id: "AESER",
        display_order: 4,
        prompt: "Is the event serious?",
        concept: "concept:00000000-0000-0000-0000-000000006024@1", // deid-ignore
        required: true,
        value_kind: "Boolean",
        unit: null,
      },
    ],
  },
  {
    id: "electrocardiogram",
    category: "Findings",
    form_oid: "EG",
    form_name: "12-Lead Electrocardiogram",
    file_name: "electrocardiogram.xml",
    item_count: 3,
    items: [
      {
        id: "EGPERF",
        display_order: 1,
        prompt: "Was the ECG performed?",
        concept: "concept:00000000-0000-0000-0000-000000006031@1", // deid-ignore
        required: true,
        value_kind: "Boolean",
        unit: null,
      },
      {
        id: "EGHRMN",
        display_order: 2,
        prompt: "Mean Heart Rate",
        concept: "concept:00000000-0000-0000-0000-000000006032@1", // deid-ignore
        required: true,
        value_kind: "Quantity",
        unit: "/min",
      },
      {
        id: "EGQTCF",
        display_order: 3,
        prompt: "QTcF Interval",
        concept: "concept:00000000-0000-0000-0000-000000006033@1", // deid-ignore
        required: false,
        value_kind: "Quantity",
        unit: "ms",
      },
    ],
  },
];

const KNOWN_CONCEPT_CODES = new Set([
  "C25298",
  "C25299",
  "C49676",
  "C49677",
  "C49678",
  "C174447",
  "C25164",
  "C28421",
]);

function mapOdmDataType(rawType) {
  const normalized = String(rawType || "text").toLowerCase();
  if (
    normalized === "integer" ||
    normalized === "float" ||
    normalized === "double"
  ) {
    return "Quantity";
  }
  if (
    normalized === "date" ||
    normalized === "datetime" ||
    normalized === "time" ||
    normalized === "partialdate"
  ) {
    return "DateTime";
  }
  if (normalized === "boolean") {
    return "Boolean";
  }
  return "Text";
}

function makeConceptRef(seedIndex) {
  const suffix = String(7000 + seedIndex)
    .padStart(12, "0")
    .slice(-12);
  return `concept:00000000-0000-4000-8000-${suffix}@1`; // deid-ignore
}

function parseOdmXmlText(xmlText) {
  if (/<!DOCTYPE\b|<!ENTITY\b/i.test(xmlText)) {
    return {
      error: {
        status: 422,
        problem: {
          title: "Invalid protocol import payload",
          status: 422,
          code: "Kernel.Protocol.Import.InvalidFormat",
          detail:
            "External XML DTD and entity declarations (<!DOCTYPE> / <!ENTITY>) are prohibited.",
        },
      },
    };
  }

  const parser = new DOMParser();
  const doc = parser.parseFromString(xmlText, "application/xml");
  const parserError = doc.querySelector("parsererror");
  if (parserError || !doc.documentElement) {
    return {
      error: {
        status: 422,
        problem: {
          title: "Malformed ODM v1.3.2 XML",
          status: 422,
          code: "Kernel.Protocol.Import.MalformedXml",
          detail:
            "Uploaded XML could not be parsed as valid ODM v1.3.2 XML. Check element nesting and closing tags.",
        },
      },
    };
  }

  if (doc.documentElement.localName !== "ODM") {
    return {
      error: {
        status: 422,
        problem: {
          title: "Invalid ODM root element",
          status: 422,
          code: "Kernel.Protocol.Import.InvalidOdmRoot",
          detail: `Expected root <ODM> element, found <${doc.documentElement.localName}>.`,
        },
      },
    };
  }

  const byLocalName = (parent, name) =>
    Array.from(parent.getElementsByTagName("*")).filter(
      (el) => el.localName === name
    );
  const directChildren = (parent, name) =>
    Array.from(parent.children || []).filter((el) => el.localName === name);

  const importNotes = [];
  for (const attr of Array.from(doc.documentElement.attributes || [])) {
    if (
      attr.name.includes(":") &&
      !attr.name.startsWith("xmlns") &&
      !attr.name.startsWith("xsi:")
    ) {
      importNotes.push({
        source: "ODM",
        message: `Optional vendor attribute '${attr.name}' preserved as import note.`,
      });
    }
  }

  const metaData = byLocalName(doc, "MetaDataVersion")[0];
  if (!metaData) {
    return {
      error: {
        status: 422,
        problem: {
          title: "Missing MetaDataVersion",
          status: 422,
          code: "Kernel.Protocol.Import.MissingMetaDataVersion",
          detail:
            "ODM v1.3.2 XML must contain a <Study><MetaDataVersion> element with form definitions.",
        },
      },
    };
  }

  const studyNameEl = byLocalName(doc, "StudyName")[0];
  const title =
    (studyNameEl?.textContent || "").trim() ||
    metaData.getAttribute("Name") ||
    "Imported ODM Protocol Draft";

  const itemGroups = new Map();
  for (const ig of directChildren(metaData, "ItemGroupDef")) {
    itemGroups.set(ig.getAttribute("OID") || "", ig);
  }

  const itemDefs = new Map();
  for (const itemDef of directChildren(metaData, "ItemDef")) {
    itemDefs.set(itemDef.getAttribute("OID") || "", itemDef);
  }

  let conceptSeed = 1;
  const forms = [];
  const formDefs = directChildren(metaData, "FormDef");
  for (const formDef of formDefs) {
    const formOid = formDef.getAttribute("OID") || `FORM_${forms.length + 1}`;
    const formName = formDef.getAttribute("Name") || formOid;

    for (const attr of Array.from(formDef.attributes || [])) {
      if (attr.name.includes(":") && !attr.name.startsWith("xmlns")) {
        importNotes.push({
          source: formOid,
          message: `Optional vendor attribute '${attr.name}=${attr.value}' recorded on form '${formName}'.`,
        });
      }
    }

    const igRefs = directChildren(formDef, "ItemGroupRef")
      .map((ref, idx) => ({
        oid: ref.getAttribute("ItemGroupOID") || "",
        order: Number(ref.getAttribute("OrderNumber") ?? idx + 1),
        idx,
      }))
      .sort((a, b) =>
        a.order !== b.order ? a.order - b.order : a.idx - b.idx
      );

    const orderedItems = [];
    for (const igRef of igRefs) {
      const ig = itemGroups.get(igRef.oid);
      if (!ig) continue;
      const itemRefs = directChildren(ig, "ItemRef")
        .map((ref, idx) => ({
          oid: ref.getAttribute("ItemOID") || "",
          mandatory:
            String(ref.getAttribute("Mandatory") || "").toLowerCase() === "yes",
          order: Number(ref.getAttribute("OrderNumber") ?? idx + 1),
          idx,
        }))
        .sort((a, b) =>
          a.order !== b.order ? a.order - b.order : a.idx - b.idx
        );

      for (const itemRef of itemRefs) {
        const def = itemDefs.get(itemRef.oid);
        if (!def) continue;
        const questionTexts = byLocalName(def, "TranslatedText");
        const prompt =
          (questionTexts[0]?.textContent || "").trim() ||
          def.getAttribute("Name") ||
          itemRef.oid;

        for (const attr of Array.from(def.attributes || [])) {
          if (attr.name.includes(":") && !attr.name.startsWith("xmlns")) {
            importNotes.push({
              source: itemRef.oid,
              message: `Optional vendor attribute '${attr.name}=${attr.value}' recorded on item '${prompt}'.`,
            });
          }
        }

        for (const alias of directChildren(def, "Alias")) {
          const ctx = alias.getAttribute("Context") || "";
          const aliasName = alias.getAttribute("Name") || "";
          const codeMatch = aliasName.match(/(C\d{3,}|EXT-[A-Z0-9_-]+)/i);
          if (
            codeMatch &&
            !KNOWN_CONCEPT_CODES.has(codeMatch[1].toUpperCase())
          ) {
            importNotes.push({
              source: itemRef.oid,
              message: `Unmapped terminology code '${codeMatch[1]}' (${ctx || "Alias"}) bound to provisional concept for '${prompt}'.`,
            });
          }
        }

        const hasCodeList = directChildren(def, "CodeListRef").length > 0;
        conceptSeed += 1;
        orderedItems.push({
          id: itemRef.oid,
          display_order: orderedItems.length + 1,
          prompt,
          concept: makeConceptRef(conceptSeed),
          required: itemRef.mandatory,
          value_kind: hasCodeList
            ? "Coded"
            : mapOdmDataType(def.getAttribute("DataType")),
          unit: null,
        });
      }
    }

    forms.push({
      id: formOid,
      display_order: forms.length + 1,
      name: formName,
      item_count: orderedItems.length,
      required_item_count: orderedItems.filter((i) => i.required).length,
      items: orderedItems,
    });
  }

  if (forms.length === 0) {
    return {
      error: {
        status: 422,
        problem: {
          title: "No forms found in ODM XML",
          status: 422,
          code: "Kernel.Protocol.Import.EmptyDesign",
          detail:
            "ODM v1.3.2 XML did not contain any <FormDef> elements under <MetaDataVersion>.",
        },
      },
    };
  }

  return { title, forms, importNotes };
}

function parseUsdmJsonText(jsonText) {
  let root;
  try {
    root = JSON.parse(jsonText);
  } catch {
    return {
      error: {
        status: 422,
        problem: {
          title: "Malformed USDM v4 JSON",
          status: 422,
          code: "Kernel.Protocol.Import.MalformedJson",
          detail:
            "Uploaded file is not valid JSON. Verify brackets, quotes, and USDM v4 structure.",
        },
      },
    };
  }

  const versions = root?.study?.versions;
  if (!Array.isArray(versions) || versions.length === 0) {
    return {
      error: {
        status: 422,
        problem: {
          title: "Missing USDM study.versions",
          status: 422,
          code: "Kernel.Protocol.Import.InvalidUsdm",
          detail:
            "USDM v4 JSON must include a non-empty 'study.versions' array containing 'studyDesigns'.",
        },
      },
    };
  }

  const version = versions[0];
  const designs = version?.studyDesigns;
  if (!Array.isArray(designs) || designs.length === 0) {
    return {
      error: {
        status: 422,
        problem: {
          title: "Missing USDM studyDesigns",
          status: 422,
          code: "Kernel.Protocol.Import.InvalidUsdm",
          detail:
            "USDM v4 JSON 'study.versions[0].studyDesigns' must contain at least one study design.",
        },
      },
    };
  }

  const design = designs[0];
  const title =
    root?.study?.label ||
    root?.study?.name ||
    version?.titles?.[0]?.text ||
    "Imported USDM v4 Protocol Draft";

  const importNotes = [];
  if (Array.isArray(design.extensionAttributes)) {
    for (const ext of design.extensionAttributes) {
      importNotes.push({
        source: design.id || "StudyDesign_1",
        message: `Optional USDM extensionAttribute '${ext.url || ext.id || "vendor"}' preserved as import note.`,
      });
    }
  }

  const activities = Array.isArray(design.activities) ? design.activities : [];
  const biConcepts = new Map();
  for (const bc of Array.isArray(version.biomedicalConcepts)
    ? version.biomedicalConcepts
    : []) {
    if (bc?.id) biConcepts.set(bc.id, bc);
  }

  let conceptSeed = 100;
  const forms = activities.map((act, fIdx) => {
    const bcIds = Array.isArray(act.biomedicalConceptIds)
      ? act.biomedicalConceptIds
      : [];
    const items =
      bcIds.length > 0
        ? bcIds.map((bcId, iIdx) => {
            const bc = biConcepts.get(bcId);
            conceptSeed += 1;
            const code = bc?.code?.standardCode?.code;
            if (code && !KNOWN_CONCEPT_CODES.has(String(code).toUpperCase())) {
              importNotes.push({
                source: bcId,
                message: `Unmapped terminology code '${code}' bound to provisional concept for '${bc?.label || bc?.name || bcId}'.`,
              });
            }
            return {
              id: bcId,
              display_order: iIdx + 1,
              prompt: bc?.label || bc?.name || bcId,
              concept: makeConceptRef(conceptSeed),
              required: true,
              value_kind: "Quantity",
              unit: null,
            };
          })
        : [
            {
              id: `${act.id || `ACT_${fIdx + 1}`}_PERF`,
              display_order: 1,
              prompt: `${act.label || act.name || "Activity"} Performed`,
              concept: makeConceptRef((conceptSeed += 1)),
              required: true,
              value_kind: "Boolean",
              unit: null,
            },
          ];

    return {
      id: act.id || `FORM_${fIdx + 1}`,
      display_order: fIdx + 1,
      name: act.label || act.name || `Activity ${fIdx + 1}`,
      item_count: items.length,
      required_item_count: items.filter((i) => i.required).length,
      items,
    };
  });

  return {
    title,
    forms:
      forms.length > 0
        ? forms
        : [
            {
              id: "USDM_OVERVIEW",
              display_order: 1,
              name: "Protocol Overview Assessment",
              item_count: 1,
              required_item_count: 1,
              items: [
                {
                  id: "ELIG_CONFIRMED",
                  display_order: 1,
                  prompt: "Participant eligibility confirmed per protocol",
                  concept: makeConceptRef(199),
                  required: true,
                  value_kind: "Boolean",
                  unit: null,
                },
              ],
            },
          ],
    importNotes,
  };
}

export function createInitialDemoStudyDirectoryEntry() {
  return {
    ref: DEMO_STUDY.ref || `study:${CADENCE_101_UUID}@1`,
    kind: "study",
    code: DEMO_STUDY.code || "CADENCE-101",
    title: DEMO_STUDY.title || "Cadence demo study: cardiovascular outcomes",
    phase: DEMO_STUDY.phase || "Phase 3",
    sponsor_ref: DEMO_ORGANIZATIONS[0].ref,
    sponsor_code: DEMO_ORGANIZATIONS[0].code,
    sponsor_name: DEMO_ORGANIZATIONS[0].name,
    site_count: DEMO_SITES.length || 2,
    status: "ACTIVE",
    mask: null,
  };
}

export async function parseDemoProtocolImportRequest(request) {
  const contentType = (request.headers.get("content-type") || "").toLowerCase();
  if (contentType.includes("multipart/form-data")) {
    const formData = await request.formData();
    const file = formData.get("file");
    const templateIds = formData
      .getAll("template_ids")
      .flatMap((v) => String(v).split(","))
      .map((s) => s.trim())
      .filter(Boolean);
    const replaceForms =
      String(formData.get("replace_forms") || "").toLowerCase() === "true";
    const amendmentClassification =
      String(formData.get("amendment_classification") || "").trim() || null;
    const transitionPolicy =
      String(formData.get("transition_policy") || "").trim() || null;

    let fileName = "";
    let content = null;
    let byteLength = 0;
    if (file && typeof file === "object" && "text" in file) {
      fileName = file.name || "upload.xml";
      byteLength = Number(file.size || 0);
      content = await file.text();
    }
    return {
      format: String(formData.get("format") || "auto"),
      fileName,
      content,
      byteLength,
      templateIds,
      replaceForms,
      amendmentClassification,
      transitionPolicy,
    };
  }

  let body;
  try {
    body = await request.json();
  } catch {
    body = {};
  }
  const content = typeof body?.content === "string" ? body.content : null;
  return {
    format: body?.format || "auto",
    fileName: body?.file_name || "",
    content,
    byteLength: content ? new TextEncoder().encode(content).length : 0,
    templateIds: Array.isArray(body?.template_ids) ? body.template_ids : [],
    replaceForms: Boolean(body?.replace_forms),
    amendmentClassification: body?.amendment_classification || null,
    transitionPolicy: body?.transition_policy || null,
  };
}

export function buildImportedDraftDesign(parsedRequest) {
  const { format, fileName, content, byteLength, templateIds } = parsedRequest;

  if (byteLength > MAX_SYNC_IMPORT_BYTES) {
    return {
      error: {
        status: 413,
        problem: {
          title: "Payload Too Large",
          status: 413,
          code: "Kernel.Protocol.Import.PayloadTooLarge",
          detail:
            "Synchronous protocol import rejects files larger than 10 MB (10,485,760 bytes).",
        },
      },
    };
  }

  if (
    (!content || !content.trim()) &&
    (!templateIds || templateIds.length === 0)
  ) {
    return {
      error: {
        status: 400,
        problem: {
          title: "Missing import source",
          status: 400,
          code: "Kernel.Protocol.Import.EmptyRequest",
          detail:
            "Provide an ODM v1.3.2 XML or USDM v4 JSON file or select at least one CDISC Library eCRF template.",
        },
      },
    };
  }

  let title = "CDISC Library Protocol Draft";
  const forms = [];
  const importNotes = [];

  if (content && content.trim()) {
    const trimmed = content.trimStart();
    const lowerName = String(fileName || "").toLowerCase();
    const lowerFmt = String(format || "auto").toLowerCase();
    const isXml =
      lowerFmt.includes("odm") ||
      lowerFmt.includes("xml") ||
      lowerName.endsWith(".xml") ||
      trimmed.startsWith("<");

    const parsed = isXml
      ? parseOdmXmlText(content)
      : parseUsdmJsonText(content);
    if (parsed.error) {
      return parsed;
    }
    title = parsed.title;
    forms.push(...parsed.forms);
    importNotes.push(...parsed.importNotes);
  }

  for (const tplId of templateIds || []) {
    const tpl = DEMO_PROTOCOL_TEMPLATES.find((t) => t.id === tplId);
    if (!tpl) {
      return {
        error: {
          status: 400,
          problem: {
            title: "Unknown CDISC template",
            status: 400,
            code: "Kernel.Protocol.Import.UnknownTemplate",
            detail: `Bundled CDISC Library template '${tplId}' was not found.`,
          },
        },
      };
    }
    if (!forms.some((f) => f.id === tpl.form_oid)) {
      forms.push({
        id: tpl.form_oid,
        display_order: forms.length + 1,
        name: tpl.form_name,
        item_count: tpl.items.length,
        required_item_count: tpl.items.filter((i) => i.required).length,
        items: tpl.items.map((item, idx) => ({
          ...item,
          display_order: idx + 1,
        })),
      });
    }
  }

  return {
    title,
    forms: forms.map((f, idx) => ({ ...f, display_order: idx + 1 })),
    importNotes,
  };
}
