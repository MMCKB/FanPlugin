using System.Windows.Input;
using ClassIsland.FanControl.Core;
using ClassIsland.FanControl.Services;

namespace ClassIsland.FanControl.ViewModels;

/// <summary>
/// 风扇控制设置页面的视图模型。
/// </summary>
public class FanControlSettingsViewModel : Observable
{
    public FanControlSettingsViewModel()
    {
        ResetAllCommand = new RelayCommand(() => Service.ResetAllToAuto());
        RefreshCommand = new RelayCommand(() => Service.RefreshNow());
    }

    public FanService Service => FanService.Instance;

    public ICommand ResetAllCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>是否已检测到风扇。</summary>
    public bool HasFans => Service.Fans.Count > 0;

    /// <summary>未检测到 PawnIO 时显示安装提示卡片；已安装时完全不出现。</summary>
    public bool PawnIoMissing => !Service.PawnIo.IsInstalled;

    public string PawnIoStatusText => Service.PawnIo.Summary;

    public string PawnIoPathText => string.IsNullOrWhiteSpace(Service.Settings.PawnIoCustomPath)
        ? "未指定自定义路径"
        : Service.Settings.PawnIoCustomPath;

    public bool RespectSafeMinimum
    {
        get => Service.Settings.RespectSafeMinimum;
        set
        {
            if (Service.Settings.RespectSafeMinimum == value)
            {
                return;
            }

            Service.Settings.RespectSafeMinimum = value;
            Service.SaveSettings();
            OnPropertyChanged();
        }
    }

    public decimal? SafeMinimumPercent
    {
        get => (decimal)Service.Settings.SafeMinimumPercent;
        set
        {
            if (value is not { } v)
            {
                return;
            }

            Service.Settings.SafeMinimumPercent = (double)v;
            Service.SaveSettings();
            OnPropertyChanged();
        }
    }

    public decimal? PollIntervalMs
    {
        get => Service.Settings.PollIntervalMs;
        set
        {
            if (value is not { } v)
            {
                return;
            }

            Service.Settings.PollIntervalMs = (int)v;
            Service.SaveSettings();
            Service.RestartPolling();
            OnPropertyChanged();
        }
    }

    public bool RestoreOnStartup
    {
        get => Service.Settings.RestoreOnStartup;
        set
        {
            if (Service.Settings.RestoreOnStartup == value)
            {
                return;
            }

            Service.Settings.RestoreOnStartup = value;
            Service.SaveSettings();
            OnPropertyChanged();
        }
    }

    public void Attach()
    {
        Service.Updated += OnServiceUpdated;
        OnPropertyChanged(nameof(HasFans));
        RefreshPawnIoState();
    }

    /// <summary>重新检测 PawnIO 并刷新提示卡片。</summary>
    public void RefreshPawnIoState()
    {
        Service.RefreshPawnIo();
        OnPropertyChanged(nameof(PawnIoMissing));
        OnPropertyChanged(nameof(PawnIoStatusText));
        OnPropertyChanged(nameof(PawnIoPathText));
    }

    /// <summary>保存用户指定的 PawnIO 目录；返回是否在该目录找到了驱动文件。</summary>
    public bool SetPawnIoPath(string path)
    {
        var ok = Service.SetPawnIoCustomPath(path);
        OnPropertyChanged(nameof(PawnIoMissing));
        OnPropertyChanged(nameof(PawnIoStatusText));
        OnPropertyChanged(nameof(PawnIoPathText));
        return ok;
    }

    /// <summary>把自定义目录加入用户 PATH 并重新检测。</summary>
    public void AddPawnIoToPath()
    {
        var path = Service.Settings.PawnIoCustomPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        PawnIoLocator.AddToUserPath(path);
        RefreshPawnIoState();
    }

    public void Detach() => Service.Updated -= OnServiceUpdated;

    private void OnServiceUpdated(object? sender, EventArgs e) => OnPropertyChanged(nameof(HasFans));
}
