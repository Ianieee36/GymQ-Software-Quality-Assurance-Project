using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using GymQ.Models;
using GymQ.Services;

namespace GymQ.Persistence;

/// <summary>One local writer, one versioned file, and a last-known-good backup.</summary>
public sealed class JsonGymStateStore : IGymStateStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { RequireCompleteNestedState } },
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _filePath;
    private readonly string _backupPath;
    private readonly FileStream _writerLease;
    private readonly object _sync = new();
    private bool _disposed;
    private bool _recoveredFromBackup;
    private SnapshotFile? _legacySource;
    private string? _legacyArchivePath;

    public JsonGymStateStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("A gym state file path is required.", nameof(filePath));
        _filePath = Path.GetFullPath(filePath);
        _backupPath = _filePath + ".bak";
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        // Keep the sidecar after disposal: deleting it could let writers lock different inodes.
        _writerLease = new FileStream(_filePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public GymStateSnapshot? Load()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            _recoveredFromBackup = false;
            _legacySource = null;
            _legacyArchivePath = null;
            if (!File.Exists(_filePath))
                return File.Exists(_backupPath) ? RecoverBackup() : null;
            try { return RememberLegacySource(ReadValidatedFile(_filePath)); }
            catch (UnsupportedSchemaException ex) { throw new InvalidDataException(ex.Message, ex); }
            catch (InvalidDataException ex)
            {
                if (File.Exists(_backupPath)) return RecoverBackup();
                throw new InvalidDataException("The gym state file is invalid and no backup is available. The file was preserved.", ex);
            }
        }
    }

    public void Save(GymStateSnapshot state)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            GymStateValidator.Validate(state);
            var data = JsonSerializer.SerializeToUtf8Bytes(state, JsonOptions);
            SnapshotFile? previous = null;
            bool corruptPrimary = false;
            if (File.Exists(_filePath))
            {
                try { previous = ReadValidatedFile(_filePath); }
                catch (UnsupportedSchemaException ex) { throw new InvalidDataException(ex.Message, ex); }
                catch (InvalidDataException)
                {
                    if (!_recoveredFromBackup)
                        throw new InvalidDataException("Load a valid backup before replacing an invalid gym state file. The file was preserved.");
                    ReadBackup();
                    corruptPrimary = true;
                }
            }
            else if (File.Exists(_backupPath))
            {
                if (!_recoveredFromBackup)
                    throw new InvalidDataException("Load the existing gym state backup before saving a new file.");
                ReadBackup();
            }

            var legacy = previous?.SourceVersion == 1 ? previous : _legacySource;
            if (legacy != null && _legacyArchivePath == null)
            {
                // The rotating backup will soon contain version 3. Keep the exact version 1
                // source separately, including when it came from a recovered backup.
                var archive = _filePath + $".v1.{Guid.NewGuid():N}.bak";
                WriteAtomically(archive, legacy.Data);
                _legacyArchivePath = archive;
            }
            if (previous != null) WriteAtomically(_backupPath, previous.Data);
            if (corruptPrimary)
                File.Copy(_filePath, _filePath + $".corrupt.{Guid.NewGuid():N}");
            WriteAtomically(_filePath, data);
            _recoveredFromBackup = false;
            _legacySource = null;
        }
    }

    private GymStateSnapshot RecoverBackup()
    {
        var state = RememberLegacySource(ReadBackup());
        _recoveredFromBackup = true;
        return state;
    }

    private GymStateSnapshot RememberLegacySource(SnapshotFile file)
    {
        if (file.SourceVersion == 1) _legacySource = file;
        return file.State;
    }

    private SnapshotFile ReadBackup()
    {
        try { return ReadValidatedFile(_backupPath); }
        catch (UnsupportedSchemaException ex) { throw new InvalidDataException(ex.Message, ex); }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("The gym state backup is also invalid. Both files were preserved.", ex);
        }
    }

    private static void RequireCompleteNestedState(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(Equipment) && typeInfo.Type != typeof(SessionState) &&
            typeInfo.Type != typeof(FaultReport) && typeInfo.Type != typeof(QueueEntry) &&
            typeInfo.Type != typeof(NudgeNotice) && typeInfo.Type != typeof(NudgeCooldown) &&
            typeInfo.Type != typeof(QueueCancellationNotice))
            return;
        // Null is meaningful for inactive timers and sessions; omission must not silently invent it.
        foreach (var property in typeInfo.Properties.Where(p => p.Get != null && p.Set != null))
            property.IsRequired = true;
    }

    private static SnapshotFile ReadValidatedFile(string path)
    {
        byte[] data = File.ReadAllBytes(path);
        try
        {
            using var document = JsonDocument.Parse(data);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out var schema) ||
                schema.ValueKind != JsonValueKind.Number || !schema.TryGetInt32(out var version))
                throw new InvalidDataException("The gym state file requires an integer schemaVersion.");
            if (version != 1 && version != GymStateSnapshot.CurrentSchemaVersion)
                throw new UnsupportedSchemaException(version);
            var state = version == 1
                ? LegacyGymStateMigration.UpgradeVersion1(data, JsonOptions)
                : JsonSerializer.Deserialize<GymStateSnapshot>(data, JsonOptions)
                ?? throw new InvalidDataException("The gym state file is empty.");
            GymStateValidator.Validate(state);
            return new(state, data, version);
        }
        catch (JsonException ex) { throw new InvalidDataException("The gym state JSON is invalid.", ex); }
    }

    private static void WriteAtomically(string destination, byte[] data)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            // Cleanup cannot change whether the replacement succeeded or hide its original error.
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _writerLease.Dispose();
            _disposed = true;
        }
    }

    private sealed record SnapshotFile(GymStateSnapshot State, byte[] Data, int SourceVersion);
    private sealed class UnsupportedSchemaException(int version)
        : Exception($"Unsupported gym state schema version '{version}'. The file was preserved.") { }
}
