using ClassIsland.Core.Abstractions.Automation;
using ClassIsland.Core.Attributes;
using ClassIsland.FanControl.Services;

namespace ClassIsland.FanControl.Automation;

/// <summary>
/// 行动：把风扇转速设置为指定百分比。
/// </summary>
[ActionInfo("classisland.fancontrol.setSpeed", "设置风扇转速", "\ue8e1", true, "风扇控制")]
public class SetFanSpeedAction : ActionBase<SetFanSpeedActionSettings>
{
    protected override async Task OnInvoke()
    {
        await base.OnInvoke();
        if (Settings == null)
        {
            return;
        }

        FanService.Instance.ApplyToFans(Settings.FanId, Settings.Percent);
    }

    protected override async Task OnRevert()
    {
        await base.OnRevert();
        if (Settings == null)
        {
            return;
        }

        FanService.Instance.RestoreFans(Settings.FanId);
    }
}
