using System.Collections.Generic;

namespace LocalizePak.UI.Localization.Languages;

public static class StringsZh
{
    public static readonly IReadOnlyDictionary<string, string> Map = new Dictionary<string, string>
    {
        // Window & App Bar
        ["App_Title"] = "GLS — 游戏本地化系统",
        ["App_Subtitle"] = "游戏本地化与汉化系统",
        ["Engine_Label"] = "引擎:",
        ["Reasoning_Label"] = "思考深度:",
        ["Reasoning_Tooltip"] = "AI 深度推理与思考层级 (关闭 / 低 / 中 / 高)",

        // Menus
        ["Menu_File"] = "文件(_F)",
        ["Menu_Edit"] = "编辑(_E)",
        ["Menu_Translation"] = "翻译(_T)",
        ["Menu_Settings"] = "设置(_S)",
        ["Menu_Help"] = "帮助(_H)",

        // File Menu
        ["Menu_File_OpenPak"] = "🗂️  打开 .pak 封包... (Ctrl+O)",
        ["Menu_File_OpenSingle"] = "📄  打开本地化文件... (.locres, .json, .csv) (Ctrl+Shift+O)",
        ["Menu_File_AddExternal"] = "➕  添加外部文本文件...",
        ["Menu_File_Close"] = "✕  关闭当前文件/封包",
        ["Menu_File_ExportFolder"] = "🚀  导出到时间戳目录 (原文件名)... (Ctrl+E)",
        ["Menu_File_OpenFolder"] = "📂  打开输出目录",
        ["Menu_File_ExportCsv"] = "📥  导出翻译为 CSV 表格 (Excel)...",
        ["Menu_File_ImportCsv"] = "📤  导入已编辑的 CSV...",
        ["Menu_File_SavePak"] = "💾  直接另存为... (Ctrl+S)",
        ["Menu_File_ExportPatch"] = "📦  导出为游戏补丁包 (_P.pak)...",
        ["Menu_File_Exit"] = "🚪  退出",

        // Edit Menu
        ["Menu_Edit_FindReplace"] = "🔍  查找与替换... (Ctrl+H / Ctrl+F)",
        ["Menu_Edit_CopySource"] = "📋  复制原文",
        ["Menu_Edit_CopyTranslation"] = "📋  复制译文",
        ["Menu_Edit_CopySourceToTranslation"] = "🔄  复制原文至译文栏",
        ["Menu_Edit_SelectAll"] = "☑️  全选",
        ["Menu_Edit_DeselectAll"] = "⬜  清除选择",

        // Translation Menu
        ["Menu_Trans_StartStop"] = "🚀  开始 / 停止翻译 (F5)",
        ["Menu_Trans_Resume"] = "⏭️  断点继续翻译",
        ["Menu_Trans_Cancel"] = "⏹️  取消任务 (Esc)",
        ["Menu_Trans_RunQa"] = "🛡️  运行质量检查 (QA) (F7)",
        ["Menu_Trans_AutoFix"] = "🛠️  一键修复缺失标签与代码变量",
        ["Menu_Trans_Pseudo"] = "🧪  伪本地化测试 (Pseudolocalization)...",
        ["Menu_Trans_Pseudo_Tooltip"] = "零 Token 消耗扩展 40% 文本长度，测试 UI 界面溢出问题",
        ["Menu_Trans_Retranslate"] = "✨  重新翻译选中行",
        ["Menu_Trans_ExportErrors"] = "⚠️  导出错误报告 (CSV)...",
        ["Menu_Trans_Glossary"] = "📚  专有名词与术语表 (Glossary)...",

        // Settings Menu
        ["Menu_Settings_Config"] = "⚙️  AI 引擎与偏好设置...",
        ["Menu_Settings_ClearTM"] = "🗑️  清除翻译记忆库 (TM)",

        // Help Menu
        ["Menu_Help_Guide"] = "💡  游戏文件与本地化实战指南...",
        ["Menu_Help_KeyVault"] = "🔑  AES 密钥保管库...",
        ["Menu_Help_RamExtractor"] = "🎮  游戏内存 AES 提取器 (RAM Dumper)...",
        ["Menu_Help_AiAssistant"] = "🤖  AI 引擎与 AES 智能侦探...",
        ["Menu_Help_AesHelp"] = "🔑  什么是 AES 密钥及如何获取？",
        ["Menu_Help_Workflow"] = "📖  操作工作流指南 (WORKFLOW.md)",
        ["Menu_Help_About"] = "ℹ️  关于 — GLS",

        // Navigation Tabs (Left Pane)
        ["Tab_Pak"] = "📦 封包",
        ["Tab_ArchiveFiles"] = "📄 归档文件",
        ["Tab_AesKey"] = "🔑 AES 密钥",
        ["Tab_Filter"] = "🔍 筛选与搜索",
        ["Tab_Progress"] = "📊 进度",

        // Left Pane: Pak Card
        ["Pak_NoPakLoaded"] = "尚未加载 .pak 封包",
        ["Pak_DragDropTip"] = "选择文件或直接拖拽至此处",
        ["Pak_OpenPakBtn"] = "🗂️  打开 .pak",
        ["Pak_OpenFileBtn"] = "📄  打开文件",
        ["Pak_NoTextFound"] = "⚠️ 该压缩包中未检测到可翻译文本 (.locres 等)，仅包含音频/图像资源。",
        ["Pak_AddExternalBtn"] = "➕  添加外部翻译文本 (.locres, .json)",
        ["Pak_AddExternal_Tooltip"] = "选择需要与此游戏包匹配的外部翻译文件。",
        ["Pak_Close_Tooltip"] = "关闭当前封包/文件",
        ["Pak_FilesParsedSuccess"] = "✅ 已成功解析 {0} 个本地化文本文件",
        ["Pak_FilesHeader"] = "封包内容 / 本地化文件列表",
        ["Pak_SavePatchBtn"] = "📦  编译补丁包 (_P.pak)",
        ["Pak_SavePatch_Tooltip"] = "生成游戏引擎最高优先级直接加载的 _P.pak 模组补丁。",
        ["Pak_SaveDirectBtn"] = "💾  直接另存为...",
        ["Pak_ExportFolderBtn"] = "🚀  导出到时间戳目录 (原始文件名)",

        // Left Pane: Filters
        ["Filter_SearchLabel"] = "搜索文本或 ID:",
        ["Filter_SearchPlaceholder"] = "输入关键词、句子或 ID...",
        ["Filter_StatusLabel"] = "状态筛选:",
        ["Filter_All"] = "全部",
        ["Filter_Translated"] = "仅已翻译",
        ["Filter_Untranslated"] = "仅未翻译",
        ["Filter_Errors"] = "仅错误行",
        ["Filter_Modified"] = "已修改",
        ["Filter_SourceLen"] = "原文字符长度:",
        ["Filter_Min"] = "最小",
        ["Filter_Max"] = "最大",
        ["Filter_ClearBtn"] = "重置所有筛选",

        // Left Pane: Telemetry & KPI
        ["Telemetry_Header"] = "实时监控与 KPI 指标",
        ["Telemetry_Speed"] = "实时翻译速度:",
        ["Telemetry_Etc"] = "预计完成时间:",
        ["Telemetry_TotalVol"] = "文本总容量:",
        ["Telemetry_Chars"] = "{0:N0} 字符",
        ["Telemetry_LinesSec"] = "{0:F1} 行/秒",
        ["Telemetry_LinesSec_Calculating"] = "计算中...",

        // Center Tabs
        ["Tab_TranslationEditor"] = "📋 翻译编辑器",
        ["Tab_StatsSummary"] = "📊 统计与概览",
        ["Tab_QaInspection"] = "🛡️ 质量检查 (QA)",
        ["Tab_MemoryGlossary"] = "📚 翻译记忆与术语表",
        ["Tab_FindReplace"] = "🔍 批量查找与替换",
        ["Tab_ConsoleLogs"] = "📜 控制台与实时日志",

        // Translation Editor Toolbar & Grid
        ["Grid_SearchPlaceholder"] = "实时搜索 (原文或译文)...",
        ["Grid_TotalStats"] = "总计: {0} 行 | 已翻译: {1} | 剩余: {2} | 成功率: %{3:F1}",
        ["Grid_FilteredStats"] = "已筛选: {0} 行",
        ["Grid_Col_Index"] = "#",
        ["Grid_Col_Key"] = "键名 / ID",
        ["Grid_Col_Source"] = "游戏原文 (Source)",
        ["Grid_Col_Translation"] = "翻译译文 (Target)",
        ["Grid_Col_Status"] = "状态",
        ["Grid_Btn_Start"] = "🚀  开始翻译 (F5)",
        ["Grid_Btn_Resume"] = "⏭️  继续翻译 (F6)",
        ["Grid_Btn_Stop"] = "⏹️  停止 (Esc)",
        ["Grid_Btn_TranslateSelected"] = "✨  翻译选中行",
        ["Grid_Btn_RetryErrors"] = "⚠️  重试错误行",

        // Find & Replace
        ["Find_Title"] = "批量查找与替换",
        ["Find_SearchTerm"] = "查找内容:",
        ["Find_SearchPlaceholder"] = "输入要查找的文本...",
        ["Find_ReplaceTerm"] = "替换为:",
        ["Find_ReplacePlaceholder"] = "输入替换后的新文本...",
        ["Find_MatchCase"] = "区分大小写",
        ["Find_ExactMatch"] = "全词匹配",
        ["Find_InTranslations"] = "在译文中替换",
        ["Find_InSources"] = "在原文中替换",
        ["Find_Btn_FindNext"] = "查找下一个",
        ["Find_Btn_Replace"] = "替换",
        ["Find_Btn_ReplaceAll"] = "全部替换",
        ["Find_ResultCount"] = "找到 {0} 处匹配",

        // QA Panel
        ["Qa_Title"] = "🛡️ 翻译质量与语法标签完整性检查",
        ["Qa_Description"] = "全面检测代码变量 ([VAR_0], {0}, %s)、未闭合格式标签、UI 长度超标及译名一致性。",
        ["Qa_RunBtn"] = "🛡️  执行质量检查 (F7)",
        ["Qa_AutoFixBtn"] = "🛠️  一键修复所有标签错误",
        ["Qa_AllGood"] = "✅ 完美！未检测到任何代码标签破坏、变量遗漏或 UI 溢出风险。",
        ["Qa_IssuesCount"] = "⚠️ 检测到 {0} 个质量问题:",
        ["Qa_Col_Index"] = "#",
        ["Qa_Col_Issue"] = "问题类型",
        ["Qa_Col_Key"] = "键名",
        ["Qa_Col_Details"] = "详细说明",
        ["Qa_Col_Action"] = "操作",
        ["Qa_Action_Fix"] = "修复",

        // Settings Window
        ["Settings_Title"] = "设置与 AI 模型引擎 — GLS",
        ["Settings_Header"] = "系统与 AI 偏好设置",
        ["Settings_SubHeader"] = "配置大语言模型、API 密钥、界面语言以及本地模型库。",
        ["Settings_Tab_General"] = "⚙️  常规偏好",
        ["Settings_Tab_Providers"] = "🤖  AI 提供商与密钥",
        ["Settings_Tab_Prompt"] = "📝  Master 提示词",
        ["Settings_Tab_Ollama"] = "📥  本地 AI 与 Ollama 库",
        ["Settings_Tab_Memory"] = "🧠  翻译记忆库 (TM)",
        ["Settings_AppLanguage"] = "🌐 界面显示语言",
        ["Settings_AppLanguage_Desc"] = "软件操作界面与菜单的首选语言 (Türkçe, English, Русский, 简体中文)。",
        ["Settings_ActiveEngine"] = "当前使用的 AI 引擎",
        ["Settings_ActiveEngine_Desc"] = "选择用于翻译与语言润色的默认大模型服务。",
        ["Settings_DefaultLanguages"] = "默认翻译语言对 (X ➔ X)",
        ["Settings_DefaultLanguages_Desc"] = "游戏的原始语言与目标汉化语言。",
        ["Settings_ReasoningEffort"] = "🧠 AI 思考 / 推理深度 (Reasoning Effort)",
        ["Settings_ReasoningEffort_Desc"] = "为具备推理链的模型配置深度思考预算 (o/GPT-6, Claude Thinking, DeepSeek R1)。",
        ["Settings_BatchSize"] = "单次批处理行数 (Batch Size)",
        ["Settings_BatchSize_Desc"] = "每次向模型发送的对话行数 (建议设为 10 至 50 行)。",
        ["Settings_Concurrency"] = "最大并发调用数 (Max Concurrency)",
        ["Settings_Concurrency_Desc"] = "允许同时进行的 API 并发请求量 (1 至 10)。",
        ["Settings_PromptLanguage"] = "Master 提示词语言",
        ["Settings_PromptLanguage_Desc"] = "注入大语言模型的专业游戏文学汉化与角色人设规则模板。",
        ["Settings_LoadPromptBtn"] = "加载对应语言的默认模板",
        ["Settings_Btn_Save"] = "保存并关闭",
        ["Settings_Btn_Cancel"] = "取消",

        // Status Bar
        ["Status_Ready"] = "就绪",
        ["Status_Translating"] = "正在翻译... 已完成 {0}/{1} 行 (%{2:F1})",
        ["Status_Completed"] = "翻译任务已圆满完成！",
        ["Status_Cancelled"] = "翻译已被用户取消。",
        ["Status_Saved"] = "文件已成功保存: {0}",
        ["Status_Error"] = "错误: {0}",
        ["Status_FileLoaded"] = "已加载文件: {0} ({1} 行)",
        ["Status_PakLoaded"] = "封包已打开: {0} (共解析 {1} 个文件)"
    };
}
