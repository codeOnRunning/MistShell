namespace MistShell.Models;

public sealed record ThemePalette(
    string Key,
    string DisplayName,
    string WindowBackground,
    string Surface,
    string SurfaceElevated,
    string SurfaceHover,
    string SurfaceActive,
    string SidebarBackground,
    string TitleBar,
    string Border,
    string BorderStrong,
    string TextPrimary,
    string TextSecondary,
    string TextMuted,
    string Accent,
    string AccentHover,
    string AccentSoft,
    string AccentText,
    string Danger,
    string DangerSoft,
    string Warning,
    string Success,
    string TerminalBackground,
    string TerminalForeground,
    string TerminalCursor,
    string TerminalSelection,
    string TerminalBlack,
    string TerminalRed,
    string TerminalGreen,
    string TerminalYellow,
    string TerminalBlue,
    string TerminalMagenta,
    string TerminalCyan,
    string TerminalWhite,
    bool IsDark = false);

public static class ThemePalettes
{
    public static readonly ThemePalette Mist = new(
        "Mist", "雾纸",
        "#E8E5DE", "#F0EDE6", "#F5F2EC", "#E2DED5", "#DCD7CC", "#E3DFD7", "#E1DDD5",
        "#D1CBC0", "#BDB6A9", "#2B2F33", "#62686B", "#8A8F90",
        "#3D7C73", "#326B63", "#D4E3DE", "#FFFFFF", "#B65A5A", "#F2DEDC", "#A9783E", "#3F7D5A",
        "#E9E6DF", "#2D3336", "#2F746B", "#C8D8D2",
        "#3D454A", "#A44F4F", "#3F7355", "#8D6A32", "#3D6388", "#7A527D", "#35747C", "#6E7476");

    public static readonly ThemePalette Sand = new(
        "Sand", "暖砂",
        "#E9E1D1", "#F2EBDD", "#F8F2E6", "#E3DAC8", "#DDD3C0", "#E6DDCC", "#E3DAC9",
        "#D3C7B0", "#C0B39A", "#3A332B", "#6C6256", "#938778",
        "#8A6245", "#755038", "#E8D7C2", "#FFFFFF", "#B65A5A", "#F2DEDC", "#A9783E", "#4F7B57",
        "#EFE7D8", "#3D372F", "#8A6245", "#DCCDB6",
        "#4A4239", "#A44F4F", "#4C7355", "#8D6A32", "#42617F", "#79557A", "#3E7378", "#746C63");

    public static readonly ThemePalette Slate = new(
        "Slate", "深岩",
        "#171A1E", "#1E2227", "#24292F", "#2A3037", "#30373F", "#1B1F23", "#1C2024",
        "#3A424A", "#4F5A64", "#E7E9E6", "#C4CAC9", "#9BA3A6",
        "#75BFB2", "#89CBBE", "#2A4540", "#0E211D", "#D98A8A", "#452D2D", "#D2AE76", "#86C49D",
        "#15181C", "#E5E7E3", "#84CCBE", "#3B544F",
        "#9AA2A5", "#DC8A8A", "#8CCA9F", "#D8B47B", "#8BB9DE", "#C9A1CF", "#82C9D0", "#C5CAC9",
        true);

    public static readonly ThemePalette Sakura = new(
        "Sakura", "樱雾",
        "#F1E6E7", "#F7EDEF", "#FBF4F5", "#EBDCDD", "#E4D2D4", "#EFE4E5", "#EDE1E3",
        "#DCC9CC", "#C8B1B5", "#3A2C30", "#725F64", "#9A858A",
        "#A8586D", "#8F465B", "#EACFD6", "#FFFFFF", "#B85A5A", "#F2DEDC", "#A9783E", "#4F7B57",
        "#F4EAEC", "#342A2D", "#A8586D", "#E1C4CB",
        "#3A3033", "#A94E5A", "#4B7355", "#93682F", "#486A8A", "#85547E", "#3C7378", "#6D6164");

    public static readonly ThemePalette Glacier = new(
        "Glacier", "冰川",
        "#E4EAF0", "#EDF2F7", "#F4F8FB", "#DCE5EC", "#D3DEE8", "#E8EEF4", "#E6EDF3",
        "#CDD8E2", "#B5C4D2", "#26313B", "#5D6D7A", "#8997A3",
        "#3E6F91", "#315C7B", "#D2E2ED", "#FFFFFF", "#B25B5B", "#F1DEDE", "#9A713A", "#3F7658",
        "#E9F0F5", "#233039", "#3E6F91", "#C9DDEA",
        "#2A343D", "#A84F59", "#47725B", "#8F6A32", "#3F668C", "#775A80", "#3A707A", "#68737C");

    public static readonly ThemePalette Sage = new(
        "Sage", "青苔",
        "#E5EBE3", "#EEF3EC", "#F6F9F4", "#DEE8DC", "#D6E2D3", "#E8EFE6", "#E6EDE4",
        "#CFDBCE", "#B8C8B6", "#27302A", "#5D6C60", "#89978B",
        "#4F7C5A", "#3E6548", "#D4E5D8", "#FFFFFF", "#B25B5B", "#F1DEDE", "#98713B", "#3F7854",
        "#EBF1E8", "#26322A", "#4F7C5A", "#C9DEC9",
        "#303A32", "#A65058", "#477A55", "#8E6A32", "#4B6C88", "#7A5B7C", "#3D7476", "#6C786D");

    public static readonly ThemePalette Midnight = new(
        "Midnight", "午夜蓝",
        "#111923", "#17222F", "#1D2A39", "#243445", "#2B3D50", "#131C27", "#141E29",
        "#2C3D4F", "#40566B", "#E5ECF2", "#B6C5D4", "#8497A8",
        "#79B8D9", "#91C9E5", "#1F3A4C", "#0B1820", "#DC8C8C", "#462D34", "#D6B06F", "#83C5A1",
        "#0E1620", "#E4EBF2", "#79B8D9", "#28465C",
        "#A2B0BC", "#DC8C8C", "#83C5A1", "#D6B06F", "#79B8D9", "#C7A0D0", "#82CBDC", "#DCE6EE",
        true);

    public static readonly ThemePalette Ember = new(
        "Ember", "炭火",
        "#1D1714", "#27201B", "#302721", "#3B3028", "#463930", "#201A16", "#211A16",
        "#45372E", "#5D4A3D", "#F0E7DE", "#CDBCAE", "#9F8D7F",
        "#D79862", "#E6AB77", "#4A3428", "#241811", "#DE8E86", "#4B2D2B", "#D9B26F", "#8FC39A",
        "#171210", "#EEE5DC", "#D79862", "#4A382D",
        "#A18F80", "#DE8E86", "#8FC39A", "#D9B26F", "#92B4D4", "#C6A0C7", "#88C4C7", "#E8DED5",
        true);

    public static readonly ThemePalette Amethyst = new(
        "Amethyst", "紫夜",
        "#17141D", "#201B29", "#292235", "#332B41", "#3D344E", "#1A1622", "#1B1723",
        "#3B304A", "#554467", "#EDE7F2", "#C5B8D5", "#9687AA",
        "#B59BDD", "#C7B0EA", "#3C2E50", "#1C1328", "#D98A9C", "#452B35", "#D4B16F", "#8BC6A5",
        "#131019", "#EAE3F1", "#B59BDD", "#3F3152",
        "#A193B1", "#D98A9C", "#8BC6A5", "#D4B16F", "#91B4DC", "#C39CDB", "#87C4D1", "#E4DCEB",
        true);

    public static readonly ThemePalette Carbon = new(
        "Carbon", "石墨",
        "#111315", "#181B1E", "#202428", "#282D32", "#30363C", "#131619", "#14171A",
        "#30363B", "#485057", "#E8EAEB", "#B8BEC2", "#858D93",
        "#8BC7BC", "#A1D8CD", "#263A38", "#0B1A18", "#D98787", "#422B2B", "#D1AD73", "#85C39A",
        "#0E1012", "#E6E8E9", "#8BC7BC", "#2B3D3B",
        "#7C858B", "#D98787", "#85C39A", "#D1AD73", "#8BB7D9", "#C09AC9", "#83C7CC", "#DDE1E3",
        true);

    public static readonly IReadOnlyList<ThemePalette> All =
        [Mist, Sand, Sakura, Glacier, Sage, Slate, Midnight, Ember, Amethyst, Carbon];

    public static ThemePalette Get(string key) => key switch
    {
        "Sand" => Sand,
        "Sakura" => Sakura,
        "Glacier" => Glacier,
        "Sage" => Sage,
        "Slate" => Slate,
        "Midnight" => Midnight,
        "Ember" => Ember,
        "Amethyst" => Amethyst,
        "Carbon" => Carbon,
        _ => Mist
    };
}
