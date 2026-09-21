namespace GlamourerBulkEditor.Core.Models;

/// <summary> Outcome of writing one design to disk. Always returned instead of throwing, so a batch save never aborts partway through. </summary>
public sealed record DesignSaveResult(string SourceFile, bool Success, string? ErrorMessage)
{
    public static DesignSaveResult Ok(string sourceFile) => new(sourceFile, true, null);

    public static DesignSaveResult Failed(string sourceFile, string errorMessage) => new(sourceFile, false, errorMessage);
}
