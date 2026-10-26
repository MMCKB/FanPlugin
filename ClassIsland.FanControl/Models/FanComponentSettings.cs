using ClassIsland.FanControl.Core;

namespace ClassIsland.FanControl.Models;

/// <summary>
/// 「风扇转速」组件的设置。每个组件实例独立保存。
/// </summary>
public class FanComponentSettings : Observable
{
    private string _fanId = "";

    /// <summary>
    /// 要显示的风扇 Id。空字符串表示自动选择（优先显示可调速且有转速读数的风扇）。
    /// </summary>
    public string FanId
    {
        get => _fanId;
        set => Set(ref _fanId, value);
    }
}
