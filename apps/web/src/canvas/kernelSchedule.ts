import type { KernelScheduleOfActivities } from "../api/kernelApi";
import type {
  StudyScheduleColumn,
  StudyScheduleEpoch,
  StudyScheduleOptions,
  StudyScheduleRow,
} from "./kernelStudy";

function buildScheduleEpochs(
  rawEpochs: ReadonlyArray<{
    epoch_id?: string;
    name?: string;
    order?: number | string;
    first_column?: number | string;
    column_count?: number | string;
  }>,
  columns: ReadonlyArray<StudyScheduleColumn>
): StudyScheduleEpoch[] {
  if (rawEpochs.length > 0) {
    return rawEpochs
      .map((epoch, index) => ({
        epochId: epoch.epoch_id ?? `epoch-${index}`,
        name: epoch.name ?? epoch.epoch_id ?? `Epoch ${index + 1}`,
        order: Number(epoch.order) || index,
        firstColumn: Number(epoch.first_column) || 0,
        columnCount: Number(epoch.column_count) || 0,
      }))
      .filter((epoch) => epoch.columnCount > 0);
  }
  const synthesized: StudyScheduleEpoch[] = [];
  for (let i = 0; i < columns.length; i++) {
    const epochId = columns[i].epochId || "study";
    const last = synthesized.at(-1);
    if (last && last.epochId === epochId) {
      last.columnCount += 1;
    } else {
      synthesized.push({
        epochId,
        name: epochId,
        order: synthesized.length + 1,
        firstColumn: i,
        columnCount: 1,
      });
    }
  }
  return synthesized;
}

export function mapScheduleOfActivities(
  soa: KernelScheduleOfActivities
): StudyScheduleOptions {
  const rawColumns = Array.isArray(soa.columns) ? soa.columns : [];
  const rawRows = Array.isArray(soa.rows) ? soa.rows : [];
  const columns: StudyScheduleColumn[] = rawColumns.map((col) => ({
    activityId: col.activity_id,
    name: col.name,
    epochId: col.epoch_id ?? "",
    studyDay:
      col.study_day !== null && col.study_day !== undefined
        ? Number(col.study_day)
        : null,
    windowBefore: Number(col.window_before) || 0,
    windowAfter: Number(col.window_after) || 0,
    locations: Array.isArray(col.locations) ? col.locations : [],
  }));
  const epochs = buildScheduleEpochs(
    Array.isArray(soa.epochs) ? soa.epochs : [],
    columns
  );
  const mappedRows: StudyScheduleRow[] = rawRows.map((row) => {
    const columnIndices = (Array.isArray(row.columns) ? row.columns : [])
      .map((idx) => Number(idx))
      .filter(
        (idx) => Number.isInteger(idx) && idx >= 0 && idx < columns.length
      );
    return {
      collectionSpecId: row.collection_spec_id,
      name: row.name,
      itemCount: Array.isArray(row.item_ids) ? row.item_ids.length : 0,
      columnIndices,
      plannedActivityIds: columnIndices
        .map((idx) => columns[idx]?.activityId)
        .filter((id): id is string => Boolean(id)),
    };
  });

  return {
    versionRef: soa.version,
    title: soa.title ?? "",
    epochs,
    columns,
    scheduledRows: mappedRows.filter((row) => row.columnIndices.length > 0),
    unscheduledRows: mappedRows.filter((row) => row.columnIndices.length === 0),
    collectionSpecs: mappedRows.map((row) => ({
      id: row.collectionSpecId,
      name: row.name,
      plannedActivityIds: row.plannedActivityIds,
    })),
    plannedActivities: columns.map((col) => ({
      id: col.activityId,
      name: col.name,
      studyDay: col.studyDay,
    })),
  };
}
