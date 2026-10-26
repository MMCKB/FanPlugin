using ClassIsland.FanControl.Core;
using ClassIsland.FanControl.Services;

namespace ClassIsland.FanControl.ViewModels;

/// <summary>
/// 主界面风扇转速组件的视图模型。
/// </summary>
public class FanComponentViewModel : Observable
{
    private readonly Func<string?> _fanIdProvider;
    private string _displayText = "风扇 --";
    private bool _attached;

    public FanComponentViewModel(Func<string?> fanIdProvider)
    {
        _fanIdProvider = fanIdProvider;
    }

    public string DisplayText
    {
        get => _displayText;
        private set => Set(ref _displayText, value);
    }

    /// <summary>组件挂到视觉树时调用。会重新订阅并立即刷新一次。</summary>
    public void Attach()
    {
        if (!_attached)
        {
            FanService.Instance.Updated += OnUpdated;
            _attached = true;
        }

        Refresh();
    }

    /// <summary>组件从视觉树移除时调用。只取消订阅，不销毁自身，便于重新挂载。</summary>
    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        FanService.Instance.Updated -= OnUpdated;
        _attached = false;
    }

    public void Refresh()
    {
        var fan = FanService.Instance.GetDisplayFan(_fanIdProvider());
        if (fan == null)
        {
            DisplayText = "风扇 --";
            return;
        }

        DisplayText = fan.HasTach ? $"{fan.Name} {fan.RpmText}" : $"{fan.Name} 无转速传感器";
    }

    private void OnUpdated(object? sender, EventArgs e) => Refresh();
}
