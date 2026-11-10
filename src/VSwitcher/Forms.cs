using Microsoft.Win32;

namespace VSwitcher;

internal sealed class ChoiceForm : Form
{
    private readonly ListBox list = new() { Dock = DockStyle.Fill, DisplayMember = "Label" };
    private readonly TextBox preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new Font("Segoe UI", 12) };
    internal Candidate? Selected => (list.SelectedItem as Item)?.Candidate;
    private record Item(Candidate Candidate, string Label);
    internal ChoiceForm(List<Candidate> candidates, string original)
    {
        Text = "VSwitcher — оберіть правильний варіант"; Size = new Size(740, 510); MinimumSize = new Size(550, 400); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.Controls.Add(new Label { Text = "Мова неоднозначна. Оберіть результат і перевірте текст нижче.", Dock = DockStyle.Fill }, 0, 0);
        foreach (var c in candidates) list.Items.Add(new Item(c, $"{Engine.Name(c.Target)} · з {Engine.Name(c.Source)}{(c.Text == original ? " · без зміни тексту" : "")} — {c.Text.Replace("\r", " ").Replace("\n", " ")[..Math.Min(65, c.Text.Length)]}"));
        list.SelectedIndexChanged += (_, _) => preview.Text = Selected?.Text;
        list.DoubleClick += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        layout.Controls.Add(list, 0, 1); layout.Controls.Add(preview, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = "Застосувати", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Скасувати", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout); AcceptButton = ok; CancelButton = cancel; list.SelectedIndex = 0;
    }
}

internal sealed class SettingsForm : Form
{
    private readonly CheckBox ctrl = new() { Text = "Ctrl", AutoSize = true }, alt = new() { Text = "Alt", AutoSize = true }, shift = new() { Text = "Shift", AutoSize = true }, startup = new() { Text = "Запускати разом із Windows", AutoSize = true };
    private readonly ComboBox key = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
    internal Settings Value => new((uint)((alt.Checked ? 1 : 0) | (ctrl.Checked ? 2 : 0) | (shift.Checked ? 4 : 0)), (uint)(Keys)key.SelectedItem!);
    internal bool Autostart => startup.Checked;
    internal SettingsForm(Settings settings)
    {
        Text = "VSwitcher — налаштування"; ClientSize = new Size(440, 240); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; StartPosition = FormStartPosition.CenterScreen;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        panel.Controls.Add(new Label { Text = "Виправити виділений текст:", AutoSize = true });
        var row = new FlowLayoutPanel { Width = 400, Height = 40 };
        row.Controls.AddRange([ctrl, alt, shift, key]); panel.Controls.Add(row);
        key.Items.Add(Keys.Space); for (var k = Keys.F1; k <= Keys.F12; k++) key.Items.Add(k);
        for (var k = Keys.A; k <= Keys.Z; k++) key.Items.Add(k);
        key.SelectedItem = (Keys)settings.Key; if (key.SelectedIndex < 0) key.SelectedIndex = 0;
        ctrl.Checked = (settings.Modifiers & 2) != 0; alt.Checked = (settings.Modifiers & 1) != 0; shift.Checked = (settings.Modifiers & 4) != 0;
        using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"); startup.Checked = run?.GetValue("VSwitcher") != null;
        panel.Controls.Add(startup);
        panel.Controls.Add(new Label { Text = "Обробка лише за командою. Мережа не використовується.", AutoSize = true, Margin = new Padding(0, 14, 0, 14) });
        var save = new Button { Text = "Зберегти", AutoSize = true };
        save.Click += (_, _) => { if (Value.Modifiers == 0) { MessageBox.Show("Оберіть Ctrl, Alt або Shift."); return; } DialogResult = DialogResult.OK; Close(); };
        panel.Controls.Add(save); Controls.Add(panel); AcceptButton = save;
    }
}
