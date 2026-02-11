using System.Windows;
using MiniCalendar.Models;
using MiniCalendar.Services;

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
            ? "全天" 
            : $"{_calendarEvent.StartTime:MM月dd日 HH:mm} - {_calendarEvent.EndTime:MM月dd日 HH:mm}";
        TimeText.Text = timeText;
        
        LocationText.Text = string.IsNullOrEmpty(_calendarEvent.Location) 
            ? "无" 
            : _calendarEvent.Location;
        
        DescriptionText.Text = string.IsNullOrEmpty(_calendarEvent.Description) 
            ? "无" 
            : _calendarEvent.Description;
        
        SourceText.Text = _calendarEvent.SourceName;

        if (!string.IsNullOrEmpty(_calendarEvent.Url))
        {
            UrlPanel.Visibility = Visibility.Visible;
            UrlRun.Text = _calendarEvent.Url;
            try 
            {
                UrlLink.NavigateUri = new Uri(_calendarEvent.Url);
            }
            catch { }
        }
        else
        {
            UrlPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void UrlLink_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_calendarEvent.Url))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _calendarEvent.Url,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"无法打开链接: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}