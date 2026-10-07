using System.Globalization;
using KariyerTakip.Services;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private async Task EditPositionRulesAsync()
    {
        var item = _selectedItem;
        if (item == null || item.Positions.Count == 0) return;
        using var dialog = new Form { Text = "Kadro şartlarını doğrula", Width = 1050, Height = 540, StartPosition = FormStartPosition.CenterParent };
        var selector = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        selector.Items.AddRange(item.Positions.Select(p => (object)p.Title).ToArray());
        var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Apply", HeaderText = "Doğruladım", FillWeight = 35 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Rule", HeaderText = "Şart", ReadOnly = true });
        grid.Columns.Add("Value", "Değer (düzenlenebilir)");
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Kaynak cümle", ReadOnly = true, FillWeight = 200 });
        var save = new Button { Text = "Doğruladığım şartları kaydet ve yeniden değerlendir", Dock = DockStyle.Bottom, Height = 36 };
        var hint = new Label { Dock = DockStyle.Bottom, Height = 45, Text = "Yalnızca resmî ilandan doğruladığınız satırları işaretleyin. Yılları virgülle ayırın. KPSS muafiyeti için Evet/Hayır yazın. İşaret kaldırılması mevcut düzeltmeyi siler." };
        void Populate()
        {
            if (selector.SelectedIndex < 0) return;
            var pos = item.Positions[selector.SelectedIndex];
            var req = new RequirementExtractor(new DocumentReader()).Extract(pos.RawText);
            var custom = _profileEditor.CurrentProfile.PositionRules.GetValueOrDefault(pos.PositionKey);
            grid.Rows.Clear();
            void Row(string name, object? value, string? source, bool applied) => grid.Rows.Add(applied, name, value?.ToString() ?? "", source ?? "");
            Row("KPSS türü", custom?.KpssType ?? req.RequiredKpssType?.Value, req.RequiredKpssType?.SourceText, custom?.KpssType != null);
            Row("KPSS taban puanı", custom?.MinKpssScore ?? req.MinKpssScore?.Value, req.MinKpssScore?.SourceText, custom?.MinKpssScore != null);
            Row("KPSS yılları", string.Join(",", custom?.KpssYears ?? req.AllowedKpssYears.ToList()), req.RequiredKpssYear?.SourceText, custom?.KpssYears != null);
            Row("KPSS en erken yıl", custom?.MinKpssYear ?? req.MinKpssYear, "", custom?.MinKpssYear != null);
            Row("KPSS en geç yıl", custom?.MaxKpssYear ?? req.MaxKpssYear, "", custom?.MaxKpssYear != null);
            Row("KPSS muafiyeti", (custom?.NoKpss ?? req.ExplicitlyNoKpss) ? "Evet" : "Hayır", "", custom?.NoKpss != null);
            Row("En az tecrübe (ay)", custom?.ExperienceMonths ?? req.MinExperienceMonths?.Value, req.ExperienceSourceText, custom?.ExperienceMonths != null);
            Row("Yaş sınırı", custom?.AgeLimit ?? req.MaxAgeLimit?.Value, req.MaxAgeLimit?.SourceText, custom?.AgeLimit != null);
            Row("Ehliyet sınıfı", custom?.DrivingLicense ?? req.RequiredDrivingLicense?.Value, req.RequiredDrivingLicense?.SourceText, custom?.DrivingLicense != null);
            Row("En çok tecrübe (ay)", custom?.MaxExperienceMonths ?? req.MaxExperienceMonths?.Value, req.MaxExperienceMonths?.SourceText, custom?.MaxExperienceMonths != null);
            Row("Tüm kadro/genel kural çakışmalarını doğruladım", custom?.ResolveConflicts == true ? "Evet" : "Hayır", "Kadroya uygulanacak tüm çakışan şartları resmî ilandan doğruladığınızda işaretleyin.", custom?.ResolveConflicts == true);
        }
        selector.SelectedIndexChanged += (_, _) => Populate();
        selector.SelectedIndex = 0;
        save.Click += async (_, _) =>
        {
            try
            {
                grid.EndEdit();
                var rules = new PositionRuleOverrides();
                var position = item.Positions[selector.SelectedIndex];
                var general = item.Record.Source == KariyerTakip.Models.AnnouncementSource.KamuIlan ? item.Record.GeneralConditionsText : item.Record.RawGeneralText;
                rules.SourceHash = new ChangeDetector().ComputeHash(position.RawText + "\n" + general);
                string? Value(int row) => grid.Rows[row].Cells[0].Value is true ? grid.Rows[row].Cells[2].Value?.ToString()?.Trim() ?? "" : null;
                int? Number(int row, int min, int max)
                {
                    var text = Value(row); if (text == null) return null;
                    if (!int.TryParse(text, out var number) || number < min || number > max) throw new InvalidOperationException($"{grid.Rows[row].Cells[1].Value}: {min}–{max} arasında sayı girin.");
                    return number;
                }
                rules.KpssType = Value(0)?.ToUpperInvariant();
                if (rules.KpssType != null && !System.Text.RegularExpressions.Regex.IsMatch(rules.KpssType, @"^P[1-9]\d*$")) throw new InvalidOperationException("Geçerli KPSS türü girin (ör. P93).");
                if (Value(1) is string score)
                {
                    if (!double.TryParse(score.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || number < 0 || number > 100) throw new InvalidOperationException("Puan 0–100 arasında olmalıdır.");
                    rules.MinKpssScore = number;
                }
                if (Value(2) is string years) rules.KpssYears = years.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(y => int.TryParse(y, out var n) && n >= 2000 && n <= 2100 ? n : throw new InvalidOperationException("Geçerli yılları virgülle ayırın.")).ToList();
                rules.MinKpssYear = Number(3, 2000, 2100); rules.MaxKpssYear = Number(4, 2000, 2100);
                if (rules.MinKpssYear > rules.MaxKpssYear) throw new InvalidOperationException("Yıl aralığı ters olamaz.");
                if (Value(5) is string noKpss) rules.NoKpss = noKpss.Equals("Evet", StringComparison.OrdinalIgnoreCase) ? true : noKpss.Equals("Hayır", StringComparison.OrdinalIgnoreCase) ? false : throw new InvalidOperationException("Muafiyet için Evet veya Hayır yazın.");
                rules.ExperienceMonths = Number(6, 0, 1200); rules.AgeLimit = Number(7, 18, 100);
                rules.DrivingLicense = Value(8)?.ToUpperInvariant();
                rules.MaxExperienceMonths = Number(9, 0, 1200);
                if (rules.ExperienceMonths > rules.MaxExperienceMonths) throw new InvalidOperationException("Tecrübe aralığı ters olamaz.");
                if (Value(10) is string resolved) rules.ResolveConflicts = resolved.Equals("Evet", StringComparison.OrdinalIgnoreCase) ? true : resolved.Equals("Hayır", StringComparison.OrdinalIgnoreCase) ? false : throw new InvalidOperationException("Çakışma doğrulaması için Evet veya Hayır yazın.");
                if (rules.DrivingLicense != null && !ProfileChoices.Licenses.Contains(rules.DrivingLicense)) throw new InvalidOperationException("Geçerli ehliyet sınıfı girin.");
                save.Enabled = false;
                var hasCustom = grid.Rows.Cast<DataGridViewRow>().Any(r => r.Cells[0].Value is true);
                await _profileEditor.SavePositionRulesAsync(item.Positions[selector.SelectedIndex].PositionKey, hasCustom ? rules : null);
                dialog.Close();
            }
            catch (Exception ex) { MessageBox.Show(dialog, "Şartlar kaydedilemedi: " + ex.Message); save.Enabled = true; }
        };
        dialog.Controls.Add(grid); dialog.Controls.Add(selector); dialog.Controls.Add(hint); dialog.Controls.Add(save);
        dialog.ShowDialog(this);
    }
}
