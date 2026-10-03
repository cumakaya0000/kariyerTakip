using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KariyerTakip.Common;
using KariyerTakip.Models;
using KariyerTakip.Services;
using KariyerTakip.Storage;

namespace KariyerTakip.Forms;

public partial class MainForm : Form
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ScanCoordinator _scanCoordinator;
    private readonly IAnnouncementRepository _repository;
    private readonly TelegramNotifier _telegramNotifier;
    private readonly ChangeDetector _changeDetector;
    private readonly ILogger<MainForm> _logger;
    private ProfileOptions _profile;
    private AppConfig _config;

    // Concurrency / Cancellation
    private readonly ScanPresenter _scanPresenter;

    // UI Controls
    private TabControl _tabControl = null!;
    private DataGridView _gridAnnouncements = null!;
    private ComboBox _cmbFilter = null!;
    private TextBox _txtSearch = null!;
    private Button _btnScanNow = null!;
    private Button _btnCancelScan = null!;
    private Label _lblStatus = null!;
    private Label _lblLastScanTime = null!;
    private RichTextBox _rtbLog = null!;

    // Detail Panel Controls
    private Label _lblDetailTitle = null!;
    private Label _lblDetailInstitution = null!;
    private Label _lblDetailDates = null!;
    private Label _lblDetailStatusBadge = null!;
    private RichTextBox _rtbDetailContent = null!;
    private Button _btnOpenKariyerKapisi = null!;
    private Button _btnOpenEDevlet = null!;
    private Button _btnSendTelegramNow = null!;

    private ProfileEditorControl _profileEditor = null!;
    private ApplicationTrackingControl _applicationTracking = null!;
    private DesktopLifetime _desktop = null!;

    // Theme
    private Button _btnThemeToggle = null!;
    private bool _isDarkMode = false;
    // Tooltip for grid rows
    private ToolTip _gridTooltip = null!;

    // Data Cache
    private List<AnnouncementDisplayItem> _cachedItems = new();
    private AnnouncementDisplayItem? _selectedItem = null;
    private readonly HashSet<string> _checkedAnnouncementGuids = new();
    private Button _btnOpenSelectedAnnouncements = null!;
    private Label _lblCheckedAnnouncements = null!;
    private bool _isUpdatingAnnouncementRows;

    public MainForm(
        IServiceProvider serviceProvider,
        ScanCoordinator scanCoordinator,
        IAnnouncementRepository repository,
        TelegramNotifier telegramNotifier,
        ChangeDetector changeDetector,
        IOptions<ProfileOptions> profileOptions,
        IOptions<AppConfig> appConfig,
        ILogger<MainForm> logger)
    {
        _serviceProvider = serviceProvider;
        _scanCoordinator = scanCoordinator;
        _repository = repository;
        _telegramNotifier = telegramNotifier;
        _changeDetector = changeDetector;
        _logger = logger;
        _profile = profileOptions.Value;
        _config = appConfig.Value;
        _scanPresenter = new ScanPresenter(scanCoordinator);

        InitializeComponentsCustom();
        GuiLoggerProvider.OnLogReceived += HandleLogReceived;
        _desktop = new DesktopLifetime(this, () => _config.Desktop.MinimizeToTray);
    }

    private void InitializeComponentsCustom()
    {
        Text = "KariyerTakip — Kamu İlan Asistanı";
        Icon = AppBrand.CreateIcon();
        Size = new Size(1220, 800);
        MinimumSize = new Size(1020, 680);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(244, 246, 249);

        // 1. Top Header Bar
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = Color.FromArgb(15, 37, 65),
            Padding = new Padding(15, 10, 15, 10)
        };

        var lblAppTitle = new Label
        {
            Text = "💼 KariyerTakip",
            Font = new Font("Segoe UI", 15, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize = true,
            Location = new Point(15, 12)
        };

        _lblLastScanTime = new Label
        {
            Text = "Son Başarılı Tarama: Henüz yapılmadı",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(180, 200, 220),
            AutoSize = true,
            Location = new Point(17, 42)
        };

        _btnScanNow = new Button
        {
            Text = "⚡ Şimdi Tara",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0, 122, 255),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(130, 42),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 145, 15)
        };
        _btnScanNow.FlatAppearance.BorderSize = 0;
        _btnScanNow.Click += async (s, e) => await StartScanAsync();

        _btnCancelScan = new Button
        {
            Text = "⛔ İptal",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(220, 53, 69),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(80, 42),
            Cursor = Cursors.Hand,
            Enabled = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 235, 15)
        };
        _btnCancelScan.FlatAppearance.BorderSize = 0;
        _btnCancelScan.Click += (s, e) => CancelScan();

        _lblStatus = new Label
        {
            Text = "Hazır",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(150, 220, 150),
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _btnThemeToggle = new Button
        {
            Text = "Koyu",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = Color.Gold,
            BackColor = Color.FromArgb(35, 60, 90),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(65, 42),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 285, 15)
        };
        _btnThemeToggle.FlatAppearance.BorderSize = 0;
        _btnThemeToggle.Click += (s, e) => ToggleTheme();

        var headerLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var titleLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        lblAppTitle.Margin = new Padding(0);
        _lblLastScanTime.AutoSize = false;
        _lblLastScanTime.AutoEllipsis = true;
        _lblLastScanTime.Dock = DockStyle.Fill;
        _lblLastScanTime.Height = 22;
        titleLayout.Controls.Add(lblAppTitle, 0, 0);
        titleLayout.Controls.Add(_lblLastScanTime, 0, 1);
        var headerActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        foreach (var button in new[] { _btnThemeToggle, _btnCancelScan, _btnScanNow })
        {
            button.Anchor = AnchorStyles.None;
            button.AutoSize = true;
            button.MinimumSize = button.Size;
            button.Margin = new Padding(6, 3, 0, 3);
            headerActions.Controls.Add(button);
        }
        headerLayout.Controls.Add(titleLayout, 0, 0);
        headerLayout.Controls.Add(headerActions, 1, 0);
        headerLayout.Controls.Add(_lblStatus, 0, 1);
        headerLayout.SetColumnSpan(_lblStatus, 2);
        topPanel.Controls.Add(headerLayout);
        Controls.Add(topPanel);

        // 2. Main TabControl
        _tabControl = new ThemeTabControl
        {
            Dock = DockStyle.Fill,
            Padding = new Point(15, 8),
            Font = new Font("Segoe UI", 10, FontStyle.Regular)
        };

        // Tab 1: İlanlar
        var tabAnnouncements = new TabPage("📋 İlanlar & Pozisyonlar") { BackColor = Color.White };
        SetupAnnouncementsTab(tabAnnouncements);
        _tabControl.TabPages.Add(tabAnnouncements);

        // Tab 2: Profilim
        var tabProfile = new TabPage("👤 Profilim") { BackColor = Color.White };
        SetupProfileTab(tabProfile);
        _tabControl.TabPages.Add(tabProfile);

        // Tab 3: Telegram & Ayarlar
        var tabSettings = new TabPage("⚙️ Telegram & Sistem") { BackColor = Color.White };
        SetupSettingsTab(tabSettings);
        _tabControl.TabPages.Add(tabSettings);

        // Tab 4: Canlı Loglar
        var tabLogs = new TabPage("📜 Canlı İşlem Günlüğü") { BackColor = Color.White };
        SetupLogsTab(tabLogs);
        _tabControl.TabPages.Add(tabLogs);

        Controls.Add(_tabControl);
        _tabControl.BringToFront();
        ApplyTheme();

        Shown += async (s, e) => await LoadAnnouncementsFromDbAsync();
    }

    private void SetupAnnouncementsTab(TabPage tab)
    {
        tab.Controls.Add(new AnnouncementsControl(this));
    }

    private void SetupProfileTab(TabPage tab)
    {
        var store = _serviceProvider.GetService<ProfileStore>() ?? new ProfileStore();
        _profileEditor = new ProfileEditorControl(_profile, store);
        _profile = _profileEditor.CurrentProfile;
        _profileEditor.ActiveProfileChanged += async profile =>
        {
            _profile = profile;
            await LoadAnnouncementsFromDbAsync();
        };
        _profileEditor.ProfileSaved = async profile =>
        {
            _lblStatus.Text = "İlanlar yeni profile göre değerlendiriliyor...";
            await Task.Run(() => _scanCoordinator.ReevaluateCachedAnnouncementsAsync(profile));
            await LoadAnnouncementsFromDbAsync();
            _lblStatus.Text = "Profil kaydedildi ve ilanlar yeniden değerlendirildi";
        };
        tab.Controls.Add(_profileEditor);
    }

    private void SetupSettingsTab(TabPage tab)
    {
        var store = _serviceProvider.GetService<ConfigurationStore>() ?? new ConfigurationStore();
        tab.Controls.Add(new SettingsEditorControl(_config, store, _telegramNotifier));
    }
    private void SetupLogsTab(TabPage tab)
    {
        tab.Controls.Add(new LogsControl(this));
    }

    private async Task StartScanAsync()
    {
        if (_scanPresenter.IsRunning) return;
        _btnScanNow.Enabled = false;
        _btnCancelScan.Enabled = true;
        _profileEditor.SetBusy(true);
        _lblStatus.Text = "⏳ İlanlar taranıyor...";
        _lblStatus.ForeColor = Color.Gold;

        try
        {
            var result = await _scanPresenter.RunAsync(_profile);

            if (result.Status == ScanStatus.Success)
            {
                _lblStatus.Text = $"✅ Tarama başarılı ({result.TotalFound} ilan, {result.EligibleCount} uygun)";
                _lblStatus.ForeColor = Color.FromArgb(150, 255, 150);
            }
            else if (result.Status == ScanStatus.Partial)
            {
                _lblStatus.Text = $"⚠️ Kısmi tarama ({result.FailedCount} ilan okunamadı)";
                _lblStatus.ForeColor = Color.Gold;
            }
            else if (result.Status == ScanStatus.Cancelled)
            {
                _lblStatus.Text = "⛔ Tarama iptal edildi";
                _lblStatus.ForeColor = Color.FromArgb(255, 150, 150);
            }
            else
            {
                _lblStatus.Text = $"❌ Tarama başarısız ({result.ErrorMessage})";
                _lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
            }

            await LoadAnnouncementsFromDbAsync();
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "❌ Hata oluştu";
            _lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
            MessageBox.Show($"Tarama sırasında hata oluştu:\n{ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnScanNow.Enabled = true;
            _btnCancelScan.Enabled = false;
            _profileEditor.SetBusy(false);
        }
    }

    private void CancelScan()
    {
        if (_scanPresenter.IsRunning)
        {
            _scanPresenter.Cancel();
            _lblStatus.Text = "⏳ İptal ediliyor...";
        }
    }

    private async Task LoadAnnouncementsFromDbAsync()
    {
        try
        {
            await _repository.InitializeDatabaseAsync();
            var announcements = await _repository.GetAllAnnouncementsAsync();
            var profileHash = _changeDetector.ComputeHash(JsonSerializer.Serialize(_profile));
            var evaluationsMap = await _repository.GetLatestEvaluationsMapAsync(profileHash);

            var lastScan = await _repository.GetLastSuccessfulScanAsync();
            if (lastScan != null)
            {
                _lblLastScanTime.Text = $"Son Başarılı Tarama: {AppTime.Format(lastScan.FinishedAt)}";
            }

            var loadedItems = new List<AnnouncementDisplayItem>();
            foreach (var ann in announcements)
            {
                evaluationsMap.TryGetValue(ann.Guid, out var evaluations);
                evaluations ??= new List<EvaluationRecord>();

                var positions = await _repository.GetPositionsByAnnouncementGuidAsync(ann.Guid);
                var positionKeys = positions.Select(p => p.PositionKey).ToHashSet();
                evaluations = evaluations.Where(e => positionKeys.Contains(e.PositionKey)).ToList();

                var isEligible = evaluations.Any(e => e.Status == EligibilityStatus.Eligible.ToString());
                var isNeedsReview = evaluations.Any(e => e.Status == EligibilityStatus.NeedsReview.ToString());
                var hasUnevaluatedPositions = positions.Count == 0 ||
                    positions.Any(p => !evaluations.Any(e => e.PositionKey == p.PositionKey));

                var overallStatus = isEligible ? EligibilityStatus.Eligible :
                                    (isNeedsReview || hasUnevaluatedPositions ? EligibilityStatus.NeedsReview : EligibilityStatus.Ineligible);

                loadedItems.Add(new AnnouncementDisplayItem
                {
                    Record = ann,
                    Evaluations = evaluations,
                    Positions = positions,
                    Status = overallStatus
                });
            }

            _cachedItems = loadedItems;
            _checkedAnnouncementGuids.IntersectWith(loadedItems.Select(item => item.Record.Guid));
            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Veritabanından ilanlar yüklenirken hata oluştu.");
            _lblStatus.Text = "❌ İlanlar yüklenemedi. Ayrıntılar için günlükleri kontrol edin.";
        }
    }

    private void ApplyFilter()
    {
        var filterIndex = _cmbFilter.SelectedIndex;
        var search = _txtSearch.Text.Trim().ToLowerInvariant();

        var filtered = _cachedItems.Where(item =>
        {
            if (filterIndex == 1 && item.Status != EligibilityStatus.Eligible) return false;
            if (filterIndex == 2 && item.Status != EligibilityStatus.NeedsReview) return false;
            if (filterIndex == 3 && item.Status != EligibilityStatus.Ineligible) return false;

            if (!string.IsNullOrWhiteSpace(search))
            {
                var match = item.Record.InstitutionName.ToLowerInvariant().Contains(search) ||
                            item.Record.Title.ToLowerInvariant().Contains(search);
                if (!match) return false;
            }

            return true;
        }).ToList();

        _isUpdatingAnnouncementRows = true;
        try
        {
            _gridAnnouncements.Rows.Clear();
            foreach (var item in filtered)
            {
                var statusText = item.Status switch
                {
                    EligibilityStatus.Eligible => "✅ UYGUN",
                    EligibilityStatus.NeedsReview => "⚠️ İNCELE",
                    _ => "❌ UYGUN DEĞİL"
                };

                var endStr = AppTime.Format(item.Record.EndDate);
                var rowIdx = _gridAnnouncements.Rows.Add(_checkedAnnouncementGuids.Contains(item.Record.Guid), statusText, item.Record.InstitutionName, item.Record.Title, endStr,
                    ApplicationTrackingControl.DisplayStatus(item.Record.ApplicationStatus));
                _gridAnnouncements.Rows[rowIdx].Tag = item;

                if (item.Status == EligibilityStatus.Eligible)
                {
                    _gridAnnouncements.Rows[rowIdx].DefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(30, 60, 30) : Color.FromArgb(235, 255, 235);
                }
                else if (item.Status == EligibilityStatus.NeedsReview)
                {
                    _gridAnnouncements.Rows[rowIdx].DefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(60, 55, 25) : Color.FromArgb(255, 250, 230);
                }
            }

            if (_gridAnnouncements.Rows.Count > 0)
            {
                // Force deselect first so SelectionChanged always fires even if row 0 was already selected
                _gridAnnouncements.ClearSelection();
                _gridAnnouncements.Rows[0].Selected = true;
                _gridAnnouncements.CurrentCell = _gridAnnouncements.Rows[0].Cells[0];

                // Also directly populate the detail panel in case SelectionChanged doesn't re-fire
                var firstItem = _gridAnnouncements.Rows[0].Tag as AnnouncementDisplayItem;
                if (firstItem != null)
                    PopulateDetailPanel(firstItem);
            }
            else
            {
                ClearDetailPanel();
            }
        }
        finally
        {
            _isUpdatingAnnouncementRows = false;
            UpdateCheckedAnnouncements();
        }
    }

    private void UpdateCheckedAnnouncements()
    {
        var count = _checkedAnnouncementGuids.Count;
        var visibleCount = _gridAnnouncements.Rows.Cast<DataGridViewRow>()
            .Count(row => row.Tag is AnnouncementDisplayItem item && _checkedAnnouncementGuids.Contains(item.Record.Guid));
        _lblCheckedAnnouncements.Text = count == 0 ? "İlanları kutucuklarla seçin" :
            $"{count} ilan seçili" + (count > visibleCount ? $" ({count - visibleCount} tanesi filtre dışında)" : "");
        _btnOpenSelectedAnnouncements.Text = $"Seçili {count} ilana git";
        _btnOpenSelectedAnnouncements.Visible = count > 1;
    }

    private static string GetAnnouncementDetailUrl(AnnouncementRecord record) =>
        !string.IsNullOrWhiteSpace(record.DetailUrl) ? record.DetailUrl :
            $"https://kariyerkapisi.gov.tr/IlanDetay?i={Uri.EscapeDataString(record.Guid)}";

    private List<string> GetCheckedAnnouncementUrls() => _cachedItems
        .Where(item => _checkedAnnouncementGuids.Contains(item.Record.Guid))
        .Select(item => GetAnnouncementDetailUrl(item.Record)).Distinct().ToList();

    private void ClearDetailPanel()
    {
        _selectedItem = null;
        _lblDetailInstitution.Text = "Seçili ilan yok";
        _lblDetailTitle.Text = "";
        _lblDetailDates.Text = "";
        _lblDetailStatusBadge.Text = "BİLGİ";
        _lblDetailStatusBadge.BackColor = Color.FromArgb(120, 130, 140);
        _rtbDetailContent.Clear();
        _applicationTracking.Bind(null);
    }

    private void GridAnnouncements_SelectionChanged(object? sender, EventArgs e)
    {
        if (_gridAnnouncements.SelectedRows.Count == 0)
        {
            ClearDetailPanel();
            return;
        }

        var item = _gridAnnouncements.SelectedRows[0].Tag as AnnouncementDisplayItem;
        if (item == null)
        {
            ClearDetailPanel();
            return;
        }

        PopulateDetailPanel(item);
    }

    private void PopulateDetailPanel(AnnouncementDisplayItem item)
    {
        if (_selectedItem != item) _applicationTracking.Bind(item.Record);
        _selectedItem = item;

        _lblDetailInstitution.Text = item.Record.InstitutionName;
        _lblDetailTitle.Text = item.Record.Title;

        var startStr = AppTime.Format(item.Record.StartDate, "-");
        var endStr = AppTime.Format(item.Record.EndDate, "-");
        _lblDetailDates.Text = $"📅 Başlangıç: {startStr}  |  Bitiş: {endStr}";

        if (item.Status == EligibilityStatus.Eligible)
        {
            _lblDetailStatusBadge.Text = "✅ ŞARTLARA UYGUN KADRO VAR";
            _lblDetailStatusBadge.BackColor = Color.FromArgb(40, 167, 69);
        }
        else if (item.Status == EligibilityStatus.NeedsReview)
        {
            _lblDetailStatusBadge.Text = "⚠️ KONTROL GEREKLİ KADRO VAR";
            _lblDetailStatusBadge.BackColor = Color.FromArgb(255, 165, 0);
        }
        else
        {
            _lblDetailStatusBadge.Text = "❌ UYGUN KADRO BULUNAMADI";
            _lblDetailStatusBadge.BackColor = Color.FromArgb(220, 53, 69);
        }

        _rtbDetailContent.Clear();
        var textColor = _isDarkMode ? Color.FromArgb(230, 230, 230) : Color.FromArgb(35, 45, 55);
        var headingColor = _isDarkMode ? Color.FromArgb(135, 190, 255) : Color.FromArgb(15, 65, 115);
        if (!string.IsNullOrWhiteSpace(item.Record.UnitName))
            AppendDetailText($"Birim: {item.Record.UnitName}\n\n", textColor);
        AppendDetailText($"KADROLAR ({item.Positions.Count})\n\n", headingColor, bold: true);

        foreach (var pos in item.Positions)
        {
            var eval = item.Evaluations.FirstOrDefault(e => e.PositionKey == pos.PositionKey);
            var statusText = eval?.Status switch
            {
                "Eligible" => "✅ Uygun",
                "Ineligible" => "❌ Uygun değil",
                "NeedsReview" => "⚠️ Kontrol gerekli",
                _ => "⚠️ Henüz değerlendirilmedi"
            };
            var statusColor = eval?.Status switch
            {
                "Eligible" => _isDarkMode ? Color.LightGreen : Color.FromArgb(30, 125, 60),
                "Ineligible" => _isDarkMode ? Color.Salmon : Color.FromArgb(180, 45, 45),
                _ => _isDarkMode ? Color.Gold : Color.FromArgb(145, 95, 0)
            };
            AppendDetailText(pos.Title + "\n\n", headingColor, bold: true, size: 12);
            AppendDetailText(statusText + "\n\n", statusColor, bold: true);
            if (!string.IsNullOrWhiteSpace(pos.Unvan) && pos.Unvan != pos.Title)
                AppendDetailText($"Unvan: {pos.Unvan}\n", textColor);
            AppendDetailText($"Şehir: {(string.IsNullOrWhiteSpace(pos.Cities) ? "Belirtilmemiş" : pos.Cities)}\n", textColor);
            AppendDetailText($"Toplam kontenjan: {pos.Quota}\n\n", textColor);

            if (eval != null)
            {
                AppendDetailText("Değerlendirme gerekçeleri\n\n", textColor, bold: true);
                PositionEvaluation? details = null;
                if (!string.IsNullOrWhiteSpace(eval.DetailsJson))
                {
                    try { details = JsonSerializer.Deserialize<PositionEvaluation>(eval.DetailsJson); }
                    catch (JsonException ex) { _logger.LogWarning(ex, "Kadro değerlendirme ayrıntısı okunamadı: {Key}", pos.PositionKey); }
                }
                if (details?.Conditions is { Count: > 0 })
                {
                    foreach (var condition in details.Conditions)
                    {
                        var conditionStatus = condition.Status switch
                        {
                            ConditionStatus.Satisfied => "Karşılanıyor",
                            ConditionStatus.Unsatisfied => "Karşılanmıyor",
                            _ => "Kontrol gerekli"
                        };
                        AppendDetailText($"• {condition.CriterionName} — {conditionStatus}\n", textColor, bold: true);
                        AppendDetailText(condition.Explanation + "\n\n", textColor, indent: 18);
                    }
                }
                else
                {
                    foreach (var reason in eval.SummaryReason.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        AppendDetailText($"• {reason}\n\n", textColor, indent: 12);
                }
            }
            AppendDetailText("────────────────────────\n\n", headingColor);
        }

        if (item.Positions.Count == 0)
            AppendDetailText("Kadro bilgisi bulunamadı. Ayrıntılar için ilan metnini kontrol edin.\n\n", textColor);

        if (!string.IsNullOrWhiteSpace(item.Record.RawGeneralText))
        {
            AppendDetailText("İLAN METNİ\n\n", headingColor, bold: true, size: 12);
            AppendDetailText(new DocumentReader().CleanAndNormalizeText(item.Record.RawGeneralText), textColor);
        }
        _rtbDetailContent.Select(0, 0);
        _rtbDetailContent.ScrollToCaret();
    }

    private void AppendDetailText(string text, Color color, bool bold = false, float size = 10.5f, int indent = 0)
    {
        _rtbDetailContent.Select(_rtbDetailContent.TextLength, 0);
        using var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        _rtbDetailContent.SelectionFont = font;
        _rtbDetailContent.SelectionColor = color;
        _rtbDetailContent.SelectionIndent = 12 + indent;
        _rtbDetailContent.SelectionRightIndent = 12;
        _rtbDetailContent.SelectionHangingIndent = 0;
        _rtbDetailContent.AppendText(text);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GuiLoggerProvider.OnLogReceived -= HandleLogReceived;
            _scanPresenter.Dispose();
            _desktop?.Dispose();
            _gridTooltip?.Dispose();
        }
        base.Dispose(disposing);
    }

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Bağlantı adresi bulunamadı.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            MessageBox.Show("Geçersiz veya güvensiz bağlantı adresi.", "Güvenlik Uyarısı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Bağlantı açılamadı: {ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void GridAnnouncements_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex == _gridAnnouncements.Columns["Checked"]!.Index) return;

        var item = _gridAnnouncements.Rows[e.RowIndex].Tag as AnnouncementDisplayItem;
        if (item == null) return;

        OpenUrl(GetAnnouncementDetailUrl(item.Record));
    }

    private async Task SendSelectedToTelegramAsync()
    {
        if (_selectedItem == null) return;

        var matchingPositions = new List<PositionEvaluation>();
        foreach (var pos in _selectedItem.Positions)
        {
            var evalRecord = _selectedItem.Evaluations.FirstOrDefault(e => e.PositionKey == pos.PositionKey);
            var status = evalRecord != null && Enum.TryParse<EligibilityStatus>(evalRecord.Status, out var st)
                ? st
                : EligibilityStatus.Ineligible;

            matchingPositions.Add(new PositionEvaluation
            {
                PositionKey = pos.PositionKey,
                PositionTitle = pos.Title,
                Unvan = pos.Unvan,
                Cities = pos.Cities,
                TotalQuota = pos.Quota,
                Status = status,
                SummaryReason = evalRecord?.SummaryReason ?? "Değerlendirme mevcut değil"
            });
        }

        var message = _telegramNotifier.FormatAnnouncementMessage(
            _selectedItem.Record,
            matchingPositions,
            "📢 <b>KULLANICI TARAFINDAN GÖNDERİLEN İLAN</b>");

        var result = await _telegramNotifier.SendMessageAsync(message);
        if (result.IsSuccess)
        {
            MessageBox.Show("İlan Telegram'a başarıyla gönderildi!", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (result.Status == TelegramSendStatus.Disabled)
        {
            MessageBox.Show("Telegram devre dışı veya yapılandırılmamış. Lütfen Ayarlar sekmesinden etkinleştiriniz.", "Telegram Kapalı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        else
        {
            MessageBox.Show($"Telegram gönderim hatası:\n{result.ErrorMessage}", "Gönderim Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

}
