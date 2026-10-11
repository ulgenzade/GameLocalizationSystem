# **Unreal Engine Oyunlarında .pak Tabanlı Yapay Zeka Destekli Türkçe Yerelleştirme Yazılımı Mimarisi ve Teknoloji Araştırma Raporu**

## **Masaüstü Uygulaması Teknolojik Yığın ve Programlama Dili Analizi**

Oyun yerelleştirme yazılımları, yüksek hacimli ikili (binary) dosya I/O işlemleri, bellek içi veri manipülasyonu, eşzamanlı (concurrent) ağ istekleri ve karmaşık kullanıcı arayüzü (UI) etkileşimlerini aynı anda yönetmek zorundadır. Bu gereksinimler doğrultusunda, uygulama geliştirme sürecinde değerlendirilen başlıca teknolojik yığınlar; performans, bellek yönetimi, ikili dosya işleme kütüphane ekosistemi ve çapraz platform desteği açısından analiz edilmiştir.  
.NET ekosistemi (C\#), Unreal Engine arşiv yapılarının (.pak, .utoc) ayrıştırılması ve yerelleştirme dosyalarının (.locres) işlenmesi konusunda gelişmiş açık kaynak kütüphane desteğine sahiptir. C\# dilinin Span\<T\>, Memory\<T\> ve Unsafe gibi bellek optimizasyon mekanizmaları, gigabaytlarca büyüklükteki .pak dosyalarının kopyalama yapılmadan (zero-allocation parsing) bellek üzerinden okunmasını sağlar. Avalonia UI çerçevesi ise WPF benzeri bir MVVM mimarisi sunarken Windows, macOS ve Linux üzerinde yüksek GPU hızlandırmalı arayüz oluşturma imkanı tanır.  
Rust ve Tauri kombinasyonu, bellek güvenliği ve düşük kaynak kullanımı açısından üstün performans gösterir. Rust dili için geliştirilmiş repak gibi araçlar, .pak indekslerini son derece hızlı işleyebilmektedir. Ancak, karmaşık görsel veri tabloları (DataGrid) ve gelişmiş metin editörü bileşenlerinin Tauri (Webview2/WebKit) üzerinde sıfırdan geliştirilmesi veya uyarlanması ek geliştirme maliyeti getirmektedir.  
Python (PySide6 / PyQt6), pyuepak ve pylocres gibi hızlı prototipleme kütüphanelerine sahip olmasına karşın, yorumlamalı bir dil olması ve GIL (Global Interpreter Lock) kısıtlaması nedeniyle yüz binlerce satırlık metin bloklarının eşzamanlı ayrıştırılması ve büyük arşiv dosyalarının yeniden paketlenmesi süreçlerinde işlemci ve bellek darboğazları oluşturmaktadır.

| Teknoloji Yığını | Çalışma Zamanı Performansı | Bellek Tüketimi | İkili (Binary) Dosya İşleme Yeteneği | Çapraz Platform Desteği | Geliştirme Verimliliği ve Ekosistem |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **C\# (.NET 8/9) \+ Avalonia UI** | Çok Yüksek (JIT/AOT) | Orta (\~80-150 MB) | Yerel (Span\<T\>, CUE4Parse, LocresLib) | Tam (Windows, macOS, Linux) | Mükemmel (Hazır .pak/.locres kütüphaneleri) |
| **C\# (.NET 8\) \+ WPF** | Yüksek (JIT) | Orta (\~100-180 MB) | Yerel (Unpaker, Lo\[span\_2\](start\_span)\[span\_2\](end\_span)\[span\_6\](start\_span)\[span\_6\](end\_span)\[span\_10\](start\_span)\[span\_10\](end\_span)cresLib) | Yok (Sadece Windows) | Yüksek (Zengin UI ekosistemi) |
| **Rust \+ Tauri (TS/Svelte)** | En Yüksek (Yerel) | Düşük (\~30-60 MB) | Mükemmel (re\[span\_14\](start\_span)\[span\_14\](end\_span)pak C-ABI bağlama) | Tam (Windows, macOS, Linux) | Orta (Arayüz bileşen zinciri karmaşık) |
| **Python \+ PySide6** | Düşük / Orta | Yüksek (\~200-400 MB) | Yavaş (struct, pyuepak) | Tam (Windows, macOS, Linux) | Yüksek Prototipleme / Düşük Ölçekleme |
| **C++ \+ Qt 6** | En Yüksek (Yerel) | Düşük (\~40-80 MB) | Yüksek (Manuel pointer / Zlib / Oodle) | Tam | Düşük (Yüksek geliştirme ve bakım süresi) |

Analiz sonuçları, projenin ana omurgası için C\# (.NET 8/9) ve Avalonia UI yığınının en uygun seçenek olduğunu göstermektedir. Unreal Engine yerelleştirme standart dosyaları olan .locres formatını tam sürümlerle (v1-v3) okuyup yazabilen LocresLib ve .pak/.utoc dosyalarını ayrıştıran CUE4Parse ile Unpaker kütüphaneleri doğrudan C\# dilinde yazılmıştır. Bu durum, ikili format adaptörlerinin yeniden yazılması riskini ortadan kaldırır. Ayrıca, yapay zeka API'lerine yapılacak yüzlerce eşzamanlı HTTP/REST isteğinin ve JSON serileştirme süreçlerinin Task, async/await ve HttpClientFactory ile yönetimi C\# ekosisteminde son derece stabildir. Oyunlarda 100.000 satırı aşabilen metin dizilerinin kullanıcıya performans kaybı yaşatılmadan liste görünümünde sunulması, Avalonia'nın gelişmiş DataGrid sanallaştırma altyapısı ile doğrudan çözülmektedir.

## **Unreal Engine .pak ve .locres Dosya Biçimlerinin İşleme Metotları**

Unreal Engine platformundaki oyunlar, varlıklarını (assets) ve metin kaynaklarını sıkıştırılmış veya şifrelenmiş kapsayıcı (container) dosyalarında depolar. Başarılı bir Türkçe yerelleştirme işlemi, bu ikili dosyaların iç yapısının eksiksiz anlaşılmasını gerektirir.  
.pak dosyaları, dosya verilerinin ve en sonda yer alan bir indeks dizininin (Footer ve Index) bulunduğu arşiv yapılarıdır. Okuma işlemi dosyanın sonundaki sabit boyutlu offsetten başlar. Dosya içerikleri genellikle Zlib, Gzip, LZ4, Zstd veya Epic Games'in yüksek oranlı sıkıştırma kütüphanesi olan Oodle (oo2core\_9\_win64.dll) ile sıkıştırılır. Uygulamanın Oodle kütüphanesini dinamik yükleme (P/Invoke) yeteneğine sahip olması kritik önem taşır. Birçok modern oyun, .pak indeksini ve içeriklerini AES-256-CBC algoritması ile şifreler. Ayrıştırma motoru, kullanıcının girdi olarak sunduğu 256-bit Hex formatındaki AES anahtarını doğrulamalı ve bellek içi şifre çözme (in-memory decryption) gerçekleştirmelidir. Unreal Engine 5 ile birlikte gelen IO Store mimarisinde metinler ve varlıklar sanal blok indekslerine (.utoc/.ucas) bölünmüştür. Eski tarz .pak dosyalarının yanı sıra retoc veya CUE4Parse benzeri mantıklar entegre edilerek .utoc kapsayıcıları da taranabilmelidir.  
Unreal Engine metin içeriklerini Text Localization Resource (.locres) dosyalarında saklar. Bu dosya yapısı, düz bir metin dosyası olmayıp karmaşık bir karma tablo (hash table) mimarisine sahiptir. Dosya yapısı; Magic Identifier (GUID: 0x0E147475674A03FC...) ve sürüm bilgisini içeren başlık (Header) alanıyla başlar. Metinler UTF-16LE veya UTF-8 formatında dizgiler dizisi (String Array) olarak depolanır. Metinlerin oyundaki hangi kod bloğuna ait olduğunu belirten Namespace (İsim Alanı), benzersiz Key (Anahtar) ve bu anahtarların kaynak metindeki CityHash / CRC32 değerlerini tutan dizin tabloları yer alır. Uygulamanın .locres ayrıştırma aşamasında, orijinal metin anahtarlarını bozmadan yalnızca metin değerlerini (Value string) değiştirmesi, güncelleme sonrası dosyayı doğru CRC/CityHash değerleri ve sürüm bilgisiyle yeniden serileştirmesi gerekmektedir.

| Kütüphane / Araç | Dil | Desteklenen Biçimler | Sıkıştırma / Şifreleme | Proje İçi Kullanım Yöntemi |
| :---- | :---- | :---- | :---- | :---- |
| **Unpaker** | C\# | .pak (UE4 V0-V11) | Zlib, Gzip, Oodle, LZ4, Zstd / AES-256 | Doğrudan NuGet veya C\# kaynak kodu modülü olarak entegrasyon |
| **CUE4Parse** | C\# | .pak, .utoc, .ucas (UE4 & UE5) | Tüm UE Sıkıştırmaları / AES Auto-detect | Gelişmiş okuma, veri tablosu (DataTable, StringTable) çıkarma |
| **LocresLib / UnrealLocres** | C\# | .locres (Sürüm 1 \- 3\) | N/A (İkili Metin Ayrıştırma) | .locres dosyalarını bellek içi nesne modeline dönüştürme ve yazma |
| **repak** | Rust | .pak (UE1 \- V11) | Zlib, Oodle / AES-256 | CLI aracı veya FFI (Foreign Function Interface) köprülemesi |
| **pylocres** | Python | .locres, .locmeta | N/A | CSV/PO dönüştürme ve CityHash mantığı referansı |

## **Maliyet Etkin Yapay Zeka Çeviri API'leri, Özel İsim Koruma ve Direktif Yönetimi**

Büyük ölçekli bir oyunda 500.000 ila 2.000.000 arasında kelime bulunabilmektedir. Geleneksel çeviri servislerinin (örneğin DeepL API) yüksek maliyeti ve esneklik sunmaması göz önüne alındığında, gelişmiş Prompt mühendisliğini destekleyen ve yüksek bağlam penceresi sunan LLM (Large Language Model) API'lerinin kullanımı maliyetleri kritik oranda düşürmektedir.  
Yapay zeka modellerinin oyun çevirisindeki başarısı yalnızca token başına fiyata değil, aynı zamanda sistem yönergelerine uyum (system prompt adherence), özel isimleri tanıma ve format koruma yeteneklerine bağlıdır. Google Gemini 2.5/3.5/3.7 Flash, OpenAI GPT-4o mini ve DeepSeek V3/V4 Flash modelleri oyun yerelleştirme süreçlerinde en yüksek fiyat/performans oranını sunmaktadır.

| Yapay Zeka Servisi | Girdi Fiyatı (1M Token) | Çıktı Fiyatı (1M Token) | Bağlam Önbellekleme (Context Caching) | Özel Yönerge ve Terim Sadakati | Maliyet/Performans Derecesi |
| :---- | :---- | :---- | :---- | :---- | :---- |
| **Google Gemini 2.5 / 3.5 Flash** | $0.25 \- $0.30 | $1.50 \- $2.50 | Var (%75-%90 indirimli) | Mükemmel (1M+ bağlam ile oyun içi glosary iletimi) | **En Yüksek** (Ana Çeviri Motoru) |
| **OpenAI GPT-4o mini** | $0.15 | $0.60 | Var (%50 indirimli) | Yüksek (Kısa metinlerde hızlı ve tutarlı) | **Çok Yüksek** (İkincil / Yedek Motor) |
| **DeepSeek V3 / V4 Flash** | $0.14 \- $0.28 | $0.28 \- $0.66 | Var (Düşük Maliyetli Cache Hit) | Yüksek (Dil bilgisi ve Türkçe uyumu çok güçlü) | **Yüksek** (Ekonomik Alternatif) |
| **DeepL API (Developer)** | \~$27.50 / 1M Karakter | N/A (Karakter başı) | Yok | Sınırlı (Sadece sabit terim listesi, prompt alamaz) | **Düşük** (Yüksek Maliyet / Esneklik Yok) |

DeepL API, karakter bazlı fiyatlandırması ve LLM esnekliğine sahip olmaması (kullanıcı direktiflerini dinamik olarak işleyememesi) nedeniyle bu mimaride ana motor olarak tercih edilmemektedir.  
Oyunlardaki kişi isimleri, yer adları, büyü veya silah isimlerinin (örneğin *Winterfell*, *Shadowmourne*, *Cloud Strife*) Türkçe çeviri sırasında yanlışlıkla kelime anlamına göre çevrilmesini önlemek amacıyla üç kademeli bir koruma mekanizması uygulanır. İlk olarak, çeviri öncesinde metinler taranarak büyük harfle başlayan ikili/üçlü kelime grupları, tırnak içindeki terimler ve oyun içi kod anahtarları otomatik varlık tespiti (Named Entity Recognition \- NER) ile belirlenir. İkinci olarak, kullanıcının tanımladığı veya sistemin otomatik çıkardığı özel isimler dizisi API isteğinin sistem talimatı alanına JSON formatında dinamik sözlük (Glossary) olarak eklenir. Son olarak, oyun motoruna ait metin içi değişkenlerin ({0}, {PlayerName}, %s, \\n, \<RichColor\>...\</\>) çeviri sırasında bozulmasını önlemek için metinler API'ye gönderilmeden önce Regex analizörü ile rastgele GUID benzeri etiketlerle maskelenir (\[VAR\_01\]), çeviriden gelen yanıtta maskeler tekrar orijinal değişken kodlarıyla değiştirilir.  
Kullanıcı arayüz üzerinden çeviriye müdahale edebilmektedir. Örneğin kullanıcı *"Oyun orta çağ temalıdır, üslubu kasvetli ve epik yap, asker rütbelerini Türkçe askeri terimlerle eşleştir"* şeklinde bir talimat girebilir. Bu direktifler, LLM isteminin (prompt) tepesine dinamik olarak eklenir. Gemini Flash veya GPT-4o mini modellerinde Context Caching kullanılarak, oyunun genel hikaye özeti ve ana terimler sözlüğü bir kez önbelleğe alınır. Böylece her batch işleminde aynı bağlam tekrar gönderilse dahi token maliyeti %80'e varan oranda düşmektedir.

## **Sistem Mimarisi ve Yerelleştirme İş Akışı Hattı (Pipeline)**

Geliştirilecek masaüstü uygulamasının veri akışı ve katmanlı yazılım mimarisi, modülerlik ve yüksek hata toleransı ilkesine göre kesintisiz bir hatta dönüştürülmüştür.  
Süreç, kullanıcının .pak dosyasını ve gerekiyorsa 256-bit Hex AES anahtarını arayüz üzerinden yüklemesiyle başlar. PakArchiveEngine bileşeni, ikili indeks yapısını okur ve bellek üzerinde filtreleme yaparak yalnızca .locres, .uasset veya .json uzantılı yerelleştirme dosyalarını tespit edip geçici bellek alanına çıkarır.  
İkinci aşamada, LocresBinaryParser modülü ayıklanan ikili dosyayı C\# nesne modeline (LocresFile \-\> Namespace \-\> Entry \-\> SourceString / TargetString) dönüştürür. Değişkenler, kontrol karakterleri ve özel isim adayları Regex analizöründen geçirilerek metin verileri üzerinde frekans analizi yapılır. Tekrar eden kelimeler ve özel isim adayları kullanıcı onayına sunulmak üzere sözlük paneline aktarılır.  
Çeviri orkestrasyonu aşamasında, metin dizileri 50 ile 100 satırlık optimize edilmiş paketlere (batch) bölünür. Kullanıcının girdiği özel direktifler, glosary kuralları ve değişken maskeleri hazırlanarak Yapay Zeka API'sine asenkron kanallar vasıtasıyla eşzamanlı istekler gönderilir. Hız sınırına (Rate-limit \- 429\) takılan istekler "Exponential Backoff" stratejisi ile otomatik olarak yeniden denenir.  
Gelen Türkçe çevirilerde kaynak metindeki değişken sayılarının ({0}, {1}) ve biçimlendirme etiketlerinin korunup korunmadığı post-processing doğrulama aşamasında kontrol edilir. Eksik etiket tespiti durumunda ilgili satır "Hatalı Çeviri" olarak işaretlenir. Son aşamada, onaylanan Türkçe metinler orijinal .locres dosya formatına uygun olarak binary formatta yeniden yazılır ve orijinal mount point yolu muhafaza edilerek yeni bir yamalama .pak dosyasına (örneğin GameName-Turkish\_P.pak) sıkıştırılıp paketlenir.

## **Proje Deposu Dokümantasyon Tasarımı (.md Dosyaları)**

Proje deposunun sürdürülebilirliği ve açık kaynak katkı standartlarına uygunluğu açısından dökümantasyon dosyalarının içeriği ve mimari yapısı aşağıdaki gibi tasarlanmıştır.

### **README.md İçerik Tasarımı**

Projenin giriş dokümanı olan README.md, uygulamanın yeteneklerini ve kurulum adımlarını açıklar. Doküman; projenin Unreal Engine .pak ve .locres dosyalarını doğrudan işleyebilme yeteneğini, değişken maskeleme ve özel isim koruma mekanizmalarını, dinamik prompt entegrasyonunu ve desteklenen AI API motorlarını (Gemini, GPT-4o mini, DeepSeek) özetler. Kurulum bölümünde .NET 8 SDK gereksinimi, git clone adımları, dotnet build ve dotnet run komutları yer alır. Lisans olarak MIT Lisansı belirtilir.

### **ARCHITECTURE.md İçerik Tasarımı**

Yazılım mimarisini detaylandıran ARCHITECTURE.md dosyası, uygulamanın dört ana katmanını açıklar:

> * **LocalizePak.Core Katmanı:** .pak ve .locres ikili veri yapılarını temsil eden domain modellerini ve temel veri arayüzlerini barındırır.  
> * **LocalizePak.IO Katmanı:** Unpaker kütüphanesi entegrasyonu ile .pak arşiv I/O yönetimini yapan PakReader/PakWriter sınıflarını ve LocresLib tabanlı ikili okuma/yazma motorunu içeren LocresService sınıfını kapsar.  
> * **LocalizePak.Translation Katmanı:** ITranslationProvider arayüzü altında LLM API servislerinin (Gemini, OpenAI, DeepSeek) entegrasyonunu, değişken koruma mantığını yürüten MaskingEngine sınıfını ve asenkron istek kuyruğunu yöneten BatchOrchestrator yapısını barındırır.  
> * **LocalizePak.UI Katmanı:** Avalonia UI ve CommunityToolkit.Mvvm altyapısı üzerinde MVVM deseniyle geliştirilmiş arayüz bileşenlerini ve sanallaştırılmış DataGrid yönetimini içerir.

### **WORKFLOW.md İçerik Tasarımı**

Kullanıcı kılavuzu niteliğindeki WORKFLOW.md, adım adım çeviri sürecini anlatır. Arayüzden .pak dosyasının seçilmesi ve varsa Hex AES anahtarının girilmesi, taranan dosyalar arasından hedeflenen .locres dosyasının seçilerek metinlerin ayrıştırılması, özel isimlerin ve üslup yönergelerinin tanımlanması, API anahtarı seçilerek çevirinin başlatılması ve işlem sonunda oyuna eklenebilir \_P.pak yama paketinin üretilmesi adımları sıralanır.

### **CONTRIBUTING.md İçerik Tasarımı**

Açık kaynak katkı prensiplerini belirleyen CONTRIBUTING.md dosyası, C\# resmi kodlama standartlarına uyum zorunluluğunu, yeni eklenen I/O ve ayrıştırma modülleri için xUnit birim testlerinin yazılması gerektiğini ve Pull Request öncesinde dotnet test komutunun çalıştırılması kuralını içerir.

## **Proje Başlatıcı Yapay Zeka Asistanı Master Promptu**

Aşağıdaki master prompt, projenin kod tabanını, katmanlı mimarisini ve temel sınıflarını bir yapay zeka kodlama asistanına (Cursor, Windsurf, Claude vb.) sıfırdan oluşturtmak üzere tasarlanmıştır.  
**Master Prompt Metni:**  
Unreal Engine .pak ve .locres dosyalarını okuyup Türkçe yerelleştirme yapan, özel isimleri ve değişkenleri koruyan, kullanıcıdan dinamik çeviri direktifleri alabilen çapraz platform bir C\# masaüstü uygulaması iskelesi (scaffolding) ve mimarisi oluşturulacaktır.  
Teknoloji yığını olarak C\# / .NET 8 runtime ve Avalonia UI (MVVM deseni, CommunityToolkit.Mvvm kullanımı) seçilmiştir.  
Proje Çözüm (Solution) Yapısı ve Katmanlar:

> 1. LocalizePak.Core: Domain modelleri, arayüzler, veri yapıları ve sabitler.  
> 2. LocalizePak.IO: .pak ve .locres ikili (binary) dosya okuma/yazma servisleri (Unpaker ve LocresLib mantığı entegreli).  
> 3. LocalizePak.Translation: LLM API entegrasyonları (Gemini Flash, OpenAI GPT-4o mini), Maskeleme Motoru, Batch Orkestratörü.  
> 4. LocalizePak.UI: Avalonia UI görünümleri (Views) ve ViewModel'ler.

Temel Fonksiyonel Bileşen Gereksinimleri:  
Binary Parsing Modülü (LocalizePak.IO):

> * PakService.cs: .pak dosya indeksini okuyan, AES-256 anahtarı ile şifre çözebilen, içerisindeki .locres dosyalarını bellek akışına (MemoryStream) çıkartan sınıfı yazın.  
> * LocresService.cs: .locres binary formatını ayrıştıran; Namespace, Key, SourceString ve TargetString yapılarını nesne modeline dönüştüren ve tekrar binary .locres olarak serileştiren sınıfı yazın.

Çeviri ve Maskeleme Motoru (LocalizePak.Translation):

> * MaskingEngine.cs: Metin içerisindeki değişkenleri ({0}, {PlayerName}, %s, \\n, \<tag\>...\</tag\>) ve kullanıcının belirttiği Özel İsimleri (Proper Nouns) tespit edip benzersiz etiketlerle (\[VAR\_0\], \[NAME\_1\]) değiştiren, çeviri sonrası bu etiketleri orijinal hallerine geri döndüren sınıfları yazın.  
> * GeminiTranslationProvider.cs ve OpenAiTranslationProvider.cs: ITranslationProvider arayüzünü uygulayan, HttpClient ile asenkron çalışan, kullanıcının girdiği özel sistem direktiflerini (system\_instruction) ve önbelleğe alınmış sözlüğü prompta ekleyen servisleri yazın.  
> * BatchTranslationOrchestrator.cs: Metinleri 50'şerli paketlere bölen, ayarlanabilir eşzamanlılık (SemaphoreSlim) ile API'ye gönderen, Rate-Limit (429) durumunda Exponential Backoff ile yeniden deneyen orkestratör sınıfını yazın.

Arayüz ve ViewModel (LocalizePak.UI):

> * MainWindow.axaml: Sol tarafta dosya ağacı/seçim paneli, sağ tarafta sanallaştırılmış DataGrid (Kaynak Metin, Türkçe Çeviri, Durum), alt tarafta Çeviri Yönergesi girdisi, Özel İsim Listesi ve İlerleme Çubuğu (ProgressBar) içeren modern koyu tema bir arayüz tasarlayın.  
> * MainViewModel.cs: Tüm I/O, ayrıştırma ve çeviri süreçlerini asenkron komutlar (IAsyncRelayCommand) ile yöneten ViewModel sınıfını yazın.

Bana proje dosyalarının dizin yapısını gösterin ve ardından yukarıda belirtilen katmanlardaki temel C\# kodlarını, arayüz XAML dosyalarını ve bağımlılık (.csproj) dosyalarını eksiksiz, production-ready mantıkta oluşturun.

## **Sonuç ve Stratejik Tavsiyeler**

Kapsamlı teknik analiz ve mimari değerlendirmeler sonucunda, projenin C\# (.NET 8/9) ve Avalonia UI ile geliştirilmesi; hazır ekosistem desteği (CUE4Parse, Unpaker, LocresLib), bellek içi sıfır kopyalamalı I/O performansı ve yüksek liste sanallaştırma yetenekleri sebebiyle en düşük riskli ve en hızlı çözümdür.  
Oyun dosyalarının bütünlüğünü bozmamak adına orijinal oyun paketlerini değiştirmek yerine, sadece çevrilen .locres dosyasını içeren ve öncelikli yüklenen bir yama paketi (Game\_Turkish\_P.pak) üretme yaklaşımı benimsenmelidir.  
Maliyet ve performans dengesinde Google Gemini 2.5/3.5 Flash ve OpenAI GPT-4o mini modelleri öne çıkmaktadır. Çeviri maliyetini minimize etmek için Gemini servisinin Context Caching özelliği aktif kullanılmalı, metin içi oyun değişkenleri API'ye gönderilmeden önce kesinlikle maskelenmelidir. Çeviri sonrasında otomatik doğrulamalar (değişken sayısı eşleşmesi, uzunluk kontrolleri) çalıştırılmalı ve hatalı etiket içeren satırlar arayüzde kullanıcıya manuel düzeltme imkanıyla sunulmalıdır.

#### **Alıntılanan çalışmalar**

1\. GitHub \- vaibhavpandeyvpz/unpaker: A comprehensive C\# library and desktop application for reading and writing Unreal Engine 4 Pak archive files, compatible with GTA:III, GTA:VC, and GTA:SA Definitive Editions., https://github.com/vaibhavpandeyvpz/unpaker 2\. akintos/UnrealLocres: UE4 localization resource file tool \- GitHub, https://github.com/akintos/UnrealLocres 3\. GitHub \- SolicenTEAM/UEExtractor: Extracts text from Unreal Engine: .pak or .ucas archive files to localize the game., https://github.com/SolicenTEAM/UEExtractor 4\. GitHub \- AcTePuKc/LocresStudio: The modern, tabbed GUI for Unreal Engine localization files. Supports TSV import/export, multi-file editing, and stability fixes. AI-assisted and Community driven., https://github.com/AcTePuKc/LocresStudio 5\. trumank/repak: Unreal Engine .pak file library and CLI in rust \- GitHub, https://github.com/trumank/repak 6\. Compare Desktop App Frameworks \- GitHub Star History, https://www.star-history.com/compare/desktop-app-framework 7\. Best Desktop App Frameworks 2026: Electron, Tauri, Neutralino, Flutter, Qt, and .NET MAUI, https://www.pkgpulse.com/guides/best-desktop-app-frameworks-2026 8\. pyuepak is a Python library for working with Unreal Engine .pak files. \- GitHub, https://github.com/stas96111/pyuepak 9\. stas96111/pylocres: Tool for reading and writing Unreal Engine translation files. \- GitHub, https://github.com/stas96111/pylocres 10\. panzi/u4pak: unpack, pack, list, check and mount Unreal Engine 4 .pak archives \- GitHub, https://github.com/panzi/u4pak 11\. retoc \- Unreal Engine IoStore CLI packing/unpacking tool \- GitHub, https://github.com/trumank/retoc/ 12\. Unreal Engine 4 locres structure (010Editor Template) \- GitHub Gist, https://gist.github.com/marcussacana/84948809ac6067a9bec0ff8a2b15308e 13\. How much does AI translation cost? DeepL, Google Translate, OpenAI compared (2026), https://simplelocalize.io/blog/posts/ai-machine-translation-cost-comparison/ 14\. Gemini Developer API pricing, https://ai.google.dev/gemini-api/docs/pricing 15\. AI API Pricing Calculator 2026: Cost Per Million Tokens \- Iternal Technologies, https://iternal.ai/llm-pricing-calculator 16\. GPT-4o mini: advancing cost-efficient intelligence \- OpenAI, https://openai.com/index/gpt-4o-mini-advancing-cost-efficient-intelligence/ 17\. LLM API Pricing 2026: OpenAI, Gemini, Claude & Grok | IntuitionLabs, https://intuitionlabs.ai/articles/llm-api-pricing-comparison-2025 18\. Budget AI Coding Models Compared August 2026: V4 Flash vs Luna vs Gemini 3.5 Flash vs Haiku 4.5 \- Developers Digest, https://www.developersdigest.tech/blog/budget-ai-coding-models-compared-2026