using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ircc;

namespace IrccServer
{
    class ClientHandle
    {
        Socket so;
        int bytecount;


        long uuid;
        int status;
        int roomId;
        int chattingCount;

        const int HEADER_SIZE = 12;

        public ClientHandle(Socket s)
        {
            so = s;
            Thread chThread = new Thread(echo);
            chThread.Start();
        }

        private void echo()
        {
            String remoteHost = ((IPEndPoint)so.RemoteEndPoint).Address.ToString();
            String remotePort = ((IPEndPoint)so.RemoteEndPoint).Port.ToString();
            Console.WriteLine("Connection established with {0}:{1}\n", remoteHost, remotePort);

            for (;;)
            {
                /* Receive */
                Header recvdHeader;
                Packet recvdRequest;
                
                /* get HEADER */
                byte[] header = new byte[HEADER_SIZE];
                bytecount = so.Receive(header);
                recvdHeader = IrccHelper.bytesToHeader(header);
                recvdRequest.header = recvdHeader;

                /* get DATA */
                byte[] data = new byte[recvdHeader.size];
                bytecount = so.Receive(data);
                recvdRequest.data = data;

                Console.WriteLine("Received {0}bytes from {1}:{2}", bytecount, remoteHost, remotePort);
                Console.WriteLine("COMM: {0}\nCODE: {1}\nSIZE: {2}\nRSVD: {3}\nDATA: {4}", recvdRequest.header.comm, recvdRequest.header.code, recvdRequest.header.size, recvdRequest.header.reserved, Encoding.UTF8.GetString(recvdRequest.data));

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
