using KariyerTakip.Models;
using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public sealed class ProfileEditorControl : UserControl
{
    private readonly ProfileCatalog _catalog;
    private readonly ProfileStore _store;
    private NamedProfile _active;
    private readonly ComboBox _profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240 };
    private readonly TextBox _newName = new() { PlaceholderText = "Yeni profil adı", Width = 190 };
    private readonly ComboBox _department = Choices(ProfileChoices.Departments.Cast<object>().ToArray());
    private readonly ComboBox _level = Choices("Ön Lisans", "Lisans", "Ortaöğretim / Lise", "Yüksek Lisans");
    private readonly ComboBox _graduation = Choices("Mezun", "Öğrenci", "Bilinmiyor");
    private readonly ComboBox _kpssStatus = Choices("Var", "Yok", "Bilinmiyor");
    private readonly DataGridView _scores = new() { Height = 110, BackgroundColor = System.Drawing.Color.White, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = true };
    private readonly ThemeDateTimePicker _birth = new() { Format = DateTimePickerFormat.Short, ShowCheckBox = true };
    private readonly ComboBox _military = Choices("Muaf / Yapıldı", "Tecilli", "Yapılmadı", "Bilinmiyor");
    private readonly NumericUpDown _months = new() { Minimum = 0, Maximum = 1200 };
    private readonly TextBox _field = new();
    private readonly CheckBox _known = new() { Text = "Tecrübe bilgim biliniyor", AutoSize = true };
    private readonly CheckBox _documented = new() { Text = "Tecrübem belgelenebilir", AutoSize = true };
    private readonly MultiChoiceControl _cities = new(ProfileChoices.Cities, "Seçim yoksa tüm Türkiye'deki ilanlar değerlendirilir.");
    private readonly MultiChoiceControl _licenses = new(ProfileChoices.Licenses, "Ehliyetiniz yoksa listeyi boş bırakın.");
    private readonly TextBox _certificates = new();
    private readonly TextBox _aliases = new() { Multiline = true, Height = 80, ScrollBars = ScrollBars.Vertical,
        PlaceholderText = "Her satır: Bölüm adı = eş ad 1; eş ad 2" };
    private readonly CheckedListBox _work = new() { Height = 80, CheckOnClick = true };
    private readonly Button _save = new() { Text = "Profili kaydet ve ilanları yeniden değerlendir", AutoSize = true, Height = 34 };
    private bool _loading;
    public event Action<ProfileOptions>? ActiveProfileChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<ProfileOptions, Task>? ProfileSaved { get; set; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ProfileOptions CurrentProfile => _active.Profile;

    public ProfileEditorControl(ProfileOptions profile, ProfileStore store)
    {
        _store = store;
        _catalog = store.Load(profile);
        _active = _catalog.Profiles.First(p => p.Name == _catalog.ActiveName);
        Dock = DockStyle.Fill;
        AutoScroll = true;
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(14) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var profileActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        var add = new Button { Text = "Profil ekle", AutoSize = true };
        profileActions.Controls.AddRange(new Control[] { _profiles, _newName, add });
        AddRow(layout, "Aktif profil", profileActions);
        AddRow(layout, "Bölüm", _department);
        _department.DropDownStyle = ComboBoxStyle.DropDown;
        AddRow(layout, "Bölüm eş adları", _aliases);
        AddRow(layout, "Öğrenim düzeyi", _level);
        AddRow(layout, "Mezuniyet", _graduation);
        AddRow(layout, "KPSS durumu", _kpssStatus);
        var scoreTypes = new DataGridViewComboBoxColumn { Name = "Type", HeaderText = "Puan türü", DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
        scoreTypes.Items.AddRange(Enumerable.Range(1, 121).Select(n => (object)$"P{n}").ToArray());
        _scores.Columns.Add(scoreTypes);
        var years = new DataGridViewComboBoxColumn { Name = "Year", HeaderText = "Sınav yılı", DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton };
        years.Items.AddRange(Enumerable.Range(2000, DateTime.Today.Year - 1999).Reverse().Cast<object>().ToArray());
        _scores.Columns.Add(years);
        _scores.Columns.Add("Score", "Puan (0–100)");
        AddRow(layout, "KPSS puanları", _scores);
        AddRow(layout, "Doğum tarihi (isteğe bağlı)", _birth);
        AddRow(layout, "Askerlik", _military);
        AddRow(layout, "Tecrübe (ay)", _months);
        AddRow(layout, "Tecrübe alanı", _field);
        AddRow(layout, "", _known);
        AddRow(layout, "", _documented);
        AddRow(layout, "Tercih edilen şehirler", _cities);
        AddRow(layout, "Ehliyet sınıfları", _licenses);
        AddRow(layout, "Sertifikalar (virgülle)", _certificates);
        _work.Items.AddRange(new object[] { "Sözleşmeli", "Kadrolu", "İşçi", "Geçici" });
        AddRow(layout, "Çalışma türleri (boş: tümü)", _work);
        AddRow(layout, "", _save);
        Controls.Add(layout);
        PopulateProfiles();
        PopulateFields();
        _profiles.SelectedIndexChanged += (s, e) =>
        {
            if (_loading || _profiles.SelectedItem is not string name || name == _active.Name) return;
            try
            {
                _active.Profile = ReadProfile();
                _active = _catalog.Profiles.First(p => p.Name == name);
                _catalog.ActiveName = name;
                PopulateFields();
                ActiveProfileChanged?.Invoke(_active.Profile);
            }
            catch (Exception ex) { ShowError(ex); PopulateProfiles(); }
        };
        add.Click += (s, e) =>
        {
            var name = _newName.Text.Trim();
            if (name.Length == 0 || _catalog.Profiles.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            { MessageBox.Show("Benzersiz bir profil adı girin."); return; }
            try
            {
                _active.Profile = ReadProfile();
                _active = new NamedProfile { Name = name, Profile = new ProfileOptions { BirthDate = null, KpssScores = new(), KpssStatus = "Bilinmiyor" } };
                _catalog.Profiles.Add(_active);
                _catalog.ActiveName = name;
                _newName.Clear();
                PopulateProfiles(); PopulateFields(); ActiveProfileChanged?.Invoke(_active.Profile);
            }
            catch (Exception ex) { ShowError(ex); }
        };
        _save.Click += async (s, e) =>
        {
            _save.Enabled = false;
            try
            {
                _active.Profile = ReadProfile();
                await _store.SaveAsync(_catalog);
                ActiveProfileChanged?.Invoke(_active.Profile);
                if (ProfileSaved != null) await ProfileSaved(_active.Profile);
                MessageBox.Show("Profil kaydedildi ve ilanlar yeniden değerlendirildi.", "Tamamlandı");
            }
            catch (Exception ex) { ShowError(ex); }
            finally { _save.Enabled = true; }
        };
    }
    public void SetBusy(bool busy) { Enabled = !busy; }
    public async Task SavePositionRulesAsync(string key, PositionRuleOverrides? rules)
    {
        _active.Profile = ReadProfile();
        if (rules == null) _active.Profile.PositionRules.Remove(key);
        else _active.Profile.PositionRules[key] = rules;
        await _store.SaveAsync(_catalog);
        ActiveProfileChanged?.Invoke(_active.Profile);
        if (ProfileSaved != null) await ProfileSaved(_active.Profile);
    }
    private void PopulateProfiles()
    {
        _loading = true;
        _profiles.Items.Clear();
        _profiles.Items.AddRange(_catalog.Profiles.Select(p => (object)p.Name).ToArray());
        _profiles.SelectedItem = _active.Name;
        _loading = false;
    }
    private void PopulateFields()
    {
        var p = _active.Profile;
        if (!string.IsNullOrWhiteSpace(p.Department) && !_department.Items.Contains(p.Department)) _department.Items.Add(p.Department);
        _department.Text = p.Department; _level.SelectedItem = p.EducationLevel; _graduation.SelectedItem = p.GraduationStatus;
        _aliases.Text = string.Join(Environment.NewLine, p.DepartmentAliases.Select(g => g.Key + " = " + string.Join("; ", g.Value)));
        _kpssStatus.SelectedItem = p.KpssStatus; _military.SelectedItem = p.MilitaryStatus;
        _scores.Rows.Clear();
        foreach (var score in p.KpssScores)
        {
            var types = (DataGridViewComboBoxColumn)_scores.Columns[0];
            var years = (DataGridViewComboBoxColumn)_scores.Columns[1];
            if (!types.Items.Contains(score.ScoreType)) types.Items.Add(score.ScoreType);
            if (!years.Items.Contains(score.ExamYear)) years.Items.Add(score.ExamYear);
            _scores.Rows.Add(score.ScoreType, score.ExamYear, score.Score);
        }
        _birth.Value = p.BirthDate ?? DateTime.Today; _birth.Checked = p.BirthDate.HasValue;
        _months.Value = Math.Clamp(p.Experience.TotalMonths, 0, 1200); _field.Text = p.Experience.Field;
        _known.Checked = p.Experience.IsKnown; _documented.Checked = p.Experience.IsDocumented;
        _cities.SetValues(p.CityPreferences); _licenses.SetValues(p.DrivingLicenses);
        _certificates.Text = string.Join(", ", p.Certificates);
        for (int i = 0; i < _work.Items.Count; i++) _work.SetItemChecked(i, p.WorkPreferences.Contains(_work.Items[i]!.ToString()!));
    }
    private ProfileOptions ReadProfile()
    {
        _scores.EndEdit();
        var scores = new List<KpssScoreEntry>();
        foreach (DataGridViewRow row in _scores.Rows)
        {
            if (row.IsNewRow) continue;
            var type = row.Cells[0].Value?.ToString()?.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(type) || !int.TryParse(row.Cells[1].Value?.ToString(), out var year) || year < 2000 || year > DateTime.Today.Year + 1 ||
                !double.TryParse(row.Cells[2].Value?.ToString(), out var score) || !double.IsFinite(score) || score < 0 || score > 100)
                throw new InvalidOperationException("KPSS satırlarında puan türü, geçerli yıl ve 0–100 puan girin.");
            if (scores.Any(s => s.ScoreType == type && s.ExamYear == year)) throw new InvalidOperationException("Aynı puan türü ve yılı yalnızca bir kez girin.");
            scores.Add(new KpssScoreEntry { ScoreType = type, ExamYear = year, Score = score });
        }
        if (_birth.Checked && _birth.Value.Date > DateTime.Today) throw new InvalidOperationException("Doğum tarihi gelecekte olamaz.");
        var department = _department.Text.Trim();
        if (string.IsNullOrWhiteSpace(department)) throw new InvalidOperationException("Bölümünüzü yazın veya seçin.");
        var aliases = new Dictionary<string, List<string>>();
        foreach (var line in _aliases.Lines.Where(l => !string.IsNullOrWhiteSpace(l)))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length < 3 || !aliases.TryAdd(parts[0], parts[1].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList()))
                throw new InvalidOperationException("Bölüm eş adlarını benzersiz 'Bölüm = eş ad; eş ad' satırları olarak yazın.");
        }
        return new ProfileOptions
        {
            Department = department, EducationLevel = _level.SelectedItem?.ToString() ?? "Ön Lisans",
            DepartmentAliases = aliases,
            PositionRules = _active.Profile.PositionRules,
            GraduationStatus = _graduation.SelectedItem?.ToString() ?? "Bilinmiyor", KpssStatus = _kpssStatus.SelectedItem?.ToString() ?? "Bilinmiyor",
            KpssScores = scores, BirthDate = _birth.Checked ? _birth.Value.Date : null, MilitaryStatus = _military.SelectedItem?.ToString() ?? "Bilinmiyor",
            Experience = new ExperienceEntry { TotalMonths = (int)_months.Value, Field = _field.Text.Trim(), IsKnown = _known.Checked, IsDocumented = _documented.Checked },
            CityPreferences = _cities.ReadValues(), DrivingLicenses = _licenses.ReadValues(), Certificates = Split(_certificates.Text),
            WorkPreferences = _work.CheckedItems.Cast<string>().ToList()
        };
    }
    private static List<string> Split(string text) => text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    private static ComboBox Choices(params object[] values) { var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }; combo.Items.AddRange(values); return combo; }
    internal static void AddRow(TableLayoutPanel layout, string label, Control control)
    {
        var row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 6, 10, 6) }, 0, row);
        control.Dock = control is Button ? DockStyle.None : DockStyle.Top;
        control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        control.Margin = new Padding(0, 3, 0, 6);
        layout.Controls.Add(control, 1, row);
    }
    private static void ShowError(Exception ex) => MessageBox.Show("Profil kaydedilemedi: " + ex.Message, "Profil hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
