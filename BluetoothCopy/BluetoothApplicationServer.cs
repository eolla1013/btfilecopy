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
        public event PropertyChangedEventHandler StatusMessageChanged;
        public event AsyncCompletedEventHandler ClientConnectStatusChanged;

        private readonly Guid RfcommChatServiceUuid = Guid.Parse("34B1CF4D-1069-4AD6-89B6-E161D79BE4D8");
        private const string SdpServiceName = "Bluetooth Rfcomm Voice Input Service";
        private NLog.ILogger Logger;
        private StreamSocket Socket;
        private DataWriter SocketWriter;
        private DataReader SocketReader;
        private RfcommServiceProvider RfcommProvider;
        private StreamSocketListener SocketListener;

        private Task SendTask;
        private System.Collections.Concurrent.ConcurrentQueue<string> SendTextQueue;

        private Task ReceiveTask;
        private System.Collections.Concurrent.ConcurrentQueue<string> ReceiveTextQueue;

        private bool RunningFlag;

        public BluetoothApplicationServer() {
            this.Logger = NLog.LogManager.GetLogger("BluetoothApplicationServer");
            this.SendTextQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
            this.ReceiveTextQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
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
            this.StatusMessageChanged?.Invoke("接続待ち", new PropertyChangedEventArgs("StatusMessage"));
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

                this.StatusMessageChanged?.Invoke("クライアント接続中", new PropertyChangedEventArgs("StatusMessage"));
                this.ClientConnectStatusChanged?.Invoke(this, new AsyncCompletedEventArgs(null, false, "Connected"));
            } catch (Exception ex) {
                this.Logger.Error(ex,"クライアント接続中にエラー発生！");
                this.Logger.Error(ex.ToString());
            }
        }

        private void RunReceive() {
            this.Logger.Info("データ受信処理開始");

            while (RunningFlag) {
                try {
                    var loadop = SocketReader.LoadAsync(sizeof(uint));
                    while (loadop.Status != Windows.Foundation.AsyncStatus.Completed) {
                        System.Threading.Thread.Sleep(500);
                        if (!RunningFlag) {
                            break;
                        }
                    }
                    if (!RunningFlag) {
                        break;
                    }
                    uint readlen = loadop.GetResults();
                    this.Logger.Info("データ受信1:" + readlen);

                    if (readlen < sizeof(uint)) {
                        break;
                    }
                    uint datalen = SocketReader.ReadUInt32();
                    this.Logger.Info("データ受信2:" + datalen);

                    loadop = SocketReader.LoadAsync(datalen);
                    while (loadop.Status != Windows.Foundation.AsyncStatus.Completed) {
                        System.Threading.Thread.Sleep(500);
                        if (!RunningFlag) {
                            break;
                        }
                    }
                    if (!RunningFlag) {
                        break;
                    }
                    readlen = loadop.GetResults();
                    this.Logger.Info("データ受信3:" + readlen);

                    if (readlen < datalen) {
                        break;
                    }
                    string message = SocketReader.ReadString(datalen);
                    this.Logger.Info("データ受信");
                    this.Logger.Info(message);
                    this.ReceiveTextQueue.Enqueue(message);
                } catch (Exception ex) {
                    this.Logger.Error(ex,"データ受信中にエラー発生！");
                    this.Logger.Error(ex.ToString());
                    break;
                }
            }

            this.StatusMessageChanged?.Invoke("接続待ち", new PropertyChangedEventArgs("StatusMessage"));
            this.ClientConnectStatusChanged?.Invoke(this, new AsyncCompletedEventArgs(null, false, "Disconnected"));
        }

        private void RunSend() {
            this.Logger.Info("データ送信処理開始");
            while (RunningFlag) {
                try {
                    string msg = "";
                    var ret = this.SendTextQueue.TryDequeue(out msg);
                    if (ret) {
                        this.SendText(msg);
                    }
                    System.Threading.Thread.Sleep(500);
                } catch (Exception ex) {
                    this.Logger.Error(ex, "データ送信中にエラー発生！");
                    this.Logger.Error(ex.ToString());
                    break;
                }
            }
        }

        public void RequestSendText(string msg) {
            if (!string.IsNullOrWhiteSpace(msg)) {
                this.SendTextQueue.Enqueue(msg);
            }
        }

        public void Stop() {
            this.Logger.Info("Bluetooth RFCOMM サーバ終了開始");

            RunningFlag = false;
            
            Disconnect();

            this.Logger.Info("サーバ終了");
            this.StatusMessageChanged?.Invoke("サーバ停止", new PropertyChangedEventArgs("StatusMessage"));
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

        private async void SendText(string msg) {
            try {
                this.Logger.Info("テキスト送信開始");

                //SocketWriter.WriteUInt32((uint)msg.Length);
                //SocketWriter.WriteString(msg);

                var senddata = System.Text.Encoding.UTF8.GetBytes(msg);
                SocketWriter.WriteUInt32((uint)senddata.Length);
                SocketWriter.WriteBytes(senddata);

                await SocketWriter.StoreAsync();

                this.Logger.Info("テキスト送信完了");
            } catch (Exception ex) {
                this.Logger.Error(ex, "データ送信中にエラー発生！");
                this.Logger.Error(ex.ToString());
            }
        }

        public bool IsRunning() {
            return this.RunningFlag;
        }

    }
}
