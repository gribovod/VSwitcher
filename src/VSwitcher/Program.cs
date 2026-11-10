using Microsoft.Win32;
using System.Text.Json;

namespace VSwitcher;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        using var mutex = new Mutex(true, @"Local\VSwitcher.Desktop", out bool first);
        if (!first) { MessageBox.Show("VSwitcher уже працює. Шукайте значок у треї."); return; }
        ApplicationConfiguration.Initialize();
        try { Application.Run(new Switcher()); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "VSwitcher — помилка"); }
    }
}

public sealed record Settings(uint Modifiers = 3, uint Key = (uint)Keys.Space)
{
    internal static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VSwitcher", "settings.json");
    internal static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }
    internal void Save() { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(this)); }
}

internal sealed class Switcher : Form
{
    private readonly HotkeyWindow hotkey = new();
    private readonly NotifyIcon tray;
    private readonly Engine engine;
    private Settings settings = Settings.Load();
    private bool busy;
    protected override void SetVisibleCore(bool value) => base.SetVisibleCore(false);
    internal Switcher()
    {
        _ = Handle;
        hotkey.Pressed += () => { if (!busy) _ = Correct(); };
        engine = new Engine(Path.Combine(AppContext.BaseDirectory, "Dictionaries"));
        var menu = new ContextMenuStrip();
        menu.Items.Add("Налаштування…", null, (_, _) => OpenSettings());
        menu.Items.Add("Як користуватися", null, (_, _) => MessageBox.Show("Виділіть помилково набраний текст і натисніть налаштовану комбінацію (типово Ctrl+Alt+Space).\n\nЯкщо мова неоднозначна — оберіть варіант у вікні. Esc скасовує дію.\nСкасування вставлення: Ctrl+Z у редакторі.\n\nОбробка локальна. Текст не зберігається. Термінали, захищені поля та RDP потребують окремої перевірки.", "VSwitcher"));
        menu.Items.Add("Вийти", null, (_, _) => { if (!busy) Close(); });
        tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "VSwitcher — виправлення розкладки", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => OpenSettings();
        if (!Register(settings)) { MessageBox.Show("Гаряча клавіша зайнята. Оберіть іншу комбінацію.", "VSwitcher"); BeginInvoke(OpenSettings); }
        else
        {
            Status("Гаряча клавіша зареєстрована");
            tray.ShowBalloonTip(3500, "VSwitcher працює", "Виправлення виділення увімкнене. Гаряча клавіша й довідка — у меню трея.", ToolTipIcon.Info);
        }
    }
    private bool Register(Settings s) => Native.RegisterHotKey(hotkey.Handle, 1, s.Modifiers | 0x4000, s.Key);
    private static void Status(string message)
    {
        try { var path = Path.GetDirectoryName(Settings.FilePath)!; Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "last-status.txt"), message); } catch { }
    }
    private void Notify(string message) { Status(message); tray.ShowBalloonTip(4000, "VSwitcher", message, ToolTipIcon.Info); }
    private static void EnsureTarget(IntPtr target)
    {
        if (Native.GetForegroundWindow() != target) throw new InvalidOperationException("Активне вікно змінилося. Виділіть текст і спробуйте ще раз.");
    }
    private static DataObject? Snapshot()
    {
        var source = Clipboard.GetDataObject();
        if (source == null) return null;
        var result = new DataObject();
        foreach (var format in source.GetFormats(false))
        {
            var value = source.GetData(format, false);
            if (value is Stream stream) { var copy = new MemoryStream(); stream.CopyTo(copy); copy.Position = 0; value = copy; }
            if (value != null) result.SetData(format, false, value);
        }
        return result;
    }
    private async Task Correct()
    {
        busy = true;
        Status("Початок виправлення");
        DataObject? saved = null;
        uint ownedSequence = 0;
        bool clipboardTouched = false;
        try
        {
            var target = Native.GetForegroundWindow();
            // Do not synthesize shortcuts while any activation modifier is held.
            for (int i = 0; i < 100 && new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => Native.GetAsyncKeyState(k) < 0); i++) await Task.Delay(20);
            if (new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(k => Native.GetAsyncKeyState(k) < 0)) throw new InvalidOperationException("Відпустіть клавіші й повторіть дію.");
            EnsureTarget(target);
            saved = Snapshot();
            uint before = Native.GetClipboardSequenceNumber();
            Native.Shortcut(0x43);
            for (int i = 0; i < 60 && Native.GetClipboardSequenceNumber() == before; i++) await Task.Delay(20);
            if (Native.GetClipboardSequenceNumber() == before) { Notify("Не вдалося скопіювати виділення. Перевірте, чи текст виділений і доступний для копіювання."); return; }
            ownedSequence = Native.GetClipboardSequenceNumber(); clipboardTouched = true;
            EnsureTarget(target);
            bool hasText = Clipboard.ContainsText();
            ownedSequence = Native.GetClipboardSequenceNumber();
            if (!hasText) { Notify("У виділенні немає звичайного тексту."); return; }
            var original = Clipboard.GetText();
            // OLE delayed rendering during GetText can advance the sequence itself.
            ownedSequence = Native.GetClipboardSequenceNumber();
            if (string.IsNullOrWhiteSpace(original)) return;
            if (original.Length > 100_000) throw new InvalidOperationException("Виділіть фрагмент до 100 000 символів.");
            var candidates = engine.Candidates(original);
            var chosen = engine.Automatic(original, candidates);
            if (chosen == null)
            {
                using var dialog = new ChoiceForm(candidates, original);
                if (dialog.ShowDialog() != DialogResult.OK) return;
                chosen = dialog.Selected;
                if (chosen == null) return;
                Native.SetForegroundWindow(target);
                await Task.Delay(150);
            }
            EnsureTarget(target);
            if (Native.GetClipboardSequenceNumber() != ownedSequence) throw new InvalidOperationException("Буфер обміну змінився. Повторіть виправлення.");
            if (chosen.Text == original) { Native.SwitchLayout(target, chosen.Target); return; }
            Clipboard.SetText(chosen.Text);
            ownedSequence = Native.GetClipboardSequenceNumber();
            EnsureTarget(target);
            Native.Shortcut(0x56);
            Status("Надіслано вставлення");
            await Task.Delay(800);
            EnsureTarget(target);
            Native.SwitchLayout(target, chosen.Target);
            Status("Виправлення завершено");
        }
        catch (Exception ex) { Notify(ex.Message); }
        finally
        {
            try
            {
                // Never overwrite clipboard changes made by the user or another app.
                if (clipboardTouched && Native.GetClipboardSequenceNumber() == ownedSequence)
                {
                    if (saved == null) Clipboard.Clear(); else Clipboard.SetDataObject(saved, true, 5, 100);
                }
            }
            catch { Notify("Не вдалося відновити буфер обміну: він зайнятий іншою програмою."); }
            busy = false;
        }
    }
    private void OpenSettings()
    {
        if (busy) return;
        busy = true;
        try
        {
        using var dialog = new SettingsForm(settings);
        if (dialog.ShowDialog() != DialogResult.OK) return;
        Native.UnregisterHotKey(hotkey.Handle, 1);
        if (!Register(dialog.Value)) { Register(settings); MessageBox.Show("Ця комбінація зайнята. Попередню залишено."); return; }
        try
        {
            dialog.Value.Save();
            using var run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (dialog.Autostart) run.SetValue("VSwitcher", "\"" + Environment.ProcessPath + "\""); else run.DeleteValue("VSwitcher", false);
            settings = dialog.Value;
        }
        catch (Exception ex) { MessageBox.Show("Не вдалося зберегти налаштування: " + ex.Message); }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "VSwitcher — налаштування"); }
        finally { busy = false; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { Native.UnregisterHotKey(hotkey.Handle, 1); hotkey.Dispose(); tray.Visible = false; tray.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    internal event Action? Pressed;
    internal HotkeyWindow() => CreateHandle(new CreateParams { Caption = "VSwitcher.Hotkey", Parent = new IntPtr(-3) });
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x312 && m.WParam == 1) Pressed?.Invoke();
        base.WndProc(ref m);
    }
    public void Dispose() => DestroyHandle();
}
