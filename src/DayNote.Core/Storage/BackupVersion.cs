namespace DayNote.Core.Storage;

/// <summary>Metadata for one stored backup version (its content is fetched separately on restore).</summary>
public sealed record BackupVersion(string Id, DateTimeOffset CreatedUtc, string ContentHash);
