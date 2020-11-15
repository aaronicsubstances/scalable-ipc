using ScalableIPC.Core.Abstractions;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace ScalableIPC.Core
{
    public class GenericNetworkIdentifier
    {
        public string HostName { get; set; }
        public IPAddress NetworkAddress { get; set; }
        public int Port { get; set; } = -1;

        public override bool Equals(object obj)
        {
            return obj is GenericNetworkIdentifier identifier &&
                   HostName == identifier.HostName &&
                   EqualityComparer<IPAddress>.Default.Equals(NetworkAddress, identifier.NetworkAddress) &&
                   Port == identifier.Port;
        }

        public override int GetHashCode()
        {
            int hashCode = 273286397;
            hashCode = hashCode * -1521134295 + EqualityComparer<string>.Default.GetHashCode(HostName);
            hashCode = hashCode * -1521134295 + EqualityComparer<IPAddress>.Default.GetHashCode(NetworkAddress);
            hashCode = hashCode * -1521134295 + Port.GetHashCode();
            return hashCode;
        }

        public override string ToString()
        {
            return $"{{Host={HostName},IP={NetworkAddress},Port={Port}}}";
        }
    }
}
