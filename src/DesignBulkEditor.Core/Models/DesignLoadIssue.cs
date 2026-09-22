namespace DesignBulkEditor.Core.Models;

/// <summary> One design file that could not be loaded (e.g. malformed JSON), and why. </summary>
public sealed record DesignLoadIssue(string FilePath, string ErrorMessage);
