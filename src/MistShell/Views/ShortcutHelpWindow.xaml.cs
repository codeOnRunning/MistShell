using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MistShell.Views;

public partial class ShortcutHelpWindow : Window
{
    public ShortcutHelpWindow()
    {
        InitializeComponent();
        Add("Ctrl+C", "有选中内容时复制；没有选中时发送终端中断（^C）");
        Add("Ctrl+V", "粘贴剪贴板内容");
        Add("Ctrl+Insert", "复制终端选中内容");
        Add("Shift+Insert", "粘贴");
        Add("Ctrl+Shift+C", "复制终端选中内容（备用）");
        Add("Ctrl+Shift+V", "粘贴（备用）");
        Add("Ctrl+Shift+A", "全选终端内容");
        Add("Ctrl+Shift+L", "清屏");
        Add("Ctrl+鼠标滚轮", "调整终端字号");
        Add("Enter", "快捷命令栏发送；连接列表聚焦时连接");
        Add("↑ / ↓", "快捷命令历史");
        Add("鼠标右键", "打开复制、粘贴、全选、清屏菜单");
        Add("双击文件 / 目录", "下载文件 / 进入目录");
    }

    private void Add(string key, string description)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var keyText = new TextBlock
        {
            Text = key,
            FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"),
            FontSize = 11.5,
            Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(keyText);

        var separator = new TextBlock
        {
            Text = "—",
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("TextMutedBrush"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(separator, 2);
        row.Children.Add(separator);

        var descriptionText = new TextBlock
        {
            Text = description,
            FontSize = 11,
            Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(descriptionText, 3);
        row.Children.Add(descriptionText);

        Rows.Children.Add(row);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
