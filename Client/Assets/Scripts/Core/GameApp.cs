using System;
using System.Collections.Generic;
using Bloodfall.Client.Audio;
using Bloodfall.Client.Backend;
using Bloodfall.Client.Match;
using Bloodfall.Client.UI;
using Bloodfall.Data;
using UnityEngine;

namespace Bloodfall.Client.Core
{
    /// <summary>
    /// Creates the client on start-up from any scene (no scene wiring required), so opening the project and pressing
    /// Play always boots the full Bloodfall client.
    /// </summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (GameApp.Instance != null) return;
            if (Application.isBatchMode && Environment.GetCommandLineArgs() is string[] args && Array.IndexOf(args, "-bloodfall-no-boot") >= 0) return;
            var go = new GameObject("Bloodfall");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<GameApp>();
        }
    }

    public interface ITickable
    {
        void Tick(float dt);
    }

    /// <summary>
    /// The single MonoBehaviour driving the client. Services are plain C# objects ticked in a fixed order, which keeps
    /// the frame predictable and avoids hundreds of scattered Update() methods.
    /// </summary>
    public sealed class GameApp : MonoBehaviour
    {
        public static GameApp Instance { get; private set; }

        public ClientConfig Config { get; private set; }
        public ClientSettings Settings { get; private set; }
        public GameData Data { get; private set; }
        public BackendClient Backend { get; private set; }
        public UIManager UI { get; private set; }
        public AudioManager Audio { get; private set; }
        public FlowController Flow { get; private set; }
        public MatchController Match { get; set; }
        public MenuBackdrop Backdrop { get; private set; }
        public string DataError { get; private set; }

        private readonly List<ITickable> _tickables = new List<ITickable>();
        private readonly Queue<Action> _mainThread = new Queue<Action>();

        private void Awake()
        {
            Instance = this;
            Application.targetFrameRate = -1;
            Config = ClientConfig.Load();
            Settings = ClientSettings.Load();
            Settings.ApplyGraphics();
            try
            {
                Data = GameDataProvider.Load();
                if (Data.Errors.Count > 0) DataError = string.Join("\n", Data.Errors);
            }
            catch (Exception e)
            {
                DataError = e.Message;
                Debug.LogException(e);
            }
            Audio = new AudioManager(gameObject, Settings);
            Backend = new BackendClient(Config, this);
            UI = new UIManager(gameObject, Settings);
            Backdrop = new MenuBackdrop();
            Flow = new FlowController(this);
            Register(Audio);
            Register(Backend);
            Register(Backdrop);
            Register(Flow);
            Register(UI);
        }

        private void Start()
        {
            CursorManager.Apply(CursorKind.Default);
            Flow.Start();
        }

        public void Register(ITickable t) { if (!_tickables.Contains(t)) _tickables.Add(t); }
        public void Unregister(ITickable t) => _tickables.Remove(t);

        /// <summary>Queues work onto the main thread (for network callbacks raised on worker threads).</summary>
        public void RunOnMainThread(Action a) { lock (_mainThread) _mainThread.Enqueue(a); }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;
            while (true)
            {
                Action a;
                lock (_mainThread)
                {
                    if (_mainThread.Count == 0) break;
                    a = _mainThread.Dequeue();
                }
                try { a(); } catch (Exception e) { Debug.LogException(e); }
            }
            Match?.Tick(dt);
            for (int i = 0; i < _tickables.Count; i++)
            {
                try { _tickables[i].Tick(dt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        private void LateUpdate()
        {
            Match?.LateTick(Time.unscaledDeltaTime);
        }

        private void OnApplicationQuit()
        {
            Match?.Dispose();
            Backend?.Dispose();
            Settings?.Save();
        }
    }
}
