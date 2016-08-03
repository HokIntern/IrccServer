using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using Ircc;
using static Ircc.IrccHelper;

namespace IrccServer
{
    class ServerHandle
    {
        Socket so;
        ReceiveHandler recvHandler;

        public ServerHandle(Socket s, string status)
        {
            so = s;

            if("amserver" == status)
            {
                Thread shThread = new Thread(start);
                shThread.Start();
            }
        }

        private void start()
        {
            string remoteHost = ((IPEndPoint)so.RemoteEndPoint).Address.ToString();
            string remotePort = ((IPEndPoint)so.RemoteEndPoint).Port.ToString();
            Console.WriteLine("Connection established with {0}:{1}\n", remoteHost, remotePort);

            for (;;)
            {
                // Receive
                Header recvHeader;
                Packet recvRequest;

                // get HEADER
                byte[] headerBytes = getBytes(HEADER_SIZE);
                if (null == headerBytes)
                    break;
                else
                {
                    recvHeader = BytesToHeader(headerBytes);
                    recvRequest.header = recvHeader;
                }

                //if (headerBytes.Length != HEADER_SIZE && headerBytes[0] == byte.MaxValue)

                recvHeader = BytesToHeader(headerBytes);
                recvRequest.header = recvHeader;

                // get DATA
                byte[] dataBytes = getBytes(recvHeader.size);
                if (null == dataBytes)
                    break;
                recvRequest.data = dataBytes;

                recvHandler = new ReceiveHandler(recvRequest);
                Packet respPacket = recvHandler.GetResponse();
                if (-1 != respPacket.header.comm)
                {
                    byte[] respBytes = PacketToBytes(respPacket);
                    bool sendSuccess = sendBytes(respBytes);
                    if (!sendSuccess)
                    {
                        Console.WriteLine("Send failed.");
                        break;
                    }
                }

                if (!isConnected())
                {
                    Console.WriteLine("Connection lost with {0}:{1}", remoteHost, remotePort);
                    break;
                }
            }
            Console.WriteLine("Closing connection with {0}:{1}", remoteHost, remotePort);
            so.Shutdown(SocketShutdown.Both);
            so.Close();
            Console.WriteLine("Connection closed\n");
        }

        public bool Send(Packet p)
        {
            byte[] respBytes = PacketToBytes(p);
            return sendBytes(respBytes);
        }

        public Packet Receive()
        {
            // Receive
            Header recvHeader;
            Packet recvRequest = new Packet();

            // get HEADER
            byte[] headerBytes = getBytes(HEADER_SIZE);
            if (null == headerBytes)
                return recvRequest;
            else
            {
                recvHeader = BytesToHeader(headerBytes);
                recvRequest.header = recvHeader;
            }

            //if (headerBytes.Length != HEADER_SIZE && headerBytes[0] == byte.MaxValue)

            recvHeader = BytesToHeader(headerBytes);
            recvRequest.header = recvHeader;

            // get DATA
            byte[] dataBytes = getBytes(recvHeader.size);
            if (null == dataBytes)
                return recvRequest;
            recvRequest.data = dataBytes;

            return recvRequest;
        }

        private byte[] getBytes(int length)
        {
            byte[] bytes = new byte[length];
            try
            {
                so.ReceiveTimeout = 10000;
                int bytecount = so.Receive(bytes);
            }
            catch (Exception e)
            {
                return null;
            }

            return bytes;
        }

        private bool sendBytes(byte[] bytes)
        {
            try
            {
                int bytecount = so.Send(bytes);
            }
            catch (Exception e)
            {
                Console.WriteLine("\n" + e.Message);
                return false;
            }
            return true;
        }

        private bool isConnected()
        {
            try
            {
                return !(so.Poll(1, SelectMode.SelectRead) && so.Available == 0);
            }
            catch (SocketException) { return false; }
        }
    }
}
