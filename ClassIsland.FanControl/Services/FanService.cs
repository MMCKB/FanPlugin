using System.Collections.ObjectModel;
using System.Security.Principal;
using Avalonia.Threading;
using ClassIsland.FanControl.Core;
using ClassIsland.FanControl.Models;
using LibreHardwareMonitor.Hardware;

namespace ClassIsland.FanControl.Services;

/// <summary>
/// 风扇数据服务：负责打开硬件监控、枚举风扇、轮询转速并下发调速指令。
/// </summary>
public sealed class FanService : Observable
{
    private readonly object _sync = new();
    private readonly UpdateVisitor _visitor = new();
    private readonly HashSet<string> _restoreOnShutdown = new();
    private readonly object _settingsLock = new();

    private Computer? _computer;
    private Timer? _pollTimer;
    private Timer? _saveTimer;
    private FanDevice[] _fanArray = Array.Empty<FanDevice>();
    private FanDevice[] _allFanArray = Array.Empty<FanDevice>();
    private string _configFolder = "";
    private bool _started;
    private bool _shutdown;

    private string _statusText = "正在初始化硬件监控…";
    private string _hintText = "";
    private bool _isReady;

    public static FanService Instance { get; } = new();

    public ObservableCollection<FanDevice> Fans { get; } = new();

    public PluginSettings Settings { get; private set; } = new();

    /// <summary>是否已成功打开硬件监控。</summary>
    public bool IsReady
    {
        get => _isReady;
        private set => Set(ref _isReady, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string HintText
    {
        get => _hintText;
        private set
        {
            if (Set(ref _hintText, value))
            {
                OnPropertyChanged(nameof(HasHint));
            }
        }
    }

    /// <summary>是否存在需要提示用户的环境问题。</summary>
    public bool HasHint => !string.IsNullOrWhiteSpace(_hintText);

    public bool IsAdministrator { get; } = CheckAdministrator();

    /// <summary>每次轮询刷新后触发（在 UI 线程）。</summary>
    public event EventHandler? Updated;

    public void Initialize(string configFolder)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _configFolder = configFolder;
        Settings = PluginSettings.Load(configFolder);
        RefreshPawnIo();
        Task.Run(OpenHardware);
    }

    private void OpenHardware()
    {
        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = false,
                IsStorageEnabled = false,
                IsNetworkEnabled = false,
                IsPsuEnabled = false,
                IsBatteryEnabled = false,
                IsPowerMonitorEnabled = false
            };

            computer.Open();

            var devices = new List<FanDevice>();
            foreach (var hardware in computer.Hardware)
            {
                Collect(hardware, devices);
            }

            var ordered = devices
                .GroupBy(x => x.Id)
                .Select(g => g.First())
                .OrderBy(x => HardwareRank(x))
                .ThenByDescending(x => x.HasTach)
                .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            lock (_sync)
            {
                _computer = computer;
                _allFanArray = ordered.ToArray();
                _fanArray = ordered.ToArray();
            }

            WriteDiagnostics(computer, ordered);

            Dispatcher.UIThread.Post(() =>
            {
                RebuildVisibleFans();

                IsReady = true;
                StatusText = ordered.Count == 0
                    ? "未检测到风扇传感器。若主板支持调速，请确认已安装 PawnIO 驱动并以管理员身份运行。"
                    : $"已检测到 {ordered.Count} 个风扇，其中 {ordered.Count(x => x.CanControl)} 个支持调速。";
                HintText = BuildHint(ordered);
                ApplySavedStates(ordered);
            });

            StartPolling();
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                StatusText = "初始化硬件监控失败：" + ex.Message;
                HintText = BuildHint(Array.Empty<FanDevice>());
            });
        }
    }

    /// <summary>
    /// 把硬件与传感器树写入插件配置目录下的 diagnostics.txt，方便排查“读不到转速”之类的问题。
    /// </summary>
    private void WriteDiagnostics(Computer computer, IReadOnlyList<FanDevice> devices)
    {
        try
        {
            if (string.IsNullOrEmpty(_configFolder))
            {
                return;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"管理员: {IsAdministrator}");
            sb.AppendLine($"PawnIO: {IsPawnIoInstalled()}");
            sb.AppendLine($"风扇条目: {devices.Count}");
            sb.AppendLine();

            foreach (var device in devices)
            {
                sb.AppendLine($"[风扇] {device.DisplayName}  id={device.Id}");
                sb.AppendLine($"       转速源={(device.Sensor != null ? "LHM 传感器" : device.HasTach ? "SuperIO 原始数据" : "无")}" +
                              $"  可调速={device.CanControl}");
            }

            sb.AppendLine();
            sb.AppendLine("== 硬件树 ==");
            foreach (var hardware in computer.Hardware)
            {
                AppendHardware(sb, hardware, 0);
            }

            Directory.CreateDirectory(_configFolder);
            File.WriteAllText(Path.Combine(_configFolder, "diagnostics.txt"), sb.ToString());
        }
        catch
        {
            // 诊断输出失败不影响插件运行。
        }
    }

    private static void AppendHardware(System.Text.StringBuilder sb, IHardware hardware, int depth)
    {
        var pad = new string(' ', depth * 2);
        var tach = SuperIoTachSource.TryCreate(hardware) != null ? " [含 SuperIO 原始转速]" : "";
        sb.AppendLine($"{pad}[{hardware.HardwareType}] {hardware.Name}  ({hardware.Identifier}){tach}");

        try
        {
            foreach (var sensor in hardware.Sensors)
            {
                var control = sensor.Control;
                var extra = control == null
                    ? ""
                    : $"  controlMode={control.ControlMode} sw={control.SoftwareValue} max={control.MaxSoftwareValue}";
                sb.AppendLine($"{pad}   {sensor.SensorType,-12} idx={sensor.Index,-2} {sensor.Name,-22} value={sensor.Value?.ToString("0.##") ?? "null"}{extra}");
            }
        }
        catch
        {
            sb.AppendLine($"{pad}   <读取传感器失败>");
        }

        foreach (var sub in hardware.SubHardware)
        {
            AppendHardware(sb, sub, depth + 1);
        }
    }

    private static void Collect(IHardware hardware, List<FanDevice> devices)
    {
        try
        {
            var sensors = hardware.Sensors;
            var fanSensors = sensors.Where(s => s.SensorType == SensorType.Fan).ToList();
            var controlSensors = sensors.Where(s => s.SensorType == SensorType.Control && s.Control != null).ToList();
            var usedControls = new HashSet<ISensor>();

            // 部分 SuperIO 芯片在 LHM 中只创建了调速通道、没有创建转速传感器，
            // 但芯片本身仍在刷新转速数组，这里作为兜底数据源。
            var tachSource = SuperIoTachSource.TryCreate(hardware);

            foreach (var fan in fanSensors)
            {
                // 大多数设备上，转速传感器与调速通道的 Index 是一一对应的。
                var controlSensor = controlSensors.FirstOrDefault(c => c.Index == fan.Index && !usedControls.Contains(c));
                if (controlSensor == null && controlSensors.Count == fanSensors.Count)
                {
                    var candidate = controlSensors[fanSensors.IndexOf(fan)];
                    if (!usedControls.Contains(candidate))
                    {
                        controlSensor = candidate;
                    }
                }

                if (controlSensor != null)
                {
                    usedControls.Add(controlSensor);
                }

                devices.Add(new FanDevice(fan, controlSensor, hardware.Name, fan.Name, fan.Identifier.ToString()));
            }

            // 只有调速通道、没有转速传感器的设备（例如部分独立显卡的风扇）。
            foreach (var controlSensor in controlSensors.Where(c => !usedControls.Contains(c)))
            {
                Func<float?>? fallback = null;
                if (tachSource != null)
                {
                    var index = controlSensor.Index;
                    fallback = () => tachSource.Get(index);
                }

                devices.Add(new FanDevice(null, controlSensor, hardware.Name, controlSensor.Name,
                    controlSensor.Identifier.ToString(), fallback));
            }
        }
        catch
        {
            // 某些硬件在枚举时可能抛出异常，忽略即可。
        }

        foreach (var sub in hardware.SubHardware)
        {
            Collect(sub, devices);
        }
    }

    private static int HardwareRank(FanDevice device)
    {
        var name = device.HardwareName;
        if (name.Contains("Nuvoton", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("ITE", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Fintek", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Winbond", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.Contains("GPU", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return 1;
    }

    private string BuildHint(IReadOnlyCollection<FanDevice> devices)
    {
        var hints = new List<string>();

        if (!IsAdministrator)
        {
            hints.Add("当前 ClassIsland 未以管理员身份运行，读取和调节风扇可能失败，建议以管理员身份启动。");
        }

        if (devices.Count > 0 && devices.All(x => !x.CanControl))
        {
            hints.Add("检测到风扇转速但未找到调速通道：请确认 PawnIO 驱动已安装，或在 BIOS 中关闭风扇的固件自动控制。");
        }

        return string.Join("\n", hints);
    }

    private PawnIoStatus? _pawnIo;

    /// <summary>
    /// 当前 PawnIO 检测结果。未初始化时按需检测，避免设置页在初始化完成前误报“未安装”。
    /// </summary>
    public PawnIoStatus PawnIo => _pawnIo ??= PawnIoLocator.Detect(Settings?.PawnIoCustomPath);

    /// <summary>重新检测 PawnIO（用户指定路径或加入 PATH 后调用）。</summary>
    public void RefreshPawnIo()
    {
        _pawnIo = PawnIoLocator.Detect(Settings?.PawnIoCustomPath);
    }

    /// <summary>保存用户指定的 PawnIO 目录并重新检测。</summary>
    public bool SetPawnIoCustomPath(string path)
    {
        var dir = PawnIoLocator.ResolveDirectory(path);
        Settings.PawnIoCustomPath = dir ?? path;
        SaveSettings();
        RefreshPawnIo();
        return dir != null;
    }

    private bool IsPawnIoInstalled()
    {
        try
        {
            return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled;
        }
        catch
        {
            return false;
        }
    }

    private void StartPolling()
    {
        _pollTimer?.Dispose();
        var interval = Math.Clamp(Settings.PollIntervalMs, 300, 10000);
        _pollTimer = new Timer(Poll, null, 250, interval);
    }

    /// <summary>按当前设置重新开始轮询。</summary>
    public void RestartPolling()
    {
        if (_started && !_shutdown)
        {
            StartPolling();
        }
    }

    /// <summary>立即刷新一次传感器数据。</summary>
    public void RefreshNow()
    {
        if (!_started || _shutdown)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ => Poll(null));
    }

    private void Poll(object? state)
    {
        if (_shutdown)
        {
            return;
        }

        var snapshot = new List<(FanDevice Device, float? Rpm, float? Software, bool Manual)>();

        lock (_sync)
        {
            if (_computer == null)
            {
                return;
            }

            try
            {
                _computer.Accept(_visitor);
            }
            catch
            {
                // 单次刷新失败不影响后续轮询。
            }

            foreach (var device in _fanArray)
            {
                float? rpm = null;
                float? software = null;
                var manual = false;

                try
                {
                    rpm = device.ReadRpm();
                    if (device.Control is { } control)
                    {
                        // 优先使用控制通道传感器的实时值，其次才是上次软件设定的值。
                        software = device.ControlSensor?.Value ?? control.SoftwareValue;
                        manual = control.ControlMode == ControlMode.Software;
                    }
                }
                catch
                {
                    // 忽略单个传感器的读取异常。
                }

                snapshot.Add((device, rpm, software, manual));
            }
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var (device, rpm, software, manual) in snapshot)
            {
                device.ApplyPolled(rpm, software, manual);
            }

            Updated?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>设置指定风扇的占空比（0-100）。</summary>
    public void SetPercent(FanDevice device, double percent)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetPercent(device, percent));
            return;
        }

        if (device.Control is not { } control)
        {
            return;
        }

        var target = Math.Clamp(percent, 0, 100);
        if (Settings.RespectSafeMinimum)
        {
            target = Math.Max(target, Math.Clamp(Settings.SafeMinimumPercent, 0, 100));
        }

        try
        {
            lock (_sync)
            {
                var max = control.MaxSoftwareValue > 0 ? control.MaxSoftwareValue : 100f;
                control.SetSoftware((float)(target / 100.0 * max));
            }

            device.ApplyUserResult(target, controlled: true);
            _restoreOnShutdown.Add(device.Id);
            lock (_settingsLock)
            {
                Settings.FanStates[device.Id] = new FanState { Manual = true, Percent = target };
            }
            RequestSave();
        }
        catch (Exception ex)
        {
            // 下发失败：把开关回退到未接管，避免界面状态与实际硬件不一致。
            device.ApplyUserResult(device.Percent, controlled: false);
            StatusText = $"设置「{device.DisplayName}」转速失败：{ex.Message}";
        }
    }

    /// <summary>将指定风扇恢复为主板/固件自动控制。</summary>
    public void ResetToAuto(FanDevice device)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ResetToAuto(device));
            return;
        }

        if (device.Control is not { } control)
        {
            return;
        }

        try
        {
            lock (_sync)
            {
                control.SetDefault();
            }

            device.ApplyUserResult(device.Percent, controlled: false);
            _restoreOnShutdown.Remove(device.Id);
            lock (_settingsLock)
            {
                Settings.FanStates[device.Id] = new FanState { Manual = false, Percent = device.Percent };
            }
            RequestSave();
        }
        catch (Exception ex)
        {
            StatusText = $"恢复「{device.DisplayName}」自动控制失败：{ex.Message}";
        }
    }

    /// <summary>把所有可控风扇恢复为自动控制。</summary>
    public void ResetAllToAuto()
    {
        foreach (var device in _fanArray.Where(x => x.CanControl))
        {
            ResetToAuto(device);
        }
    }

    /// <summary>按行动设置调速。fanId 为空时作用于所有可控风扇。</summary>
    public void ApplyToFans(string? fanId, double percent)
    {
        if (string.IsNullOrEmpty(fanId))
        {
            foreach (var device in _fanArray.Where(x => x.CanControl))
            {
                SetPercent(device, percent);
            }

            return;
        }

        var target = _fanArray.FirstOrDefault(x => x.Id == fanId);
        if (target != null)
        {
            SetPercent(target, percent);
        }
    }

    /// <summary>按行动设置恢复自动。fanId 为空时作用于所有风扇。</summary>
    public void RestoreFans(string? fanId)
    {
        if (string.IsNullOrEmpty(fanId))
        {
            ResetAllToAuto();
            return;
        }

        var target = _fanArray.FirstOrDefault(x => x.Id == fanId);
        if (target != null)
        {
            ResetToAuto(target);
        }
    }

    /// <summary>主界面组件使用的风扇。</summary>
    /// <summary>检测到的全部风扇（含被隐藏的），按硬件顺序。</summary>
    public IReadOnlyList<FanDevice> AllFans
    {
        get
        {
            lock (_sync)
            {
                return _allFanArray.ToArray();
            }
        }
    }

    /// <summary>按保存的顺序与隐藏列表重建 <see cref="Fans"/>。</summary>
    private void RebuildVisibleFans()
    {
        FanDevice[] all;
        lock (_sync)
        {
            all = _allFanArray.ToArray();
        }

        var hidden = new HashSet<string>(Settings.HiddenFanIds);
        var ordered = new List<FanDevice>();

        foreach (var id in Settings.FanOrder)
        {
            var match = all.FirstOrDefault(x => x.Id == id);
            if (match != null && !hidden.Contains(match.Id) && !ordered.Contains(match))
            {
                ordered.Add(match);
            }
        }

        // 未出现在顺序表里的风扇（新检测到的）追加到末尾。
        foreach (var device in all)
        {
            if (!hidden.Contains(device.Id) && !ordered.Contains(device))
            {
                ordered.Add(device);
            }
        }

        // 安全兜底：如果所有风扇都被隐藏（历史配置或异常数据），自动全部恢复显示，
        // 否则风扇列表会空掉、设置页的「调整」按钮也会被禁用，用户再也无法恢复。
        if (ordered.Count == 0 && all.Length > 0)
        {
            ordered.AddRange(all);
            Settings.HiddenFanIds.Clear();
            Settings.FanOrder = all.Select(x => x.Id).ToList();
            SaveSettings();
        }

        Fans.Clear();
        foreach (var device in ordered)
        {
            Fans.Add(device);
        }

        lock (_sync)
        {
            _fanArray = all;
        }
    }

    /// <summary>保存风扇列表的显示顺序与隐藏项，并立即刷新界面与组件显示。</summary>
    public void SaveFanLayout(IEnumerable<string> orderedVisibleIds, IEnumerable<string> hiddenIds)
    {
        var order = orderedVisibleIds.ToList();
        var hidden = hiddenIds.Distinct().Where(x => !order.Contains(x)).ToList();

        // 至少保留一个可见风扇。
        if (order.Count == 0 && hidden.Count > 0)
        {
            order.Add(hidden[0]);
            hidden.RemoveAt(0);
        }

        Settings.FanOrder = order.Concat(hidden).ToList();
        Settings.HiddenFanIds = hidden;
        SaveSettings();

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() =>
            {
                RebuildVisibleFans();
                Updated?.Invoke(this, EventArgs.Empty);
            });
            return;
        }

        RebuildVisibleFans();
        Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>风扇是否处于隐藏状态。</summary>
    public bool IsFanHidden(string fanId) => Settings.HiddenFanIds.Contains(fanId);

    /// <summary>按组件设置里的风扇 Id 取出要显示的风扇；为空或找不到时自动挑选。</summary>
    public FanDevice? GetDisplayFan(string? fanId)
    {
        var visible = Fans;
        if (visible.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrEmpty(fanId))
        {
            var match = visible.FirstOrDefault(x => x.Id == fanId);
            if (match != null)
            {
                return match;
            }
        }

        // 优先选“既能调速又能读到转速”的风扇，避免默认显示一个没有转速通道的设备。
        return visible.FirstOrDefault(x => x.CanControl && x.HasTach)
               ?? visible.FirstOrDefault(x => x.HasTach)
               ?? visible.FirstOrDefault(x => x.CanControl)
               ?? visible.FirstOrDefault();
    }

    private void ApplySavedStates(IEnumerable<FanDevice> devices)
    {
        if (!Settings.RestoreOnStartup)
        {
            return;
        }

        foreach (var device in devices)
        {
            if (device.Control == null)
            {
                continue;
            }

            if (Settings.FanStates.TryGetValue(device.Id, out var state) && state.Manual)
            {
                SetPercent(device, state.Percent);
            }
        }
    }

    private void RequestSave()
    {
        _saveTimer?.Dispose();
        _saveTimer = new Timer(_ =>
        {
            lock (_settingsLock)
            {
                Settings.Save(_configFolder);
            }
        }, null, 800, Timeout.Infinite);
    }

    public void SaveSettings()
    {
        Settings.Save(_configFolder);
    }

    /// <summary>插件停止时调用：恢复接管过的风扇并关闭硬件访问。</summary>
    public void Shutdown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _pollTimer?.Dispose();
        _saveTimer?.Dispose();

        var restored = 0;
        var failed = new List<string>();
        var details = new List<string>();

        try
        {
            // 退出阶段可能和轮询线程抢锁，这里给个上限，抢不到就跳过（宁可少关一次也不要卡死退出）。
            var locked = Monitor.TryEnter(_sync, 1500);
            try
            {
                foreach (var device in _fanArray)
                {
                    if (device.Control is not { } control)
                    {
                        continue;
                    }

                    // 只要“当前处于接管状态”或“记录在待恢复集合里”，都要交回系统。
                    // 双重条件是为了防止记录集合因异常路径没写进去。
                    if (!device.IsControlled && !_restoreOnShutdown.Contains(device.Id))
                    {
                        continue;
                    }

                    try
                    {
                        control.SetDefault();

                        // 有些芯片写完后需要一拍才真正切回固件控制，读回确认一次，必要时重试。
                        var mode = ReadControlMode(control);
                        if (mode == ControlMode.Software)
                        {
                            Thread.Sleep(30);
                            control.SetDefault();
                            mode = ReadControlMode(control);
                        }

                        restored++;
                        details.Add($"{device.Name}={mode?.ToString() ?? "未知"}");
                    }
                    catch (Exception ex)
                    {
                        failed.Add($"{device.DisplayName}: {ex.Message}");
                    }
                }

                _restoreOnShutdown.Clear();
                _computer?.Close();
                _computer = null;
            }
            finally
            {
                if (locked)
                {
                    Monitor.Exit(_sync);
                }
            }
        }
        catch (Exception ex)
        {
            failed.Add("关闭硬件访问: " + ex.Message);
        }

        lock (_settingsLock)
        {
            Settings.Save(_configFolder);
        }

        WriteShutdownLog(restored, failed, details);
    }

    /// <summary>安全地读一次控制模式，读不到返回 null。</summary>
    private static ControlMode? ReadControlMode(IControl control)
    {
        try
        {
            return control.ControlMode;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>退出时把恢复情况写进 shutdown.log，便于排查“退出后风扇没交回系统”。</summary>
    private void WriteShutdownLog(int restored, List<string> failed, List<string> details)
    {
        try
        {
            if (string.IsNullOrEmpty(_configFolder))
            {
                return;
            }

            Directory.CreateDirectory(_configFolder);
            var path = Path.Combine(_configFolder, "shutdown.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 恢复 {restored} 个风扇" +
                       (details.Count > 0 ? "（" + string.Join(", ", details) + "）" : "") +
                       (failed.Count == 0 ? "，无失败" : "，失败：" + string.Join(" | ", failed)) +
                       Environment.NewLine;
            File.AppendAllText(path, line);
        }
        catch
        {
            // 日志失败不影响退出。
        }
    }

    private static bool CheckAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>遍历所有硬件并刷新传感器数值。</summary>
    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            // 单个硬件更新失败（例如某些芯片不支持）不应影响其它硬件。
            try
            {
                hardware.Update();
            }
            catch
            {
                // 忽略。
            }

            foreach (var sub in hardware.SubHardware)
            {
                try
                {
                    sub.Accept(this);
                }
                catch
                {
                    // 忽略。
                }
            }
        }

        public void VisitSensor(ISensor sensor)
        {
        }

        public void VisitParameter(IParameter parameter)
        {
        }
    }
}
