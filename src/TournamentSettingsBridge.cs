using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BazaarSim.Tournament
{
    /// <summary>
    /// 锦标赛房主设置与插件之间的反射桥(锚点见 research/testserver/tournament-notes.md)。
    /// 全部游戏类型按名解析:游戏更新改类名时这里返回"锚点未找到",面板可诊断,插件不崩。
    /// 读 = 游戏的 public getter(IsPublic 是属性);写 = UI 控件赋值(toggle.isOn / input.text / 私有 _bannedEncounters 字段)。
    /// </summary>
    internal static class TournamentSettingsBridge
    {
        private const string ViewTypeName = "TheBazaar.UI.Tournaments.TournamentHostSettingsView";
        private const string DataTypeName = "TheBazaar.Data";
        private const string RuntimeAsm = "TheBazaarRuntime";
        private const string SharedAsm = "BazaarGameShared";

        private static Type _viewType, _dataType, _cardBaseType;
        private static bool _probed;

        private static void Probe()
        {
            if (_probed) return;
            _probed = true;
            _viewType = FindGameType(ViewTypeName, RuntimeAsm);
            _dataType = FindGameType(DataTypeName, RuntimeAsm);
            _cardBaseType = FindGameType("BazaarGameShared.Domain.Cards.TCardBase", SharedAsm);
        }

        /// <summary>锚点诊断:逐项报告解析结果,拼给面板状态行。</summary>
        public static string ProbeReport()
        {
            Probe();
            return $"锚点 view={Ok(_viewType)} data={Ok(_dataType)} card={Ok(_cardBaseType)}";

            static string Ok(Type t) => t != null ? "✓" : "✗";
        }

        /// <summary>设置界面是否有实例(先找激活的,再找未激活的)。</summary>
        public static bool IsSettingsScreenPresent
        {
            get { Probe(); return FindView(false) != null || FindView(true) != null; }
        }

        /// <summary>读取界面当前设置为一份预设(ban 标题解析失败时用 InternalName,身份始终是 Guid)。</summary>
        public static bool TryCapture(string name, out TournamentPreset preset, out string error)
        {
            preset = null;
            Probe();
            var view = FindView(false) ?? FindView(true);
            if (view == null)
            {
                error = _viewType == null
                    ? "锚点未找到: 游戏更新可能改了 " + ViewTypeName
                    : "未找到设置界面: 游戏内先打开一次 锦标赛→创建(房主设置) 界面";
                return false;
            }

            var bans = ReadViewValue(view, "GetBannedEncounters") as IEnumerable;
            var banIds = new List<Guid>();
            if (bans != null)
                foreach (var o in bans)
                    if (o is Guid g) banIds.Add(g);

            preset = new TournamentPreset
            {
                Name = name,
                CreatedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
                PlayerCapEnabled = AsBool(ReadViewValue(view, "DisplayPlayerCap")),
                PlayerCap = AsInt(ReadViewValue(view, "GetRequestedPlayerCap"), 1000),
                StartOnFull = ReadToggle(view, "_startOnFullToggle"),
                IsPublic = AsBool(ReadViewMember(view, "IsPublic")),
                DisplayCode = AsBool(ReadViewValue(view, "ShouldDisplayCode")),
                TotalRoundCountdownSeconds = AsInt(ReadViewValue(view, "GetTotalCountdownTime"), 300),
                AcceleratedRoundCountdownSeconds = AsInt(ReadViewValue(view, "GetPvpCountdownTime"), 30),
                BannedEncounters = banIds.Select(id => new BannedEncounterRef
                {
                    Id = id.ToString(),
                    Title = DescribeCard(id) ?? "?"
                }).ToList()
            };
            error = "";
            return true;
        }

        /// <summary>把预设写回当前设置界面。返回 false 时 error 给出原因。</summary>
        public static bool TryApply(TournamentPreset preset, out string error)
        {
            var view = FindView(false) ?? FindView(true);
            return TryApplyOn(view, preset, out error);
        }

        /// <summary>把预设写回指定视图实例(Harmony 后缀直接给 __instance,避免场景重查)。</summary>
        public static bool TryApplyOn(object view, TournamentPreset preset, out string error)
        {
            Probe();
            if (view == null)
            {
                error = "未找到设置界面: 先在游戏里打开 锦标赛→创建(房主设置) 界面";
                return false;
            }
            try
            {
                WriteToggle(view, "_limitSizeToggle", preset.PlayerCapEnabled);
                WriteToggle(view, "_startOnFullToggle", preset.StartOnFull);
                WriteToggle(view, "_listPubliclyToggle", preset.IsPublic);
                WriteToggle(view, "_displayCodeToggle", preset.DisplayCode);
                WriteInput(view, "_limitSizeInput", preset.PlayerCap.ToString());
                if (preset.TotalRoundCountdownSeconds is int t) WriteInput(view, "_totalCountdownInput", t.ToString());
                if (preset.AcceleratedRoundCountdownSeconds is int a) WriteInput(view, "_pvpCountdownInput", a.ToString());

                var wanted = new HashSet<Guid>(
                    preset.BannedEncounters.Where(b => Guid.TryParse(b.Id, out _)).Select(b => new Guid(b.Id)));
                var resolved = new List<object>();
                var unresolved = 0;
                foreach (var id in wanted)
                {
                    var card = FindCard(id);
                    if (card != null) resolved.Add(card);
                    else unresolved++;
                }

                // ban 列表真身:私有 _bannedEncounters: TCardBase[](变体组展开后的逐个卡牌)
                var arr = Array.CreateInstance(_cardBaseType ?? typeof(object), resolved.Count);
                for (int i = 0; i < resolved.Count; i++) arr.SetValue(resolved[i], i);
                Field(view, "_bannedEncounters")?.SetValue(view, arr);

                // 同步 ban 弹窗勾选,防"开弹窗再关闭"把预设整体覆盖(见笔记 坑#2)
                SyncPopupChecks(view, wanted);

                // 刷新界面上的 ban 文本(OnLocaleChanged → DrawBannedEncounters)
                view.GetType().GetMethod("OnLocaleChanged", Type.EmptyTypes)?.Invoke(view, null);

                LastAppliedBans = wanted;
                error = unresolved > 0 ? $"{unresolved} 个 ban 项在新版本中未找到(已跳过)" : "";
                return true;
            }
            catch (Exception ex)
            {
                error = "应用失败: " + ex.Message;
                return false;
            }
        }

        /// <summary>最近一次成功应用的 ban 集合,弹窗 Show 后缀用它补勾选。</summary>
        public static HashSet<Guid> LastAppliedBans { get; private set; } = new HashSet<Guid>();

        /// <summary>弹窗 Show 后调用:保证弹窗数据里 Banned 标志与当前视图 ban 列表一致。</summary>
        public static void SyncOpenPopup()
        {
            Probe();
            try
            {
                var view = FindView(false) ?? FindView(true);
                if (view == null) return;
                var bans = ReadViewValue(view, "GetBannedEncounters") as IEnumerable;
                var set = new HashSet<Guid>();
                if (bans != null)
                    foreach (var o in bans)
                        if (o is Guid g) set.Add(g);
                SyncPopupChecks(view, set);
            }
            catch { /* 弹窗同步失败不影响游戏 */ }
        }

        // ---- 内部:反射锚点访问 ----

        private static Object FindView(bool includeInactive)
        {
            Probe();
            return FindAnyInstance(_viewType, includeInactive);
        }

        /// <summary>场景内找实例:优先 Unity 6 的 FindFirstObjectByType(含未激活变体),回退 FindObjectOfType。</summary>
        private static Object FindAnyInstance(Type type, bool includeInactive)
        {
            if (type == null) return null;
            try
            {
                var m = typeof(Object).GetMethod("FindFirstObjectByType",
                    new[] { typeof(Type), typeof(FindObjectsInactive) });
                if (m != null)
                    return m.Invoke(null, new object[]
                    {
                        type,
                        includeInactive ? FindObjectsInactive.Include : FindObjectsInactive.Exclude
                    }) as Object;
            }
            catch { /* API 缺失则走回退 */ }
            try
            {
                var m2 = typeof(Object).GetMethod("FindObjectOfType", new[] { typeof(Type), typeof(bool) });
                if (m2 != null) return m2.Invoke(null, new object[] { type, includeInactive }) as Object;
            }
            catch { }
#pragma warning disable CS0618
            return Object.FindObjectOfType(type);
#pragma warning restore CS0618
        }

        private static Type FindGameType(string fullName, string asmName)
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

        private static FieldInfo Field(object obj, string name) =>
            obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        /// <summary>调视图的无参方法(失败返回 null)。</summary>
        private static object ReadViewValue(object view, string method)
        {
            try
            {
                return view.GetType().GetMethod(method, Type.EmptyTypes)?.Invoke(view, null);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>读视图成员:先按方法找,再按属性找(IsPublic 是属性不是方法)。</summary>
        private static object ReadViewMember(object view, string name)
        {
            var t = view.GetType();
            try
            {
                var mi = t.GetMethod(name, Type.EmptyTypes);
                if (mi != null) return mi.Invoke(view, null);
            }
            catch { }
            try
            {
                return t.GetProperty(name)?.GetValue(view, null);
            }
            catch
            {
                return null;
            }
        }

        private static bool AsBool(object o) => o is bool b && b;
        private static int AsInt(object o, int fallback) => o is int i ? i : fallback;

        private static bool ReadToggle(object view, string fieldName)
        {
            var toggle = Field(view, fieldName)?.GetValue(view);
            return toggle?.GetType().GetProperty("isOn")?.GetValue(toggle, null) is bool on && on;
        }

        private static void WriteToggle(object view, string fieldName, bool value)
        {
            var toggle = Field(view, fieldName)?.GetValue(view);
            toggle?.GetType().GetProperty("isOn")?.SetValue(toggle, value, null);
        }

        private static void WriteInput(object view, string fieldName, string text)
        {
            var input = Field(view, fieldName)?.GetValue(view);
            input?.GetType().GetProperty("text")?.SetValue(input, text, null);
        }

        /// <summary>Data.GetStatic().GetCardMap():Guid → ITCard;找不到或静态数据未就绪返回 null。</summary>
        private static object FindCard(Guid id)
        {
            if (_dataType == null) return null;
            var map = _dataType.GetMethod("GetStatic", Type.EmptyTypes)?.Invoke(null, null);
            if (map == null) return null;
            if (!(map.GetType().GetMethod("GetCardMap", Type.EmptyTypes)?.Invoke(map, null) is IDictionary dict))
                return null;
            return dict.Contains(id) ? dict[id] : null;
        }

        /// <summary>卡牌展示名:Localization.Title.GetLocalizedText() 尽力而为,退回 InternalName。</summary>
        private static string DescribeCard(Guid id)
        {
            try
            {
                var card = FindCard(id);
                if (card == null) return null;
                var loc = card.GetType().GetProperty("Localization")?.GetValue(card, null);
                var title = loc?.GetType().GetProperty("Title")?.GetValue(loc, null);
                var text = title?.GetType().GetMethod("GetLocalizedText", Type.EmptyTypes)
                    ?.Invoke(title, null) as string;
                if (!string.IsNullOrEmpty(text)) return text;
                return card.GetType().GetProperty("InternalName")?.GetValue(card, null) as string ?? id.ToString();
            }
            catch
            {
                return id.ToString();
            }
        }

        /// <summary>把弹窗内每行的 Banned 标志同步为目标集合;返回是否找到并同步了弹窗数据。</summary>
        private static bool SyncPopupChecks(object view, HashSet<Guid> wanted)
        {
            try
            {
                var popup = Field(view, "_encounterBanPopupView")?.GetValue(view);
                if (popup == null) return false;
                if (!(popup.GetType().GetField("_data", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.GetValue(popup) is IEnumerable rows)) return false;
                foreach (var row in rows)
                {
                    if (row == null || row.GetType().Name != "EncounterBanPoolableData") continue;
                    if (!(row.GetType().GetProperty("Variants")?.GetValue(row, null) is IEnumerable variants))
                        continue;
                    var hit = false;
                    foreach (var v in variants)
                    {
                        if (v != null && v.GetType().GetProperty("Id")?.GetValue(v, null) is Guid g
                            && wanted.Contains(g))
                        {
                            hit = true;
                            break;
                        }
                    }
                    row.GetType().GetProperty("Banned")?.SetValue(row, hit, null);
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
