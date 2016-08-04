using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Windows;
using Ircc;
using StackExchange.Redis;
using System.Threading;

namespace IrccServer
{
    class Program
    {
        static void Main(string[] args)
        {
            string host = null;     //Default
            string clientPort = "30000";  //Default
            string serverPort = "40000";  //Default
            TcpServer echoc;
            TcpServer echos;

            //=========================GET ARGS=================================
            if(args.Length == 0)
            {
                Console.WriteLine("Format: IrccServer -cp [client port] -sp [server port]");
                Environment.Exit(0);
            }

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--help")
                {
                    Console.WriteLine("Format: IrccServer -cp [client port] -sp [server port]");
                    Environment.Exit(0);
                }
                else if (args[i] == "-cp")
                    clientPort = args[++i];
                else if (args[i] == "-sp")
                    serverPort = args[++i];
                else
                {
                    Console.Error.WriteLine("ERROR: incorrect inputs \nFormat: IrccServer -cp [client port] -sp [server port]");
                    Environment.Exit(0);
                }
            }

            //======================SOCKET BIND/LISTEN==========================
            /* if only given port, host is ANY */
            echoc = new TcpServer(host, clientPort);
            echos = new TcpServer(host, serverPort);

            //======================REDIS CONNECT===============================
            //string configString = "10.100.58.5:26379,keepAlive=180";
            Console.WriteLine("Connecting to Redis...");
            string configString = System.IO.File.ReadAllText("redis.conf");
            ConfigurationOptions configOptions = ConfigurationOptions.Parse(configString);
            RedisHelper redis = new RedisHelper(configOptions);
            if(!redis.IsConnected())
            {
                Console.WriteLine("\nFailed to connect to Redis\nExiting...");
                Environment.Exit(0);
            }

            //======================INITIALIZE==================================
            Console.WriteLine("Initializing lobby and rooms...");
            ReceiveHandler recvHandler = new ReceiveHandler();

            //=====================CONNECT TO PEERS=============================
            Console.WriteLine("Connecting to other IRC servers...");
            string[] peerInfo = System.IO.File.ReadAllLines("peer_info.conf");
            bool havePeers = recvHandler.SetPeerServers(peerInfo);
            if (!havePeers)
            {
                bool success = redis.Reset();
                if (!success)
                {
                    Console.WriteLine("Redis reinitialize failed. Exiting...");
                    Environment.Exit(0);
                }
            }
            

            //================SERVER/CLIENT SOCKET ACCEPT=======================
            //thread for accepting clients
            Thread chThread = new Thread(() => AcceptClient(echoc, redis));
            chThread.Start();

            //thread for accepting servers
            Thread shThread = new Thread(() => AcceptServer(echos, recvHandler));
            shThread.Start();
        }

        static void AcceptClient(TcpServer echoc, RedisHelper redis)
        {
            while (true)
            {
                Socket s = echoc.so.Accept();
                ClientHandle client = new ClientHandle(s, redis);
                //'client' is not added to lobbyClient because
                //lobby is for clients that signed in
            }
        }

        static void AcceptServer(TcpServer echos, ReceiveHandler recvHandler)
        {
            while (true)
            {
                Socket s = echos.so.Accept();
                ServerHandle server = new ServerHandle(s);

                recvHandler.AddPeerServer(server);
            }
        }
    }
}
