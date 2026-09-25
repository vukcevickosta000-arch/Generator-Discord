using Bloodfall.Client.Core;
using Bloodfall.Client.Input;
using UnityEngine;

namespace Bloodfall.Client.Match
{
    /// <summary>
    /// Classic MOBA camera: fixed pitch/yaw perspective looking at a focus point on the terrain. Edge scrolling,
    /// arrow keys, middle-mouse drag, smooth zoom, hero centring / lock, minimap jumps, bounds and shake.
    /// </summary>
    public sealed class MobaCamera
    {
        public readonly Camera Cam;
        public readonly AudioListener Listener;
        private readonly MapRenderer _map;
        private readonly ClientSettings _settings;
        private Vector3 _focus;
        private Vector3 _focusTarget;
        private float _zoom = 0.45f, _zoomTarget = 0.45f;
        private float _shake;
        private Vector3 _dragAnchor;
        private bool _dragging;
        public bool Locked;
        public float Pitch = 58f;
        public float Yaw = 0f;
        public const float MinDistance = 16f, MaxDistance = 34f;

        public MobaCamera(Transform parent, MapRenderer map, ClientSettings settings)
        {
            _map = map;
            _settings = settings;
            var go = new GameObject("MatchCamera");
            go.transform.SetParent(parent, false);
            Cam = go.AddComponent<Camera>();
            Cam.fieldOfView = 38f;
            Cam.nearClipPlane = 1f;
            Cam.farClipPlane = 160f;
            Cam.clearFlags = CameraClearFlags.SolidColor;
            Cam.backgroundColor = new Color(0.03f, 0.025f, 0.035f);
            Cam.depth = 0;
            Listener = go.AddComponent<AudioListener>();
            RenderPipelineBridge.SetupCamera(Cam, settings);
        }

        public Vector3 Focus => _focus;
        public float Distance => Mathf.Lerp(MinDistance, MaxDistance, _zoom);

        public void JumpTo(Vector3 worldPoint, bool instant = false)
        {
            _focusTarget = ClampFocus(worldPoint);
            if (instant) _focus = _focusTarget;
        }

        public void Shake(float amount, Vector3 at)
        {
            float d = Vector3.Distance(at, _focus);
            float falloff = Mathf.Clamp01(1f - d / 45f);
            _shake = Mathf.Max(_shake, amount * falloff);
        }

        private Vector3 ClampFocus(Vector3 p)
        {
            p.x = Mathf.Clamp(p.x, 8f, _map.Width - 8f);
            p.z = Mathf.Clamp(p.z, 4f, _map.Depth - 14f);
            p.y = _map.HeightAt(p.x, p.z);
            return p;
        }

        /// <summary>Per-frame control. <paramref name="follow"/> is the local hero position (for lock/centre).</summary>
        public void Update(float dt, bool inputEnabled, Vector3? follow, bool centerHeld)
        {
            if (inputEnabled)
            {
                var mouse = InputBridge.MousePosition;
                float speed = 38f * _settings.CameraSpeed * Mathf.Lerp(0.8f, 1.35f, _zoom);
                Vector3 move = Vector3.zero;
                bool mouseIn = InputBridge.MouseInWindow;
                if (_settings.EdgeScrolling && mouseIn && Application.isFocused)
                {
                    const float edge = 6f;
                    if (mouse.x <= edge) move.x -= 1;
                    if (mouse.x >= Screen.width - edge) move.x += 1;
                    if (mouse.y <= edge) move.z -= 1;
                    if (mouse.y >= Screen.height - edge) move.z += 1;
                    move *= _settings.EdgeScrollSpeed;
                }
                if (InputBridge.GetKey(KeyCode.LeftArrow)) move.x -= 1;
                if (InputBridge.GetKey(KeyCode.RightArrow)) move.x += 1;
                if (InputBridge.GetKey(KeyCode.DownArrow)) move.z -= 1;
                if (InputBridge.GetKey(KeyCode.UpArrow)) move.z += 1;
                if (move.sqrMagnitude > 0.01f)
                {
                    Locked = false;
                    var rot = Quaternion.Euler(0, Yaw, 0);
                    _focusTarget = ClampFocus(_focusTarget + rot * move * speed * dt);
                }

                // Middle-mouse drag pans the ground under the cursor.
                if (InputBridge.GetMouseButtonDown(2) && RaycastGround(mouse, out var anchor)) { _dragging = true; _dragAnchor = anchor; Locked = false; }
                if (!InputBridge.GetMouseButton(2)) _dragging = false;
                if (_dragging && RaycastGround(mouse, out var now))
                {
                    var delta = _dragAnchor - now;
                    if (_settings.InvertDrag) delta = -delta;
                    delta.y = 0;
                    _focusTarget = ClampFocus(_focusTarget + delta);
                    _focus = ClampFocus(_focus + delta);
                }

                float scroll = InputBridge.Scroll;
                if (Mathf.Abs(scroll) > 0.01f) _zoomTarget = Mathf.Clamp01(_zoomTarget - scroll * 0.08f * _settings.ZoomSpeed);
            }
            if (follow.HasValue && (Locked || centerHeld)) _focusTarget = ClampFocus(follow.Value);

            _focus = Vector3.Lerp(_focus, _focusTarget, 1f - Mathf.Exp(-dt * (Locked || centerHeld ? 14f : 18f)));
            _focus.y = Mathf.Lerp(_focus.y, _map.HeightAt(_focus.x, _focus.z), 1f - Mathf.Exp(-dt * 6f));
            _zoom = Mathf.Lerp(_zoom, _zoomTarget, 1f - Mathf.Exp(-dt * 10f));
            Apply(dt);
        }

        private void Apply(float dt)
        {
            float pitch = Mathf.Lerp(Pitch + 6f, Pitch - 4f, _zoom);
            var rot = Quaternion.Euler(pitch, Yaw, 0);
            var pos = _focus - rot * Vector3.forward * Distance;
            if (_shake > 0.001f)
            {
                pos += new Vector3(Mathf.PerlinNoise(Time.time * 30f, 0f) - 0.5f, Mathf.PerlinNoise(0f, Time.time * 30f) - 0.5f, 0f) * _shake * 0.9f;
                _shake = Mathf.Max(0f, _shake - dt * 2.5f);
            }
            Cam.transform.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Intersects a screen ray with the heightfield (ray march + bisection).</summary>
        public bool RaycastGround(Vector2 screen, out Vector3 hit)
        {
            var ray = Cam.ScreenPointToRay(screen);
            float t = 0f, step = 1.5f;
            float prevT = 0f;
            for (int i = 0; i < 200; i++)
            {
                var p = ray.origin + ray.direction * t;
                float h = _map.HeightAt(p.x, p.z);
                if (p.y <= h)
                {
                    float lo = prevT, hi = t;
                    for (int k = 0; k < 12; k++)
                    {
                        float mid = (lo + hi) * 0.5f;
                        var m = ray.origin + ray.direction * mid;
                        if (m.y <= _map.HeightAt(m.x, m.z)) hi = mid; else lo = mid;
                    }
                    hit = ray.origin + ray.direction * hi;
                    return hit.x >= 0 && hit.z >= 0 && hit.x <= _map.Width && hit.z <= _map.Depth;
                }
                prevT = t;
                t += step;
            }
            hit = Vector3.zero;
            return false;
        }

        /// <summary>Ground-plane corners of the view (for the minimap frustum outline).</summary>
        public Vector3[] ViewCorners()
        {
            var corners = new Vector3[4];
            Vector2[] screen = { new Vector2(0, 0), new Vector2(Screen.width, 0), new Vector2(Screen.width, Screen.height), new Vector2(0, Screen.height) };
            for (int i = 0; i < 4; i++)
            {
                var ray = Cam.ScreenPointToRay(screen[i]);
                float planeY = _focus.y;
                float t = Mathf.Abs(ray.direction.y) > 1e-4f ? (planeY - ray.origin.y) / ray.direction.y : 200f;
                if (t < 0 || t > 400f) t = 400f;
                corners[i] = ray.origin + ray.direction * t;
            }
            return corners;
        }
    }
}
