﻿using System;
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

        private string _SendFileLogText;
        public string SendFileLogText {
            get { return _SendFileLogText; }
            set { this.SetProperty(ref _SendFileLogText, value); }
        }

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

        public event AsyncCompletedEventHandler DiscoveryComplated;
        public DelegateCommand ConnectInputDeviceCommand { get; private set; }

        public DelegateCommand TickTimerCommand { get; private set; }

        private BluetoothApplicationServer Server { get; set; }
        public DelegateCommand StartServerCommand { get; private set; }
        public DelegateCommand StopServerCommand { get; private set; }

        private BluetoothApplicationClient Client { get; set; }

        public MainWindowViewModel() {
            this._SendFileLogText = "";
            this._RecvFileLogText = "";
            this._DeviceList = new ObservableCollection<KeyValuePair<string, string>>();
            this._SelectedDevice = null;

            this.TickTimerCommand = new DelegateCommand(TickTimer, CanTickTimer);
            this.StartServerCommand = new DelegateCommand(StartServer, CanStartServer);
            this.StopServerCommand = new DelegateCommand(StopServer, CanStopServer);
            this.ConnectInputDeviceCommand = new DelegateCommand(this.ConnectInputDevice, this.CanConnectInputDevice);

            if (this.IsServerMode()) {
                this.RunModeMessage = "現在サーバモードで動作しています。";
                this.Server = new BluetoothApplicationServer();
            }
            if (this.IsClientMode()) {
                this.RunModeMessage = "現在クライアントモードで動作しています。";

            }
        }

        private void TickTimer() {

        }

        private bool CanTickTimer() {
            return true;
        }

        private bool IsServerMode() {
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

        private bool IsClientMode() {
            if (Properties.Settings.Default.Mode.ToLower() == "client") {
                return true;
            } else {
                return false;
            }
        }

        private void ConnectInputDevice() {
            Console.WriteLine("入力デバイスの接続開始");

            if (!this.SelectedDevice.HasValue) {
                //this.BluetoothClient.Discovery();
                //this.DeviceList.Clear();
                //foreach (var itm in this.BluetoothClient.GetFindedDeviceList()) {
                //    this.DeviceList.Add(itm);
                //}
                this.DiscoveryComplated?.Invoke(this, null);
            }

            if (this.SelectedDevice.HasValue) {
                //this.BluetoothClient.Connect(this.SelectedDevice.Value.Key);
            }

        }

        private bool CanConnectInputDevice() {
            return this.IsClientMode();
        }
    }
}