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
    private CancellationTokenSource? _scanCts;
    private bool _isScanning = false;

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
    private NotifyIcon _trayIcon = null!;

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

        InitializeComponentsCustom();
        GuiLoggerProvider.OnLogReceived += HandleLogReceived;
        _trayIcon = new NotifyIcon { Icon = SystemIcons.Application, Text = "KariyerTakip" };
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Aç", null, (s, e) => RestoreFromTray());
        trayMenu.Items.Add("Çıkış", null, (s, e) => Close());
        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (s, e) => RestoreFromTray();
        Resize += (s, e) =>
        {
            if (_config.Desktop.MinimizeToTray && WindowState == FormWindowState.Minimized)
            { _trayIcon.Visible = true; Hide(); }
        };
    }

    private void InitializeComponentsCustom()
    {
        Text = "KariyerTakip — Kamu İlan Asistanı";
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

    private void HandleLogReceived(string logMessage, LogLevel level)
    {
        if (IsDisposed || !IsHandleCreated)
            return;

        BeginInvoke(new Action(() =>
        {
            Color color = level switch
            {
                LogLevel.Information => Color.FromArgb(200, 220, 240),
                LogLevel.Warning => Color.FromArgb(255, 200, 80),
                LogLevel.Error or LogLevel.Critical => Color.FromArgb(255, 100, 100),
                _ => Color.LightGray
            };

            _rtbLog.SelectionStart = _rtbLog.TextLength;
            _rtbLog.SelectionLength = 0;
            _rtbLog.SelectionColor = color;
            _rtbLog.AppendText(logMessage + Environment.NewLine);
            _rtbLog.ScrollToCaret();
        }));
    }

    private async Task StartScanAsync()
    {
        if (_isScanning) return;

        _isScanning = true;
        _scanCts = new CancellationTokenSource();
        _btnScanNow.Enabled = false;
        _btnCancelScan.Enabled = true;
        _profileEditor.SetBusy(true);
        _lblStatus.Text = "⏳ İlanlar taranıyor...";
        _lblStatus.ForeColor = Color.Gold;

        try
        {
            var result = await Task.Run(async () => await _scanCoordinator.RunScanAsync(_scanCts.Token, _profile));

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
            _isScanning = false;
            _btnScanNow.Enabled = true;
            _btnCancelScan.Enabled = false;
            _profileEditor.SetBusy(false);
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    private void CancelScan()
    {
        if (_scanCts != null && !_scanCts.IsCancellationRequested)
        {
            _scanCts.Cancel();
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

    private void RestoreFromTray()
    {
        Show(); WindowState = FormWindowState.Normal; Activate(); _trayIcon.Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            GuiLoggerProvider.OnLogReceived -= HandleLogReceived;
            _scanCts?.Cancel();
            _trayIcon?.Dispose();
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

    private void ToggleTheme()
    {
        _isDarkMode = !_isDarkMode;
        _btnThemeToggle.Text = _isDarkMode ? "Açık" : "Koyu";
        ApplyTheme();
    }

    private void ApplyTheme()
    {
        // ── Color Palettes ──
        Color formBg, panelBg, cardBg, textPrimary, textSecondary, gridBg, gridAltRow, gridHeaderBg, gridHeaderFg, gridLineBorder;
        Color headerBg, headerSubFg;
        Color detailBg;
        Color logBg, logFg;
        Color tabBg;

        if (_isDarkMode)
        {
            formBg         = Color.FromArgb(30, 30, 30);
            panelBg        = Color.FromArgb(40, 40, 40);
            cardBg         = Color.FromArgb(45, 45, 48);
            textPrimary    = Color.FromArgb(230, 230, 230);
            textSecondary  = Color.FromArgb(170, 170, 170);
            gridBg         = Color.FromArgb(38, 38, 42);
            gridAltRow     = Color.FromArgb(48, 48, 55);
            gridHeaderBg   = Color.FromArgb(55, 55, 60);
            gridHeaderFg   = Color.FromArgb(220, 220, 220);
            gridLineBorder = Color.FromArgb(65, 65, 70);
            headerBg       = Color.FromArgb(18, 18, 22);
            headerSubFg    = Color.FromArgb(140, 160, 180);
            detailBg       = Color.FromArgb(38, 40, 45);
            logBg          = Color.FromArgb(18, 20, 25);
            logFg          = Color.FromArgb(200, 210, 220);
            tabBg          = Color.FromArgb(40, 40, 40);
        }
        else
        {
            formBg         = Color.FromArgb(244, 246, 249);
            panelBg        = Color.White;
            cardBg         = Color.White;
            textPrimary    = Color.FromArgb(30, 30, 30);
            textSecondary  = Color.FromArgb(100, 110, 120);
            gridBg         = Color.White;
            gridAltRow     = Color.FromArgb(248, 250, 252);
            gridHeaderBg   = Color.FromArgb(240, 242, 245);
            gridHeaderFg   = Color.FromArgb(30, 30, 30);
            gridLineBorder = Color.FromArgb(220, 225, 230);
            headerBg       = Color.FromArgb(15, 37, 65);
            headerSubFg    = Color.FromArgb(180, 200, 220);
            detailBg       = Color.FromArgb(250, 252, 255);
            logBg          = Color.FromArgb(20, 24, 30);
            logFg          = Color.FromArgb(220, 230, 240);
            tabBg          = Color.White;
        }

        // ── Form ──
        BackColor = formBg;

        // ── Header ──
        var topPanel = Controls.OfType<Panel>().FirstOrDefault(p => p.Dock == DockStyle.Top);
        if (topPanel != null)
        {
            topPanel.BackColor = headerBg;
            void ThemeHeader(Control parent)
            {
                foreach (Control c in parent.Controls)
                {
                    if (c is Label lbl && lbl != _lblStatus)
                        lbl.ForeColor = lbl == _lblLastScanTime ? headerSubFg : Color.White;
                    else if (c is Panel)
                    {
                        c.BackColor = headerBg;
                        ThemeHeader(c);
                    }
                }
            }
            ThemeHeader(topPanel);
            _btnThemeToggle.BackColor = _isDarkMode ? Color.FromArgb(50, 55, 60) : Color.FromArgb(35, 60, 90);
        }

        // ── Grid ──
        _gridAnnouncements.BackgroundColor = gridBg;
        _gridAnnouncements.DefaultCellStyle.BackColor = gridBg;
        _gridAnnouncements.DefaultCellStyle.ForeColor = textPrimary;
        _gridAnnouncements.DefaultCellStyle.SelectionBackColor = _isDarkMode ? Color.FromArgb(60, 80, 120) : Color.FromArgb(200, 220, 250);
        _gridAnnouncements.DefaultCellStyle.SelectionForeColor = textPrimary;
        _gridAnnouncements.AlternatingRowsDefaultCellStyle.BackColor = gridAltRow;
        _gridAnnouncements.ColumnHeadersDefaultCellStyle.BackColor = gridHeaderBg;
        _gridAnnouncements.ColumnHeadersDefaultCellStyle.ForeColor = gridHeaderFg;
        _gridAnnouncements.GridColor = gridLineBorder;
        _gridAnnouncements.EnableHeadersVisualStyles = false;

        foreach (DataGridViewRow row in _gridAnnouncements.Rows)
        {
            var it = row.Tag as AnnouncementDisplayItem;
            if (it == null) continue;
            if (it.Status == EligibilityStatus.Eligible)
                row.DefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(30, 60, 30) : Color.FromArgb(235, 255, 235);
            else if (it.Status == EligibilityStatus.NeedsReview)
                row.DefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(60, 55, 25) : Color.FromArgb(255, 250, 230);
            else
                row.DefaultCellStyle.BackColor = gridBg;
        }

        // ── Detail Panel ──
        _lblDetailInstitution.ForeColor = textPrimary;
        _lblDetailTitle.ForeColor = textSecondary;
        _lblDetailDates.ForeColor = textSecondary;
        _rtbDetailContent.BackColor = cardBg;
        _rtbDetailContent.ForeColor = textPrimary;

        // ── Tabs & Tab Pages ──
        if (_tabControl is ThemeTabControl mainTabs) mainTabs.SetPalette(formBg, cardBg, textPrimary);
        foreach (TabPage tp in _tabControl.TabPages)
        {
            tp.BackColor = tabBg;
            ApplyThemeToChildren(tp, panelBg, cardBg, textPrimary, textSecondary);
        }

        // ── Log ──
        _rtbLog.BackColor = logBg;
        _rtbLog.ForeColor = logFg;

        // ── Detail area parent panels ──
        var detailParent = _rtbDetailContent.Parent;
        while (detailParent != null && detailParent != _tabControl)
        {
            if (detailParent is Panel dp)
                dp.BackColor = detailBg;
            detailParent = detailParent.Parent;
        }

        if (_selectedItem != null) PopulateDetailPanel(_selectedItem);
        Invalidate(true);
    }

    private void ApplyThemeToChildren(Control parent, Color panelBg, Color cardBg, Color textPrimary, Color textSecondary)
    {
        foreach (Control c in parent.Controls)
        {
            switch (c)
            {
                case TabPage page:
                    page.BackColor = panelBg;
                    ApplyThemeToChildren(page, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case ThemeTabControl tabs:
                    tabs.SetPalette(panelBg, cardBg, textPrimary);
                    ApplyThemeToChildren(tabs, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case DataGridView grid:
                    grid.BackgroundColor = cardBg;
                    grid.DefaultCellStyle.BackColor = cardBg;
                    grid.DefaultCellStyle.ForeColor = textPrimary;
                    grid.ColumnHeadersDefaultCellStyle.BackColor = panelBg;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = textPrimary;
                    grid.EnableHeadersVisualStyles = false;
                    grid.AlternatingRowsDefaultCellStyle.BackColor = _isDarkMode ? Color.FromArgb(48, 48, 55) : Color.FromArgb(248, 250, 252);
                    grid.DefaultCellStyle.SelectionBackColor = _isDarkMode ? Color.FromArgb(60, 80, 120) : Color.FromArgb(200, 220, 250);
                    grid.DefaultCellStyle.SelectionForeColor = textPrimary;
                    grid.RowHeadersDefaultCellStyle.BackColor = panelBg;
                    grid.RowHeadersDefaultCellStyle.ForeColor = textPrimary;
                    grid.RowHeadersDefaultCellStyle.SelectionBackColor = grid.DefaultCellStyle.SelectionBackColor;
                    grid.RowHeadersDefaultCellStyle.SelectionForeColor = textPrimary;
                    grid.GridColor = _isDarkMode ? Color.FromArgb(65, 65, 70) : Color.FromArgb(220, 225, 230);
                    break;
                case ListBox list:
                    list.BackColor = cardBg; list.ForeColor = textPrimary;
                    break;
                case Panel p:
                    p.BackColor = panelBg;
                    ApplyThemeToChildren(p, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case UserControl control:
                    control.BackColor = panelBg;
                    control.ForeColor = textPrimary;
                    ApplyThemeToChildren(control, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case SplitContainer sc:
                    sc.BackColor = _isDarkMode ? Color.FromArgb(50, 50, 55) : Color.FromArgb(230, 235, 240);
                    ApplyThemeToChildren(sc.Panel1, panelBg, cardBg, textPrimary, textSecondary);
                    ApplyThemeToChildren(sc.Panel2, panelBg, cardBg, textPrimary, textSecondary);
                    break;
                case Label lbl when lbl != _lblDetailStatusBadge && lbl != _lblStatus:
                    lbl.ForeColor = textPrimary;
                    break;
                case TextBox txt:
                    txt.BackColor = cardBg;
                    txt.ForeColor = textPrimary;
                    break;
                case ComboBox cmb:
                    cmb.BackColor = cardBg;
                    cmb.ForeColor = textPrimary;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = cardBg;
                    nud.ForeColor = textPrimary;
                    break;
                case CheckBox chk:
                    chk.ForeColor = textPrimary;
                    chk.BackColor = panelBg;
                    break;
                case Button button:
                    // Keep the colored portal actions; style ordinary editor buttons for both themes.
                    if (button != _btnOpenKariyerKapisi && button != _btnOpenEDevlet && button != _btnSendTelegramNow && button != _btnOpenSelectedAnnouncements)
                    {
                        button.UseVisualStyleBackColor = false;
                        button.FlatStyle = FlatStyle.Flat;
                        button.BackColor = _isDarkMode ? Color.FromArgb(55, 65, 80) : Color.FromArgb(235, 241, 248);
                        button.ForeColor = textPrimary;
                        button.FlatAppearance.BorderColor = _isDarkMode ? Color.FromArgb(85, 95, 110) : Color.FromArgb(185, 200, 215);
                    }
                    break;
                case DateTimePicker dtp:
                    dtp.CalendarMonthBackground = cardBg;
                    dtp.CalendarForeColor = textPrimary;
                    break;
                case RichTextBox rtb when rtb != _rtbLog:
                    rtb.BackColor = cardBg;
                    rtb.ForeColor = textPrimary;
                    break;
                default:
                    if (c.HasChildren)
                        ApplyThemeToChildren(c, panelBg, cardBg, textPrimary, textSecondary);
                    break;
            }
        }
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
