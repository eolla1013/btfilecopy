using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Prism.Commands;

namespace BluetoothCopy
{
    class MainWindowViewModel : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual bool SetProperty<T>(ref T field, T value, [CallerMemberName]string propertyName = null) {
            if (Equals(field, value)) { return false; }
            field = value;
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        #endregion

        private string _RunModeMessage;
        public string RunModeMessage {
            get { return _RunModeMessage; }
            set { this.SetProperty(ref _RunModeMessage, value); }
        }

        private List<string> SendFileNameList;

        private string _SendFileLogText;
        public string SendFileLogText {
            get { return _SendFileLogText; }
            set { this.SetProperty(ref _SendFileLogText, value); }
        }

        private List<string> RecvFileNameList;

        private string _RecvFileLogText;
        public string RecvFileLogText {
            get { return _RecvFileLogText; }
            set { this.SetProperty(ref _RecvFileLogText, value); }
        }

        private ObservableCollection<KeyValuePair<string, string>> _DeviceList;
        public ObservableCollection<KeyValuePair<string, string>> DeviceList {
            get { return this._DeviceList; }
            set {
                this.SetProperty(ref this._DeviceList, value);
            }
        }

        private KeyValuePair<string, string>? _SelectedDevice;
        public KeyValuePair<string, string>? SelectedDevice {
            get { return this._SelectedDevice; }
            set {
                this.SetProperty(ref this._SelectedDevice, value);
            }
        }

        private NLog.Logger Logger;

        public event AsyncCompletedEventHandler DiscoveryComplated;
        public event AsyncCompletedEventHandler ConnectErrored;
        public DelegateCommand StartClientCommand { get; private set; }

        public DelegateCommand TickTimerCommand { get; private set; }

        private BluetoothApplicationServer Server { get; set; }
        public DelegateCommand StartServerCommand { get; private set; }
        public DelegateCommand StopServerCommand { get; private set; }

        private BluetoothApplicationClient Client { get; set; }

        public MainWindowViewModel() {
            this.SendFileNameList = new List<string>();
            this._SendFileLogText = "";
            this.RecvFileNameList = new List<string>();
            this._RecvFileLogText = "";
            this._DeviceList = new ObservableCollection<KeyValuePair<string, string>>();
            this._SelectedDevice = null;
            this.Logger = NLog.LogManager.GetLogger("MainWindow");

            this.TickTimerCommand = new DelegateCommand(TickTimer, CanTickTimer);
            this.StartServerCommand = new DelegateCommand(StartServer, CanStartServer);
            this.StopServerCommand = new DelegateCommand(StopServer, CanStopServer);
            this.StartClientCommand = new DelegateCommand(this.StartClient, this.CanStartClient);

            if (this.IsServerMode()) {
                this.RunModeMessage = "現在サーバモードで動作しています。";
                this.Server = new BluetoothApplicationServer();
                this.Server.ConnectStatusChanged += Server_ConnectStatusChanged;
            }
            if (this.IsClientMode()) {
                this.RunModeMessage = "現在クライアントモードで動作しています。";
                this.Client = new BluetoothApplicationClient();
                this.Client.ConnectStatusChanged += Client_ConnectStatusChanged;

            }
        }

        private void TickTimer() {
            if (this.IsClientMode()) {
                try {
                    if (this.IsAutoConnect() && !this.Client.IsRunning() && !this.Client.IsStarting()) {
                        this.StopClient();
                        this.StartClient();
                        return;//再接続時はいったん抜けて次のタイミングで実行
                    }
                } catch (Exception ex) {
                    this.Logger.Error(ex, "再接続に失敗しました。");
                    return;
                }
                try {
                    var lst=this.Client.SendFile();
                    this.DisplaySendFileName(lst);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "ファイルの送信に失敗しました。");
                }
                try {
                    var lst = this.Client.ReceiveFile();
                    this.DisplayRecvFileName(lst);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "ファイルの受信に失敗しました。");
                }
            }
            if (this.IsServerMode()) {
                try {
                    if (!this.Server.IsRunning()) {
                        this.StopServer();
                        this.StartServer();
                        return;//再起動時はいったん抜けて次のタイミングで実行
                    }
                } catch (Exception ex) {
                    this.Logger.Error(ex, "再起動に失敗しました。");
                    return;
                }
                try {
                    var lst=this.Server.SendFile();
                    this.DisplaySendFileName(lst);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "ファイルの送信に失敗しました。");
                }
                try {
                    var lst = this.Server.ReceiveFile();
                    this.DisplayRecvFileName(lst);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "ファイルの受信に失敗しました。");
                }
            }
        }

        private void DisplaySendFileName(List<string> lst) {
            foreach (var name in lst) {
                if (this.SendFileNameList.Count > 100) {
                    this.SendFileNameList.RemoveAt(0);
                }
                this.SendFileNameList.Add(string.Format("{0:yyyy-MM-dd HH:mm:ss} {1}",DateTime.Now,name));
            }
            var txt = new StringBuilder();
            foreach (var name in this.SendFileNameList) {
                txt.AppendLine(name);
            }
            this.SendFileLogText = txt.ToString();
        }

        private void DisplayRecvFileName(List<string> lst) {
            foreach (var name in lst) {
                if (this.RecvFileNameList.Count > 100) {
                    this.RecvFileNameList.RemoveAt(0);
                }
                this.RecvFileNameList.Add(string.Format("{0:yyyy-MM-dd HH:mm:ss} {1}", DateTime.Now, name));
            }
            var txt = new StringBuilder();
            foreach (var name in this.RecvFileNameList) {
                txt.AppendLine(name);
            }
            this.RecvFileLogText = txt.ToString();
        }

        private bool CanTickTimer() {
            return true;
        }

        public bool IsServerMode() {
            if (Properties.Settings.Default.Mode.ToLower() == "server") {
                return true;
            } else {
                return false;
            }
        }

        private void StartServer() {
            this.Server.Start();
        }

        private bool CanStartServer() {
            return this.IsServerMode();
        }

        private void StopServer() {
            this.Server.Stop();
        }

        private bool CanStopServer() {
            return this.IsServerMode();
        }

        private void Server_ConnectStatusChanged(object sender, AsyncCompletedEventArgs e) {
            if ((BluetoothApplicationConnectStatus)e.UserState == BluetoothApplicationConnectStatus.Error) {
                this.ConnectErrored?.Invoke(this, e);
            }
        }

        public bool IsClientMode() {
            if (Properties.Settings.Default.Mode.ToLower() == "client") {
                return true;
            } else {
                return false;
            }
        }

        public bool IsAutoConnect() {
            if (string.IsNullOrWhiteSpace(Properties.Settings.Default.AutoConnectDevice)) {
                return false;
            } else {
                return true;
            }
        }

        private void StartClient() {

            this.Client.Discovery();

            if (!this.SelectedDevice.HasValue) {
                this.DeviceList.Clear();
                foreach (var itm in this.Client.GetFindedDeviceList()) {
                    this.DeviceList.Add(itm);
                }
                this.DiscoveryComplated?.Invoke(this, null);
            }

            if (this.SelectedDevice.HasValue) {
                this.Client.Connect(this.SelectedDevice.Value.Key);
            }

        }

        private bool CanStartClient() {
            return this.IsClientMode();
        }

        private void StopClient() {
            this.Client.Disconnect();
        }

        private void Client_ConnectStatusChanged(object sender, AsyncCompletedEventArgs e) {
            if ((BluetoothApplicationConnectStatus)e.UserState == BluetoothApplicationConnectStatus.Error) {
                this.ConnectErrored?.Invoke(this, e);
            }
        }

    }
}
