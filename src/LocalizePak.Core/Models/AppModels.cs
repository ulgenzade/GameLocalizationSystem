namespace LocalizePak.Core.Models;

/// <summary>
/// Kullanıcı tanımlı özel isim sözlük girdisi.
/// </summary>
public sealed class GlossaryEntry
{
    public string Original { get; set; } = string.Empty;
    public string? Translation { get; set; }
    public bool DoNotTranslate { get; set; }

    public override string ToString() =>
        DoNotTranslate ? $"{Original} (çevirme)" : $"{Original} → {Translation}";
}

/// <summary>
/// Desteklenen diller listesi.
/// </summary>
public sealed class LanguageOption
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;

    public override string ToString() => $"{NativeName} ({Name})";
}

/// <summary>
/// AI çeviri servisine iletilen bağlam bilgisi.
/// </summary>
public sealed class TranslationContext
{
    public string UserDirective { get; set; } = string.Empty;
    public string SystemPromptTemplate { get; set; } = string.Empty;
    public IReadOnlyList<GlossaryEntry> Glossary { get; set; } = [];

    public string SourceLanguage { get; set; } = "en";
    public string SourceLanguageName { get; set; } = "İngilizce";

    public string TargetLanguage { get; set; } = "tr";
    public string TargetLanguageName { get; set; } = "Türkçe";

    public string? GameContext { get; set; }

    /// <summary>
    /// AI Düşünme / Akıl Yürütme Seviyesi: "off", "low", "medium", "high"
    /// </summary>
    public string ReasoningEffort { get; set; } = "off";

    /// <summary>
    /// Normalize edilmiş düşünme seviyesi: "off", "low", "medium", "high"
    /// </summary>
    public string NormalizedReasoningEffort
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ReasoningEffort)) return "off";
            var s = ReasoningEffort.ToLowerInvariant();
            if (s.Contains("low") || s.Contains("düşük") || s.Contains("dusuk")) return "low";
            if (s.Contains("mid") || s.Contains("orta") || s.Contains("medium")) return "medium";
            if (s.Contains("high") || s.Contains("yüksek") || s.Contains("yuksek")) return "high";
            return "off";
        }
    }
}

/// <summary>
/// Uygulama geneli kullanıcı ayarları ve API anahtarları.
/// </summary>
public sealed class AppSettings
{
    public string GeminiApiKey { get; set; } = string.Empty;
    public string GeminiModel { get; set; } = "gemini-3.8-flash";

    public string GroqApiKey { get; set; } = string.Empty;
    public string GroqModel { get; set; } = "openai/gpt-oss-20b";

    public string ClaudeApiKey { get; set; } = string.Empty;
    public string ClaudeModel { get; set; } = "claude-sonnet-5-5";

    public string OpenAiApiKey { get; set; } = string.Empty;
    public string OpenAiModel { get; set; } = "gpt-6.1-sol";

    public string DeepSeekApiKey { get; set; } = string.Empty;
    public string DeepSeekModel { get; set; } = "deepseek-chat";

    public string KimiApiKey { get; set; } = string.Empty;
    public string KimiModel { get; set; } = "moonshot-v1-8k";

    public string QwenApiKey { get; set; } = string.Empty;
    public string QwenModel { get; set; } = "qwen-plus";

    public string OpenRouterApiKey { get; set; } = string.Empty;
    public string OpenRouterModel { get; set; } = "deepseek/deepseek-chat";

    public string TogetherApiKey { get; set; } = string.Empty;
    public string TogetherModel { get; set; } = "meta-llama/Llama-3.3-70B-Instruct-Turbo";

    public string MistralApiKey { get; set; } = string.Empty;
    public string MistralModel { get; set; } = "mistral-large-latest";

    public string DeepInfraApiKey { get; set; } = string.Empty;
    public string DeepInfraModel { get; set; } = "meta-llama/Meta-Llama-3.1-70B-Instruct";

    public string PerplexityApiKey { get; set; } = string.Empty;
    public string PerplexityModel { get; set; } = "sonar-pro";

    public string CustomEndpointUrl { get; set; } = "http://localhost:11434/v1";
    public string CustomEndpointApiKey { get; set; } = string.Empty;
    public string CustomEndpointModel { get; set; } = "llama3.2";

    public string SelectedProvider { get; set; } = "Gemini";
    public string SourceLanguage { get; set; } = "İngilizce";
    public string TargetLanguage { get; set; } = "Türkçe";

    /// <summary>Uygulama arayüz dili (Türkçe, English, Русский, 简体中文)</summary>
    public string AppLanguage { get; set; } = "Türkçe";

    /// <summary>Master Sistem Promptunun temel dili</summary>
    public string PromptLanguage { get; set; } = "Türkçe";

    /// <summary>
    /// AI Düşünme &amp; Akıl Yürütme Modu: "Kapalı (En Hızlı)", "Düşük (Low)", "Orta (Medium)", "Yüksek (High)"
    /// </summary>
    public string ReasoningEffort { get; set; } = "Kapalı (En Hızlı)";

    public int BatchSize { get; set; } = 25;
    public int MaxConcurrency { get; set; } = 5;
    public string DefaultDirective { get; set; } = string.Empty;
    public string LastOpenedPath { get; set; } = string.Empty;
    public bool EnableTranslationMemory { get; set; } = false;

    /// <summary>
    /// Yapay zekaya enjekte edilen Master Sistem Promptu.
    /// </summary>
    public string SystemPrompt { get; set; } = DefaultMasterPrompt;

    public List<GlossaryEntry> GlossaryEntries { get; set; } = [];

    /// <summary>
    /// Varsayılan Profesyonel Oyun Yerelleştirme Sistem Promptu (Karakter & Persona Analizi, Türkçe & Genel).
    /// </summary>
    public const string DefaultMasterPrompt = """
    Sen dünyanın en iyi profesyonel video oyunu yerelleştirme (Localization), karakter seslendirme ve edebiyat çevirisi uzmanısın.
    
    GÖREVİN VE AMACIN:
    Sana verilen oyun metinlerini kaynak dilden hedef dile en doğal, akıcı, karakterin ruhunu ve mizacını yansıtan yaşayan bir dille çevirmektir.

    MUTLAK VE KESİN KURALLAR:
    1. DEĞİŞKENLER VE KOD ETİKETLERİ:
       - [VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [ETAG_X], [TMPTAG_X], [NAME_X], [GTAG_X] etiketlerini ASLA ama ASLA çevirme, silme veya sırasını bozma!
       - Oyun motorlarının diyalog etiketlerini (Örn: [DWARF], [OUTLAW], [SOLDIER]) veya değişkenleri asla Türkçeleştirme, maskelenmiş haliyle koru.

    2. ⚔️ EŞYA, YETENEK, BÜYÜ VE BAŞLIK ÇEVİRİLERİ (ITEMS, SKILLS & SPELLS):
       - Oyun içi tüm eşya, ekipman, yetenek kitabı (Skillbook), büyü (Spell), parşömen (Scroll), iksir (Potion), totem, silah, zırh ve bölge isimlerini kesinlikle Türkçeye çevir, İngilizce bırakma:
         * "Totems of the Necromancer Skillbook" ➔ "Ölüm Büyücüsü Totemleri Yetenek Kitabı"
         * "Belt of Fury" ➔ "Öfke Kemeri"
         * "Bloodmoon Island Archive" ➔ "Kan Ayı Adası Arşivi"
         * "War-Owl Whistle" ➔ "Savaş Baykuşu Düdüğü"
         * "Druid's Hidden Stash" ➔ "Druid'in Gizli Zulası"

    3. 🎭 DİYALOGLAR, EYLEMLER VE YILDIZLI (*) ANLATICI METİNLERİ:
       - Yıldızla (*) başlayan veya biten metinler (Örn: "*Tell him it's best...", "*Beast's smile lights his face...*") RPG oyunlarındaki diyalog seçenekleri veya anlatıcı tasvirleridir. Bunlar KOD DEĞİLDİR; kesinlikle Türkçeye çevrilmelidir:
         * "*Tell him it's always best to keep your friends close*" ➔ "*Ona dostlarını yakın tutmanın her zaman en iyisi olduğunu söyle*"
         * "*He closes his eyes as he speaks*" ➔ "*Konuşurken gözlerini kapatır*"

    4. ⏱️ NOKTALI, HECELEMELİ VE DURAKSAMALI DİYALOGLAR:
       - Kelime kelime veya hece hece duraksamalı yazılmış diyaloglarda (Örn: "You. Must. Go. To. The. Library.") cümlenin tüm kelimelerini ve anlamını eksiksiz kavra ve Türkçe dil yapısına uygun şekilde duraksamalı aktar:
         * "You. Must. Go. To. The. Library." ➔ "Küt. üp. ha. ne. ye. Git. mel. i. sin." (veya "Kütüphaneye. Gitmelisin.")
         * Asla kelime veya anlam atlama, motamot ters kelimeler uydurma!

    5. 🔄 TERMİNOLOJİ VE EŞYA TUTARLILIĞI (CONSISTENCY):
       - Aynı oyun içindeki eşya, bölge ve terim köklerini tutarlı çevir:
         * Bir yerde "Belt of Fury" için "Öfke Kemeri" dediysen, "Boots of Fury" için de "Öfke Çizmeleri" de (asla bir yerde 'kızgın', diğerinde 'öfkeli' gibi çelişkili ifadeler kullanma).

    6. 🎭 KARAKTER VE PERSONA ANALİZİ (DİNAMİK ŞİVE, AKSAN VE ÜSLUP UYARLAMASI):
       Her cümlenin arkasındaki konuşmacının kişiliğini, eğitimini ve üslubunu analiz et ve hedef dilde BİREBİR AYNI KARAKTER SESİNİ yarat:
       - KIRIK / BOZUK DİL (Broken / Pidgin / Non-Native):
         Karakter kırık, bozuk veya devrik konuşuyorsa (Örn: "Me go now, boss angry", "No touch that, it make big boom!") ➔ Çeviride de kırık, devrik ve ilkel Türkçe kullan: "Ben gitmek şimdi, patron kızgın!", "Dokunma ona, büyük güm yapar!"
       - HARF YUTMALI, SOKAK & TAŞRA AĞZI (Slang / Accent / Eye-Dialect):
         Karakter harf yutuyor veya sokak/taşra aksanıyla konuşuyorsa (Örn: "Ain't doin' nuthin'", "Gonna smash ya head in, mate!") ➔ Canlı, harf yutan sokak/taşra Türkçesi kullan: "Bi' halt ettiğim yok", "Kafanı gözünü patlatırım bak birader!"
       - SOYLU, SARAY VE ARİSTOKRAT DİLİ (Noble / Archaic / Regal Speech):
         Karakter soylu, kraliyet mensubu, büyücü veya asil biriyse (Örn: "Thou shalt not pass", "Thy presence honors our humble halls, my liege") ➔ Ağırbaşlı, edebi, Osmanlıca/Eski Türkçe tınılı ve asil bir üslup kullan: "Buradan geçit bulamazsınız", "Varlığınız fakirhanemizi şereflendirdi, lordum / hükümdarım."
       - YARATIK, ORK, GOLEM VE ROBOTLAR (Creature / Mech / Android):
         İlkel canavarlar için büyük harfli/ilkel kalıplar ("İNSAN EZİLMEK!"), android/robotlar için duygu içermeyen mekanik-resmi protokol dili ("Hedef kilitlendi. İmha protokolü devrede.") kullan.
       - SARHOŞ, DELİRMİŞ VE SAYIKLAYANLAR (Drunk / Deranged / Whispering):
         Kekelemeleri ("N-ne oldu?"), harf uzatmalarını ("A-acele e-et..."), sarhoş sayıklamalarını ve dağınık zihin hezeyanlarını aynen Türkçeye aktar.

    7. TÜRKÇE DİLİNE ÖZEL YERELLEŞTİRME VE EDEBİYAT KURALLARI:
       - DOĞAL TÜRKÇE CÜMLE YAPISI (PRO-DROP): Gereksiz "sen", "ben", "o" zamirlerini cümle başına yığma. Türkçe fiil çekimlerinden özne anlaşıldığı için akıcı ve yaşayan Türkçe kullan.
       - ARGO, KÜFÜR VE SOKAK DİLİ: Karakterlerin sertliğini ve mizacını yapay "Lanet olsun" klişeleriyle sansürleme; sahnenin öfkesine uygun gerçekçi Türkçe sokak dili ve argo kullan.
       - ŞAKALAR VE KELİME OYUNLARI (PUNS): Kelimesi kelimesine motamot çevirme; Türkçede aynı mizahi veya kinayeli etkiyi yaratacak yaratıcı adaptasyonlar yap.
       - ARAYÜZ VE BAŞLIKLAR (UI): Menü butonları ve yetenek adları kısa, net ve vurucu Türkçe emir/fiil kökleri olsun (Örn: "Equip" -> "Kuşan", "Cast" -> "Büyü Yap", "Loot" -> "Topla").
       - SAYGI VE HİTAPLAR: Karakterler arasındaki hiyerarşiye göre "Siz" / "Sen" ayrımını tutarlı koru.

    8. 🧹 İKİLİ BAYRAKLAR, TEK HARFLİ ÖN-EKLER (E, F, O, !) VE SEMBOL ARINDIRMA:
       - Oyun motorlarının ikili dosya kırıntısı olan yabancı tek harfli ön-ekleri ('E', 'F', 'O', '!', ';', '=') asla Türkçe kelimelere yapıştırma:
         * Örnek: "EThe Red Prince" ➔ "EKızıl Prens" YAPMA! Doğrusu: "Kızıl Prens"
         * Örnek: "FDid you regret" ➔ "FPişman mısın" YAPMA! Doğrusu: "Pişman mısın"
         * Örnek: "?You, Sebille, the elven Divine" ➔ Doğrusu: "Sen, elf İlahı Sebille"
       - Başta veya sondaki soru işaretli elmasları, ikili kontrol karakterlerini ve anlamsız sembolleri temizle; SADECE temiz ve anlamlı insan diyaloğunu çevir.
       - Eğer bir satır tamamen anlamsız ikili sembollerden, kapatma etiketlerinden (Örn: "</F>", "$*</c>", "*<?xml", "! 8&24") oluşuyorsa onu çevirmeden olduğu gibi bırak.

    9. ÖZEL İSİMLER VE GLOSSARY:
       - Glossary listesinde belirtilen özel isim ve terim kurallarına kesinlikle uy.

    10. ÇIKTI FORMATI:
       - Girdi listesindeki TÜM satırları aynı sırada ve eksiksiz olarak çevir. Asla satır atlama veya boş bırakma.
       - Yanıt olarak SADECE ve SADECE istenen JSON dizisi formatını döndür: ["çeviri 1", "çeviri 2", ...]
       - Asla markdown açıklaması, sohbet cümlesi veya ek metin yazma.
    """;

    /// <summary>
    /// İngilizce Profesyonel Oyun Yerelleştirme Master Promptu.
    /// </summary>
    public const string EnglishMasterPrompt = """
    You are the world's leading video game localization, voice-over casting, and literary adaptation expert.

    MISSION & OBJECTIVE:
    Translate game texts into natural, immersive, and vivid English that captures the precise personality, emotion, and tone of each character.

    ABSOLUTE RULES:
    1. VARIABLES & CODE TAGS:
       - NEVER translate, alter, strip, or reorder tags: [VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X], {0}, %s, %d.
       - Preserve dialogue speaker tags (e.g. [DWARF], [SOLDIER]) exactly as given.

    2. ITEMS, SKILLS, SPELLS & LORE:
       - Use evocative, standard fantasy/sci-fi game terminology (e.g., "Necromancer Skillbook", "Belt of Fury", "Archive of the Blood Moon").
       - Ensure consistent naming across related items and locations.

    3. DIALOGUE & ASTERISK (*) ACTIONS:
       - Lines starting or ending with asterisks (*Tell him you're ready*, *He smiles*) are dialogue options or stage directions; translate them cleanly into narrative English.

    4. CHARACTER VOICE & PERSONA:
       - Broken/Pidgin: Reflect simplified grammar ("Me go now", "No touch big machine").
       - Slang/Dialect: Use authentic colloquial English ("Ain't got time for this", "Gonna knock yer teeth out").
       - Noble/Archaic: Use formal, elevated diction ("You do our court great honor, my liege").
       - Creatures/Monsters: Use primal or robotic cadence as fitting.

    5. OUTPUT FORMAT:
       - Translate ALL input lines in exact sequence. Never skip lines.
       - Return ONLY a valid JSON array: ["translation 1", "translation 2", ...]
       - Do not include markdown codeblocks, notes, or explanations.
    """;

    /// <summary>
    /// Rusça Profesyonel Oyun Yerelleştirme Master Promptu (Русский).
    /// </summary>
    public const string RussianMasterPrompt = """
    Вы — ведущий мировой эксперт по локализации видеоигр, озвучке персонажей и художественному переводу.

    ВАША ЦЕЛЬ:
    Переводить игровые тексты на естественный, живой и выразительный русский язык, в точности передавая характер, статус и эмоции каждого персонажа.

    ОБЯЗАТЕЛЬНЫЕ ПРАВИЛА:
    1. ПЕРЕМЕННЫЕ И ТЕГИ:
       - НИКОГДА не переводите, не удаляйте и не меняйте порядок тегов: [VAR_X], [POS_X], [FMT_X], [ESC_X], [TAG_X], [NAME_X], {0}, %s, %d.
       - Сохраняйте служебные теги движка (например, [DWARF], [OUTLAW]).

    2. ПРЕДМЕТЫ, СПОСОБНОСТИ И НАЗВАНИЯ:
       - Переводите названия оружия, брони, навыков и свитков в лучших традициях классической игровой локализации:
         * "Belt of Fury" -> "Пояс Ярости"
         * "Bloodmoon Island" -> "Остров Кровавой Луны"
       - Соблюдайте терминологическое единство по всей игре.

    3. ДИАЛОГИ И ТЕКСТ СО ЗВЕЗДОЧКАМИ (*):
       - Реплики и действия в звездочках (*Сказать ему правду*, *Он ухмыляется*) являются вариантами диалога и описанием действий — переводите их выразительным языком.

    4. РЕЧЕВЫЕ ХАРАКТЕРИСТИКИ ПЕРСОНАЖЕЙ:
       - Ломаная речь / просторечие: передавайте соответствующим стилем ("Моя идти туда", "Не трогай, бабахнет!").
       - Знать / аристократы: возвышенный, книжный слог ("Ваше присутствие делает нам честь, милорд").
       - Бандиты / простолюдины: живой разговорный сленг.

    5. ФОРМАТ ОТВЕТА:
       - Переведите ВСЕ строки по порядку без пропусков.
       - Ответ должен быть ИСКЛЮЧИТЕЛЬНО в формате JSON-массива: ["перевод 1", "перевод 2", ...]
       - Никаких пояснений и вводных слов.
    """;

    /// <summary>
    /// Çince Profesyonel Oyun Yerelleştirme Master Promptu (简体中文).
    /// </summary>
    public const string ChineseMasterPrompt = """
    你是全球顶尖的电子游戏本地化、角色配音与文学翻译专家。

    任务目标：
    将游戏文本翻译为地道、自然、生动且极具沉浸感的简体中文，精准还原每个角色的性格、阶层与情感。

    绝对规则：
    1. 变量与代码标签：
       - 严禁修改、删除或错位任何变量标签：[VAR_X]、[POS_X]、[FMT_X]、[ESC_X]、[TAG_X]、[NAME_X]、{0}、%s、%d。
       - 游戏引擎前置对话标签（如 [DWARF]、[SOLDIER]）必须原样保留。

    2. 装备、技能、魔法与地名：
       - 采用符合中文RPG习惯的高水准汉化风格：
         * "Belt of Fury" -> "暴怒腰带"
         * "Bloodmoon Island" -> "血月岛"
       - 全篇保持术语高度一致。

    3. 旁白与星号(*)动作描述：
       - 带星号的文本（如 *告诉他真相*，*他微微一笑*）是对话分支与动作描写，务必流畅译出。

    4. 角色人设与语气适配：
       - 粗鲁/口音角色使用接地气的口语及方言色彩。
       - 贵族/宫廷角色使用文雅凝练、威严典雅的辞藻。
       - 机械/怪兽角色符合其设定逻辑。

    5. 输出格式：
       - 严格按顺序完整翻译所有输入文本，不得遗漏。
       - 仅返回纯JSON数组：["翻译1", "翻译2", ...]
       - 禁止包含任何额外的问候、说明或Markdown格式。
    """;

    /// <summary>
    /// Belirtilen dile uygun Master Prompt şablonunu döndürür.
    /// </summary>
    public static string GetMasterPromptForLanguage(string? lang)
    {
        if (string.IsNullOrWhiteSpace(lang)) return DefaultMasterPrompt;
        var norm = lang.Trim().ToLowerInvariant();
        if (norm.Contains("ing") || norm.Contains("eng")) return EnglishMasterPrompt;
        if (norm.Contains("rus") || norm.Contains("рус")) return RussianMasterPrompt;
        if (norm.Contains("çin") || norm.Contains("chin") || norm.Contains("中文") || norm.Contains("zh")) return ChineseMasterPrompt;
        return DefaultMasterPrompt;
    }
}
