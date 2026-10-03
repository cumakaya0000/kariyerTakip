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

    // Profile UI Controls
    private TextBox _txtProfileDept = null!;
    private ComboBox _cmbProfileLevel = null!;
    private ComboBox _cmbGraduationStatus = null!;
    private ComboBox _cmbKpssStatus = null!;
    private TextBox _txtKpssType = null!;
    private NumericUpDown _numKpssScore = null!;
    private NumericUpDown _numKpssYear = null!;
    private DateTimePicker _dtpBirthDate = null!;
    private ComboBox _cmbMilitary = null!;
    private NumericUpDown _numExpMonths = null!;
    private TextBox _txtExpField = null!;
    private CheckBox _chkExpKnown = null!;
    private CheckBox _chkExpDoc = null!;
    private TextBox _txtCertificates = null!;
    private TextBox _txtCities = null!;
    private TextBox _txtDriving = null!;
    private Button _btnSaveProfile = null!;

    // Telegram UI Controls
    private TextBox _txtTgToken = null!;
    private TextBox _txtTgChatId = null!;
    private CheckBox _chkTgEnabled = null!;
    private Button _btnTgTest = null!;

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
            Height = 72,
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
            AutoSize = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 460, 26)
        };

        _btnThemeToggle = new Button
        {
            Text = "🌙",
            Font = new Font("Segoe UI", 14, FontStyle.Regular),
            ForeColor = Color.Gold,
            BackColor = Color.FromArgb(35, 60, 90),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(42, 42),
            Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(topPanel.Width - 285, 15)
        };
        _btnThemeToggle.FlatAppearance.BorderSize = 0;
        _btnThemeToggle.Click += (s, e) => ToggleTheme();

        topPanel.Controls.Add(lblAppTitle);
        topPanel.Controls.Add(_lblLastScanTime);
        topPanel.Controls.Add(_btnScanNow);
        topPanel.Controls.Add(_btnCancelScan);
        topPanel.Controls.Add(_btnThemeToggle);
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

        Shown += async (s, e) => await LoadAnnouncementsFromDbAsync();
    }

    private void SetupAnnouncementsTab(TabPage tab)
    {
        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 600,
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

        _txtSearch = new TextBox { Location = new Point(240, 7), Width = 210, PlaceholderText = "Kurum / İlan Ara..." };
        _txtSearch.TextChanged += (s, e) => ApplyFilter();

        var btnRefresh = new Button { Text = "🔄 Yenile", Location = new Point(460, 6), Width = 80, Height = 28 };
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
            ReadOnly = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = true,
            RowHeadersWidth = 24,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        // Row header styling – show only the selection arrow, no text
        _gridAnnouncements.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
        _gridAnnouncements.RowHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 37, 65);
        _gridAnnouncements.RowHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(15, 37, 65);
        _gridAnnouncements.RowHeadersDefaultCellStyle.SelectionForeColor = Color.White;


        _gridAnnouncements.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Checked", HeaderText = "Seç", Width = 42,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ToolTipText = "Birden fazla ilanı birlikte açmak için işaretleyin"
        });
        _gridAnnouncements.Columns.Add("Status", "Durum");
        _gridAnnouncements.Columns.Add("Institution", "Kurum");
        _gridAnnouncements.Columns.Add("Title", "İlan Başlığı");
        _gridAnnouncements.Columns.Add("EndDate", "Son Başvuru");
        foreach (DataGridViewColumn column in _gridAnnouncements.Columns)
            column.ReadOnly = column.Name != "Checked";
        _gridAnnouncements.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (_gridAnnouncements.IsCurrentCellDirty && _gridAnnouncements.CurrentCell?.OwningColumn?.Name == "Checked")
                _gridAnnouncements.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _gridAnnouncements.CellValueChanged += (s, e) =>
        {
            if (_isUpdatingAnnouncementRows || e.RowIndex < 0 || e.ColumnIndex != _gridAnnouncements.Columns["Checked"]!.Index)
                return;
            var row = _gridAnnouncements.Rows[e.RowIndex];
            if (row.Tag is not AnnouncementDisplayItem item) return;
            if (row.Cells["Checked"].Value is true) _checkedAnnouncementGuids.Add(item.Record.Guid);
            else _checkedAnnouncementGuids.Remove(item.Record.Guid);
            UpdateCheckedAnnouncements();
        };

        _gridAnnouncements.Columns["Status"]!.Width = 100;
        _gridAnnouncements.Columns["Status"]!.FillWeight = 20;
        _gridAnnouncements.Columns["Institution"]!.FillWeight = 40;
        _gridAnnouncements.Columns["Title"]!.FillWeight = 60;
        _gridAnnouncements.Columns["EndDate"]!.Width = 120;
        _gridAnnouncements.Columns["EndDate"]!.FillWeight = 25;
        // Initialize tooltip for rows
        _gridTooltip = new ToolTip { AutoPopDelay = 5000, InitialDelay = 500, ReshowDelay = 200, ShowAlways = true };
        // Show placeholder on hover
        _gridAnnouncements.CellToolTipTextNeeded += (s, e) => {
            if (e.RowIndex >= 0)
                e.ToolTipText = "İlana git";
        };
        // Change cursor to hand on hover
        _gridAnnouncements.CellMouseEnter += (s, e) => {
            if (e.RowIndex >= 0) _gridAnnouncements.Cursor = Cursors.Hand;
        };
        _gridAnnouncements.CellMouseLeave += (s, e) => {
            _gridAnnouncements.Cursor = Cursors.Default;
        };

        _gridAnnouncements.SelectionChanged += GridAnnouncements_SelectionChanged;
        _gridAnnouncements.CellDoubleClick += GridAnnouncements_CellDoubleClick;

        var bulkActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(0, 5, 0, 0),
            WrapContents = false
        };
        _lblCheckedAnnouncements = new Label { Text = "İlanları kutucuklarla seçin", AutoSize = true, Margin = new Padding(3, 9, 12, 0) };
        _btnOpenSelectedAnnouncements = new Button
        {
            Text = "Seçili ilanlara git", AutoSize = true, Height = 32, Visible = false,
            BackColor = Color.FromArgb(15, 37, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
        };
        _btnOpenSelectedAnnouncements.Click += (s, e) =>
        {
            foreach (var url in GetCheckedAnnouncementUrls()) OpenUrl(url);
        };
        bulkActions.Controls.Add(_lblCheckedAnnouncements);
        bulkActions.Controls.Add(_btnOpenSelectedAnnouncements);
        leftPanel.Controls.Add(_gridAnnouncements);
        leftPanel.Controls.Add(filterPanel);
        leftPanel.Controls.Add(bulkActions);
        mainSplit.Panel1.Controls.Add(leftPanel);

        // Right Panel (Detailed View & Actions)
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(250, 252, 255), Padding = new Padding(15) };

        var pnlDetailHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 4, Padding = new Padding(0, 0, 0, 12)
        };
        pnlDetailHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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

        var headerLabels = new[] { _lblDetailStatusBadge, _lblDetailInstitution, _lblDetailTitle, _lblDetailDates };
        for (int i = 0; i < headerLabels.Length; i++)
        {
            var label = headerLabels[i];
            label.AutoSize = true;
            label.Margin = new Padding(0, 0, 0, 8);
            if (i > 0) label.Dock = DockStyle.Fill;
            pnlDetailHeader.Controls.Add(label, 0, i);
        }
        pnlDetailHeader.SizeChanged += (s, e) =>
        {
            foreach (var label in headerLabels)
                label.MaximumSize = new Size(Math.Max(100, pnlDetailHeader.ClientSize.Width), 0);
        };

        // Action Buttons at Bottom
        var pnlActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 10, 0, 0), WrapContents = true
        };
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
        _btnOpenKariyerKapisi.Click += (s, e) => OpenUrl(_selectedItem == null ? null : GetAnnouncementDetailUrl(_selectedItem.Record));

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
            Font = new Font("Segoe UI", 10.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false
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
            Text = "👤 Profil Bilgileri & Başvuru Kriterleri",
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 37, 65),
            Location = new Point(25, y),
            AutoSize = true
        };
        scroll.Controls.Add(lblHeader);

        y += 45;
        // Bölüm
        scroll.Controls.Add(new Label { Text = "Bölüm Adı:", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtProfileDept = new TextBox { Text = _profile.Department, Location = new Point(25, y + 25), Width = 300 };
        scroll.Controls.Add(_txtProfileDept);

        // Öğrenim Düzeyi
        scroll.Controls.Add(new Label { Text = "Öğrenim Düzeyi:", Location = new Point(340, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _cmbProfileLevel = new ComboBox { Location = new Point(340, y + 25), Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbProfileLevel.Items.AddRange(new object[] { "Ön Lisans", "Lisans", "Ortaöğretim / Lise", "Yüksek Lisans" });
        _cmbProfileLevel.SelectedItem = _profile.EducationLevel;
        if (_cmbProfileLevel.SelectedIndex < 0) _cmbProfileLevel.SelectedIndex = 0;
        scroll.Controls.Add(_cmbProfileLevel);

        // Mezuniyet Durumu
        scroll.Controls.Add(new Label { Text = "Mezuniyet:", Location = new Point(515, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _cmbGraduationStatus = new ComboBox { Location = new Point(515, y + 25), Width = 130, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbGraduationStatus.Items.AddRange(new object[] { "Mezun", "Öğrenci", "Bilinmiyor" });
        _cmbGraduationStatus.SelectedItem = _profile.GraduationStatus;
        if (_cmbGraduationStatus.SelectedIndex < 0) _cmbGraduationStatus.SelectedIndex = 0;
        scroll.Controls.Add(_cmbGraduationStatus);

        y += 70;
        // KPSS
        scroll.Controls.Add(new Label { Text = "KPSS Durumu:", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _cmbKpssStatus = new ComboBox { Location = new Point(25, y + 25), Width = 110, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbKpssStatus.Items.AddRange(new object[] { "Var", "Yok", "Bilinmiyor" });
        _cmbKpssStatus.SelectedItem = _profile.KpssStatus;
        if (_cmbKpssStatus.SelectedIndex < 0) _cmbKpssStatus.SelectedIndex = 0;
        scroll.Controls.Add(_cmbKpssStatus);

        var firstScore = _profile.KpssScores.FirstOrDefault() ?? new KpssScoreEntry { ScoreType = "P93", Score = 75, ExamYear = 2024 };
        scroll.Controls.Add(new Label { Text = "Puan Türü:", Location = new Point(145, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtKpssType = new TextBox { Text = firstScore.ScoreType, Location = new Point(145, y + 25), Width = 100 };
        scroll.Controls.Add(_txtKpssType);

        scroll.Controls.Add(new Label { Text = "Puan:", Location = new Point(255, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _numKpssScore = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 100, Value = (decimal)firstScore.Score, Location = new Point(255, y + 25), Width = 90 };
        scroll.Controls.Add(_numKpssScore);

        scroll.Controls.Add(new Label { Text = "Sınav Yılı:", Location = new Point(355, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _numKpssYear = new NumericUpDown { Minimum = 2018, Maximum = 2035, Value = firstScore.ExamYear, Location = new Point(355, y + 25), Width = 90 };
        scroll.Controls.Add(_numKpssYear);

        // Doğum Tarihi (Yaş Hesabı İçin)
        scroll.Controls.Add(new Label { Text = "Doğum Tarihi:", Location = new Point(455, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _dtpBirthDate = new DateTimePicker { Format = DateTimePickerFormat.Short, Value = _profile.BirthDate ?? new DateTime(1998, 1, 1), Location = new Point(455, y + 25), Width = 120 };
        scroll.Controls.Add(_dtpBirthDate);

        // Askerlik
        scroll.Controls.Add(new Label { Text = "Askerlik:", Location = new Point(585, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _cmbMilitary = new ComboBox { Location = new Point(585, y + 25), Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbMilitary.Items.AddRange(new object[] { "Muaf / Yapıldı", "Tecilli", "Yapılmadı", "Bilinmiyor" });
        _cmbMilitary.SelectedItem = _profile.MilitaryStatus;
        if (_cmbMilitary.SelectedIndex < 0) _cmbMilitary.SelectedIndex = 0;
        scroll.Controls.Add(_cmbMilitary);

        y += 70;
        // Deneyim
        scroll.Controls.Add(new Label { Text = "Mesleki Tecrübe (Ay):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _numExpMonths = new NumericUpDown { Minimum = 0, Maximum = 480, Value = _profile.Experience.TotalMonths, Location = new Point(25, y + 25), Width = 130 };
        scroll.Controls.Add(_numExpMonths);

        scroll.Controls.Add(new Label { Text = "Tecrübe Alanı:", Location = new Point(165, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtExpField = new TextBox { Text = _profile.Experience.Field, Location = new Point(165, y + 25), Width = 280 };
        scroll.Controls.Add(_txtExpField);

        _chkExpKnown = new CheckBox { Text = "Tecrübe bilgisi profilimde belirtildi (İşaretsizse 'Bilinmiyor' sayılır)", Checked = _profile.Experience.IsKnown, Location = new Point(25, y + 60), AutoSize = true };
        scroll.Controls.Add(_chkExpKnown);

        _chkExpDoc = new CheckBox { Text = "Tecrübem resmi SGK / çalışma belgesi ile belgelenebilir", Checked = _profile.Experience.IsDocumented, Location = new Point(25, y + 85), AutoSize = true };
        scroll.Controls.Add(_chkExpDoc);

        y += 120;
        // Şehir Tercihleri
        scroll.Controls.Add(new Label { Text = "Şehir Tercihleri (Virgülle ayırınız. Boş bırakırsanız TÜM TÜRKİYE kabul edilir):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtCities = new TextBox { Text = string.Join(", ", _profile.CityPreferences), Location = new Point(25, y + 25), Width = 600 };
        scroll.Controls.Add(_txtCities);

        y += 70;
        // Ehliyet & Sertifikalar
        scroll.Controls.Add(new Label { Text = "Ehliyet Sınıfları (Örn: B, A2):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtDriving = new TextBox { Text = string.Join(", ", _profile.DrivingLicenses), Location = new Point(25, y + 25), Width = 200 };
        scroll.Controls.Add(_txtDriving);

        scroll.Controls.Add(new Label { Text = "Sertifikalar / Belgeler (Virgülle ayırınız):", Location = new Point(240, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtCertificates = new TextBox { Text = string.Join(", ", _profile.Certificates), Location = new Point(240, y + 25), Width = 385 };
        scroll.Controls.Add(_txtCertificates);

        y += 75;
        _btnSaveProfile = new Button
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
        _btnSaveProfile.Click += async (s, e) => await SaveProfileAndReevaluateAsync();
        scroll.Controls.Add(_btnSaveProfile);

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
        scroll.Controls.Add(new Label { Text = "Telegram Bot Token (@BotFather'dan aldığınız token):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
        _txtTgToken = new TextBox { Text = _config.Telegram.BotToken, Location = new Point(25, y + 25), Width = 550 };
        scroll.Controls.Add(_txtTgToken);

        y += 70;
        scroll.Controls.Add(new Label { Text = "Telegram Chat ID (Mesajın iletileceği sohbet ID'si):", Location = new Point(25, y), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) });
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
        if (_isScanning) return;

        _isScanning = true;
        _scanCts = new CancellationTokenSource();
        _btnScanNow.Enabled = false;
        _btnCancelScan.Enabled = true;
        _btnSaveProfile.Enabled = false;
        _lblStatus.Text = "⏳ İlanlar taranıyor...";
        _lblStatus.ForeColor = Color.Gold;

        try
        {
            var result = await Task.Run(async () => await _scanCoordinator.RunScanAsync(_scanCts.Token));

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
            _btnSaveProfile.Enabled = true;
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
                _lblLastScanTime.Text = $"Son Başarılı Tarama: {lastScan.FinishedAt?.ToLocalTime():dd.MM.yyyy HH:mm}";
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

                var endStr = item.Record.EndDate?.ToString("dd.MM.yyyy HH:mm") ?? "Belirtilmemiş";
                var rowIdx = _gridAnnouncements.Rows.Add(_checkedAnnouncementGuids.Contains(item.Record.Guid), statusText, item.Record.InstitutionName, item.Record.Title, endStr);
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
        _btnThemeToggle.Text = _isDarkMode ? "☀️" : "🌙";
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
            foreach (Control c in topPanel.Controls)
            {
                if (c is Label lbl)
                {
                    if (lbl == _lblLastScanTime)
                        lbl.ForeColor = headerSubFg;
                    else if (lbl != _lblStatus)
                        lbl.ForeColor = Color.White;
                }
            }
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
                case Panel p:
                    p.BackColor = panelBg;
                    ApplyThemeToChildren(p, panelBg, cardBg, textPrimary, textSecondary);
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

    private async Task SaveProfileAndReevaluateAsync()
    {
        _profile.Department = _txtProfileDept.Text.Trim();
        _profile.EducationLevel = _cmbProfileLevel.SelectedItem?.ToString() ?? "Ön Lisans";
        _profile.GraduationStatus = _cmbGraduationStatus.SelectedItem?.ToString() ?? "Mezun";
        _profile.KpssStatus = _cmbKpssStatus.SelectedItem?.ToString() ?? "Var";
        _profile.KpssScores = new List<KpssScoreEntry>
        {
            new KpssScoreEntry
            {
                ScoreType = _txtKpssType.Text.Trim(),
                Score = (double)_numKpssScore.Value,
                ExamYear = (int)_numKpssYear.Value
            }
        };
        _profile.BirthDate = _dtpBirthDate.Value;
        _profile.MilitaryStatus = _cmbMilitary.SelectedItem?.ToString() ?? "Muaf / Yapıldı";
        _profile.Experience.TotalMonths = (int)_numExpMonths.Value;
        _profile.Experience.Field = _txtExpField.Text.Trim();
        _profile.Experience.IsKnown = _chkExpKnown.Checked;
        _profile.Experience.IsDocumented = _chkExpDoc.Checked;
        _profile.CityPreferences = _txtCities.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        _profile.DrivingLicenses = _txtDriving.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
        _profile.Certificates = _txtCertificates.Text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();

        var json = JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(AppPaths.ProfileFile, json);

        _logger.LogInformation("Profil güncellendi: {File}", AppPaths.ProfileFile);

        // Re-evaluate cached postings locally
        _btnSaveProfile.Enabled = false;
        _lblStatus.Text = "⏳ İlanlar yerel olarak yeniden değerlendiriliyor...";

        try
        {
            await Task.Run(async () => await _scanCoordinator.ReevaluateCachedAnnouncementsAsync(_profile));
            await LoadAnnouncementsFromDbAsync();
            _lblStatus.Text = "✅ Profil güncellendi ve ilanlar yeniden değerlendirildi";
            MessageBox.Show("Profil kaydedildi ve önbellekteki tüm aktif ilanlar başarıyla yeniden değerlendirildi.", "Tamamlandı", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yerel yeniden değerlendirme başarısız.");
        }
        finally
        {
            _btnSaveProfile.Enabled = true;
        }
    }

    private void SaveAppSettings()
    {
        _config.Telegram.Enabled = _chkTgEnabled.Checked;
        _config.Telegram.BotToken = _txtTgToken.Text.Trim();
        _config.Telegram.ChatId = _txtTgChatId.Text.Trim();

        var fullConfig = new { KariyerTakip = _config };
        var json = JsonSerializer.Serialize(fullConfig, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(AppPaths.AppSettingsFile, json);

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
