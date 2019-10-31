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
            this.ProcessTimer.Interval = new TimeSpan(0, 0, 2);
            this.ProcessTimer.Tick += ProcessTimer_Run;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e) {
            this.Logger.Info("Start Bluetooth Copy");
            this.ViewModel.DiscoveryComplated += ViewModel_DiscoveryComplated;
            this.ViewModel.ConnectErrored += ViewModel_ConnectErrored;

            if (this.IsReady()) {
                if (this.ViewModel.StartServerCommand.CanExecute()) {
                    this.ViewModel.StartServerCommand.Execute();
                }
                if (this.ViewModel.StartClientCommand.CanExecute()) {
                    var devid = Properties.Settings.Default.AutoConnectDevice;
                    if (!string.IsNullOrWhiteSpace(devid)) {
                        this.ViewModel.SelectedDevice = new KeyValuePair<string, string>(devid, "AutoConnectDevice");
                        this.ViewModel.StartClientCommand.Execute();
                    }
                }
                this.ProcessTimer.Start();
            } else {
                MessageBox.Show("環境設定の不備があります。ログを確認してください。", this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e) {

        }

        private void Window_Closed(object sender, EventArgs e) {
            this.ProcessTimer.Stop();
            if (this.ViewModel.StopServerCommand.CanExecute()) {
                this.ViewModel.StopServerCommand.Execute();
            }
            this.ViewModel.ConnectErrored -= ViewModel_ConnectErrored;
            this.ViewModel.DiscoveryComplated -= ViewModel_DiscoveryComplated;
            this.Logger.Info("End Bluetooth Copy");
        }

        private void ViewModel_DiscoveryComplated(object sender, System.ComponentModel.AsyncCompletedEventArgs e) {
            var dialog = new SelectedListDialog();
            dialog.DataContext = sender;
            dialog.ShowDialog();
        }

        private void ViewModel_ConnectErrored(object sender, System.ComponentModel.AsyncCompletedEventArgs e) {
            if (!this.ViewModel.IsAutoConnect()) {
                MessageBox.Show("Bluetooth機器への接続に失敗しました。", this.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessTimer_Run(object sender, EventArgs e) {
            if (this.ViewModel.TickTimerCommand.CanExecute()) {
                this.ViewModel.TickTimerCommand.Execute();
            }
        }

        private bool IsReady() {
            if (!System.IO.Directory.Exists(Properties.Settings.Default.SendDirectoryPath)) {
                this.Logger.Error("送信用フォルダがありません。{0}", Properties.Settings.Default.SendDirectoryPath);
                return false;
            }
            if (!System.IO.Directory.Exists(Properties.Settings.Default.ReceiveDirectoryPath)) {
                this.Logger.Error("受信用フォルダがありません。{0}", Properties.Settings.Default.ReceiveDirectoryPath);
                return false;
            }
            if (this.ViewModel.IsServerMode()) {
                if (!System.IO.Directory.Exists(Properties.Settings.Default.ArchiveDirectoryPath)) {
                    this.Logger.Error("保存用フォルダがありません。{0}", Properties.Settings.Default.ArchiveDirectoryPath);
                    return false;
                }
            }

            return true;
        }

        private void Test() {
            var srcfilename = System.IO.Path.Combine(Properties.Settings.Default.SendDirectoryPath, "テスト文書.pdf");
            var srcfile = new System.IO.FileInfo(srcfilename);
            var filedata = System.IO.File.ReadAllBytes(srcfilename);
            var senddata = new BluetoothApplicationTransferData(srcfile.Name, filedata);
            var stream = senddata.GetTransferByteStream();
            var recvdata = new BluetoothApplicationTransferData(stream);
            var destfilename = System.IO.Path.Combine(Properties.Settings.Default.ReceiveDirectoryPath, recvdata.Name);
            System.IO.File.WriteAllBytes(destfilename, recvdata.Data);
        }
    }
}
