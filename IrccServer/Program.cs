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
            String host = null;     //Default
            String port = "30000";  //Default
            Socket s1;
            TcpServer echos;
            //HashSet<Room> rooms = new HashSet<Room>();    //room_id to Room instance mapping
            //HashSet<ClientHandle> clients = new HashSet<ClientHandle>();    //user_id to state,room_id mapping
            //List<ClientHandle> clientThreads = new List<ClientHandle>();

            /* if only given port, host is ANY */
            echos = new TcpServer(host, port);

            //string configString = "10.100.58.5:26379,keepAlive=180";
            Console.WriteLine("Connecting to Redis...");
            string configString = System.IO.File.ReadAllText("redis.conf");
            ConfigurationOptions configOptions = ConfigurationOptions.Parse(configString);
            RedisHelper redis = new RedisHelper(configOptions);

            Console.WriteLine("Initializing lobby and rooms...");
            ReceiveHandler recvHandler = new ReceiveHandler();

            while (true)
            {
                s1 = echos.so.Accept();
                ClientHandle client = new ClientHandle(s1, redis);
                //ClientHandle client = new ClientHandle(s1, ref redis); // ref because if they have to share the same connection
                //clients.Add(client);
            }
        }

        public class ServerHandle
        {
            Socket so;
            public ServerHandle(Socket s)
            {
                so = s;
                Thread chThread = new Thread(start);
                chThread.Start();
            }

            private void start()
            {
            }
        }
    }
}
