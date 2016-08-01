using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net.Sockets;
using System.Net;
using System.Threading;

namespace IrccServer
{
    class ServerHandle
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
