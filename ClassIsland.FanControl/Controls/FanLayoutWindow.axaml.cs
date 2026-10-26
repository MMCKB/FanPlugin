using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClassIsland.FanControl.ViewModels;

namespace ClassIsland.FanControl.Controls;

/// <summary>
/// 「调整风扇列表」弹窗：左侧排序/隐藏，右侧显示已隐藏项。
/// 拖拽排序复用 ClassIsland 的 <c>AdvancedItemDragBehavior</c>（在 XAML 的 ItemContainerTheme 里挂载）。
/// </summary>
public partial class FanLayoutWindow : Window
{
    private readonly FanLayoutViewModel _viewModel = new();

    public FanLayoutWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void TitleBar_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void HideButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FanLayoutItem item })
        {
            _viewModel.Hide(item);
        }
    }

    private void ShowButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FanLayoutItem item })
        {
            _viewModel.Show(item);
        }
    }

    private void SaveButton_OnClick(object? sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        Close(true);
    }

    private void CancelButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);

    private void CloseButton_OnClick(object? sender, RoutedEventArgs e) => Close(false);
}
