using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.FanControl.Models;
using ClassIsland.FanControl.ViewModels;

namespace ClassIsland.FanControl.Controls;

/// <summary>
/// 主界面组件：显示指定风扇的实时转速。
/// </summary>
[ComponentInfo("7f2a5c10-3d64-4e1b-9c2a-5b8e1f4d6a21", "风扇转速", "\ue8e1",
    "在主界面显示风扇的实时转速。在组件设置里可以选择要显示哪一路风扇。")]
public partial class FanComponent : ComponentBase<FanComponentSettings>
{
    private readonly FanComponentViewModel _viewModel;
    private FanComponentSettings? _subscribedSettings;

    public FanComponent()
    {
        _viewModel = new FanComponentViewModel(() => Settings?.FanId);

        InitializeComponent();

        AttachedToVisualTree += (_, _) =>
        {
            SubscribeSettings();
            _viewModel.Attach();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            UnsubscribeSettings();
            _viewModel.Detach();
        };
    }

    /// <summary>
    /// 组件内容通过 <c>RelativeSource</c> 绑定到这里。
    /// 不能用 DataContext：宿主会把它覆盖成主窗口的 DataContext。
    /// </summary>
    public FanComponentViewModel ViewModel => _viewModel;

    private void SubscribeSettings()
    {
        var settings = Settings;
        if (settings == null || ReferenceEquals(_subscribedSettings, settings))
        {
            return;
        }

        UnsubscribeSettings();
        _subscribedSettings = settings;
        settings.PropertyChanged += OnSettingsChanged;
    }

    private void UnsubscribeSettings()
    {
        if (_subscribedSettings == null)
        {
            return;
        }

        _subscribedSettings.PropertyChanged -= OnSettingsChanged;
        _subscribedSettings = null;
    }

    private void OnSettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FanComponentSettings.FanId))
        {
            _viewModel.Refresh();
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
