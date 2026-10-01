using System.Windows;
using System.Windows.Threading;

namespace DaqLink.App
{
    public partial class App : Application
    {
        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 未預期的例外不要讓程式直接關掉，顯示出來方便除錯
            MessageBox.Show(e.Exception.ToString(), "Unhandled exception", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
