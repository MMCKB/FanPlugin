using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClassIsland.FanControl.Models;

/// <summary>
/// 单个风扇持久化的状态。
/// </summary>
public class FanState
{
    /// <summary>
    /// 是否处于手动（软件）控制模式。
    /// </summary>
    public bool Manual { get; set; }

    /// <summary>
    /// 手动模式下的占空比（0-100）。
    /// </summary>
    public double Percent { get; set; } = 50;
}

/// <summary>
/// 插件设置。保存在插件配置目录下的 Settings.json 中。
/// </summary>
public class PluginSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// 转速轮询间隔（毫秒）。
    /// </summary>
    public int PollIntervalMs { get; set; } = 1000;

    /// <summary>
    /// 是否启用安全下限，避免手动把风扇调到过低转速导致过热。
    /// </summary>
    public bool RespectSafeMinimum { get; set; } = true;

    /// <summary>
    /// 安全下限（百分比）。
    /// </summary>
    public double SafeMinimumPercent { get; set; } = 20;

    /// <summary>
    /// 插件启动后是否恢复上次的手动转速设置。
    /// </summary>
    public bool RestoreOnStartup { get; set; } = true;

    /// <summary>
    /// 各风扇的持久化状态，键为风扇 Id。
    /// </summary>
    public Dictionary<string, FanState> FanStates { get; set; } = new();

    /// <summary>
    /// 风扇在列表中的显示顺序（存风扇 Id）。未列出的风扇会排在最后。
    /// </summary>
    public List<string> FanOrder { get; set; } = new();

    /// <summary>
    /// 被隐藏、不在风扇列表里显示的风扇 Id。
    /// </summary>
    public List<string> HiddenFanIds { get; set; } = new();

    /// <summary>
    /// 用户手动指定的 PawnIO 安装目录（非默认安装位置时使用）。
    /// </summary>
    public string PawnIoCustomPath { get; set; } = "";

    /// <summary>
    /// 强制保护开关：0% 交回系统控制、CPU 超过 80 °C 强制交回系统。
    /// 默认开启；关闭需要通过隐藏手势解锁。
    /// </summary>
    public bool ProtectionEnabled { get; set; } = true;

    /// <summary>
    /// 是否已经通过隐藏手势解锁「保护」设置项。
    /// </summary>
    public bool ProtectionUnlocked { get; set; }

    public static PluginSettings Load(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "Settings.json");
            if (!File.Exists(path))
            {
                // 插件 id 从 classisland.fancontrol 改成了 mmckb.fancontrol，
                // 配置目录跟着变，这里把旧目录的设置迁移过来，避免用户丢配置。
                if (TryLoadLegacy(folder) is { } legacy)
                {
                    legacy.Save(folder);
                    return legacy;
                }

                return new PluginSettings();
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PluginSettings>(json, JsonOptions) ?? new PluginSettings();
        }
        catch
        {
            // 配置损坏时回退到默认设置，不影响插件加载。
            return new PluginSettings();
        }
    }

    /// <summary>旧插件 id，仅用于配置迁移。</summary>
    private const string LegacyPluginId = "classisland.fancontrol";

    private static PluginSettings? TryLoadLegacy(string folder)
    {
        try
        {
            var parent = Path.GetDirectoryName(folder);
            if (string.IsNullOrEmpty(parent))
            {
                return null;
            }

            var oldPath = Path.Combine(parent, LegacyPluginId, "Settings.json");
            if (!File.Exists(oldPath))
            {
                return null;
            }

            var json = File.ReadAllText(oldPath);
            return JsonSerializer.Deserialize<PluginSettings>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "Settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // 忽略写入失败（例如磁盘只读）。
        }
    }
}
