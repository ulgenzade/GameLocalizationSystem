using LocalizePak.Translation.Services;
using Xunit;

namespace LocalizePak.Tests;

public class PseudolocalizationTests
{
    [Fact]
    public void Transform_BasicString_ExpandsAndAddsDiacritics()
    {
        var input = "Start Game";
        var result = PseudolocalizationEngine.Transform(input);

        Assert.StartsWith("[~!! ", result);
        Assert.EndsWith(" !~]", result);
        Assert.Contains("Şŧàřŧ", result); // S->Ş, t->ŧ, a->à, r->ř, t->ŧ
        Assert.True(result.Length > input.Length);
    }

    [Fact]
    public void Transform_PreservesVariablesAndTags()
    {
        var input = "Found {0} items in <color=red>{ChestName}</color>!";
        var result = PseudolocalizationEngine.Transform(input);

        // Değişkenler kesinlikle bozulmamalıdır
        Assert.Contains("{0}", result);
        Assert.Contains("{ChestName}", result);
        Assert.Contains("<color=red>", result);
        Assert.Contains("</color>", result);
    }

    [Fact]
    public void Transform_PreservesFormatSpecifiers()
    {
        var input = "Health: %d / %d (%s)";
        var result = PseudolocalizationEngine.Transform(input);

        Assert.Contains("%d", result);
        Assert.Contains("%s", result);
    }

    [Fact]
    public void Transform_NonLetterStrings_ReturnsOriginal()
    {
        var input = "1234567890 ++ --";
        var result = PseudolocalizationEngine.Transform(input);

        Assert.Equal(input, result);
    }
}
