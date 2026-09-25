using System;
using System.Collections.Generic;
using System.Linq;
using Bloodfall.Client.Core;
using Bloodfall.Contracts;
using UnityEngine;
using UnityEngine.UIElements;

namespace Bloodfall.Client.UI.Screens
{
    // ====================================================================== splash

    public sealed class SplashScreen : UIScreen
    {
        private VisualElement _logo;
        private Label _press;
        private float _t;
        private bool _done;

        protected override void OnBuild(VisualElement root)
        {
            var vign = El.Div("fill", "vignette");
            vign.pickingMode = PickingMode.Ignore;
            root.Add(vign);
            var center = El.Div("fill", "col", "center");
            center.pickingMode = PickingMode.Ignore;
            _logo = El.Img("Textures/UI/logo_bloodfall", 900, 300);
            _logo.style.opacity = 0;
            center.Add(_logo);
            var sub = El.Text("WAR OF THE ANCIENTS", "t-title", "t-center");
            sub.style.letterSpacing = 8;
            center.Add(sub);
            _press = El.Text("Press any key to continue", "t-subheading", "t-center", "mt-l");
            _press.style.opacity = 0;
            center.Add(_press);
            root.Add(center);
            var foot = El.Text($"Bloodfall {GameApp.Instance.Config.ClientVersion} · Original game in development · Pre-alpha", "t-small");
            foot.style.position = Position.Absolute;
            foot.style.bottom = 12;
            foot.style.left = 16;
            root.Add(foot);
            root.pickingMode = PickingMode.Position;
            root.RegisterCallback<ClickEvent>(_ => Continue());
        }

        public override void OnShow() { _t = 0; _done = false; }

        public override void Tick(float dt)
        {
            _t += dt;
            _logo.style.opacity = Mathf.Clamp01(_t / 1.4f);
            _logo.style.scale = new Scale(Vector3.one * Mathf.Lerp(1.06f, 1f, Mathf.Clamp01(_t / 2f)));
            _press.style.opacity = _t > 1.6f ? 0.55f + 0.45f * Mathf.Sin(_t * 3f) : 0f;
            if (_t > 1.2f && (UnityEngine.Input.anyKeyDown || _t > 6f)) Continue();
        }

        private void Continue()
        {
            if (_done) return;
            _done = true;
            App.Flow.GoConnecting();
        }
    }

    // ====================================================================== service checks

    public sealed class ConnectingScreen : UIScreen
    {
        private VisualElement _list;
        private VisualElement _actions;
        private Label _title;

        protected override void OnBuild(VisualElement root)
        {
            root.Add(El.Div("fill", "vignette"));
            var col = El.Div("fill", "col", "center");
            col.Add(El.Img("Textures/UI/logo_bloodfall", 620, 206));
            var panel = El.Div("panel", "col").Width(620);
            _title = El.Text("Connecting to Bloodfall services", "t-title", "t-center");
            panel.Add(_title);
            panel.Add(El.Div("divider"));
            _list = El.Div("col", "mt-m");
            panel.Add(_list);
            var info = El.Text("Services: " + GameApp.Instance.Config.BackendUrl + (GameApp.Instance.Config.IsDevelopment ? "   (development environment)" : ""), "t-small", "t-center", "mt-m");
            panel.Add(info);
            _actions = El.Div("row", "center", "mt-l");
            panel.Add(_actions);
            col.Add(panel);
            root.Add(col);
            App.Flow.ChecksChanged += Refresh;
        }

        public override void OnShow() => Refresh();

        private void Refresh()
        {
            if (_list == null) return;
            _list.Clear();
            foreach (var c in App.Flow.Checks)
            {
                var row = El.Div("row", "mb-m");
                string mark; string cls;
                switch (c.Status)
                {
                    case CheckStatus.Ok: mark = "✔"; cls = "status-online"; break;
                    case CheckStatus.Warning: mark = "!"; cls = "status-degraded"; break;
                    case CheckStatus.Failed: mark = "✖"; cls = "status-offline"; break;
                    case CheckStatus.Running: mark = "…"; cls = "t-gold"; break;
                    case CheckStatus.Skipped: mark = "–"; cls = "t-muted"; break;
                    default: mark = "·"; cls = "t-muted"; break;
                }
                var m = El.Text(mark, "t-heading", cls).Width(26);
                row.Add(m);
                var textCol = El.Div("col", "grow");
                textCol.Add(El.Text(c.Label, "t-heading"));
                if (!string.IsNullOrEmpty(c.Detail)) textCol.Add(El.Text(c.Detail, "t-small", "t-wrap", c.Status == CheckStatus.Failed ? "status-offline" : ""));
                row.Add(textCol);
                _list.Add(row);
            }
            _actions.Clear();
            bool failed = App.Flow.ChecksFailed;
            bool dataOk = App.Data != null && string.IsNullOrEmpty(App.DataError);
            _title.text = failed ? "Unable to connect" : App.Flow.Checking ? "Connecting to Bloodfall services" : "Connected";
            if (failed && !App.Flow.Checking)
            {
                _actions.Add(El.Btn("Retry", () => App.Flow.GoConnecting(), "btn--primary"));
                if (dataOk) _actions.Add(El.Btn("Play Offline", () => App.Flow.GoOffline()));
                _actions.Add(El.Btn("Settings", () => SettingsDialog.Open()));
                _actions.Add(El.Btn("Quit", Application.Quit, "btn--ghost"));
            }
        }
    }

    // ====================================================================== login

    public sealed class LoginScreen : UIScreen
    {
        private TextField _login, _password;
        private Toggle _remember;
        private Button _submit;
        private Label _error;
        private Label _status;
        private bool _busy;

        protected override void OnBuild(VisualElement root)
        {
            root.Add(El.Div("fill", "vignette"));
            var left = El.Div("col");
            left.style.position = Position.Absolute;
            left.style.left = 70;
            left.style.top = 0;
            left.style.bottom = 0;
            left.style.justifyContent = Justify.Center;
            left.Add(El.Img("Textures/UI/logo_bloodfall", 520, 173));
            var panel = El.Div("panel", "col").Width(460);
            panel.Add(El.Text("Sign In", "t-title"));
            panel.Add(El.Div("divider"));
            _login = El.Field("Username or email", false, App.Settings.LastLogin, 254);
            _password = El.Field("Password", true, "", 128);
            _remember = El.Check("Remember me on this computer", true);
            _error = El.Text("", "field-error");
            _error.Show(false);
            _submit = El.Btn("Enter Velmoragh", Submit, "btn--primary", "btn--large");
            _submit.style.alignSelf = Align.Stretch;
            _password.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) Submit(); });
            _login.RegisterCallback<KeyDownEvent>(e => { if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) _password.Focus(); });
            panel.Add(_login);
            panel.Add(_password);
            panel.Add(_remember);
            panel.Add(_error);
            panel.Add(_submit);
            var links = El.Div("row", "space-between", "mt-m");
            links.Add(El.Link("Create an account", () => App.Flow.GoRegister()));
            links.Add(El.Link("Forgot password?", ForgotPassword));
            panel.Add(links);
            panel.Add(El.Div("divider", "mt-m"));
            var row = El.Div("row", "center");
            row.Add(El.Btn("Play Offline", () => App.Flow.GoOffline(), "btn--ghost", "btn--small"));
            row.Add(El.Btn("Settings", () => SettingsDialog.Open(), "btn--ghost", "btn--small"));
            row.Add(El.Btn("Quit", Application.Quit, "btn--ghost", "btn--small"));
            panel.Add(row);
            left.Add(panel);
            root.Add(left);

            _status = El.Text("", "t-small");
            _status.style.position = Position.Absolute;
            _status.style.right = 20;
            _status.style.bottom = 14;
            root.Add(_status);
        }

        public override void OnShow()
        {
            _busy = false;
            _submit.SetEnabled(true);
            _error.Show(false);
            _password.value = "";
            var s = App.Backend.Status;
            _status.text = s == null ? "Service status unknown" : $"{s.PlayersOnline} players online · {s.MatchesInProgress} matches in progress · {s.Environment}";
            if (string.IsNullOrEmpty(_login.value)) _login.Focus(); else _password.Focus();
        }

        private async void Submit()
        {
            if (_busy) return;
            if (string.IsNullOrWhiteSpace(_login.value) || string.IsNullOrEmpty(_password.value)) { ShowError("Enter your username (or email) and password."); return; }
            _busy = true;
            _submit.SetEnabled(false);
            _submit.text = "Signing in…";
            var r = await App.Backend.Login(_login.value.Trim(), _password.value, _remember.value);
            _busy = false;
            _submit.SetEnabled(true);
            _submit.text = "Enter Velmoragh";
            if (!r.Ok) { ShowError(r.Message ?? "Login failed."); return; }
            App.Settings.LastLogin = _login.value.Trim();
            App.Settings.Save();
            _password.value = "";
            App.Flow.OnAuthenticated();
        }

        private void ShowError(string msg)
        {
            _error.text = msg;
            _error.Show(true);
            App.Audio.PlayUi("error");
        }

        private void ForgotPassword()
        {
            var d = El.Div("dialog", "col").Width(520);
            d.Add(El.Text("Reset Password", "t-title", "t-center"));
            d.Add(El.Div("divider"));
            d.Add(El.Text("Enter the email address of your account. If it exists, a reset code is sent to it.", "t-body"));
            var email = El.Field("Email");
            d.Add(email);
            var msg = El.Text("", "t-body", "mt-m");
            d.Add(msg);
            var row = El.Div("row", "center", "mt-m");
            VisualElement ov = null;
            row.Add(El.Btn("Send code", async () =>
            {
                if (string.IsNullOrWhiteSpace(email.value)) { msg.text = "Enter an email address."; return; }
                var r = await App.Backend.ForgotPassword(email.value.Trim());
                msg.text = r.Ok || r.Status == 202 ? "If an account exists for that address, a reset code has been sent." : r.Message;
            }, "btn--primary"));
            row.Add(El.Btn("I have a code", () => { UI.CloseOverlay(ov); ResetWithCode(); }));
            row.Add(El.Btn("Close", () => UI.CloseOverlay(ov), "btn--ghost"));
            d.Add(row);
            ov = UI.ShowOverlay(d);
        }

        private void ResetWithCode()
        {
            var d = El.Div("dialog", "col").Width(520);
            d.Add(El.Text("Choose a New Password", "t-title", "t-center"));
            d.Add(El.Div("divider"));
            var code = El.Field("Reset code");
            var pw = El.Field("New password", true);
            var pw2 = El.Field("Confirm new password", true);
            var msg = El.Text("", "field-error");
            d.Add(code); d.Add(pw); d.Add(pw2); d.Add(msg);
            var row = El.Div("row", "center");
            VisualElement ov = null;
            row.Add(El.Btn("Reset", async () =>
            {
                if (pw.value != pw2.value) { msg.text = "Passwords do not match."; return; }
                var r = await App.Backend.ResetPassword(code.value.Trim(), pw.value);
                if (r.Ok) { UI.CloseOverlay(ov); UI.Toast("Password changed. You can sign in now.", ToastKind.Success); }
                else msg.text = r.Message;
            }, "btn--primary"));
            row.Add(El.Btn("Cancel", () => UI.CloseOverlay(ov), "btn--ghost"));
            d.Add(row);
            ov = UI.ShowOverlay(d);
        }

        public override bool OnEscape() => false;
    }

    // ====================================================================== register

    public sealed class RegisterScreen : UIScreen
    {
        private TextField _username, _email, _display, _password, _confirm;
        private Label _userHint, _error, _pwHint;
        private DropdownField _region;
        private Toggle _terms;
        private Button _submit;
        private List<string> _regionIds = new List<string>();
        private float _checkTimer = -1f;
        private string _lastChecked;
        private bool _busy;

        protected override void OnBuild(VisualElement root)
        {
            root.Add(El.Div("fill", "vignette"));
            var col = El.Div("fill", "col", "center");
            var panel = El.Div("panel", "col").Width(560);
            panel.Add(El.Text("Create Your Account", "t-title", "t-center"));
            panel.Add(El.Div("divider"));
            _username = El.Field("Username (3-16 letters, digits, _ or -)", false, "", 16);
            _userHint = El.Text("", "t-small");
            _username.RegisterValueChangedCallback(_ => { _checkTimer = 0.5f; _userHint.text = ""; });
            _email = El.Field("Email", false, "", 254);
            _display = El.Field("Display name (optional)", false, "", 24);
            _password = El.Field("Password (at least 10 characters)", true, "", 128);
            _pwHint = El.Text("", "t-small");
            _password.RegisterValueChangedCallback(e => _pwHint.text = Strength(e.newValue));
            _confirm = El.Field("Confirm password", true, "", 128);
            _region = El.Dropdown("Home region", new List<string> { "Automatic" }, 0, null);
            _terms = El.Check("I agree to the Bloodfall Code of Conduct (no cheating, harassment or account sharing).", false);
            _error = El.Text("", "field-error");
            _error.Show(false);
            _submit = El.Btn("Create Account", Submit, "btn--primary", "btn--large");
            _submit.style.alignSelf = Align.Stretch;
            panel.Add(_username); panel.Add(_userHint); panel.Add(_email); panel.Add(_display);
            panel.Add(_password); panel.Add(_pwHint); panel.Add(_confirm); panel.Add(_region); panel.Add(_terms);
            panel.Add(_error); panel.Add(_submit);
            panel.Add(El.Link("Back to sign in", () => App.Flow.GoLogin()).Cls("mt-m"));
            col.Add(panel);
            root.Add(col);
        }

        private static string Strength(string pw)
        {
            if (string.IsNullOrEmpty(pw)) return "";
            int score = 0;
            if (pw.Length >= 10) score++;
            if (pw.Length >= 14) score++;
            if (pw.Any(char.IsDigit)) score++;
            if (pw.Any(char.IsUpper) && pw.Any(char.IsLower)) score++;
            if (pw.Any(c => !char.IsLetterOrDigit(c))) score++;
            return pw.Length < 10 ? "Too short" : score <= 2 ? "Strength: weak" : score == 3 ? "Strength: fair" : "Strength: strong";
        }

        public override void OnShow()
        {
            _busy = false;
            _submit.SetEnabled(true);
            _error.Show(false);
            var regions = App.Backend.Regions;
            _regionIds = new List<string> { "" };
            var names = new List<string> { "Automatic" };
            foreach (var r in regions) { _regionIds.Add(r.Id); names.Add(r.Name); }
            _region.choices = names;
            _region.index = 0;
            _username.Focus();
        }

        public override void Tick(float dt)
        {
            if (_checkTimer > 0f)
            {
                _checkTimer -= dt;
                if (_checkTimer <= 0f) CheckName();
            }
        }

        private async void CheckName()
        {
            var name = _username.value?.Trim();
            if (string.IsNullOrEmpty(name) || name == _lastChecked) return;
            _lastChecked = name;
            var r = await App.Backend.CheckUsername(name);
            if (_username.value?.Trim() != name) return;
            if (!r.Ok) { _userHint.text = ""; return; }
            _userHint.text = r.Value.Available ? "✔ Available" : "✖ " + (r.Value.Reason ?? "Not available");
            _userHint.EnableInClassList("status-online", r.Value.Available);
            _userHint.EnableInClassList("status-offline", !r.Value.Available);
        }

        private async void Submit()
        {
            if (_busy) return;
            if (_password.value != _confirm.value) { ShowError("Passwords do not match."); return; }
            if (!_terms.value) { ShowError("You must accept the Code of Conduct."); return; }
            _busy = true;
            _submit.SetEnabled(false);
            var req = new RegisterRequest
            {
                Username = _username.value?.Trim(),
                Email = _email.value?.Trim(),
                Password = _password.value,
                DisplayName = string.IsNullOrWhiteSpace(_display.value) ? null : _display.value.Trim(),
                Region = _region.index > 0 && _region.index < _regionIds.Count ? _regionIds[_region.index] : null,
                Language = "en",
            };
            var r = await App.Backend.Register(req);
            _busy = false;
            _submit.SetEnabled(true);
            if (!r.Ok)
            {
                var msg = r.Message ?? "Registration failed.";
                if (r.Error?.Fields != null && r.Error.Fields.Count > 0) msg = string.Join("\n", r.Error.Fields.Values);
                ShowError(msg);
                return;
            }
            _password.value = _confirm.value = "";
            UI.Toast("Welcome to Bloodfall, " + r.Value.Account.DisplayName + "!", ToastKind.Success);
            App.Flow.OnAuthenticated();
        }

        private void ShowError(string msg)
        {
            _error.text = msg;
            _error.Show(true);
            App.Audio.PlayUi("error");
        }

        public override bool OnEscape() { App.Flow.GoLogin(); return true; }
    }
}
