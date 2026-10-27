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

        // 主路径：ClassIsland 退出时会调用 Host.StopAsync()，托管服务在这里收尾最可靠。
        services.AddHostedService<FanShutdownService>();

        // 兜底：应用停止事件与进程退出，任意一条先到都能完成恢复（Shutdown 内部幂等）。
        if (ClassIsland.Core.AppBase.Current is { } app)
        {
            app.AppStopping += (_, _) => FanService.Instance.Shutdown();
        }

        AppDomain.CurrentDomain.ProcessExit += (_, _) => FanService.Instance.Shutdown();

        // 再兜一层：Avalonia 调度器关闭时（界面线程已开始收尾）再试一次，Shutdown 内部幂等。
        Avalonia.Threading.Dispatcher.UIThread.ShutdownStarted += (_, _) => FanService.Instance.Shutdown();

        services.AddSettingsPage<FanControlSettingsPage>();
        services.AddComponent<FanComponent, FanComponentSettingsControl>();
        services.AddAction<SetFanSpeedAction, SetFanSpeedActionSettingsControl>();
    }
}
