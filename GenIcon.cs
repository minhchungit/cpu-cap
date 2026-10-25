using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;

class GenIcon {
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr h);

    static void Main() {
        int S = 256;
        using (var b = new Bitmap(S, S))
        using (var g = Graphics.FromImage(b)) {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // nen xanh luc bo goc
            using (var br = new SolidBrush(Color.FromArgb(0, 100, 45))) {
                var rect = new Rectangle(0, 0, S, S);
                int r = S / 5;
                using (var path = Rounded(rect, r)) g.FillPath(br, path);
            }
            // canh quat: 3 canh ellipse xoay quanh tam
            using (var br = new SolidBrush(Color.White)) {
                for (int i = 0; i < 3; i++) {
                    var st = g.Save();
                    g.TranslateTransform(S / 2, S / 2);
                    g.RotateTransform(i * 120);
                    g.FillEllipse(br, -S / 16, -S / 2 + S / 14, S / 8, S / 3);
                    g.Restore(st);
                }
                g.FillEllipse(br, S / 2 - S / 12, S / 2 - S / 12, S / 6, S / 6);
            }
            using (var br = new SolidBrush(Color.FromArgb(0, 100, 45)))
                g.FillEllipse(br, S / 2 - S / 24, S / 2 - S / 24, S / 12, S / 12);
            IntPtr h = b.GetHicon();
            using (var ic = Icon.FromHandle(h))
            using (var fs = File.Create(AppDomain.CurrentDomain.BaseDirectory + "CpuCap.ico"))
                ic.Save(fs);
            DestroyIcon(h);
        }
        Console.WriteLine("icon written");
    }

    static GraphicsPath Rounded(Rectangle r, int rad) {
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
