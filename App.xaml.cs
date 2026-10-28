using System;
using System.Windows;

namespace QuickQR
{
    public partial class App : System.Windows.Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var win = new MainWindow();
            this.MainWindow = win;
            win.Show();
            win.Activate();
        }
    }
}
