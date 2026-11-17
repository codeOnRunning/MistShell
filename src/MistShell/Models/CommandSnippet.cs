namespace MistShell.Models;

public sealed class CommandSnippet
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Group { get; set; } = "自定义";
    public bool IsBuiltIn { get; set; }
}
