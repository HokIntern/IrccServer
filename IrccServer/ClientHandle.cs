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
        bool debug = true;

        Socket so;
        RedisHelper redis;
        int bytecount;
        int heartbeatMiss = 0;

        private long userId;
        private State status;
        private bool isDummy;
        private long roomId;
        private int chatCount;
        ReceiveHandler recvHandler;

        public Socket So { get { return so; } }
        public long UserId { get { return userId; } set { userId = value; } }
        public State Status { get { return status; } set { status = value; } }
        public bool IsDummy { get { return isDummy; } set { isDummy = value; } }
        public long RoomId { get { return roomId; } set { roomId = value; } }
        public int ChatCount { get { return chatCount; }  set { chatCount = value; } }


        public enum State
        {
            Online, Offline, Lobby, Room, Error
        }

        public ClientHandle(Socket s, RedisHelper redis)
        {
            so = s;
            this.redis = redis;
            this.status = State.Online;
            Thread chThread = new Thread(() => start(redis));
            chThread.Start();
        }

        private void start(RedisHelper redis)
        {
            string remoteHost = ((IPEndPoint)so.RemoteEndPoint).Address.ToString();
            string remotePort = ((IPEndPoint)so.RemoteEndPoint).Port.ToString();
            Console.WriteLine("[Client] Connection established with {0}:{1}\n", remoteHost, remotePort);

            for (;;)
            {
                //=========================Receive==============================
                Header recvHeader;
                Packet recvRequest;

                //========================get HEADER============================
                byte[] headerBytes = getBytes(HEADER_SIZE);
                if (null == headerBytes)
                {
                    signout();               
                    break;
                }
                recvHeader = BytesToHeader(headerBytes);
                recvRequest.header = recvHeader;

                //========================get DATA==============================
                byte[] dataBytes = getBytes(recvHeader.size);
                if (null == dataBytes)
                {
                    signout();
                    break;
                }
                recvRequest.data = dataBytes;

                //=================Process Request/Get Response=================
                if (debug) //Receive endpoint
                    Console.WriteLine("\n[Client] {0}:{1}", remoteHost, remotePort);

                ClientHandle surrogateClient;
                recvHandler = new ReceiveHandler(this, recvRequest, redis);
                Packet respPacket = recvHandler.GetResponse(out surrogateClient);

                if (debug) //Send endpoint
                    Console.WriteLine("^[Client] {0}:{1}", remoteHost, remotePort);

                //=======================Send Response==========================
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

                //=======================Check Connection=======================
                if (!isConnected())
                {
                    Console.WriteLine("Connection lost with {0}:{1}", remoteHost, remotePort);
                    break;
                }
            }
            //=================Close Connection/Exit Thread=====================
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

        private void signout()
        {
            if (status == State.Lobby || status == State.Room)
            {
                redis.SignOut(this.userId);
                ReceiveHandler.RemoveClient(this);
                this.roomId = 0;
            }
            this.status = State.Offline;
        }

        private byte[] getBytes(int length)
        {
            byte[] bytes = new byte[length];
            if (length != 0) //this check has to exist. otherwise Receive timeouts for 60seconds while waiting for nothing
            {
                try
                {
                    so.ReceiveTimeout = 60000;
                    bytecount = so.Receive(bytes);

                    //assumes that the line above(so.Receive) will throw exception 
                    //if times out, so the line below(reset hearbeatMiss) will not be reached
                    //if an exception is thrown.
                    heartbeatMiss = 0;
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
                            heartbeatMiss++;
                            if (heartbeatMiss == 2)
                                return null;

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
