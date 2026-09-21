namespace DesignBulkEditor.Core.Models;

/// <summary> One automatic backup of a design file, taken before it was overwritten. </summary>
public sealed record BackupEntry(string FilePath, DateTime TimestampUtc);
