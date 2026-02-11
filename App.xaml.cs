using System.Windows;

namespace MiniCalendar;

public partial class App : Application
{
    private System.Threading.Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        
        // 确保单实例运行
        _mutex = new System.Threading.Mutex(true, "MiniCalendar_SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        // 初始化主窗口但不显示
        // 托盘图标会在构造函数中初始化
        MainWindow = new MainWindow();
    }
}