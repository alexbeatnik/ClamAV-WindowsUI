// Tests for the startup sweep of stale scan temp files (src\MainForm.Monitor.cs).
// A hard crash leaves clamui-list-* files and clamui-mem-* RAM-dump folders (up
// to 128 MB) in %TEMP%; the sweep must remove exactly those and nothing else.
using System;
using System.IO;
using ClamAVUI;

namespace ClamAVUI.Tests
{
    static class SweepTempTests
    {
        public static void TestSweepDeletesOwnLeftovers()
        {
            using (var dir = new TempDir())
            {
                File.WriteAllText(dir.File("clamui-list-abc123.txt"), "x");
                File.WriteAllText(dir.File("clamui-list-2-abc123.txt"), "x"); // chunked list
                string memDir = dir.File("clamui-mem-abc123");
                Directory.CreateDirectory(memDir);
                File.WriteAllText(Path.Combine(memDir, "proc_pid1_0x1000.bin"), "x");

                MainForm.SweepStaleTempFiles(dir.Path);

                Assert.False(File.Exists(dir.File("clamui-list-abc123.txt")), "list file swept");
                Assert.False(File.Exists(dir.File("clamui-list-2-abc123.txt")), "chunk list swept");
                Assert.False(Directory.Exists(memDir), "mem-dump folder swept with its contents");
            }
        }

        public static void TestSweepKeepsForeignFiles()
        {
            using (var dir = new TempDir())
            {
                File.WriteAllText(dir.File("report.txt"), "x");           // unrelated file
                File.WriteAllText(dir.File("clamui-listing.txt"), "x");   // similar name, no GUID dash
                File.WriteAllText(dir.File("clamui-memo.txt"), "x");      // not a dump folder
                Directory.CreateDirectory(dir.File("clamui-memories"));   // dir not matching clamui-mem-*

                MainForm.SweepStaleTempFiles(dir.Path);

                Assert.True(File.Exists(dir.File("report.txt")), "unrelated file kept");
                Assert.True(File.Exists(dir.File("clamui-listing.txt")), "near-miss file kept");
                Assert.True(File.Exists(dir.File("clamui-memo.txt")), "near-miss file kept (2)");
                Assert.True(Directory.Exists(dir.File("clamui-memories")), "near-miss folder kept");
            }
        }

        public static void TestSweepOnMissingDirIsANoOp()
        {
            MainForm.SweepStaleTempFiles(Path.Combine(Path.GetTempPath(),
                "clamui-tests-no-such-dir-" + Guid.NewGuid().ToString("N"))); // must not throw
        }
    }
}
