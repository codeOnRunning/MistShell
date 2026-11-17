namespace MistShell.Models;

public sealed record TerminalFontOption(string Family, string DisplayName, string Sample);

public static class TerminalFonts
{
    public static readonly IReadOnlyList<TerminalFontOption> All =
    [
        new("JetBrains Mono", "JetBrains Mono（内置）", "JetBrains Mono"),
        new("Cascadia Mono", "Cascadia Mono（内置）", "Cascadia Mono"),
        new("Cascadia Code", "Cascadia Code（连字）", "Cascadia Code"),
        new("Consolas", "Consolas", "Consolas"),
        new("Courier New", "Courier New", "Courier New"),
        new("Lucida Console", "Lucida Console", "Lucida Console"),
        new("Microsoft YaHei UI", "微软雅黑（中文优先）", "Microsoft YaHei UI")
    ];

    public static string BuildCssStack(string? family)
    {
        var selected = All.FirstOrDefault(f => string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase))
                       ?? All[0];
        return $"\"{selected.Family}\", \"Cascadia Mono\", \"JetBrains Mono\", \"Consolas\", \"Microsoft YaHei UI\", monospace";
    }
}
