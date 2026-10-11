# 🤝 CONTRIBUTING.md — Katkı Rehberi

GameLocalizationSystem projesine katkıda bulunmak için aşağıdaki standartları takip edin.

---

## Geliştirme Ortamı Kurulumu

```bash
# Gereksinimler
dotnet --version  # 8.0 veya üzeri olmalı

# Projeyi klonla
git clone https://github.com/kullanici/GameLocalizationSystem.git
cd GameLocalizationSystem/src

# Bağımlılıkları yükle
dotnet restore

# Derleme
dotnet build

# Testleri çalıştır
dotnet test
```

---

## Kod Standartları

### C# Kodlama Kuralları
- Microsoft [C# Coding Conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions) standartlarına uy
- `PascalCase` — sınıf, metot, property adları
- `camelCase` — yerel değişkenler ve parametreler
- `_camelCase` — private field adları
- `I` prefix — arayüz adları (`ITranslationProvider`)

### async/await Kuralları
- Tüm I/O işlemleri `async` olmalı
- `ConfigureAwait(false)` kütüphane katmanlarında zorunlu
- Senkron I/O bloklama yasak (UI thread kilitlenmesi riski)

### XML Belgeleme
```csharp
/// <summary>
/// .pak arşivini açar ve içindeki lokalizasyon dosyalarını listeler.
/// </summary>
/// <param name="path">Arşiv dosyasının tam yolu.</param>
/// <param name="aesKey">Şifreli arşivler için 256-bit Hex AES anahtarı. Opsiyonel.</param>
/// <returns>Arşiv içindeki .locres dosyalarının yolu listesi.</returns>
public Task<IReadOnlyList<string>> ListLocresFilesAsync(string path, string? aesKey = null)
```

---

## Birim Test Yazma Kuralları

- Her yeni I/O veya ayrıştırma modülü için **xUnit** testi zorunlu
- Test dosyası adı: `[SınıfAdı]Tests.cs`
- Test metot adı: `[Metot]_[Senaryo]_[BeklenenSonuç]`

```csharp
[Fact]
public async Task OpenAsync_WithValidAesKey_ReturnsLocresFiles()
{
    // Arrange
    var service = new PakService();
    
    // Act
    var files = await service.OpenAsync("test.pak", "0xABCD...");
    
    // Assert
    Assert.NotEmpty(files);
}
```

---

## Pull Request Süreci

1. Feature branch oluştur: `git checkout -b feature/oodle-support`
2. Değişikliklerini commit'le (anlamlı mesajlarla)
3. `dotnet test` çalıştır — tüm testler geçmeli
4. `dotnet format` ile kodu formatla
5. PR aç ve şablonu doldur

### Commit Mesaj Formatı

```
feat: Oodle sıkıştırma desteği eklendi
fix: AES anahtar doğrulama hatası giderildi
docs: WORKFLOW.md güncellendi
test: PakService birim testleri eklendi
refactor: MaskingEngine regex optimizasyonu
```

---

## Yeni AI Servis Entegrasyonu

Yeni bir AI servis eklemek için:

1. `LocalizePak.Translation/Providers/` altında yeni sınıf oluştur
2. `ITranslationProvider` arayüzünü uygula:

```csharp
public class MyNewProvider : ITranslationProvider
{
    public string Name => "MyService";
    public Task<IReadOnlyList<string>> TranslateBatchAsync(
        IReadOnlyList<string> texts,
        TranslationContext context,
        CancellationToken ct = default);
}
```

3. `ServiceProviderFactory.cs`'e kaydet
4. `SettingsViewModel.cs`'deki provider listesine ekle
5. Birim testi yaz

---

## Lisans

Tüm katkılar MIT Lisansı altında yayımlanacaktır.
