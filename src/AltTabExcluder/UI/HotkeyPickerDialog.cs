using System.Runtime.InteropServices;
using System.Windows.Forms;
using AltTabExcluder.Services;

namespace AltTabExcluder.UI;

/// <summary>
/// A small dialog that captures a keyboard shortcut from the user. Uses a
/// low-level <c>WH_KEYBOARD_LL</c> hook to reliably capture Win key combinations
/// (which Windows intercepts before regular key processing).
/// </summary>
public sealed class HotkeyPickerDialog : Form
{
    private readonly Label _label;
    private readonly TextBox _inputBox;
    private readonly Button _okButton;
    private readonly Button _cancelButton;

    private IntPtr _hook = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;

    /// <summary>Selected modifier flags (see <see cref="Win32Constants"/> MOD_* values).</summary>
    public uint Modifiers { get; private set; }

    /// <summary>Selected virtual key code.</summary>
    public uint Key { get; private set; }

    /// <summary>True if the user pressed a valid combo and clicked OK.</summary>
    public bool HasValidCombo { get; private set; }

    public HotkeyPickerDialog(uint currentModifiers, uint currentKey)
    {
        Modifiers = currentModifiers & ~Win32Constants.MOD_NOREPEAT; // strip NoRepeat for display
        Key = currentKey;

        Text = "Change Hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        Width = 360;
        Height = 180;

        _label = new Label
        {
            Text = "Press the key combination you want to use:",
            Left = 16, Top = 16,
            Width = 320,
        };

        _inputBox = new TextBox
        {
            Left = 16, Top = 44,
            Width = 320,
            ReadOnly = true,
            TabIndex = 0,
            Text = AppSettings.FormatHotkey(Modifiers, Key),
        };

        _okButton = new Button
        {
            Text = "OK",
            Left = 160, Top = 100,
            Width = 80,
            // DialogResult is set conditionally in the click handler — a bare
            // key with no modifier would globally intercept that key in every
            // app, so we reject it here rather than letting the dialog close.
        };

        _cancelButton = new Button
        {
            Text = "Cancel",
            Left = 256, Top = 100,
            Width = 80,
            DialogResult = DialogResult.Cancel,
        };

        _okButton.Click += (_, _) =>
        {
            if (Key != 0 && Modifiers != 0)
            {
                HasValidCombo = true;
                DialogResult = DialogResult.OK;
            }
            else
            {
                _label.Text = "Press a key with at least one modifier (Ctrl/Alt/Shift/Win):";
            }
        };

        Controls.AddRange(new Control[] { _label, _inputBox, _okButton, _cancelButton });
        AcceptButton = _okButton;
        CancelButton = _cancelButton;

        // Install the low-level hook when the form loads, remove on close.
        Load += (_, _) => InstallHook();
        FormClosing += (_, _) => UninstallHook();
    }

    // ─── Low-level keyboard hook ────────────────────────────────────────

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private void InstallHook()
    {
        _hookProc = HookCallback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
    }

    private void UninstallHook()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam.ToInt32() == WM_KEYDOWN || wParam.ToInt32() == WM_SYSKEYDOWN))
        {
            uint vk = (uint)Marshal.ReadInt32(lParam);

            // Ignore pure modifier keys.
            if (IsModifierKey(vk))
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            // Escape closes the dialog (let it through).
            if (vk == Win32Constants.VK_ESCAPE)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            // Build modifier flags from currently-held keys.
            uint mods = 0;
            if (IsKeyDown(Win32Constants.VK_LSHIFT) || IsKeyDown(Win32Constants.VK_RSHIFT)) mods |= Win32Constants.MOD_SHIFT;
            if (IsKeyDown(Win32Constants.VK_LCONTROL) || IsKeyDown(Win32Constants.VK_RCONTROL)) mods |= Win32Constants.MOD_CONTROL;
            if (IsKeyDown(Win32Constants.VK_LMENU) || IsKeyDown(Win32Constants.VK_RMENU)) mods |= Win32Constants.MOD_ALT;
            if (IsKeyDown(Win32Constants.VK_LWIN) || IsKeyDown(Win32Constants.VK_RWIN)) mods |= Win32Constants.MOD_WIN;

            Modifiers = mods;
            Key = vk;
            _inputBox.Text = AppSettings.FormatHotkey(Modifiers, Key);

            // Swallow the key so Windows doesn't act on Win+D etc.
            return (IntPtr)1;
        }

        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool IsModifierKey(uint vk)
        => vk is Win32Constants.VK_LSHIFT or Win32Constants.VK_RSHIFT
            or Win32Constants.VK_LCONTROL or Win32Constants.VK_RCONTROL
            or Win32Constants.VK_LMENU or Win32Constants.VK_RMENU
            or Win32Constants.VK_LWIN or Win32Constants.VK_RWIN;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool IsKeyDown(uint vKey)
        => (GetAsyncKeyState((int)vKey) & 0x8000) != 0;
}
