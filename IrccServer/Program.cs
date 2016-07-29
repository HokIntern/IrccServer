using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Windows;
using Ircc;

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

            /* if only given port, host is ANY */
            echos = new TcpServer(host, port);

            while (true)
            {
                s1 = echos.so.Accept();
                ClientHandle client = new ClientHandle(s1);
            }
        }
    }
}
