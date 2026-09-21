namespace GlamourerBulkEditor.Core.Models;

/// <summary> Outcome of staging one <see cref="PendingPropertyEdit"/> onto one design's working copy. </summary>
public sealed record PropertyEditOutcome(string SourceFile, PendingPropertyEdit Edit, bool Success, string? ErrorMessage);
