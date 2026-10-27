using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.FanControl.Services;
using ClassIsland.FanControl.ViewModels;

namespace ClassIsland.FanControl.Controls;

/// <summary>
/// 风扇控制设置页面。
/// </summary>
[SettingsPageInfo("classisland.fancontrol.settings", "风扇控制", "\ue8e1", "\ue8e0", SettingsPageCategory.External)]
public partial class FanControlSettingsPage : SettingsPageBase
{
    private readonly FanControlSettingsViewModel _viewModel = new();

    public FanControlSettingsPage()
    {
        InitializeComponent();
        DataContext = _viewModel;

        AttachedToVisualTree += (_, _) => _viewModel.Attach();
        DetachedFromVisualTree += (_, _) => _viewModel.Detach();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void AdjustButton_OnClick(object? sender, RoutedEventArgs e)
    {
        var window = new FanLayoutWindow();
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
        {
            await window.ShowDialog<bool>(owner);
        }
        else
        {
            window.Show();
        }
    }

    private void OpenPawnIoDownload_OnClick(object? sender, RoutedEventArgs e)
        => PawnIoLocator.OpenDownloadPage();

    private async void PickPawnIoDirectory_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择 PawnIO 安装目录",
            AllowMultiple = false
        });

        var folder = folders.FirstOrDefault();
        if (folder?.Path is not { } uri)
        {
            return;
        }

        _viewModel.SetPawnIoPath(uri.LocalPath);
    }

    private async void InstallPawnIoDriver_OnClick(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage)
        {
            return;
        }

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择包含 PawnIO.inf 的目录",
            AllowMultiple = false
        });

        if (folders.FirstOrDefault()?.Path is not { } uri)
        {
            return;
        }

        _viewModel.InstallPawnIoDriver(uri.LocalPath);
    }

    private void AddPawnIoToPath_OnClick(object? sender, RoutedEventArgs e)
        => _viewModel.AddPawnIoToPath();

    private void RecheckPawnIo_OnClick(object? sender, RoutedEventArgs e)
        => _viewModel.RefreshPawnIoState();
}
