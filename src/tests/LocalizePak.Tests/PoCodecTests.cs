using System.Text;
using LocalizePak.Core.Enums;
using LocalizePak.IO.Formats;
using Xunit;

namespace LocalizePak.Tests;

public class PoCodecTests
{
    private const string SamplePoContent = """
        # Translation of MyGame into Turkish
        # Author: Ulgen
        msgid ""
        msgstr ""
        "Project-Id-Version: MyGame 1.0\n"
        "Content-Type: text/plain; charset=UTF-8\n"

        #: Source/UI/Menu.cpp:42
        msgctxt "MainMenu"
        msgid "Start Game"
        msgstr "Oyunu Başlat"

        #: Source/UI/Menu.cpp:43
        msgctxt "MainMenu"
        msgid "Settings"
        msgstr "Ayarlar"

        #: Source/Dialogue/Quest.cpp:10
        msgid "Hello, adventurer!\nWelcome to our village."
        msgstr "Merhaba maceracı!\nKöyümüze hoş geldin."

        msgid "Unfinished Quest"
        msgstr ""
        """;

    [Fact]
    public void IsPoContent_DetectsCorrectly()
    {
        Assert.True(PoCodec.IsPoContent(SamplePoContent));
        Assert.False(PoCodec.IsPoContent("Just some plain text without PO structure."));
        Assert.False(PoCodec.IsPoContent("<contentList><content>Hi</content></contentList>"));
    }

    [Fact]
    public void Parse_ParsesEntriesWithContextAndMultiline()
    {
        var locres = PoCodec.Parse(SamplePoContent, "Game.po");

        Assert.Equal(4, locres.TotalEntryCount);

        var startEntry = locres.AllEntries.FirstOrDefault(e => e.SourceText == "Start Game");
        Assert.NotNull(startEntry);
        Assert.Equal("Oyunu Başlat", startEntry.TranslatedText);
        Assert.Equal("MainMenu", startEntry.NamespaceName);
        Assert.Equal(TranslationStatus.Done, startEntry.Status);
        Assert.Contains("Menu.cpp:42", startEntry.Notes);

        var questEntry = locres.AllEntries.FirstOrDefault(e => e.SourceText.StartsWith("Hello, adventurer!"));
        Assert.NotNull(questEntry);
        Assert.Equal("Merhaba maceracı!\nKöyümüze hoş geldin.", questEntry.TranslatedText);
        Assert.Equal(TranslationStatus.Done, questEntry.Status);

        var pendingEntry = locres.AllEntries.FirstOrDefault(e => e.SourceText == "Unfinished Quest");
        Assert.NotNull(pendingEntry);
        Assert.Equal(string.Empty, pendingEntry.TranslatedText);
        Assert.Equal(TranslationStatus.Pending, pendingEntry.Status);
    }

    [Fact]
    public void Roundtrip_SerializeAndParse_PreservesContent()
    {
        var original = PoCodec.Parse(SamplePoContent, "Game.po");
        var serialized = PoCodec.Serialize(original);

        Assert.Contains("msgctxt \"MainMenu\"", serialized);
        Assert.Contains("msgid \"Start Game\"", serialized);
        Assert.Contains("msgstr \"Oyunu Başlat\"", serialized);

        var roundtripped = PoCodec.Parse(serialized, "Game.po");
        Assert.Equal(original.TotalEntryCount, roundtripped.TotalEntryCount);

        var entry = roundtripped.AllEntries.FirstOrDefault(e => e.SourceText == "Start Game");
        Assert.NotNull(entry);
        Assert.Equal("Oyunu Başlat", entry.TranslatedText);
    }

    [Fact]
    public async Task UniversalLocalizationService_ParsesAndSerializesPo()
    {
        var service = new UniversalLocalizationService();
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(SamplePoContent));

        var locres = await service.DeserializeAsync(ms);
        locres.SourcePath = "tr/Game.po";

        Assert.Equal(4, locres.TotalEntryCount);

        var bytes = await service.SerializeAsync(locres);
        var text = Encoding.UTF8.GetString(bytes);

        Assert.Contains("msgid \"Start Game\"", text);
        Assert.Contains("msgstr \"Oyunu Başlat\"", text);
    }
}
