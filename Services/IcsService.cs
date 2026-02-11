using System.Net.Http;
using System.IO;
using Ical.Net;
using MiniCalendar.Models;

namespace MiniCalendar.Services;

public class IcsService
{
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly string _logFile;

    public IcsService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
        
        // 设置缓存和日志路径
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appFolder = Path.Combine(appData, "MiniCalendar");
        _cacheDirectory = Path.Combine(appFolder, "Cache");
        _logFile = Path.Combine(appFolder, "requests.log");
        
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
    }

    private void LogRequest(string url, string triggerType, string status, string? error = null)
    {
        try
        {
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Trigger: {triggerType} | URL: {url} | Status: {status} | Error: {error ?? "None"}{Environment.NewLine}";
            File.AppendAllText(_logFile, logEntry);
        }
        catch
        {
            // 忽略日志记录失败
        }
    }

    private string GetCacheFilePath(string url)
    {
        // 使用 URL 的哈希作为文件名
        using var md5 = System.Security.Cryptography.MD5.Create();
        var hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(url));
        var fileName = BitConverter.ToString(hash).Replace("-", "").ToLower() + ".ics";
        return Path.Combine(_cacheDirectory, fileName);
    }

    public DateTime? GetLastCacheUpdateTime(string url)
    {
        try
        {
            var cachePath = GetCacheFilePath(url);
            if (File.Exists(cachePath))
            {
                return File.GetLastWriteTime(cachePath);
            }
        }
        catch
        {
            // ignore
        }
        return null;
    }

    public async Task<List<CalendarEvent>> LoadEventsFromIcsAsync(IcsSource source, string triggerType = "Unknown")
    {
        string? icsContent = null;
        
        // 处理 webcal 协议
        var requestUrl = source.Url;
        if (requestUrl.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
        {
            requestUrl = "https://" + requestUrl.Substring(9);
        }
        
        var cachePath = GetCacheFilePath(source.Url); // 缓存键依然使用原始 URL

        // 检查是否需要跳过网络请求
        // 1. 如果是启动触发 (Startup)，且刷新间隔 <= 0，则永远只读缓存（之前已有的逻辑）
        // 2. 如果是启动触发 (Startup)，且上次更新时间在有效期内，则跳过网络请求
        // 3. 如果是定时器触发 (Timer)，逻辑通常由 Timer 间隔控制，但这里双重检查更稳妥
        
        bool skipNetwork = false;
        
        if (triggerType == "Startup")
        {
            if (source.RefreshIntervalMinutes <= 0)
            {
                skipNetwork = true;
            }
            else
            {
                // 检查上次更新时间
                var nextRefreshTime = source.LastUpdated.AddMinutes(source.RefreshIntervalMinutes);
                if (DateTime.Now < nextRefreshTime)
                {
                    skipNetwork = true;
                    LogRequest(requestUrl, triggerType, $"SkippedNetwork (Next refresh: {nextRefreshTime:MM-dd HH:mm})");
                }
            }
        }
        else if (triggerType == "Timer")
        {
             // 定时器触发时，通常意味着时间到了。但为了防止 Timer 刚刚启动就触发（虽然设置了 Interval），
             // 或者系统休眠唤醒导致的一堆 Timer 触发，也可以检查一下。
             // 不过 Timer 的逻辑在 MainWindow 里控制，这里主要负责执行。
             // 暂不强制检查，除非为了防抖。
        }

        try
        {
            if (!skipNetwork)
            {
                // 尝试网络请求
                icsContent = await _httpClient.GetStringAsync(requestUrl);
                
                // 请求成功，保存缓存
                await File.WriteAllTextAsync(cachePath, icsContent);
                LogRequest(requestUrl, triggerType, "Success");
                
                // 更新最后更新时间（注意：这里修改的是内存对象的属性，调用方需要负责持久化保存）
                source.LastUpdated = DateTime.Now;
            }
            else
            {
                // 故意抛出异常以进入 catch 块读取缓存
                throw new Exception("Network refresh skipped due to configuration or cache validity.");
            }
        }
        catch (Exception ex)
        {
            if (!skipNetwork) // 如果不是故意跳过的，才记录失败日志
            {
                LogRequest(requestUrl, triggerType, "Failed", ex.Message);
                System.Diagnostics.Debug.WriteLine($"加载ICS文件失败: {source.Name} - {ex.Message}");
            }
            
            // 网络请求失败或跳过，尝试读取缓存
            if (File.Exists(cachePath))
            {
                try
                {
                    icsContent = await File.ReadAllTextAsync(cachePath);
                    LogRequest(source.Url, triggerType, "CacheHit");
                }
                catch (Exception cacheEx)
                {
                    LogRequest(source.Url, triggerType, "CacheReadFailed", cacheEx.Message);
                }
            }
        }

        if (string.IsNullOrEmpty(icsContent))
        {
            return new List<CalendarEvent>();
        }

        try
        {
            var calendar = Calendar.Load(icsContent);
            var events = new List<CalendarEvent>();

            foreach (var calendarEvent in calendar.Events)
            {
                var startTime = calendarEvent.Start.AsSystemLocal;
                var endTime = calendarEvent.End?.AsSystemLocal ?? startTime.AddHours(1);

                events.Add(new CalendarEvent
                {
                    Id = calendarEvent.Uid ?? Guid.NewGuid().ToString(),
                    Title = calendarEvent.Summary ?? "无标题",
                    Description = calendarEvent.Description ?? string.Empty,
                    StartTime = startTime,
                    EndTime = endTime,
                    Location = calendarEvent.Location ?? string.Empty,
                    SourceId = source.Id,
                    SourceName = source.Name,
                    Color = source.Color,
                    IsAllDay = calendarEvent.IsAllDay
                });
            }

            source.LastUpdated = DateTime.Now;
            return events;
        }
        catch (Exception ex)
        {
            LogRequest(source.Url, triggerType, "ParseFailed", ex.Message);
            return new List<CalendarEvent>();
        }
    }

    public async Task<Dictionary<string, List<CalendarEvent>>> LoadAllEventsAsync(List<IcsSource> sources, string triggerType = "Unknown")
    {
        var allEvents = new Dictionary<string, List<CalendarEvent>>();
        var tasks = new List<Task>();

        foreach (var source in sources.Where(s => s.IsEnabled))
        {
            tasks.Add(Task.Run(async () =>
            {
                var events = await LoadEventsFromIcsAsync(source, triggerType);
                lock (allEvents)
                {
                    allEvents[source.Id] = events;
                }
            }));
        }

        await Task.WhenAll(tasks);
        return allEvents;
    }
}