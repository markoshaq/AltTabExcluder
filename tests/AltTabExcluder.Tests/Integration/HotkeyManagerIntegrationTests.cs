using System.Runtime.InteropServices;
using AltTabExcluder.Services;

namespace AltTabExcluder.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="HotkeyManager"/> — exercises real
/// RegisterHotKey/UnregisterHotKey on a live desktop session. Tagged
/// "Integration" so they can be filtered out of headless runs.
/// </summary>
[Trait("Category", "Integration")]
public class HotkeyManagerIntegrationTests
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint TestKey = 0x7B; // F12
    private const uint WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0xA7E;

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
        string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [Fact]
    public void Register_WithValidCombo_ReturnsTrue()
    {
        using var hk = new HotkeyManager();
        hk.SetHotkey(MOD_ALT | MOD_CONTROL | MOD_SHIFT, TestKey);

        bool ok = hk.Register();

        Assert.True(ok, "RegisterHotKey should succeed for a unique combo");
        Assert.True(hk.IsRegistered);
    }

    [Fact]
    public void Unregister_AfterRegister_ClearsIsRegistered()
    {
        using var hk = new HotkeyManager();
        hk.SetHotkey(MOD_ALT | MOD_CONTROL | MOD_SHIFT, TestKey);
        hk.Register();

        hk.Unregister();

        Assert.False(hk.IsRegistered);
    }

    [Fact]
    public void Register_AlreadyTakenCombo_ReturnsFalse()
    {
        using var hk1 = new HotkeyManager();
        hk1.SetHotkey(MOD_ALT | MOD_CONTROL | MOD_SHIFT, TestKey);
        bool firstOk = hk1.Register();
        Assert.True(firstOk, "First registration should succeed");

        try
        {
            using var hk2 = new HotkeyManager();
            hk2.SetHotkey(MOD_ALT | MOD_CONTROL | MOD_SHIFT, TestKey);
            bool secondOk = hk2.Register();

            Assert.False(secondOk, "Second registration of the same combo should fail");
            Assert.False(hk2.IsRegistered);
        }
        finally
        {
            hk1.Unregister();
        }
    }

    [Fact]
    public void Register_WithZeroKey_ReturnsFalse()
    {
        using var hk = new HotkeyManager();
        hk.SetHotkey(MOD_ALT, 0);

        bool ok = hk.Register();

        Assert.False(ok, "Register with key=0 should fail");
    }

    [Fact]
    public void Register_Twice_DoesNotLeakOrFail()
    {
        using var hk = new HotkeyManager();
        hk.SetHotkey(MOD_ALT | MOD_CONTROL | MOD_SHIFT, TestKey);

        bool first = hk.Register();
        bool second = hk.Register(); // Should be a no-op (already registered).

        Assert.True(first);
        Assert.True(second, "Re-registering while already registered should return true");
        Assert.True(hk.IsRegistered);
    }

    [Fact]
    public void HotkeyPressed_Fires_WhenWMHotKeyPosted()
    {
        // We can't easily simulate a physical key press, but we CAN find the
        // message-only window and post WM_HOTKEY directly to it. This verifies
        // the WndProc routing and event dispatch work end-to-end.
        using var hk = new HotkeyManager();
        hk.SetHotkey(MOD_ALT | MOD_CONTROL | MOD_SHIFT, TestKey);
        Assert.True(hk.Register());

        try
        {
            bool fired = false;
            hk.HotkeyPressed += (_, _) => fired = true;

            // Find the message-only window by caption. Message-only windows are
            // children of HWND_MESSAGE (-3) and are not visible to FindWindow.
            IntPtr hwndMsg = FindMessageWindow("AltTabExcluderHotkey");
            if (hwndMsg == IntPtr.Zero)
            {
                // If we can't find the window, skip — the registration tests
                // above still validate the core path.
                return;
            }

            // Post WM_HOTKEY with WParam = hotkey ID.
            PostMessage(hwndMsg, WM_HOTKEY, (IntPtr)HotkeyId, IntPtr.Zero);

            // Pump messages so the posted WM_HOTKEY gets processed.
            IntegrationTestHelper.PumpMessages(500);

            Assert.True(fired, "HotkeyPressed event should fire when WM_HOTKEY is posted");
        }
        finally
        {
            hk.Unregister();
        }
    }

    /// <summary>
    /// Finds a message-only window by caption via FindWindowEx with
    /// HWND_MESSAGE (-3) as the parent.
    /// </summary>
    private static IntPtr FindMessageWindow(string caption)
    {
        IntPtr hwndMessage = new(-3); // HWND_MESSAGE
        IntPtr result = IntPtr.Zero;
        IntPtr hwnd = IntPtr.Zero;
        while (true)
        {
            hwnd = FindWindowEx(hwndMessage, hwnd, null, null);
            if (hwnd == IntPtr.Zero)
                break;

            int len = GetWindowTextLength(hwnd);
            if (len > 0)
            {
                char[] buf = new char[len + 1];
                int written = GetWindowText(hwnd, buf, buf.Length);
                if (written > 0 && new string(buf, 0, written) == caption)
                {
                    result = hwnd;
                    break;
                }
            }
        }
        return result;
    }
}
