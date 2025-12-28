using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Reflection;
using HarmonyLib;
using RimTalk.Service;
using RimTalk.Data;
using Verse;
using RimWorld;
using UnityEngine;

namespace RimTalk.DisplayOptimization
{
    // --- 1. 设置系统 ---
    public class DisplayOptimizationSettings : ModSettings
    {
        public bool EnableHighlighting = true;
        public bool EnableBolding = true;
        public bool EnableHistoryCircles = true;
        public bool EnableBubbleCircles = true;
        public bool CircleAtEnd = false;
        public bool OnlyShowLastSymbol = false;
        public bool TokenSavingMode = false;
        public int PromptInterval = 3;
        public bool EnableExperimentalSync = true; // 实验性功能默认开启

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref EnableHighlighting, "EnableHighlighting", true);
            Scribe_Values.Look(ref EnableBolding, "EnableBolding", true);
            Scribe_Values.Look(ref EnableHistoryCircles, "EnableHistoryCircles", true);
            Scribe_Values.Look(ref EnableBubbleCircles, "EnableBubbleCircles", true);
            Scribe_Values.Look(ref CircleAtEnd, "CircleAtEnd", false);
            Scribe_Values.Look(ref OnlyShowLastSymbol, "OnlyShowLastSymbol", false);
            Scribe_Values.Look(ref TokenSavingMode, "TokenSavingMode", false);
            Scribe_Values.Look(ref PromptInterval, "PromptInterval", 3);
            Scribe_Values.Look(ref EnableExperimentalSync, "EnableExperimentalSync", true);
        }
    }

    // --- 2. 模组主类 ---
    public class DisplayOptimizationMod : Mod
    {
        public static DisplayOptimizationSettings Settings;
        private static int _dialogueCounter = 0;
        private static bool _isFirstCallOfSession = true;

        public DisplayOptimizationMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DisplayOptimizationSettings>();
        }

        private bool IsChinese => LanguageDatabase.activeLanguage?.folderName.Contains("Chinese") ?? 
                                 LanguageDatabase.activeLanguage?.info.friendlyNameNative.Contains("中文") ?? false;

        public override string SettingsCategory() => IsChinese ? "Rimtalk-显示优化" : "Rimtalk-Display Optimization";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled(IsChinese ? "名字高亮 (琥珀棕)" : "Name Highlighting", ref Settings.EnableHighlighting);
            if (Settings.EnableHighlighting)
            {
                listing.CheckboxLabeled(IsChinese ? "  名字加粗" : "  Bold Names", ref Settings.EnableBolding);
            }
            listing.GapLine();

            listing.Label(IsChinese ? "进度提示 (○/●):" : "Progress Symbols:");
            listing.CheckboxLabeled(IsChinese ? "  对话历史窗口启用" : "  Enable in history window", ref Settings.EnableHistoryCircles, 
                IsChinese ? "关闭后，请把“强制气泡动态同步”也一起关闭" : "Turn off Force Bubble Sync as well after disabling");

            
            listing.CheckboxLabeled(IsChinese ? "  对话气泡启用" : "  Enable in bubbles", ref Settings.EnableBubbleCircles);
            
            listing.Gap(5f);
            if (listing.RadioButton(IsChinese ? "  符号置于句首" : "  Symbol at start", !Settings.CircleAtEnd)) Settings.CircleAtEnd = false;
            if (listing.RadioButton(IsChinese ? "  符号置于句末" : "  Symbol at end", Settings.CircleAtEnd)) Settings.CircleAtEnd = true;

            listing.Gap(5f);
            listing.CheckboxLabeled(IsChinese ? "  仅末尾句才显示标记 (隐藏 ○)" : "  Only show last symbol", ref Settings.OnlyShowLastSymbol);

            listing.GapLine();

            // 实验性功能开关
            Color oldColor = GUI.color;
            GUI.color = Color.yellow;
            listing.CheckboxLabeled(IsChinese ? "强制气泡动态同步" : "Experimental: Force Bubble Sync", ref Settings.EnableExperimentalSync, 
                IsChinese ? "开启后，气泡中会完全同步于对话历史。消除气泡出现标记错误的情况。如对性能造成过多影响请关闭" : "Turn it on, and the bubbles will be fully synchronized with the history window, eliminating incorrect tagging in the bubbles. Turn it off if it causes excessive performance impact.");
            GUI.color = oldColor;

            listing.GapLine();

            listing.CheckboxLabeled(IsChinese ? "Token 节省模式" : "Token Saving Mode", ref Settings.TokenSavingMode);
            if (Settings.TokenSavingMode)
            {
                Settings.PromptInterval = (int)listing.SliderLabeled(IsChinese ? $"  频率: 每 {Settings.PromptInterval} 轮" : $"  Frequency: {Settings.PromptInterval}", Settings.PromptInterval, 1, 10);
            }

            listing.End();
            ModInit.BubblesRebuildMethod?.Invoke(null, null);
        }

        public static bool ShouldSend()
        {
            if (!Settings.TokenSavingMode) return true;
            if (_isFirstCallOfSession) { _isFirstCallOfSession = false; return true; }
            _dialogueCounter++;
            if (_dialogueCounter >= Settings.PromptInterval) { _dialogueCounter = 0; return true; }
            return false;
        }
    }

    // --- 3. 文本处理引擎 ---
    public static class TextProcessor
    {
        private const string HighlightColor = "#CC7A00"; 
        private static readonly Regex NewlineRegex = new Regex(@"[\r\n\t\v\f]+", RegexOptions.Compiled);
        private static readonly Regex MarkdownRegex = new Regex(@"\*\*(.*?)\*\*", RegexOptions.Compiled);
        private static readonly Regex SymbolCleaner = new Regex(@"^[○●]|[○●]$", RegexOptions.Compiled);

        public static string ProcessBase(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = NewlineRegex.Replace(text, " ").Trim();
            result = result.TrimStart('○', '●', ' ', '\t').TrimEnd('○', '●', ' ', '\t');

            if (DisplayOptimizationMod.Settings.EnableHighlighting)
            {
                bool bold = DisplayOptimizationMod.Settings.EnableBolding;
                string o = bold ? $"<color={HighlightColor}><b>" : $"<color={HighlightColor}>";
                string c = bold ? "</b></color>" : "</color>";
                result = MarkdownRegex.Replace(result, $"{o}$1{c}");
                
                if (Current.ProgramState == ProgramState.Playing)
                {
                    foreach (var pawn in PawnsFinder.AllMaps_FreeColonists)
                    {
                        string name = pawn.LabelShort;
                        if (name.Length < 2 || result.Contains(o + name)) continue;
                        string pattern = @"(?<!>)" + Regex.Escape(name);
                        result = Regex.Replace(result, pattern, $"{o}{name}{c}");
                    }
                }
            }
            return result.Replace("*", "");
        }

        public static string StripOldSymbols(string text) => SymbolCleaner.Replace(text, "").Trim();

        public static string ApplySymbol(string text, string symbol)
        {
            if (string.IsNullOrEmpty(symbol)) return text;
            return DisplayOptimizationMod.Settings.CircleAtEnd ? text + symbol : symbol + text;
        }
    }

    // --- 4. 补丁加载 ---
    [StaticConstructorOnStartup]
    public static class ModInit
    {
        public static FieldInfo CachedStringField;
        public static MethodInfo BubblesRebuildMethod;

        static ModInit()
        {
            var harmony = new Harmony("RimTalk.DisplayOptimization.Master.V18");
            
            // 缓存
            CachedStringField = typeof(PlayLogEntry_RimTalkInteraction).GetField("_cachedString", BindingFlags.NonPublic | BindingFlags.Instance);
            BubblesRebuildMethod = AccessTools.TypeByName("Bubbles.Core.Bubbler")?.GetMethod("Rebuild");

            // 1. 自动加载带属性标记的类
            new PatchClassProcessor(harmony, typeof(PromptPatch)).Patch();
            new PatchClassProcessor(harmony, typeof(SetterPatch)).Patch();
            new PatchClassProcessor(harmony, typeof(HistoryMasterPatch)).Patch();
            new PatchClassProcessor(harmony, typeof(DisplaySyncMirrorPatch)).Patch();

            // 2. 手动加载构造函数补丁，彻底解决 "Undefined target method" 报错
            var constructor = AccessTools.Constructor(typeof(PlayLogEntry_RimTalkInteraction), 
                new[] { typeof(InteractionDef), typeof(Pawn), typeof(Pawn), typeof(List<RulePackDef>) });
            
            if (constructor != null)
            {
                harmony.Patch(constructor, postfix: new HarmonyMethod(typeof(PlayLogConstructorManualPatch), nameof(PlayLogConstructorManualPatch.Postfix)));
            }
            else
            {
                Log.Error("[RimTalk-显示优化] 无法定位 PlayLogEntry 构造函数，气泡同步功能可能受限。");
            }
        }
    }

    public static class PromptPatch
    {
        [HarmonyPatch(typeof(PromptService), "DecoratePrompt", new[] { typeof(TalkRequest), typeof(List<Pawn>), typeof(string) })]
        [HarmonyPostfix]
        public static void Postfix(TalkRequest talkRequest)
        {
            if (talkRequest?.Prompt == null || !DisplayOptimizationMod.Settings.EnableHighlighting) return;
            if (DisplayOptimizationMod.ShouldSend())
                talkRequest.Prompt += "\n\n[MANDATORY RULE]: You MUST wrap person names or titles in double asterisks. Example: **Alice** or **Captain**.";
        }
    }

    public static class SetterPatch
    {
        [HarmonyPatch(typeof(TalkResponse), "Text", MethodType.Setter)]
        [HarmonyPrefix]
        public static void Prefix(ref string value) => value = TextProcessor.ProcessBase(value);
    }

    // 历史记录纠错与刷新逻辑
    public static class HistoryMasterPatch
    {
        [HarmonyPatch(typeof(ApiHistory), "AddResponse")]
        [HarmonyPrefix]
        public static void Prefix(Guid id, ref string response, string name)
        {
            if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(name)) return;
            var originalLog = ApiHistory.GetApiLog(id);
            if (originalLog == null) return;

            bool changedAny = false;
            var allLogs = ApiHistory.GetAll().ToList();

            for (int i = allLogs.Count - 1; i >= 0 && (allLogs.Count - i < 20); i--)
            {
                if (allLogs[i].ConversationId == originalLog.ConversationId && allLogs[i].Name == name)
                {
                    if (allLogs[i].Response != null && allLogs[i].Response.Contains("●"))
                    {
                        string cleanText = TextProcessor.StripOldSymbols(allLogs[i].Response);
                        string nextSymbol = DisplayOptimizationMod.Settings.OnlyShowLastSymbol ? "" : "○";
                        allLogs[i].Response = TextProcessor.ApplySymbol(cleanText, nextSymbol);
                        changedAny = true;
                        break;
                    }
                }
            }

            // 发生纠错且开启实验性同步时，刷新气泡
            if (changedAny && DisplayOptimizationMod.Settings.EnableExperimentalSync && DisplayOptimizationMod.Settings.EnableBubbleCircles)
            {
                ModInit.BubblesRebuildMethod?.Invoke(null, null);
            }

            if (DisplayOptimizationMod.Settings.EnableHistoryCircles)
                response = TextProcessor.ApplySymbol(response.Trim('○', '●', ' '), "●");
        }
    }

    // 手动构造函数补丁类
    public static class PlayLogConstructorManualPatch
    {
        public static void Postfix(object __instance, Pawn initiator)
        {
            if (ModInit.CachedStringField == null || __instance == null || initiator == null) return;

            string rawText = (string)ModInit.CachedStringField.GetValue(__instance);
            if (string.IsNullOrEmpty(rawText)) return;

            string processed = TextProcessor.ProcessBase(rawText);

            if (DisplayOptimizationMod.Settings.EnableBubbleCircles)
            {
                // 如果没有开启实验性动态同步，我们需要在这里预判一次符号，否则会全是实心圆
                bool isLast = true;
                if (!DisplayOptimizationMod.Settings.EnableExperimentalSync)
                {
                    var logsSnapshot = ApiHistory.GetAll().ToArray();
                    string matchSeed = processed.Substring(0, Math.Min(5, processed.Length));
                    var currentLog = logsSnapshot.LastOrDefault(l => l.Name == initiator.LabelShort && l.Response != null && l.Response.Contains(matchSeed));
                    if (currentLog != null)
                    {
                        if (logsSnapshot.Skip(Array.IndexOf(logsSnapshot, currentLog) + 1).Any(l => l.ConversationId == currentLog.ConversationId && l.Name == initiator.LabelShort))
                            isLast = false;
                    }
                }
                
                processed = TextProcessor.ApplySymbol(processed, isLast ? "●" : "○");
            }
            ModInit.CachedStringField.SetValue(__instance, processed);
        }
    }

    // 镜像同步补丁 (Worker 拦截)
    public static class DisplaySyncMirrorPatch
    {
        [HarmonyPatch(typeof(PlayLogEntry_RimTalkInteraction), "ToGameStringFromPOV_Worker")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(PlayLogEntry_RimTalkInteraction __instance, ref string __result)
        {
            // 只有开启了实验性同步，才每帧去映射 ApiHistory 的状态
            if (!DisplayOptimizationMod.Settings.EnableExperimentalSync || !DisplayOptimizationMod.Settings.EnableBubbleCircles || string.IsNullOrEmpty(__result)) return;

            Pawn initiator = __instance.Initiator;
            if (initiator == null) return;

            var logsSnapshot = ApiHistory.GetAll().Reverse().Take(20).ToArray();
            string cleanDisplay = Regex.Replace(__result, @"<.*?>", "").Trim('○', '●', ' ');
            string matchSeed = cleanDisplay.Substring(0, Math.Min(5, cleanDisplay.Length));

            var masterLog = logsSnapshot.FirstOrDefault(l => l.Name == initiator.LabelShort && 
                            (l.Response != null && l.Response.Contains(matchSeed)));

            if (masterLog != null && masterLog.Response != null)
            {
                __result = masterLog.Response;
            }
        }
    }
}