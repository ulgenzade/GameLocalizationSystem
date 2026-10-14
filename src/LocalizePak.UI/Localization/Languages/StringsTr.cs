using System.Collections.Generic;

namespace LocalizePak.UI.Localization.Languages;

public static class StringsTr
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        // Window & App Bar
        ["App_Title"] = "GLS — Game Localization System",
        ["App_Subtitle"] = "Game Localization System",
        ["Engine_Label"] = "Motor:",
        ["Reasoning_Label"] = "Düşünme:",
        ["Reasoning_Tooltip"] = "AI Düşünme & Akıl Yürütme Seviyesi (Kapalı / Düşük / Orta / Yüksek)",

        // Menus
        ["Menu_File"] = "_Dosya",
        ["Menu_Edit"] = "_Düzenle",
        ["Menu_Translation"] = "_Çeviri",
        ["Menu_Settings"] = "_Ayarlar",
        ["Menu_Help"] = "_Yardım",

        // File Menu
        ["Menu_File_OpenPak"] = "🗂️  .pak Dosyası Aç... (Ctrl+O)",
        ["Menu_File_OpenSingle"] = "📄  Lokalizasyon Dosyası Aç... (.locres, .json, .csv) (Ctrl+Shift+O)",
        ["Menu_File_AddExternal"] = "➕  Harici Lokalizasyon Dosyası Ekle...",
        ["Menu_File_Close"] = "✕  Mevcut Dosyayı/Paketi Kapat",
        ["Menu_File_ExportFolder"] = "🚀  Tarihli Klasöre Çıkart (Orijinal İsimle)... (Ctrl+E)",
        ["Menu_File_OpenFolder"] = "📂  Son Çıktı Klasörünü Aç",
        ["Menu_File_ExportCsv"] = "📥  Çevirileri CSV Olarak Dışa Aktar (Excel)...",
        ["Menu_File_ImportCsv"] = "📤  Düzenlenmiş CSV'yi İçe Aktar...",
        ["Menu_File_SavePak"] = "💾  Farklı Kaydet (Doğrudan Seç)... (Ctrl+S)",
        ["Menu_File_ExportPatch"] = "📦  Yama Paketi Olarak Dışa Aktar (_P.pak)...",
        ["Menu_File_Exit"] = "🚪  Çıkış",

        // Edit Menu
        ["Menu_Edit_FindReplace"] = "🔍  Bul ve Değiştir... (Ctrl+H / Ctrl+F)",
        ["Menu_Edit_CopySource"] = "📋  Kaynak Metni Kopyala",
        ["Menu_Edit_CopyTranslation"] = "📋  Çeviri Metnini Kopyala",
        ["Menu_Edit_CopySourceToTranslation"] = "🔄  Orijinal Metni Çeviriye Aktar",
        ["Menu_Edit_SelectAll"] = "☑️  Tümünü Seç",
        ["Menu_Edit_DeselectAll"] = "⬜  Seçimi Temizle",

        // Translation Menu
        ["Menu_Trans_StartStop"] = "🚀  Çeviriyi Başlat / Durdur (F5)",
        ["Menu_Trans_Resume"] = "⏭️  Kaldığı Yerden Devam Et",
        ["Menu_Trans_Cancel"] = "⏹️  İptal Et (Esc)",
        ["Menu_Trans_RunQa"] = "🛡️  Kalite Kontrolü (QA) Çalıştır (F7)",
        ["Menu_Trans_AutoFix"] = "🛠️  Eksik Etiket ve Değişkenleri Otomatik Onar",
        ["Menu_Trans_Pseudo"] = "🧪  Sahte Yerelleştirme ile Test Et (Pseudolocalization)...",
        ["Menu_Trans_Pseudo_Tooltip"] = "0 jeton harcayarak metinleri %40 uzatır ve arayüz taşmalarını test eder",
        ["Menu_Trans_Retranslate"] = "✨  Seçili Satırı Yeniden Çevir",
        ["Menu_Trans_ExportErrors"] = "⚠️  Hata Raporu Al (CSV)...",
        ["Menu_Trans_Glossary"] = "📚  Özel İsimler & Terimler Sözlüğü (Glossary)...",

        // Settings Menu
        ["Menu_Settings_Config"] = "⚙️  AI Motorları ve Tercihler...",
        ["Menu_Settings_ClearTM"] = "🗑️  Çeviri Belleğini (Hafızayı) Temizle",

        // Help Menu
        ["Menu_Help_Guide"] = "💡  Oyun Dosyaları & Yerelleştirme Rehberi (Öğretici)...",
        ["Menu_Help_KeyVault"] = "🔑  AES Anahtar Havuzu (Key Vault)...",
        ["Menu_Help_RamExtractor"] = "🎮  Canlı Oyundan AES Çıkarıcı (RAM Dumper)...",
        ["Menu_Help_AiAssistant"] = "🤖  AI Oyun Motoru & AES Dedektifi...",
        ["Menu_Help_AesHelp"] = "🔑  AES Anahtarı Nedir ve Nereden Bulunur?",
        ["Menu_Help_Workflow"] = "📖  Kullanım Kılavuzu (WORKFLOW.md)",
        ["Menu_Help_About"] = "ℹ️  Hakkında — GLS",

        // Navigation Tabs (Left Pane)
        ["Tab_Pak"] = "📦 Paket",
        ["Tab_Filter"] = "🔍 Filtre & Arama",
        ["Tab_Progress"] = "📊 İlerleme",

        // Left Pane: Pak Card
        ["Pak_NoPakLoaded"] = "Henüz .pak Dosyası Yüklenmedi",
        ["Pak_DragDropTip"] = "Bir dosya seçin veya buraya sürükleyin",
        ["Pak_OpenPakBtn"] = "🗂️  .pak Aç",
        ["Pak_OpenFileBtn"] = "📄  Dosya Aç",
        ["Pak_NoTextFound"] = "⚠️ Bu arşivde çevrilebilir metin (.locres vb.) bulunamadı (Yalnızca görsel/ses varlıkları içeriyor).",
        ["Pak_AddExternalBtn"] = "➕  Dışarıdan Metin Dosyası Ekle (.locres, .json)",
        ["Pak_AddExternal_Tooltip"] = "Bu arşivle eşleştirmek istediğiniz harici bir çeviri dosyasını seçin.",
        ["Pak_Close_Tooltip"] = "Mevcut paketi/dosyayı kapat",
        ["Pak_FilesParsedSuccess"] = "✅ {0} lokalizasyon dosyası başarıyla ayrıştırıldı ve hazır",
        ["Pak_FilesHeader"] = "PAK İÇERİĞİ / LOKALİZASYON DOSYALARI",
        ["Pak_SavePatchBtn"] = "📦  Yama Paketi Üret (_P.pak)",
        ["Pak_SavePatch_Tooltip"] = "Orijinal dosyanın yanına oyuna doğrudan eklenebilir yüksek öncelikli _P.pak yama paketi derler.",
        ["Pak_SaveDirectBtn"] = "💾  Farklı Kaydet (Doğrudan Seç)...",
        ["Pak_ExportFolderBtn"] = "🚀  Tarihli Klasöre Çıkart (Orijinal İsimle)",

        // Left Pane: Filters
        ["Filter_SearchLabel"] = "Metin veya ID Ara:",
        ["Filter_SearchPlaceholder"] = "Kelime, cümle veya ID yazın...",
        ["Filter_StatusLabel"] = "Durum Filtresi:",
        ["Filter_All"] = "Tümü",
        ["Filter_Translated"] = "Yalnızca Çevrilenler",
        ["Filter_Untranslated"] = "Yalnızca Çevrilmeyenler",
        ["Filter_Errors"] = "Yalnızca Hatalılar",
        ["Filter_Modified"] = "Değiştirilenler",
        ["Filter_SourceLen"] = "Kaynak Karakter Uzunluğu:",
        ["Filter_Min"] = "Min",
        ["Filter_Max"] = "Maks",
        ["Filter_ClearBtn"] = "Filtreleri Temizle",

        // Left Pane: Telemetry & KPI
        ["Telemetry_Header"] = "TELEMETRİ & CANLI KPI",
        ["Telemetry_Speed"] = "Canlı Çeviri Hızı:",
        ["Telemetry_Etc"] = "Tahmini Bitiş Süresi:",
        ["Telemetry_TotalVol"] = "Toplam Karakter Hacmi:",
        ["Telemetry_Chars"] = "{0:N0} karakter",
        ["Telemetry_LinesSec"] = "{0:F1} satır/sn",
        ["Telemetry_LinesSec_Calculating"] = "Hesaplanıyor...",

        // Center Tabs
        ["Tab_TranslationEditor"] = "📝 Çeviri Düzenleyici",
        ["Tab_FindReplace"] = "🔍 Toplu Bul ve Değiştir",
        ["Tab_QaInspection"] = "🛡️ Kalite Kontrol (QA)",
        ["Tab_ConsoleLogs"] = "📜 Konsol & Canlı Günlük",

        // Translation Editor Toolbar & Grid
        ["Grid_SearchPlaceholder"] = "Canlı ara (Kaynak veya Çeviri)...",
        ["Grid_TotalStats"] = "Toplam: {0} satır | Çevrilen: {1} | Kalan: {2} | Başarı: %{3:F1}",
        ["Grid_FilteredStats"] = "Filtrelenen: {0} satır",
        ["Grid_Col_Index"] = "#",
        ["Grid_Col_Key"] = "Anahtar / ID",
        ["Grid_Col_Source"] = "Orijinal Metin (Kaynak)",
        ["Grid_Col_Translation"] = "Çeviri Metni (Hedef)",
        ["Grid_Col_Status"] = "Durum",
        ["Grid_Btn_Start"] = "🚀  Çeviriyi Başlat (F5)",
        ["Grid_Btn_Resume"] = "⏭️  Devam Et (F6)",
        ["Grid_Btn_Stop"] = "⏹️  Durdur (Esc)",
        ["Grid_Btn_TranslateSelected"] = "✨  Seçilileri Çevir",
        ["Grid_Btn_RetryErrors"] = "⚠️  Hataları Yeniden Dene",

        // Find & Replace
        ["Find_Title"] = "Toplu Bul ve Değiştir",
        ["Find_SearchTerm"] = "Aranan İfade:",
        ["Find_SearchPlaceholder"] = "Bulunacak metni girin...",
        ["Find_ReplaceTerm"] = "Yeni Değer (Değiştirilecek):",
        ["Find_ReplacePlaceholder"] = "Yerine yazılacak yeni metin...",
        ["Find_MatchCase"] = "Büyük/Küçük Harfe Duyarlı",
        ["Find_ExactMatch"] = "Tam Kelime Eşleşmesi",
        ["Find_InTranslations"] = "Çeviri Metinlerinde Değiştir",
        ["Find_InSources"] = "Kaynak Metinlerde Değiştir",
        ["Find_Btn_FindNext"] = "Sonrakini Bul",
        ["Find_Btn_Replace"] = "Değiştir",
        ["Find_Btn_ReplaceAll"] = "Tümünü Değiştir",
        ["Find_ResultCount"] = "{0} eşleşme bulundu",

        // QA Panel
        ["Qa_Title"] = "🛡️ Kalite Kontrolü & Sözdizimi Bütünlüğü",
        ["Qa_Description"] = "Format etiketlerini, eksik değişkenleri ([VAR_0], {0}, %s), arayüz taşma risklerini ve çeviri tutarlılığını analiz eder.",
        ["Qa_RunBtn"] = "🛡️  Kalite Kontrolü Çalıştır (F7)",
        ["Qa_AutoFixBtn"] = "🛠️  Tüm Etiket Hatalarını Otomatik Onar",
        ["Qa_AllGood"] = "✅ Harika! Hiçbir değişken, etiket veya arayüz taşma sorunu bulunamadı.",
        ["Qa_IssuesCount"] = "⚠️ {0} adet kalite sorunu tespit edildi:",
        ["Qa_Col_Index"] = "#",
        ["Qa_Col_Issue"] = "Sorun Türü",
        ["Qa_Col_Key"] = "Anahtar",
        ["Qa_Col_Details"] = "Ayrıntılar",
        ["Qa_Col_Action"] = "İşlem",
        ["Qa_Action_Fix"] = "Onar",

        // Settings Window
        ["Settings_Title"] = "Ayarlar & AI Motorları — GLS",
        ["Settings_Header"] = "Uygulama & AI Ayarları",
        ["Settings_SubHeader"] = "AI motorlarını, API anahtarlarını, dilleri ve yerel modelleri yapılandırın.",
        ["Settings_Tab_General"] = "⚙️  Genel Tercihler",
        ["Settings_Tab_Providers"] = "🤖  AI Sağlayıcıları & Anahtarlar",
        ["Settings_Tab_Prompt"] = "📝  Master Sistem Promptu",
        ["Settings_Tab_Ollama"] = "📥  Yerel AI & Ollama Hub",
        ["Settings_Tab_Memory"] = "🧠  Çeviri Belleği (TM)",
        ["Settings_AppLanguage"] = "🌐 Uygulama Arayüz Dili",
        ["Settings_AppLanguage_Desc"] = "Programın genel menü ve arayüz dil tercihi (Türkçe, English, Русский, 简体中文).",
        ["Settings_ActiveEngine"] = "Aktif Çeviri Motoru",
        ["Settings_ActiveEngine_Desc"] = "Çeviri ve analiz işlemlerinde varsayılan olarak kullanılacak yapay zeka servisini seçin.",
        ["Settings_DefaultLanguages"] = "Varsayılan Çeviri Dilleri (X ➔ X)",
        ["Settings_DefaultLanguages_Desc"] = "Kaynak dil ve hedef yerelleştirme dili.",
        ["Settings_ReasoningEffort"] = "🧠 AI Düşünme / Akıl Yürütme Modu (Reasoning Effort)",
        ["Settings_ReasoningEffort_Desc"] = "OpenAI (o/GPT-6), Claude Thinking, Gemini ve DeepSeek modellerinin deyimler ve edebiyat için düşünme bütçesi.",
        ["Settings_BatchSize"] = "Toplu Çeviri Paket Boyutu (Batch Size)",
        ["Settings_BatchSize_Desc"] = "Her bir API çağrısında modele aynı anda gönderilecek satır sayısı (10 - 50 arası önerilir).",
        ["Settings_Concurrency"] = "Maksimum Eşzamanlı İstek (Max Concurrency)",
        ["Settings_Concurrency_Desc"] = "Aynı anda paralel gönderilecek maksimum API çağrısı sayısı (1 - 10 arası).",
        ["Settings_PromptLanguage"] = "Master Sistem Prompt Dili",
        ["Settings_PromptLanguage_Desc"] = "AI modellerine enjekte edilen temel edebi oyun yerelleştirme kuralları şablonu.",
        ["Settings_LoadPromptBtn"] = "Seçili Dile Göre Şablonu Yükle",
        ["Settings_Btn_Save"] = "Kaydet ve Kapat",
        ["Settings_Btn_Cancel"] = "İptal",

        // Status Bar
        ["Status_Ready"] = "Hazır",
        ["Status_Translating"] = "Çeviri yapılıyor... {0}/{1} satır tamamlandı (%{2:F1})",
        ["Status_Completed"] = "Çeviri başarıyla tamamlandı!",
        ["Status_Cancelled"] = "Çeviri kullanıcı tarafından iptal edildi.",
        ["Status_Saved"] = "Dosya başarıyla kaydedildi: {0}",
        ["Status_Error"] = "Hata: {0}",
        ["Status_FileLoaded"] = "Dosya yüklendi: {0} ({1} satır)",
        ["Status_PakLoaded"] = "Paket açıldı: {0} ({1} dosya ayrıştırıldı)"
    };
}
