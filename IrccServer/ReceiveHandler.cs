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
        ServerHandle server;
        Packet recvPacket;
        RedisHelper redis;
        static List<ClientHandle> lobbyClients;
        static List<ServerHandle> peerServers;
        static Dictionary<long, Room> rooms;
        Header NoResponseHeader = new Header(-1, 0, 0);

        public ReceiveHandler()
        {
            lobbyClients = new List<ClientHandle>();
            peerServers = new List<ServerHandle>();
            rooms = new Dictionary<long, Room>();
        }

        public ReceiveHandler(ClientHandle client, Packet recvPacket, RedisHelper redis)
        {
            this.client = client;
            this.recvPacket = recvPacket;
            this.redis = redis;

            lobbyClients.Add(client);
        }

        public ReceiveHandler(ServerHandle server, Packet recvPacket)
        {
            this.server = server;
            this.recvPacket = recvPacket;
        }

        public void SetPeerServers(string[] peerInfo)
        {
            foreach (string peerAddress in peerInfo)
            {
                Socket so = ConnectToPeer(peerAddress);
                if (null != so)
                {
                    ServerHandle peer = new ServerHandle(so, "amclient");
                    peerServers.Add(peer);
                }
            }
        }

        public void AddPeerServer(ServerHandle peer)
        {
            if(!peerServers.Contains(peer))
                peerServers.Add(peer);
        }

        //public Packet PacketHandler()
        public Packet GetResponse()
        {
            Packet returnPacket;
            Header returnHeader = new Header();
            byte[] returnData = null;

            bool debug = true;

            if(debug)
                Console.WriteLine("==RECEIVED: \n" + PacketDebug(recvPacket));

            if(-1 == recvPacket.header.comm)
            {
                //------------No action from client----------
                returnHeader = new Header(Comm.CS, Code.HEARTBEAT, 0);
                returnData = null;
            }
            //Client to Server side
            else if (Comm.CS == recvPacket.header.comm)
            {
                byte[] roomnameBytes;
                byte[] roomIdBytes;
                byte[] usernameBytes;
                byte[] passwordBytes;
                string username;
                string password;
                string roomname;
                long userId;
                long roomId = 0;
                bool createAndJoin = false;
                Room requestedRoom;

                switch (recvPacket.header.code)
                {
                    //------------CREATE------------
                    case Code.CREATE:
                        //CL -> FE side
                        // bytes to roomname
                        int dataSize = recvPacket.header.size;
                        roomnameBytes = new byte[dataSize];
                        Array.Copy(recvPacket.data, 0, roomnameBytes, 0, dataSize);

                        roomname = Encoding.UTF8.GetString(roomnameBytes).Trim();
                        roomId = redis.CreateRoom(roomname);

                        if (-1 == roomId)
                        {
                            //make packet for room duplicate
                            returnHeader = new Header(Comm.CS, Code.CREATE_DUPLICATE_ERR, 0);
                            returnData = null;
                            break;
                        }
                        else
                        {
                            //add room to dictionary
                            requestedRoom = new Room(roomId);
                            lock(rooms)
                                rooms.Add(roomId, requestedRoom);

                            //make packet for room create success
                            roomIdBytes = BitConverter.GetBytes(roomId);
                            returnHeader = new Header(Comm.CS, Code.CREATE_RES, roomIdBytes.Length);
                            returnData = roomIdBytes;

                            createAndJoin = true;
                            goto case Code.JOIN;
                        }
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
                    case Code.HEARTBEAT_RES:
                        //CL -> FE side
                        returnHeader = NoResponseHeader;
                        returnData = null;
                        break;


                    //------------JOIN------------
                    case Code.JOIN:
                        //CL -> FE side
                        if (createAndJoin)
                            createAndJoin = false; //reuse roomId already set and set flag to false
                        else
                            roomId = ToInt64(recvPacket.data, 0);
                        
                        //TODO: allow client to join other room while in another room?
                        //      or just allow to join when client is in lobby?
                        
                        lock (rooms)
                        {
                            if (!rooms.TryGetValue(roomId, out requestedRoom))
                            {
                                byte[] sreqData = BitConverter.GetBytes(roomId); ;
                                Header sreqHeader = new Header(Comm.SS, Code.SJOIN, sreqData.Length);
                                Packet sreqPacket = new Packet(sreqHeader, sreqData);

                                //room not in local server. check other servers
                                bool roomExists = false;
                                foreach (ServerHandle peer in peerServers)
                                {
                                    Packet srespPacket;
                                    
                                    bool success = peer.Send(sreqPacket);
                                    if (success)
                                    {
                                        srespPacket = peer.Receive();
                                        if(Code.SJOIN_ERR == srespPacket.header.code)
                                        {
                                            continue;
                                        }
                                        else if(Code.SJOIN_RES == srespPacket.header.code)
                                        {
                                            roomExists = true;
                                            long recvRoomId = ToInt64(srespPacket.data, 0);

                                            if (recvRoomId != roomId)
                                                Console.WriteLine("JOIN and SJOIN room id's don't match. you fucked up.");
                                            else
                                            {
                                                if(!rooms.TryGetValue(roomId, out requestedRoom))
                                                {
                                                    //first SJOIN_RES from peer servers
                                                    Room newJoinRoom = new Room(roomId);
                                                    lobbyClients.Remove(client);
                                                    newJoinRoom.AddClient(client);
                                                    newJoinRoom.AddServer(peer);

                                                    client.Status = ClientHandle.State.Room;
                                                    client.RoomId = roomId;
                                                }
                                                else
                                                {
                                                    //room already made by previous iteration
                                                    requestedRoom.AddServer(peer);
                                                }
                                            }
                                        }
                                    }
                                }

                                if (roomExists)
                                {
                                    //room exists in other peer irc servers
                                    //created one in local and connected with peers
                                    roomIdBytes = BitConverter.GetBytes(roomId);
                                    returnHeader = new Header(Comm.CS, Code.JOIN_RES, roomIdBytes.Length);
                                    returnData = roomIdBytes;
                                }
                                else
                                {
                                    //no such key exists error (no such room error)
                                    returnHeader = new Header(Comm.CS, Code.JOIN_NULL_ERR, 0);
                                    returnData = null;
                                }
                            }
                            else
                            {
                                lobbyClients.Remove(client);
                                requestedRoom.AddClient(client);
                                client.Status = ClientHandle.State.Room;
                                client.RoomId = roomId;

                                roomIdBytes = BitConverter.GetBytes(roomId);
                                returnHeader = new Header(Comm.CS, Code.JOIN_RES, roomIdBytes.Length);
                                returnData = roomIdBytes;
                            }
                        }
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
                        if (lobbyClients.Contains(client))
                        {
                            returnHeader = new Header(Comm.CS, Code.LEAVE_ERR, 0);
                            returnData = null;
                        }
                        else
                        {
                            bool roomEmpty = false;
                            lock (rooms)
                            {
                                if (!rooms.TryGetValue(client.RoomId, out requestedRoom))
                                {
                                    Console.WriteLine("ERROR: Client is in a room that doesn't exist. WTF you fucked up.");
                                    returnHeader = new Header(Comm.CS, Code.LEAVE_ERR, 0);
                                    returnData = null;
                                }
                                else
                                {
                                    requestedRoom.RemoveClient(client);
                                    client.RoomId = 0;
                                    client.Status = ClientHandle.State.Lobby;

                                    if(requestedRoom.Clients.Count == 0)
                                    {
                                        rooms.Remove(requestedRoom.RoomId);
                                        roomEmpty = true;

                                        returnHeader = new Header(Comm.CS, Code.LEAVE_RES, 0);
                                        returnData = null;
                                    }
                                }
                            }

                            if (roomEmpty)
                            {
                                Packet reqPacket;
                                Header reqHeader;
                                roomIdBytes = BitConverter.GetBytes(requestedRoom.RoomId);
                                reqHeader = new Header(Comm.SS, Code.SLEAVE, roomIdBytes.Length);
                                reqPacket.header = reqHeader;
                                reqPacket.data = roomIdBytes;

                                foreach (ServerHandle peerServer in requestedRoom.Servers)
                                    peerServer.EchoSend(reqPacket);
                            }
                        }
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
                        //update user chat count. make it so that it increments value in redis
                        client.ChatCount++;
                        //redis.UpdateUser(client.UserId, );
                        client.ChatCount = 0;

                        lock(rooms)
                        {
                            if(!rooms.TryGetValue(client.RoomId, out requestedRoom))
                            {
                                // room doesnt exist error
                            }
                            else
                            {
                                foreach (ClientHandle peerClient in requestedRoom.Clients)
                                    peerClient.EchoSend(recvPacket);

                                recvPacket.header.comm = Comm.SS;
                                recvPacket.header.code = Code.SMSG;
                                foreach (ServerHandle peerServer in requestedRoom.Servers)
                                    peerServer.EchoSend(recvPacket);

                                returnHeader = NoResponseHeader;
                                returnData = null;
                            }
                        }
                        break;
                    case Code.MSG_ERR:
                        //CL <--> FE side
                        break;


                    //------------SIGNIN------------
                    case Code.SIGNIN:
                        //CL -> FE -> BE side
                        //bytes to string
                        usernameBytes = new byte[12];
                        passwordBytes = new byte[18];
                        Array.Copy(recvPacket.data, 0, usernameBytes, 0, 12);
                        Array.Copy(recvPacket.data, 12, passwordBytes, 0, 18);

                        username = Encoding.UTF8.GetString(usernameBytes).Trim();
                        password = Encoding.UTF8.GetString(passwordBytes).Trim();
                        userId = redis.SignIn(username, password);

                        if (-1 == userId)
                        {
                            //make packet for signin error
                            returnHeader = new Header(Comm.CS, Code.SIGNIN_ERR, 0);
                            returnData = null;
                        }
                        else
                        {
                            //make packet for signin success
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
                        //bytes to string
                        usernameBytes = new byte[12];
                        passwordBytes = new byte[18];
                        Array.Copy(recvPacket.data, 0, usernameBytes, 0, 12);
                        Array.Copy(recvPacket.data, 12, passwordBytes, 0, 18);

                        username = Encoding.UTF8.GetString(usernameBytes).Trim();
                        password = Encoding.UTF8.GetString(passwordBytes).Trim();
                        userId = redis.CreateUser(username, password);

                        if (-1 == userId)
                        {
                            //make packet for signup error
                            returnHeader = new Header(Comm.CS, Code.SIGNUP_ERR, 0);
                            returnData = null;
                        }
                        else
                        {
                            //make packet for signup success
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

                    default:
                        if(debug)
                            Console.WriteLine("Unknown code: {0}\n", recvPacket.header.code);
                        break;
                }
            }
            //Server to Server Side
            else if (Comm.SS == recvPacket.header.comm)
            {
                Room requestedRoom;
                long recvRoomId;
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
                        recvRoomId = ToInt64(recvPacket.data, 0);
                        bool haveRoom;
                        lock (rooms)
                        {
                            haveRoom = rooms.ContainsKey(recvRoomId);
                            if(haveRoom)
                            {
                                if(rooms.TryGetValue(recvRoomId, out requestedRoom))
                                    requestedRoom.AddServer(server);
                            }
                        }
                        
                        if(haveRoom)
                        {
                            returnHeader = new Header(Comm.SS, Code.SJOIN_RES, recvPacket.data.Length);
                            returnData = recvPacket.data; //need to send back room id. so receiver can check again if they are the same id
                        }
                        else
                        {
                            returnHeader = new Header(Comm.SS, Code.SJOIN_ERR, 0);
                            returnData = null;
                        }
                        //response should include roomid
                        break;
                    case Code.SJOIN_RES:
                        //FE side
                        //check if room is made
                        //no room: make room and add peer in server echo list
                        //room exists: add peer in server echo list
                        break;
                    case Code.SJOIN_ERR:
                        //FE side
                        break;

                    //------------SLEAVE-----------
                    case Code.SLEAVE:
                        //FE side
                        recvRoomId = ToInt64(recvPacket.data, 0);

                        lock (rooms)
                        {
                            if (rooms.ContainsKey(recvRoomId))
                            {
                                if (rooms.TryGetValue(recvRoomId, out requestedRoom))
                                    requestedRoom.RemoveServer(server);
                            }
                        }

                        //returnHeader = new Header(Comm.SS, Code.SLEAVE_RES, 0);
                        returnHeader = NoResponseHeader;
                        returnData = null;
                        break;
                    case Code.SLEAVE_ERR:
                        //FE side
                        break;
                    case Code.SLEAVE_RES:
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
                        byte[] roomIdBytes = new byte[16];
                        Array.Copy(recvPacket.data, 0, roomIdBytes, 0, 16);
                        long roomId = ToInt64(roomIdBytes, 0);
                        
                        lock (rooms)
                        {
                            if (!rooms.TryGetValue(roomId, out requestedRoom))
                            {
                                // room doesnt exist error
                            }
                            else
                            {
                                foreach (ClientHandle peerClient in requestedRoom.Clients)
                                    peerClient.EchoSend(recvPacket);

                                returnHeader = NoResponseHeader;
                                returnData = null;
                            }
                        }
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
            if (debug)
                Console.WriteLine("==SEND: \n" + PacketDebug(returnPacket));

            return returnPacket;
        }
        
        private long ToInt64(byte[] bytes, int startIndex)
        {
            long result = 0;
            try
            {
                result = BitConverter.ToInt64(bytes, startIndex);
            }
            catch (Exception e)
            {
                Console.WriteLine("bytes to int64: fuck you. you messsed up");
            }

            return result;
        }

        private Socket ConnectToPeer(string info)
        {
            string host;
            int port;

            string[] hostport = info.Split(':');
            host = hostport[0];
            if (!int.TryParse(hostport[1], out port))
            {
                Console.Error.WriteLine("port must be int. given: {0}", hostport[1]);
                Environment.Exit(0);
            }

            Socket so = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPAddress ipAddress = IPAddress.Parse(host);
            Console.WriteLine("Establishing connection to {0}:{1} ...", host, port);

            try
            {
                so.Connect(ipAddress, port);
                Console.WriteLine("Connection established.\n");
            }
            catch(Exception e)
            {
                Console.WriteLine("Peer is not alive.");
                return null;
            }

            return so;
        }
    }
}
