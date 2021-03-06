using System;
using ENet;

namespace Mirage.ENet
{
        // Snipped from the transport files, as this will help
        // me keep things up to date.
        [Serializable]
        public enum IgnoranceChannelTypes
        {
            Reliable = PacketFlags.Reliable,                                        // TCP Emulation.
            ReliableUnsequenced = PacketFlags.Reliable | PacketFlags.Unsequenced,   // TCP Emulation, but no sequencing.
            ReliableUnbundledInstant = PacketFlags.Reliable | PacketFlags.Instant,  // Experimental: Reliablity + Instant hybrid packet type.
            UnbundledInstant = PacketFlags.Instant,                                 // Instant packet, will not be bundled with others.
            Unreliable = PacketFlags.Unsequenced,                                   // Pure UDP.
            UnreliableFragmented = PacketFlags.UnreliableFragmented,                // Pure UDP, but fragmented.
            UnreliableSequenced = PacketFlags.None,                                 // Pure UDP, but sequenced.
            Unthrottled = PacketFlags.Unthrottled,                                // ???

    }

        [Serializable]
		public class PeerStatistics
        {
			public ulong CurrentPing;
			public ulong PacketsSent;
			public ulong PacketsLost;
            public ulong BytesSent;
            public ulong BytesReceived;
		}
		
}
