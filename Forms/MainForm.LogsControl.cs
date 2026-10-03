using System.Drawing;
using System.Windows.Forms;
using KariyerTakip.Models;

namespace KariyerTakip.Forms;

public partial class MainForm
{
    private sealed class LogsControl : UserControl
    {
        public LogsControl(MainForm owner)
        {
            Dock = DockStyle.Fill;
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };

        var topBar = new Panel { Dock = DockStyle.Top, Height = 40 };
        var btnClearLogs = new Button { Text = "🗑️ Logları Temizle", Location = new Point(0, 5), Width = 130, Height = 30 };
        btnClearLogs.Click += (s, e) => owner._rtbLog.Clear();
        topBar.Controls.Add(btnClearLogs);

        owner._rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BackColor = Color.FromArgb(20, 24, 30),
            ForeColor = Color.FromArgb(220, 230, 240),
            Font = new Font("Consolas", 9.5f),
            BorderStyle = BorderStyle.None
        };

        panel.Controls.Add(owner._rtbLog);
        panel.Controls.Add(topBar);
        Controls.Add(panel);

        }
    }
}
