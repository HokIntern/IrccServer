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

        private long userId;
        private State status;
        private bool isDummy;
        private long roomId;
        private int chatCount;
        ReceiveHandler recvHandler;

        public long UserId { get { return userId; } }
        public State Status { get { return status; } set { status = value; } }
        public bool IsDummy { get { return isDummy; } }
        public long RoomId { get { return roomId; } set { roomId = value; } }
        public int ChatCount { get { return chatCount; }  set { chatCount = value; } }


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
            Console.WriteLine("[Client] Connection established with {0}:{1}\n", remoteHost, remotePort);

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

                recvHandler = new ReceiveHandler(this, recvRequest, redis);
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

        public void EchoSend(Packet echoPacket)
        {
            byte[] echoBytes = PacketToBytes(echoPacket);
            bool echoSuccess = sendBytes(echoBytes);
            if (!echoSuccess)
            {
                Console.WriteLine("FAIL: Relay message to client {0} failed", userId);
                /*
                Console.WriteLine("Closing connection with {0}:{1}", remoteHost, remotePort);
                so.Shutdown(SocketShutdown.Both);
                so.Close();
                Console.WriteLine("Connection closed\n");
                */
            }
        }

        private byte[] getBytes(int length)
        {
            byte[] bytes = new byte[length];
            try
            {
                so.ReceiveTimeout = 30000;
                bytecount = so.Receive(bytes);
            }
            catch (Exception e)
            {
                if (!isConnected())
                {
                    Console.WriteLine("\n" + e.Message);
                    return null;
                }
                else
                {
                    if (bytes.Length != 0)
                    {
                        //puts Comm.CS into 1st and 2nd bytes (COMM)
                        byte[] noRespBytes = BitConverter.GetBytes(Comm.CS);
                        bytes[0] = noRespBytes[0];
                        bytes[1] = noRespBytes[1];
                        //puts -1 bytes into 3rd and 4th bytes (CODE)
                        noRespBytes = BitConverter.GetBytes((short)-1);
                        bytes[2] = noRespBytes[0];
                        bytes[3] = noRespBytes[1];
                    }
                }
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
