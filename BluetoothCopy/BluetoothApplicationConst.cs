using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BluetoothCopy
{
    enum BluetoothApplicationConnectStatus
    {
        Connect,
        Disconnect,
        Error
    }

    class BluetoothApplicationTransferData
    {
        public string Name { get; private set; }
        public byte[] Data { get; private set; }

        public BluetoothApplicationTransferData(string name,byte[] data) {
            this.Name = name;
            this.Data = data;
        }

        public byte[] GetTransferByteStream() {
            var stream = new List<byte>();
            var filename = System.Text.Encoding.UTF8.GetBytes(this.Name);
            var filenamelen = BitConverter.GetBytes((uint)filename.Length);
            var datalen= BitConverter.GetBytes((uint)this.Data.Length);

            stream.AddRange(filenamelen);
            stream.AddRange(filename);
            stream.AddRange(datalen);
            stream.AddRange(this.Data);
            return stream.ToArray();
        }
    }

}
