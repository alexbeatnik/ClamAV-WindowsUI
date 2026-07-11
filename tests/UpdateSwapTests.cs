// Tests for the loss-safe database file swap (src\MainForm.Updates.cs).
using System;
using System.IO;
using ClamAVUI;

namespace ClamAVUI.Tests
{
    // PromoteDownloadedFile must never leave a moment where neither the old
    // nor the new database exists at the destination path.
    static class PromoteDownloadedFileTests
    {
        public static void TestMovesWhenDestinationMissing()
        {
            using (var tmp = new TempDir())
            {
                string part = tmp.File("daily.cvd.part");
                string dest = tmp.File("daily.cvd");
                File.WriteAllText(part, "new database");
                MainForm.PromoteDownloadedFile(part, dest);
                Assert.Equal("new database", File.ReadAllText(dest), "downloaded content lands at dest");
                Assert.False(File.Exists(part), "the .part file is consumed");
            }
        }

        public static void TestReplacesExistingDestination()
        {
            using (var tmp = new TempDir())
            {
                string part = tmp.File("daily.cvd.part");
                string dest = tmp.File("daily.cvd");
                File.WriteAllText(dest, "old database");
                File.WriteAllText(part, "new database");
                MainForm.PromoteDownloadedFile(part, dest);
                Assert.Equal("new database", File.ReadAllText(dest), "existing dest is replaced with the new content");
                Assert.False(File.Exists(part), "the .part file is consumed");
            }
        }

        public static void TestFailedPromoteKeepsTheExistingDatabase()
        {
            using (var tmp = new TempDir())
            {
                string part = tmp.File("daily.cvd.part"); // never created
                string dest = tmp.File("daily.cvd");
                File.WriteAllText(dest, "old database");
                Assert.Throws<FileNotFoundException>(
                    delegate { MainForm.PromoteDownloadedFile(part, dest); },
                    "promoting a missing .part fails");
                Assert.Equal("old database", File.ReadAllText(dest), "the working database survives a failed swap");
            }
        }
    }
}
