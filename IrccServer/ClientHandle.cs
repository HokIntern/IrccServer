using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ircc;
using static Ircc.IrccHelper;
using StackExchange.Redis;

namespace IrccServer
{
    class ClientHandle
    {
        Socket so;
        int bytecount;

        long userId;
        State state;
        bool isDummy;
        int roomId;
        int chattingCount;

        ReceiveHandler recvHandler;

        public enum State
        {
            Lobby, Room, Error
        }

        public ClientHandle(Socket s, RedisHelper redis)
        {
            so = s;
            Thread chThread = new Thread(() => start(redis));
            chThread.Start();
            //chThread.Abort();
        }

        private void start(RedisHelper redis)
        {
            string remoteHost = ((IPEndPoint)so.RemoteEndPoint).Address.ToString();
            string remotePort = ((IPEndPoint)so.RemoteEndPoint).Port.ToString();
            Console.WriteLine("Connection established with {0}:{1}\n", remoteHost, remotePort);

            for (;;)
            {
                // Receive
                Header recvdHeader;
                Packet recvdRequest;

                // get HEADER
                byte[] headerBytes = getBytes(HEADER_SIZE);
                if (null == headerBytes)
                    break;
                recvdHeader = BytesToHeader(headerBytes);
                recvdRequest.header = recvdHeader;

                // get DATA
                byte[] dataBytes = getBytes(recvdHeader.size);
                if (null == dataBytes)
                    break;
                recvdRequest.data = dataBytes;

                recvHandler = new ReceiveHandler(this, recvdRequest, redis);
                Packet respPacket = recvHandler.GetResponse();
                byte[] respBytes = PacketToBytes(respPacket);
                bool sendSuccess = sendBytes(respBytes);
                if (!sendSuccess)
                {
                    Console.WriteLine("Send failed.");
                    break;
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

        private byte[] getBytes(int length)
        {
            byte[] bytes = new byte[length];
            try
            {
                bytecount = so.Receive(bytes);
            }
            catch (Exception e)
            {
                Console.WriteLine("\n" + e.Message);
                return null;
            }

            return bytes;
        }

        private bool sendBytes(byte[] bytes)
        {
            try
            {
                bytecount = so.Send(bytes);
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
