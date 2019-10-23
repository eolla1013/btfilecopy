using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace BluetoothCopy
{
    class BluetoothApplicationServer
    {
        public event AsyncCompletedEventHandler ConnectStatusChanged;

        private readonly Guid RfcommChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");
        private const string SdpServiceName = "Bluetooth Rfcomm Voice Input Service";
        private NLog.ILogger Logger;
        private StreamSocket Socket;
        private DataWriter SocketWriter;
        private DataReader SocketReader;
        private RfcommServiceProvider RfcommProvider;
        private StreamSocketListener SocketListener;

        private Task SendTask;
        private System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData> SendFileQueue;

        private Task ReceiveTask;
        private System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData> ReceiveFileQueue;

        private bool RunningFlag;

        public BluetoothApplicationServer() {
            this.Logger = NLog.LogManager.GetLogger("BluetoothApplicationServer");
            this.SendFileQueue = new System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData>();
            this.ReceiveFileQueue = new System.Collections.Concurrent.ConcurrentQueue<BluetoothApplicationTransferData>();
            this.RunningFlag = false;
        }

        public async void Start() {
            this.Logger.Info("Bluetooth RFCOMM サーバ起動開始");

            RfcommProvider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(RfcommChatServiceUuid));
            SocketListener = new StreamSocketListener();
            SocketListener.ConnectionReceived += OnConnectionReceived;
            var rfcomm = RfcommProvider.ServiceId.AsString();

            await SocketListener.BindServiceNameAsync(RfcommProvider.ServiceId.AsString(),SocketProtectionLevel.BluetoothEncryptionWithAuthentication);
            InitializeServiceSdpAttributes(RfcommProvider);

            RfcommProvider.StartAdvertising(SocketListener, true);

            this.Logger.Info("サーバ起動");
        }

        private void InitializeServiceSdpAttributes(RfcommServiceProvider rfcommProvider) {
            var sdpwriter = new DataWriter();

            // Write the Service Name Attribute.
            // The SDP Type of the Service Name SDP attribute.
            // The first byte in the SDP Attribute encodes the SDP Attribute Type as follows :
            //    -  the Attribute Type size in the least significant 3 bits,
            //    -  the SDP Attribute Type value in the most significant 5 bits.
            sdpwriter.WriteByte((4 << 3) | 5);

            // The length of the UTF-8 encoded Service Name SDP Attribute.
            sdpwriter.WriteByte((byte)SdpServiceName.Length);

            // The UTF-8 encoded Service Name value.
            sdpwriter.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf8;
            sdpwriter.WriteString(SdpServiceName);

            // Set the SDP Attribute on the RFCOMM Service Provider.
            // The Id of the Service Name SDP attribute
            rfcommProvider.SdpRawAttributes.Add(0x100, sdpwriter.DetachBuffer());
        }

        private async void OnConnectionReceived(StreamSocketListener sender, StreamSocketListenerConnectionReceivedEventArgs args) {
            this.Logger.Info("クライアント接続");

            try {

                Socket = args.Socket;

                var rmtdev = await BluetoothDevice.FromHostNameAsync(Socket.Information.RemoteHostName);
                SocketWriter = new DataWriter(Socket.OutputStream);
                SocketReader = new DataReader(Socket.InputStream);
                RunningFlag = true;

                this.ReceiveTask = Task.Run(() => { this.RunReceive(); });
                this.SendTask = Task.Run(() => { this.RunSend(); });

                this.ConnectStatusChanged?.Invoke(this, new AsyncCompletedEventArgs(null, false, BluetoothApplicationConnectStatus.Connect));
            } catch (Exception ex) {
                this.Logger.Error(ex,"クライアント接続中にエラー発生！");
                this.Logger.Error(ex.ToString());
            }
        }

        public void SendFile(string filename) {
            var fullfilename = System.IO.Path.Combine(Properties.Settings.Default.ReceiveDirectoryPath, filename);
            var filedata = System.IO.File.ReadAllBytes(fullfilename);
            var itm = new BluetoothApplicationTransferData(filename, filedata);
            this.SendFileQueue.Enqueue(itm);

            this.WriteArchiveFile("send",fullfilename);
            System.IO.File.Delete(fullfilename);
        }

        private void RunSend() {
            this.Logger.Info("データ送信処理開始");
            while (RunningFlag) {
                try {
                    BluetoothApplicationTransferData send = null;
                    var ret = this.SendFileQueue.TryDequeue(out send);
                    if (ret) {
                        SocketWriter.WriteBytes(send.GetTransferByteStream());
                        var sendlen = SocketWriter.StoreAsync().GetResults();
                        this.Logger.Info("データ送信結果:{0}", sendlen);
                    }
                    System.Threading.Thread.Sleep(1000);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "データ送信中にエラー発生！");
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

                    this.WriteArchiveFile("recv",filename);
                    lst.Add(recv.Name);
                }
            }

            return lst;
        }

        private void RunReceive() {
            this.Logger.Info("データ受信処理開始");

            while (this.RunningFlag) {
                try {
                    //ファイル名の長さ
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
                    uint filenamelen = SocketReader.ReadUInt32();

                    //ファイル名のデータ
                    loadop = SocketReader.LoadAsync(filenamelen);
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
                    if (size != filenamelen) {
                        break;
                    }
                    byte[] recvfilename = new byte[filenamelen];
                    SocketReader.ReadBytes(recvfilename);
                    string filename = System.Text.Encoding.UTF8.GetString(recvfilename);

                    //ファイルのデータ長
                    loadop = SocketReader.LoadAsync(filenamelen);
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
                    if (size < sizeof(uint)) {
                        break;
                    }
                    uint filedatalen = SocketReader.ReadUInt32();

                    //ファイルのデータ
                    loadop = SocketReader.LoadAsync(filenamelen);
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
                    if (size != filedatalen) {
                        break;
                    }
                    byte[] recvfiledata = new byte[filedatalen];
                    SocketReader.ReadBytes(recvfilename);

                    var recv = new BluetoothApplicationTransferData(filename, recvfiledata);
                    this.Logger.Info("データ受信結果:{0},{1}", recv.Name, recv.Data.Length);
                    this.ReceiveFileQueue.Enqueue(recv);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "データ通信中にエラー発生！");
                    break;
                }
            }
            this.ConnectStatusChanged?.Invoke(this, new AsyncCompletedEventArgs(null, false, BluetoothApplicationConnectStatus.Disconnect));
        }

        public void Stop() {
            this.Logger.Info("Bluetooth RFCOMM サーバ終了開始");

            RunningFlag = false;
            
            Disconnect();

            this.Logger.Info("サーバ終了");
        }

        private void Disconnect() {
            if (SocketListener != null) {
                SocketListener.Dispose();
                SocketListener = null;
            }

            if (RfcommProvider != null) {
                RfcommProvider.StopAdvertising();
                RfcommProvider = null;
            }

            if (SocketListener != null) {
                SocketListener.Dispose();
                SocketListener = null;
            }

            if (SocketWriter != null) {
                SocketWriter.DetachStream();
                SocketWriter = null;
            }

            if (SocketReader != null) {
                SocketReader.DetachStream();
                SocketReader = null;
            }

            if (Socket != null) {
                Socket.Dispose();
                Socket = null;
            }
        }

        public bool IsRunning() {
            return this.RunningFlag;
        }

        private void WriteArchiveFile(string prefix,string filename) {
            var file = new System.IO.FileInfo(filename);
            var arcfilename = $"{DateTime.Now.ToString("yyyyMMddhhmmssfff")}_{prefix}_{file.Name}.bak";
            var arcfullname = System.IO.Path.Combine(Properties.Settings.Default.ArchiveDirectoryPath, arcfilename);
            System.IO.File.Copy(filename, arcfullname);
        }
    }
}
