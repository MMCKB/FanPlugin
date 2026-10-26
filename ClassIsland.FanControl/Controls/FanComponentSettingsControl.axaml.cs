using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.FanControl.Models;
using ClassIsland.FanControl.ViewModels;

namespace ClassIsland.FanControl.Controls;

/// <summary>
/// 「风扇转速」组件的设置界面。由 ClassIsland 在组件设置抽屉中实例化。
/// </summary>
public partial class FanComponentSettingsControl : ComponentBase<FanComponentSettings>
{
    private readonly FanComponentSettingsViewModel _viewModel;
    private StackPanel? _root;

    public FanComponentSettingsControl()
    {
        _viewModel = new FanComponentSettingsViewModel(() => Settings);

        InitializeComponent();

        _root = this.FindControl<StackPanel>("RootPanel");
        if (_root != null)
        {
            _root.DataContext = _viewModel;
        }

        AttachedToVisualTree += (_, _) => _viewModel.Attach();
        DetachedFromVisualTree += (_, _) => _viewModel.Detach();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
