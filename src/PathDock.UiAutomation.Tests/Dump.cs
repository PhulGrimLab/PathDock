using System.Windows.Automation;

namespace PathDock.UiAutomation.Tests;

/// <summary>진단용: 앱을 띄워 UI Automation 트리(이름/종류/AutomationId)를 출력한다. 사용: --dump [AutomationId]</summary>
public static class Dump
{
    public static void Run(string dataDir, string? rootId)
    {
        using var app = AppDriver.Launch(dataDir);
        AutomationElement root = rootId is null ? app.Window : app.Find(rootId);
        Print(root, 0, 6);
    }

    private static void Print(AutomationElement e, int depth, int maxDepth)
    {
        Console.WriteLine($"{new string(' ', depth * 2)}[{e.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}] name='{e.Current.Name}' id='{e.Current.AutomationId}'");
        if (depth >= maxDepth)
        {
            return;
        }
        foreach (AutomationElement c in e.FindAll(TreeScope.Children, Condition.TrueCondition))
        {
            Print(c, depth + 1, maxDepth);
        }
    }
}
