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
        bool debug = true;
        int heartbeatMiss = 0;

        Socket so;
        ReceiveHandler recvHandler;

        public ServerHandle(Socket s)
        {
            so = s;

            Thread shThread = new Thread(start);
            shThread.Start();
        }

        private void start()
        {
            string remoteHost = ((IPEndPoint)so.RemoteEndPoint).Address.ToString();
            string remotePort = ((IPEndPoint)so.RemoteEndPoint).Port.ToString();
            Console.WriteLine("[Server] Connection established with {0}:{1}\n", remoteHost, remotePort);

            for (;;)
            {
                //=========================Receive==============================
                Header recvHeader;
                Packet recvRequest;

                //========================get HEADER============================
                byte[] headerBytes = getBytes(HEADER_SIZE);
                if (null == headerBytes)
                {
                    ReceiveHandler.RemoveServer(this);
                    break;
                }
                recvHeader = BytesToHeader(headerBytes);
                recvRequest.header = recvHeader;

                //========================get DATA==============================
                byte[] dataBytes = getBytes(recvHeader.size);
                if (null == dataBytes)
                {
                    ReceiveHandler.RemoveServer(this);
                    break;
                }
                recvRequest.data = dataBytes;

                //=================Process Request/Get Response=================
                if (debug) //Receive endpoint
                    Console.WriteLine("\n[Server] {0}:{1}", remoteHost, remotePort);

                ClientHandle surrogateClient;
                recvHandler = new ReceiveHandler(this, recvRequest);
                Packet respPacket = recvHandler.GetResponse(out surrogateClient);

                if (debug) //Send endpoint
                {
                    if (surrogateClient == null)
                        Console.WriteLine("^[Server] {0}:{1}", remoteHost, remotePort);
                    else
                        Console.WriteLine("^[Client] {0}:{1}", ((IPEndPoint)surrogateClient.So.RemoteEndPoint).Address.ToString(), ((IPEndPoint)surrogateClient.So.RemoteEndPoint).Port.ToString());
                }

                //=======================Send Response==========================
                if (-1 != respPacket.header.comm)
                {
                    byte[] respBytes = PacketToBytes(respPacket);
                    bool sendSuccess = false;
                    if (surrogateClient == null)
                        sendSuccess = sendBytes(respBytes);
                    else
                        sendSuccess = sendBytes(surrogateClient.So, respBytes);

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

        public bool Send(Packet p)
        {
            byte[] respBytes = PacketToBytes(p);
            return sendBytes(respBytes);
        }

        public void EchoSend(Packet echoPacket)
        {
            if (debug)
                Console.WriteLine("==SEND: \n" + PacketDebug(echoPacket));
            byte[] echoBytes = PacketToBytes(echoPacket);
            bool echoSuccess = sendBytes(echoBytes);
            if (!echoSuccess)
            {
                string remoteHost = ((IPEndPoint)so.RemoteEndPoint).Address.ToString();
                string remotePort = ((IPEndPoint)so.RemoteEndPoint).Port.ToString();
                Console.WriteLine("FAIL: Relay message to server {0}:{1} failed", remoteHost, remotePort);
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
            if (length != 0) //this check has to exist. otherwise Receive timeouts for 60seconds while waiting for nothing
            {
                try
                {
                    so.ReceiveTimeout = 10000;
                    int bytecount = so.Receive(bytes);

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

                            //puts Comm.SS into 1st and 2nd bytes (COMM)
                            byte[] noRespBytes = BitConverter.GetBytes(Comm.SS);
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
                int bytecount = so.Send(bytes);
            }
            catch (Exception e)
            {
                Console.WriteLine("\n" + e.Message);
                return false;
            }
            return true;
        }

        private bool sendBytes(Socket so, byte[] bytes)
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
