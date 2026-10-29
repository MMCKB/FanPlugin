using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Enums.SettingsWindow;
using ClassIsland.FanControl.Services;
using ClassIsland.FanControl.ViewModels;
using FluentAvalonia.UI.Controls;

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

        AttachedToVisualTree += (_, _) =>
        {
            _viewModel.Attach();
            HookPageTitle();
        };

        DetachedFromVisualTree += (_, _) =>
        {
            _viewModel.Detach();
            UnhookPageTitle();
        };
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

    /// <summary>
    /// 滑条保持完整的 0-100 轨道，但开启安全下限时不允许拖到下限以下。
    /// 绑定写回会被 Avalonia 去重，所以直接在控件层把值顶回去。
    /// </summary>
    private void FanPercent_OnValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (sender is not Slider { DataContext: FanDevice device } slider)
        {
            return;
        }

        // 没接管时滑条只是显示系统当前值，不要去夹它（否则会顺手把控制权抢过来）。
        if (!device.IsControlled)
        {
            return;
        }

        var minimum = device.MinimumPercent;
        if (minimum > 0 && e.NewValue < minimum - 0.01)
        {
            slider.SetCurrentValue(Slider.ValueProperty, minimum);
        }
    }

    private void AddPawnIoToPath_OnClick(object? sender, RoutedEventArgs e)
        => _viewModel.AddPawnIoToPath();

    private void RecheckPawnIo_OnClick(object? sender, RoutedEventArgs e)
        => _viewModel.RefreshPawnIoState();

    // ===== 隐藏手势：连点页面标题 10 次解锁「保护」设置 =====

    private const string PageTitle = "风扇控制";
    private const int UnlockClickCount = 10;

    /// <summary>需要一字不差输入的确认文字。</summary>
    private const string UnlockPhrase = "我已了解风险并自愿关闭全部保护，一切后果由本人承担";

    private const string UnlockWarning =
        "您正在关闭「风扇控制」插件的强制保护。关闭后：\n" +
        "· 滑条可以设为 0%；\n" +
        "· CPU 温度超过 80 °C 时不再自动交回系统控制；\n" +
        "· 「安全下限」仍按你自己的设置生效（那是单独的选项）。\n" +
        "风扇长时间低速或停转可能导致 CPU / GPU 过热、降频、系统不稳定甚至硬件损坏。\n" +
        "由此产生的一切后果由您自行承担，插件作者不承担任何责任。";

    private TextBlock? _titleBlock;
    private Window? _titleWindow;
    private int _titleClicks;

    /// <summary>在设置窗口里找到页面标题，并在窗口上挂一个捕获阶段的点击计数。</summary>
    private void HookPageTitle()
    {
        if (_titleBlock != null)
        {
            return;
        }

        if (TopLevel.GetTopLevel(this) is not Window window)
        {
            return;
        }

        var candidates = window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.Text == PageTitle)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        // 设置窗口顶部的大标题（在 TitleContainer 里），不是左侧导航项。
        var title = candidates.FirstOrDefault(IsInTitleContainer)
                    ?? candidates.OrderByDescending(t => t.FontSize).First();

        _titleBlock = title;
        _titleWindow = window;

        // TextBlock 本身不一定参与命中测试（没有背景），所以挂在窗口上按坐标判断。
        window.AddHandler(PointerPressedEvent, Title_OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void UnhookPageTitle()
    {
        if (_titleWindow != null)
        {
            _titleWindow.RemoveHandler(PointerPressedEvent, Title_OnPointerPressed);
            _titleWindow = null;
        }

        _titleBlock = null;
        _titleClicks = 0;
    }

    private static bool IsInTitleContainer(Visual visual)
    {
        for (Visual? current = visual; current != null; current = current.GetVisualParent())
        {
            if (current is Control { Name: "TitleContainer" })
            {
                return true;
            }
        }

        return false;
    }

    private async void Title_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 标题控件是复用的，切到别的页面后文字会变，这里要重新确认。
        if (_titleBlock is not { } title || title.Text != PageTitle)
        {
            _titleClicks = 0;
            return;
        }

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 只统计点在标题文字范围内的点击。
        var position = e.GetPosition(title);
        if (position.X < 0 || position.Y < 0 ||
            position.X > title.Bounds.Width || position.Y > title.Bounds.Height)
        {
            return;
        }

        if (++_titleClicks < UnlockClickCount)
        {
            return;
        }

        _titleClicks = 0;

        if (_viewModel.ProtectionUnlocked)
        {
            return;
        }

        await ShowUnlockDialogAsync();
    }

    private async Task ShowUnlockDialogAsync()
    {
        if (TopLevel.GetTopLevel(this) is not { } topLevel)
        {
            return;
        }

        while (true)
        {
            var input = new TextBox
            {
                Watermark = "在此输入上面的文字",
                AcceptsReturn = false
            };

            var panel = new StackPanel { Spacing = 10, Width = 460 };
            panel.Children.Add(new TextBlock
            {
                Text = UnlockWarning,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = "请输入以下文字以确认（需完全一致）：",
                FontWeight = FontWeight.SemiBold,
                Margin = new Thickness(0, 6, 0, 0)
            });
            panel.Children.Add(new TextBlock
            {
                Text = UnlockPhrase,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85
            });
            panel.Children.Add(input);

            var dialog = new ContentDialog
            {
                Title = "启用无保护模式",
                Content = panel,
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync(topLevel);
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            if (Normalize(input.Text) == Normalize(UnlockPhrase))
            {
                _viewModel.UnlockProtection();
                await ShowMessageAsync(topLevel, "已解锁保护设置",
                    "「保护」卡片已经出现，你可以在里面关闭强制保护。再次提醒：由此产生的一切后果由你自行承担。");
                return;
            }

            await ShowMessageAsync(topLevel, "验证结果不正确", "请重新输入。");
        }
    }

    private static async Task ShowMessageAsync(TopLevel topLevel, string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Width = 420 },
            CloseButtonText = "好",
            DefaultButton = ContentDialogButton.Close
        };

        await dialog.ShowAsync(topLevel);
    }

    /// <summary>去掉空白并把半角标点归一化，避免用户被标点卡住。</summary>
    private static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                continue;
            }

            builder.Append(ch switch
            {
                ',' => '，',
                '.' => '。',
                ';' => '；',
                ':' => '：',
                '!' => '！',
                '?' => '？',
                _ => ch
            });
        }

        return builder.ToString();
    }
}
