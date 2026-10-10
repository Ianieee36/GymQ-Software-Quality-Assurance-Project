using System.Text.Json;
using System.Text.Json.Nodes;
using GymQ.Models;
using GymQ.Persistence;
using GymQ.Services;

namespace GymQ.Tests;

[TestClass]
public sealed class JsonGymStateStoreTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [TestMethod]
    public void IncompleteVersion1File_IsRejectedAndPreserved()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        const string version1 = "{\"schemaVersion\":1}";
        File.WriteAllText(directory.StatePath, version1);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.AreEqual(version1, File.ReadAllText(directory.StatePath));
    }

    [TestMethod]
    public void SaveAndLoad_PreservesHistoryOrderedQueuesTimersAndMaintenanceState()
    {
        using var directory = new TemporaryDirectory();
        var expected = FullState();
        using (var store = new JsonGymStateStore(directory.StatePath)) store.Save(expected);

        using var reopened = new JsonGymStateStore(directory.StatePath);
        var actual = reopened.Load();
        Assert.IsNotNull(actual);
        GymStateValidator.Validate(actual, new[] { "M1", "M2", "M3", "S1" });
        Assert.AreEqual(expected.SavedAtUtc, actual.SavedAtUtc);
        Assert.AreEqual(TimeSpan.FromMinutes(30), actual.ClockOffset);
        Assert.AreEqual(actual.ClockOffset, actual.AdvancedBy);
        Assert.AreEqual(3L, actual.NextReportNumber);
        Assert.AreEqual(EquipmentStatus.Unavailable, actual.Equipment.Single(e => e.EquipmentId == "E2").Status);
        Assert.AreEqual("S2", actual.Sessions[1].SessionId);
        Assert.AreEqual(Now.AddMinutes(-18), actual.Sessions[1].EndTime);
        Assert.AreEqual(SessionEndReason.ManualFinish, actual.Sessions[1].EndReason);
        CollectionAssert.AreEqual(new[] { FaultReportStatus.Pending, FaultReportStatus.Confirmed, FaultReportStatus.Rejected }, actual.Reports.Select(r => r.Status).ToArray());
        Assert.AreEqual("S1", actual.Reports[1].ReviewedByStaffId);
        Assert.AreEqual("Loose cable", actual.Reports[1].Description);
        Assert.AreEqual(Now.AddMinutes(-18), actual.Reports[1].ReviewedAt);
        CollectionAssert.AreEqual(new[] { "M2", "M3" }, actual.Queue.Where(q => q.EquipmentId == "E3").Select(q => q.MemberId).ToArray());
        Assert.AreEqual(Now.AddMinutes(-1), actual.Queue.Single(q => q.EquipmentId == "E3" && q.MemberId == "M2").NotifiedAt);
        Assert.IsNull(actual.Queue.Single(q => q.EquipmentId == "E3" && q.MemberId == "M3").NotifiedAt);
                  Assert.AreEqual(new NudgeCooldown("S1", "M3", Now.AddMinutes(-1)), actual.NudgeCooldowns.Single());
        Assert.AreEqual(new NudgeNotice(SessionId: "S1", EquipmentId: "E1", MemberId: "M1", RequestedBy: "M3", ExpiresAt: Now.AddMinutes(1)), actual.Nudges.Single());
        Assert.AreEqual(new QueueCancellationNotice("M3", "E2", "Bike"), actual.Cancellations.Single());
    }

    [TestMethod]
    public void MissingFile_ReturnsNull_AndAnExistingEmptyStateStaysEmpty()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        Assert.IsNull(store.Load());
        store.Save(EmptyState());
        var restored = store.Load();
        Assert.IsNotNull(restored);
        Assert.IsEmpty(restored.Equipment);
        Assert.IsEmpty(restored.Sessions);
        Assert.IsEmpty(restored.Reports);
        Assert.IsEmpty(restored.Queue);
    }

    [TestMethod]
    public void CorruptPrimary_LoadsValidatedBackup_AndPreservesCorruptionUntilNextSave()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(EmptyState(Now));
        store.Save(EmptyState(Now.AddMinutes(1)));
        const string corrupted = "{\"schemaVersion\":2,";
        File.WriteAllText(directory.StatePath, corrupted);

        Assert.AreEqual(Now, store.Load()!.SavedAtUtc);
        Assert.AreEqual(corrupted, File.ReadAllText(directory.StatePath));
        store.Save(EmptyState(Now.AddMinutes(2)));

        Assert.AreEqual(Now.AddMinutes(2), store.Load()!.SavedAtUtc);
        var preserved = Directory.GetFiles(directory.Path, "state.json.corrupt.*");
        Assert.HasCount(1, preserved);
        Assert.AreEqual(corrupted, File.ReadAllText(preserved[0]));
        using var backup = JsonDocument.Parse(File.ReadAllText(directory.StatePath + ".bak"));
        Assert.AreEqual(Now, backup.RootElement.GetProperty("savedAtUtc").GetDateTime());
    }

    [TestMethod]
    public void MissingPrimary_RecoversBackupBeforeCreatingReplacement()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(EmptyState(Now));
        store.Save(EmptyState(Now.AddMinutes(1)));
        File.Delete(directory.StatePath);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(EmptyState(Now.AddMinutes(2))));
        Assert.AreEqual(Now, store.Load()!.SavedAtUtc);
        Assert.IsFalse(File.Exists(directory.StatePath));
        store.Save(EmptyState(Now.AddMinutes(2)));
        Assert.AreEqual(Now.AddMinutes(2), store.Load()!.SavedAtUtc);
    }

    [TestMethod]
    public void UnsupportedPrimary_DoesNotFallBackOrOverwriteEvenWithValidBackup()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(EmptyState(Now));
        store.Save(EmptyState(Now.AddMinutes(1)));
        const string unsupported = "{\"schemaVersion\":999}";
        File.WriteAllText(directory.StatePath, unsupported);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(EmptyState()));
        Assert.AreEqual(unsupported, File.ReadAllText(directory.StatePath));
    }

    [TestMethod]
    [DataRow("null")]
    [DataRow("{}")]
    [DataRow("{\"schemaVersion\":\"2\"}")]
    [DataRow("{\"schemaVersion\":2,\"savedAtUtc\":\"2026-10-09T12:00:00Z\",\"equipment\":null}")]
    public void InvalidPrimaryWithoutBackup_IsPreservedAndCannotBeOverwritten(string invalid)
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        File.WriteAllText(directory.StatePath, invalid);
        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(EmptyState()));
        Assert.AreEqual(invalid, File.ReadAllText(directory.StatePath));
    }

    [TestMethod]
    [DataRow("schemaVersion")]
    [DataRow("savedAtUtc")]
    [DataRow("clockOffset")]
    [DataRow("advancedBy")]
    [DataRow("nextReportNumber")]
    [DataRow("equipment")]
    [DataRow("sessions")]
    [DataRow("reports")]
    [DataRow("queue")]
    [DataRow("nudgeCooldowns")]
    [DataRow("nudges")]
    [DataRow("cancellations")]
    public void MissingRootField_IsRejectedAndCannotReplaceTheIncompleteFile(string field)
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(EmptyState());
        var incomplete = JsonNode.Parse(File.ReadAllText(directory.StatePath))!.AsObject();
        Assert.IsTrue(incomplete.Remove(field));
        var invalid = incomplete.ToJsonString();
        File.WriteAllText(directory.StatePath, invalid);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(EmptyState()));
        Assert.AreEqual(invalid, File.ReadAllText(directory.StatePath));
        Assert.IsFalse(File.Exists(directory.StatePath + ".bak"));
    }

    [TestMethod]
    public void MissingReportCollection_RecoversBackupWithoutErasingHistoryOrReplacingTheIncompleteFile()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(FullState());
        store.Save(FullState());
        var incomplete = JsonNode.Parse(File.ReadAllText(directory.StatePath))!.AsObject();
        Assert.IsTrue(incomplete.Remove("reports"));
        var invalid = incomplete.ToJsonString();
        File.WriteAllText(directory.StatePath, invalid);

        var restored = store.Load();

        Assert.IsNotNull(restored);
        CollectionAssert.AreEqual(new[] { "R-1", "R-2", "R-3" }, restored.Reports.Select(r => r.ReportId).ToArray());
        Assert.AreEqual(invalid, File.ReadAllText(directory.StatePath));
    }

    [TestMethod]
    [DataRow("equipment", 1, "status")]
    [DataRow("queue", 1, "notifiedAt")]
    [DataRow("sessions", 0, "endTime")]
    [DataRow("reports", 0, "reviewedAt")]
    public void MissingNestedField_IsRejectedInsteadOfInventingBusinessDefaults(string collection, int index, string field)
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(FullState());
        var incomplete = JsonNode.Parse(File.ReadAllText(directory.StatePath))!.AsObject();
        Assert.IsTrue(incomplete[collection]![index]!.AsObject().Remove(field));
        var invalid = incomplete.ToJsonString();
        File.WriteAllText(directory.StatePath, invalid);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.ThrowsExactly<InvalidDataException>(() => store.Save(EmptyState()));
        Assert.AreEqual(invalid, File.ReadAllText(directory.StatePath));
    }

    [TestMethod]
    public void MissingBothSessionEndFields_IsRejectedInsteadOfResurrectingEndedMaintenanceHistory()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(FullState());
        var incomplete = JsonNode.Parse(File.ReadAllText(directory.StatePath))!.AsObject();
        var ended = incomplete["sessions"]![1]!.AsObject();
        Assert.IsTrue(ended.Remove("endTime"));
        Assert.IsTrue(ended.Remove("endReason"));
        var invalid = incomplete.ToJsonString();
        File.WriteAllText(directory.StatePath, invalid);

        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.AreEqual(invalid, File.ReadAllText(directory.StatePath));
    }

    [TestMethod]
    public void CorruptPrimaryAndBackup_AreBothPreserved()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        File.WriteAllText(directory.StatePath, "broken primary");
        File.WriteAllText(directory.StatePath + ".bak", "broken backup");
        Assert.ThrowsExactly<InvalidDataException>(() => store.Load());
        Assert.AreEqual("broken primary", File.ReadAllText(directory.StatePath));
        Assert.AreEqual("broken backup", File.ReadAllText(directory.StatePath + ".bak"));
    }

    [TestMethod]
    public void FailedBackupReplacement_PreservesLiveFileAndCleansTemporaryFile()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(EmptyState());
        var original = File.ReadAllBytes(directory.StatePath);
        Directory.CreateDirectory(directory.StatePath + ".bak");

        Assert.ThrowsExactly<IOException>(() => store.Save(EmptyState(Now.AddMinutes(1))));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath));
        Assert.AreEqual(Now, store.Load()!.SavedAtUtc);
        Assert.IsEmpty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [TestMethod]
    public void WriterLease_BlocksSecondInstanceUntilDisposed()
    {
        using var directory = new TemporaryDirectory();
        var first = new JsonGymStateStore(directory.StatePath);
        try
        {
            first.Save(EmptyState());
            Assert.ThrowsExactly<IOException>(() => { using var second = new JsonGymStateStore(directory.StatePath); });
        }
        finally { first.Dispose(); }
        using var second = new JsonGymStateStore(directory.StatePath);
        Assert.IsNotNull(second.Load());
        first.Dispose();
        Assert.ThrowsExactly<ObjectDisposedException>(() => first.Load());
        Assert.ThrowsExactly<ObjectDisposedException>(() => first.Save(EmptyState()));
    }

    [TestMethod]
    public void StateFile_ContainsNoCredentialsOrComputedUiFields()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(FullState());
        using var document = JsonDocument.Parse(File.ReadAllText(directory.StatePath));
        var names = PropertyNames(document.RootElement).ToArray();
        foreach (var forbidden in new[] { "password", "userName", "members", "current", "duration", "message" })
            Assert.IsFalse(names.Contains(forbidden, StringComparer.OrdinalIgnoreCase), $"Unexpected persisted field '{forbidden}'.");
    }

    [TestMethod]
    public void Validator_AllowsFutureDemoDatesAndUnavailableActiveSession_ButChecksMemberReferences()
    {
        var state = new GymStateSnapshot
        {
            SavedAtUtc = Now.AddYears(1), ClockOffset = TimeSpan.FromDays(365), AdvancedBy = TimeSpan.FromDays(365),
            Equipment = new[] { new Equipment("E1", "Bike") { Status = EquipmentStatus.Unavailable } },
            Sessions = new[] { new SessionState("S1", "E1", "M1", Now.AddMonths(6), null, null) }
        };
        GymStateValidator.Validate(state, new[] { "M1" });
        Assert.ThrowsExactly<InvalidDataException>(() => GymStateValidator.Validate(state, new[] { "M2" }));
    }

    [TestMethod]
    public void InvalidSnapshot_CannotReplacePreviouslySavedState()
    {
        using var directory = new TemporaryDirectory();
        using var store = new JsonGymStateStore(directory.StatePath);
        store.Save(EmptyState());
        var original = File.ReadAllBytes(directory.StatePath);
        foreach (var invalid in InvalidStates())
            Assert.ThrowsExactly<InvalidDataException>(() => store.Save(invalid));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(directory.StatePath));
    }

    private static IEnumerable<GymStateSnapshot> InvalidStates()
    {
        yield return new() { SavedAtUtc = Now, SchemaVersion = GymStateSnapshot.CurrentSchemaVersion + 1 };
        yield return new() { SavedAtUtc = DateTime.SpecifyKind(Now, DateTimeKind.Unspecified) };
        yield return new() { SavedAtUtc = Now, ClockOffset = TimeSpan.FromMinutes(1) };
        yield return new() { SavedAtUtc = Now, NextReportNumber = long.MaxValue };
        yield return new() { SavedAtUtc = Now, Equipment = null! };
        yield return new() { SavedAtUtc = Now, Equipment = new Equipment[] { null! } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") { Status = (EquipmentStatus)9 } } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike"), new Equipment("E1", "Duplicate") } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") { Status = EquipmentStatus.InUse } } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") }, Sessions = new[] { new SessionState("S1", "E1", "M1", Now, null, null) } };
        yield return new() { SavedAtUtc = Now, NudgeCooldowns = new[] { new NudgeCooldown("unknown", "M1", Now) } };
        yield return new() { SavedAtUtc = Now, Cancellations = new[] { new QueueCancellationNotice("M1", "unknown", "Bike") } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") }, Nudges = new[] { new NudgeNotice("S1", "E1", "M1", "M2", Now) } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") { Status = EquipmentStatus.Unavailable } }, Queue = new[] { new QueueEntry("E1", "M1") { JoinedAt = Now } } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") }, Queue = new[] { new QueueEntry("E1", "M1") { JoinedAt = Now }, new QueueEntry("E1", "M2") { JoinedAt = Now, NotifiedAt = Now } } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") }, Reports = new[] { new FaultReport { ReportId = "R-1", EquipmentId = "E1", SubmittedByMemberId = "M1", Description = "Broken", SubmittedAt = Now } } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") { Status = EquipmentStatus.InUse } }, Sessions = new[] { new SessionState("S1", "E1", "M1", DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), null, null) } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") }, Queue = new[] { new QueueEntry("E1", "M1") { JoinedAt = Now, NotifiedAt = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc) } } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") { Status = EquipmentStatus.InUse } }, Sessions = new[] { new SessionState("S1", "E1", "M1", Now, null, null) }, Queue = new[] { new QueueEntry("E1", "M2") { JoinedAt = Now, NotifiedAt = Now } } };
        // Session-owned nudges: the nudge must match the active session, and nobody nudges themselves.
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") { Status = EquipmentStatus.InUse } }, Sessions = new[] { new SessionState("S1", "E1", "M1", Now, null, null) }, Nudges = new[] { new NudgeNotice("S-old", "E1", "M1", "M2", Now) } };
        yield return new() { SavedAtUtc = Now, Equipment = new[] { new Equipment("E1", "Bike") { Status = EquipmentStatus.InUse } }, Sessions = new[] { new SessionState("S1", "E1", "M1", Now, null, null) }, Nudges = new[] { new NudgeNotice("S1", "E1", "M1", "M1", Now) } };
    }

    private static GymStateSnapshot EmptyState(DateTime? savedAt = null) => new() { SavedAtUtc = savedAt ?? Now };

    private static GymStateSnapshot FullState() => new()
    {
        SavedAtUtc = Now, ClockOffset = TimeSpan.FromMinutes(30), AdvancedBy = TimeSpan.FromMinutes(30), NextReportNumber = 3,
        Equipment = new[]
        {
            new Equipment("E1", "Treadmill") { Status = EquipmentStatus.InUse },
            new Equipment("E2", "Bike") { Status = EquipmentStatus.Unavailable }, new Equipment("E3", "Rack")
        },
        Sessions = new[]
        {
            new SessionState("S1", "E1", "M1", Now.AddMinutes(-10), null, null),
            new SessionState("S2", "E2", "M2", Now.AddMinutes(-20), Now.AddMinutes(-18), SessionEndReason.ManualFinish)
        },
        Reports = new[]
        {
            new FaultReport { ReportId = "R-1", EquipmentId = "E1", SubmittedByMemberId = "M2", Description = "Vibration", SubmittedAt = Now.AddMinutes(-9) },
            new FaultReport { ReportId = "R-2", EquipmentId = "E2", SubmittedByMemberId = "M3", Description = "Loose cable", SubmittedAt = Now.AddMinutes(-19), Status = FaultReportStatus.Confirmed, ReviewedByStaffId = "S1", ReviewedAt = Now.AddMinutes(-18) },
            new FaultReport { ReportId = "R-3", EquipmentId = "E3", SubmittedByMemberId = "M2", Description = "Noise", SubmittedAt = Now.AddMinutes(-8), Status = FaultReportStatus.Rejected, ReviewedByStaffId = "S1", ReviewedAt = Now.AddMinutes(-7) }
        },
        Queue = new[]
        {
            new QueueEntry("E1", "M3") { JoinedAt = Now.AddMinutes(-4) },
            new QueueEntry("E3", "M2") { JoinedAt = Now.AddMinutes(-2), NotifiedAt = Now.AddMinutes(-1) },
            new QueueEntry("E3", "M3") { JoinedAt = Now.AddMinutes(-1) }
        },

        // M3 last nudged session S1 a minute ago (the pending nudge below).
        NudgeCooldowns = new[] { new NudgeCooldown("S1", "M3", Now.AddMinutes(-1)) },
        // M3 (front of the E1 queue) nudged M1, who owns active session S1 on E1.
        Nudges = new[] { new NudgeNotice(SessionId: "S1", EquipmentId: "E1", MemberId: "M1", RequestedBy: "M3", ExpiresAt: Now.AddMinutes(1)) },
        Cancellations = new[] { new QueueCancellationNotice("M3", "E2", "Bike") }
    };

    private static IEnumerable<string> PropertyNames(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var name in PropertyNames(property.Value)) yield return name;
            }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray())
                foreach (var name in PropertyNames(item)) yield return name;
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gymq-store-tests-" + Guid.NewGuid().ToString("N"));
        public string StatePath => System.IO.Path.Combine(Path, "state.json");
        public TemporaryDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
