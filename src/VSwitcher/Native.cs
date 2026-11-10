using System.Runtime.InteropServices;
using System.Text;

namespace VSwitcher;

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, IntPtr process);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")] internal static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern short VkKeyScanEx(char character, IntPtr layout);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyEx(uint key, uint type, IntPtr layout);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, StringBuilder output, int count, uint flags, IntPtr layout);
    [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public Keyboard Keyboard;
        [FieldOffset(0)] public Mouse Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    internal static void Shortcut(ushort key)
    {
        Input K(ushort k, bool up) => new() { Type = 1, Data = new InputUnion { Keyboard = new Keyboard { Key = k, Flags = up ? 2u : 0 } } };
        var input = new[] { K(0x11, false), K(key, false), K(key, true), K(0x11, true) };
        if (SendInput(4, input, Marshal.SizeOf<Input>()) != 4) throw new InvalidOperationException("Windows заблокувала введення. Перевірте рівень прав цільової програми.");
    }
    private static readonly Dictionary<Language, IntPtr> Layouts = new();
    internal static IntPtr Layout(Language language)
    {
        lock (Layouts)
        {
            if (Layouts.TryGetValue(language, out var found)) return found;
            int languageId = language switch { Language.Ukrainian => 0x422, Language.Russian => 0x419, _ => 0x409 };
            // A language can have another language's keyboard assigned to it.
            // Request exact standard layouts rather than trusting HKL's language bits.
            found = LoadKeyboardLayout(language == Language.Ukrainian ? "00020422" : languageId.ToString("x8"), 0);
            if (found == IntPtr.Zero) throw new InvalidOperationException("Розкладка недоступна: " + Engine.Name(language));
            Layouts[language] = found;
            return found;
        }
    }
    internal static char Translate(char character, Language source, Language target)
    {
        if (char.IsControl(character) || char.IsSurrogate(character)) return character;
        var from = Layout(source); var to = Layout(target);
        short key = VkKeyScanEx(character, from);
        if (key == -1) return character;
        var state = new byte[256];
        if ((key & 0x100) != 0) state[0x10] = 0x80;
        if ((key & 0x200) != 0) state[0x11] = 0x80;
        if ((key & 0x400) != 0) state[0x12] = 0x80;
        uint scan = MapVirtualKeyEx((uint)(key & 255), 0, from);
        uint targetKey = MapVirtualKeyEx(scan, 3, to);
        var output = new StringBuilder(8);
        // Flag 4 keeps dead-key state unchanged (Windows 10 1607 and later).
        int count = ToUnicodeEx(targetKey, scan, state, output, 8, 4, to);
        return count == 1 && !char.IsControl(output[0]) ? output[0] : character;
    }
    internal static void SwitchLayout(IntPtr window, Language language)
    {
        var layout = Layout(language);
        if (layout == IntPtr.Zero || !PostMessage(window, 0x50, IntPtr.Zero, layout))
            throw new InvalidOperationException("Не вдалося перемкнути розкладку Windows.");
    }
}
