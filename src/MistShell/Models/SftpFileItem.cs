using Renci.SshNet.Sftp;

namespace MistShell.Models;

public sealed class SftpFileItem
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public bool IsDirectory { get; init; }
    public bool IsSymlink { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string Mode { get; init; } = "";
    public int UserId { get; init; }
    public int GroupId { get; init; }

    public string SizeText => IsDirectory ? "—" : FormatSize(Size);
    public string ModifiedText => Modified == default ? "—" : Modified.ToString("MM-dd HH:mm");
    public string IconGlyph => IsDirectory ? "\uE8B7" : "\uE8A5";

    public static SftpFileItem From(ISftpFile file, string parentPath)
    {
        var fullPath = CombineUnix(parentPath, file.Name);
        return new SftpFileItem
        {
            Name = file.Name,
            FullPath = fullPath,
            IsDirectory = file.IsDirectory,
            IsSymlink = file.IsSymbolicLink,
            Size = file.IsDirectory ? 0 : file.Length,
            Modified = file.LastWriteTime,
            Mode = FormatMode(file),
            UserId = file.UserId,
            GroupId = file.GroupId
        };
    }

    public static string CombineUnix(string parent, string name)
    {
        if (string.IsNullOrEmpty(parent) || parent == "/") return "/" + name.TrimStart('/');
        return parent.TrimEnd('/') + "/" + name.TrimStart('/');
    }

    public static string FormatSize(long bytes)
    {
        if (bytes < 0) return "—";
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {units[unit]}";
    }

    public static string FormatMode(ISftpFile file)
    {
        var a = file.Attributes;
        var prefix = a.IsDirectory ? 'd' : a.IsSymbolicLink ? 'l' : '-';
        Span<char> chars = stackalloc char[10];
        chars[0] = prefix;
        chars[1] = a.OwnerCanRead ? 'r' : '-';
        chars[2] = a.OwnerCanWrite ? 'w' : '-';
        chars[3] = a.OwnerCanExecute ? 'x' : '-';
        chars[4] = a.GroupCanRead ? 'r' : '-';
        chars[5] = a.GroupCanWrite ? 'w' : '-';
        chars[6] = a.GroupCanExecute ? 'x' : '-';
        chars[7] = a.OthersCanRead ? 'r' : '-';
        chars[8] = a.OthersCanWrite ? 'w' : '-';
        chars[9] = a.OthersCanExecute ? 'x' : '-';
        return new string(chars);
    }
}
