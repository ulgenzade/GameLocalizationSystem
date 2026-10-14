using System.Collections.Generic;

namespace LocalizePak.UI.Localization.Languages;

public static class StringsEn
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        // Window & App Bar
        ["App_Title"] = "GLS — Game Localization System",
        ["App_Subtitle"] = "Game Localization System",
        ["Engine_Label"] = "Engine:",
        ["Reasoning_Label"] = "Reasoning:",
        ["Reasoning_Tooltip"] = "AI Reasoning & Thinking Depth (Disabled / Low / Medium / High)",

        // Menus
        ["Menu_File"] = "_File",
        ["Menu_Edit"] = "_Edit",
        ["Menu_Translation"] = "_Translation",
        ["Menu_Settings"] = "_Settings",
        ["Menu_Help"] = "_Help",

        // File Menu
        ["Menu_File_OpenPak"] = "🗂️  Open .pak Archive... (Ctrl+O)",
        ["Menu_File_OpenSingle"] = "📄  Open Localization File... (.locres, .json, .csv) (Ctrl+Shift+O)",
        ["Menu_File_AddExternal"] = "➕  Add External Localization File...",
        ["Menu_File_Close"] = "✕  Close Current File/Package",
        ["Menu_File_ExportFolder"] = "🚀  Export to Timestamped Folder (Original Name)... (Ctrl+E)",
        ["Menu_File_OpenFolder"] = "📂  Open Output Folder",
        ["Menu_File_ExportCsv"] = "📥  Export Translations to CSV (Excel)...",
        ["Menu_File_ImportCsv"] = "📤  Import Edited CSV...",
        ["Menu_File_SavePak"] = "💾  Save As (Direct Pick)... (Ctrl+S)",
        ["Menu_File_ExportPatch"] = "📦  Export as Patch Archive (_P.pak)...",
        ["Menu_File_Exit"] = "🚪  Exit",

        // Edit Menu
        ["Menu_Edit_FindReplace"] = "🔍  Find and Replace... (Ctrl+H / Ctrl+F)",
        ["Menu_Edit_CopySource"] = "📋  Copy Source Text",
        ["Menu_Edit_CopyTranslation"] = "📋  Copy Translation Text",
        ["Menu_Edit_CopySourceToTranslation"] = "🔄  Copy Source to Translation",
        ["Menu_Edit_SelectAll"] = "☑️  Select All",
        ["Menu_Edit_DeselectAll"] = "⬜  Clear Selection",

        // Translation Menu
        ["Menu_Trans_StartStop"] = "🚀  Start / Stop Translation (F5)",
        ["Menu_Trans_Resume"] = "⏭️  Resume Translation",
        ["Menu_Trans_Cancel"] = "⏹️  Cancel Translation (Esc)",
        ["Menu_Trans_RunQa"] = "🛡️  Run Quality Assurance (QA) (F7)",
        ["Menu_Trans_AutoFix"] = "🛠️  Auto-Fix Missing Tags and Variables",
        ["Menu_Trans_Pseudo"] = "🧪  Test with Pseudolocalization...",
        ["Menu_Trans_Pseudo_Tooltip"] = "Expands text by 40% with zero token cost to test for UI text clipping",
        ["Menu_Trans_Retranslate"] = "✨  Retranslate Selected Row",
        ["Menu_Trans_ExportErrors"] = "⚠️  Export Error Report (CSV)...",
        ["Menu_Trans_Glossary"] = "📚  Glossary & Specialized Terms...",

        // Settings Menu
        ["Menu_Settings_Config"] = "⚙️  AI Engines & Preferences...",
        ["Menu_Settings_ClearTM"] = "🗑️  Clear Translation Memory (Cache)",

        // Help Menu
        ["Menu_Help_Guide"] = "💡  Game Files & Localization Guide (Tutorial)...",
        ["Menu_Help_KeyVault"] = "🔑  AES Key Vault...",
        ["Menu_Help_RamExtractor"] = "🎮  Live Game AES Extractor (RAM Dumper)...",
        ["Menu_Help_AiAssistant"] = "🤖  AI Game Engine & AES Detective...",
        ["Menu_Help_AesHelp"] = "🔑  What is an AES Key & Where to Find it?",
        ["Menu_Help_Workflow"] = "📖  User Guide (WORKFLOW.md)",
        ["Menu_Help_About"] = "ℹ️  About — GLS",

        // Navigation Tabs (Left Pane)
        ["Tab_Pak"] = "📦 Package",
        ["Tab_Filter"] = "🔍 Filter & Search",
        ["Tab_Progress"] = "📊 Progress",

        // Left Pane: Pak Card
        ["Pak_NoPakLoaded"] = "No .pak File Loaded Yet",
        ["Pak_DragDropTip"] = "Select a file or drag & drop here",
        ["Pak_OpenPakBtn"] = "🗂️  Open .pak",
        ["Pak_OpenFileBtn"] = "📄  Open File",
        ["Pak_NoTextFound"] = "⚠️ No translatable text (.locres etc.) found in this archive (Contains only audio/textures).",
        ["Pak_AddExternalBtn"] = "➕  Add External Text File (.locres, .json)",
        ["Pak_AddExternal_Tooltip"] = "Choose an external localization file you wish to pair with this archive.",
        ["Pak_Close_Tooltip"] = "Close current package/file",
        ["Pak_FilesParsedSuccess"] = "✅ {0} localization file(s) successfully parsed and ready",
        ["Pak_FilesHeader"] = "PAK CONTENTS / LOCALIZATION FILES",
        ["Pak_SavePatchBtn"] = "📦  Build Patch Archive (_P.pak)",
        ["Pak_SavePatch_Tooltip"] = "Compiles a high-priority _P.pak mod file that the game directly loads.",
        ["Pak_SaveDirectBtn"] = "💾  Save As (Direct)...",
        ["Pak_ExportFolderBtn"] = "🚀  Export to Timestamped Folder (Original Name)",

        // Left Pane: Filters
        ["Filter_SearchLabel"] = "Search Text or ID:",
        ["Filter_SearchPlaceholder"] = "Type word, sentence or ID...",
        ["Filter_StatusLabel"] = "Status Filter:",
        ["Filter_All"] = "All",
        ["Filter_Translated"] = "Translated Only",
        ["Filter_Untranslated"] = "Untranslated Only",
        ["Filter_Errors"] = "Errors Only",
        ["Filter_Modified"] = "Modified",
        ["Filter_SourceLen"] = "Source Character Length:",
        ["Filter_Min"] = "Min",
        ["Filter_Max"] = "Max",
        ["Filter_ClearBtn"] = "Clear Filters",

        // Left Pane: Telemetry & KPI
        ["Telemetry_Header"] = "TELEMETRY & LIVE KPI",
        ["Telemetry_Speed"] = "Live Speed:",
        ["Telemetry_Etc"] = "Estimated Completion:",
        ["Telemetry_TotalVol"] = "Total Volume:",
        ["Telemetry_Chars"] = "{0:N0} chars",
        ["Telemetry_LinesSec"] = "{0:F1} lines/s",
        ["Telemetry_LinesSec_Calculating"] = "Calculating...",

        // Center Tabs
        ["Tab_TranslationEditor"] = "📝 Translation Editor",
        ["Tab_FindReplace"] = "🔍 Bulk Find & Replace",
        ["Tab_QaInspection"] = "🛡️ Quality Assurance (QA)",
        ["Tab_ConsoleLogs"] = "📜 Console & Live Logs",

        // Translation Editor Toolbar & Grid
        ["Grid_SearchPlaceholder"] = "Live search (Source or Translation)...",
        ["Grid_TotalStats"] = "Total: {0} lines | Translated: {1} | Remaining: {2} | Success: %{3:F1}",
        ["Grid_FilteredStats"] = "Filtered: {0} lines",
        ["Grid_Col_Index"] = "#",
        ["Grid_Col_Key"] = "Key / ID",
        ["Grid_Col_Source"] = "Original Text (Source)",
        ["Grid_Col_Translation"] = "Translation Text (Target)",
        ["Grid_Col_Status"] = "Status",
        ["Grid_Btn_Start"] = "🚀  Start Translation (F5)",
        ["Grid_Btn_Resume"] = "⏭️  Resume (F6)",
        ["Grid_Btn_Stop"] = "⏹️  Stop (Esc)",
        ["Grid_Btn_TranslateSelected"] = "✨  Translate Selected",
        ["Grid_Btn_RetryErrors"] = "⚠️  Retry Errors",

        // Find & Replace
        ["Find_Title"] = "Bulk Find and Replace",
        ["Find_SearchTerm"] = "Find What:",
        ["Find_SearchPlaceholder"] = "Enter text to find...",
        ["Find_ReplaceTerm"] = "Replace With:",
        ["Find_ReplacePlaceholder"] = "Enter replacement text...",
        ["Find_MatchCase"] = "Match Case",
        ["Find_ExactMatch"] = "Match Whole Word",
        ["Find_InTranslations"] = "Replace in Translations",
        ["Find_InSources"] = "Replace in Sources",
        ["Find_Btn_FindNext"] = "Find Next",
        ["Find_Btn_Replace"] = "Replace",
        ["Find_Btn_ReplaceAll"] = "Replace All",
        ["Find_ResultCount"] = "{0} match(es) found",

        // QA Panel
        ["Qa_Title"] = "🛡️ Quality Assurance & Syntax Integrity",
        ["Qa_Description"] = "Scans format tags, missing variables ([VAR_0], {0}, %s), UI text overflow risks and translation consistency.",
        ["Qa_RunBtn"] = "🛡️  Run Quality Inspection (F7)",
        ["Qa_AutoFixBtn"] = "🛠️  Auto-Repair All Tag Errors",
        ["Qa_AllGood"] = "✅ Great! No tag corruption, variable mismatch or UI overflow detected.",
        ["Qa_IssuesCount"] = "⚠️ {0} quality issue(s) detected:",
        ["Qa_Col_Index"] = "#",
        ["Qa_Col_Issue"] = "Issue Type",
        ["Qa_Col_Key"] = "Key",
        ["Qa_Col_Details"] = "Details",
        ["Qa_Col_Action"] = "Action",
        ["Qa_Action_Fix"] = "Fix",

        // Settings Window
        ["Settings_Title"] = "Settings & AI Engines — GLS",
        ["Settings_Header"] = "Application & AI Settings",
        ["Settings_SubHeader"] = "Configure AI engines, API credentials, language preferences, and local models.",
        ["Settings_Tab_General"] = "⚙️  General Preferences",
        ["Settings_Tab_Providers"] = "🤖  AI Providers & Keys",
        ["Settings_Tab_Prompt"] = "📝  Master System Prompt",
        ["Settings_Tab_Ollama"] = "📥  Local AI & Ollama Hub",
        ["Settings_Tab_Memory"] = "🧠  Translation Memory (TM)",
        ["Settings_AppLanguage"] = "🌐 Interface Language",
        ["Settings_AppLanguage_Desc"] = "Overall application UI language (Türkçe, English, Русский, 简体中文).",
        ["Settings_ActiveEngine"] = "Active Translation Engine",
        ["Settings_ActiveEngine_Desc"] = "Select the default AI provider service for translation and linguistic tasks.",
        ["Settings_DefaultLanguages"] = "Default Language Pair (X ➔ X)",
        ["Settings_DefaultLanguages_Desc"] = "Default source language and target localization language.",
        ["Settings_ReasoningEffort"] = "🧠 AI Reasoning & Thinking Effort",
        ["Settings_ReasoningEffort_Desc"] = "Thinking token budget for models supporting chain-of-thought (o/GPT-6, Claude, Gemini, DeepSeek).",
        ["Settings_BatchSize"] = "Batch Size",
        ["Settings_BatchSize_Desc"] = "Number of dialogue lines sent per single API request (10 - 50 recommended).",
        ["Settings_Concurrency"] = "Max Concurrency",
        ["Settings_Concurrency_Desc"] = "Maximum number of simultaneous parallel API requests (1 - 10).",
        ["Settings_PromptLanguage"] = "Master System Prompt Language",
        ["Settings_PromptLanguage_Desc"] = "Template rules for literary narrative, persona analysis and item naming injected into LLMs.",
        ["Settings_LoadPromptBtn"] = "Load Template for Selected Language",
        ["Settings_Btn_Save"] = "Save and Close",
        ["Settings_Btn_Cancel"] = "Cancel",

        // Status Bar
        ["Status_Ready"] = "Ready",
        ["Status_Translating"] = "Translating... {0}/{1} lines completed (%{2:F1})",
        ["Status_Completed"] = "Translation successfully completed!",
        ["Status_Cancelled"] = "Translation cancelled by user.",
        ["Status_Saved"] = "File saved successfully: {0}",
        ["Status_Error"] = "Error: {0}",
        ["Status_FileLoaded"] = "File loaded: {0} ({1} lines)",
        ["Status_PakLoaded"] = "Package loaded: {0} ({1} files parsed)"
    };
}
