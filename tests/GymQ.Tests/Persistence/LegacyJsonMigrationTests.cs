using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GymQ.Models;
using GymQ.Persistence;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public sealed class LegacyJsonMigrationTests
{
    private static readonly DateTime SavedAt = Utc(12, 0);

    [TestMethod]
    public void Load_CompleteVersion1File_PreservesBusinessStateWithoutChangingEitherFile()
    {
        using var directory = new TemporaryDirectory();
        var original = WriteLegacy(directory.StatePath);
        var backup = Encoding.UTF8.GetBytes(EmptyVersion1);
        File.WriteAllBytes(directory.StatePath + ".bak", backup);
        using var store = new JsonGymStateStore(directory.StatePath);

        var restored = store.Load();

        Assert.IsNotNull(restored);
        AssertFullState(restored);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath));
        CollectionAssert.AreEqual(backup, File.ReadAllBytes(directory.StatePath + ".bak"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "state.json.corrupt.*"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "state.json.v1.*.bak"));
    }

    [TestMethod]
    public void FirstSave_AfterVersion1Load_WritesCurrentSchemaAndKeepsExactLegacyBackup()
    {
        using var directory = new TemporaryDirectory();
        var original = WriteLegacy(directory.StatePath);
        using (var store = new JsonGymStateStore(directory.StatePath))
        {
            var restored = store.Load();
            Assert.IsNotNull(restored);
            store.Save(restored);
        }

        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath + ".bak"));
        AssertLegacyArchive(directory, original);
        using (var document = JsonDocument.Parse(File.ReadAllBytes(directory.StatePath)))
        {
            Assert.AreEqual(GymStateSnapshot.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.IsTrue(document.RootElement.TryGetProperty("nudgeCooldowns", out _));
            Assert.IsFalse(document.RootElement.TryGetProperty("lastNudgeAt", out _));
        }
        using var reopened = new JsonGymStateStore(directory.StatePath);
        var savedAgain = reopened.Load();
        Assert.IsNotNull(savedAgain);
        AssertFullState(savedAgain);
        reopened.Save(savedAgain);
        AssertLegacyArchive(directory, original);
        using var rotatedBackup = JsonDocument.Parse(File.ReadAllBytes(directory.StatePath + ".bak"));
        Assert.AreEqual(GymStateSnapshot.CurrentSchemaVersion, rotatedBackup.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public void Save_WhenLegacyArchiveCannotBeWritten_PreservesOriginalAndDoesNotRotateBackup()
    {
        using var directory = new TemporaryDirectory();
        // The source, lock and ordinary save names fit the filesystem's component limit;
        // the migration archive's longer temporary filename exceeds that limit.
        var path = System.IO.Path.Combine(directory.Path, new string('x', 200) + ".json");
        var original = WriteLegacy(path);
        using var store = new JsonGymStateStore(path);
        var restored = store.Load();
        Assert.IsNotNull(restored);

        Assert.Throws<IOException>(() => store.Save(restored));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(path));
        Assert.IsFalse(File.Exists(path + ".bak"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "*.v1.*.bak"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "*.tmp"));
        AssertFullState(store.Load()!);
    }

    [TestMethod]
    public void OpenPersistent_Version1_PreservesClockReservationHistoryAndReportNumbering()
    {
        using var directory = new TemporaryDirectory();
        var original = WriteLegacy(directory.StatePath);
        using var store = new JsonGymStateStore(directory.StatePath);

        var gym = GymSession.OpenPersistent(store, clock: new FixedClock());

        Assert.AreEqual(SavedAt, gym.UtcNow);
        Assert.AreEqual(TimeSpan.FromMinutes(30), gym.AdvancedBy);
        AssertFullState(gym.CaptureState(), restoredServices: true);
        Assert.AreEqual(Utc(11, 59), gym.Queue.ReadQueue("E3")[0].NotifiedAt);
        Assert.AreEqual("M003", gym.Queue.ReadQueue("E3")[1].MemberId);
        Assert.AreEqual("legacy-active", gym.Sessions.ReadActiveSession("E1")!.SessionId);
        Assert.AreEqual(new QueueCancellationNotice("M003", "E2", "Bike"), gym.ReadQueueCancellation("M003"));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath + ".bak"));

        gym.Report("E4", gym.FindMember("M003"), "New report after migration");

        Assert.AreEqual("R-21", gym.Reports.Last().ReportId);
        CollectionAssert.AreEquivalent(new[] { "R-2", "R-7", "R-12", "R-21" }, gym.Reports.Select(r => r.ReportId).ToArray());
        AssertLegacyArchive(directory, original);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void OpenPersistent_LegacyNudgeTransients_AreRetiredWithoutEndingSessionOrChangingQueues(bool expired)
    {
        using var directory = new TemporaryDirectory();
        var legacy = WithLegacyTransients(expired);
        var original = Encoding.UTF8.GetBytes(legacy.ToJsonString());
        File.WriteAllBytes(directory.StatePath, original);
        using var store = new JsonGymStateStore(directory.StatePath);

        var loaded = store.Load();

        Assert.IsNotNull(loaded);
        AssertFullState(loaded);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "state.json.v1.*.bak"));

        var gym = GymSession.OpenPersistent(store, clock: new FixedClock());
        gym.Tick();

        AssertFullState(gym.CaptureState(), restoredServices: true);
        Assert.AreEqual("legacy-active", gym.Sessions.ReadActiveSession("E1")!.SessionId);
        AssertLegacyArchive(directory, original);
        // No requester or per-session cooldown is invented from the old equipment-only ledger.
        gym.SendNudge("E1", gym.FindMember("M003"));
        Assert.AreEqual("M003", gym.Nudges.Single().RequestedBy);
        Assert.AreEqual(SavedAt.AddMinutes(2), gym.Nudges.Single().ExpiresAt);
        AssertLegacyArchive(directory, original);
    }

    [TestMethod]
    [DataRow("nullCooldowns")]
    [DataRow("nullNudges")]
    [DataRow("nullNudgeEntry")]
    [DataRow("missingNudgeTarget")]
    [DataRow("unknownCooldownEquipment")]
    [DataRow("nonUtcCooldown")]
    [DataRow("wrongNudgeTarget")]
    [DataRow("unknownNudgeEquipment")]
    [DataRow("nudgeWithoutActiveSession")]
    [DataRow("nonUtcNudgeExpiry")]
    [DataRow("duplicateNudge")]
    public void Load_InvalidLegacyNudgeTransients_AreRejectedBeforeRetirement(string defect)
    {
        using var directory = new TemporaryDirectory();
        var invalid = WithLegacyTransients(expired: false);
        switch (defect)
        {
            case "nullCooldowns": invalid["lastNudgeAt"] = null; break;
            case "nullNudges": invalid["nudges"] = null; break;
            case "nullNudgeEntry": invalid["nudges"]![0] = null; break;
            case "missingNudgeTarget": invalid["nudges"]![0]!.AsObject().Remove("memberId"); break;
            case "unknownCooldownEquipment": invalid["lastNudgeAt"]!["unknown"] = "2026-10-09T11:57:00Z"; break;
            case "nonUtcCooldown": invalid["lastNudgeAt"]!["E1"] = "2026-10-09T11:57:00"; break;
            case "wrongNudgeTarget": invalid["nudges"]![0]!["memberId"] = "M002"; break;
            case "unknownNudgeEquipment": invalid["nudges"]![0]!["equipmentId"] = "unknown"; break;
            case "nudgeWithoutActiveSession": invalid["nudges"]![0]!["equipmentId"] = "E4"; break;
            case "nonUtcNudgeExpiry": invalid["nudges"]![0]!["expiresAt"] = "2026-10-09T12:01:00"; break;
            case "duplicateNudge": invalid["nudges"]!.AsArray().Add(invalid["nudges"]![0]!.DeepClone()); break;
            default: Assert.Fail("Unknown test defect."); break;
        }
        var original = Encoding.UTF8.GetBytes(invalid.ToJsonString());
        File.WriteAllBytes(directory.StatePath, original);
        using var store = new JsonGymStateStore(directory.StatePath);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(new GymStateSnapshot { SavedAtUtc = SavedAt }));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath));
        Assert.IsFalse(File.Exists(directory.StatePath + ".bak"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "state.json.v1.*.bak"));
    }

    [TestMethod]
    [DataRow("missingCooldownCollection")]
    [DataRow("missingNudgeCollection")]
    [DataRow("missingSessionEndMetadata")]
    [DataRow("missingReportReviewMetadata")]
    [DataRow("invalidEquipmentReference")]
    [DataRow("invalidQueueNotificationOrder")]
    [DataRow("inconsistentDemoClock")]
    public void Load_MalformedVersion1_PreservesFileAndRefusesReplacement(string defect)
    {
        using var directory = new TemporaryDirectory();
        var invalid = JsonNode.Parse(FullVersion1)!.AsObject();
        switch (defect)
        {
            case "missingCooldownCollection": invalid.Remove("lastNudgeAt"); break;
            case "missingNudgeCollection": invalid.Remove("nudges"); break;
            case "missingSessionEndMetadata":
                invalid["sessions"]![1]!.AsObject().Remove("endTime");
                invalid["sessions"]![1]!.AsObject().Remove("endReason");
                break;
            case "missingReportReviewMetadata":
                invalid["reports"]![1]!.AsObject().Remove("reviewedByStaffId");
                invalid["reports"]![1]!.AsObject().Remove("reviewedAt");
                break;
            case "invalidEquipmentReference": invalid["sessions"]![1]!["equipmentId"] = "unknown"; break;
            case "invalidQueueNotificationOrder": invalid["queue"]![2]!["notifiedAt"] = "2026-10-09T11:59:30Z"; break;
            case "inconsistentDemoClock": invalid["advancedBy"] = "00:29:00"; break;
            default: Assert.Fail("Unknown test defect."); break;
        }
        var original = Encoding.UTF8.GetBytes(invalid.ToJsonString());
        File.WriteAllBytes(directory.StatePath, original);
        using var store = new JsonGymStateStore(directory.StatePath);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(new GymStateSnapshot { SavedAtUtc = SavedAt }));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath));
        Assert.IsFalse(File.Exists(directory.StatePath + ".bak"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "state.json.corrupt.*"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "state.json.v1.*.bak"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Load_Version1Backup_RecoversMissingOrMalformedPrimaryWithoutChangingBackup(bool malformedPrimary)
    {
        using var directory = new TemporaryDirectory();
        var legacyBackup = WriteLegacy(directory.StatePath + ".bak");
        const string corrupt = "{\"schemaVersion\":1,\"sessions\":[]}";
        if (malformedPrimary) File.WriteAllText(directory.StatePath, corrupt);
        using (var store = new JsonGymStateStore(directory.StatePath))
        {
            var restored = store.Load();
            Assert.IsNotNull(restored);
            AssertFullState(restored);
            CollectionAssert.AreEqual(legacyBackup, File.ReadAllBytes(directory.StatePath + ".bak"));
            if (malformedPrimary) Assert.AreEqual(corrupt, File.ReadAllText(directory.StatePath));
            else Assert.IsFalse(File.Exists(directory.StatePath));

            store.Save(restored);
        }

        CollectionAssert.AreEqual(legacyBackup, File.ReadAllBytes(directory.StatePath + ".bak"));
        AssertLegacyArchive(directory, legacyBackup);
        var preserved = Directory.GetFiles(directory.Path, "state.json.corrupt.*");
        Assert.HasCount(malformedPrimary ? 1 : 0, preserved);
        if (malformedPrimary) Assert.AreEqual(corrupt, File.ReadAllText(preserved[0]));
        using var reopened = new JsonGymStateStore(directory.StatePath);
        var savedAgain = reopened.Load();
        Assert.IsNotNull(savedAgain);
        AssertFullState(savedAgain);
    }

    [TestMethod]
    public void OpenPersistent_ValidEmptyVersion1File_DoesNotReseed()
    {
        using var directory = new TemporaryDirectory();
        var original = Encoding.UTF8.GetBytes(EmptyVersion1);
        File.WriteAllBytes(directory.StatePath, original);
        using var store = new JsonGymStateStore(directory.StatePath);

        var gym = GymSession.OpenPersistent(store, seedWhenMissing: true, clock: new FixedClock());

        Assert.IsEmpty(gym.Equipment);
        Assert.IsEmpty(gym.Sessions.ReadSessions());
        Assert.IsEmpty(gym.Reports);
        Assert.IsEmpty(gym.CaptureState().Queue);
        Assert.IsEmpty(gym.Nudges);
        Assert.AreEqual(TimeSpan.FromMinutes(30), gym.AdvancedBy);
        Assert.AreEqual(SavedAt, gym.UtcNow);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath + ".bak"));
        AssertLegacyArchive(directory, original);
    }

    [TestMethod]
    public void Load_Version2Primary_DoesNotGuessItsFormatOrFallBackToVersion1Backup()
    {
        using var directory = new TemporaryDirectory();
        var unknown = JsonNode.Parse(FullVersion1)!.AsObject();
        unknown["schemaVersion"] = 2;
        var original = Encoding.UTF8.GetBytes(unknown.ToJsonString());
        File.WriteAllBytes(directory.StatePath, original);
        var legacyBackup = WriteLegacy(directory.StatePath + ".bak");
        using var store = new JsonGymStateStore(directory.StatePath);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(new GymStateSnapshot { SavedAtUtc = SavedAt }));

        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath));
        CollectionAssert.AreEqual(legacyBackup, File.ReadAllBytes(directory.StatePath + ".bak"));
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "state.json.v1.*.bak"));
    }

    private static void AssertFullState(GymStateSnapshot state, bool restoredServices = false)
    {
        Assert.AreEqual(GymStateSnapshot.CurrentSchemaVersion, state.SchemaVersion);
        Assert.AreEqual(SavedAt, state.SavedAtUtc);
        Assert.AreEqual(TimeSpan.FromMinutes(30), state.ClockOffset);
        Assert.AreEqual(TimeSpan.FromMinutes(30), state.AdvancedBy);
        Assert.AreEqual(20L, state.NextReportNumber);
        CollectionAssert.AreEqual(new[]
        {
            ("E1", "Treadmill", EquipmentStatus.InUse),
            ("E2", "Bike", EquipmentStatus.Unavailable),
            ("E3", "Rack", EquipmentStatus.Available),
            ("E4", "Rower", EquipmentStatus.Available)
        }, state.Equipment.Select(e => (e.EquipmentId, e.Name, e.Status)).ToArray());
        var sessions = new[]
        {
            new SessionState("legacy-active", "E1", "M001", Utc(11, 50), null, null),
            new SessionState("legacy-manual", "E2", "M002", Utc(10, 0), Utc(10, 3), SessionEndReason.ManualFinish),
            new SessionState("legacy-response", "E4", "M003", Utc(10, 10), Utc(10, 13), SessionEndReason.NudgeResponse),
            new SessionState("legacy-timeout", "E4", "M003", Utc(10, 20), Utc(10, 22), SessionEndReason.NudgeTimeout),
            new SessionState("legacy-max", "E4", "M003", Utc(10, 30), Utc(11, 0), SessionEndReason.MaxDurationReached)
        };
        if (restoredServices) CollectionAssert.AreEquivalent(sessions, state.Sessions);
        else CollectionAssert.AreEqual(sessions, state.Sessions);
        var reports = new[]
        {
            ("R-2", "E1", "M002", "Vibration", FaultReportStatus.Pending, Utc(11, 51), (string?)null, (DateTime?)null),
            ("R-7", "E2", "M003", "Loose cable", FaultReportStatus.Confirmed, Utc(10, 2), "S001", (DateTime?)Utc(10, 4)),
            ("R-12", "E4", "M002", "Noise", FaultReportStatus.Rejected, Utc(11, 40), "S001", (DateTime?)Utc(11, 41))
        };
        var actualReports = state.Reports.Select(r => (r.ReportId, r.EquipmentId, r.SubmittedByMemberId, r.Description, r.Status, r.SubmittedAt, r.ReviewedByStaffId, r.ReviewedAt)).ToArray();
        if (restoredServices) CollectionAssert.AreEquivalent(reports, actualReports);
        else CollectionAssert.AreEqual(reports, actualReports);
        var queue = new[]
        {
            ("E1", "M003", Utc(11, 56), (DateTime?)null),
            ("E3", "M002", Utc(11, 57), (DateTime?)Utc(11, 59)),
            ("E3", "M003", Utc(11, 58), (DateTime?)null),
            ("E1", "M002", Utc(11, 59), (DateTime?)null)
        };
        var actualQueue = state.Queue.Select(q => (q.EquipmentId, q.MemberId, q.JoinedAt, q.NotifiedAt)).ToArray();
        if (restoredServices)
        {
            CollectionAssert.AreEquivalent(queue, actualQueue);
            foreach (var equipmentId in new[] { "E1", "E3" })
                CollectionAssert.AreEqual(queue.Where(q => q.Item1 == equipmentId).ToArray(), actualQueue.Where(q => q.EquipmentId == equipmentId).ToArray());
        }
        else CollectionAssert.AreEqual(queue, actualQueue);
        Assert.IsEmpty(state.NudgeCooldowns);
        Assert.IsEmpty(state.Nudges);
        CollectionAssert.AreEqual(new[] { new QueueCancellationNotice("M003", "E2", "Bike") }, state.Cancellations);
    }

    private static DateTime Utc(int hour, int minute) => new(2026, 10, 9, hour, minute, 0, DateTimeKind.Utc);

    private static JsonObject WithLegacyTransients(bool expired)
    {
        var legacy = JsonNode.Parse(FullVersion1)!.AsObject();
        legacy["lastNudgeAt"] = new JsonObject { ["E1"] = "2026-10-09T11:57:00Z" };
        legacy["nudges"] = JsonNode.Parse(expired
            ? """[{"equipmentId":"E1","memberId":"M001","expiresAt":"2026-10-09T11:59:00Z"}]"""
            : """[{"equipmentId":"E1","memberId":"M001","expiresAt":"2026-10-09T12:01:00Z"}]""");
        return legacy;
    }

    private static void AssertLegacyArchive(TemporaryDirectory directory, byte[] original)
    {
        var archives = Directory.GetFiles(directory.Path, "state.json.v1.*.bak");
        Assert.HasCount(1, archives);
        CollectionAssert.AreEqual(original, File.ReadAllBytes(archives[0]));
    }

    private static byte[] WriteLegacy(string path)
    {
        var bytes = Encoding.UTF8.GetBytes(FullVersion1 + "\n");
        File.WriteAllBytes(path, bytes);
        return bytes;
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 9, 11, 30, 0, TimeSpan.Zero);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gymq-legacy-json-tests-" + Guid.NewGuid().ToString("N"));
        public string StatePath => System.IO.Path.Combine(Path, "state.json");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // A complete original v1 document, rather than serializing the current v3 DTO as old data.
    private const string FullVersion1 = """
        {
          "schemaVersion": 1,
          "savedAtUtc": "2026-10-09T12:00:00Z",
          "clockOffset": "00:30:00",
          "advancedBy": "00:30:00",
          "nextReportNumber": 20,
          "equipment": [
            { "equipmentId": "E1", "name": "Treadmill", "status": "InUse" },
            { "equipmentId": "E2", "name": "Bike", "status": "Unavailable" },
            { "equipmentId": "E3", "name": "Rack", "status": "Available" },
            { "equipmentId": "E4", "name": "Rower", "status": "Available" }
          ],
          "sessions": [
            { "sessionId": "legacy-active", "equipmentId": "E1", "memberId": "M001", "startTime": "2026-10-09T11:50:00Z", "endTime": null, "endReason": null },
            { "sessionId": "legacy-manual", "equipmentId": "E2", "memberId": "M002", "startTime": "2026-10-09T10:00:00Z", "endTime": "2026-10-09T10:03:00Z", "endReason": "ManualFinish" },
            { "sessionId": "legacy-response", "equipmentId": "E4", "memberId": "M003", "startTime": "2026-10-09T10:10:00Z", "endTime": "2026-10-09T10:13:00Z", "endReason": "NudgeResponse" },
            { "sessionId": "legacy-timeout", "equipmentId": "E4", "memberId": "M003", "startTime": "2026-10-09T10:20:00Z", "endTime": "2026-10-09T10:22:00Z", "endReason": "NudgeTimeout" },
            { "sessionId": "legacy-max", "equipmentId": "E4", "memberId": "M003", "startTime": "2026-10-09T10:30:00Z", "endTime": "2026-10-09T11:00:00Z", "endReason": "MaxDurationReached" }
          ],
          "reports": [
            { "reportId": "R-2", "equipmentId": "E1", "submittedByMemberId": "M002", "description": "Vibration", "status": "Pending", "submittedAt": "2026-10-09T11:51:00Z", "reviewedByStaffId": null, "reviewedAt": null },
            { "reportId": "R-7", "equipmentId": "E2", "submittedByMemberId": "M003", "description": "Loose cable", "status": "Confirmed", "submittedAt": "2026-10-09T10:02:00Z", "reviewedByStaffId": "S001", "reviewedAt": "2026-10-09T10:04:00Z" },
            { "reportId": "R-12", "equipmentId": "E4", "submittedByMemberId": "M002", "description": "Noise", "status": "Rejected", "submittedAt": "2026-10-09T11:40:00Z", "reviewedByStaffId": "S001", "reviewedAt": "2026-10-09T11:41:00Z" }
          ],
          "queue": [
            { "equipmentId": "E1", "memberId": "M003", "joinedAt": "2026-10-09T11:56:00Z", "notifiedAt": null },
            { "equipmentId": "E3", "memberId": "M002", "joinedAt": "2026-10-09T11:57:00Z", "notifiedAt": "2026-10-09T11:59:00Z" },
            { "equipmentId": "E3", "memberId": "M003", "joinedAt": "2026-10-09T11:58:00Z", "notifiedAt": null },
            { "equipmentId": "E1", "memberId": "M002", "joinedAt": "2026-10-09T11:59:00Z", "notifiedAt": null }
          ],
          "lastNudgeAt": {},
          "nudges": [],
          "cancellations": [
            { "memberId": "M003", "equipmentId": "E2", "equipmentName": "Bike" }
          ]
        }
        """;

    private const string EmptyVersion1 = """
        {
          "schemaVersion": 1,
          "savedAtUtc": "2026-10-09T12:00:00Z",
          "clockOffset": "00:30:00",
          "advancedBy": "00:30:00",
          "nextReportNumber": 0,
          "equipment": [],
          "sessions": [],
          "reports": [],
          "queue": [],
          "lastNudgeAt": {},
          "nudges": [],
          "cancellations": []
        }
        """;
}
