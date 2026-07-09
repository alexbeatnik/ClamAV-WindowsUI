// Tests for the scan-performance mode mapping and USB drive-letter mask decoding.
using System;
using System.Diagnostics;
using ClamAVUI;

namespace ClamAVUI.Tests
{
    static class PerfModeTests
    {
        public static void TestMaxThreadsPerMode()
        {
            Assert.Equal(2, MainForm.PerfMaxThreads(0), "low mode threads");
            Assert.Equal(8, MainForm.PerfMaxThreads(1), "normal mode threads");
            Assert.Equal(16, MainForm.PerfMaxThreads(2), "high mode threads");
        }

        public static void TestMaxProcsPerMode()
        {
            Assert.Equal(1, MainForm.PerfMaxProcs(0), "low mode runs a single scanner");
            Assert.Equal(4, MainForm.PerfMaxProcs(1), "normal mode cap");
            Assert.Equal(8, MainForm.PerfMaxProcs(2), "high mode cap");
        }

        public static void TestPriorityPerMode()
        {
            Assert.Equal(ProcessPriorityClass.BelowNormal, MainForm.PerfPriority(0), "low priority");
            Assert.Equal(ProcessPriorityClass.Normal, MainForm.PerfPriority(1), "normal priority");
            Assert.Equal(ProcessPriorityClass.AboveNormal, MainForm.PerfPriority(2), "high priority");
        }
    }

    static class UsbMaskTests
    {
        public static void TestSingleBitMapsToLetter()
        {
            var roots = MainForm.DriveRootsFromMask(1 << 4); // bit 4 = E:
            Assert.Equal(1, roots.Count, "one drive in mask");
            Assert.Equal("E:\\", roots[0], "bit 4 is E:");
        }

        public static void TestMultipleBits()
        {
            var roots = MainForm.DriveRootsFromMask((1 << 0) | (1 << 25)); // A: + Z:
            Assert.Equal(2, roots.Count, "two drives in mask");
            Assert.Equal("A:\\", roots[0], "bit 0 is A:");
            Assert.Equal("Z:\\", roots[1], "bit 25 is Z:");
        }

        public static void TestEmptyMask()
        {
            Assert.Equal(0, MainForm.DriveRootsFromMask(0).Count, "empty mask yields no drives");
        }
    }
}
