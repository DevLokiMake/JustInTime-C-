using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace JustInTime.App.Services;

/// <summary>Значок JustInTime в области уведомлений Windows (NotifyIcon) с меню. Не знает ничего о логике приложения.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Drawing.Icon _image;

    public TrayIcon(Action open, Action createTask, Action imageToText, Action exit)
    {
        _image = DrawIcon();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть JustInTime", null, (_, _) => open());
        menu.Items.Add("Новая задача", null, (_, _) => createTask());
        menu.Items.Add("Фото в текст", null, (_, _) => imageToText());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => exit());

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "JustInTime: задачи выполняются",
            ContextMenuStrip = menu,
            Visible = true
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                open();
            }
        };
    }

    /// <summary>Подсказка рядом со значком, например при первом сворачивании.</summary>
    public void ShowBalloon(string title, string text) =>
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        // Без Visible = false значок остаётся в трее призраком, пока на него не наведут мышь.
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    /// <summary>Рисуем простые часы в цвете приложения: отдельного файла значка в проекте пока нет.</summary>
    private static Drawing.Icon DrawIcon()
    {
        using var bitmap = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Drawing.Color.Transparent);

            using var face = new Drawing.SolidBrush(Drawing.Color.FromArgb(37, 99, 235));
            g.FillEllipse(face, 1, 1, 30, 30);

            using var hands = new Drawing.Pen(Drawing.Color.White, 3f)
            {
                StartCap = Drawing.Drawing2D.LineCap.Round,
                EndCap = Drawing.Drawing2D.LineCap.Round
            };
            g.DrawLine(hands, 16, 16, 16, 7);
            g.DrawLine(hands, 16, 16, 23, 20);
        }

        var handle = bitmap.GetHicon();

        try
        {
            // Clone делает независимую копию: исходный дескриптор GDI можно сразу освободить.
            return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
