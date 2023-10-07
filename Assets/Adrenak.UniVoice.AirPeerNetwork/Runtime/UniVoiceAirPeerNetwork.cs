using System;
using System.Collections.Generic;

using Adrenak.AirPeer;

using Debug = UnityEngine.Debug;

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
        const string TAG = "UniVoiceAirPeerNetwork";

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
        public UniVoiceAirPeerNetwork(string signalingServerURL, params string[] iceServerURLs) {
            Debug.unityLogger.Log(TAG, "Creating with signalling server URL and ICE server urls");
            node = new APNode(signalingServerURL, iceServerURLs);
            Init();
        }

        /// <summary>
        /// Creates an AirPeer based chatroom network
        /// </summary>
        /// <param name="signalingServerURL">The signaling server URL</param>
        public UniVoiceAirPeerNetwork(string signalingServerURL) {
            Debug.unityLogger.Log(TAG, "Creating with signalling server URL and default ICE server urls");
            node = new APNode(signalingServerURL);
            Init();
        }

        void Init() {
            node.OnServerStartSuccess += () => {
                Debug.unityLogger.Log(TAG, "Airpeer Server started.");
                OnCreatedChatroom?.Invoke();
            };
            node.OnServerStartFailure += e => {
                Debug.unityLogger.Log(TAG, "Airpeer Server start failed.");
                OnChatroomCreationFailed?.Invoke(e);
            };
            node.OnServerStop += () => {
                Debug.unityLogger.Log(TAG, "Airpeer Server stopped.");
                OnClosedChatroom?.Invoke();
            };

            node.OnConnectionFailed += ex => {
                Debug.unityLogger.Log(TAG, "Airpeer connection failed. " + ex);
                OnChatroomJoinFailed?.Invoke(ex);
            };

            // Think of this like "OnConnectionSuccess"
            node.OnReceiveID += id => {
                // If ID is not 0, this means we're a guest, not the host
                if (id != 0) {
                    Debug.unityLogger.Log(TAG, "Received Airpeer connection ID: " + id);
                    OnJoinedChatroom?.Invoke(id);

                    // The server with ID 0 is considered a peer immediately
                    OnPeerJoinedChatroom?.Invoke(0);
                }
            };
            node.OnDisconnected += () => {
                Debug.unityLogger.Log(TAG, "Disconnected from server");
                OnLeftChatroom?.Invoke();
            };
            node.OnRemoteServerClosed += () => {
                Debug.unityLogger.Log(TAG, "Airpeer server closed");
                OnLeftChatroom?.Invoke();
            };

            node.OnClientJoined += id => {
                Debug.unityLogger.Log(TAG, "New Airpeer peer joined: " + id);
                OnPeerJoinedChatroom?.Invoke(id);
            };
            node.OnClientLeft += id => {
                Debug.unityLogger.Log(TAG, "Airpeer peer left: " + id);
                OnPeerLeftChatroom?.Invoke(id);
            };

            node.OnPacketReceived += (sender, packet) => {
                // "audio" tag is used for sending audio data
                if (packet.Tag.Equals("audio")) {
                    var reader = new BytesReader(packet.Payload);
                    // The order we read the bytes in is important here.
                    // See SendAudioSegment where the audio packet is constructed.
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

            // Create an airpeer packet with tag "audio", that's the tag used to determine
            // on the receiving end for parsing audio data.
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
