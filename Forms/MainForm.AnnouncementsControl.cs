using System.Drawing;
using System.Windows.Forms;
using KariyerTakip.Models;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private sealed class AnnouncementsControl : UserControl
    {
        public AnnouncementsControl(MainForm owner)
        {
            Dock = DockStyle.Fill;
        var mainSplit = new SplitContainer
        {
            Size = new Size(1100, 600),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Panel1MinSize = 350,
            Panel2MinSize = 350,
            SplitterDistance = 550,
            SplitterWidth = 6,
            BackColor = Color.FromArgb(230, 235, 240)
        };
        mainSplit.SizeChanged += (s, e) =>
        {
            if (mainSplit.Width >= mainSplit.Panel1MinSize + mainSplit.Panel2MinSize + mainSplit.SplitterWidth)
                mainSplit.SplitterDistance = Math.Clamp(mainSplit.Width / 2, mainSplit.Panel1MinSize,
                    mainSplit.Width - mainSplit.Panel2MinSize - mainSplit.SplitterWidth);
        };

        // Left Panel (Filter + Grid)
        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10) };

        var filterPanel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Color.White, Padding = new Padding(0, 0, 0, 8) };
        var lblFilter = new Label { Text = "Filtre:", Location = new Point(0, 10), AutoSize = true, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        owner._cmbFilter = new ComboBox { Location = new Point(50, 7), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
        owner._cmbFilter.Items.AddRange(new object[] { "Tüm İlanlar", "✅ Uygun İlanlar", "⚠️ Kontrol Gerekli", "❌ Uygun Olmayanlar" });
        owner._cmbFilter.SelectedIndex = 0;
        owner._cmbFilter.SelectedIndexChanged += (s, e) => owner.ApplyFilter();

        owner._txtSearch = new TextBox { Location = new Point(240, 7), Width = 210, PlaceholderText = "Kurum / İlan Ara..." };
        owner._txtSearch.TextChanged += (s, e) => owner.ApplyFilter();

        var btnRefresh = new Button { Text = "🔄 Yenile", Location = new Point(460, 6), Width = 80, Height = 28 };
        btnRefresh.Click += async (s, e) => await owner.LoadAnnouncementsFromDbAsync();

        filterPanel.Controls.Add(lblFilter);
        filterPanel.Controls.Add(owner._cmbFilter);
        filterPanel.Controls.Add(owner._txtSearch);
        filterPanel.Controls.Add(btnRefresh);
        owner._cmbApplicationFilter = new ComboBox { Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
        owner._cmbApplicationFilter.Items.AddRange(new object[] { "Tüm başvuru durumları", "Takip edilmeyen", "Başvuracağım", "Başvurdum", "Geçtim" });
        owner._cmbApplicationFilter.SelectedIndex = 0;
        owner._cmbDeadlineFilter = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
        owner._cmbDeadlineFilter.Items.AddRange(new object[] { "Tüm tarihler", "Aktif ilanlar", "7 gün içinde biten", "30 gün içinde biten", "Süresi dolanlar" });
        owner._cmbDeadlineFilter.SelectedIndex = 0;
        owner._cmbApplicationFilter.SelectedIndexChanged += (_, _) => owner.ApplyFilter();
        owner._cmbDeadlineFilter.SelectedIndexChanged += (_, _) => owner.ApplyFilter();
        filterPanel.Controls.Add(owner._cmbApplicationFilter);
        filterPanel.Controls.Add(owner._cmbDeadlineFilter);
        var resetFilters = new Button { Text = "Filtreleri sıfırla", AutoSize = true, Height = 28 };
        resetFilters.Click += (_, _) => {
            owner._cmbFilter.SelectedIndex = 0; owner._cmbApplicationFilter.SelectedIndex = 0;
            owner._cmbDeadlineFilter.SelectedIndex = 0; owner._txtSearch.Clear();
        };
        filterPanel.Controls.Add(resetFilters);
        owner._lblListSummary = new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        filterPanel.Controls.Add(owner._lblListSummary);
        owner._txtSearch.PlaceholderText = "Kurum / İlan / Unvan / Şehir...";

        owner._gridAnnouncements = new DataGridView
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
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None
        };
        // Row header styling – show only the selection arrow, no text
        owner._gridAnnouncements.RowHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 242, 245);
        owner._gridAnnouncements.RowHeadersDefaultCellStyle.ForeColor = Color.FromArgb(15, 37, 65);
        owner._gridAnnouncements.RowHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(15, 37, 65);
        owner._gridAnnouncements.RowHeadersDefaultCellStyle.SelectionForeColor = Color.White;


        owner._gridAnnouncements.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = "Checked", HeaderText = "Seç", Width = 42,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            ToolTipText = "Birden fazla ilanı birlikte açmak için işaretleyin"
        });
        owner._gridAnnouncements.Columns.Add("Status", "Durum");
        owner._gridAnnouncements.Columns.Add("Institution", "Kurum");
        owner._gridAnnouncements.Columns.Add("Title", "İlan Başlığı");
        owner._gridAnnouncements.Columns.Add("EndDate", "Son Başvuru");
        owner._gridAnnouncements.Columns.Add("ApplicationStatus", "Başvuru Takibi");
        owner._gridAnnouncements.Columns.Add("RemainingTime", "Kalan Süre");
        owner._gridAnnouncements.Columns.Add("PositionsCount", "Kadro");
        foreach (DataGridViewColumn column in owner._gridAnnouncements.Columns)
        {
            column.ReadOnly = column.Name != "Checked";
            column.SortMode = column.Name == "Checked" ? DataGridViewColumnSortMode.NotSortable : DataGridViewColumnSortMode.Programmatic;
        }
        owner._gridAnnouncements.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0) return;
            var column = owner._gridAnnouncements.Columns[e.ColumnIndex];
            if (column.Name == "Checked") { owner.SelectVisibleAnnouncements(true); return; }
            owner._announcementSortDescending = column.Name == owner._announcementSortColumn && !owner._announcementSortDescending;
            owner._announcementSortColumn = column.Name;
            owner.ApplyFilter();
            foreach (DataGridViewColumn c in owner._gridAnnouncements.Columns) c.HeaderCell.SortGlyphDirection = SortOrder.None;
            column.HeaderCell.SortGlyphDirection = owner._announcementSortDescending ? SortOrder.Descending : SortOrder.Ascending;
        };
        owner._gridAnnouncements.CurrentCellDirtyStateChanged += (s, e) =>
        {
            if (owner._gridAnnouncements.IsCurrentCellDirty && owner._gridAnnouncements.CurrentCell?.OwningColumn?.Name == "Checked")
                owner._gridAnnouncements.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        owner._gridAnnouncements.CellValueChanged += (s, e) =>
        {
            if (owner._isUpdatingAnnouncementRows || e.RowIndex < 0 || e.ColumnIndex != owner._gridAnnouncements.Columns["Checked"]!.Index)
                return;
            var row = owner._gridAnnouncements.Rows[e.RowIndex];
            if (row.Tag is not AnnouncementDisplayItem item) return;
            if (row.Cells["Checked"].Value is true) owner._checkedAnnouncementGuids.Add(item.Record.Guid);
            else owner._checkedAnnouncementGuids.Remove(item.Record.Guid);
            owner.UpdateCheckedAnnouncements();
        };

        foreach (var column in new[] { (Name: "Status", Weight: 16, Minimum: 80), (Name: "Institution", Weight: 24, Minimum: 100),
            (Name: "Title", Weight: 36, Minimum: 120), (Name: "EndDate", Weight: 20, Minimum: 95), (Name: "ApplicationStatus", Weight: 18, Minimum: 90) })
        {
            owner._gridAnnouncements.Columns[column.Name]!.MinimumWidth = column.Minimum;
            owner._gridAnnouncements.Columns[column.Name]!.FillWeight = column.Weight;
        }
        owner._gridAnnouncements.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        owner._gridAnnouncements.Columns["RemainingTime"]!.MinimumWidth = 80;
        owner._gridAnnouncements.Columns["RemainingTime"]!.FillWeight = 14;
        owner._gridAnnouncements.Columns["PositionsCount"]!.MinimumWidth = 55;
        owner._gridAnnouncements.Columns["PositionsCount"]!.FillWeight = 8;
        owner._gridAnnouncements.Columns["EndDate"]!.HeaderCell.SortGlyphDirection = SortOrder.Ascending;
        owner.SetupColumnMenu();
        // Initialize tooltip for rows
        owner._gridTooltip = new ToolTip { AutoPopDelay = 5000, InitialDelay = 500, ReshowDelay = 200, ShowAlways = true };
        // Show placeholder on hover
        owner._gridAnnouncements.CellToolTipTextNeeded += (s, e) => {
            if (e.RowIndex >= 0 && owner._gridAnnouncements.Rows[e.RowIndex].Tag is AnnouncementDisplayItem item)
                e.ToolTipText = item.Record.InstitutionName + "\n" + item.Record.Title;
        };
        // Change cursor to hand on hover
        owner._gridAnnouncements.CellMouseEnter += (s, e) => {
            if (e.RowIndex >= 0) owner._gridAnnouncements.Cursor = Cursors.Hand;
        };
        owner._gridAnnouncements.CellMouseLeave += (s, e) => {
            owner._gridAnnouncements.Cursor = Cursors.Default;
        };

        owner._gridAnnouncements.SelectionChanged += owner.GridAnnouncements_SelectionChanged;
        owner._gridAnnouncements.CellDoubleClick += owner.GridAnnouncements_CellDoubleClick;

        var bulkActions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, 5, 0, 0),
            WrapContents = true
        };
        owner._lblCheckedAnnouncements = new Label { Text = "İlanları kutucuklarla seçin", AutoSize = true, Margin = new Padding(3, 9, 12, 0) };
        owner._btnOpenSelectedAnnouncements = new Button
        {
            Text = "Seçili ilanlara git", AutoSize = true, Height = 32, Visible = false,
            BackColor = Color.FromArgb(15, 37, 65), ForeColor = Color.White, FlatStyle = FlatStyle.Flat
        };
        owner._btnOpenSelectedAnnouncements.Click += async (s, e) =>
        {
            var selected = owner._cachedItems.Where(item => owner._checkedAnnouncementGuids.Contains(item.Record.Guid))
                .Select(item => item.Record).ToList();
            foreach (var record in selected) await owner.OpenAnnouncementAsync(record);
        };
        bulkActions.Controls.Add(owner._lblCheckedAnnouncements);
        bulkActions.Controls.Add(owner._btnOpenSelectedAnnouncements);
        var selectAll = new Button { Text = "Görünenleri seç", AutoSize = true, Height = 32 };
        selectAll.Click += (_, _) => owner.SelectVisibleAnnouncements(true);
        var clearSelection = new Button { Text = "Seçimleri temizle", AutoSize = true, Height = 32 };
        clearSelection.Click += (_, _) => {
            owner._checkedAnnouncementGuids.Clear(); owner.SelectVisibleAnnouncements(false);
        };
        owner._btnExportAnnouncements = new Button { Text = "Seçilileri CSV'ye aktar", AutoSize = true, Height = 32, Enabled = false };
        owner._btnExportAnnouncements.Click += async (_, _) => await owner.ExportCheckedAnnouncementsAsync();
        bulkActions.Controls.Add(selectAll);
        bulkActions.Controls.Add(clearSelection);
        bulkActions.Controls.Add(owner._btnExportAnnouncements);
        leftPanel.Controls.Add(owner._gridAnnouncements);
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
        owner._lblDetailStatusBadge = new Label
        {
            Text = "BİLGİ",
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.FromArgb(50, 150, 250),
            AutoSize = true,
            Padding = new Padding(6, 3, 6, 3),
            Location = new Point(0, 0)
        };

        owner._lblDetailInstitution = new Label
        {
            Text = "Bir ilan seçiniz",
            Font = new Font("Segoe UI", 12, FontStyle.Bold),
            ForeColor = Color.FromArgb(15, 37, 65),
            Location = new Point(0, 28),
            AutoSize = true
        };

        owner._lblDetailTitle = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(70, 80, 95),
            Location = new Point(0, 55),
            Size = new Size(500, 30)
        };

        owner._lblDetailDates = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9, FontStyle.Italic),
            ForeColor = Color.FromArgb(100, 110, 120),
            Location = new Point(0, 88),
            AutoSize = true
        };

        var headerLabels = new[] { owner._lblDetailStatusBadge, owner._lblDetailInstitution, owner._lblDetailTitle, owner._lblDetailDates };
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
        owner._btnOpenKariyerKapisi = new Button
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
        owner._btnOpenKariyerKapisi.Click += async (s, e) =>
        {
            if (owner._selectedItem != null) await owner.OpenAnnouncementAsync(owner._selectedItem.Record);
        };

        owner._btnOpenEDevlet = new Button
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
        owner._btnOpenEDevlet.Click += (s, e) => owner.OpenUrl(owner._selectedItem?.Record.ApplicationUrl);

        owner._btnSendTelegramNow = new Button
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
        owner._btnSendTelegramNow.Click += async (s, e) => await owner.SendSelectedToTelegramAsync();

        owner._btnOpenKamuIlanSite = new Button
        {
            Text = "🌐 Kamu İlan Sitesi", Width = 160, Height = 38, Visible = false,
            BackColor = Color.FromArgb(15, 37, 65), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand
        };
        owner._btnOpenKamuIlanSite.Click += (_, _) => owner.OpenUrl(KariyerTakip.Services.KamuIlanClient.BaseUrl);
        pnlActions.Controls.Add(owner._btnOpenKariyerKapisi);
        pnlActions.Controls.Add(owner._btnOpenKamuIlanSite);
        pnlActions.Controls.Add(owner._btnOpenEDevlet);
        pnlActions.Controls.Add(owner._btnSendTelegramNow);
        var feedback = new Button { Text = "Bu değerlendirme yanlış", AutoSize = true, Height = 38 };
        feedback.Click += async (_, _) => await owner.SaveEvaluationFeedbackAsync();
        pnlActions.Controls.Add(feedback);

        owner._rtbDetailContent = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10.5f),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false
        };

        var detailTabs = new ThemeTabControl { Dock = DockStyle.Fill };
        var detailPage = new TabPage("Kadro ayrıntıları");
        detailPage.Controls.Add(owner._rtbDetailContent);
        var trackingPage = new TabPage("Başvuru takibi ve notlar");
        owner._applicationTracking = new ApplicationTrackingControl(owner._repository);
        owner._applicationTracking.TrackingSaved += () =>
        {
            owner.ApplyFilter();
        };
        trackingPage.Controls.Add(owner._applicationTracking);
        detailTabs.TabPages.AddRange(new[] { detailPage, trackingPage });
        rightPanel.Controls.Add(detailTabs);
        rightPanel.Controls.Add(pnlDetailHeader);
        rightPanel.Controls.Add(pnlActions);
        mainSplit.Panel2.Controls.Add(rightPanel);

        Controls.Add(mainSplit);

        }
    }
}
