namespace ClassIsland.FanControl.Automation;

/// <summary>
/// 「设置风扇转速」行动的设置。
/// </summary>
public class SetFanSpeedActionSettings
{
    /// <summary>目标风扇 Id，空字符串表示所有可控风扇。</summary>
    public string FanId { get; set; } = "";

    /// <summary>目标占空比（0-100）。</summary>
    public double Percent { get; set; } = 60;
}
