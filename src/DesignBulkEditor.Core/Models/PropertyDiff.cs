namespace DesignBulkEditor.Core.Models;

/// <summary> One changed value between a design's on-disk baseline and its current working copy, for review before saving. </summary>
public sealed record PropertyDiff(string Path, string? OldValue, string? NewValue);
