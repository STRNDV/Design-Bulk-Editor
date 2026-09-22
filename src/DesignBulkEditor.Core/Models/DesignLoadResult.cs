namespace DesignBulkEditor.Core.Models;

/// <summary> Result of scanning a designs folder: every design that loaded successfully, and every file that didn't (with why), instead of one bad file blocking the rest. </summary>
public sealed record DesignLoadResult(IReadOnlyList<GlamourerDesign> Designs, IReadOnlyList<DesignLoadIssue> Issues);
