using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace BluetoothCopy
{
    class BluetoothApplicationClient
    {
        public event PropertyChangedEventHandler ReceiveTextChanged;

        private readonly Guid RfcommChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");

        private DeviceWatcher DeviceWatcher;
        private List<DeviceInformation> KnownDeviceList;
        private bool DeviceWatchComplateFlag;
        private NLog.ILogger Logger;
        private DeviceInformation SelectedDeviceInfo;
        private BluetoothDevice SelectedDevice;
        private StreamSocket Socket;
        private DataWriter SocketWriter;
        private DataReader SocketReader;
        private RfcommDeviceService RfcommService;

        private Task ReceiveTask;
        private System.Collections.Concurrent.ConcurrentQueue<string> ReceiveTextQueue;
        private bool RunningFlag;

        public BluetoothApplicationClient() {
            this.Logger = NLog.LogManager.GetLogger("BluetoothApplicationClient");
            this.KnownDeviceList = new List<DeviceInformation>();
            this.ReceiveTextQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
        }

        public void Discovery() {
            try {
                DeviceWatchComplateFlag = false;
                this.StartBluetoothDeviceWatcher();

                while (!DeviceWatchComplateFlag) {
                    this.Logger.Info("入力デバイス検索中");
                    System.Threading.Thread.Sleep(1000);
                }
                
                this.StopBleDeviceWatcher();
                DeviceWatchComplateFlag = false;

                foreach (var dev in KnownDeviceList) {
                    this.Logger.Info(dev.Id + "," + dev.Name);
                }

            } catch (Exception ex) {
                this.Logger.Error(ex,"入力デバイスへの検索失敗");
            }
        }

        private void StartBluetoothDeviceWatcher() {
            //// Additional properties we would like about the device.
            //// Property strings are documented here https://msdn.microsoft.com/en-us/library/windows/desktop/ff521659(v=vs.85).aspx
            //string[] requestedProperties = { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected", "System.Devices.Aep.Bluetooth.Le.IsConnectable" };

            //// BT_Code: Example showing paired and non-paired in a single query.
            //string aqsAllBluetoothLEDevices = "(System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\")";

            //DeviceWatcher =
            //        DeviceInformation.CreateWatcher(
            //            aqsAllBluetoothLEDevices,
            //            requestedProperties,
            //            DeviceInformationKind.AssociationEndpoint);

            //var selector = BluetoothDevice.GetDeviceSelector();
            var selector = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
            DeviceWatcher = DeviceInformation.CreateWatcher(selector, null, DeviceInformationKind.AssociationEndpoint);

            // Register event handlers before starting the watcher.
            DeviceWatcher.Added += DeviceWatcher_Added;
            DeviceWatcher.Updated += DeviceWatcher_Updated;
            DeviceWatcher.Removed += DeviceWatcher_Removed;
            DeviceWatcher.EnumerationCompleted += DeviceWatcher_EnumerationCompleted;
            DeviceWatcher.Stopped += DeviceWatcher_Stopped;

            // Start over with an empty collection.
            KnownDeviceList.Clear();

            // Start the watcher.
            DeviceWatcher.Start();
        }

        private void DeviceWatcher_Added(DeviceWatcher sender, DeviceInformation args) {
            if (sender == DeviceWatcher) {
                var devs = from d in KnownDeviceList where d.Id == args.Id select d;
                if (devs.Count()==0) {
                    if (args.Name != string.Empty) {
                        this.Logger.Debug("Add Device:" + args.Name);
                        KnownDeviceList.Add(args);
                    } else {
                        this.Logger.Info("未知のデバイスあり");
                        this.Logger.Info("Id={0},Kind={1},Enabled={2}",args.Id,args.Kind,args.IsEnabled);
                    }
                }

            }
        }
        private void DeviceWatcher_Updated(DeviceWatcher sender, DeviceInformationUpdate args) {
            if (sender == DeviceWatcher) {
                var devs = from d in KnownDeviceList where d.Id == args.Id select d;
                if (devs.Count() > 0) {
                    foreach (var dev in devs) {
                        this.Logger.Debug("Update Device:" + dev.Name);
                        dev.Update(args);
                    }
                }
            }
        }
        private void DeviceWatcher_Removed(DeviceWatcher sender, DeviceInformationUpdate args) {
            if (sender == DeviceWatcher) {
                var devs = from d in KnownDeviceList where d.Id == args.Id select d;
                if (devs.Count() > 0) {
                    foreach (var dev in devs.ToList()) {
                        this.Logger.Debug("Remove Device:" + dev.Name);
                        KnownDeviceList.Remove(dev);
                    }
                }
            }
        }

        private void DeviceWatcher_EnumerationCompleted(DeviceWatcher sender, object args) {
            if (sender == DeviceWatcher) {
                DeviceWatchComplateFlag = true;
                this.Logger.Info("入力デバイス検索完了");
            }
        }

        private void DeviceWatcher_Stopped(DeviceWatcher sender, object args) {
            if (sender == DeviceWatcher) {
                this.Logger.Info("入力デバイス検索停止");
            }
        }

        /// <summary>
        /// Stops watching for all nearby Bluetooth devices.
        /// </summary>
        private void StopBleDeviceWatcher() {
            if (DeviceWatcher != null) {
                // Unregister the event handlers.
                DeviceWatcher.Added -= DeviceWatcher_Added;
                DeviceWatcher.Updated -= DeviceWatcher_Updated;
                DeviceWatcher.Removed -= DeviceWatcher_Removed;
                DeviceWatcher.EnumerationCompleted -= DeviceWatcher_EnumerationCompleted;
                DeviceWatcher.Stopped -= DeviceWatcher_Stopped;

                // Stop the watcher.
                DeviceWatcher.Stop();
                DeviceWatcher = null;
            }
        }

        public SortedList<string,string> GetFindedDeviceList() {
            var lst = new SortedList<string, string>();
            foreach (var dev in this.KnownDeviceList) {
                lst.Add(dev.Id, dev.Name);
            }
            return lst;
        }

        public async void Connect(string devid) {
            this.Logger.Info("入力デバイスへの接続開始:" + devid);

            this.SelectedDeviceInfo = (from i in KnownDeviceList where i.Id==devid select i).First();

            try {
                SelectedDevice = await BluetoothDevice.FromIdAsync(this.SelectedDeviceInfo.Id);
                var rfcommsrv = await SelectedDevice.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(RfcommChatServiceUuid), BluetoothCacheMode.Uncached);
                RfcommService = rfcommsrv.Services[0];
                var attributes = await RfcommService.GetSdpRawAttributesAsync();
                var attributereader = DataReader.FromBuffer(attributes[0x100]);
                var attributetype = attributereader.ReadByte();
                var servicenamelength = attributereader.ReadByte();
                attributereader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf16BE;

                Socket = new StreamSocket();
                await Socket.ConnectAsync(RfcommService.ConnectionHostName, RfcommService.ConnectionServiceName);
                SocketWriter = new DataWriter(Socket.OutputStream);
                SocketReader = new DataReader(Socket.InputStream);
                this.RunningFlag = true;

                this.ReceiveTask = Task.Run(() => { this.RunReceive(); });

            } catch (Exception ex) {
                this.Logger.Error(ex,"入力デバイスへの接続失敗");
                this.ReceiveTextChanged?.Invoke("STS入力デバイスへの接続失敗", new PropertyChangedEventArgs("ReceiveText"));
            }
        }

        private void RunReceive() {
            this.Logger.Info("データ受信開始");
            this.ReceiveTextChanged?.Invoke("STS入力デバイスに接続しました。", new PropertyChangedEventArgs("ReceiveText"));

            while (this.RunningFlag) {
                try {
                    var loadop = SocketReader.LoadAsync(sizeof(uint));
                    while (loadop.Status != Windows.Foundation.AsyncStatus.Completed) {
                        System.Threading.Thread.Sleep(500);
                        if (!this.RunningFlag) {
                            break;
                        }
                    }
                    if (!this.RunningFlag) {
                        break;
                    }
                    uint size = loadop.GetResults();
                    this.Logger.Debug("データ受信1:" + size);

                    if (size < sizeof(uint)) {
                        break;
                    }
                    uint strlen = SocketReader.ReadUInt32();
                    this.Logger.Debug("データ受信2:" + strlen);

                    loadop = SocketReader.LoadAsync(strlen);
                    while (loadop.Status != Windows.Foundation.AsyncStatus.Completed) {
                        System.Threading.Thread.Sleep(500);
                        if (!this.RunningFlag) {
                            break;
                        }
                    }
                    if (!this.RunningFlag) {
                        break;
                    }
                    uint acstrlen = loadop.GetResults();
                    this.Logger.Debug("データ受信3:" + acstrlen);

                    if (acstrlen != strlen) {
                        break;
                    }
                    byte[] recvdata=new byte[strlen];
                    SocketReader.ReadBytes(recvdata);
                    string message = System.Text.Encoding.UTF8.GetString(recvdata);
                    this.Logger.Info("データ受信");
                    this.Logger.Info(message);
                    this.ReceiveTextQueue.Enqueue(message);
                    this.ReceiveTextChanged?.Invoke(message, new PropertyChangedEventArgs("ReceiveText"));
                } catch (Exception ex) {
                    this.Logger.Error(ex,"データ通信中にエラー発生！");
                    this.ReceiveTextChanged?.Invoke("STSデータ通信中にエラー発生！", new PropertyChangedEventArgs("ReceiveText"));
                    break;
                }
            }
        }

        public bool IsRunning() {
            return this.RunningFlag;
        }

        public void Disconnect() {

            this.RunningFlag = false;

            if (SocketReader != null) {
                try {
                    if (SocketReader.InputStreamOptions != InputStreamOptions.None) {
                        SocketReader.DetachStream();
                    }
                } catch (Exception ex) {
                    this.Logger.Error(ex);
                }
                SocketReader.Dispose();
                SocketReader = null;
            }
            if (SocketWriter != null) {
                try {
                    SocketWriter.DetachStream();
                } catch (Exception ex) {
                    this.Logger.Error(ex);
                }
                SocketWriter.Dispose();
                SocketWriter = null;
            }
            if (RfcommService != null) {
                RfcommService.Dispose();
                RfcommService = null;
            }
            if (Socket != null) {
                Socket.Dispose();
                Socket = null;
            }
        }
    }
}
