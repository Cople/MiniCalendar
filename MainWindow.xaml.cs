using System.Windows;
using System.Windows.Input;
using Hardcodet.Wpf.TaskbarNotification;
using MiniCalendar.Controls;
using MiniCalendar.Services;

namespace MiniCalendar;

public partial class MainWindow : Window
{
    private readonly SettingsService _settingsService;
    private readonly IcsService _icsService;
    private TaskbarIcon? _trayIcon;
    private Dictionary<string, System.Timers.Timer> _sourceTimers;
    private Models.IcsSource _holidaySource;

    // 添加一个字段来记录最后一次失去焦点的时间
    private DateTime _lastDeactivatedTime = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        
        // 针对 Windows 11 (Build 22000+) 启用原生圆角
        // 这需要关闭 AllowsTransparency，并让 DWM 处理圆角
        if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000)
        {
            AllowsTransparency = false;
            MainBorder.CornerRadius = new CornerRadius(0);
            
            // 在代码中动态添加 WindowChrome，仅针对 Win11
            var chrome = new System.Windows.Shell.WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0),
                CaptionHeight = 0
            };
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
        }
        else
        {
            // Windows 10 或更低版本：使用默认的 AllowsTransparency="True" 和 Border CornerRadius
            // 无需做任何事，XAML 中已经默认配置好了
        }
        
        _settingsService = new SettingsService();
        _icsService = new IcsService();
        _sourceTimers = new Dictionary<string, System.Timers.Timer>();

        InitializeHolidaySource();

        InitializeTrayIcon();
        InitializeCalendar();
        
        // 监听系统主题变化
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        // 加载设置并应用主题
        var settings = _settingsService.GetSettings();
        ApplyTheme(settings.Theme);

        // 如果启用了开机启动，确保注册表路径正确
        if (settings.AutoStart)
        {
            _settingsService.SetAutoStart(true);
        }

        SetupRefreshTimers();
        
        // 启动时加载日历
        _ = LoadCalendarEventsAsync("Startup");
        if (settings.ShowChinaHoliday)
        {
            _ = LoadHolidayDataAsync("Startup");
        }
    }

    private void InitializeHolidaySource()
    {
        _holidaySource = new Models.IcsSource
        {
            Id = "System_Holiday_CN",
            Name = "China Holidays",
            Url = "https://cdn.jsdelivr.net/gh/lanceliao/china-holiday-calender/holidayCal.ics",
            RefreshIntervalMinutes = 1440, // 1 day
            IsEnabled = true
        };
        
        // 尝试从缓存文件获取上次更新时间，以便 IcsService 能够正确判断是否需要跳过网络请求
        var lastUpdate = _icsService.GetLastCacheUpdateTime(_holidaySource.Url);
        if (lastUpdate.HasValue)
        {
            _holidaySource.LastUpdated = lastUpdate.Value;
        }
    }

    public void RefreshHolidaySource(bool isEnabled)
    {
        _holidaySource.IsEnabled = isEnabled;

        if (isEnabled)
        {
            // 启用：立即加载数据（网络/缓存）
            _ = LoadHolidayDataAsync("SettingsChanged");
        }
        else
        {
            // 禁用：清除定时器，清除界面数据
            if (_sourceTimers.ContainsKey(_holidaySource.Id))
            {
                _sourceTimers[_holidaySource.Id].Stop();
                _sourceTimers[_holidaySource.Id].Dispose();
                _sourceTimers.Remove(_holidaySource.Id);
            }
            
            // 清空 UI 上的节假日标记
            Dispatcher.Invoke(() => CalendarControl.SetHolidayData(new Dictionary<DateTime, Controls.HolidayInfo>()));
        }
    }

    private async Task LoadHolidayDataAsync(string triggerType)
    {
        try
        {
            var events = await _icsService.LoadEventsFromIcsAsync(_holidaySource, triggerType);
            
            // 如果成功加载（无论是网络还是缓存），更新 LastUpdated
            // 注意：LoadEventsFromIcsAsync 内部已经更新了 LastUpdated，但如果是读缓存，它也可能没更新？
            // 看代码：LoadEventsFromIcsAsync 只有在网络请求成功时更新 LastUpdated。读缓存不更新。
            // 但我们在 InitializeHolidaySource 里已经设置了 LastUpdated。
            
            // 如果是从网络加载成功，我们需要更新缓存时间（虽然没地方持久化 _holidaySource，但内存里更新了也好）
            // 实际上，只要文件被更新了，下次启动 InitializeHolidaySource 就会读到新的时间。
            
            // 设置定时器
            SetupHolidayTimer();
            
            // 处理数据
            var holidayData = new Dictionary<DateTime, Controls.HolidayInfo>();
            
            foreach (var evt in events)
            {
                Controls.HolidayType type = Controls.HolidayType.None;
                if (evt.Title.Contains("假期")) type = Controls.HolidayType.Holiday;
                else if (evt.Title.Contains("补班")) type = Controls.HolidayType.Workday;
                
                if (type != Controls.HolidayType.None)
                {
                    var current = evt.StartTime.Date;
                    var end = evt.EndTime.Date;
                    
                    if (evt.IsAllDay)
                    {
                         if (evt.EndTime > evt.StartTime)
                         {
                             end = evt.EndTime.AddSeconds(-1).Date;
                         }
                    }
                    else
                    {
                        if (evt.EndTime.Date > evt.StartTime.Date && evt.EndTime.TimeOfDay == TimeSpan.Zero)
                        {
                            end = evt.EndTime.AddSeconds(-1).Date;
                        }
                    }

                    // 构建 ToolTip 描述
                    // 格式示例：春节 假期 第1天/共9天\n放假通知: ...
                    // 我们可能只想要前面的部分，或者全部。
                    // 简单处理：使用 Description 字段，或者 Title + Description
                    var description = $"{evt.Title}\n{evt.Description}";

                    while (current <= end)
                    {
                        holidayData[current] = new Controls.HolidayInfo 
                        { 
                            Type = type, 
                            Description = description 
                        };
                        current = current.AddDays(1);
                    }
                }
            }
            
            await Dispatcher.BeginInvoke(() => CalendarControl.SetHolidayData(holidayData));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载节假日数据失败: {ex.Message}");
        }
    }

    private void SetupHolidayTimer()
    {
        // 类似于 SetupSourceTimer，但专门针对 _holidaySource
        // 我们可以复用 _sourceTimers，用 _holidaySource.Id 作为 key
        
        if (_sourceTimers.ContainsKey(_holidaySource.Id))
        {
            _sourceTimers[_holidaySource.Id].Stop();
            _sourceTimers[_holidaySource.Id].Dispose();
            _sourceTimers.Remove(_holidaySource.Id);
        }

        double initialDelay = 0;
        var nextRefreshTime = _holidaySource.LastUpdated.AddMinutes(_holidaySource.RefreshIntervalMinutes);
        var now = DateTime.Now;
        
        if (nextRefreshTime > now)
        {
            initialDelay = (nextRefreshTime - now).TotalMilliseconds;
        }
        else
        {
            // 如果下次刷新时间在过去（说明刚刚刷新失败，或者错过了），
            // 设置一个较长的重试间隔（例如1小时），避免在失败时频繁重试导致死循环
            initialDelay = 3600 * 1000; // 1 hour
        }
        
        var timer = new System.Timers.Timer(initialDelay);
        timer.AutoReset = false;
        timer.Elapsed += async (s, e) => 
        {
            await LoadHolidayDataAsync("Timer");
        };
        timer.Start();
        _sourceTimers[_holidaySource.Id] = timer;
    }


    private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category == Microsoft.Win32.UserPreferenceCategory.General)
        {
            var settings = _settingsService.GetSettings();
            Dispatcher.Invoke(() => ApplyTheme(settings.Theme));
        }
    }

    private void InitializeTrayIcon()
    {
        // 创建WPF托盘图标
        _trayIcon = new TaskbarIcon
        {
            IconSource = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/AppIconLight.ico")),
            ToolTipText = "Mini Calendar",
            ContextMenu = new System.Windows.Controls.ContextMenu()
        };

        var menuItem1 = new System.Windows.Controls.MenuItem { Header = "显示日历" };
        menuItem1.Click += (s, e) => ShowCalendar();
        var menuItem2 = new System.Windows.Controls.MenuItem { Header = "设置" };
        menuItem2.Click += (s, e) => ShowSettings();
        var menuItem3 = new System.Windows.Controls.MenuItem { Header = "退出" };
        menuItem3.Click += (s, e) => CloseApplication();

        _trayIcon.ContextMenu.Items.Add(menuItem1);
        _trayIcon.ContextMenu.Items.Add(menuItem2);
        _trayIcon.ContextMenu.Items.Add(new System.Windows.Controls.Separator());
        _trayIcon.ContextMenu.Items.Add(menuItem3);

        _trayIcon.TrayMouseDoubleClick += (s, e) => 
        {
            // 双击逻辑：如果隐藏则显示。
            // 避免双击触发两次单击导致闪烁。
            // 但 TrayLeftMouseUp 也会触发。
            // 通常最好不要混用单击和双击，或者双击只做“强制显示”
            if (Visibility != Visibility.Visible)
            {
                ShowCalendar();
            }
        };
        // 单击事件逻辑修改：如果窗口可见则隐藏，否则显示
        _trayIcon.TrayLeftMouseUp += (s, e) => 
        {
            if (Visibility == Visibility.Visible)
            {
                Hide();
            }
            else
            {
                // 如果刚刚因为失去焦点而隐藏（比如点击了托盘图标），则不要立即重新显示
                // 增加阈值到 500ms
                if ((DateTime.Now - _lastDeactivatedTime).TotalMilliseconds > 500)
                {
                    ShowCalendar();
                }
            }
        };
    }

    private void InitializeCalendar()
    {
        CalendarControl.DateSelected += CalendarControl_DateSelected;
        CalendarControl.EventClicked += CalendarControl_EventClicked;
        
        // 设置窗口位置在托盘图标上方
        Loaded += (s, e) => PositionWindowNearTray();
    }

    public void SetTheme(string theme)
    {
        ApplyTheme(theme);
    }

    private void ApplyTheme(string theme)
    {
        string targetTheme = theme;
        if (theme == "Auto")
        {
            targetTheme = GetSystemTheme();
        }

        // 检查系统透明度设置
        bool isTransparencyEnabled = GetSystemTransparencyEnabled();
        string opacityPrefix = isTransparencyEnabled ? "#CC" : "#FF"; // 80% opacity if enabled

        if (isTransparencyEnabled)
        {
            EnableBlur();
        }
        else
        {
            DisableBlur();
        }

        if (targetTheme == "Light")
        {
            // 浅色主题：浅灰色背景，黑色字体
            CalendarControl.SetThemeColors(
                background: $"{opacityPrefix}F5F5F5", // 浅灰色背景
                foreground: "#000000", // 黑色字体
                borderColor: "#CCCCCC" // 边框颜色
            );
        }
        else
        {
            // 深色主题（默认）
            CalendarControl.SetThemeColors(
                background: $"{opacityPrefix}202020",
                foreground: "#FFFFFF",
                borderColor: "#19ffffff"
            );
        }

        // 根据系统任务栏主题设置托盘图标颜色
        // SystemUsesLightTheme = 1 表示任务栏是浅色，应该用黑色图标
        // SystemUsesLightTheme = 0 表示任务栏是深色，应该用白色图标
        try
        {
            var taskbarTheme = GetSystemTaskbarTheme();
            string iconName = (taskbarTheme == "Light") ? "AppIcon.ico" : "AppIconLight.ico";
            
            if (_trayIcon != null)
            {
                var uri = new Uri($"pack://application:,,,/{iconName}", UriKind.Absolute);
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = uri;
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                _trayIcon.IconSource = bitmap;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置托盘图标失败: {ex.Message}");
        }
    }

    private bool GetSystemTransparencyEnabled()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    var val = key.GetValue("EnableTransparency");
                    if (val is int i && i == 1)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
        return false; // Default to false
    }

    private string GetSystemTaskbarTheme()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    var val = key.GetValue("SystemUsesLightTheme");
                    if (val is int i && i == 1)
                    {
                        return "Light";
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
        return "Dark"; // Default to Dark
    }

    private string GetSystemTheme()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    // 改为使用 SystemUsesLightTheme 来判断系统主题，与任务栏保持一致
                    var val = key.GetValue("SystemUsesLightTheme");
                    if (val is int i && i == 1)
                    {
                        return "Light";
                    }
                }
            }
        }
        catch
        {
            // ignore
        }
        return "Dark"; // Default to Dark
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, PreserveSig = false)]
    internal static extern void DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);

    public enum DWMWINDOWATTRIBUTE
    {
        DWMWA_WINDOW_CORNER_PREFERENCE = 33
    }

    public enum DWM_WINDOW_CORNER_PREFERENCE
    {
        DWMWCP_DEFAULT = 0,
        DWMWCP_DONOTROUND = 1,
        DWMWCP_ROUND = 2,
        DWMWCP_ROUNDSMALL = 3
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        
        // 针对 Windows 11 (Build 22000+) 启用原生圆角
        if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000)
        {
            EnableRoundedCorners();
        }
        else
        {
            // Windows 10: 确保模糊效果被正确应用
            // 因为在构造函数中调用 ApplyTheme -> EnableBlur 时，Handle 可能还没准备好
            var settings = _settingsService.GetSettings();
            ApplyTheme(settings.Theme);
        }
    }

    private void EnableRoundedCorners()
    {
        var windowHelper = new System.Windows.Interop.WindowInteropHelper(this);
        var preference = (int)DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_ROUND;
        try 
        {
            DwmSetWindowAttribute(windowHelper.Handle, (int)DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
        }
        catch 
        {
            // Windows 10 or older might not support this attribute or fail
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    internal enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    internal enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
        ACCENT_INVALID_STATE = 5
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    private void EnableBlur()
    {
        var windowHelper = new System.Windows.Interop.WindowInteropHelper(this);
        
        // Windows 11 Acrylic (Requires different policy or native window chrome usually handles it)
        // But for Windows 10, we use ACCENT_ENABLE_BLURBEHIND or ACCENT_ENABLE_ACRYLICBLURBEHIND
        // If Win10 build 1709+, we can use Acrylic.
        
        var accent = new AccentPolicy();
        var accentStructSize = System.Runtime.InteropServices.Marshal.SizeOf(accent);
        
        // 区分 Win10 和 Win11 的处理
        if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000)
        {
             // Win11: 使用 Acrylic，背景色设为全透明，由 DWM 处理模糊
             accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
             accent.GradientColor = 0x01FFFFFF; // 稍微不完全透明，有时能解决全黑问题
             // 或者 0x00FFFFFF
             accent.GradientColor = 0x00FFFFFF;
        }
        else
        {
            // Win10: 使用 BlurBehind 或 Acrylic
            // 尝试使用 ACCENT_ENABLE_BLURBEHIND 兼容性更好，或者 Acrylic
            // 如果之前用的是 Acrylic，这里保持一致
            accent.AccentState = AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
            // 关键：Windows 10 下 Acrylic 需要一个特定的 GradientColor 才能生效
            // 格式 AABBGGRR (hex)
            // 0xCC000000 (AA=CC, BB=00, GG=00, RR=00) -> 半透明黑色
            // 但是 WPF Window Background 已经控制了颜色。
            // 尝试设置 GradientColor 为 0 (全透明)
            accent.GradientColor = 0x00FFFFFF; 
        }

        var accentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(accentStructSize);
        System.Runtime.InteropServices.Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);

        System.Runtime.InteropServices.Marshal.FreeHGlobal(accentPtr);
    }

    private void DisableBlur()
    {
        var windowHelper = new System.Windows.Interop.WindowInteropHelper(this);
        var accent = new AccentPolicy
        {
            AccentState = AccentState.ACCENT_DISABLED
        };

        var accentStructSize = System.Runtime.InteropServices.Marshal.SizeOf(accent);
        var accentPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(accentStructSize);
        System.Runtime.InteropServices.Marshal.StructureToPtr(accent, accentPtr, false);

        var data = new WindowCompositionAttributeData
        {
            Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
            SizeOfData = accentStructSize,
            Data = accentPtr
        };

        SetWindowCompositionAttribute(windowHelper.Handle, ref data);

        System.Runtime.InteropServices.Marshal.FreeHGlobal(accentPtr);
    }

    private void PositionWindowNearTray()
    {
        // 获取工作区信息 (不包含任务栏的屏幕区域)
        var workingArea = SystemParameters.WorkArea;
        
        // 强制确保窗口大小
        if (Width < 100) Width = 360;
        
        // 默认高度
        if (Height < 100) Height = 450;
        
        // 尝试获取托盘图标位置
        var trayRect = Services.TrayIconHelper.GetTrayIconRect("Mini Calendar");
        
        if (trayRect != Rect.Empty)
        {
            // 1. 水平居中对齐
            double iconCenterX = trayRect.Left + trayRect.Width / 2;
            Left = iconCenterX - (Width / 2);
            
            // 2. 如果超出屏幕右侧，则靠右对齐
            if (Left + Width > workingArea.Right)
            {
                Left = workingArea.Right - Width;
            }
            
            // 3. 如果超出屏幕左侧（防止图标在最左边时窗口出界），靠左对齐
            if (Left < workingArea.Left)
            {
                Left = workingArea.Left;
            }
        }
        else
        {
            // 如果找不到图标，默认靠右对齐
            Left = workingArea.Right - Width;
        }

        // 垂直位置：默认在工作区底部（假设任务栏在底部）
        // 如果需要更智能的垂直对齐（支持顶部/侧边任务栏），需要判断任务栏位置
        // 暂时保持原有的底部对齐逻辑
        Top = workingArea.Bottom - Height;
    }

    private void SetupRefreshTimers()
    {
        var settings = _settingsService.GetSettings();
        
        // 1. 停止并清除所有现有计时器
        foreach (var timer in _sourceTimers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }
        _sourceTimers.Clear();

        // 2. 为每个启用的源重新设置计时器
        foreach (var source in settings.IcsSources.Where(s => s.IsEnabled))
        {
            SetupSourceTimer(source);
        }
    }

    private void SetupSourceTimer(Models.IcsSource source)
    {
        if (_sourceTimers.ContainsKey(source.Id))
        {
            _sourceTimers[source.Id].Stop();
            _sourceTimers[source.Id].Dispose();
            _sourceTimers.Remove(source.Id);
        }

        // 刷新间隔必须大于0才启动定时器
        if (source.RefreshIntervalMinutes > 0)
        {
            // 计算下一次刷新时间
            // 如果 LastUpdated 是很久以前，initialDelay 可能是负数，设置为立即执行（或很短延迟）
            // 如果 LastUpdated 是刚刚，initialDelay 会是剩余时间
            
            double initialDelay = 0;
            var nextRefreshTime = source.LastUpdated.AddMinutes(source.RefreshIntervalMinutes);
            var now = DateTime.Now;
            
            if (nextRefreshTime > now)
            {
                initialDelay = (nextRefreshTime - now).TotalMilliseconds;
            }
            else
            {
                // 如果已经过期，立即刷新（设置一个很小的延迟，比如1秒，避免阻塞启动）
                initialDelay = 1000; 
            }
            
            // 使用 System.Threading.Timer 或 System.Timers.Timer
            // 这里为了方便，我们先用一个一次性 Timer 来处理首次触发，然后在回调里设置循环 Timer
            // 或者直接用 System.Timers.Timer 的 Interval 属性，先设为 initialDelay，触发后再设为 normalInterval
            
            var timer = new System.Timers.Timer(initialDelay);
            timer.AutoReset = false; // 只触发一次
            timer.Elapsed += async (s, e) => 
            {
                await RefreshSourceAsync(source, "Timer");
                
                // 第一次触发后，重新设置定时器为正常周期
                // 注意：这里需要要在 UI 线程或者确保线程安全地更新 _sourceTimers
                // 简单起见，我们在 RefreshSourceAsync 完成后重新 SetupSourceTimer
                // 但 RefreshSourceAsync 是异步的，而且是在后台线程。
                
                // 更稳妥的方式：
                // 触发后，执行刷新。刷新成功后，LastUpdated 会更新。
                // 然后再次调用 SetupSourceTimer，它会根据新的 LastUpdated 计算下一次时间。
                // 这样就形成了一个基于 LastUpdated 的递归定时循环，非常健壮。
                
                await Dispatcher.BeginInvoke(() => SetupSourceTimer(source));
            };
            timer.Start();
            
            _sourceTimers[source.Id] = timer;
        }
    }

    private async Task RefreshSourceAsync(Models.IcsSource source, string triggerType = "Unknown")
    {
        try
        {
            var events = await _icsService.LoadEventsFromIcsAsync(source, triggerType);
            
            // 刷新成功后，保存 LastUpdated 时间
            // LoadEventsFromIcsAsync 内部已经更新了 source.LastUpdated
            // 我们只需要持久化保存设置
            _settingsService.SaveSettings(_settingsService.GetSettings());
            
            await Dispatcher.BeginInvoke(() =>
            {
                var settings = _settingsService.GetSettings();
                
                // 获取当前日历控件中已有的所有事件
                // CalendarControl.SetEvents 会覆盖所有事件，所以我们需要先获取当前的完整列表，更新其中的一项，然后再设置回去
                // 但 CalendarControl 没有 GetEvents 方法。
                // 我们可以维护一个本地的 _allEvents 缓存，或者 CalendarControl 公开获取方法。
                // 不过，RefreshSourceAsync 可能会并发调用。
                
                // 更好的做法是：
                // 1. 在 MainWindow 维护一个 Dictionary<string, List<CalendarEvent>> _currentEventsCache
                // 2. 每次 RefreshSourceAsync 更新这个 Cache
                // 3. 然后把整个 Cache 传给 CalendarControl
                
                // 由于我们之前每次都是重新构建 allEvents（从 settings.IcsSources），
                // 如果其他源的事件不在 settings.IcsSources 对应的列表里（比如还没加载），那就会丢。
                // 但 settings.IcsSources 只是配置，不包含事件数据。
                // 事件数据目前只存在于 CalendarControl 内部（SetEvents 传进去后）或者临时的局部变量中。
                // 所以问题在于：当刷新单个 source 时，我们丢失了其他 source 的数据。
                
                // 解决方案：在 MainWindow 级别缓存所有加载的事件。
                if (_cachedEvents == null)
                {
                    _cachedEvents = new Dictionary<string, List<Models.CalendarEvent>>();
                }
                
                // 更新当前源的事件
                _cachedEvents[source.Id] = events;
                
                // 移除已禁用的源的数据（可选，保持数据整洁）
                var enabledSourceIds = settings.IcsSources.Where(s => s.IsEnabled).Select(s => s.Id).ToHashSet();
                var keysToRemove = _cachedEvents.Keys.Where(k => !enabledSourceIds.Contains(k)).ToList();
                foreach (var key in keysToRemove)
                {
                    _cachedEvents.Remove(key);
                }
                
                // 创建副本传递给控件，防止引用问题
                var eventsToDisplay = new Dictionary<string, List<Models.CalendarEvent>>(_cachedEvents);
                CalendarControl.SetEvents(eventsToDisplay);
            });
        }
        catch (Exception ex)
        {
            // 静默处理错误，避免频繁弹窗
            System.Diagnostics.Debug.WriteLine($"自动刷新失败: {source.Name} - {ex.Message}");
        }
    }

    // 添加一个字段来缓存事件
    private Dictionary<string, List<Models.CalendarEvent>> _cachedEvents = new();

    private async Task LoadCalendarEventsAsync(string triggerType = "Startup")
    {
        try
        {
            var settings = _settingsService.GetSettings();
            var enabledSources = settings.IcsSources.Where(s => s.IsEnabled).ToList();
            
            if (enabledSources.Any())
            {
                // LoadAllEventsAsync 会返回所有启用源的事件
                var allEvents = await _icsService.LoadAllEventsAsync(enabledSources, triggerType);
                
                // 更新缓存
                _cachedEvents = allEvents;
                
                await Dispatcher.BeginInvoke(() =>
                {
                    CalendarControl.SetEvents(allEvents);
                });
            }
            else
            {
                // 如果没有启用的源，清空
                _cachedEvents.Clear();
                await Dispatcher.BeginInvoke(() =>
                {
                    CalendarControl.SetEvents(new Dictionary<string, List<Models.CalendarEvent>>());
                });
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"加载日历事件失败: {ex.Message}", "错误", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowCalendar()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            // 1. 先确保窗口状态正常
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            
            // 2. 确保窗口大小正确
            if (Width < 100) Width = 360;
            if (Height < 100) Height = 450;

            // 3. 计算并设置位置
            PositionWindowNearTray();

            // 4. 显示窗口
            Show();
            
            // 5. 激活窗口
            Activate();
            Focus();
            
            // 6. 强制前台
            if (PresentationSource.FromVisual(this) is System.Windows.Interop.HwndSource source)
            {
                var handle = source.Handle;
                SetForegroundWindow(handle);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void ShowSettings()
    {
        var settingsWindow = new SettingsWindow(_settingsService, _icsService);
        settingsWindow.ShowDialog();
        
        // 重新设置定时器
        SetupRefreshTimers();
    }

    public void RefreshSingleSource(Models.IcsSource source)
    {
        SetupSourceTimer(source);
        // 只刷新这一个源
        _ = RefreshSourceAsync(source, "EditSource");
    }
    
    public void RemoveSingleSource(string sourceId)
    {
        // 停止定时器
        if (_sourceTimers.ContainsKey(sourceId))
        {
            _sourceTimers[sourceId].Stop();
            _sourceTimers[sourceId].Dispose();
            _sourceTimers.Remove(sourceId);
        }
        
        // 从内存缓存中移除数据并更新 UI，但不删除本地文件缓存
        if (_cachedEvents != null && _cachedEvents.ContainsKey(sourceId))
        {
            _cachedEvents.Remove(sourceId);
            
            Dispatcher.BeginInvoke(() =>
            {
                var eventsToDisplay = new Dictionary<string, List<Models.CalendarEvent>>(_cachedEvents);
                CalendarControl.SetEvents(eventsToDisplay);
            });
        }
    }

    public void ReloadCalendar()
    {
        SetupRefreshTimers();
        _ = LoadCalendarEventsAsync("ManualReload");
    }

    private void CalendarControl_DateSelected(object? sender, DateTime e)
    {
        // 可以在这里添加日期选择后的操作
    }

    private void CalendarControl_EventClicked(object? sender, Models.CalendarEvent e)
    {
        var eventWindow = new EventDetailsWindow(e);
        eventWindow.ShowDialog();
    }

    private void CloseApplication()
    {
        _trayIcon?.Dispose();
        
        foreach (var timer in _sourceTimers.Values)
        {
            timer.Stop();
            timer.Dispose();
        }
        
        System.Windows.Application.Current.Shutdown();
    }

    protected override void OnDeactivated(EventArgs e)
    {
        base.OnDeactivated(e);
        // 点击外部时隐藏窗口
        // 添加一个检查，如果当前鼠标还在托盘图标上，可能是不小心触发的
        // 但这里我们简单处理，只要不是在显示设置窗口等子窗口时，就隐藏
        if (!IsActive)
        {
           Hide();
           _lastDeactivatedTime = DateTime.Now;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        base.OnClosed(e);
        CloseApplication();
    }
}