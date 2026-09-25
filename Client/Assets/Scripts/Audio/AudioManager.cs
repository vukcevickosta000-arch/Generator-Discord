using System.Collections.Generic;
using Bloodfall.Client.Core;
using UnityEngine;

namespace Bloodfall.Client.Audio
{
    public enum AudioBus { Music, Effects, Voice, Announcer, Ui, Ambience }

    /// <summary>
    /// Pooled audio playback with per-bus volumes (Resources/Audio/{Category}/{name}). Clips are optional: a missing
    /// clip is logged once and skipped, so placeholder audio can be replaced by final recordings file-by-file.
    /// </summary>
    public sealed class AudioManager : ITickable
    {
        private readonly ClientSettings _settings;
        private readonly GameObject _root;
        private readonly List<AudioSource> _pool3D = new List<AudioSource>();
        private readonly AudioSource _ui, _announcer, _ambience;
        private readonly AudioSource[] _music = new AudioSource[2];
        private int _musicIndex;
        private float _musicFade = 1f;
        private string _musicTrack;
        private readonly Dictionary<string, AudioClip> _clips = new Dictionary<string, AudioClip>();
        private readonly HashSet<string> _missing = new HashSet<string>();
        private readonly Queue<(string key, float delay)> _announcerQueue = new Queue<(string, float)>();
        private float _announcerCooldown;
        private readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();

        public AudioManager(GameObject host, ClientSettings settings)
        {
            _settings = settings;
            _root = new GameObject("Audio");
            _root.transform.SetParent(host.transform, false);
            _ui = Make2D("UI");
            _announcer = Make2D("Announcer");
            _ambience = Make2D("Ambience");
            _ambience.loop = true;
            for (int i = 0; i < 2; i++) { _music[i] = Make2D("Music" + i); _music[i].loop = true; }
            for (int i = 0; i < 32; i++)
            {
                var go = new GameObject("Sfx" + i);
                go.transform.SetParent(_root.transform, false);
                var s = go.AddComponent<AudioSource>();
                s.playOnAwake = false;
                s.spatialBlend = 1f;
                s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 8f;
                s.maxDistance = 60f;
                s.dopplerLevel = 0f;
                _pool3D.Add(s);
            }
        }

        private AudioSource Make2D(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        public float BusVolume(AudioBus bus)
        {
            float v;
            switch (bus)
            {
                case AudioBus.Music: v = _settings.MusicVolume; break;
                case AudioBus.Voice: v = _settings.VoiceVolume; break;
                case AudioBus.Announcer: v = _settings.AnnouncerVolume; break;
                case AudioBus.Ui: v = _settings.UiVolume; break;
                case AudioBus.Ambience: v = _settings.EffectsVolume * 0.8f; break;
                default: v = _settings.EffectsVolume; break;
            }
            return v * _settings.MasterVolume;
        }

        public AudioClip Clip(string category, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string key = category + "/" + name;
            if (_clips.TryGetValue(key, out var c)) return c;
            c = Resources.Load<AudioClip>("Audio/" + key);
            if (c == null && _missing.Add(key)) Debug.Log($"[Audio] no clip for {key} (placeholder slot)");
            _clips[key] = c;
            return c;
        }

        /// <summary>Picks a random variation name_1.._n if present, else name.</summary>
        private AudioClip Variation(string category, string name)
        {
            var baseClip = Clip(category, name);
            var v2 = Clip(category, name + "_2");
            if (v2 == null) return baseClip ?? Clip(category, name + "_1");
            int n = 2;
            while (n < 6 && Clip(category, name + "_" + (n + 1)) != null) n++;
            int pick = Random.Range(0, n + (baseClip != null ? 1 : 0));
            if (baseClip != null && pick == n) return baseClip;
            return Clip(category, name + "_" + (pick + 1)) ?? baseClip;
        }

        public void PlayUi(string name)
        {
            if (!Throttle("ui:" + name, 0.04f)) return;
            var c = Clip("UI", name);
            if (c != null) _ui.PlayOneShot(c, BusVolume(AudioBus.Ui));
        }

        public void PlaySfx(string name, Vector3 position, float volume = 1f, float pitchJitter = 0.06f, string category = "Sfx")
        {
            if (!Throttle(category + name, 0.03f)) return;
            var c = Variation(category, name);
            if (c == null) return;
            AudioSource src = null;
            foreach (var s in _pool3D) if (!s.isPlaying) { src = s; break; }
            if (src == null) src = _pool3D[Random.Range(0, _pool3D.Count)];
            src.transform.position = position;
            src.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            src.volume = volume * BusVolume(category == "Voice" ? AudioBus.Voice : AudioBus.Effects);
            src.clip = c;
            src.Play();
        }

        public void Play2D(string category, string name, float volume = 1f, AudioBus bus = AudioBus.Effects)
        {
            var c = Variation(category, name);
            if (c != null) _ui.PlayOneShot(c, volume * BusVolume(bus));
        }

        /// <summary>Queues an announcer line; lines never overlap and stale ones are dropped.</summary>
        public void Announce(string key, float maxDelay = 3f)
        {
            if (_announcerQueue.Count > 3) return;
            _announcerQueue.Enqueue((key, Time.unscaledTime + maxDelay));
        }

        public void PlayMusic(string track, float fadeSeconds = 2f)
        {
            if (_musicTrack == track) return;
            _musicTrack = track;
            var clip = Clip("Music", track);
            _musicIndex = 1 - _musicIndex;
            var next = _music[_musicIndex];
            next.clip = clip;
            next.volume = 0f;
            if (clip != null) next.Play();
            _musicFade = 0f;
        }

        public void StopMusic() { PlayMusic(null); }

        public void PlayAmbience(string name)
        {
            var c = Clip("Ambience", name);
            if (_ambience.clip == c) return;
            _ambience.clip = c;
            if (c != null) _ambience.Play(); else _ambience.Stop();
        }

        private bool Throttle(string key, float seconds)
        {
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(key, out var t) && now - t < seconds) return false;
            _lastPlayed[key] = now;
            return true;
        }

        public void Tick(float dt)
        {
            AudioListener.volume = _settings.MuteWhenUnfocused && !Application.isFocused ? 0f : 1f;
            // Music crossfade.
            if (_musicFade < 1f) _musicFade = Mathf.Min(1f, _musicFade + dt / 2f);
            float mv = BusVolume(AudioBus.Music);
            _music[_musicIndex].volume = _musicFade * mv;
            var prev = _music[1 - _musicIndex];
            prev.volume = (1f - _musicFade) * mv;
            if (_musicFade >= 1f && prev.isPlaying) prev.Stop();
            _ambience.volume = BusVolume(AudioBus.Ambience) * 0.6f;
            // Announcer queue.
            _announcerCooldown -= dt;
            if (_announcerCooldown <= 0f && !_announcer.isPlaying && _announcerQueue.Count > 0)
            {
                var (key, deadline) = _announcerQueue.Dequeue();
                if (Time.unscaledTime <= deadline)
                {
                    var c = Clip("Announcer", key);
                    if (c != null)
                    {
                        _announcer.clip = c;
                        _announcer.volume = BusVolume(AudioBus.Announcer);
                        _announcer.Play();
                        _announcerCooldown = 0.25f;
                    }
                }
            }
        }
    }
}
