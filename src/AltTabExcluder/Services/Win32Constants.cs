namespace AltTabExcluder.Services;

/// <summary>
/// Win32 virtual-key codes and hotkey modifier flags used across the app.
/// Centralized here so call sites read as self-documenting names rather than
/// raw hex values. Values match the Win32 SDK definitions in winuser.h.
/// </summary>
internal static class Win32Constants
{
    // ─── RegisterHotKey modifier flags (MOD_*) ───────────────────────────

    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Mask covering all user-selectable modifiers (excludes NoRepeat).</summary>
    public const uint MOD_USER_MASK = MOD_ALT | MOD_CONTROL | MOD_SHIFT | MOD_WIN;

    // ─── Virtual-key codes (VK_*) ────────────────────────────────────────

    public const uint VK_BACK = 0x08;
    public const uint VK_TAB = 0x09;
    public const uint VK_RETURN = 0x0D;
    public const uint VK_ESCAPE = 0x1B;
    public const uint VK_SPACE = 0x20;

    // Digits 0x30–0x39, Letters A–Z 0x41–0x5A — no named constants needed;
    // the character cast is self-documenting.

    public const uint VK_F1 = 0x70;
    public const uint VK_F12 = 0x7B;

    // OEM punctuation keys.
    public const uint VK_OEM_1 = 0xBA;  // ;:
    public const uint VK_OEM_PLUS = 0xBB;  // =+
    public const uint VK_OEM_COMMA = 0xBC;  // ,<
    public const uint VK_OEM_MINUS = 0xBD;  // -_
    public const uint VK_OEM_PERIOD = 0xBE;  // .>
    public const uint VK_OEM_2 = 0xBF;  // /?
    public const uint VK_OEM_3 = 0xC0;  // `~
    public const uint VK_OEM_4 = 0xDB;  // [{
    public const uint VK_OEM_5 = 0xDC;  // \|
    public const uint VK_OEM_6 = 0xDD;  // ]}

    // Left/right modifier keys (for GetAsyncKeyState queries in the hotkey picker).
    public const uint VK_LSHIFT = 0xA0;
    public const uint VK_RSHIFT = 0xA1;
    public const uint VK_LCONTROL = 0xA2;
    public const uint VK_RCONTROL = 0xA3;
    public const uint VK_LMENU = 0xA4;  // Left Alt
    public const uint VK_RMENU = 0xA5;  // Right Alt
    public const uint VK_LWIN = 0x5B;
    public const uint VK_RWIN = 0x5C;

    /// <summary>Default hotkey key code: 'X' (0x58).</summary>
    public const uint DefaultHotkeyKey = 0x58;

    /// <summary>Default hotkey modifiers: Win+Alt+NoRepeat.</summary>
    public const uint DefaultHotkeyModifiers = MOD_WIN | MOD_ALT | MOD_NOREPEAT;
}
