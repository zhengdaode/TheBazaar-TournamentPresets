using System;
using System.IO;
using System.Text;
using LitJson;

namespace BazaarSim.Tournament
{
    /// <summary>tournament-presets.json 的读写与增删改。路径显式传入,便于测试与插件侧解耦。</summary>
    public static class TournamentPresetStore
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static string DefaultPath(string bepInExRoot) =>
            Path.Combine(bepInExRoot ?? "", "config", "TournamentPresets", "tournament-presets.json");

        /// <summary>读取;文件缺失或损坏时返回空的全新文件对象(不落盘,首次 Save 才创建)。</summary>
        public static TournamentPresetFile Load(string path)
        {
            try
            {
                if (!File.Exists(path)) return new TournamentPresetFile();
                var file = JsonMapper.ToObject<TournamentPresetFile>(File.ReadAllText(path));
                return file ?? new TournamentPresetFile();
            }
            catch (Exception)
            {
                return new TournamentPresetFile();
            }
        }

        /// <summary>原子落盘:先写 .tmp 再整体替换,避免写一半损坏预设。</summary>
        public static void Save(string path, TournamentPresetFile file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            var json = JsonMapper.ToJson(file);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Utf8NoBom);
            File.Copy(tmp, path, overwrite: true);
            File.Delete(tmp);
        }

        /// <summary>按名称(序数比较)覆盖或追加,返回更新后的文件对象。</summary>
        public static TournamentPresetFile Upsert(string path, TournamentPreset preset)
        {
            if (string.IsNullOrWhiteSpace(preset?.Name)) throw new ArgumentException("预设名称不能为空");
            var file = Load(path);
            var existing = file.Presets.FindIndex(p =>
                string.Equals(p.Name, preset.Name, StringComparison.Ordinal));
            if (existing >= 0) file.Presets[existing] = preset;
            else file.Presets.Add(preset);
            Save(path, file);
            return file;
        }

        /// <summary>按名称删除;返回是否确有删除。</summary>
        public static bool Delete(string path, string name)
        {
            var file = Load(path);
            var removed = file.Presets.RemoveAll(p =>
                string.Equals(p.Name, name, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                if (string.Equals(file.DefaultPreset, name, StringComparison.Ordinal))
                    file.DefaultPreset = null;
                Save(path, file);
            }
            return removed;
        }

        /// <summary>设置自动应用的预设;name 为 null 清除。名称不存在抛 ArgumentException。</summary>
        public static TournamentPresetFile SetDefault(string path, string name)
        {
            var file = Load(path);
            if (name != null && file.Presets.Find(p =>
                    string.Equals(p.Name, name, StringComparison.Ordinal)) == null)
                throw new ArgumentException("预设不存在: " + name);
            file.DefaultPreset = name;
            Save(path, file);
            return file;
        }

        /// <summary>取默认预设;未设置或指向已删除的名称时返回 null。</summary>
        public static TournamentPreset GetDefault(TournamentPresetFile file)
        {
            if (file?.DefaultPreset == null) return null;
            return file.Presets.Find(p =>
                string.Equals(p.Name, file.DefaultPreset, StringComparison.Ordinal));
        }

        /// <summary>名称是否已占用(保存时提示覆盖)。</summary>
        public static bool Exists(string path, string name)
        {
            return Load(path).Presets.Exists(p =>
                string.Equals(p.Name, name, StringComparison.Ordinal));
        }

        /// <summary>重命名预设;同步更新默认预设指向。目标名已存在或原名不存在时抛 ArgumentException。</summary>
        public static TournamentPresetFile Rename(string path, string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("新名称不能为空");
            var file = Load(path);
            var preset = file.Presets.Find(p =>
                string.Equals(p.Name, oldName, StringComparison.Ordinal));
            if (preset == null) throw new ArgumentException("预设不存在: " + oldName);
            if (file.Presets.Exists(p =>
                    string.Equals(p.Name, newName, StringComparison.Ordinal)))
                throw new ArgumentException("已存在同名预设: " + newName);
            preset.Name = newName;
            if (string.Equals(file.DefaultPreset, oldName, StringComparison.Ordinal))
                file.DefaultPreset = newName;
            Save(path, file);
            return file;
        }
    }
}
