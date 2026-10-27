using System.Windows.Input;
using ClassIsland.FanControl.Core;
using ClassIsland.FanControl.Services;

namespace ClassIsland.FanControl.ViewModels;

/// <summary>
/// 风扇控制设置页面的视图模型。
/// </summary>
public class FanControlSettingsViewModel : Observable
{
    private readonly Avalonia.Threading.DispatcherTimer _pawnIoTimer;
    private bool _lastPawnIoInstalled;
    private bool _pawnIoKnown;
    private string _pawnIoMessage = "";

    public FanControlSettingsViewModel()
    {
        ResetAllCommand = new RelayCommand(() => Service.ResetAllToAuto());
        RefreshCommand = new RelayCommand(() =>
        {
            Service.RefreshNow();
            RefreshPawnIoState();
        });

        // 页面打开期间定期重测，卸载/安装 PawnIO 后无需重开设置页。
        _pawnIoTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _pawnIoTimer.Tick += (_, _) => RefreshPawnIoState();
    }

    public FanService Service => FanService.Instance;

    public ICommand ResetAllCommand { get; }

    public ICommand RefreshCommand { get; }

    /// <summary>是否已检测到风扇。</summary>
    public bool HasFans => Service.Fans.Count > 0;

    /// <summary>未检测到 PawnIO 时显示安装提示卡片；已安装时完全不出现。</summary>
    public bool PawnIoMissing => !Service.PawnIo.IsInstalled;

    public string PawnIoStatusText => Service.PawnIo.Summary;

    /// <summary>指定路径 / 安装驱动后的反馈信息。</summary>
    public string PawnIoMessage
    {
        get => _pawnIoMessage;
        private set
        {
            if (Set(ref _pawnIoMessage, value))
            {
                OnPropertyChanged(nameof(HasPawnIoMessage));
            }
        }
    }

    public bool HasPawnIoMessage => !string.IsNullOrEmpty(PawnIoMessage);

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
        _pawnIoTimer.Start();
    }

    /// <summary>重新检测 PawnIO 并刷新提示卡片（结果没变就不打扰绑定）。</summary>
    public void RefreshPawnIoState()
    {
        Service.RefreshPawnIo();

        var installed = Service.PawnIo.IsInstalled;
        if (_pawnIoKnown && installed == _lastPawnIoInstalled)
        {
            return;
        }

        _pawnIoKnown = true;
        _lastPawnIoInstalled = installed;
        OnPropertyChanged(nameof(PawnIoMissing));
        OnPropertyChanged(nameof(PawnIoStatusText));
        OnPropertyChanged(nameof(PawnIoPathText));
    }

    /// <summary>保存用户指定的 PawnIO 目录；返回是否在该目录找到了驱动文件。</summary>
    public bool SetPawnIoPath(string path)
    {
        var ok = Service.SetPawnIoCustomPath(path);
        PawnIoMessage = ok
            ? "已在该目录找到 PawnIO.sys。如果仍无法读取风扇，说明驱动还没注册到系统，请用「安装驱动…」。"
            : "该目录里没有 PawnIO.sys。PawnIO 是内核驱动，单纯指定路径不会让它生效，需要先安装。";
        OnPropertyChanged(nameof(PawnIoMissing));
        OnPropertyChanged(nameof(PawnIoStatusText));
        OnPropertyChanged(nameof(PawnIoPathText));
        return ok;
    }

    /// <summary>
    /// 用 pnputil 安装指定目录里的 PawnIO 驱动（需要管理员权限）。
    /// 目录中必须包含 .inf 文件。
    /// </summary>
    public bool InstallPawnIoDriver(string folder)
    {
        var inf = PawnIoLocator.FindInfFile(folder);
        if (inf == null)
        {
            PawnIoMessage = "该目录里没有 PawnIO.inf，无法安装。请先运行 PawnIO 安装程序（或解压安装包后选择解压目录）。";
            return false;
        }

        var (ok, output) = PawnIoLocator.InstallDriver(inf);
        PawnIoMessage = ok
            ? "驱动安装命令已执行，正在重新检测…"
            : "驱动安装失败：" + output;

        if (ok)
        {
            Service.SetPawnIoCustomPath(folder);
            RefreshPawnIoState();
            OnPropertyChanged(nameof(PawnIoPathText));
            PawnIoMessage = PawnIoMissing
                ? "安装命令已执行，但仍未检测到驱动。可能需要重启后再试，或该 .inf 与当前系统不匹配。"
                : "驱动已安装并检测成功。";
        }

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

    public void Detach()
    {
        Service.Updated -= OnServiceUpdated;
        _pawnIoTimer.Stop();
    }

    private void OnServiceUpdated(object? sender, EventArgs e) => OnPropertyChanged(nameof(HasFans));
}
