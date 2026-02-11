namespace MiniCalendar.Models;

public class IcsSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int RefreshIntervalMinutes { get; set; } = 60;
        
        public static readonly Dictionary<int, string> RefreshIntervals = new()
        {
            { 30, "30分钟" },
            { 60, "1小时" },
            { 360, "6小时" },
            { 720, "12小时" },
            { 1440, "1天" }
        };

        public string RefreshIntervalDisplay
        {
            get
            {
                if (RefreshIntervals.TryGetValue(RefreshIntervalMinutes, out var display))
                {
                    return display;
                }
                return $"{RefreshIntervalMinutes}分钟";
            }
        }

        public string NextRefreshTimeDisplay
        {
            get
            {
                if (RefreshIntervalMinutes <= 0) return "未启用自动刷新";
                var nextTime = LastUpdated.AddMinutes(RefreshIntervalMinutes);
                return $"更新时间: {LastUpdated:yyyy-MM-dd HH:mm:ss}\n下次刷新: {nextTime:yyyy-MM-dd HH:mm:ss}";
            }
        }

        public string Color { get; set; } = "#0078D7"; // 默认蓝色
        public DateTime LastUpdated { get; set; }
        public bool IsEnabled { get; set; } = true;
}

public class CalendarEvent
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string Location { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string Color { get; set; } = "#0078D4";
    public bool IsAllDay { get; set; }
    public string Url { get; set; } = string.Empty;
}

public class AppSettings
{
    public List<IcsSource> IcsSources { get; set; } = new();
    public bool AutoStart { get; set; } = false;
    public string Theme { get; set; } = "Auto";
    public bool ShowChinaHoliday { get; set; } = true;
}