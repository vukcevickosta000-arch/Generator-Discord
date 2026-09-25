using System.Collections.Generic;
using UnityEngine;
#if BLOODFALL_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Bloodfall.Client.Input
{
    /// <summary>
    /// Thin input facade so gameplay code works with either the legacy Input Manager or the Input System package
    /// (whichever the project has enabled). Keys are expressed as KeyCode everywhere else in the client.
    /// </summary>
    public static class InputBridge
    {
#if BLOODFALL_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        private static readonly Dictionary<KeyCode, Key> Map = BuildMap();

        private static Dictionary<KeyCode, Key> BuildMap()
        {
            var m = new Dictionary<KeyCode, Key>();
            for (KeyCode k = KeyCode.A; k <= KeyCode.Z; k++) m[k] = Key.A + (k - KeyCode.A);
            for (KeyCode k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) m[k] = k == KeyCode.Alpha0 ? Key.Digit0 : Key.Digit1 + (k - KeyCode.Alpha1);
            for (KeyCode k = KeyCode.F1; k <= KeyCode.F12; k++) m[k] = Key.F1 + (k - KeyCode.F1);
            m[KeyCode.Space] = Key.Space; m[KeyCode.Tab] = Key.Tab; m[KeyCode.Return] = Key.Enter; m[KeyCode.KeypadEnter] = Key.NumpadEnter;
            m[KeyCode.Escape] = Key.Escape; m[KeyCode.Backspace] = Key.Backspace; m[KeyCode.Delete] = Key.Delete;
            m[KeyCode.LeftShift] = Key.LeftShift; m[KeyCode.RightShift] = Key.RightShift; m[KeyCode.LeftControl] = Key.LeftCtrl; m[KeyCode.RightControl] = Key.RightCtrl;
            m[KeyCode.LeftAlt] = Key.LeftAlt; m[KeyCode.RightAlt] = Key.RightAlt; m[KeyCode.UpArrow] = Key.UpArrow; m[KeyCode.DownArrow] = Key.DownArrow;
            m[KeyCode.LeftArrow] = Key.LeftArrow; m[KeyCode.RightArrow] = Key.RightArrow; m[KeyCode.BackQuote] = Key.Backquote; m[KeyCode.Minus] = Key.Minus; m[KeyCode.Equals] = Key.Equals;
            return m;
        }

        public static bool GetKey(KeyCode k) => Keyboard.current != null && Map.TryGetValue(k, out var key) && Keyboard.current[key].isPressed;
        public static bool GetKeyDown(KeyCode k) => Keyboard.current != null && Map.TryGetValue(k, out var key) && Keyboard.current[key].wasPressedThisFrame;
        public static bool GetKeyUp(KeyCode k) => Keyboard.current != null && Map.TryGetValue(k, out var key) && Keyboard.current[key].wasReleasedThisFrame;
        public static Vector2 MousePosition => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        public static float Scroll => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
        public static bool GetMouseButton(int b) => Mouse.current != null && Button(b).isPressed;
        public static bool GetMouseButtonDown(int b) => Mouse.current != null && Button(b).wasPressedThisFrame;
        public static bool GetMouseButtonUp(int b) => Mouse.current != null && Button(b).wasReleasedThisFrame;
        private static UnityEngine.InputSystem.Controls.ButtonControl Button(int b) => b == 0 ? Mouse.current.leftButton : b == 1 ? Mouse.current.rightButton : Mouse.current.middleButton;
        public static bool Shift => GetKey(KeyCode.LeftShift) || GetKey(KeyCode.RightShift);
        public static bool Ctrl => GetKey(KeyCode.LeftControl) || GetKey(KeyCode.RightControl);
        public static bool Alt => GetKey(KeyCode.LeftAlt) || GetKey(KeyCode.RightAlt);
#else
        public static bool GetKey(KeyCode k) => UnityEngine.Input.GetKey(k);
        public static bool GetKeyDown(KeyCode k) => UnityEngine.Input.GetKeyDown(k);
        public static bool GetKeyUp(KeyCode k) => UnityEngine.Input.GetKeyUp(k);
        public static Vector2 MousePosition => UnityEngine.Input.mousePosition;
        public static float Scroll => UnityEngine.Input.mouseScrollDelta.y;
        public static bool GetMouseButton(int b) => UnityEngine.Input.GetMouseButton(b);
        public static bool GetMouseButtonDown(int b) => UnityEngine.Input.GetMouseButtonDown(b);
        public static bool GetMouseButtonUp(int b) => UnityEngine.Input.GetMouseButtonUp(b);
        public static bool Shift => UnityEngine.Input.GetKey(KeyCode.LeftShift) || UnityEngine.Input.GetKey(KeyCode.RightShift);
        public static bool Ctrl => UnityEngine.Input.GetKey(KeyCode.LeftControl) || UnityEngine.Input.GetKey(KeyCode.RightControl);
        public static bool Alt => UnityEngine.Input.GetKey(KeyCode.LeftAlt) || UnityEngine.Input.GetKey(KeyCode.RightAlt);
#endif
        /// <summary>Mouse inside the game window (edge scrolling must stop when the cursor leaves it).</summary>
        public static bool MouseInWindow
        {
            get
            {
                var p = MousePosition;
                return p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height && Application.isFocused;
            }
        }
    }
}
