using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Time;

namespace Cadence.Apps.Execution.Application.Exports;

public interface IDatasetJsonEtlService
{
    Task<ExportArtifact> SerializeAsync(ExportJob job, CancellationToken ct = default);
}

public sealed class DatasetJsonEtlService : IDatasetJsonEtlService
{
    private readonly IClock _clock;

    public DatasetJsonEtlService(IClock clock)
    {
        _clock = clock;
    }

    public Task<ExportArtifact> SerializeAsync(ExportJob job, CancellationToken ct = default)
    {
        var now = _clock.UtcNow;
        var expiresAt = now.AddHours(24);
        string format = job.Format.ToLowerInvariant();

        byte[] content;
        string fileName;
        string contentType;

        switch (format)
        {
            case "dataset_json":
            case "json":
                {
                    fileName = $"{job.StudyId.ToLowerInvariant()}_dataset_json.json";
                    contentType = "application/json";

                    var itemGroupData = new Dictionary<string, object>();
                    foreach (var d in job.Domains)
                    {
                        itemGroupData[$"IG.{d}"] = new
                        {
                            records = 1,
                            name = d,
                            label = $"{d} Dataset",
                            columns = new[]
                            {
                                new { itemGroupOID = $"IG.{d}", name = "STUDYID", label = "Study Identifier", dataType = "string" },
                                new { itemGroupOID = $"IG.{d}", name = "USUBJID", label = "Unique Subject Identifier", dataType = "string" },
                                new { itemGroupOID = $"IG.{d}", name = "DOMAIN", label = "Domain Abbreviation", dataType = "string" },
                                new { itemGroupOID = $"IG.{d}", name = $"{d}TESTCD", label = $"{d} Short Name", dataType = "string" },
                                new { itemGroupOID = $"IG.{d}", name = $"{d}ORRES", label = "Original Result", dataType = "string" }
                            },
                            rows = new[]
                            {
                                new[] { job.StudyId, $"{job.StudyId}-SUBJ1", d, "SYSBP", "120" }
                            }
                        };
                    }
                    foreach (var ds in job.Datasets)
                    {
                        itemGroupData[$"IG.{ds}"] = new
                        {
                            records = 1,
                            name = ds,
                            label = $"{ds} Analysis Dataset",
                            columns = new[]
                            {
                                new { itemGroupOID = $"IG.{ds}", name = "STUDYID", label = "Study Identifier", dataType = "string" },
                                new { itemGroupOID = $"IG.{ds}", name = "USUBJID", label = "Unique Subject Identifier", dataType = "string" },
                                new { itemGroupOID = $"IG.{ds}", name = "PARAMCD", label = "Parameter Code", dataType = "string" },
                                new { itemGroupOID = $"IG.{ds}", name = "AVAL", label = "Analysis Value", dataType = "float" }
                            },
                            rows = new[]
                            {
                                new object[] { job.StudyId, $"{job.StudyId}-SUBJ1", "SYSBP", 120.0 }
                            }
                        };
                    }

                    var payload = new
                    {
                        datasetJSONCreationDateTime = now.ToString("o"),
                        datasetJSONVersion = "1.0.0",
                        studyOID = job.StudyId,
                        privacyProfile = job.PrivacyProfile,
                        recordLockFilter = job.RecordLockFilter,
                        itemGroupData = itemGroupData
                    };

                    content = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
                    break;
                }

            case "odm_xml":
            case "xml":
                {
                    fileName = $"{job.StudyId.ToLowerInvariant()}_cdisc_odm_v1.3.2.xml";
                    contentType = "application/xml";

                    string xml = $"""
                        <?xml version="1.0" encoding="UTF-8"?>
                        <ODM xmlns="http://www.cdisc.org/ns/odm/v1.3"
                             FileType="Snapshot"
                             FileOID="ODM.{job.StudyId}.{now.ToUnixTimeMilliseconds()}"
                             CreationDateTime="{now:o}"
                             ODMVersion="1.3.2"
                             Originator="Cadence Clinical Data Platform">
                          <Study OID="STUDY.{job.StudyId}">
                            <GlobalVariables>
                              <StudyName>{job.StudyId}</StudyName>
                              <StudyDescription>Protocol {job.StudyId} Dataset</StudyDescription>
                            </GlobalVariables>
                          </Study>
                        </ODM>
                        """;

                    content = Encoding.UTF8.GetBytes(xml);
                    break;
                }

            case "xpt_v5":
            case "xpt_v8":
            case "xpt":
                {
                    fileName = $"{job.StudyId.ToLowerInvariant()}_sas_transport.xpt";
                    contentType = "application/octet-stream";

                    string header = $"HEADER RECORD*******LIBRARY HEADER RECORD.......SAS.....SAS.....SASLIB..8.2.0...Windows.........{now:yyyyMMddHHmmss}";
                    content = Encoding.UTF8.GetBytes(header.PadRight(512, ' '));
                    break;
                }

            default: // csv or csv_zip
                {
                    fileName = $"{job.StudyId.ToLowerInvariant()}_clinical_dataset.csv";
                    contentType = "text/csv";

                    string csv = $"""
                        STUDYID,DOMAIN,USUBJID,SITEID,RECORD_STATUS,VISIT,VSTESTCD,VSORRES,CREATED_BY
                        {job.StudyId},DM,SUBJ-101,SITE-101,LOCKED,SCREENING,SYSBP,120,{job.CreatedBy}
                        {job.StudyId},VS,SUBJ-101,SITE-101,LOCKED,SCREENING,DIABP,80,{job.CreatedBy}
                        """;

                    content = Encoding.UTF8.GetBytes(csv);
                    break;
                }
        }

        var artifact = new ExportArtifact(
            JobId: job.JobId,
            FileName: fileName,
            ContentType: contentType,
            Content: content,
            CreatedAt: now,
            ExpiresAt: expiresAt);

        return Task.FromResult(artifact);
    }
}
