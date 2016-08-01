using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Ircc;
using static Ircc.IrccHelper;
using StackExchange.Redis;

namespace IrccServer
{
    class ReceiveHandler
    {
        ClientHandle client;
        Packet recvPacket;
        RedisHelper redis;
        static List<ClientHandle> clients;
        static HashSet<Room> rooms;

        public ReceiveHandler()
        {
            clients = new List<ClientHandle>();
            rooms = new HashSet<Room>();
        }

        public ReceiveHandler(ClientHandle client, Packet recvPacket, RedisHelper redis)
        {
            this.client = client;
            this.recvPacket = recvPacket;
            this.redis = redis;

            clients.Add(client);
        }

        //public Packet PacketHandler()
        public Packet GetResponse()
        {
            Packet returnPacket;
            Header returnHeader = new Header();
            byte[] returnData = null;

            //Client to Server side
            if (Comm.CS == recvPacket.header.comm)
            {
                byte[] usernameBytes = new byte[12];
                byte[] passwordBytes = new byte[18];
                string username;
                string password;
                long userId;
                
                switch (recvPacket.header.code)
                {
                    //------------CREATE------------
                    case Code.CREATE:
                        //CL -> FE side
                        break;
                    case Code.CREATE_DUPLICATE_ERR:
                        //FE -> CL side
                        break;
                    case Code.CREATE_FULL_ERR:
                        //FE -> CL side
                        break;


                    //------------DESTROY------------
                    case Code.DESTROY:
                        //CL -> FE side
                        break;
                    case Code.DESTROY_ERR:
                        //FE -> CL side
                        break;


                    //------------FAIL------------
                    case Code.FAIL:
                        //
                        break;


                    //------------HEARTBEAT------------
                    case Code.HEARTBEAT:
                        //FE -> CL side
                        break;
                        /*
                    case Code.HEARTBEAT_RES:
                        //CL -> FE side
                        break;
                        */


                    //------------JOIN------------
                    case Code.JOIN:
                        roomIdBytes = new byte[xxx];
                        Array.Copy(recvPacket.data, 0, roomIdBytes, 0, xxx);
                        roomId = roomIdBytes.toInt;

                        client.state = ClientHandle.State.Room;
                        client.roomId = roomId;

                        returnHeader = new Header(Comm.CS, Code.JOIN_RES, 0);
                        returnData = null;
                        //CL -> FE side
                        break;
                    case Code.JOIN_FULL_ERR:
                        //FE -> CL side
                        break;
                    case Code.JOIN_NULL_ERR:
                        //FE -> CL side
                        break;


                    //------------LEAVE------------
                    case Code.LEAVE:
                        //CL -> FE side
                        //remove clienthandle from room.clients
                        //check if clienthandle is empty
                        //if empty, send SDESTROY to servers in room.servers
                        break;
                    case Code.LEAVE_ERR:
                        //FE -> CL side
                        break;


                    //------------LIST------------
                    case Code.LIST:
                        //CL -> FE side
                        break;
                    case Code.LIST_ERR:
                        //FE -> CL side
                        break;
                    case Code.LIST_RES:
                        //FE -> CL side
                        break;


                    //------------MSG------------
                    case Code.MSG:
                        //CL <--> FE side
                        roomIdBytes = new byte[xxx];
                        msgBytes = new BytesToHeader[xxx];
                        Array.Copy(recvPacket.data, 0, roomIdBytes, 0, xxx);
                        Array.Copy(recvPacket.data, 0, msgBytes, 0, xxx);
                        roomId = roomIdBytes.toInt;
                        msg = UTF8Encoding.GetEncoding.getstring(msgBytes);

                        lock (rooms)
                        {
                            foreach user as rooms.getHash(client.roomId).clients
                            {
                                //make packet and send
                            }
                            foreach server as rooms.getHash(client.roomId).servers
                            {
                                //make packet and send
                            }
                        }
                        break;
                    case Code.MSG_ERR:
                        //CL <--> FE side
                        break;


                    //------------SIGNIN------------
                    case Code.SIGNIN:
                        //CL -> FE -> BE side
                        usernameBytes = new byte[12];
                        passwordBytes = new byte[18];
                        Array.Copy(recvPacket.data, 0, usernameBytes, 0, 12);
                        Array.Copy(recvPacket.data, 12, passwordBytes, 0, 18);

                        username = Encoding.UTF8.GetString(usernameBytes).Trim();
                        password = Encoding.UTF8.GetString(passwordBytes).Trim();
                        userId = redis.SignIn(username, password);
                        if (-1 == userId)
                        {
                            returnHeader = new Header(Comm.CS, Code.SIGNIN_ERR, 0);
                            returnData = null;
                        }
                        else
                        {
                            returnHeader = new Header(Comm.CS, Code.SIGNIN_RES, 0);
                            returnData = null;
                        }
                        break;
                    case Code.SIGNIN_ERR:
                        //BE -> FE -> CL side
                        break;
                    case Code.SIGNIN_RES:
                        //BE -> FE -> CL side
                        break;


                    //------------SIGNUP------------
                    case Code.SIGNUP:
                        //CL -> FE -> BE side
                        usernameBytes = new byte[12];
                        passwordBytes = new byte[18];
                        Array.Copy(recvPacket.data, 0, usernameBytes, 0, 12);
                        Array.Copy(recvPacket.data, 12, passwordBytes, 0, 18);

                        username = Encoding.UTF8.GetString(usernameBytes).Trim();
                        password = Encoding.UTF8.GetString(passwordBytes).Trim();
                        userId = redis.CreateUser(username, password);

                        if (-1 == userId)
                        {
                            returnHeader = new Header(Comm.CS, Code.SIGNUP_ERR, 0);
                            returnData = null;
                        }
                        else
                        {
                            returnHeader = new Header(Comm.CS, Code.SIGNUP_RES, 0);
                            returnData = null;
                        }
                        break;
                    case Code.SIGNUP_ERR:
                        //BE -> FE -> CL side
                        //error handling
                        break;
                    case Code.SIGNUP_RES:
                        //BE -> FE -> CL side
                        //success
                        break;


                    //------------SUCCESS------------
                    case Code.SUCCESS:
                        //
                        break;
                }
            }
            //Server to Server Side
            else if (Comm.SS == recvPacket.header.comm)
            {
                switch (recvPacket.header.code)
                {
                    //------------SDESTROY------------
                    case Code.SDESTROY:
                        //FE side
                        break;
                    case Code.SDESTROY_ERR:
                        //FE side
                        break;


                    //------------SJOIN------------
                    case Code.SJOIN:
                        //FE side
                        break;
                    case Code.SJOIN_ERR:
                        //FE side
                        break;


                    //------------SLIST------------
                    case Code.SLIST:
                        //FE side
                        break;
                    case Code.SLIST_ERR:
                        //FE side
                        break;
                        /*
                    case Code.SLIST_RES:
                        //FE side
                        break;
                        */


                    //------------SMSG------------                
                    case Code.SMSG:
                        //FE side
                        break;
                    case Code.SMSG_ERR:
                        //FE side
                        break;
                }
            }
            //Dummy to Server Side
            else if (Comm.DUMMY == recvPacket.header.comm)
            {

            }
            returnPacket = new Packet(returnHeader, returnData);
      
            return returnPacket;
            /*
            switch (packet.header.code)
            {

                // ReceiveHandler recvHandler = new ReceiveHandler(Packet, this);
                // ReceiveHandler.Join(this)
                // this.state = State.Room
                // this.roomId = xxxx
                case Code.SIGNUP:
                    byte[] usernameBytes = new byte[12];
                    byte[] passwordBytes = new byte[18];
                    Array.Copy(packet.data, 0, usernameBytes, 0, 12);
                    Array.Copy(packet.data, 12, passwordBytes, 0, 18);

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
                default:
                    break;
            }
            */
        }
    }
}
