using System.Reflection;
using ClassIsland.FanControl.Core;
using LibreHardwareMonitor.Hardware;

namespace ClassIsland.FanControl.Services;

/// <summary>
/// 单个风扇（转速 + 可选的控制通道）。
/// </summary>
public class FanDevice : Observable
{
    private readonly Func<float?>? _tachFallback;
    private double? _rpm;
    private double _percent;
    private bool _isControlled;
    private bool _canControl;
    private bool _suppressCallbacks;

    public FanDevice(
        ISensor? sensor,
        ISensor? controlSensor,
        string hardwareName,
        string name,
        string id,
        Func<float?>? tachFallback = null)
    {
        Sensor = sensor;
        ControlSensor = controlSensor;
        Control = controlSensor?.Control;
        HardwareName = hardwareName;
        Name = name;
        Id = id;
        _tachFallback = tachFallback;
        _canControl = Control != null;
    }

    /// <summary>风扇唯一标识（LibreHardwareMonitor 传感器 Identifier）。</summary>
    public string Id { get; }

    /// <summary>所属硬件名称，例如 “Nuvoton NCT6797D”。</summary>
    public string HardwareName { get; }

    /// <summary>转速传感器，可能为 null（LHM 未为该通道创建转速传感器时）。</summary>
    public ISensor? Sensor { get; }

    /// <summary>控制通道传感器（SensorType.Control），可能为 null。</summary>
    public ISensor? ControlSensor { get; }

    /// <summary>控制通道，为 null 表示此风扇不可调速（仅可读取转速）。</summary>
    public IControl? Control { get; }

    /// <summary>风扇名称，例如 “Fan #1”。</summary>
    public string Name { get; }

    public string DisplayName => $"{HardwareName} · {Name}";

    /// <summary>是否有可用的转速数据来源。</summary>
    public bool HasTach => Sensor != null || _tachFallback != null;

    public bool CanControl
    {
        get => _canControl;
        private set
        {
            if (Set(ref _canControl, value))
            {
                OnPropertyChanged(nameof(ModeText));
                OnPropertyChanged(nameof(CanAdjust));
            }
        }
    }

    /// <summary>是否允许用户调节（=存在调速通道）。</summary>
    public bool CanAdjust => _canControl;

    /// <summary>当前转速（RPM），读取不到时为 null。</summary>
    public double? Rpm
    {
        get => _rpm;
        private set
        {
            if (Set(ref _rpm, value))
            {
                OnPropertyChanged(nameof(RpmText));
            }
        }
    }

    public string RpmText
    {
        get
        {
            if (!HasTach)
            {
                return "无转速传感器";
            }

            return _rpm is { } v ? $"{v:0} RPM" : "-- RPM";
        }
    }

    public string PercentText => $"{_percent:0}%";

    /// <summary>
    /// 接管开关。true = ClassIsland 接管（软件控制）该风扇；false = 交回系统/主板自动控制。
    /// 绑定到界面开关，用户切换时由 FanService 实际下发指令。
    /// </summary>
    public bool IsControlled
    {
        get => _isControlled;
        set
        {
            if (!CanControl)
            {
                return;
            }

            if (Set(ref _isControlled, value))
            {
                OnPropertyChanged(nameof(ModeText));
                OnPropertyChanged(nameof(PercentEnabled));

                if (_suppressCallbacks)
                {
                    return;
                }

                if (value)
                {
                    FanService.Instance.SetPercent(this, _percent);
                }
                else
                {
                    FanService.Instance.ResetToAuto(this);
                }
            }
        }
    }

    /// <summary>滑块是否可用（仅在接管开启时可用）。</summary>
    public bool PercentEnabled => _canControl && _isControlled;

    /// <summary>当前占空比（0-100）。用户拖动滑块时立即下发。</summary>
    public double Percent
    {
        get => _percent;
        set
        {
            if (Math.Abs(value - _percent) < 0.01)
            {
                return;
            }

            _percent = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(PercentText));

            if (!_suppressCallbacks)
            {
                FanService.Instance.SetPercent(this, value);
            }
        }
    }

    public string ModeText
    {
        get
        {
            if (!CanControl)
            {
                return HasTach ? "仅可读取转速（系统控制）" : "不可用";
            }

            return IsControlled ? $"ClassIsland 接管 · {_percent:0}%" : "系统自动控制";
        }
    }

    internal float MaxSoftwareValue =>
        Control is { MaxSoftwareValue: > 0 } c ? c.MaxSoftwareValue : 100f;

    /// <summary>读取当前转速：优先用转速传感器，其次用芯片原始转速数据。</summary>
    internal float? ReadRpm()
    {
        try
        {
            if (Sensor?.Value is { } value)
            {
                return value;
            }
        }
        catch
        {
            // 忽略单个传感器的读取异常。
        }

        try
        {
            return _tachFallback?.Invoke();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 应用一轮轮询采样结果。controlPercent / manual 来自控制通道的实时读取。
    /// </summary>
    internal void ApplyPolled(float? rpm, float? controlPercent, bool manual)
    {
        Rpm = rpm;

        if (Control == null)
        {
            CanControl = false;
            return;
        }

        CanControl = true;

        _suppressCallbacks = true;
        try
        {
            if (IsControlled != manual)
            {
                // 注意：Set 的 CallerMemberName 在这里会取到 "ApplyPolled"，
                // 必须显式传属性名，否则界面收不到 IsControlled 的变更（开关不刷新）。
                Set(ref _isControlled, manual, nameof(IsControlled));
                OnPropertyChanged(nameof(ModeText));
                OnPropertyChanged(nameof(PercentEnabled));
            }

            if (controlPercent is { } value)
            {
                var percent = Math.Clamp(value / MaxSoftwareValue * 100.0, 0, 100);
                if (Math.Abs(percent - _percent) > 0.4)
                {
                    _percent = Math.Round(percent);
                    OnPropertyChanged(nameof(Percent));
                    OnPropertyChanged(nameof(PercentText));
                    OnPropertyChanged(nameof(ModeText));
                }
            }
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }

    /// <summary>用户操作（开关/滑块）成功后同步界面状态，避免被轮询回写造成抖动。</summary>
    internal void ApplyUserResult(double percent, bool controlled)
    {
        _suppressCallbacks = true;
        try
        {
            var rounded = Math.Round(percent);
            if (Math.Abs(_percent - rounded) > 0.01)
            {
                _percent = rounded;
                OnPropertyChanged(nameof(Percent));
            }

            if (_isControlled != controlled)
            {
                Set(ref _isControlled, controlled, nameof(IsControlled));
            }

            OnPropertyChanged(nameof(PercentText));
            OnPropertyChanged(nameof(ModeText));
            OnPropertyChanged(nameof(PercentEnabled));
        }
        finally
        {
            _suppressCallbacks = false;
        }
    }
}

/// <summary>
/// 部分主板（例如 NCT6797D 的某些板级配置）在 LibreHardwareMonitor 中只暴露调速通道，
/// 不创建转速传感器。此时通过反射读取 SuperIO 芯片对象上的原始转速数组。
/// </summary>
internal sealed class SuperIoTachSource
{
    private readonly object _superIo;
    private readonly PropertyInfo _fansProperty;

    private SuperIoTachSource(object superIo, PropertyInfo fansProperty)
    {
        _superIo = superIo;
        _fansProperty = fansProperty;
    }

    public static SuperIoTachSource? TryCreate(IHardware hardware)
    {
        try
        {
            var field = hardware.GetType().GetField("_superIO",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var superIo = field?.GetValue(hardware);
            if (superIo == null)
            {
                return null;
            }

            var fansProperty = superIo.GetType().GetProperty("Fans", BindingFlags.Instance | BindingFlags.Public);
            if (fansProperty?.GetValue(superIo) is not float?[])
            {
                return null;
            }

            return new SuperIoTachSource(superIo, fansProperty);
        }
        catch
        {
            return null;
        }
    }

    public float? Get(int index)
    {
        try
        {
            if (_fansProperty.GetValue(_superIo) is not float?[] fans || index < 0 || index >= fans.Length)
            {
                return null;
            }

            // 0 表示风扇停转，null 表示该通道无数据。
            return fans[index];
        }
        catch
        {
            return null;
        }
    }
}
