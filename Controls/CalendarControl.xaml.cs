using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MiniCalendar.Models;

namespace MiniCalendar.Controls;

public enum HolidayType
{
    None,
    Holiday, // 休
    Workday  // 班
}

public struct HolidayInfo
{
    public HolidayType Type;
    public string Description;
}

public partial class CalendarControl : UserControl
{
    private DateTime _currentDate;
    private Dictionary<string, List<CalendarEvent>> _events = new();
    private Dictionary<DateTime, HolidayInfo> _holidayData = new();

    public event EventHandler<DateTime>? DateSelected;
    public event EventHandler<CalendarEvent>? EventClicked;

    public static readonly DependencyProperty HoverBackgroundProperty =
        DependencyProperty.Register("HoverBackground", typeof(Brush), typeof(CalendarControl), new PropertyMetadata(new SolidColorBrush(Color.FromArgb(51, 255, 255, 255)))); // 默认 #33FFFFFF

    public Brush HoverBackground
    {
        get { return (Brush)GetValue(HoverBackgroundProperty); }
        set { SetValue(HoverBackgroundProperty, value); }
    }

    public void SetHolidayData(Dictionary<DateTime, HolidayInfo> holidayData)
    {
        _holidayData = holidayData;
        UpdateCalendar();
    }

    public CalendarControl()
    {
        InitializeComponent();
        _currentDate = DateTime.Today;
        
        PreviousMonthButton.Click += (s, e) => ChangeMonth(-1);
        NextMonthButton.Click += (s, e) => ChangeMonth(1);
        
        // 点击年月回到今天
        MonthYearText.Cursor = Cursors.Hand;
        MonthYearText.MouseLeftButtonDown += (s, e) => 
        {
            _currentDate = DateTime.Today;
            UpdateCalendar();
        };
        
        UpdateCalendar();
    }

    private DateTime _lastWheelTime = DateTime.MinValue;
    private const int WheelThrottleMilliseconds = 200; // 200ms 内只响应一次滚轮

    private void Border_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((DateTime.Now - _lastWheelTime).TotalMilliseconds < WheelThrottleMilliseconds)
        {
            return;
        }

        if (e.Delta > 0)
        {
            ChangeMonth(-1); // 向上滚，上个月
            _lastWheelTime = DateTime.Now;
        }
        else if (e.Delta < 0)
        {
            ChangeMonth(1); // 向下滚，下个月
            _lastWheelTime = DateTime.Now;
        }
    }

    public void SetThemeColors(string background, string foreground, string borderColor)
    {
        try
        {
            var brushConverter = new BrushConverter();
            var bgBrush = (Brush)brushConverter.ConvertFromString(background);
            var fgBrush = (Brush)brushConverter.ConvertFromString(foreground);
            var borderBrush = (Brush)brushConverter.ConvertFromString(borderColor);

            MainBorder.Background = bgBrush;
            MainBorder.BorderBrush = borderBrush;
            MonthNavGrid.Background = Brushes.Transparent; // 去掉导航栏背景色
            MonthYearText.Foreground = fgBrush;
            PreviousMonthButton.Foreground = fgBrush; // 箭头颜色跟随字体颜色
            NextMonthButton.Foreground = fgBrush; // 箭头颜色跟随字体颜色
            
            // 设置悬停背景色 (20% 的前景色，或者如果是浅色模式用黑色20%，深色模式用白色20%)
            // 简单起见，如果背景是深色（MainBorder.Background），悬停用白色半透明；如果背景是浅色，悬停用黑色半透明。
            // 这里我们根据 foreground 来判断：如果前景是黑色，悬停用 #33000000；如果前景是白色，悬停用 #33FFFFFF。
            
            if (fgBrush is SolidColorBrush solidFg && (solidFg.Color.R + solidFg.Color.G + solidFg.Color.B) / 3 < 128)
            {
                 // 黑色字体 -> 浅色主题 -> 悬停用黑色 20%
                 HoverBackground = new SolidColorBrush(Color.FromArgb(51, 0, 0, 0));
            }
            else
            {
                 // 白色字体 -> 深色主题 -> 悬停用白色 20%
                 HoverBackground = new SolidColorBrush(Color.FromArgb(51, 255, 255, 255));
            }
            
            // 刷新日历以更新日期文字颜色
            UpdateCalendar();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"设置主题失败: {ex.Message}");
        }
    }

    public void SetEvents(Dictionary<string, List<CalendarEvent>> events)
    {
        _events = events;
        UpdateCalendar();
    }

    private void ChangeMonth(int months)
    {
        _currentDate = _currentDate.AddMonths(months);
        UpdateCalendar();
    }

    private void UpdateCalendar()
    {
        MonthYearText.Text = _currentDate.ToString("yyyy年M月");
        CalendarGrid.Children.Clear();
        CalendarGrid.RowDefinitions.Clear();
        CalendarGrid.ColumnDefinitions.Clear();

        // 创建7列
        for (int i = 0; i < 7; i++)
        {
            CalendarGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var firstDayOfMonth = new DateTime(_currentDate.Year, _currentDate.Month, 1);
        var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
        var firstDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
        // 将周日(0)转换为7，使得周一为1
        if (firstDayOfWeek == 0) firstDayOfWeek = 7;

        var days = new List<(DateTime Date, bool IsOtherMonth)>();

        // 添加上个月的日期
        var previousMonth = firstDayOfMonth.AddMonths(-1);
        var daysInPreviousMonth = DateTime.DaysInMonth(previousMonth.Year, previousMonth.Month);
        for (int i = firstDayOfWeek - 1; i > 0; i--)
        {
            var day = daysInPreviousMonth - i + 1;
            var date = new DateTime(previousMonth.Year, previousMonth.Month, day);
            days.Add((date, true));
        }

        // 添加当前月的日期
        for (int day = 1; day <= lastDayOfMonth.Day; day++)
        {
            var date = new DateTime(_currentDate.Year, _currentDate.Month, day);
            days.Add((date, false));
        }

        // 添加下个月的日期，补齐到整行
        // 计算当前总天数
        int currentCount = days.Count;
        // 计算需要的行数
        int rowCount = (int)Math.Ceiling(currentCount / 7.0);
        // 如果最后一行没满，补齐
        int remainingInLastRow = (rowCount * 7) - currentCount;
        
        // 至少显示6行，或者根据内容自适应？
        // 用户要求“没有事件的行应该矮一点”，这意味着行高是 Auto。
        // 为了视觉稳定，通常日历是 6 行。
        // 如果我们用 Grid，并将 RowDefinition.Height 设置为 Auto，那么没有事件的行就会变矮（只显示日期数字的高度）。
        // 有事件的行会被撑开。
        
        // 补齐到 42 天 (6行) 还是只补齐到最后一行？
        // 通常日历显示 6 行以涵盖所有情况。
        int targetTotalDays = 42; 
        
        var nextMonth = firstDayOfMonth.AddMonths(1);
        for (int i = 1; days.Count < targetTotalDays; i++)
        {
            var date = new DateTime(nextMonth.Year, nextMonth.Month, i);
            days.Add((date, true));
        }

        // 创建行定义
        rowCount = (int)Math.Ceiling(days.Count / 7.0);
        for (int i = 0; i < rowCount; i++)
        {
            CalendarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 50 }); // 设置最小高度
        }

        // 填充网格
        for (int i = 0; i < days.Count; i++)
        {
            var (date, isOtherMonth) = days[i];
            int row = i / 7;
            int col = i % 7;
            
            AddDayButton(date, isOtherMonth, row, col);
        }
    }

    private void AddDayButton(DateTime date, bool isOtherMonth, int row, int col)
    {
        var button = new Button
        {
            Style = (Style)FindResource("CalendarDayButtonStyle"),
            Tag = date,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Stretch // 确保填满单元格
        };

        // 使用 Grid 作为容器，以便支持右上角的角标
        var grid = new Grid();

        var stackPanel = new StackPanel();
        
        var dayText = new TextBlock
        {
            Text = date.Day.ToString(),
            HorizontalAlignment = HorizontalAlignment.Center,
            FontWeight = date.Date == DateTime.Today ? FontWeights.Bold : FontWeights.Normal,
            Foreground = isOtherMonth ? new SolidColorBrush(Color.FromRgb(150, 150, 150)) : (date.Date == DateTime.Today ? Brushes.White : MonthYearText.Foreground),
            Background = date.Date == DateTime.Today ? new SolidColorBrush(Colors.LightBlue) { Opacity = 0.8 } : Brushes.Transparent,
            Padding = new Thickness(6, 2, 6, 2)
        };
        
        // 圆角背景
        if (date.Date == DateTime.Today)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0, 120, 212)),
                // CornerRadius = new CornerRadius(10), // 移除圆角
                Child = new TextBlock
                {
                    Text = date.Day.ToString(),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold, // 当前日期改为粗体
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(6, 1, 6, 1), // 调整 Padding 保持高度一致
                    Height = 18 // 固定高度以对齐
                },
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 2)
            };
            stackPanel.Children.Add(border);
        }
        else
        {
            dayText.Height = 18; // 固定高度以对齐
            dayText.Margin = new Thickness(0, 2, 0, 2);
            stackPanel.Children.Add(dayText);
        }

        // 添加事件列表显示
        var dayEvents = GetEventsForDate(date);
        
        if (dayEvents.Any())
        {
            var eventsStackPanel = new StackPanel
            {
                Margin = new Thickness(2, 2, 2, 2),
            };

            foreach (var calendarEvent in dayEvents.Take(3)) // 最多显示3个事件
            {
                // 使用 Emoji.Wpf 显示彩色 Emoji
                var eventText = new Emoji.Wpf.TextBlock
                {
                    Text = calendarEvent.Title,
                    FontSize = 10,
                    Foreground = Brushes.White,
                    Padding = new Thickness(2, 1, 2, 1),
                    Margin = new Thickness(0, 1, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    // ToolTip = $"{calendarEvent.StartTime:HH:mm} - {calendarEvent.Title}\n{calendarEvent.Description}", // ToolTip 移到 Border 上
                    VerticalAlignment = VerticalAlignment.Center
                };

                // 包装在一个 Border 里以提供背景色
                var eventBorder = new Border
                {
                    Background = (Brush)new BrushConverter().ConvertFromString(calendarEvent.Color) ?? Brushes.Blue,
                    Child = eventText,
                    Margin = new Thickness(0, 2, 0, 0) // 增加垂直间距，原为0或1
                };
                
                if (calendarEvent.IsAllDay)
                {
                    eventBorder.ToolTip = $"全天 - {calendarEvent.Title}\n{calendarEvent.Description}";
                }
                else
                {
                    eventBorder.ToolTip = $"{calendarEvent.StartTime:HH:mm} - {calendarEvent.Title}\n{calendarEvent.Description}";
                }

                eventsStackPanel.Children.Add(eventBorder);
            }

            stackPanel.Children.Add(eventsStackPanel);
        }

        grid.Children.Add(stackPanel);

        // 添加节假日/补班标记
        if (_holidayData.TryGetValue(date.Date, out var holidayInfo) && holidayInfo.Type != HolidayType.None)
        {
            var indicator = new TextBlock 
            { 
                Text = holidayInfo.Type == HolidayType.Holiday ? "休" : "班",
                Foreground = holidayInfo.Type == HolidayType.Holiday ? new SolidColorBrush(Color.FromRgb(46, 204, 113)) : new SolidColorBrush(Color.FromRgb(231, 76, 60)), // 绿色 / 红色
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 1, 1, 0),
                ToolTip = holidayInfo.Description // 添加 ToolTip
            };
            grid.Children.Add(indicator);
        }

        button.Content = grid;
        button.Click += DayButton_Click;
        button.MouseRightButtonUp += DayButton_RightClick;

        Grid.SetRow(button, row);
        Grid.SetColumn(button, col);
        CalendarGrid.Children.Add(button);
    }

    // private class TextPart ... (removed)
    // private List<TextPart> SplitEmoji ... (removed)
    // private bool IsStringEmoji ... (removed)

    private List<CalendarEvent> GetEventsForDate(DateTime date)
    {
        var events = new List<CalendarEvent>();
        
        foreach (var sourceEvents in _events.Values)
        {
            events.AddRange(sourceEvents.Where(e => 
            {
                // 如果是全天事件，直接判断日期
                if (e.IsAllDay)
                {
                     // 全天事件，如果结束时间大于开始时间，说明结束时间是第二天的0点
                     // 例如 2026-02-11 00:00:00 到 2026-02-12 00:00:00，这应该是只有11号一天
                     // 此时应该把结束时间减去1秒，变成 2026-02-11 23:59:59
                     // 注意：这里我们只修改判断逻辑中的 effectiveEndTime，不修改原始事件
                     
                     var effectiveEndTime = e.EndTime;
                     
                     if (effectiveEndTime > e.StartTime)
                     {
                          effectiveEndTime = effectiveEndTime.AddSeconds(-1);
                     }
                     
                     return e.StartTime.Date <= date.Date && effectiveEndTime.Date >= date.Date;
                }
                
                // 普通跨天事件，结束时间是第二天的 00:00:00，这通常意味着“直到第二天开始前”，所以需要减去一点时间
                var normalEffectiveEndTime = e.EndTime;
                if (normalEffectiveEndTime.Date > e.StartTime.Date && normalEffectiveEndTime.TimeOfDay == TimeSpan.Zero)
                {
                     normalEffectiveEndTime = normalEffectiveEndTime.AddSeconds(-1);
                }
                
                return e.StartTime.Date <= date.Date && normalEffectiveEndTime.Date >= date.Date;
            }));
        }
        return events.OrderBy(e => e.StartTime).ToList();
    }

    private void DayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is DateTime date)
        {
            DateSelected?.Invoke(this, date);
        }
    }

    private void DayButton_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && button.Tag is DateTime date)
        {
            var events = GetEventsForDate(date);
            if (events.Any())
            {
                var contextMenu = new ContextMenu();
                foreach (var calendarEvent in events)
                {
                    var menuItem = new MenuItem
                    {
                        Header = calendarEvent.IsAllDay ? $"全天 - {calendarEvent.Title}" : $"{calendarEvent.StartTime:HH:mm} - {calendarEvent.Title}",
                        Tag = calendarEvent
                        // Background = (Brush)new BrushConverter().ConvertFromString(calendarEvent.Color) ?? Brushes.Blue // 移除自定义背景色
                    };
                    menuItem.Click += (s, args) =>
                    {
                        if (s is MenuItem item && item.Tag is CalendarEvent calEvent)
                        {
                            EventClicked?.Invoke(this, calEvent);
                        }
                    };
                    contextMenu.Items.Add(menuItem);
                }
                contextMenu.IsOpen = true;
            }
        }
    }
}