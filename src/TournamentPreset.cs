using System;
using System.Collections.Generic;

namespace BazaarSim.Tournament
{
    /// <summary>锦标赛房主设置预设(tournament-presets.json 的单条记录)。
    /// 数值域与游戏校验一致:PlayerCap 2..1000,TotalRound 1..3600,AcceleratedRound 1..600。</summary>
    public sealed class TournamentPreset
    {
        public string Name { get; set; }
        /// <summary>ISO 8601 绝对时间(yyyy-MM-ddTHH:mm:ssZ)。</summary>
        public string CreatedAtUtc { get; set; }
        public bool PlayerCapEnabled { get; set; }
        public int PlayerCap { get; set; } = 1000;
        public bool StartOnFull { get; set; }
        public bool IsPublic { get; set; }
        public bool DisplayCode { get; set; }
        public int? TotalRoundCountdownSeconds { get; set; }
        public int? AcceleratedRoundCountdownSeconds { get; set; }
        /// <summary>ban 的 encounter(TCardBase.Id 的 Guid 串);Title 仅展示用,身份以 Id 为准。</summary>
        public List<BannedEncounterRef> BannedEncounters { get; set; } = new List<BannedEncounterRef>();
    }

    /// <summary>ban 的单个 encounter 引用。Id 对应 TCardBase.Id(变体组所有变体一起 ban/解 ban)。</summary>
    public sealed class BannedEncounterRef
    {
        public string Id { get; set; }
        public string Title { get; set; }
    }

    /// <summary>tournament-presets.json 顶层结构。DefaultPreset 为 null 表示不自动应用。</summary>
    public sealed class TournamentPresetFile
    {
        public int Version { get; set; } = 1;
        public string DefaultPreset { get; set; }
        public List<TournamentPreset> Presets { get; set; } = new List<TournamentPreset>();
    }
}
