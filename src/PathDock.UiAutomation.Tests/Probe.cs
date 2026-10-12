using System.Windows.Automation;

namespace PathDock.UiAutomation.Tests;

/// <summary>진단용: F2를 눌러 본 뒤 앱의 최상위 창 목록과 앱 로그를 출력한다. 사용: --probe-f2</summary>
public static class Probe
{
    public static void RunF2(string dataDir, string folder, string keys)
    {
        File.WriteAllText(Path.Combine(folder, "probe.txt"), "x");
        using var app = AppDriver.Launch(dataDir);
        app.SelectListItem("probe.txt");
        app.PressKeys(keys);
        Thread.Sleep(3000);
        foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            if (w.Current.ProcessId == app.ProcessId)
            {
                Console.WriteLine($"TOPLEVEL name='{w.Current.Name}' class='{w.Current.ClassName}'");
            }
        }
        foreach (AutomationElement c in app.Window.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            Console.WriteLine($"WINCHILD type={c.Current.ControlType.ProgrammaticName} name='{c.Current.Name}' class='{c.Current.ClassName}'");
        }
        foreach (string f in Directory.GetFiles(Path.Combine(dataDir, "logs")))
        {
            using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Console.WriteLine(new StreamReader(fs).ReadToEnd());
        }
    }

    /// <summary>진단용: 정보 창을 열어 창 하나만 PNG로 저장한다. 사용: --shot-about 출력경로.png</summary>
    public static void ShotAbout(string dataDir, string pngPath)
    {
        using var app = AppDriver.Launch(dataDir);
        app.Click("AboutButton");
        AutomationElement dlg = app.FindDialog("PathDock 정보");
        Thread.Sleep(600);
        Native.CaptureWindow(new IntPtr(dlg.Current.NativeWindowHandle), pngPath);
        Console.WriteLine("저장: " + pngPath);
    }
}
