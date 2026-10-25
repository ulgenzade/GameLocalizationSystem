namespace LocalizePak.Core.Models;

/// <summary>CSV/TSV hücresi: değer ve dosyada tırnak içinde yazılıp yazılmadığı (geri yazarken korunur).</summary>
public sealed class DelimitedCell
{
    public string Value { get; set; } = string.Empty;
    public bool Quoted { get; set; }

    /// <summary>Dosyadan okunan değer; değişmeyen hücreler bayt bayt aynı yazılır (yeni eklenen hücrede null).</summary>
    public string? OriginalValue { get; init; }

    /// <summary>Hücrenin dosyadaki ham metni (tırnaklar dahil).</summary>
    public string? RawText { get; init; }
}

/// <summary>
/// Çok sütunlu (ör. <c>Key, English, French, …, Türkçe</c>) yerelleştirme tablosu. Dosyanın tamamı biçimiyle
/// birlikte saklanır; geri yazarken yalnızca hedef sütun değişir — diğer diller, başlık, yorum satırları,
/// tırnaklama, satır sonu ve BOM korunur.
/// </summary>
public sealed class DelimitedTable
{
    public char Delimiter { get; init; } = ',';
    public bool HasBom { get; init; }

    /// <summary>Dosyada tırnaklı hücre var mı (yoksa oyun büyük olasılıkla düz bölme yapıyordur; tırnaklama asgaride tutulur).</summary>
    public bool UsesQuotes { get; init; }

    /// <summary>Tüm satırlar (başlık dahil), dosyadaki sırayla.</summary>
    public List<List<DelimitedCell>> Rows { get; init; } = [];

    /// <summary>Her satırın sonundaki satır sonu ("\r\n", "\n" veya son satırda ""); karışık satır sonları da korunur.</summary>
    public List<string> RowEndings { get; init; } = [];

    /// <summary>Başlık satırının dizini (yoksa -1).</summary>
    public int HeaderRow { get; init; }

    public int KeyColumn { get; init; }
    public int SourceColumn { get; init; }

    /// <summary>Çevirinin yazılacağı sütun. Hedef dil sütunu yoksa kaynak sütunla aynıdır (kaynak metin üzerine yazılır).</summary>
    public int TargetColumn { get; init; }

    /// <summary>Başlıklar (teşhis/arayüz için; başlık yoksa boş).</summary>
    public IReadOnlyList<string> Headers => HeaderRow >= 0 ? Rows[HeaderRow].Select(c => c.Value).ToList() : [];

    /// <summary>Çeviri girdisi → tablo satırı eşlemesi (aynı nesne referansları).</summary>
    public List<(int Row, LocalizationEntry Entry)> EntryRows { get; init; } = [];
}
