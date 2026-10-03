using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly ILogger<MainForm> _logger;
    private ProfileOptions _profile;
    private AppConfig _config;

    // UI Controls
    private TabControl _tabControl = null!;
    private DataGridView _gridAnnouncements = null!;
    private ComboBox _cmbFilter = null!;
    private TextBox _txtSearch = null!;
    private Button _btnScanNow = null!;
    private Label _lblStatus = null!;
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

    // Profile UI Controls
    private TextBox _txtProfileDept = null!;
    private ComboBox _cmbProfileLevel = null!;
    private TextBox _txtKpssType = null!;
    private NumericUpDown _numKpssScore = null!;
    private NumericUpDown _numKpssYear = null!;
    private NumericUpDown _numExpYears = null!;
    private TextBox _txtExpField = null!;
    private CheckBox _chkExpKnown = null!;
    private TextBox _txtCities = null!;
    private TextBox _txtDriving = null!;

    // Telegram UI Controls
    private TextBox _txtTgToken = null!;
    private TextBox _txtTgChatId = null!;
    private CheckBox _chkTgEnabled = null!;
    private Button _btnTgTest = null!;

    // Data Cache
    private List<AnnouncementDisplayItem> _cachedItems = new();
    private AnnouncementDisplayItem? _selectedItem = null;

    public MainForm(
        IServiceProvider serviceProvider,
        ScanCoordinator scanCoordinator,
        IAnnouncementRepository repository,
        TelegramNotifier telegramNotifier,
        IOptions<ProfileOptions> profileOptions,
        IOptions<AppConfig> appConfig,
        ILogger<MainForm> logger)
    {
        _serviceProvider = serviceProvider;
        _scanCoordinator = scanCoordinator;
        _repository = repository;
        _telegramNotifier = telegramNotifier;
        _logger = logger;
        _profile = profileOptions.Value;
        _config = appConfig.Value;

        InitializeComponentsCustom();
        GuiLoggerProvider.OnLogReceived += HandleLogReceived;
    }

    private void InitializeComponentsCustom()
    {
        Text = "KariyerTakip — Kamu İlan Asistanı";
        Size = new Size(1200, 780);
        MinimumSize = new Size(1000, 650);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        BackColor = Color.FromArgb(244, 246, 249);

        // 1. Top Header Bar
        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
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

        var lblAppSub = new Label
        {
            Text = "Kişisel Kamu İşe Alım & Kadro Uygunluk Takipçisi",
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.FromArgb(180, 200, 220),
            AutoSize = true,
            Location = new Point(17, 40)
        };

        _btnScanNow = new Button
        {
            Text = "⚡ Şimdi Tara",
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(0, 122, 255),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(140, 42),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 165, 14)
        };
        _btnScanNow.FlatAppearance.BorderSize = 0;
        _btnScanNow.Click += async (s, e) => await StartScanAsync();

        _lblStatus = new Label
        {
            Text = "Hazır",
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(150, 220, 150),
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 380, 25)
        };

        topPanel.Controls.Add(lblAppTitle);
        topPanel.Controls.Add(lblAppSub);
        topPanel.Controls.Add(_btnScanNow);
        topPanel.Controls.Add(_lblStatus);
        Controls.Add(topPanel);

        // 2. Main TabControl
        _tabControl = new TabControl
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
        var tabProfile = new TabPage("👤 Profilim (profile.json)") { BackColor = Color.White };
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

        // Load Initial Data on Shown
        Shown += async (s, e) => await LoadAnnouncementsFromDbAsync();
    }

    private void SetupAnnouncementsTab(TabPage tab)
    {
        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 580,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(230, 235, 240)
        };

        // Left Panel (Filter + Grid)
        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10) };

        var filterPanel = new Panel { Dock = DockStyle.Top, Height = 45, BackColor = Color.White };
        var lblFilter = new Label { Text = "Filtre:", Location = new Point(0, 10), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        _cmbFilter = new ComboBox { Location = new Point(50, 7), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbFilter.Items.AddRange(new object[] { "Tüm İlanlar", "✅ Uygun İlanlar", "⚠️ Kontrol Gerekli", "❌ Uygun Olmayanlar" });
        _cmbFilter.SelectedIndex = 0;
        _cmbFilter.SelectedIndexChanged += (s, e) => ApplyFilter();

        _txtSearch = new TextBox { Location = new Point(240, 7), Width = 200, PlaceholderText = "Kurum / İlan Ara..." };
        _txtSearch.TextChanged += (s, e) => ApplyFilter();

        var btnRefresh = new Button { Text = "🔄 Yenile", Location = new Point(450, 6), Width = 80, Height = 28 };
        btnRefresh.Click += async (s, e) => await LoadAnnouncementsFromDbAsync();

        filterPanel.Controls.Add(lblFilter);
        filterPanel.Controls.Add(_cmbFilter);
        filterPanel.Controls.Add(_txtSearch);
        filterPanel.Controls.Add(btnRefresh);

        _gridAnnouncements = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };

        _gridAnnouncements.Columns.Add("Status", "Durum");
        _gridAnnouncements.Columns.Add("Institution", "Kurum");
        _gridAnnouncements.Columns.Add("Title", "İlan Başlığı");
        _gridAnnouncements.Columns.Add("EndDate", "Son Başvuru");

        _gridAnnouncements.Columns["Status"]!.Width = 100;
        _gridAnnouncements.Columns["Status"]!.FillWeight = 20;
        _gridAnnouncements.Columns["Institution"]!.FillWeight = 40;
        _gridAnnouncements.Columns["Title"]!.FillWeight = 60;
        _gridAnnouncements.Columns["EndDate"]!.Width = 120;
        _gridAnnouncements.Columns["EndDate"]!.FillWeight = 25;

        _gridAnnouncements.SelectionChanged += GridAnnouncements_SelectionChanged;

        leftPanel.Controls.Add(_gridAnnouncements);
        leftPanel.Controls.Add(filterPanel);
        mainSplit.Panel1.Controls.Add(leftPanel);

        // Right Panel (Detailed View & Actions)
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 252, 255), Padding = new Padding(15) };

        var pnlDetailHeader = new Panel { Dock = DockStyle.Top, Height = 110, BackColor = Color.FromArgb(250, 252, 255) };
        _lblDetailStatusBadge = new Label
        {
            Text = "BİLGİ",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(50, 150, 250),
            AutoSize = true,
            Padding = new Padding(6, 3, 6, 3),
            Location = new Point(0, 0)
        };

        _lblDetailInstitution = new Label
        {
            Text = "Bir ilan seçiniz",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 37, 65),
            Location = new Point(0, 28),
            AutoSize = true
        };

        _lblDetailTitle = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(70, 80, 95),
            Location = new Point(0, 55),
            Size = new Size(500, 30)
        };

        _lblDetailDates = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(100, 110, 120),
            Location = new Point(0, 88),
            AutoSize = true
        };

        pnlDetailHeader.Controls.Add(_lblDetailStatusBadge);
        pnlDetailHeader.Controls.Add(_lblDetailInstitution);
        pnlDetailHeader.Controls.Add(_lblDetailTitle);
        pnlDetailHeader.Controls.Add(_lblDetailDates);

        // Action Buttons at Bottom of Right Panel
        var pnlActions = new Panel { Dock = DockStyle.Bottom, Height = 55, BackColor = Color.FromArgb(250, 252, 255) };
        _btnOpenKariyerKapisi = new Button
        {
            Text = "🌐 Kariyer Kapısı İlanı",
            Width = 160,
            Height = 38,
            Location = new Point(0, 8),
            BackColor = Color.FromArgb(15, 37, 65),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnOpenKariyerKapisi.Click += (s, e) => OpenUrl(_selectedItem?.Record.DetailUrl);

        _btnOpenEDevlet = new Button
        {
            Text = "📝 e-Devlet Başvuru",
            Width = 160,
            Height = 38,
            Location = new Point(170, 8),
            BackColor = Color.FromArgb(200, 30, 30),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnOpenEDevlet.Click += (s, e) => OpenUrl(_selectedItem?.Record.ApplicationUrl);

        _btnSendTelegramNow = new Button
        {
            Text = "📲 Telegram'a At",
            Width = 140,
            Height = 38,
            Location = new Point(340, 8),
            BackColor = Color.FromArgb(0, 136, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand
        };
        _btnSendTelegramNow.Click += async (s, e) => await SendSelectedToTelegramAsync();

        pnlActions.Controls.Add(_btnOpenKariyerKapisi);
        pnlActions.Controls.Add(_btnOpenEDevlet);
        pnlActions.Controls.Add(_btnSendTelegramNow);

        _rtbDetailContent = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f)
        };

        rightPanel.Controls.Add(_rtbDetailContent);
        rightPanel.Controls.Add(pnlDetailHeader);
        rightPanel.Controls.Add(pnlActions);
        mainSplit.Panel2.Controls.Add(rightPanel);

        tab.Controls.Add(mainSplit);
    }

    private void SetupProfileTab(TabPage tab)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(25) };

        int y = 20;
        var lblHeader = new Label
        {
            Text = "👤 Kullanıcı Profili ve Şartlar",
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 37, 65),
            Location = new Point(25, y),
            AutoSize = true
        };
        scroll.Controls.Add(lblHeader);

        y += 45;
        // Bölüm
        scroll.Controls.Add(new Label { Text = "Mezun Olunan / Okunan Bölüm:", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtProfileDept = new TextBox { Text = _profile.Department, Location = new Point(25, y + 25), Width = 350 };
        scroll.Controls.Add(_txtProfileDept);

        // Öğrenim Düzeyi
        scroll.Controls.Add(new Label { Text = "Öğrenim Düzeyi:", Location = new Point(400, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _cmbProfileLevel = new ComboBox { Location = new Point(400, y + 25), Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbProfileLevel.Items.AddRange(new object[] { "Ön Lisans", "Lisans", "Ortaöğretim / Lise", "Yüksek Lisans" });
        _cmbProfileLevel.SelectedItem = _profile.EducationLevel;
        if (_cmbProfileLevel.SelectedIndex < 0) _cmbProfileLevel.SelectedIndex = 0;
        scroll.Controls.Add(_cmbProfileLevel);

        y += 70;
        // KPSS Puanı
        scroll.Controls.Add(new Label { Text = "KPSS Puan Türü:", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        var firstScore = _profile.KpssScores.FirstOrDefault() ?? new KpssScoreEntry { ScoreType = "P93", Score = 70, ExamYear = 2024 };
        _txtKpssType = new TextBox { Text = firstScore.ScoreType, Location = new Point(25, y + 25), Width = 150 };
        scroll.Controls.Add(_txtKpssType);

        scroll.Controls.Add(new Label { Text = "KPSS Puanı:", Location = new Point(200, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _numKpssScore = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 100, Value = (decimal)firstScore.Score, Location = new Point(200, y + 25), Width = 120 };
        scroll.Controls.Add(_numKpssScore);

        scroll.Controls.Add(new Label { Text = "Sınav Yılı:", Location = new Point(350, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _numKpssYear = new NumericUpDown { Minimum = 2020, Maximum = 2030, Value = firstScore.ExamYear, Location = new Point(350, y + 25), Width = 120 };
        scroll.Controls.Add(_numKpssYear);

        y += 70;
        // Deneyim
        scroll.Controls.Add(new Label { Text = "Mesleki Tecrübe (Yıl):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _numExpYears = new NumericUpDown { Minimum = 0, Maximum = 40, Value = (decimal)_profile.Experience.Years, Location = new Point(25, y + 25), Width = 150 };
        scroll.Controls.Add(_numExpYears);

        scroll.Controls.Add(new Label { Text = "Tecrübe Alanı:", Location = new Point(200, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtExpField = new TextBox { Text = _profile.Experience.Field, Location = new Point(200, y + 25), Width = 270 };
        scroll.Controls.Add(_txtExpField);

        _chkExpKnown = new CheckBox { Text = "Tecrübe durumu profilimde belirtildi (İşaretsizse 'Bilinmiyor' sayılır)", Checked = _profile.Experience.IsKnown, Location = new Point(25, y + 60), AutoSize = true };
        scroll.Controls.Add(_chkExpKnown);

        y += 95;
        // Şehir Tercihleri
        scroll.Controls.Add(new Label { Text = "Şehir Tercihleri (Virgülle ayırınız. Boş bırakırsanız TÜM TÜRKİYE kabul edilir):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtCities = new TextBox { Text = string.Join(", ", _profile.CityPreferences), Location = new Point(25, y + 25), Width = 575 };
        scroll.Controls.Add(_txtCities);

        y += 70;
        // Ehliyet
        scroll.Controls.Add(new Label { Text = "Ehliyet Sınıfları (Örn: B, A2):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtDriving = new TextBox { Text = string.Join(", ", _profile.DrivingLicenses), Location = new Point(25, y + 25), Width = 300 };
        scroll.Controls.Add(_txtDriving);

        y += 75;
        var btnSaveProfile = new Button
        {
            Text = "💾 Profili Kaydet & İlanları Yeniden Değerlendir",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(40, 167, 69),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(380, 45),
            Location = new Point(25, y),
            Cursor = Cursors.Hand
        };
        btnSaveProfile.Click += async (s, e) => await SaveProfileAndReevaluateAsync();
        scroll.Controls.Add(btnSaveProfile);

        tab.Controls.Add(scroll);
    }

    private void SetupSettingsTab(TabPage tab)
    {
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(25) };

        int y = 20;
        var lblHeader = new Label
        {
            Text = "⚙️ Telegram Bildirim & Sistem Ayarları",
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 37, 65),
            Location = new Point(25, y),
            AutoSize = true
        };
        scroll.Controls.Add(lblHeader);

        y += 45;
        _chkTgEnabled = new CheckBox
        {
            Text = "Telegram Bildirimlerini Etkinleştir",
            Checked = _config.Telegram.Enabled,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Location = new Point(25, y),
            AutoSize = true
        };
        scroll.Controls.Add(_chkTgEnabled);

        y += 40;
        scroll.Controls.Add(new Label { Text = "Telegram Bot Token (@BotFather'dan aldığınız anahtar):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtTgToken = new TextBox { Text = _config.Telegram.BotToken, Location = new Point(25, y + 25), Width = 550 };
        scroll.Controls.Add(_txtTgToken);

        y += 70;
        scroll.Controls.Add(new Label { Text = "Telegram Chat ID (Mesajın iletileceği sohbet kimliğiniz):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtTgChatId = new TextBox { Text = _config.Telegram.ChatId, Location = new Point(25, y + 25), Width = 300 };
        scroll.Controls.Add(_txtTgChatId);

        _btnTgTest = new Button
        {
            Text = "📨 Test Bildirimi Gönder",
            Location = new Point(340, y + 23),
            Width = 180,
            Height = 30,
            BackColor = Color.FromArgb(0, 136, 204),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        _btnTgTest.Click += async (s, e) => await TestTelegramConnectionAsync();
        scroll.Controls.Add(_btnTgTest);

        y += 90;
        var btnSaveSettings = new Button
        {
            Text = "💾 Ayarları Kaydet",
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(15, 37, 65),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(220, 42),
            Location = new Point(25, y),
            Cursor = Cursors.Hand
        };
        btnSaveSettings.Click += (s, e) => SaveAppSettings();
        scroll.Controls.Add(btnSaveSettings);

        tab.Controls.Add(scroll);
    }

    private void SetupLogsTab(TabPage tab)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

        var topBar = new Panel { Dock = DockStyle.Top, Height = 40 };
        var btnClearLogs = new Button { Text = "🗑️ Logları Temizle", Location = new Point(0, 5), Width = 130, Height = 30 };
        btnClearLogs.Click += (s, e) => _rtbLog.Clear();
        topBar.Controls.Add(btnClearLogs);

        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(20, 24, 30),
            ForeColor = Color.FromArgb(220, 230, 240),
            Font = new Font("Consolas", 9.5f),
            BorderStyle = BorderStyle.None
        };

        panel.Controls.Add(_rtbLog);
        panel.Controls.Add(topBar);
        tab.Controls.Add(panel);
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
        _btnScanNow.Enabled = false;
        _lblStatus.Text = "⏳ İlanlar taranıyor...";
        _lblStatus.ForeColor = Color.Gold;

        try
        {
            await Task.Run(async () =>
            {
                await _scanCoordinator.RunScanAsync();
            });

            _lblStatus.Text = "✅ Tarama tamamlandı";
            _lblStatus.ForeColor = Color.FromArgb(150, 255, 150);
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
        }
    }

    private async Task LoadAnnouncementsFromDbAsync()
    {
        try
        {
            await _repository.InitializeDatabaseAsync();
            var announcements = await _repository.GetAllAnnouncementsAsync();

            _cachedItems.Clear();
            foreach (var ann in announcements)
            {
                var evaluations = await _repository.GetEvaluationsByAnnouncementGuidAsync(ann.Guid);
                var positions = await _repository.GetPositionsByAnnouncementGuidAsync(ann.Guid);

                var isEligible = evaluations.Any(e => e.Status == EligibilityStatus.Eligible.ToString());
                var isNeedsReview = evaluations.Any(e => e.Status == EligibilityStatus.NeedsReview.ToString());

                var overallStatus = isEligible ? EligibilityStatus.Eligible :
                                    (isNeedsReview ? EligibilityStatus.NeedsReview : EligibilityStatus.Ineligible);

                _cachedItems.Add(new AnnouncementDisplayItem
                {
                    Record = ann,
                    Evaluations = evaluations,
                    Positions = positions,
                    Status = overallStatus
                });
            }

            ApplyFilter();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Veritabanından ilanlar yüklenirken hata oluştu.");
        }
    }

    private void ApplyFilter()
    {
        var filterIndex = _cmbFilter.SelectedIndex;
        var search = _txtSearch.Text.Trim().ToLowerInvariant();

        var filtered = _cachedItems.Where(item =>
        {
            // Status filter
            if (filterIndex == 1 && item.Status != EligibilityStatus.Eligible) return false;
            if (filterIndex == 2 && item.Status != EligibilityStatus.NeedsReview) return false;
            if (filterIndex == 3 && item.Status != EligibilityStatus.Ineligible) return false;

            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                var match = item.Record.InstitutionName.ToLowerInvariant().Contains(search) ||
                            item.Record.Title.ToLowerInvariant().Contains(search);
                if (!match) return false;
            }

            return true;
        }).ToList();

        _gridAnnouncements.Rows.Clear();
        foreach (var item in filtered)
        {
            var statusText = item.Status switch
            {
                EligibilityStatus.Eligible => "✅ UYGUN",
                EligibilityStatus.NeedsReview => "⚠️ İNCELE",
                _ => "❌ UYGUN DEĞİL"
            };

            var endStr = item.Record.EndDate?.ToString("dd.MM.yyyy HH:mm") ?? "Belirtilmemiş";
            var rowIdx = _gridAnnouncements.Rows.Add(statusText, item.Record.InstitutionName, item.Record.Title, endStr);
            _gridAnnouncements.Rows[rowIdx].Tag = item;

            if (item.Status == EligibilityStatus.Eligible)
            {
                _gridAnnouncements.Rows[rowIdx].DefaultCellStyle.BackColor = Color.FromArgb(235, 255, 235);
            }
            else if (item.Status == EligibilityStatus.NeedsReview)
            {
                _gridAnnouncements.Rows[rowIdx].DefaultCellStyle.BackColor = Color.FromArgb(255, 250, 230);
            }
        }

        if (_gridAnnouncements.Rows.Count > 0)
        {
            _gridAnnouncements.Rows[0].Selected = true;
        }
    }

    private void GridAnnouncements_SelectionChanged(object? sender, EventArgs e)
    {
        if (_gridAnnouncements.SelectedRows.Count == 0)
            return;

        var item = _gridAnnouncements.SelectedRows[0].Tag as AnnouncementDisplayItem;
        if (item == null) return;

        _selectedItem = item;

        _lblDetailInstitution.Text = item.Record.InstitutionName;
        _lblDetailTitle.Text = item.Record.Title;

        var startStr = item.Record.StartDate?.ToString("dd.MM.yyyy HH:mm") ?? "-";
        var endStr = item.Record.EndDate?.ToString("dd.MM.yyyy HH:mm") ?? "-";
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

        // Render rich detail text
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🏛 KURUM: {item.Record.InstitutionName}");
        if (!string.IsNullOrWhiteSpace(item.Record.UnitName))
            sb.AppendLine($"🏢 BİRİM: {item.Record.UnitName}");
        sb.AppendLine($"📋 İLAN: {item.Record.Title}");
        sb.AppendLine($"📅 SON BAŞVURU: {endStr}");
        sb.AppendLine(new string('─', 50));
        sb.AppendLine("KADRO VE DEĞERLENDİRME AYRINTILARI:");
        sb.AppendLine();

        foreach (var pos in item.Positions)
        {
            var eval = item.Evaluations.FirstOrDefault(e => e.PositionId == pos.Id) ??
                       item.Evaluations.FirstOrDefault();

            var statusEmoji = eval?.Status == EligibilityStatus.Eligible.ToString() ? "✅" :
                              (eval?.Status == EligibilityStatus.NeedsReview.ToString() ? "⚠️" : "❌");

            sb.AppendLine($"{statusEmoji} {pos.Title} (Unvan: {pos.Unvan})");
            sb.AppendLine($"   • Şehir / Kontenjan: {pos.Cities} (Toplam: {pos.Quota})");
            if (eval != null)
            {
                sb.AppendLine($"   • Durum: {eval.Status}");
                sb.AppendLine($"   • Gerekçe: {eval.SummaryReason}");
            }
            sb.AppendLine();
        }

        _rtbDetailContent.Text = sb.ToString();
    }

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            MessageBox.Show("Bağlantı adresi bulunamadı.", "Uyarı", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private async Task SendSelectedToTelegramAsync()
    {
        if (_selectedItem == null) return;

        var evaluator = _serviceProvider.GetRequiredService<EligibilityEvaluator>();
        var matchingPositions = new List<PositionEvaluation>();

        foreach (var pos in _selectedItem.Positions)
        {
            var eval = evaluator.EvaluatePosition(new AltIlanResponse
            {
                IlanBaslik = pos.Title,
                Unvan = pos.Unvan,
                IlanMetni = pos.RawText
            }, string.Empty, _profile);

            matchingPositions.Add(eval);
        }

        var message = _telegramNotifier.FormatAnnouncementMessage(
            _selectedItem.Record,
            matchingPositions,
            "📢 <b>KULLANICI TARAFINDAN GÖNDERİLEN İLAN</b>");

        var success = await _telegramNotifier.SendMessageAsync(message);
        if (success)
        {
            MessageBox.Show("İlan Telegram'a başarıyla gönderildi!", "Başarılı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("Telegram mesajı gönderilemedi. Lütfen Ayarlar sekmesindeki Bot Token ve Chat ID değerlerini kontrol ediniz.", "Gönderim Hatası", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task SaveProfileAndReevaluateAsync()
    {
        _profile.Department = _txtProfileDept.Text.Trim();
        _profile.EducationLevel = _cmbProfileLevel.SelectedItem?.ToString() ?? "Ön Lisans";
        _profile.KpssScores = new List<KpssScoreEntry>
        {
            new KpssScoreEntry
            {
                ScoreType = _txtKpssType.Text.Trim(),
                Score = (double)_numKpssScore.Value,
                ExamYear = (int)_numKpssYear.Value
            }
        };
        _profile.Experience.Years = (double)_numExpYears.Value;
        _profile.Experience.Field = _txtExpField.Text.Trim();
        _profile.Experience.IsKnown = _chkExpKnown.Checked;
        _profile.CityPreferences = _txtCities.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        _profile.DrivingLicenses = _txtDriving.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

        var json = JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync("profile.json", json);

        MessageBox.Show("Profil başarıyla kaydedildi. İlanlar yeni profilinize göre yeniden değerlendiriliyor...", "Profil Kaydedildi", MessageBoxButtons.OK, MessageBoxIcon.Information);

        // Re-evaluate in background
        await StartScanAsync();
    }

    private void SaveAppSettings()
    {
        _config.Telegram.Enabled = _chkTgEnabled.Checked;
        _config.Telegram.BotToken = _txtTgToken.Text.Trim();
        _config.Telegram.ChatId = _txtTgChatId.Text.Trim();

        var fullConfig = new { KariyerTakip = _config };
        var json = JsonSerializer.Serialize(fullConfig, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("appsettings.json", json);

        MessageBox.Show("Ayarlar başarıyla kaydedildi.", "Ayarlar Kaydedildi", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task TestTelegramConnectionAsync()
    {
        var token = _txtTgToken.Text.Trim();
        var chatId = _txtTgChatId.Text.Trim();

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(chatId))
        {
            MessageBox.Show("Lütfen önce Bot Token ve Chat ID alanlarını doldurunuz.", "Eksik Bilgi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _btnTgTest.Enabled = false;
        try
        {
            using var http = new HttpClient();
            var url = $"https://api.telegram.org/bot{token}/sendMessage";
            var payload = new
            {
                chat_id = chatId,
                text = "🔔 <b>KariyerTakip Test Bildirimi</b>\n\nTelegram bağlantınız başarıyla kuruldu ve çalışıyor!",
                parse_mode = "HTML"
            };

            var response = await http.PostAsJsonAsync(url, payload);
            if (response.IsSuccessStatusCode)
            {
                MessageBox.Show("Test mesajı başarıyla Telegram'a ulaştı!", "Tebrikler", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                var err = await response.Content.ReadAsStringAsync();
                MessageBox.Show($"Telegram API hatası döndürdü:\n{err}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Bağlantı hatası:\n{ex.Message}", "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _btnTgTest.Enabled = true;
        }
    }
}

public class AnnouncementDisplayItem
{
    public AnnouncementRecord Record { get; set; } = new();
    public List<EvaluationRecord> Evaluations { get; set; } = new();
    public List<PositionRecord> Positions { get; set; } = new();
    public EligibilityStatus Status { get; set; }
}
