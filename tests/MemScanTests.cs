// Tests for the process-memory-scan region predicate and dump-name sanitizer.
using System;
using System.IO;
using ClamAVUI;

namespace ClamAVUI.Tests
{
    static class MemScanTests
    {
        // Windows memory constants mirrored from MainForm.MemScan.
        const uint MEM_COMMIT = 0x1000, MEM_RESERVE = 0x2000;
        const uint MEM_PRIVATE = 0x20000, MEM_MAPPED = 0x40000, MEM_IMAGE = 0x1000000;
        const uint PAGE_NOACCESS = 0x01, PAGE_READONLY = 0x02, PAGE_READWRITE = 0x04;
        const uint PAGE_EXECUTE = 0x10, PAGE_EXECUTE_READ = 0x20, PAGE_EXECUTE_READWRITE = 0x40;
        const uint PAGE_GUARD = 0x100;
        const long Cap = 100L * 1024 * 1024;

        public static void TestPrivateExecutableRegionIsDumped()
        {
            Assert.True(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_EXECUTE_READWRITE, MEM_PRIVATE, 4096, Cap),
                "committed private RWX code (classic injected shellcode) must be scanned");
            Assert.True(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_EXECUTE_READ, MEM_MAPPED, 4096, Cap),
                "mapped executable (non-image) code must be scanned");
        }

        public static void TestImageRegionIsSkipped()
        {
            Assert.False(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_EXECUTE_READ, MEM_IMAGE, 4096, Cap),
                "disk-backed image code is scanned as a file, not dumped from RAM");
        }

        public static void TestNonExecutableRegionIsSkipped()
        {
            Assert.False(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_READWRITE, MEM_PRIVATE, 4096, Cap),
                "plain data pages are not code");
            Assert.False(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_READONLY, MEM_PRIVATE, 4096, Cap),
                "read-only data pages are not code");
        }

        public static void TestUncommittedRegionIsSkipped()
        {
            Assert.False(MainForm.ShouldDumpRegion(MEM_RESERVE, PAGE_EXECUTE_READWRITE, MEM_PRIVATE, 4096, Cap),
                "reserved-but-not-committed memory has nothing to read");
        }

        public static void TestGuardAndNoAccessSkipped()
        {
            Assert.False(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_EXECUTE_READ | PAGE_GUARD, MEM_PRIVATE, 4096, Cap),
                "guard pages fault on read");
            Assert.False(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_NOACCESS, MEM_PRIVATE, 4096, Cap),
                "no-access pages are unreadable");
        }

        public static void TestOversizedRegionSkipped()
        {
            Assert.False(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_EXECUTE_READWRITE, MEM_PRIVATE, Cap + 1, Cap),
                "a region larger than the cap is skipped so one process can't dump gigabytes");
            Assert.True(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_EXECUTE_READWRITE, MEM_PRIVATE, Cap, Cap),
                "exactly at the cap is still dumped");
            Assert.False(MainForm.ShouldDumpRegion(MEM_COMMIT, PAGE_EXECUTE_READWRITE, MEM_PRIVATE, 0, Cap),
                "an empty region is skipped");
        }

        public static void TestSanitizeNameStripsInvalidChars()
        {
            string clean = MainForm.SanitizeName("chrome");
            Assert.Equal("chrome", clean, "a plain name is unchanged");
            string bad = MainForm.SanitizeName("weird:na/me");
            foreach (char c in Path.GetInvalidFileNameChars())
                Assert.True(bad.IndexOf(c) < 0, "sanitized name keeps no invalid char");
            Assert.Equal("proc", MainForm.SanitizeName(""), "an empty process name falls back to a placeholder");
            Assert.Equal("proc", MainForm.SanitizeName(null), "a null process name falls back to a placeholder");
        }
    }
}
