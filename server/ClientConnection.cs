using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace MIS_Statements_Server {
    class ClientConnection {
        public Socket socket { get; private set; }
        public byte clientType { get; private set; } // 0 - Ошибка соединения 1 - Viewer, 2 - Client
        public string mac { get; private set; }
        public string query { get; set; }

        public ClientConnection(Socket _socket)
        {
            socket = _socket;
            byte[] buffer = new byte[2000];
            try {
                _socket.Receive(buffer);
                clientType = buffer[0];
                mac = Encoding.UTF8.GetString(buffer, 1, 12);
                query = Encoding.UTF8.GetString(buffer, 13, Array.IndexOf(buffer, (byte)0) - 13);
            }
            catch {
                clientType = 0;
                mac = "";
                query = "";
            }
        }

        public static bool operator !=(ClientConnection cc1, ClientConnection cc2)
        {
            if (!(cc1.socket.RemoteEndPoint as IPEndPoint).Address.Equals((cc2.socket.RemoteEndPoint as IPEndPoint).Address)
                || cc1.clientType != cc2.clientType || cc1.mac != cc2.mac)
                return true;
            else
                return false;
        }

        public static bool operator ==(ClientConnection cc1, ClientConnection cc2)
        {
            if ((cc1.socket.RemoteEndPoint as IPEndPoint).Address.Equals((cc2.socket.RemoteEndPoint as IPEndPoint).Address) 
                && cc1.clientType == cc2.clientType && cc1.mac == cc2.mac)
                return true;
            else
                return false;
        }
    }
}
