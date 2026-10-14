# The Bazaar TournamentPresets(锦标赛预设)

《The Bazaar》锦标赛房主设置预设 —— BepInEx 插件。
Save / preview / apply / auto-apply tournament host setting presets (player cap, timers, public, show-code, encounter bans).

## 功能特性

- **F9** 开关面板(可在配置文件改键;Bazaar++ 占用的 F8 已避开)
- **批量 ban**:按类型(商人/事件/怪物/底座/全部)一键全选/取消/清空,过滤规则与游戏弹窗一致
- 一键保存"锦标赛 → 创建"界面的全部设置:人数上限、人满即开、公开列表、显示房间码、回合倒计时、加速倒计时、遭遇 ban 列表
- 点击预设行**预览**完整配置;预览区内执行 应用 / 改名 / 设默认 / 删除
- **默认预设自动应用**:每次打开锦标赛创建界面自动填好(Harmony)
- **ban 弹窗勾选同步**:自动应用后开/关 ban 弹窗不会丢 ban
- 全部数据存本地 JSON(`BepInEx/config/TournamentPresets/tournament-presets.json`),不修改任何服务器数据

## 安装(解压 → 复制 → 使用)

前置:[BepInEx 5.x(Mono 版)](https://github.com/BepInEx/BepInEx) 已安装并能正常进游戏。

1. 从 [Releases](../../releases) 下载 `TournamentPresets-vX.Y.Z.zip`
2. 解压到**游戏根目录**(如 `SteamLibrary\steamapps\common\The Bazaar`),得到 `BepInEx\plugins\TournamentPresets\`
3. 启动游戏,主菜单按 **F9**

## 使用

1. 游戏内 锦标赛 → 创建,把人数/时间/ban 配置好
2. F9 打开面板 → 输入框填名字(可留空,自动命名)→ **保存当前为预设**
3. 以后开新锦标赛:创建界面打开时 F9 → 点预设行预览 → **应用此预设**
4. 点 **设默认** 后,每次打开创建界面自动填好,直接点"创建"即可
5. 改名:预览选中预设后,输入框填新名字,点 **改名**

## 从源码构建

- 需要 .NET SDK 8+;编译时引用本机游戏的 Managed DLL
- `dotnet build src/BazaarSim.Tournament -p:GameDir="你的游戏目录"`(`GameDir` 默认是作者的本地路径,务必覆盖)
- 将 `src/BazaarSim.Tournament/bin/Debug/netstandard2.1/` 下的 `BazaarSim.Tournament.dll` 与 `LitJSON.dll` 放入 `BepInEx/plugins/TournamentPresets/`

## 工作原理(简)

通过反射定位游戏 UI 类 `TournamentHostSettingsView`,读取控件值存为 JSON;应用时反向写入控件(与手动操作等价);Harmony 挂 `OnEnable` 后缀实现自动应用。游戏更新若改了类名,面板状态行会显示 ✗,手动功能仍可诊断。

## 常见问题

- **F9 没反应?** 其他 mod 占键时改 `BepInEx/config/local.bazaarsim.tournamentpresets.cfg` 的 `ToggleKey`
- **状态行显示 ✗?** 游戏更新改了类名;等适配或自行更新 `TournamentSettingsBridge` 中的锚点名
- **保存提示"未找到设置界面"?** 先在游戏里打开一次 锦标赛 → 创建

## 免责声明

本项目仅供学习、研究与个人便利,按"现状"提供。作者与 Tempo Studios / The Bazaar 官方无关;不修改服务器数据、不影响他人对局;使用产生的任何后果由使用者自负,请遵守游戏用户协议。

## 许可证

[MIT](LICENSE)
