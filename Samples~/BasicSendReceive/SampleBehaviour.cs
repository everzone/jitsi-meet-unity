using AVStack.Jitsi;
using System.Collections;
using System.Collections.Generic;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.UI;

public class SampleBehaviour : MonoBehaviour
{
    [SerializeField]
    private GameObject canvasGameObject;

    private Tile localTile;
    private int tileIndex;
    private Dictionary<string, Tile> remoteTiles;
    private Dictionary<string, AudioObject> remoteAudios;

    private Connection connection;
    private Conference conference;

    // private WebCamTexture webCamTexture;
    private AudioSource microphoneSource;
    private VideoStreamTrack localVideoTrack;
    private AudioStreamTrack localAudioTrack;

    internal struct Tile
    {
        internal string trackId;
        private GameObject gameObject;
        private RawImage rawImage;

        internal Tile(string trackId, Transform parent, Texture texture, int index)
        {
            this.trackId = trackId;

            this.gameObject = new GameObject();
            this.gameObject.transform.parent = parent;

            this.rawImage = this.gameObject.AddComponent<RawImage>();
            this.rawImage.texture = texture;

            // var transform = this.rawImage.GetComponent<RectTransform>();
        }

        internal void Dispose() {
            Destroy(this.gameObject);
        }
    }

    // https://docs.unity3d.com/Packages/com.unity.webrtc@2.4/manual/audiostreaming.html
    internal struct AudioObject
    {
        internal string trackId;
        private GameObject gameObject;
        private AudioSource audioSource;

        internal AudioObject(AudioStreamTrack audioStreamTrack) {
            this.trackId = audioStreamTrack.Id;

            this.gameObject = new GameObject();

            this.audioSource = this.gameObject.AddComponent<AudioSource>();
            this.audioSource.SetTrack(audioStreamTrack);
            this.audioSource.loop = true;
            this.audioSource.Play();
        }

        internal bool isPlaying {
            get => this.audioSource.isPlaying;
        }

        internal void Dispose() {
            this.audioSource.Stop();
            Destroy(this.gameObject);
        }
    }

    private class ConferenceDelegate : IConferenceDelegate
    {
        private SampleBehaviour behaviour;

        internal ConferenceDelegate(SampleBehaviour behaviour)
        {
            this.behaviour = behaviour;
        }

        public IEnumerator ParticipantJoined(Participant participant)
        {
            Debug.Log($"Participant joined: JID({participant.Jid()}) NICK({participant.Nick()}) EndpointId({participant.EndpointId()})");
            yield return null;
        }

        public IEnumerator ParticipantLeft(Participant participant)
        {
            string jid = participant.Jid();
            Debug.Log($"Participant left: JID({jid}) NICK({participant.Nick()}) EndpointId({participant.EndpointId()})");
            this.behaviour.RemoveRemoteTile(jid);
            this.behaviour.RemoveRemoteAudio(jid);
            yield return null;
        }

        public IEnumerator RemoteAudioTrackAdded(Participant participant, AudioStreamTrack audioTrack)
        {
            string jid = participant?.Jid();
            Debug.Log($"Remote audio track added: {audioTrack.Id} jid: {jid}");
            // audioTrack.source is an AudioSource
            this.behaviour.AddRemoteAudio(audioTrack.Id, audioTrack);
            yield return null;
        }

        public IEnumerator RemoteAudioTrackRemoved(Participant participant, AudioStreamTrack audioTrack)
        {
            string jid = participant?.Jid();
            Debug.Log($"Remote audio track removed: {audioTrack.Id} jid: {jid}");
            this.behaviour.RemoveRemoteAudio(audioTrack.Id);
            yield return null;
        }

        public IEnumerator RemoteVideoTrackAdded(Participant participant, VideoStreamTrack videoTrack)
        {
            string jid = participant?.Jid();
            Debug.Log($"Remote video track added: {videoTrack.Id} jid: {jid}");
            // We defer adding the tile until we get the video texture in VideoReceived
            yield return null;
        }

        public IEnumerator RemoteVideoTrackRemoved(Participant participant, VideoStreamTrack videoTrack)
        {
            string jid = participant?.Jid();
            Debug.Log($"Remote video track removed: {videoTrack.Id} jid: {jid}");
            this.behaviour.RemoveRemoteTile(jid);
            yield return null;
        }

        public IEnumerator VideoReceived(Participant participant, VideoStreamTrack videoTrack, Texture texture)
        {
            string jid = participant?.Jid();
            Debug.Log($"Video received trackId: {videoTrack.Id} jid: {jid}");
            this.behaviour.AddRemoteTile(jid, videoTrack.Id, texture);
            yield return null;
        }

        public IEnumerator SessionTerminate()
        {
            this.behaviour.RemoveAllRemoteObject();
            
            yield return null;
        }
    }

    void Awake()
    {
        Debug.Log("Initialising Jitsi");
        Jitsi.Initialize();
    }

    void OnDestroy()
    {
        if (jitsiCoroutine != null) {
            StopCoroutine(jitsiCoroutine);
        }
        if (joinCoroutine != null) {
            StopCoroutine(joinCoroutine);
        }

        Jitsi.Dispose();
    }

    IEnumerator jitsiCoroutine;
    IEnumerator joinCoroutine;
    void Start()
    {
        Debug.Log("Starting Jitsi background task");
        jitsiCoroutine = Jitsi.Update(this);
        StartCoroutine(jitsiCoroutine);
        
        joinCoroutine = this.JoinConference();
        StartCoroutine(joinCoroutine);
    }

    private IEnumerator op;
    void Update() {
        if (Jitsi.asyncOperationQueue.TryDequeue(out op))
            StartCoroutine(op);
    }

    void AddRemoteTile(string jid, string trackId, Texture texture)
    {
        Debug.Log($"Adding tile for remote video stream: {trackId} jid: {jid}");
        this.remoteTiles.Add(jid, new Tile(trackId, this.canvasGameObject.transform, texture, this.tileIndex++));
    }

    void RemoveRemoteTile(string jid)
    {
        Tile tile;
        if (this.remoteTiles.TryGetValue(jid, out tile)) {
            Debug.Log($"Removing tile for remote video stream: {tile.trackId} jid: {jid}");
            tile.Dispose();
            this.remoteTiles.Remove(jid);
        }
    }

    void AddRemoteAudio(string jid, AudioStreamTrack audioStreamTrack) {
        Debug.Log($"Adding audioSource for remote audio stream: {audioStreamTrack.Id} jid: {jid}");
        this.remoteAudios.Add(jid, new AudioObject(audioStreamTrack));
    }

    void RemoveRemoteAudio(string jid)
    {
        AudioObject audioObject;
        if (this.remoteAudios.TryGetValue(jid, out audioObject)) {
            Debug.Log($"Removing audioSource for remote audio stream: {audioObject.trackId} jid: {jid}");
            audioObject.Dispose();
            this.remoteAudios.Remove(jid);
        }
    }

    public void RemoveAllRemoteObject() {
        foreach (Tile tile in this.remoteTiles.Values) {
            tile.Dispose();
        }
        this.remoteTiles.Clear();
        this.tileIndex = 1;

        foreach (AudioObject audioObject in this.remoteAudios.Values) {
            audioObject.Dispose();
        }
        this.remoteAudios.Clear();
    }

    private MediaStreamTrack[] localTracks;
    private GameObject micGameObject;
    IEnumerator JoinConference()
    {
        // Debug.Log("Starting webcam");
        // this.webCamTexture = new WebCamTexture(640, 360);
        // this.webCamTexture.Play();

        RenderTexture dummyVideo = new RenderTexture(640, 360, 32, RenderTextureFormat.BGRA32);
        dummyVideo.Create();

        // for (int i = 0; i < Microphone.devices.Length; i++)
        // {
        //     Debug.Log($"Microphone device {i}: {Microphone.devices[i].ToString()}");
        // }

        string micDevice = null;
        if (Application.platform == RuntimePlatform.Android) {
            // const string micDevice = "Android audio input";
            // const string micDevice = "Android camcorder input";
            micDevice = "Android voice recognition input";
        } else {
            micDevice = Microphone.devices[0].ToString();
        }

        Debug.Log($"Starting microphone: {micDevice}");
        micGameObject = new GameObject();
        this.microphoneSource = micGameObject.AddComponent<AudioSource>();
        this.microphoneSource.loop = true;
        this.microphoneSource.volume = 1.0f;
        this.microphoneSource.clip = Microphone.Start(micDevice, true, 1, 48000);

        // set the latency to “0” samples before the audio starts to play.
        while (!(Microphone.GetPosition(micDevice) > 0)) {}

        this.microphoneSource.Play();

        // // Unity's webcam texture reports 16x16 until capture has finished starting,
        // // which happens asynchronously.
        // // 
        // // If it's assigned to the local track while it still reports 16x16, then
        // // 16x16 will be the transmitted resolution!
        // Debug.Log("Waiting for webcam to finish starting");
        // yield return new WaitUntil(() => this.webCamTexture.width != 16);
        this.tileIndex = 0;
        this.localVideoTrack = new VideoStreamTrack(dummyVideo, true);

        this.remoteTiles = new Dictionary<string, Tile>();
        this.remoteAudios = new Dictionary<string, AudioObject>();


        this.localAudioTrack = new AudioStreamTrack(this.microphoneSource);
        // this.localAudioTrack.Loopback = true;
        localTracks = new MediaStreamTrack[] { this.localVideoTrack, this.localAudioTrack };

        Debug.Log("Connecting");
        // new Connection() currently blocks until the connection is established.
        // This will be changed to be asynchronous in future
        this.connection = new Connection("wss://example.com/xmpp-websocket", "meet.jitsi", false);

        Debug.Log("Joining conference");
        // join() currently blocks until the room is joined.
        // This will be changed to be asynchronous in future
        this.conference = this.connection.join("nativeroom", "unitynick", localTracks, new ConferenceDelegate(this));

        var localEndpointId = this.conference.LocalEndpointId();
        Debug.Log($"Joined. My endpoint ID: {localEndpointId}");

        yield return null;
    }
}
