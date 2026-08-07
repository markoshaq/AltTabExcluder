using System.Windows.Forms;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace AltTabExcluder.Services;

/// <summary>
/// Registers a global hotkey and dispatches <c>WM_HOTKEY</c> via a hidden
/// <see cref="NativeWindow"/>. Global hotkeys are process-wide and survive focus
/// changes, so this is the primary, reliable mechanism for toggling the
/// <em>focused</em> window's Alt+Tab visibility — including Electron/custom-frame
/// apps where title-bar menu injection is not possible.
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0xA7E;

    private HotkeyWindow? _window;
    private bool _registered;
    private bool _disposed;

    /// <summary>Current modifier flags (without NoRepeat).</summary>
    public uint Modifiers { get; private set; }

    /// <summary>Current virtual key code.</summary>
    public uint Key { get; private set; }

    /// <summary>Raised when the registered hotkey is pressed. Runs on the UI thread.</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>True while the hotkey is actively registered with the OS.</summary>
    public bool IsRegistered => _registered;

    /// <summary>
    /// Configures the hotkey to use. Must be called before <see cref="Register"/>
    /// or after <see cref="Unregister"/> to change an active registration.
    /// The NoRepeat flag (0x4000) is stripped — it is added internally by
    /// <see cref="Register"/> and should not be stored in <see cref="Modifiers"/>.
    /// </summary>
    public void SetHotkey(uint modifiers, uint key)
    {
        Modifiers = modifiers & ~MOD_NOREPEAT;
        Key = key;
    }

    /// <summary>
    /// Registers the configured hotkey. Returns <c>false</c> if the OS rejected
    /// the registration (usually because another app owns it).
    /// </summary>
    public bool Register()
    {
        if (_registered)
            return true;
        if (Key == 0)
            return false;

        // Destroy any previous window so re-registering (e.g. after a hotkey
        // change) doesn't leak a native HWND each time.
        _window?.DestroyHandle();
        _window = new HotkeyWindow();
        _window.HotkeyReceived += () => HotkeyPressed?.Invoke(this, EventArgs.Empty);

        bool ok = PInvoke.RegisterHotKey((HWND)_window.Handle, HotkeyId,
            (HOT_KEY_MODIFIERS)(Modifiers | MOD_NOREPEAT), Key);
        _registered = ok;
        return ok;
    }

    public void Unregister()
    {
        if (!_registered || _window is null)
            return;

        try { PInvoke.UnregisterHotKey((HWND)_window.Handle, HotkeyId); }
        catch { /* best effort on teardown */ }
        _registered = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _window?.DestroyHandle();
        _window = null;
        _disposed = true;
    }

    /// <summary>Hidden message-only window that receives WM_HOTKEY.</summary>
    private sealed class HotkeyWindow : NativeWindow
    {
        public event Action? HotkeyReceived;

        public HotkeyWindow()
        {
            var cp = new CreateParams
            {
                Caption = "AltTabExcluderHotkey",
                Parent = (IntPtr)(-3), // HWND_MESSAGE
            };
            CreateHandle(cp);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
                HotkeyReceived?.Invoke();
            base.WndProc(ref m);
        }
    }
}
