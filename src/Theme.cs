// Dark theme palette, rounded-corner helpers, Win32 interop for the dark title bar.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClamAVUI
{
    static class NativeMethods
    {
        public const int HWND_BROADCAST = 0xffff;
        [DllImport("user32.dll")]
        public static extern int RegisterWindowMessage(string message);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }

    // Dark theme palette
    static class Theme
    {
        public static readonly Color Bg        = Color.FromArgb(23, 24, 28);    // window background
        public static readonly Color Card      = Color.FromArgb(35, 37, 43);    // cards
        public static readonly Color CardLine  = Color.FromArgb(52, 55, 63);    // thin card border
        public static readonly Color LogBg     = Color.FromArgb(16, 17, 20);    // log/list background
        public static readonly Color Text      = Color.FromArgb(230, 232, 236);
        public static readonly Color Muted     = Color.FromArgb(154, 161, 173);
        public static readonly Color Accent    = Color.FromArgb(59, 130, 246);  // blue
        public static readonly Color AccentHot = Color.FromArgb(106, 161, 248);
        public static readonly Color Good      = Color.FromArgb(35, 165, 90);   // green shield
        public static readonly Color Warn      = Color.FromArgb(232, 197, 71);  // yellow (values)
        public static readonly Color Danger    = Color.FromArgb(239, 68, 68);
        public static readonly Color DangerHot = Color.FromArgb(248, 113, 113);
        public static readonly Color Disabled  = Color.FromArgb(92, 97, 108); // clearly gray, not just faded
        public static readonly Color Btn       = Color.FromArgb(216, 219, 226); // light buttons
        public static readonly Color BtnHot    = Color.FromArgb(233, 235, 240);
        public static readonly Color BtnText   = Color.FromArgb(51, 54, 62);
        public const int Radius = 10; // card corner radius

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Dark window title bar (Win10 1903+); without it the frame stays white
        public static void DarkTitleBar(Form f)
        {
            EventHandler apply = delegate
            {
                try
                {
                    int on = 1;
                    if (DwmSetWindowAttribute(f.Handle, 20, ref on, 4) != 0)
                        DwmSetWindowAttribute(f.Handle, 19, ref on, 4); // older Win10 builds
                }
                catch { }
            };
            if (f.IsHandleCreated) apply(null, EventArgs.Empty);
            else f.HandleCreated += apply;
        }
    }
}
