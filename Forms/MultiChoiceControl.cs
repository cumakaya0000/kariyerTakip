namespace KariyerTakip.Forms;

// A dropdown adds individual values while the list keeps multiple selections visible.
internal sealed class MultiChoiceControl : UserControl
{
    private readonly ComboBox _choices = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Sorted = true };
    private readonly ListBox _selected = new() { Dock = DockStyle.Fill };

    public MultiChoiceControl(IEnumerable<string> choices, string emptyHint)
    {
        Height = 150;
        _choices.Items.AddRange(choices.Cast<object>().ToArray());
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var add = new Button { Text = "Ekle", AutoSize = true };
        var remove = new Button { Text = "Seçileni kaldır", AutoSize = true };
        add.Click += (_, _) =>
        {
            if (_choices.SelectedItem is string value && !_selected.Items.Contains(value)) _selected.Items.Add(value);
        };
        remove.Click += (_, _) =>
        {
            if (_selected.SelectedIndex >= 0) _selected.Items.RemoveAt(_selected.SelectedIndex);
        };
        layout.Controls.Add(_choices, 0, 0);
        layout.Controls.Add(add, 1, 0);
        layout.Controls.Add(remove, 2, 0);
        layout.Controls.Add(_selected, 0, 1);
        layout.SetColumnSpan(_selected, 3);
        var hint = new Label { Text = emptyHint, AutoSize = true, ForeColor = System.Drawing.SystemColors.GrayText };
        layout.Controls.Add(hint, 0, 2);
        layout.SetColumnSpan(hint, 3);
        Controls.Add(layout);
    }

    public List<string> ReadValues() => _selected.Items.Cast<string>().ToList();

    public void SetValues(IEnumerable<string> values)
    {
        _selected.Items.Clear();
        foreach (var value in values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            // Preserve existing profiles, including values outside the supplied catalog.
            if (!_choices.Items.Contains(value)) _choices.Items.Add(value);
            _selected.Items.Add(value);
        }
        _choices.SelectedIndex = -1;
    }
}
