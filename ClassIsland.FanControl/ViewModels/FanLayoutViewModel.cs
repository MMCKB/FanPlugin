using System.Collections.ObjectModel;
using ClassIsland.FanControl.Core;
using ClassIsland.FanControl.Services;

namespace ClassIsland.FanControl.ViewModels;

/// <summary>
/// 「调整风扇列表」弹窗的视图模型：左侧可排序的可见风扇，右侧被隐藏的风扇。
/// </summary>
public class FanLayoutViewModel : Observable
{
    public FanLayoutViewModel()
    {
        Load();
    }

    /// <summary>左侧：会显示在风扇列表里的风扇，顺序即列表顺序。</summary>
    public ObservableCollection<FanLayoutItem> VisibleFans { get; } = new();

    /// <summary>右侧：被隐藏、不显示在列表里的风扇。</summary>
    public ObservableCollection<FanLayoutItem> HiddenFans { get; } = new();

    public bool HasHiddenFans => HiddenFans.Count > 0;

    public bool HasVisibleFans => VisibleFans.Count > 0;

    /// <summary>还能不能再隐藏（至少保留一个可见风扇）。</summary>
    public bool CanHideMore => VisibleFans.Count > 1;

    private void Load()
    {
        var service = FanService.Instance;
        var all = service.AllFans;
        var hidden = new HashSet<string>(service.Settings.HiddenFanIds);

        // 先按已保存顺序排出可见风扇。
        foreach (var id in service.Settings.FanOrder)
        {
            var match = all.FirstOrDefault(x => x.Id == id);
            if (match != null && !hidden.Contains(match.Id) && VisibleFans.All(v => v.Id != match.Id))
            {
                VisibleFans.Add(FanLayoutItem.FromDevice(match));
            }
        }

        // 其余未在顺序表里的风扇：隐藏的进右侧，其余追加到左侧末尾。
        foreach (var device in all)
        {
            if (VisibleFans.Any(v => v.Id == device.Id) || HiddenFans.Any(v => v.Id == device.Id))
            {
                continue;
            }

            var item = FanLayoutItem.FromDevice(device);
            if (hidden.Contains(device.Id))
            {
                HiddenFans.Add(item);
            }
            else
            {
                VisibleFans.Add(item);
            }
        }
    }

    /// <summary>把风扇移到右侧（隐藏）。</summary>
    public void Hide(FanLayoutItem item)
    {
        // 至少保留一个可见风扇，否则风扇列表会空掉、连「调整」按钮都点不开。
        if (VisibleFans.Count <= 1)
        {
            return;
        }

        if (!VisibleFans.Remove(item))
        {
            return;
        }

        HiddenFans.Add(item);
        RaiseListChanged();
    }

    /// <summary>把风扇移回左侧（显示），排在末尾。</summary>
    public void Show(FanLayoutItem item)
    {
        if (!HiddenFans.Remove(item))
        {
            return;
        }

        VisibleFans.Add(item);
        RaiseListChanged();
    }

    /// <summary>拖动排序：把 <paramref name="fromIndex"/> 移到 <paramref name="toIndex"/>。</summary>
    public void Move(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || toIndex < 0 ||
            fromIndex >= VisibleFans.Count || toIndex >= VisibleFans.Count ||
            fromIndex == toIndex)
        {
            return;
        }

        VisibleFans.Move(fromIndex, toIndex);
    }

    /// <summary>保存到插件设置；返回给调用方决定是否关闭窗口。</summary>
    public void Save()
    {
        // 兜底：如果可见列表被清空，把右侧第一个拉回来。
        if (VisibleFans.Count == 0 && HiddenFans.Count > 0)
        {
            var first = HiddenFans[0];
            HiddenFans.RemoveAt(0);
            VisibleFans.Add(first);
        }

        FanService.Instance.SaveFanLayout(
            VisibleFans.Select(x => x.Id),
            HiddenFans.Select(x => x.Id));
    }

    private void RaiseListChanged()
    {
        OnPropertyChanged(nameof(HasHiddenFans));
        OnPropertyChanged(nameof(HasVisibleFans));
        OnPropertyChanged(nameof(CanHideMore));
    }
}

/// <summary>列表项。</summary>
public class FanLayoutItem
{
    public string Id { get; init; } = "";

    public string Name { get; init; } = "";

    /// <summary>副标题，例如模式与转速，便于辨认。</summary>
    public string Detail { get; init; } = "";

    public static FanLayoutItem FromDevice(FanDevice device) => new()
    {
        Id = device.Id,
        Name = device.DisplayName,
        Detail = device.HasTach ? device.RpmText : "无转速传感器"
    };
}
