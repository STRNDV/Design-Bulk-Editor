using DesignBulkEditor.Core.Models;
using DesignBulkEditor.Core.Services;

namespace DesignBulkEditor.Core.Tests;

public class PropertyValueInspectorTests
{
    [Theory]
    [InlineData("true", PropertyValueKind.Boolean)]
    [InlineData("false", PropertyValueKind.Boolean)]
    [InlineData("42", PropertyValueKind.Integer)]
    [InlineData("1.5", PropertyValueKind.Float)]
    [InlineData("\"Some Text\"", PropertyValueKind.Text)]
    public void ClassifyDetectsKindFromJsonShape(string rawJson, PropertyValueKind expected)
    {
        Assert.Equal(expected, PropertyValueInspector.Classify("SomeProperty", rawJson));
    }

    [Theory]
    [InlineData("\"00ff00\"")]
    [InlineData("\"AABBCCDD\"")]
    public void ClassifyDetectsHexColorOnlyWhenPropertyNameMentionsColor(string rawJson)
    {
        Assert.Equal(PropertyValueKind.HexColor, PropertyValueInspector.Classify("SkinColor", rawJson));
        Assert.Equal(PropertyValueKind.Text, PropertyValueInspector.Classify("UnrelatedField", rawJson));
    }

    [Fact]
    public void ClassifyFallsBackToTextForUnparsableInput()
    {
        Assert.Equal(PropertyValueKind.Text, PropertyValueInspector.Classify("SomeProperty", "not json at all"));
    }

    [Fact]
    public void ClassifyFallsBackToTextForNullOrEmptyInput()
    {
        Assert.Equal(PropertyValueKind.Text, PropertyValueInspector.Classify("SomeProperty", null));
        Assert.Equal(PropertyValueKind.Text, PropertyValueInspector.Classify("SomeProperty", string.Empty));
    }

    [Fact]
    public void DecodeForEditingStripsQuotesFromStrings()
    {
        // Regression guard: feeding the quoted JSON text straight back into
        // TrySetPropertyValue would embed literal quote characters in the saved value.
        Assert.Equal("Some Text", PropertyValueInspector.DecodeForEditing("\"Some Text\""));
    }

    [Theory]
    [InlineData("true")]
    [InlineData("42")]
    [InlineData("1.5")]
    public void DecodeForEditingLeavesNonStringsUnchanged(string rawJson)
    {
        Assert.Equal(rawJson, PropertyValueInspector.DecodeForEditing(rawJson));
    }
}
