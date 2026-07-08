// Tests for the risky-extension filter, the file-lock probe, and theme geometry.
using System;
using System.Drawing;
using System.IO;
using ClamAVUI;

namespace ClamAVUI.Tests
{
    static class RiskyExtensionsTests
    {
        public static void TestExecutablesAreRisky()
        {
            Assert.True(MainForm.RiskyExtensions.Contains(".exe"), ".exe");
            Assert.True(MainForm.RiskyExtensions.Contains(".dll"), ".dll");
            Assert.True(MainForm.RiskyExtensions.Contains(".msi"), ".msi");
        }

        public static void TestScriptsAndDocsAreRisky()
        {
            Assert.True(MainForm.RiskyExtensions.Contains(".js"), ".js");
            Assert.True(MainForm.RiskyExtensions.Contains(".ps1"), ".ps1");
            Assert.True(MainForm.RiskyExtensions.Contains(".docm"), ".docm");
            Assert.True(MainForm.RiskyExtensions.Contains(".pdf"), ".pdf");
            Assert.True(MainForm.RiskyExtensions.Contains(".zip"), ".zip");
        }

        public static void TestLookupIsCaseInsensitive()
        {
            Assert.True(MainForm.RiskyExtensions.Contains(".EXE"), "upper-case .EXE");
            Assert.True(MainForm.RiskyExtensions.Contains(".Zip"), "mixed-case .Zip");
        }

        public static void TestHarmlessTypesAreNotRisky()
        {
            Assert.False(MainForm.RiskyExtensions.Contains(".txt"), ".txt");
            Assert.False(MainForm.RiskyExtensions.Contains(".jpg"), ".jpg");
            Assert.False(MainForm.RiskyExtensions.Contains(".mp4"), ".mp4");
            Assert.False(MainForm.RiskyExtensions.Contains(""), "empty extension");
        }
    }

    static class FileLockTests
    {
        public static void TestClosedFileIsNotLocked()
        {
            using (var tmp = new TempDir())
            {
                File.WriteAllText(tmp.File("free.txt"), "x");
                Assert.False(MainForm.IsFileLocked(tmp.File("free.txt")), "closed file");
            }
        }

        public static void TestExclusivelyOpenedFileIsLocked()
        {
            using (var tmp = new TempDir())
            {
                string p = tmp.File("busy.txt");
                File.WriteAllText(p, "x");
                using (new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Assert.True(MainForm.IsFileLocked(p), "file opened with FileShare.None");
                Assert.False(MainForm.IsFileLocked(p), "unlocked again after the handle closes");
            }
        }
    }

    static class ThemeTests
    {
        public static void TestRoundProducesClosedPath()
        {
            using (var path = Theme.Round(new RectangleF(0, 0, 100, 40), 10))
                Assert.True(path.PointCount > 0, "rounded-rect path has geometry");
        }

        public static void TestPaintCardDrawsWithoutErrors()
        {
            // smoke test: the card painter must work on a plain in-memory bitmap
            using (var bmp = new Bitmap(200, 100))
            using (var g = Graphics.FromImage(bmp))
                Theme.PaintCard(g, bmp.Width, bmp.Height); // throws on any GDI+ error
        }
    }
}
