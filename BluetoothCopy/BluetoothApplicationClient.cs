﻿using System;
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
        private readonly Guid RfcommServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");

        public event AsyncCompletedEventHandler ConnectStatusChanged;

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

        private Task SendTask;
        private System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData> SendFileQueue;

        private Task ReceiveTask;
        private System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData> ReceiveFileQueue;
        private bool RunningFlag;
        private bool StartingSendTaskFlag;
        private bool StartingReceiveTaskFlag;

        public BluetoothApplicationClient() {
            this.Logger = NLog.LogManager.GetLogger("BluetoothApplicationClient");
            this.KnownDeviceList = new List<DeviceInformation>();

            this.SendFileQueue = new System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData>();
            this.ReceiveFileQueue = new System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData>();
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

            this.StartingSendTaskFlag = true;
            this.StartingReceiveTaskFlag = true;

            this.SelectedDeviceInfo = (from i in KnownDeviceList where i.Id==devid select i).First();

            try {
                SelectedDevice = await BluetoothDevice.FromIdAsync(this.SelectedDeviceInfo.Id);
                var rfcommsrv = await SelectedDevice.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(RfcommServiceUuid), BluetoothCacheMode.Uncached);
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
                this.SendTask = Task.Run(() => { this.RunSend(); });
                this.ReceiveTask = Task.Run(() => { this.RunReceive(); });

                this.ConnectStatusChanged?.Invoke(this, new AsyncCompletedEventArgs(null, false, BluetoothApplicationConnectStatus.Connect));
            } catch (Exception ex) {
                this.Logger.Error(ex,"入力デバイスへの接続失敗");
                this.RunningFlag = false;
                this.StartingSendTaskFlag = false;
                this.StartingReceiveTaskFlag = false;
                this.ConnectStatusChanged?.Invoke(this, new AsyncCompletedEventArgs(null, false, BluetoothApplicationConnectStatus.Error));
            }
        }

        public List<string> SendFile() {
            var filelst = System.IO.Directory.GetFiles(Properties.Settings.Default.SendDirectoryPath);
            var sendlst = new List<string>();
            foreach (var filename in filelst) {
                try {
                    var file = new System.IO.FileInfo(filename);
                    var filedata = System.IO.File.ReadAllBytes(filename);
                    System.IO.File.Delete(filename);

                    var send = new BluetoothApplicationTransferData(file.Name, filedata);
                    this.SendFileQueue.Enqueue(send);
                    sendlst.Add(file.Name);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "送信ファイル読み込み失敗。{0}",filename);
                }
            }
            return sendlst;
        }

        private void RunSend() {
            this.Logger.Info("データ送信処理開始");
            this.StartingSendTaskFlag = false;
            while (RunningFlag) {
                try {
                    BluetoothApplicationTransferData send = null;
                    var ret = this.SendFileQueue.TryDequeue(out send);
                    if (ret) {
                        var senddata = send.GetTransferByteStream();

                        SocketWriter.WriteUInt32((uint)senddata.Length);
                        SocketWriter.WriteBytes(senddata);

                        //var sendlen=SocketWriter.StoreAsync().GetResults();
                        var tsk = SocketWriter.StoreAsync();
                        while (tsk.Status == Windows.Foundation.AsyncStatus.Started) {
                            System.Threading.Thread.Sleep(500);
                        }
                        var sendlen = tsk.GetResults();

                        this.Logger.Info("データ送信結果:{0},{1}",send.Name,sendlen);
                    }
                    System.Threading.Thread.Sleep(1000);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "データ送信中にエラー発生！");
                    break;
                }
            }
        }

        public List<string> ReceiveFile() {
            var lst = new List<string>();
            while (!this.ReceiveFileQueue.IsEmpty) {
                BluetoothApplicationTransferData recv = null;
                var ret = this.ReceiveFileQueue.TryDequeue(out recv);
                if (ret) {
                    var filename = System.IO.Path.Combine(Properties.Settings.Default.ReceiveDirectoryPath, recv.Name);
                    System.IO.File.WriteAllBytes(filename, recv.Data);

                    lst.Add(recv.Name);
                }
            }

            return lst;
        }

        private void RunReceive() {
            this.Logger.Info("データ受信処理開始");
            this.StartingReceiveTaskFlag = false;
            while (this.RunningFlag) {
                try {
                    //受信データ長
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
                    if (size < sizeof(uint)) {
                        break;
                    }
                    uint recvsize = SocketReader.ReadUInt32();

                    //ファイル名のデータ
                    loadop = SocketReader.LoadAsync(recvsize);
                    while (loadop.Status != Windows.Foundation.AsyncStatus.Completed) {
                        System.Threading.Thread.Sleep(500);
                        if (!this.RunningFlag) {
                            break;
                        }
                    }
                    if (!this.RunningFlag) {
                        break;
                    }
                    size = loadop.GetResults();
                    if (size != recvsize) {
                        break;
                    }
                    byte[] recvdata = new byte[recvsize];
                    SocketReader.ReadBytes(recvdata);

                    var recv = new BluetoothApplicationTransferData(recvdata);
                    this.Logger.Info("データ受信結果:{0},{1}",recv.Name,recv.Data.Length);
                    this.ReceiveFileQueue.Enqueue(recv);
                } catch (Exception ex) {
                    this.Logger.Error(ex,"データ通信中にエラー発生！");
                    break;
                }
            }
            this.ConnectStatusChanged?.Invoke(this, new AsyncCompletedEventArgs(null, false, BluetoothApplicationConnectStatus.Disconnect));
        }

        public bool IsRunning() {
            if(this.SendTask==null || this.ReceiveTask == null) {
                return false;
            }
            if(this.SendTask.IsCompleted || this.ReceiveTask.IsCompleted) {
                return false;
            }
            return true;
        }

        public bool IsStarting() {
            if(this.StartingSendTaskFlag || this.StartingReceiveTaskFlag) {
                return true;
            } else {
                return false;
            }
        }

        public void Disconnect() {

            this.RunningFlag = false;
            this.StartingSendTaskFlag = false;
            this.StartingReceiveTaskFlag = false;

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
