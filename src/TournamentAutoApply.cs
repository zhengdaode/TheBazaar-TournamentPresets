using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace BazaarSim.Tournament
{
    /// <summary>
    /// 锦标赛自动应用(Harmony 手工挂接,游戏类型按名解析,不依赖编译期类型):
    /// 1) TournamentHostSettingsView.OnEnable 后缀 → 存在默认预设时自动写入控件;
    /// 2) EncounterBanPopupController.Show 后缀 → 弹窗勾选与视图 ban 列表同步,
    ///    防"开弹窗→关闭"把已应用的 ban 整体覆盖(见 research/testserver/tournament-notes.md 坑#2)。
    /// </summary>
    internal static class TournamentAutoApply
    {
        private static ManualLogSource _log;
        private static Func<string> _presetPath;

        public static void Install(ManualLogSource log, Func<string> presetPath)
        {
            _log = log;
            _presetPath = presetPath;
            try
            {
                var harmony = new Harmony("local.bazaarsim.battle.tournament");
                var patched = 0;

                var viewType = GameType("TheBazaar.UI.Tournaments.TournamentHostSettingsView", "TheBazaarRuntime");
                var onEnable = viewType?.GetMethod("OnEnable",
                    BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (onEnable != null)
                {
                    harmony.Patch(onEnable,
                        postfix: new HarmonyMethod(typeof(TournamentAutoApply), nameof(HostSettingsEnabled)));
                    patched++;
                }

                var popupType = GameType("TheBazaar.UI.EncounterBanPopupController", "TheBazaarRuntime");
                var show = popupType?.GetMethod("Show",
                    BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
                if (show != null)
                {
                    harmony.Patch(show,
                        postfix: new HarmonyMethod(typeof(TournamentAutoApply), nameof(PopupShown)));
                    patched++;
                }

                _log.LogInfo($"[锦标赛] 自动应用挂接 {patched}/2" +
                             (patched < 2 ? "(锚点缺失, 游戏更新可能改了类型; 手动应用仍可用)" : ""));
            }
            catch (Exception ex)
            {
                _log?.LogError("[锦标赛] 自动应用挂接失败: " + ex);
            }
        }

        /// <summary>OnEnable 后缀:此时控件与监听已就绪(状态机 OnEnter 的 SetInteractable 在其后执行,会读到新值)。</summary>
        private static void HostSettingsEnabled(object __instance)
        {
            try
            {
                var def = TournamentPresetStore.GetDefault(TournamentPresetStore.Load(_presetPath?.Invoke()));
                if (def == null) return;
                if (TournamentSettingsBridge.TryApplyOn(__instance, def, out var err))
                    _log.LogInfo($"[锦标赛] 已自动应用默认预设[{def.Name}]" +
                                 (err.Length > 0 ? "(" + err + ")" : ""));
                else
                    _log.LogWarning($"[锦标赛] 自动应用失败: {err}");
            }
            catch (Exception ex)
            {
                _log?.LogWarning("[锦标赛] 自动应用异常: " + ex.Message);
            }
        }

        /// <summary>弹窗 Show 后缀:弹窗 _data 已同步构建完成,此刻补勾选,渲染即含 ban 状态。</summary>
        private static void PopupShown()
        {
            try { TournamentSettingsBridge.SyncOpenPopup(); }
            catch { /* 同步失败不影响游戏 */ }
        }

        private static Type GameType(string fullName, string asmName)
        {
            var t = Type.GetType(fullName + ", " + asmName);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.Equals(asm.GetName().Name, asmName, StringComparison.OrdinalIgnoreCase)) continue;
                return asm.GetType(fullName);
            }
            return null;
        }
    }
}
