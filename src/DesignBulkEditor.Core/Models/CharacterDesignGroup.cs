namespace DesignBulkEditor.Core.Models;

/// <summary> All designs sharing the same character name, derived from the "(Character) Design" naming convention. </summary>
public sealed record CharacterDesignGroup(string CharacterName, IReadOnlyList<GlamourerDesign> Designs)
{
    public int Count => Designs.Count;
}
