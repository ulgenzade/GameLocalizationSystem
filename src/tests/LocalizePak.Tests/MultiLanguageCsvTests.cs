using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LocalizePak.Core.Enums;
using LocalizePak.Core.Models;
using LocalizePak.IO.Formats;
using Xunit;

namespace LocalizePak.Tests;

public class MultiLanguageCsvTests
{
    // PEAK tarzı: virgül ayırıcı, BOM, CRLF, tırnaklı/çok satırlı hücreler, mevcut Türkçe sütun
    private const string PeakStyle =
        "CURRENT_LANGUAGE,English,Français,Türkçe,ENDLINE\r\n" +
        "HOSTGAME,HOST GAME,HÉBERGER,OYUN KUR,ENDLINE\r\n" +
        "MODWARNING,\"Warning, modded game.\nAre you sure?\",\"Attention, partie moddée.\",,ENDLINE\r\n" +
        "QUOTE,\"Say \"\"hi\"\"\",\"Dis \"\"salut\"\"\",,ENDLINE\r\n";

    private static UniversalLocalizationService CreateService(string source = "en", string target = "tr")
        => new() { LanguagePreference = () => (source, target) };

    private static async Task<LocresFile> ParseAsync(byte[] bytes, UniversalLocalizationService? svc = null)
        => await (svc ?? CreateService()).DeserializeAsync(new MemoryStream(bytes));

    private static byte[] Utf8(string s, bool bom = false)
    {
        var body = Encoding.UTF8.GetBytes(s);
        return bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    private static string Text(byte[] bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('﻿');

    [Fact]
    public async Task CommaCsv_DetectsSourceAndTargetColumns()
    {
        var file = await ParseAsync(Utf8(PeakStyle, bom: true));

        Assert.NotNull(file.Table);
        Assert.Equal(',', file.Table!.Delimiter);
        Assert.Equal("English", file.Table.Headers[file.Table.SourceColumn]);
        Assert.Equal("Türkçe", file.Table.Headers[file.Table.TargetColumn]);

        var entries = file.AllEntries.ToDictionary(e => e.Key);
        Assert.Equal(3, entries.Count);
        Assert.Equal("Warning, modded game.\nAre you sure?", entries["MODWARNING"].SourceText);
        Assert.Equal("Say \"hi\"", entries["QUOTE"].SourceText);

        // Dolu Türkçe hücre hazır çeviri olarak gelir; boş olan bekler
        Assert.Equal(TranslationStatus.Done, entries["HOSTGAME"].Status);
        Assert.Equal("OYUN KUR", entries["HOSTGAME"].TranslatedText);
        Assert.Equal(TranslationStatus.Pending, entries["MODWARNING"].Status);
    }

    [Fact]
    public async Task CommaCsv_UnchangedRoundTrip_IsByteIdentical()
    {
        var original = Utf8(PeakStyle, bom: true);
        var file = await ParseAsync(original);

        var written = await CreateService().SerializeAsync(file);

        Assert.Equal(original, written);
    }

    [Fact]
    public async Task CommaCsv_Edit_ChangesOnlyTargetColumnAndQuotesWhenNeeded()
    {
        var file = await ParseAsync(Utf8(PeakStyle, bom: true));
        var entries = file.AllEntries.ToDictionary(e => e.Key);
        entries["MODWARNING"].TranslatedText = "Uyarı, modlu oyun.\nEmin misin?";
        entries["MODWARNING"].Status = TranslationStatus.Done;
        entries["QUOTE"].TranslatedText = "\"Merhaba\" de";
        entries["QUOTE"].Status = TranslationStatus.Done;

        var written = await CreateService().SerializeAsync(file);
        var text = Text(written);

        Assert.Contains("MODWARNING,\"Warning, modded game.\nAre you sure?\",\"Attention, partie moddée.\",\"Uyarı, modlu oyun.\nEmin misin?\",ENDLINE\r\n", text);
        Assert.Contains("QUOTE,\"Say \"\"hi\"\"\",\"Dis \"\"salut\"\"\",\"\"\"Merhaba\"\" de\",ENDLINE\r\n", text);
        Assert.StartsWith("CURRENT_LANGUAGE,English,Français,Türkçe,ENDLINE\r\nHOSTGAME,HOST GAME,HÉBERGER,OYUN KUR,ENDLINE\r\n", text);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, written[..3]);

        // Yeniden okununca çeviriler hedef sütundan gelir
        var reread = (await ParseAsync(written)).AllEntries.ToDictionary(e => e.Key);
        Assert.Equal("Uyarı, modlu oyun.\nEmin misin?", reread["MODWARNING"].TranslatedText);
        Assert.Equal("\"Merhaba\" de", reread["QUOTE"].TranslatedText);
        Assert.Equal("Warning, modded game.\nAre you sure?", reread["MODWARNING"].SourceText);
    }

    [Fact]
    public async Task PendingRowWithEmptyTarget_IsLeftEmpty()
    {
        var file = await ParseAsync(Utf8(PeakStyle));
        var text = Text(await CreateService().SerializeAsync(file));

        // Çevrilmemiş satırın boş Türkçe hücresine İngilizce kopyalanmaz
        Assert.Contains("\"Attention, partie moddée.\",,ENDLINE", text);
    }

    [Fact]
    public async Task Tsv_WithoutQuoting_KeepsInnerQuotesAndLoneCarriageReturns()
    {
        // Travellers Rest tarzı: sekme ayırıcı, hiç tırnaklama yok, metin içinde " ve tek başına \r
        var tsv =
            "StringID\tEnglish\tGerman\tTurkish\n" +
            "tip\tUse <color=\"blue\">tools</color>\tWerkzeuge\t\n" +
            "odd\tLine\rbreak\tZeile\tSatır\n";
        var original = Utf8(tsv);
        var file = await ParseAsync(original);

        Assert.Equal('\t', file.Table!.Delimiter);
        Assert.False(file.Table.UsesQuotes);
        Assert.Equal(2, file.Table.Rows.Count - 1);
        Assert.Equal(original, await CreateService().SerializeAsync(file));

        var tip = file.AllEntries.Single(e => e.Key == "tip");
        Assert.Equal("Use <color=\"blue\">tools</color>", tip.SourceText);
        tip.TranslatedText = "<color=\"blue\">Aletleri</color> kullan";
        tip.Status = TranslationStatus.Done;

        // Tırnak kullanmayan dosyada " kaçışlanmaz; oyun düz bölme yapıyorsa "" görünürdü
        var text = Text(await CreateService().SerializeAsync(file));
        Assert.Contains("tip\tUse <color=\"blue\">tools</color>\tWerkzeuge\t<color=\"blue\">Aletleri</color> kullan\n", text);
        Assert.Contains("odd\tLine\rbreak\tZeile\tSatır\n", text);
    }

    [Fact]
    public async Task MixedLineEndingsAndNoTrailingNewLine_ArePreserved()
    {
        var original = Utf8("key;English;Deutsch\r\na;One;Eins\nb;Two;Zwei");
        var file = await ParseAsync(original);

        Assert.Equal(';', file.Table!.Delimiter);
        Assert.Equal(original, await CreateService().SerializeAsync(file));
    }

    [Fact]
    public async Task NoTargetLanguageColumn_WritesIntoSourceColumn()
    {
        var file = await ParseAsync(Utf8("Key,English,German\nhello,Hello,Hallo\n"));
        Assert.Equal(file.Table!.SourceColumn, file.Table.TargetColumn);

        var entry = file.AllEntries.Single();
        entry.TranslatedText = "Merhaba";
        entry.Status = TranslationStatus.Done;

        Assert.Equal("Key,English,German\nhello,Merhaba,Hallo\n", Text(await CreateService().SerializeAsync(file)));
    }

    [Fact]
    public async Task SelectedSourceLanguage_PicksThatColumn()
    {
        var svc = CreateService(source: "de", target: "tr");
        var file = await ParseAsync(Utf8("Key,English,German,Turkish\nhello,Hello,Hallo,\n"), svc);

        var entry = file.AllEntries.Single();
        Assert.Equal("Hallo", entry.SourceText);
        Assert.Equal("Turkish", file.Table!.Headers[file.Table.TargetColumn]);
    }

    [Fact]
    public async Task HeaderlessTable_UsesMostTextualColumnAsSource()
    {
        var file = await ParseAsync(Utf8("1,10,Open the door\n2,20,Close the door\n3,30,Ring the bell\n"));

        Assert.Equal(-1, file.Table!.HeaderRow);
        Assert.Equal(2, file.Table.SourceColumn);
        Assert.Equal(new[] { "Open the door", "Close the door", "Ring the bell" }, file.AllEntries.Select(e => e.SourceText));
    }

    [Fact]
    public async Task PlainTextLines_AreNotTreatedAsTable()
    {
        var file = await ParseAsync(Utf8("Hello there\nGeneral Kenobi\nYou are a bold one\n"));
        Assert.Null(file.Table);
    }

    [Fact]
    public async Task IniWithCommasInValues_IsNotSplitIntoColumns()
    {
        var file = await ParseAsync(Utf8("[Strings]\ngreet=Hello, traveller\nbye=See you, friend\n"));
        Assert.Null(file.Table);
    }

    [Theory]
    [InlineData("English", "en")]
    [InlineData("Türkçe", "tr")]
    [InlineData("Turkish", "tr")]
    [InlineData("Français", "fr")]
    [InlineData("pt-BR", "pt")]
    [InlineData("Español (España)", "es")]
    [InlineData("Simplified Chinese", "zh")]
    [InlineData("简体中文", "zh")]
    [InlineData("Русский", "ru")]
    public void TryMapLanguage_RecognisesCommonHeaders(string header, string iso)
    {
        Assert.True(DelimitedTableCodec.TryMapLanguage(header, out var actual));
        Assert.Equal(iso, actual);
    }

    [Fact]
    public void TryMapLanguage_RejectsNonLanguageHeaders()
    {
        Assert.False(DelimitedTableCodec.TryMapLanguage("StringID", out _));
        Assert.False(DelimitedTableCodec.TryMapLanguage("ENDLINE", out _));
    }
}
