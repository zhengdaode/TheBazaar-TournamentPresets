using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UIElements;

namespace BazaarSim.Tournament
{
    /// <summary>
    /// TournamentPresets —— 独立插件:保存/应用锦标赛房主设置预设(人数/时间/公开/显示码/ban)。
    /// 与 BazaarSim 完全分离:独立热键(默认 F9,Bazaar++ 占用 F8)、独立面板、独立配置文件
    /// BepInEx/config/TournamentPresets/tournament-presets.json。
    /// 交互:点击预设行 = 预览完整配置;预览区内执行 应用/改名/设默认/删除。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class TournamentPresetsPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "local.bazaarsim.tournamentpresets";
        public const string PluginName = "TournamentPresets";
        public const string PluginVersion = "0.1.2";

        private ConfigEntry<KeyboardShortcut> _toggleKey;

        private ManualLogSource Log;
        private bool _visible;
        private PresetsUi _ui;
        private string _presetPath;
        private string _message = "";
        private int _selected = -1;

        private readonly List<PresetRow> _rows = new List<PresetRow>();

        private sealed class PresetRow
        {
            public TournamentPreset Preset;
            public bool IsDefault;
        }

        private void Awake()
        {
            Log = Logger;
            _toggleKey = Config.Bind("UI", "ToggleKey", new KeyboardShortcut(KeyCode.F9), "开关面板热键(Bazaar++ 占 F8)");
            _presetPath = TournamentPresetStore.DefaultPath(Paths.BepInExRootPath);
            TournamentAutoApply.Install(Log, () => _presetPath);
            Log.LogInfo($"{PluginName} {PluginVersion} 已加载。按 {_toggleKey.Value} 开关面板。");
        }

        private void Update()
        {
            bool pressed = false;
            try { pressed = Input.GetKeyDown(_toggleKey.Value.MainKey); } catch { }
            try
            {
                var kb = UnityEngine.InputSystem.Keyboard.current;
                if (kb != null && kb.f9Key.wasPressedThisFrame) pressed = true;
            }
            catch { }
            if (pressed)
            {
                _visible = !_visible;
                if (_visible && _ui == null) BuildUi();
                if (_ui != null) _ui.Visible = _visible;
                if (_visible) RefreshPresets();
            }
        }

        private void BuildUi()
        {
            _ui = new PresetsUi();
            _ui.Build(transform,
                onSave: name => Logged("保存当前为预设", () => SaveCurrent(name)),
                onSelect: i => Logged("预览预设", () => SelectAt(i)),
                onApply: () => Logged("应用预设", ApplySelected),
                onRename: () => Logged("改名预设", RenameSelected),
                onToggleDefault: () => Logged("切换默认", ToggleDefaultSelected),
                onDelete: () => Logged("删除预设", DeleteSelected),
                getStatus: StatusLine,
                getMessage: () => _message);
            RefreshPresets();
        }

        /// <summary>按钮回调包装:每一步都写日志与面板消息,杜绝"点了没反应"无从排查。</summary>
        private void Logged(string name, Action inner)
        {
            Log.LogInfo("[TournamentPresets] 点击 " + name);
            try
            {
                inner();
            }
            catch (Exception ex)
            {
                Log.LogError("[TournamentPresets] " + name + " 异常: " + ex);
                Say(name + " 异常: " + ex.Message);
            }
        }

        /// <summary>写面板消息行 + 日志(所有用户可见反馈的唯一出口)。</summary>
        private void Say(string message)
        {
            _message = message;
            Log.LogInfo("[TournamentPresets] " + message);
        }

        private void SaveCurrent(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) name = "预设-" + DateTime.Now.ToString("MMdd-HHmmss");
            if (!TournamentSettingsBridge.TryCapture(name, out var preset, out var err))
            {
                Say("保存失败: " + err);
                return;
            }
            var existed = TournamentPresetStore.Exists(_presetPath, name);
            TournamentPresetStore.Upsert(_presetPath, preset);
            var bans = string.Join("、", preset.BannedEncounters
                .Select(b => b.Title).Distinct().Take(4));
            Say($"{(existed ? "覆盖" : "保存")}预设[{name}] 人数={(preset.PlayerCapEnabled ? preset.PlayerCap.ToString() : "不限")} 回合={preset.TotalRoundCountdownSeconds}s 加速={preset.AcceleratedRoundCountdownSeconds}s ban={preset.BannedEncounters.Count}{(bans.Length > 0 ? "(" + bans + ")" : "")}");
            _selected = -1;
            RefreshPresets();
        }

        private void SelectAt(int index)
        {
            _selected = index >= 0 && index < _rows.Count ? index : -1;
            UpdatePreview();
        }

        private PresetRow Selected() =>
            _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

        private void ApplySelected()
        {
            var row = Selected();
            if (row == null) { Say("应用失败: 先点击一个预设进行预览"); return; }
            if (!TournamentSettingsBridge.TryApply(row.Preset, out var err))
            {
                Say("应用失败: " + err);
                return;
            }
            Say(err.Length > 0
                ? $"已应用预设[{row.Preset.Name}]: {err}"
                : $"已应用预设[{row.Preset.Name}]: 人数/时间/ban 已写入游戏创建界面");
        }

        private void RenameSelected()
        {
            var row = Selected();
            if (row == null) { Say("改名失败: 先点击一个预设进行预览"); return; }
            var newName = (_ui.NameValue() ?? "").Trim();
            if (newName.Length == 0) { Say("改名失败: 先在上方输入框填新名称"); return; }
            var old = row.Preset.Name;
            try
            {
                TournamentPresetStore.Rename(_presetPath, old, newName);
            }
            catch (ArgumentException ex)
            {
                Say("改名失败: " + ex.Message);
                return;
            }
            Say($"已改名[{old}] → [{newName}]");
            RefreshPresets();
        }

        private void ToggleDefaultSelected()
        {
            var row = Selected();
            if (row == null) { Say("设默认失败: 先点击一个预设进行预览"); return; }
            var file = TournamentPresetStore.Load(_presetPath);
            var isDefault = string.Equals(file.DefaultPreset, row.Preset.Name, StringComparison.Ordinal);
            TournamentPresetStore.SetDefault(_presetPath, isDefault ? null : row.Preset.Name);
            Say(isDefault
                ? $"已取消自动应用[{row.Preset.Name}]"
                : $"[{row.Preset.Name}] 设为默认: 打开锦标赛创建界面时自动应用");
            RefreshPresets();
        }

        private void DeleteSelected()
        {
            var row = Selected();
            if (row == null) { Say("删除失败: 先点击一个预设进行预览"); return; }
            TournamentPresetStore.Delete(_presetPath, row.Preset.Name);
            Say($"已删除预设[{row.Preset.Name}]");
            _selected = -1;
            RefreshPresets();
        }

        private void RefreshPresets()
        {
            if (_ui == null) return;
            _rows.Clear();
            try
            {
                var file = TournamentPresetStore.Load(_presetPath);
                foreach (var p in file.Presets)
                    _rows.Add(new PresetRow
                    {
                        Preset = p,
                        IsDefault = string.Equals(file.DefaultPreset, p.Name, StringComparison.Ordinal)
                    });
            }
            catch (Exception ex)
            {
                Log.LogWarning("[TournamentPresets] 预设列表加载失败: " + ex.Message);
            }
            if (_selected >= _rows.Count) _selected = -1;
            _ui.SetPresets(_rows.Select(r => new PresetsUi.Row
            {
                Label = (r.IsDefault ? "★ " : "") + r.Preset.Name
            }).ToList(),
                onSelect: i => Logged("预览预设", () => SelectAt(i)));
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_ui == null) return;
            var row = Selected();
            _ui.SetPreview(row == null ? null : Describe(row), row != null);
        }

        private string Describe(PresetRow row)
        {
            var p = row.Preset;
            var created = "?";
            if (DateTime.TryParse(p.CreatedAtUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                created = dt.ToLocalTime().ToString("MM-dd HH:mm");
            var bans = string.Join("、", p.BannedEncounters
                .Select(b => b.Title).Distinct());
            if (bans.Length > 80) bans = bans.Substring(0, 80) + "…";
            return $"[{(row.IsDefault ? "★" : " ")}] {p.Name}   (创建 {created})\n" +
                   $"人数上限: {(p.PlayerCapEnabled ? p.PlayerCap + " 人" : "不限")}   人满即开: {(p.StartOnFull ? "开" : "关")}\n" +
                   $"公开列表: {(p.IsPublic ? "开" : "关")}   显示房间码: {(p.DisplayCode ? "开" : "关")}\n" +
                   $"回合倒计时: {p.TotalRoundCountdownSeconds}s   加速倒计时: {p.AcceleratedRoundCountdownSeconds}s\n" +
                   $"ban ({p.BannedEncounters.Count}): {(bans.Length > 0 ? bans : "无")}\n" +
                   $"确认无误点[应用]写入游戏界面;改名: 上方输入新名后点[改名]";
        }

        private string StatusLine()
        {
            var file = TournamentPresetStore.Load(_presetPath);
            return $"默认: {file.DefaultPreset ?? "无"} · 设置界面{(TournamentSettingsBridge.IsSettingsScreenPresent ? "在场景中" : "未打开(先开一次 锦标赛→创建)")} · {TournamentSettingsBridge.ProbeReport()}";
        }
    }

    /// <summary>
    /// 锦标赛预设专属面板(UI Toolkit)。比主面板更透明(0.82)、更靠左(left 8),
    /// 避免遮挡游戏锦标赛界面;点击预设行预览完整配置,预览区内执行操作;消息行醒目反馈。
    /// </summary>
    internal sealed class PresetsUi : IDisposable
    {
        public sealed class Row
        {
            public string Label;
        }

        private GameObject _rootObject;
        private UIDocument _document;
        private PanelSettings _settings;
        private VisualElement _root;
        private VisualElement _list;
        private VisualElement _preview;
        private Label _previewText;
        private Label _status;
        private Label _message;
        private TextField _nameField;
        private IVisualElementScheduledItem _schedule;

        public bool Visible
        {
            get => _root != null && _root.style.display == DisplayStyle.Flex;
            set { if (_root != null) _root.style.display = value ? DisplayStyle.Flex : DisplayStyle.None; }
        }

        /// <summary>顶部名称输入框的当前值(保存/改名共用)。</summary>
        public string NameValue() => _nameField?.value;

        public void Build(Transform parent,
            Action<string> onSave, Action<int> onSelect,
            Action onApply, Action onRename, Action onToggleDefault, Action onDelete,
            Func<string> getStatus, Func<string> getMessage)
        {
            _rootObject = new GameObject("TournamentPresetsUiRoot");
            _rootObject.transform.SetParent(parent, false);
            _document = _rootObject.AddComponent<UIDocument>();
            _settings = ScriptableObject.CreateInstance<PanelSettings>();
            _document.panelSettings = _settings;
            _root = _document.rootVisualElement;
            _root.pickingMode = PickingMode.Position;
            ApplyGameFont(_root);

            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.left = 8; panel.style.top = 48;
            panel.style.width = 480; panel.style.height = 600;
            panel.style.backgroundColor = new Color(0.07f, 0.08f, 0.10f, 0.82f);
            panel.style.borderTopColor = panel.style.borderBottomColor =
            panel.style.borderLeftColor = panel.style.borderRightColor = new Color(0.79f, 0.66f, 0.38f, 0.85f);
            panel.style.borderTopWidth = panel.style.borderBottomWidth =
            panel.style.borderLeftWidth = panel.style.borderRightWidth = 1;
            panel.style.borderTopLeftRadius = panel.style.borderTopRightRadius =
            panel.style.borderBottomLeftRadius = panel.style.borderBottomRightRadius = 8;
            panel.style.paddingTop = 10; panel.style.paddingBottom = 10;
            panel.style.paddingLeft = 12; panel.style.paddingRight = 12;
            panel.style.overflow = Overflow.Hidden;
            _root.Add(panel);

            var titleRow = new VisualElement();
            titleRow.style.flexDirection = FlexDirection.Row;
            var title = new Label("锦标赛预设");
            title.style.fontSize = 16;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.79f, 0.66f, 0.38f);
            titleRow.Add(title);
            var hint = new Label("按 F9 打开/关闭本面板");
            hint.style.fontSize = 12;
            hint.style.color = new Color(0.6f, 0.63f, 0.7f);
            hint.style.unityTextAlign = TextAnchor.MiddleRight;
            hint.style.flexGrow = 1;
            titleRow.Add(hint);
            panel.Add(titleRow);

            _status = new Label("");
            _status.style.fontSize = 12;
            _status.style.color = new Color(0.55f, 0.58f, 0.65f);
            _status.style.whiteSpace = WhiteSpace.Normal;
            _status.style.marginTop = 4;
            _status.style.marginBottom = 6;
            panel.Add(_status);

            var saveRow = new VisualElement();
            saveRow.style.flexDirection = FlexDirection.Row;
            var nameLabel = new Label("预设名:");
            nameLabel.style.fontSize = 13;
            nameLabel.style.color = new Color(0.95f, 0.9f, 0.5f);
            nameLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            saveRow.Add(nameLabel);
            _nameField = new TextField();
            _nameField.tooltip = "预设名称(保存留空自动命名;同名覆盖;改名时填新名)";
            _nameField.style.flexGrow = 1;
            _nameField.style.fontSize = 13;
            // 白底黑字 + 金边,让输入框醒目
            _nameField.style.backgroundColor = Color.white;
            _nameField.style.color = new Color(0.05f, 0.05f, 0.05f);
            _nameField.style.borderTopWidth = _nameField.style.borderBottomWidth =
            _nameField.style.borderLeftWidth = _nameField.style.borderRightWidth = 1;
            _nameField.style.borderTopColor = _nameField.style.borderBottomColor =
            _nameField.style.borderLeftColor = _nameField.style.borderRightColor = new Color(0.79f, 0.66f, 0.38f);
            _nameField.style.borderTopLeftRadius = _nameField.style.borderTopRightRadius =
            _nameField.style.borderBottomLeftRadius = _nameField.style.borderBottomRightRadius = 4;
            _nameField.style.marginLeft = 4;
            saveRow.Add(_nameField);
            var saveBtn = new Button(() => onSave?.Invoke(_nameField.value))
            {
                text = "保存当前为预设",
                tooltip = "读取游戏 锦标赛→创建 界面当前的人数/时间/ban 等全部设置"
            };
            Style(saveBtn, 13);
            saveBtn.style.marginLeft = 6;
            saveRow.Add(saveBtn);
            panel.Add(saveRow);

            var tip = new Label("点击预设行 = 预览配置;应用前请先在游戏内打开 锦标赛→创建 界面");
            tip.style.fontSize = 12;
            tip.style.color = new Color(0.55f, 0.58f, 0.65f);
            tip.style.whiteSpace = WhiteSpace.Normal;
            tip.style.marginTop = 4;
            tip.style.marginBottom = 4;
            panel.Add(tip);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.height = 150;
            _list = scroll.contentContainer;
            panel.Add(scroll);

            // 预览区:选中预设后展示完整配置 + 操作按钮
            _preview = new VisualElement();
            _preview.style.display = DisplayStyle.None;
            _preview.style.backgroundColor = new Color(0, 0, 0, 0.35f);
            _preview.style.borderTopLeftRadius = _preview.style.borderTopRightRadius =
            _preview.style.borderBottomLeftRadius = _preview.style.borderBottomRightRadius = 6;
            _preview.style.marginTop = 6;
            _preview.style.paddingTop = 8; _preview.style.paddingBottom = 8;
            _preview.style.paddingLeft = 10; _preview.style.paddingRight = 10;
            panel.Add(_preview);

            _previewText = new Label("");
            _previewText.style.fontSize = 13;
            _previewText.style.whiteSpace = WhiteSpace.Normal;
            _previewText.style.color = new Color(0.9f, 0.9f, 0.88f);
            _preview.Add(_previewText);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginTop = 6;
            var applyBtn = new Button(() => onApply?.Invoke()) { text = "应用此预设" };
            Style(applyBtn, 13);
            applyBtn.style.flexGrow = 1;
            actions.Add(applyBtn);
            var renameBtn = new Button(() => onRename?.Invoke()) { text = "改名", tooltip = "把选中的预设改名为上方输入框里的名称" };
            Style(renameBtn, 12);
            renameBtn.style.marginLeft = 6;
            actions.Add(renameBtn);
            var defBtn = new Button(() => onToggleDefault?.Invoke()) { text = "设默认", tooltip = "默认预设会在打开锦标赛创建界面时自动应用" };
            Style(defBtn, 12);
            defBtn.style.marginLeft = 6;
            actions.Add(defBtn);
            var delBtn = new Button(() => onDelete?.Invoke()) { text = "删除" };
            Style(delBtn, 12);
            delBtn.style.marginLeft = 6;
            actions.Add(delBtn);
            _preview.Add(actions);

            _message = new Label("");
            _message.style.fontSize = 13;
            _message.style.color = new Color(0.95f, 0.9f, 0.6f);
            _message.style.whiteSpace = WhiteSpace.Normal;
            _message.style.backgroundColor = new Color(0, 0, 0, 0.4f);
            _message.style.marginTop = 6;
            panel.Add(_message);

            _schedule = panel.schedule.Execute(() =>
            {
                try
                {
                    var s = getStatus?.Invoke();
                    if (s != null && _status.text != s) _status.text = s;
                    var m = getMessage?.Invoke();
                    if (m != null && _message.text != m) _message.text = m;
                }
                catch { /* 单次同步失败不终止周期任务 */ }
            }).Every(500);
        }

        /// <summary>重建预设列表:行 = 预设名按钮(默认标金星),点击触发预览。</summary>
        public void SetPresets(IReadOnlyList<Row> presets, Action<int> onSelect)
        {
            if (_list == null) return;
            _list.Clear();
            for (int i = 0; i < presets.Count; i++)
            {
                int idx = i;
                var btn = new Button(() => onSelect?.Invoke(idx))
                {
                    text = presets[i].Label,
                    tooltip = "点击预览此预设的完整配置"
                };
                btn.style.height = 26;
                btn.style.marginBottom = 2;
                btn.style.fontSize = 13;
                btn.style.backgroundColor = new Color(0.13f, 0.14f, 0.18f);
                btn.style.color = new Color(0.85f, 0.85f, 0.85f);
                _list.Add(btn);
            }
        }

        /// <summary>更新预览区;text 为 null 时隐藏。</summary>
        public void SetPreview(string text, bool hasSelection)
        {
            if (_preview == null) return;
            _preview.style.display = hasSelection ? DisplayStyle.Flex : DisplayStyle.None;
            if (hasSelection && _previewText.text != text) _previewText.text = text;
        }

        private static void Style(Button b, float fontSize)
        {
            b.style.fontSize = fontSize;
            b.style.height = 30;
            b.style.backgroundColor = new Color(0.18f, 0.19f, 0.25f);
            b.style.color = new Color(0.9f, 0.9f, 0.9f);
            b.style.borderTopLeftRadius = b.style.borderTopRightRadius =
            b.style.borderBottomLeftRadius = b.style.borderBottomRightRadius = 4;
        }

        private static void ApplyGameFont(VisualElement root)
        {
            try
            {
                var fonts = Resources.FindObjectsOfTypeAll<Font>();
                Font chosen = null;
                foreach (var f in fonts)
                {
                    if (f == null) continue;
                    if (f.name.ToLowerInvariant().Contains("noto")) { chosen = f; break; }
                }
                if (chosen == null)
                    foreach (var f in fonts)
                    {
                        if (f != null && !string.IsNullOrEmpty(f.name) && f.name.ToLowerInvariant().Contains("sans")) { chosen = f; break; }
                    }
                if (chosen != null)
                    root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(chosen));
            }
            catch { /* 字体注入失败仅影响观感 */ }
        }

        public void Dispose()
        {
            _schedule?.Pause();
            if (_settings != null) UnityEngine.Object.Destroy(_settings);
            if (_rootObject != null) UnityEngine.Object.Destroy(_rootObject);
            _root = null;
        }
    }
}
