# GameLocalizationSystem (GLS) Proje Standartları ve Kuralları

Bu kurallar, GameLocalizationSystem projesinde kod yazarken, hata ayıklarken veya yeni özellikler eklerken geçerlidir.

## 1. Dil ve İletişim
- Kullanıcı ile iletişimde her zaman **TÜRKÇE** konuşulur ve yazılır.
- Kod içi XML dokümantasyonları ve log mesajları tutarlı, açık ve anlaşılır olmalıdır.

## 2. C# ve .NET 10 Mimari Standartları
- **File-scoped Namespaces:** Tüm dosyalarda `namespace LocalizePak.Namespace;` formatı kullanılmalıdır.
- **Async/Await & Thread-Safety:** UI kilitlenmelerini önlemek için I/O ve şifreleme/çözme işlemleri arka plan iş parçacıklarında (`Task.Run` veya `IAsyncEnumerable`) çalıştırılmalıdır.
- **Bellek ve Kaynak Yönetimi:**
  - Dosya akışları (`FileStream`, `MemoryStream`), `Process` nesneleri ve kripto nesneleri (`Aes`, `CryptoStream`) daima `using` blokları ile serbest bırakılmalıdır.
  - Decompression bomb veya büyük dosya saldırılarına karşı giriş boyutları sınırlanmalıdır (Pak entry: max 1GB, Locres: max 100MB).
  - Dosya üzerine yazma işlemleri daima GUID damgalı geçici dosyalar (`.tmp`) üzerinden atomik olarak yapılmalıdır.

## 3. Avalonia UI 11 & MVVM
- **CommunityToolkit.Mvvm:** `[ObservableProperty]`, `[RelayCommand]` öznitelikleri kullanılmalıdır.
- **UI Dispatching:** Arka plan iş parçacıklarından UI koleksiyonları güncellenirken daima `Dispatcher.UIThread.Post(...)` veya `InvokeAsync` kullanılmalıdır.
- **Fluent 2 / Koyu Tema:** Renkler ve stiller `FluentTheme` paletiyle uyumlu olmalı; `CardBackgroundFillColorDefault`, `BorderStroke` gibi WinUI 3 standart katmanları korunmalıdır.

## 4. Oyun Paketleri (PAK / Locres / LSPak) Bütünlüğü
- **Unreal PAK:** Unreal Engine dosya dizini (`IndexOffset`) şifreli olabilir, ancak sonundaki footer şifresizdir. Anahtar doğrulaması her zaman gerçek şifreli dizin başlığı üzerinden yapılmalıdır.
- **FString Negatif Boyutlar:** Unreal `FString`'lerde negatif boyutlar UTF-16 belirtir. `int.MinValue` mutlak değer taşması koruması (`Math.Abs` taşması) daima gözetilmelidir.
- **Larian LSPak:** V7–V18 sürüm başlıkları, Zlib/LZ4 sıkıştırma baytları ve XML/LSX UTF-8 BOM kurallarına dikkat edilmelidir.

## 5. Çoklu LLM Yerelleştirme Güvenliği
- **API Anahtarı Güvenliği:** API anahtarları asla URL parametrelerine eklenmemeli, HTTP Request Header (`x-goog-api-key`, `Authorization: Bearer ...`) içinde gönderilmelidir.
- **İstek Başına Header:** `HttpClient.DefaultRequestHeaders` kesinlikle çalışma zamanında dinamik mutasyona uğratılmamalıdır (race condition önlenir). İstek bazlı `HttpRequestMessage.Headers` kullanılmalıdır.
- **Değişken Maskeleme:** Oyun motoru değişkenleri (`{0}`, `%s`, `<Tag>...</Tag>`, `[var_0]`) çeviri modellerine gönderilmeden önce benzersiz token'larla maskelenmeli ve çeviri sonrası toleranslı olarak geri yüklenmelidir.
