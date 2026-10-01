using System.ComponentModel;
using System.Windows;
using DaqLink.App.ViewModels;

namespace DaqLink.App
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _vm = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _vm.Dispose();
        }
    }
}
