using System.Collections.ObjectModel;
using ClassIsland.FanControl.Core;
using ClassIsland.FanControl.Models;
using ClassIsland.FanControl.Services;

namespace ClassIsland.FanControl.ViewModels;

/// <summary>
/// 「风扇转速」组件设置的视图模型。
/// </summary>
public class FanComponentSettingsViewModel : Observable
{
    private readonly Func<FanComponentSettings?> _settingsProvider;
    private bool _attached;

    public FanComponentSettingsViewModel(Func<FanComponentSettings?> settingsProvider)
    {
        _settingsProvider = settingsProvider;
    }

    /// <summary>可选风扇列表，第一项是“自动”。</summary>
    public ObservableCollection<FanChoice> FanChoices { get; } = new();

    /// <summary>当前选中的风扇项。</summary>
    public FanChoice? SelectedFanChoice
    {
        get
        {
            var id = _settingsProvider()?.FanId ?? "";
            return FanChoices.FirstOrDefault(x => x.Id == id) ?? FanChoices.FirstOrDefault();
        }
        set
        {
            var settings = _settingsProvider();
            if (settings == null || value == null)
            {
                return;
            }

            settings.FanId = value.Id;
        }
    }

    public void Attach()
    {
        if (!_attached)
        {
            FanService.Instance.Updated += OnServiceUpdated;
            _attached = true;
        }

        RebuildChoices();
    }

    public void Detach()
    {
        if (!_attached)
        {
            return;
        }

        FanService.Instance.Updated -= OnServiceUpdated;
        _attached = false;
    }

    private void OnServiceUpdated(object? sender, EventArgs e) => RebuildChoices();

    private void RebuildChoices()
    {
        var fans = FanService.Instance.Fans;

        // 依据“可见风扇的 Id 序列”判断是否需要重建，
        // 这样在对话框里调整顺序或隐藏风扇后，组件设置里的下拉也会同步变化。
        var currentIds = FanChoices.Skip(1).Select(x => x.Id).ToList();
        var expectedIds = fans.Select(x => x.Id).ToList();

        if (!currentIds.SequenceEqual(expectedIds))
        {
            FanChoices.Clear();
            FanChoices.Add(new FanChoice { Id = "", Name = "（自动：优先可调速且有转速的风扇）" });
            foreach (var fan in fans)
            {
                FanChoices.Add(new FanChoice { Id = fan.Id, Name = fan.DisplayName });
            }
        }

        // 选中的风扇被隐藏后，回退到“自动”。
        var settings = _settingsProvider();
        if (settings != null && !string.IsNullOrEmpty(settings.FanId) &&
            FanChoices.All(x => x.Id != settings.FanId))
        {
            settings.FanId = "";
        }

        OnPropertyChanged(nameof(SelectedFanChoice));
    }
}

/// <summary>风扇下拉项。</summary>
public class FanChoice
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";
}
