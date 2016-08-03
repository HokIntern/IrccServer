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
            //HashSet<Room> rooms = new HashSet<Room>();    //room_id to Room instance mapping
            //HashSet<ClientHandle> clients = new HashSet<ClientHandle>();    //user_id to state,room_id mapping
            //List<ClientHandle> clientThreads = new List<ClientHandle>();

            /* if only given port, host is ANY */
            echoc = new TcpServer(host, clientPort);
            echos = new TcpServer(host, serverPort);

            //string configString = "10.100.58.5:26379,keepAlive=180";
            Console.WriteLine("Connecting to Redis...");
            string configString = System.IO.File.ReadAllText("redis.conf");
            ConfigurationOptions configOptions = ConfigurationOptions.Parse(configString);
            RedisHelper redis = new RedisHelper(configOptions);

            Console.WriteLine("Initializing lobby and rooms...");
            ReceiveHandler recvHandler = new ReceiveHandler();

            Console.WriteLine("Connecting to other IRC servers...");
            string[] peerInfo = System.IO.File.ReadAllLines("peer_info.conf");
            recvHandler.SetPeerServers(peerInfo);

            //thread for accepting clients
            Thread chThread = new Thread(() => AcceptClient(echoc, redis));
            chThread.Start();

            //thread for accepting servers
            Thread shThread = new Thread(() => AcceptServer(echos, recvHandler));
            shThread.Start();
                
            //ClientHandle client = new ClientHandle(s1, ref redis); // ref because if they have to share the same connection
            //clients.Add(client);
        }

        static void AcceptClient(TcpServer echoc, RedisHelper redis)
        {
            while (true)
            {
                Socket s = echoc.so.Accept();
                ClientHandle client = new ClientHandle(s, redis);
            }
        }

        static void AcceptServer(TcpServer echos, ReceiveHandler recvHandler)
        {
            while (true)
            {
                Socket s = echos.so.Accept();
                ServerHandle server = new ServerHandle(s, "amserver");

                recvHandler.AddPeerServer(server);
            }
        }
    }
}
