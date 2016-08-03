using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Ircc;

namespace IrccServer
{
    class Room
    {
        private long roomId;
        private List<ServerHandle> servers;
        private List<ClientHandle> clients;

        public long RoomId { get { return roomId; } }
        public List<ClientHandle> Clients { get { return clients; } }
        public List<ServerHandle> Servers { get { return servers; } }

        public Room(long roomId)
        {
            this.roomId = roomId;
            servers = new List<ServerHandle>();
            clients = new List<ClientHandle>();
        }

        public void AddClient(ClientHandle client)
        {
            clients.Add(client);
        }
        public void RemoveClient(ClientHandle client)
        {
            clients.Remove(client);
        }
        public void AddServer(ServerHandle server)
        {
            servers.Add(server);
        }
        public void RemoveServer(ServerHandle server)
        {
            servers.Remove(server);
        }
    }
}
