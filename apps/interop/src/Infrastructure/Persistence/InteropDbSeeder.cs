using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Apps.Interop.Domain;
using Cadence.Kernel.Time;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Interop.Infrastructure.Persistence;

/// <summary>
/// Seeds reference ePRO instruments, participant assignments, and initial reminder notifications
/// for <c>SUBJ-101-001</c> matching <c>ExecutionDbSeeder</c> and <c>docker/cadence-realm.json</c>.
/// </summary>
public static class InteropDbSeeder
{
    public const string SeededSubjectCode = "SUBJ-101-001";
    public const string DailyDiaryInstrumentId = "inst_daily_diary";
    public const string WeeklySymptomsInstrumentId = "inst_weekly_symptoms";

    public static async Task SeedAsync(InteropDbContext dbContext, IClock? clock = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        var now = (clock ?? SystemClock.Instance).UtcNow;

        if (!await dbContext.Instruments.AnyAsync(i => i.Id == DailyDiaryInstrumentId, ct).ConfigureAwait(false))
        {
            var dailyItems = new
            {
                vssbp = new
                {
                    label = "Systolic Blood Pressure (mmHg)",
                    type = "numeric",
                    required = true,
                    min = 50,
                    max = 250,
                    unit = "mm[Hg]",
                },
                vsdpb = new
                {
                    label = "Diastolic Blood Pressure (mmHg)",
                    type = "numeric",
                    required = true,
                    min = 30,
                    max = 150,
                    unit = "mm[Hg]",
                },
                vshr = new
                {
                    label = "Pulse Rate (bpm)",
                    type = "numeric",
                    required = true,
                    min = 30,
                    max = 200,
                    unit = "/min",
                },
                pain_scale = new
                {
                    label = "Symptom Severity (Visual Analog Scale 0-10)",
                    type = "visual_analog",
                    required = true,
                    min = 0,
                    max = 10,
                    step = 1,
                    min_label = "No Pain (0)",
                    max_label = "Worst Possible (10)",
                    unit = "1",
                },
                has_symptoms = new
                {
                    label = "Are you experiencing any new physical symptoms today?",
                    type = "choice_single",
                    options = new[] { "Yes", "No" },
                },
            };

            await dbContext.Instruments.AddAsync(new Instrument
            {
                Id = DailyDiaryInstrumentId,
                Name = "Daily Health & Vital Diary",
                Description = "Please record your systolic/diastolic blood pressure, pulse, and current symptoms.",
                ItemsJson = JsonSerializer.Serialize(dailyItems),
                ResponseTypesJson = JsonSerializer.Serialize(new
                {
                    vssbp = "quantity",
                    vsdpb = "quantity",
                    vshr = "quantity",
                    pain_scale = "quantity",
                    has_symptoms = "boolean",
                }),
                ScoringMetadataJson = JsonSerializer.Serialize(new { domain = "VS", scale = "0-10" }),
                CreatedAt = now,
                CreatedBy = "svc:interop-seeder",
                ReasonForChange = "Initial CADENCE-101 protocol ePRO diary setup",
                VersionIndex = 1,
            }, ct).ConfigureAwait(false);
        }

        if (!await dbContext.Instruments.AnyAsync(i => i.Id == WeeklySymptomsInstrumentId, ct).ConfigureAwait(false))
        {
            var weeklyItems = new
            {
                severity = new
                {
                    label = "Overall symptom severity this week",
                    type = "choice_single",
                    options = new[] { "None", "Mild", "Moderate", "Severe" },
                },
                restricted_activity = new
                {
                    label = "Did symptoms restrict your daily activities?",
                    type = "choice_single",
                    options = new[] { "Yes", "No" },
                },
                missed_doses = new
                {
                    label = "Did you miss any medication doses?",
                    type = "choice_single",
                    options = new[] { "Yes", "No" },
                },
            };

            await dbContext.Instruments.AddAsync(new Instrument
            {
                Id = WeeklySymptomsInstrumentId,
                Name = "Weekly Symptoms & eCOA Checklist",
                Description = "Please complete this survey detailing any adverse clinical signs experienced during the week.",
                ItemsJson = JsonSerializer.Serialize(weeklyItems),
                ResponseTypesJson = JsonSerializer.Serialize(new
                {
                    severity = "text",
                    restricted_activity = "boolean",
                    missed_doses = "boolean",
                }),
                ScoringMetadataJson = JsonSerializer.Serialize(new { domain = "QS" }),
                CreatedAt = now,
                CreatedBy = "svc:interop-seeder",
                ReasonForChange = "Initial CADENCE-101 protocol eCOA checklist setup",
                VersionIndex = 1,
            }, ct).ConfigureAwait(false);
        }

        if (!await dbContext.SubjectAssignments.AnyAsync(a => a.SubjectId == SeededSubjectCode, ct).ConfigureAwait(false))
        {
            var dailyAssignment = new SubjectAssignment
            {
                Id = "assign_subj_101_001_daily",
                SubjectId = SeededSubjectCode,
                InstrumentId = DailyDiaryInstrumentId,
                StartDate = now.AddDays(-1),
                EndDate = now.AddDays(30),
                RecurrencePattern = "DAILY",
                DueAt = now.AddHours(4),
                CreatedAt = now,
                CreatedBy = "svc:interop-seeder",
                ReasonForChange = "Initial CADENCE-101 participant assignment",
                VersionIndex = 1,
            };

            var weeklyAssignment = new SubjectAssignment
            {
                Id = "assign_subj_101_001_weekly",
                SubjectId = SeededSubjectCode,
                InstrumentId = WeeklySymptomsInstrumentId,
                StartDate = now.AddDays(-7),
                EndDate = now.AddDays(30),
                RecurrencePattern = "WEEKLY",
                DueAt = now.AddHours(-2),
                CreatedAt = now,
                CreatedBy = "svc:interop-seeder",
                ReasonForChange = "Initial CADENCE-101 weekly checklist assignment",
                VersionIndex = 1,
            };

            await dbContext.SubjectAssignments.AddRangeAsync([dailyAssignment, weeklyAssignment], ct).ConfigureAwait(false);
        }

        if (!await dbContext.SubjectNotifications.AnyAsync(n => n.SubjectId == SeededSubjectCode, ct).ConfigureAwait(false))
        {
            var reminder = new SubjectNotification
            {
                Id = "notif_subj_101_001_01",
                SubjectId = SeededSubjectCode,
                AssignmentId = "assign_subj_101_001_daily",
                DueAt = now.AddHours(4),
                Channel = "IN_APP",
                DeliveryStatus = "SENT",
                IsRead = false,
                ReadAt = null,
                CreatedAt = now,
                CreatedBy = "svc:interop-seeder",
                ReasonForChange = "Reminder: Daily Health & Vital Diary is due today.",
                VersionIndex = 1,
            };

            var overdueAlert = new SubjectNotification
            {
                Id = "notif_subj_101_001_02",
                SubjectId = SeededSubjectCode,
                AssignmentId = "assign_subj_101_001_weekly",
                DueAt = now.AddHours(-2),
                Channel = "IN_APP",
                DeliveryStatus = "SENT",
                IsRead = false,
                ReadAt = null,
                CreatedAt = now,
                CreatedBy = "svc:interop-seeder",
                ReasonForChange = "ALERT: Weekly Symptoms & eCOA Checklist is overdue. Please complete your entry.",
                VersionIndex = 1,
            };

            await dbContext.SubjectNotifications.AddRangeAsync([reminder, overdueAlert], ct).ConfigureAwait(false);
        }

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
