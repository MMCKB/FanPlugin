using Microsoft.Extensions.Hosting;

namespace ClassIsland.FanControl.Services;

/// <summary>
/// 随宿主生命周期收尾的托管服务。
/// ClassIsland 退出时会调用 <c>Host.StopAsync()</c>，这是最可靠的收尾时机
/// （比 AppStopping / ProcessExit 更早、且有 5 秒预算）。
/// </summary>
public sealed class FanShutdownService : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            FanService.Instance.Shutdown();
        }
        catch
        {
            // 退出阶段不抛异常。
        }

        return Task.CompletedTask;
    }
}
