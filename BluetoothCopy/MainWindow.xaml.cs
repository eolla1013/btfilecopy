using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace BluetoothCopy
{
    /// <summary>
    /// MainWindow.xaml の相互作用ロジック
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainWindowViewModel ViewModel {
            get {
                return (MainWindowViewModel)this.DataContext;
            }
        }
        private NLog.Logger Logger;
        private DispatcherTimer ProcessTimer;

        public MainWindow() {
            InitializeComponent();

            this.Logger = NLog.LogManager.GetLogger("MainWindow");
            this.ProcessTimer = new DispatcherTimer();
            this.ProcessTimer.Interval = new TimeSpan(0, 0, 1);
            this.ProcessTimer.Tick += ProcessTimer_Run;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            this.Logger.Info("End Bluetooth Copy");
            this.ViewModel.DiscoveryComplated += ViewModel_DiscoveryComplated;
            if (this.ViewModel.StartServerCommand.CanExecute()) {
                this.ViewModel.StartServerCommand.Execute();
            }
            this.ProcessTimer.Start();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {

        }

        private void Window_Closed(object sender, EventArgs e) {
            this.ProcessTimer.Stop();
            if (this.ViewModel.StopServerCommand.CanExecute()) {
                this.ViewModel.StopServerCommand.Execute();
            }
            this.ViewModel.DiscoveryComplated -= ViewModel_DiscoveryComplated;
            this.Logger.Info("End Bluetooth Copy");
        }

        private void ViewModel_DiscoveryComplated(object sender, System.ComponentModel.AsyncCompletedEventArgs e) {
            var dialog = new SelectedListDialog();
            dialog.DataContext = sender;
            dialog.ShowDialog();
        }

        private void ProcessTimer_Run(object sender, EventArgs e) {
            this.ViewModel.TickTimerCommand.Execute();
        }

    }
}
