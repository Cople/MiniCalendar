using System.Windows;
using System.Windows.Forms;
using System.Windows.Input; // 添加这行
using MiniCalendar.Models;
using MiniCalendar.Services;

namespace MiniCalendar.Controls;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly IcsService _icsService;
    private AppSettings _settings;

    public SettingsWindow(SettingsService settingsService, IcsService icsService)
    {
        InitializeComponent();
        
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _icsService = icsService ?? throw new ArgumentNullException(nameof(icsService));
        
        LoadSettings();
    }

    private void LoadSettings()
    {
        _settings = _settingsService.GetSettings();
        IcsSourcesGrid.ItemsSource = _settings.IcsSources;
        AutoStartCheckBox.IsChecked = _settings.AutoStart;
        ShowChinaHolidayCheckBox.IsChecked = _settings.ShowChinaHoliday;
        
        var themeIndex = _settings.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0
        };
        ThemeComboBox.SelectedIndex = themeIndex;
    }

    private void IcsSourcesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        bool hasSelection = IcsSourcesGrid.SelectedItem != null;
        EditIcsSourceButton.IsEnabled = hasSelection;
        DeleteIcsSourceButton.IsEnabled = hasSelection;
    }

    private void AddIcsSourceButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new IcsSourceEditWindow();
        if (dialog.ShowDialog() == true)
        {
            _settings.IcsSources.Add(dialog.IcsSource);
            IcsSourcesGrid.Items.Refresh();
            
            // 如果新添加的源是启用的，立即刷新
            if (dialog.IcsSource.IsEnabled)
            {
                if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.Dispatcher.Invoke(() => mainWindow.RefreshSingleSource(dialog.IcsSource));
                }
            }
        }
    }

    private void EditIcsSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (IcsSourcesGrid.SelectedItem is IcsSource selectedSource)
        {
            EditIcsSource(selectedSource);
        }
    }

    private void DeleteIcsSourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (IcsSourcesGrid.SelectedItem is IcsSource selectedSource)
        {
            var result = System.Windows.MessageBox.Show(
                $"确定要删除日历 \"{selectedSource.Name}\" 吗？",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _settings.IcsSources.Remove(selectedSource);
                IcsSourcesGrid.Items.Refresh();
                
                // 强制刷新主界面日历
                if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                {
                    // 使用 Dispatcher 确保在 UI 线程执行
                    mainWindow.Dispatcher.Invoke(() => mainWindow.ReloadCalendar());
                }
            }
        }
    }

    private async void RefreshNowButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshNowButton.IsEnabled = false;
        RefreshNowButton.Content = "刷新中...";

        try
        {
            var enabledSources = _settings.IcsSources.Where(s => s.IsEnabled).ToList();
            await _icsService.LoadAllEventsAsync(enabledSources, "ManualRefreshButton");
            
            System.Windows.MessageBox.Show("日历刷新完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"刷新失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RefreshNowButton.IsEnabled = true;
            RefreshNowButton.Content = "立即刷新";
        }
    }

    private void AutoStartCheckBox_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void ShowChinaHolidayCheckBox_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    private void ThemeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // 避免初始化时触发
        if (!IsLoaded) return;
        SaveSettings();
    }

    private void IcsSourcesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IcsSourcesGrid.SelectedItem is IcsSource selectedSource)
        {
            // 调用编辑逻辑，复用 EditIcsSourceButton_Click 的逻辑
            // 或者直接调用 EditIcsSource 方法
            EditIcsSource(selectedSource);
        }
    }
    
    private void EditIcsSource(IcsSource selectedSource)
    {
        var dialog = new IcsSourceEditWindow(selectedSource);
        if (dialog.ShowDialog() == true)
        {
            var index = _settings.IcsSources.IndexOf(selectedSource);
            _settings.IcsSources[index] = dialog.IcsSource;
            IcsSourcesGrid.Items.Refresh();
            
            // 编辑后立即刷新（因为可能改了URL或颜色或启用状态）
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                // 如果是启用状态，刷新该源
                if (dialog.IcsSource.IsEnabled)
                {
                    mainWindow.Dispatcher.Invoke(() => mainWindow.RefreshSingleSource(dialog.IcsSource));
                }
                else
                {
                    // 如果是禁用状态，刷新 UI 以移除事件（使用 ReloadCalendar 会全量刷新，RefreshSingleSource 也会尝试加载但可能不会清除旧数据？）
                    // 实际上，如果禁用了，我们应该从 UI 中移除该源的数据。
                    // RefreshSingleSource 内部调用 RefreshSourceAsync -> LoadEventsFromIcsAsync。
                    // 但如果禁用了，我们可能不想去联网。
                    // 而且 RefreshSourceAsync 并没有检查 IsEnabled。
                    
                    // 我们需要一个 RemoveSourceData 的方法。
                    // 或者，调用 ReloadCalendar，它会根据 EnabledSources 重新构建 UI。
                    // 但 ReloadCalendar 会刷新所有源。
                    
                    // 让我们在 MainWindow 增加一个 RemoveSingleSourceData 方法。
                    // 或者简单地，如果禁用了，我们调用 RefreshSingleSource，
                    // 但我们需要确保 RefreshSingleSource 在禁用时能正确清除数据。
                    // 目前 RefreshSingleSource -> RefreshSourceAsync -> LoadEventsFromIcsAsync -> SetEvents
                    // LoadEventsFromIcsAsync 会加载数据（不管是否启用，只要传了 source）。
                    
                    // 所以我们需要在 MainWindow 处理：
                    // 如果禁用了，清除缓存并更新 UI。
                    mainWindow.Dispatcher.Invoke(() => mainWindow.RemoveSingleSource(dialog.IcsSource.Id));
                }
            }
        }
    }

    private void IcsSourceEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.CheckBox checkBox && checkBox.DataContext is IcsSource source)
        {
             // DataGridCheckBoxColumn 的 Binding 更新可能滞后于 Click 事件
             // 强制更新源
             var bindingExpression = checkBox.GetBindingExpression(System.Windows.Controls.CheckBox.IsCheckedProperty);
             bindingExpression?.UpdateSource();
             
             _settingsService.SaveSettings(_settings);
             
             // 触发日历刷新
             if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
             {
                 // 如果是启用，立即拉取数据（联网）
                 if (source.IsEnabled)
                 {
                     // 只刷新这一个源
                     mainWindow.Dispatcher.Invoke(() => mainWindow.RefreshSingleSource(source));
                 }
                 else
                 {
                     // 如果是禁用，移除该源的数据
                     mainWindow.Dispatcher.Invoke(() => mainWindow.RemoveSingleSource(source.Id));
                 }
             }
        }
    }

    private void SaveSettings()
    {
        _settings.AutoStart = AutoStartCheckBox.IsChecked ?? false;
        _settings.ShowChinaHoliday = ShowChinaHolidayCheckBox.IsChecked ?? true;
        
        var theme = ThemeComboBox.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "Auto"
        };
        _settings.Theme = theme;

        _settingsService.SaveSettings(_settings);

        // 立即应用主题
        if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.SetTheme(_settings.Theme);
            
            // 触发节假日更新
            mainWindow.Dispatcher.Invoke(() => mainWindow.RefreshHolidaySource(_settings.ShowChinaHoliday));
        }

        if (_settings.AutoStart)
        {
            _settingsService.SetAutoStart(true);
        }
        else
        {
            _settingsService.SetAutoStart(false);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}