using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using ClassIsland.Core.Abstractions.Controls;
using ClassIsland.FanControl.Automation;
using ClassIsland.FanControl.Services;
using ClassIsland.FanControl.ViewModels;

namespace ClassIsland.FanControl.Controls;

/// <summary>
/// 「设置风扇转速」行动的设置界面（以代码构建，避免额外的 XAML 依赖）。
/// </summary>
public class SetFanSpeedActionSettingsControl : ActionSettingsControlBase<SetFanSpeedActionSettings>
{
    private readonly ComboBox _fanBox = new();
    private readonly Slider _slider = new() { Minimum = 0, Maximum = 100, TickFrequency = 1 };
    private readonly TextBlock _valueText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 44 };

    private bool _loaded;

    public SetFanSpeedActionSettingsControl()
    {
        _fanBox.DisplayMemberBinding = new Binding(nameof(FanChoice.Name));
        _fanBox.SelectionChanged += OnFanSelectionChanged;
        _slider.ValueChanged += OnSliderValueChanged;

        var sliderRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        sliderRow.Children.Add(_slider);
        Grid.SetColumn(_valueText, 1);
        _valueText.Margin = new Thickness(8, 0, 0, 0);
        sliderRow.Children.Add(_valueText);

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "作用风扇" });
        panel.Children.Add(_fanBox);
        panel.Children.Add(new TextBlock { Text = "转速百分比" });
        panel.Children.Add(sliderRow);
        panel.Children.Add(new TextBlock
        {
            Text = "提示：手动设定会覆盖主板风扇曲线，行动恢复时会交回自动控制。",
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });

        Content = panel;
        AttachedToVisualTree += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
    {
        SetFanSpeedActionSettings? settings;
        try
        {
            settings = Settings;
        }
        catch
        {
            settings = null;
        }

        settings ??= new SetFanSpeedActionSettings();

        var choices = new List<FanChoice> { new() { Id = "", Name = "全部可控风扇" } };
        choices.AddRange(FanService.Instance.Fans.Select(fan => new FanChoice { Id = fan.Id, Name = fan.DisplayName }));

        _loaded = false;
        _fanBox.ItemsSource = choices;
        _fanBox.SelectedItem = choices.FirstOrDefault(x => x.Id == settings.FanId) ?? choices[0];
        _slider.Value = settings.Percent;
        _valueText.Text = $"{settings.Percent:0}%";
        _loaded = true;
    }

    private void OnFanSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || _fanBox.SelectedItem is not FanChoice choice)
        {
            return;
        }

        try
        {
            Settings.FanId = choice.Id;
        }
        catch
        {
            // 设置尚未就绪时忽略。
        }
    }

    private void OnSliderValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (!_loaded)
        {
            return;
        }

        _valueText.Text = $"{e.NewValue:0}%";

        try
        {
            Settings.Percent = e.NewValue;
        }
        catch
        {
            // 设置尚未就绪时忽略。
        }
    }
}
