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
        State status;
        bool isDummy;
        int roomId;
        int chattingCount;

        private enum State
        {
            Lobby, Room, Error
        }

        public ClientHandle(Socket s)
        {
            so = s;
            Thread chThread = new Thread(start);
            chThread.Start();
            //chThread.Abort();
        }

        private void start()
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
                recvdHeader = bytesToHeader(headerBytes);
                recvdRequest.header = recvdHeader;

                // get DATA
                byte[] dataBytes = getBytes(recvdHeader.size);
                if (null == dataBytes)
                    break;
                recvdRequest.data = dataBytes;

                //string configString = "10.100.58.5:26379,keepAlive=180";
                string configString = System.IO.File.ReadAllText("redis.conf");
                ConfigurationOptions configOptions = ConfigurationOptions.Parse(configString);
                RedisHelper redis = new RedisHelper(configOptions);

                //TODO: fix this shit
                switch (recvdRequest.header.code)
                {
                    case Code.SIGNUP:
                        byte[] usernameBytes = new byte[12];
                        byte[] passwordBytes = new byte[18];
                        Array.Copy(recvdRequest.data, 0, usernameBytes, 0, 12);
                        Array.Copy(recvdRequest.data, 12, passwordBytes, 0, 18);

                        string username = Encoding.UTF8.GetString(usernameBytes).Trim();
                        string password = Encoding.UTF8.GetString(passwordBytes).Trim();

                        userId = redis.CreateUser(username, password);
                        break;
                    case Code.SIGNIN:
                        usernameBytes = new byte[12];
                        passwordBytes = new byte[18];
                        Array.Copy(recvdRequest.data, 0, usernameBytes, 0, 12);
                        Array.Copy(recvdRequest.data, 0, passwordBytes, 0, 18);

                        username = Encoding.UTF8.GetString(usernameBytes).Trim();
                        password = Encoding.UTF8.GetString(passwordBytes).Trim();

                        userId = redis.SignIn(username, password);
                        Console.WriteLine(userId + "has signed in");
                        break;
                    default :
                        break;
                }

                /* //DEBUG
                Console.WriteLine("Received {0}bytes from {1}:{2}", bytecount, remoteHost, remotePort);
                Console.WriteLine("COMM: {0}\nCODE: {1}\nSIZE: {2}\nRSVD: {3}\nDATA: {4}", recvdRequest.header.comm, recvdRequest.header.code, recvdRequest.header.size, recvdRequest.header.reserved, Encoding.UTF8.GetString(recvdRequest.data));
                */

                if (!isConnected())
                {
                    Console.WriteLine("Connection lost with {0}:{1}", remoteHost, remotePort);
                    break;
                }
                
                /* Echo */
                /*
                String ss = Encoding.UTF8.GetString(bytes).Substring(0, bytecount);
                byte[] sendBytes = Encoding.UTF8.GetBytes(ss);
                bytecount = so.Send(sendBytes);
                //Console.WriteLine("Sent     {0}bytes to {1}:{2} - {3} \n", bytecount, remoteHost, remotePort, Encoding.UTF8.GetString(bytes));
                */
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
