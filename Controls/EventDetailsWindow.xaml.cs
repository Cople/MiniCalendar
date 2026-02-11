using System.Windows;
using MiniCalendar.Models;

namespace MiniCalendar.Controls;

public partial class EventDetailsWindow : Window
{
    private readonly CalendarEvent _calendarEvent;

    public EventDetailsWindow(CalendarEvent calendarEvent)
    {
        InitializeComponent();
        
        _calendarEvent = calendarEvent;
        LoadEventDetails();
    }

    private void LoadEventDetails()
    {
        TitleText.Text = _calendarEvent.Title;
        
        var timeText = _calendarEvent.IsAllDay 
            ? "全天事件" 
            : $"{_calendarEvent.StartTime:MM月dd日 HH:mm} - {_calendarEvent.EndTime:MM月dd日 HH:mm}";
        TimeText.Text = $"时间: {timeText}";
        
        LocationText.Text = string.IsNullOrEmpty(_calendarEvent.Location) 
            ? "地点: 无" 
            : $"地点: {_calendarEvent.Location}";
        
        DescriptionText.Text = string.IsNullOrEmpty(_calendarEvent.Description) 
            ? "描述: 无" 
            : $"描述: {_calendarEvent.Description}";
        
        SourceText.Text = $"来源: {_calendarEvent.SourceName}";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}