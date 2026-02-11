using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Shapes;
using MiniCalendar.Models;

namespace MiniCalendar.Controls;

public partial class IcsSourceEditWindow : Window
{
    public IcsSource IcsSource { get; private set; }
    private readonly bool _isEditing;

    public IcsSourceEditWindow(IcsSource? source = null)
    {
        InitializeComponent();
        
        if (source != null)
        {
            IcsSource = new IcsSource
            {
                Id = source.Id,
                Name = source.Name,
                Url = source.Url,
                RefreshIntervalMinutes = source.RefreshIntervalMinutes,
                Color = source.Color,
                IsEnabled = source.IsEnabled
            };
            _isEditing = true;
            Title = "编辑日历";
        }
        else
        {
            IcsSource = new IcsSource();
            Title = "添加日历";
        }

        LoadSourceData();
    }

    private void LoadSourceData()
    {
        NameTextBox.Text = IcsSource.Name;
        UrlTextBox.Text = IcsSource.Url;
        
        // 设置刷新间隔
        RefreshIntervalComboBox.Items.Clear();
        foreach (var kvp in IcsSource.RefreshIntervals)
        {
            var item = new ComboBoxItem { Content = kvp.Value, Tag = kvp.Key.ToString() };
            RefreshIntervalComboBox.Items.Add(item);
            
            if (kvp.Key == IcsSource.RefreshIntervalMinutes)
            {
                RefreshIntervalComboBox.SelectedItem = item;
            }
        }
        
        // 如果没有选中任何项（可能是自定义值或默认值未匹配），选中默认项（1小时）
        if (RefreshIntervalComboBox.SelectedItem == null)
        {
            foreach (ComboBoxItem item in RefreshIntervalComboBox.Items)
            {
                if (item.Tag is string tag && tag == "60")
                {
                    RefreshIntervalComboBox.SelectedItem = item;
                    break;
                }
            }
        }
        
        IsEnabledCheckBox.IsChecked = IcsSource.IsEnabled;
        
        // 颜色
        UpdateColorPreview();
    }

    private void ChooseColorButton_Click(object sender, RoutedEventArgs e)
    {
        var colorDialog = new ColorDialog();
        if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            var color = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
            IcsSource.Color = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            UpdateColorPreview();
        }
    }

    private void UpdateColorPreview()
    {
        var brush = (Brush)new BrushConverter().ConvertFromString(IcsSource.Color) ?? Brushes.Blue;
        ColorPreview.Fill = brush;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            System.Windows.MessageBox.Show("请输入名称", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(UrlTextBox.Text))
        {
            System.Windows.MessageBox.Show("请输入URL", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IcsSource.Name = NameTextBox.Text;
        IcsSource.Url = UrlTextBox.Text;
        
        if (RefreshIntervalComboBox.SelectedItem is ComboBoxItem item && item.Tag is string tag && int.TryParse(tag, out int minutes))
        {
            IcsSource.RefreshIntervalMinutes = minutes;
        }
        else
        {
             IcsSource.RefreshIntervalMinutes = 60;
        }
        
        if (ColorPreview.Fill is SolidColorBrush brush)
        {
            IcsSource.Color = brush.Color.ToString();
        }
        
        IcsSource.IsEnabled = IsEnabledCheckBox.IsChecked ?? true;

        DialogResult = true;
        Close();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}