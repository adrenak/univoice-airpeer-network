using System;
using System.Collections.Generic;

using Adrenak.AirPeer;

namespace Adrenak.UniVoice.AirPeerNetwork {
    /// <summary>
    /// A <see cref="IChatroomNetwork"/> implementation using AirPeer
    /// For more on AirPeer, visit https://www.vatsalambastha.com/airpeer
    /// 
    /// Notes:
    /// An APNode node doesn't receive its client ID immediately after 
    /// connecting to an APNetwork, it receives it after joining the network
    /// from the host. But while it's waiting it still has peers. 
    /// This class makes sure that until the APNode doesn't receive its ID,
    /// a consumer of it will think it hasn't been connected.
    /// 
    /// TLDR; APNode first connects to host, and is given its ID by the host 
    /// after joining. We don't let anyone know we have connected until th
    /// </summary>
    public class UniVoiceAirPeerNetwork : IChatroomNetwork {
        public event Action OnCreatedChatroom;
        public event Action<Exception> OnChatroomCreationFailed;
        public event Action OnClosedChatroom;

        public event Action<short> OnJoinedChatroom;
        public event Action<Exception> OnChatroomJoinFailed;
        public event Action OnLeftChatroom; 

        public event Action<short> OnPeerJoinedChatroom;
        public event Action<short> OnPeerLeftChatroom;

        public event Action<short, ChatroomAudioSegment> OnAudioReceived;
        public event Action<short, ChatroomAudioSegment> OnAudioSent;

        public short OwnID => node.ID;

        public List<short> PeerIDs =>
            OwnID != -1 ? node.Peers : new List<short>();

        public string CurrentChatroomName => OwnID != -1 ? node.Address : null;

        readonly APNode node;

        /// <summary>
        /// Creates an AirPeer based chatroom network 
        /// </summary>
        /// <param name="signalingServerURL">The signaling server URL</param>
        /// <param name="iceServerURLs">ICE server urls</param>
        public UniVoiceAirPeerNetwork
        (string signalingServerURL, string iceServerURLs) {
            node = new APNode(signalingServerURL, iceServerURLs);
            Init();
        }

        /// <summary>
        /// Creates an AirPeer based chatroom network
        /// </summary>
        /// <param name="signalingServerURL">The signaling server URL</param>
        public UniVoiceAirPeerNetwork(string signalingServerURL) {
            node = new APNode(signalingServerURL);
            Init();
        }

        void Init() {
            node.OnServerStartSuccess += () => OnCreatedChatroom?.Invoke();
            node.OnServerStartFailure += e =>
                OnChatroomCreationFailed?.Invoke(e);
            node.OnServerStop += () => OnClosedChatroom?.Invoke();

            node.OnConnectionFailed += ex => OnChatroomJoinFailed?.Invoke(ex);
            node.OnReceiveID += id => {
                if (id != 0) {
                    OnJoinedChatroom?.Invoke(id);
                    
                    OnPeerJoinedChatroom?.Invoke(0); // server joins instantly
                }
            };
            node.OnDisconnected += () => OnLeftChatroom?.Invoke();
            node.OnRemoteServerClosed += () => OnLeftChatroom?.Invoke();

            node.OnClientJoined += id => OnPeerJoinedChatroom?.Invoke(id);
            node.OnClientLeft += id => OnPeerLeftChatroom?.Invoke(id);

            node.OnPacketReceived += (sender, packet) => {
                if (packet.Tag.Equals("audio")) {
                    var reader = new BytesReader(packet.Payload);
                    var index = reader.ReadInt();
                    var frequency = reader.ReadInt();
                    var channels = reader.ReadInt();
                    var samples = reader.ReadFloatArray();

                    OnAudioReceived?.Invoke(sender, new ChatroomAudioSegment {
                        segmentIndex = index,
                        frequency = frequency,
                        channelCount = channels,
                        samples = samples
                    });
                }
            };
        }

        public void HostChatroom(object chatroomName = null) =>
            node.StartServer(Convert.ToString(chatroomName));

        public void CloseChatroom(object data = null) =>
            node.StopServer();

        public void JoinChatroom(object chatroomName = null) =>
            node.Connect(Convert.ToString(chatroomName));

        public void LeaveChatroom(object data = null) =>
            node.Disconnect();

        public void SendAudioSegment(short peerID, ChatroomAudioSegment data) {
            if (OwnID == -1) return;

            var segmentIndex = data.segmentIndex;
            var frequency = data.frequency;
            var channelCount = data.channelCount;
            var samples = data.samples;

            var packet = new Packet().WithTag("audio")
                .WithPayload(new BytesWriter()
                    .WriteInt(segmentIndex)
                    .WriteInt(frequency)
                    .WriteInt(channelCount)
                    .WriteFloatArray(samples)
                    .Bytes
                );

            node.SendPacket(peerID, packet, false);
            OnAudioSent?.Invoke(peerID, data);
        }

        public void Dispose() => node.Dispose();
    }
}
