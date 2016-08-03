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
        static Dictionary<long, ClientHandle> lobbyClients;
        static List<ServerHandle> peerServers;
        static Dictionary<long, Room> rooms;
        Header NoResponseHeader = new Header(-1, 0, 0);

        public ReceiveHandler()
        {
            lobbyClients = new Dictionary<long, ClientHandle>();
            peerServers = new List<ServerHandle>();
            rooms = new Dictionary<long, Room>();
        }

        public ReceiveHandler(ClientHandle client, Packet recvPacket, RedisHelper redis)
        {
            this.client = client;
            this.recvPacket = recvPacket;
            this.redis = redis;

            //lobbyClients.Add(client.UserId, client);
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
        public Packet GetResponse(out ClientHandle surrogateClient)
        {
            Packet returnPacket;
            Header returnHeader = new Header();
            byte[] returnData = null;
            ClientHandle surrogateCandidate = null; //null is default

            bool debug = true;

            if(debug)
                Console.WriteLine("==RECEIVED: \n" + PacketDebug(recvPacket));

            //Client to Server side
            if (Comm.CS == recvPacket.header.comm)
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
                    //------------No action from client----------
                    case -1:
                        returnHeader = new Header(Comm.CS, Code.HEARTBEAT, 0);
                        returnData = null;
                        break;

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
                            /*
                            createAndJoin = true;
                            goto case Code.JOIN;
                            */
                            break;
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
                        returnHeader = NoResponseHeader;
                        returnData = null;
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
                                byte[] sreqUserId = BitConverter.GetBytes(client.UserId);
                                byte[] sreqRoomId = BitConverter.GetBytes(roomId);
                                byte[] sreqData = new byte[sreqUserId.Length + sreqRoomId.Length];
                                Array.Copy(sreqUserId, 0, sreqData, 0, sreqUserId.Length);
                                Array.Copy(sreqRoomId, 0, sreqData, sreqUserId.Length, sreqRoomId.Length);

                                Header sreqHeader = new Header(Comm.SS, Code.SJOIN, sreqData.Length);
                                Packet sreqPacket = new Packet(sreqHeader, sreqData);

                                //room not in local server. check other servers
                                foreach (ServerHandle peer in peerServers)
                                {
                                    bool success = peer.Send(sreqPacket);

                                    if (!success)
                                        Console.WriteLine("ERROR: SJOIN send failed");
                                }

                                returnHeader = NoResponseHeader;
                                returnData = null;
                            }
                            else
                            {
                                lobbyClients.Remove(client.UserId);
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
                        if (lobbyClients.ContainsKey(client.UserId))
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
                        //TODO: update user chat count. make it so that it increments value in redis
                        client.ChatCount++;
                        //redis.UpdateUser(client.UserId, );
                        client.ChatCount = 0;

                        lock(rooms)
                        {
                            if(!rooms.TryGetValue(client.RoomId, out requestedRoom))
                            {
                                Console.WriteLine("ERROR: Msg - Room doesn't exist");
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
                            client.UserId = userId;
                            lobbyClients.Add(userId, client);
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
                    case Code.SIGNIN_DUMMY:
                        //CL -> FE
                        userId = redis.CreateDummy();
                        userId = redis.SignInDummy(userId);
                        client.IsDummy = true;
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
                            client.UserId = userId;
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
                surrogateCandidate = client;
            }
            //Server to Server Side
            else if (Comm.SS == recvPacket.header.comm)
            {
                Room requestedRoom;
                long userId;
                long recvRoomId;
                byte[] roomIdBytes;

                switch (recvPacket.header.code)
                {
                    //------------No action from client----------
                    case -1:
                        returnHeader = new Header(Comm.SS, Code.HEARTBEAT, 0);
                        returnData = null;
                        break;

                    //------------HEARTBEAT------------
                    case Code.HEARTBEAT:
                        //FE -> CL side
                        returnHeader = new Header(Comm.SS, Code.HEARTBEAT_RES, 0);
                        returnData = null;
                        break;
                    case Code.HEARTBEAT_RES:
                        //CL -> FE side
                        returnHeader = NoResponseHeader;
                        returnData = null;
                        break;

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
                        //receive UserID (8byte) + RoomId (8byte)
                        recvRoomId = ToInt64(recvPacket.data, 8);
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
                            returnHeader = new Header(Comm.SS, Code.SJOIN_ERR, recvPacket.data.Length);
                            returnData = recvPacket.data;
                        }
                        //response should include roomid
                        break;
                    case Code.SJOIN_RES:
                        //FE side
                        //need to get 'client' because this function will return to
                        //a ServerHandle instance, which will have a SS socket
                        //so need to set surrogateClient so that the returnPacket is 
                        //send through the surrogateClient
                        userId = ToInt64(recvPacket.data, 0);
                        recvRoomId = ToInt64(recvPacket.data, 8);
                        
                        lock(lobbyClients)
                        {
                            if (!lobbyClients.TryGetValue(userId, out client))
                                Console.WriteLine("ERROR: SJOIN_RES - user no longer exists");
                        }

                        lock(rooms)
                        {
                            if (!rooms.TryGetValue(recvRoomId, out requestedRoom))
                            {
                                //first SJOIN_RES from peer servers
                                Room newJoinRoom = new Room(recvRoomId);
                                lobbyClients.Remove(client.UserId);
                                newJoinRoom.AddClient(client);
                                newJoinRoom.AddServer(server);

                                rooms.Add(recvRoomId, newJoinRoom);

                                client.Status = ClientHandle.State.Room;
                                client.RoomId = recvRoomId;
                            }
                            else
                            {
                                //room already made by previous iteration
                                requestedRoom.AddServer(server);
                            }
                        }

                        //room exists in other peer irc servers
                        //created one in local and connected with peers
                        surrogateCandidate = client;
                        roomIdBytes = BitConverter.GetBytes(recvRoomId);
                        returnHeader = new Header(Comm.CS, Code.JOIN_RES, roomIdBytes.Length);
                        returnData = roomIdBytes;
                        break;
                    case Code.SJOIN_ERR:
                        //FE side
                        //no such key exists error (no such room error)
                        //need to get 'client' because this function will return to
                        //a ServerHandle instance, which will have a SS socket
                        //so need to set surrogateClient so that the returnPacket is 
                        //send through the surrogateClient
                        userId = ToInt64(recvPacket.data, 0);
                        recvRoomId = ToInt64(recvPacket.data, 8);

                        lock (lobbyClients)
                        {
                            if (!lobbyClients.TryGetValue(userId, out client))
                                Console.WriteLine("ERROR: SJOIN_RES - user no longer exists");
                        }

                        surrogateCandidate = client;
                        returnHeader = new Header(Comm.CS, Code.JOIN_NULL_ERR, 0);
                        returnData = null;
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
                        roomIdBytes = new byte[8];
                        Array.Copy(recvPacket.data, 0, roomIdBytes, 0, 8);
                        long roomId = ToInt64(roomIdBytes, 0);
                        
                        lock (rooms)
                        {
                            if (!rooms.TryGetValue(roomId, out requestedRoom))
                            {
                                Console.WriteLine("ERROR: SMSG - room doesn't exist {0}", roomId);
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
            if (debug && returnPacket.header.comm != -1)
                Console.WriteLine("==SEND: \n" + PacketDebug(returnPacket));

            surrogateClient = surrogateCandidate;
            return returnPacket;
        }
        
        private long ToInt64(byte[] bytes, int startIndex)
        {
            long result = 0;
            try
            {
                result = BitConverter.ToInt64(bytes, startIndex);
            }
            catch (Exception)
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
            Console.WriteLine("[Server] Establishing connection to {0}:{1} ...", host, port);

            try
            {
                so.Connect(ipAddress, port);
                Console.WriteLine("[Server] Connection established.\n");
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
