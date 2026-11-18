using System.Text.Json.Serialization;

namespace MistShell.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; }
    public string Theme { get; set; } = "Mist";
    public bool FollowTerminalDirectory { get; set; } = true;
    public string TerminalFontFamily { get; set; } = "JetBrains Mono";
    public string TerminalForegroundColor { get; set; } = "";
    public double TerminalFontSize { get; set; } = 14;
    public double TerminalLineHeight { get; set; } = 1.25;
    public int ScrollbackLines { get; set; } = 3000;
    public bool LowMemoryMode { get; set; } = true;
    public double SidebarWidth { get; set; } = 270;
    public double DockWidth { get; set; } = 360;
    public bool DockOpen { get; set; } = true;
    public int DockTab { get; set; } = 0;
    public double WindowWidth { get; set; } = 1440;
    public double WindowHeight { get; set; } = 900;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
    public bool WindowMaximized { get; set; }
    public string? LastConnectionId { get; set; }
    public List<string> CommandHistory { get; set; } = new();
    public List<CommandSnippet> Snippets { get; set; } = new();
    public Dictionary<string, string> KnownHostKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> KnownHostKeyTypes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SavedPrivateKey> PrivateKeys { get; set; } = new();

    [JsonIgnore]
    public int EffectiveScrollback => LowMemoryMode ? Math.Min(ScrollbackLines, 2000) : ScrollbackLines;

    public static List<CommandSnippet> CreateDefaultSnippets() =>
    [
        new() { Name = "系统概况", Command = "uptime && echo '---' && free -h && echo '---' && df -h", Group = "系统" },
        new() { Name = "磁盘占用", Command = "df -h", Group = "系统" },
        new() { Name = "内存占用", Command = "free -h", Group = "系统" },
        new() { Name = "CPU 前 15 进程", Command = "ps -eo pid,comm,pcpu,pmem --sort=-pcpu | head -n 15", Group = "进程" },
        new() { Name = "监听端口", Command = "ss -lntp", Group = "网络" },
        new() { Name = "Docker 容器", Command = "docker ps --format 'table {{.Names}}\\t{{.Status}}\\t{{.Ports}}'", Group = "容器" },
        new() { Name = "最近系统日志", Command = "journalctl -xe --no-pager 2>/dev/null | tail -n 100", Group = "日志" },
        new()
        {
            Name = "彩色 ls / zsh 高亮",
            Group = "终端美化",
            Command = """
export CLICOLOR=1
export LS_COLORS='rs=0:di=01;34:ln=01;36:mh=00:pi=40;33:so=01;35:do=01;35:bd=40;33;01:cd=40;33;01:or=40;31;01:mi=00:su=37;41:sg=30;43:ca=30;41:tw=30;42:ow=34;42:st=37;44:ex=01;32'
alias ls='ls --color=auto'
alias grep='grep --color=auto'
alias ll='ls -alF --color=auto'
if [ -n "$ZSH_VERSION" ]; then
  [ -f /usr/share/zsh-syntax-highlighting/zsh-syntax-highlighting.zsh ] && source /usr/share/zsh-syntax-highlighting/zsh-syntax-highlighting.zsh
  [ -f /usr/share/zsh/plugins/zsh-syntax-highlighting/zsh-syntax-highlighting.zsh ] && source /usr/share/zsh/plugins/zsh-syntax-highlighting/zsh-syntax-highlighting.zsh
  [ -f /usr/share/zsh-autosuggestions/zsh-autosuggestions.zsh ] && source /usr/share/zsh-autosuggestions/zsh-autosuggestions.zsh
fi
echo "彩色输出已启用；zsh 语法高亮需要远端已安装 zsh-syntax-highlighting"
"""
        }
    ];
}
