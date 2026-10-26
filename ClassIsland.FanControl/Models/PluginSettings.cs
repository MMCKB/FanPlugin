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

    public static PluginSettings Load(string folder)
    {
        try
        {
            var path = Path.Combine(folder, "Settings.json");
            if (!File.Exists(path))
            {
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
