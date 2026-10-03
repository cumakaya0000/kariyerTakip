using System.Drawing;
using Microsoft.Extensions.Logging;
using KariyerTakip.Models;

namespace KariyerTakip.Forms;

public partial class MainForm
{
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

}
