using System.Collections.Generic;

namespace LocalizePak.UI.Localization.Languages;

public static class StringsRu
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        // Window & App Bar
        ["App_Title"] = "GLS — Game Localization System",
        ["App_Subtitle"] = "Система локализации видеоигр",
        ["Engine_Label"] = "Движок:",
        ["Reasoning_Label"] = "Рассуждения:",
        ["Reasoning_Tooltip"] = "Уровень рассуждений и глубина мышления ИИ (Выкл / Низкий / Средний / Высокий)",

        // Menus
        ["Menu_File"] = "_Файл",
        ["Menu_Edit"] = "_Правка",
        ["Menu_Translation"] = "_Перевод",
        ["Menu_Settings"] = "_Настройки",
        ["Menu_Help"] = "_Справка",

        // File Menu
        ["Menu_File_OpenPak"] = "🗂️  Открыть архив .pak... (Ctrl+O)",
        ["Menu_File_OpenSingle"] = "📄  Открыть файл локализации... (.locres, .json, .csv) (Ctrl+Shift+O)",
        ["Menu_File_AddExternal"] = "➕  Добавить внешний файл локализации...",
        ["Menu_File_Close"] = "✕  Закрыть текущий файл/пакет",
        ["Menu_File_ExportFolder"] = "🚀  Экспорт в датированную папку (с исходным именем)... (Ctrl+E)",
        ["Menu_File_OpenFolder"] = "📂  Открыть папку вывода",
        ["Menu_File_ExportCsv"] = "📥  Экспорт переводов в CSV (Excel)...",
        ["Menu_File_ImportCsv"] = "📤  Импорт отредактированного CSV...",
        ["Menu_File_SavePak"] = "💾  Сохранить как (выбрать путь)... (Ctrl+S)",
        ["Menu_File_ExportPatch"] = "📦  Экспорт в виде мода-патча (_P.pak)...",
        ["Menu_File_Exit"] = "🚪  Выход",

        // Edit Menu
        ["Menu_Edit_FindReplace"] = "🔍  Поиск и замена... (Ctrl+H / Ctrl+F)",
        ["Menu_Edit_CopySource"] = "📋  Копировать исходный текст",
        ["Menu_Edit_CopyTranslation"] = "📋  Копировать текст перевода",
        ["Menu_Edit_CopySourceToTranslation"] = "🔄  Скопировать оригинал в перевод",
        ["Menu_Edit_SelectAll"] = "☑️  Выбрать все",
        ["Menu_Edit_DeselectAll"] = "⬜  Снять выделение",

        // Translation Menu
        ["Menu_Trans_StartStop"] = "🚀  Начать / Остановить перевод (F5)",
        ["Menu_Trans_Resume"] = "⏭️  Продолжить перевод",
        ["Menu_Trans_Cancel"] = "⏹️  Отменить перевод (Esc)",
        ["Menu_Trans_RunQa"] = "🛡️  Запустить контроль качества (QA) (F7)",
        ["Menu_Trans_AutoFix"] = "🛠️  Автоматически исправить теги и переменные",
        ["Menu_Trans_Pseudo"] = "🧪  Тест псевдолокализацией...",
        ["Menu_Trans_Pseudo_Tooltip"] = "Удлиняет текст на 40% без расхода токенов для проверки переполнения UI",
        ["Menu_Trans_Retranslate"] = "✨  Переперевести выбранную строку",
        ["Menu_Trans_ExportErrors"] = "⚠️  Экспорт отчета об ошибках (CSV)...",
        ["Menu_Trans_Glossary"] = "📚  Глоссарий и словарь терминов...",

        // Settings Menu
        ["Menu_Settings_Config"] = "⚙️  ИИ-движки и параметры...",
        ["Menu_Settings_ClearTM"] = "🗑️  Очистить память переводов (кэш)",

        // Help Menu
        ["Menu_Help_Guide"] = "💡  Руководство по игровым файлам и локализации...",
        ["Menu_Help_KeyVault"] = "🔑  Хранилище AES-ключей...",
        ["Menu_Help_RamExtractor"] = "🎮  Извлечение AES из памяти игры (RAM Dumper)...",
        ["Menu_Help_AiAssistant"] = "🤖  ИИ-помощник по архивам и AES...",
        ["Menu_Help_AesHelp"] = "🔑  Что такое ключ AES и где его найти?",
        ["Menu_Help_Workflow"] = "📖  Руководство пользователя (WORKFLOW.md)",
        ["Menu_Help_About"] = "ℹ️  О программе — GLS",

        // Navigation Tabs (Left Pane)
        ["Tab_Pak"] = "📦 Пакет",
        ["Tab_Filter"] = "🔍 Фильтр и поиск",
        ["Tab_Progress"] = "📊 Прогресс",

        // Left Pane: Pak Card
        ["Pak_NoPakLoaded"] = "Файл .pak еще не загружен",
        ["Pak_DragDropTip"] = "Выберите файл или перетащите его сюда",
        ["Pak_OpenPakBtn"] = "🗂️  Открыть .pak",
        ["Pak_OpenFileBtn"] = "📄  Открыть файл",
        ["Pak_NoTextFound"] = "⚠️ В этом архиве нет переводимого текста (.locres и т.д.) — только графика/аудио.",
        ["Pak_AddExternalBtn"] = "➕  Добавить внешний текстовый файл (.locres, .json)",
        ["Pak_AddExternal_Tooltip"] = "Выберите внешний файл локализации для сопоставления с этим архивом.",
        ["Pak_Close_Tooltip"] = "Закрыть текущий пакет/файл",
        ["Pak_FilesParsedSuccess"] = "✅ Файлов локализации готово: {0}",
        ["Pak_FilesHeader"] = "СОДЕРЖИМОЕ / ФАЙЛЫ ЛОКАЛИЗАЦИИ",
        ["Pak_SavePatchBtn"] = "📦  Собрать патч-мод (_P.pak)",
        ["Pak_SavePatch_Tooltip"] = "Компилирует высокоприоритетный файл _P.pak для автоматической загрузки игрой.",
        ["Pak_SaveDirectBtn"] = "💾  Сохранить как (напрямую)...",
        ["Pak_ExportFolderBtn"] = "🚀  Экспорт в папку с датой (исходное имя)",

        // Left Pane: Filters
        ["Filter_SearchLabel"] = "Поиск текста или ID:",
        ["Filter_SearchPlaceholder"] = "Введите слово, фразу или ID...",
        ["Filter_StatusLabel"] = "Фильтр состояния:",
        ["Filter_All"] = "Все",
        ["Filter_Translated"] = "Только переведенные",
        ["Filter_Untranslated"] = "Только непереведенные",
        ["Filter_Errors"] = "Только с ошибками",
        ["Filter_Modified"] = "Измененные",
        ["Filter_SourceLen"] = "Длина исходного текста:",
        ["Filter_Min"] = "Мин",
        ["Filter_Max"] = "Макс",
        ["Filter_ClearBtn"] = "Очистить фильтры",

        // Left Pane: Telemetry & KPI
        ["Telemetry_Header"] = "ТЕЛЕМЕТРИЯ И ЖИВЫЕ KPI",
        ["Telemetry_Speed"] = "Скорость перевода:",
        ["Telemetry_Etc"] = "Расчетное время:",
        ["Telemetry_TotalVol"] = "Общий объем:",
        ["Telemetry_Chars"] = "{0:N0} симв.",
        ["Telemetry_LinesSec"] = "{0:F1} стр/сек",
        ["Telemetry_LinesSec_Calculating"] = "Вычисление...",

        // Center Tabs
        ["Tab_TranslationEditor"] = "📝 Редактор перевода",
        ["Tab_FindReplace"] = "🔍 Пакетный поиск и замена",
        ["Tab_QaInspection"] = "🛡️ Контроль качества (QA)",
        ["Tab_ConsoleLogs"] = "📜 Консоль и журнал",

        // Translation Editor Toolbar & Grid
        ["Grid_SearchPlaceholder"] = "Живой поиск (Оригинал или Перевод)...",
        ["Grid_TotalStats"] = "Всего строк: {0} | Переведено: {1} | Осталось: {2} | Успех: %{3:F1}",
        ["Grid_FilteredStats"] = "Отфильтровано: {0} стр.",
        ["Grid_Col_Index"] = "#",
        ["Grid_Col_Key"] = "Ключ / ID",
        ["Grid_Col_Source"] = "Оригинальный текст (Источник)",
        ["Grid_Col_Translation"] = "Текст перевода (Цель)",
        ["Grid_Col_Status"] = "Статус",
        ["Grid_Btn_Start"] = "🚀  Начать перевод (F5)",
        ["Grid_Btn_Resume"] = "⏭️  Продолжить (F6)",
        ["Grid_Btn_Stop"] = "⏹️  Остановить (Esc)",
        ["Grid_Btn_TranslateSelected"] = "✨  Перевести выбранные",
        ["Grid_Btn_RetryErrors"] = "⚠️  Повторить ошибки",

        // Find & Replace
        ["Find_Title"] = "Пакетный поиск и замена",
        ["Find_SearchTerm"] = "Найти:",
        ["Find_SearchPlaceholder"] = "Введите текст для поиска...",
        ["Find_ReplaceTerm"] = "Заменить на:",
        ["Find_ReplacePlaceholder"] = "Введите текст для замены...",
        ["Find_MatchCase"] = "С учетом регистра",
        ["Find_ExactMatch"] = "Точное совпадение слова",
        ["Find_InTranslations"] = "Заменять в переводах",
        ["Find_InSources"] = "Заменять в исходном тексте",
        ["Find_Btn_FindNext"] = "Найти далее",
        ["Find_Btn_Replace"] = "Заменить",
        ["Find_Btn_ReplaceAll"] = "Заменить всё",
        ["Find_ResultCount"] = "Найдено совпадений: {0}",

        // QA Panel
        ["Qa_Title"] = "🛡️ Контроль качества и целостность синтаксиса",
        ["Qa_Description"] = "Сканирует теги форматирования, пропущенные переменные ([VAR_0], {0}, %s), риск переполнения UI и единство терминов.",
        ["Qa_RunBtn"] = "🛡️  Запустить проверку качества (F7)",
        ["Qa_AutoFixBtn"] = "🛠️  Автоматически исправить ошибки тегов",
        ["Qa_AllGood"] = "✅ Отлично! Проблем с переменными, тегами и переполнением текста не обнаружено.",
        ["Qa_IssuesCount"] = "⚠️ Обнаружено проблем качества: {0}",
        ["Qa_Col_Index"] = "#",
        ["Qa_Col_Issue"] = "Тип проблемы",
        ["Qa_Col_Key"] = "Ключ",
        ["Qa_Col_Details"] = "Подробности",
        ["Qa_Col_Action"] = "Действие",
        ["Qa_Action_Fix"] = "Исправить",

        // Settings Window
        ["Settings_Title"] = "Настройки и ИИ-движки — GLS",
        ["Settings_Header"] = "Параметры приложения и ИИ",
        ["Settings_SubHeader"] = "Настройка ИИ-провайдеров, API-ключей, языков интерфейса и локальных моделей.",
        ["Settings_Tab_General"] = "⚙️  Общие настройки",
        ["Settings_Tab_Providers"] = "🤖  ИИ-провайдеры и ключи",
        ["Settings_Tab_Prompt"] = "📝  Мастер-промпт системы",
        ["Settings_Tab_Ollama"] = "📥  Локальный ИИ и хаб Ollama",
        ["Settings_Tab_Memory"] = "🧠  Память переводов (TM)",
        ["Settings_AppLanguage"] = "🌐 Язык интерфейса",
        ["Settings_AppLanguage_Desc"] = "Основной язык меню и окон программы (Türkçe, English, Русский, 简体中文).",
        ["Settings_ActiveEngine"] = "Активный движок перевода",
        ["Settings_ActiveEngine_Desc"] = "Служба ИИ по умолчанию для задач локализации и адаптации текстов.",
        ["Settings_DefaultLanguages"] = "Языковая пара по умолчанию (X ➔ X)",
        ["Settings_DefaultLanguages_Desc"] = "Исходный язык игры и целевой язык перевода.",
        ["Settings_ReasoningEffort"] = "🧠 Уровень рассуждений ИИ (Reasoning Effort)",
        ["Settings_ReasoningEffort_Desc"] = "Бюджет рассуждений для моделей с цепочкой мыслей (o/GPT-6, Claude, Gemini, DeepSeek).",
        ["Settings_BatchSize"] = "Размер пакета строк (Batch Size)",
        ["Settings_BatchSize_Desc"] = "Количество строк, отправляемых в одном API-запросе (рекомендуется 10 - 50).",
        ["Settings_Concurrency"] = "Макс. параллельных запросов (Concurrency)",
        ["Settings_Concurrency_Desc"] = "Максимальное число одновременных вызовов API (от 1 до 10).",
        ["Settings_PromptLanguage"] = "Язык мастер-промпта системы",
        ["Settings_PromptLanguage_Desc"] = "Шаблон правил художественного перевода и адаптации персонажей для ИИ.",
        ["Settings_LoadPromptBtn"] = "Загрузить шаблон для выбранного языка",
        ["Settings_Btn_Save"] = "Сохранить и закрыть",
        ["Settings_Btn_Cancel"] = "Отмена",

        // Status Bar
        ["Status_Ready"] = "Готово",
        ["Status_Translating"] = "Перевод... выполнено {0}/{1} строк (%{2:F1})",
        ["Status_Completed"] = "Перевод успешно завершен!",
        ["Status_Cancelled"] = "Перевод отменен пользователем.",
        ["Status_Saved"] = "Файл успешно сохранен: {0}",
        ["Status_Error"] = "Ошибка: {0}",
        ["Status_FileLoaded"] = "Файл загружен: {0} ({1} строк)",
        ["Status_PakLoaded"] = "Пакет открыт: {0} (файлов готово: {1})"
    };
}
