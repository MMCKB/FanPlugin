using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;
using ClassIsland.Core.Extensions.Registry;
using ClassIsland.FanControl.Automation;
using ClassIsland.FanControl.Controls;
using ClassIsland.FanControl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ClassIsland.FanControl;

/// <summary>
/// 风扇控制插件入口。
/// </summary>
[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services)
    {
        FanService.Instance.Initialize(PluginConfigFolder);

        if (ClassIsland.Core.AppBase.Current is { } app)
        {
            app.AppStopping += (_, _) => FanService.Instance.Shutdown();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => FanService.Instance.Shutdown();

        services.AddSettingsPage<FanControlSettingsPage>();
        services.AddComponent<FanComponent, FanComponentSettingsControl>();
        services.AddAction<SetFanSpeedAction, SetFanSpeedActionSettingsControl>();
    }
}
