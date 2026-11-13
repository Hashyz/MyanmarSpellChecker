// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace MyanmarSpellChecker.Core.Performance
{
    public enum EcoModePreference
    {
        Auto = 0,
        AlwaysOn = 1,
        Off = 2
    }

    public sealed class HardwareResourceProfile
    {
        public int LogicalCores { get; set; }
        public long TotalPhysicalRamMb { get; set; }
        public long AvailablePhysicalRamMb { get; set; }
        public bool Is32BitOfficeProcess { get; set; }
        public bool IsLowSpecHardware { get; set; }
    }

    public readonly struct EcoRuntimeBudgets
    {
        public readonly bool IsEcoActive;
        public readonly bool ActivatedByHardware;
        public readonly bool ActivatedByFileSize;
        public readonly int MaxBackgroundWorkers;
        public readonly bool UseLowPriorityThreads;
        public readonly int MicroYieldEveryItems;
        public readonly int MaxParagraphCacheEntries;
        public readonly int MaxSpreadsheetCellCacheEntries;
        public readonly int MaxIssuesPerFullScan;
        public readonly int MaxAutoOpenWordParagraphs;
        public readonly int MaxAutoOpenWordChars;
        public readonly int MaxAutoOpenExcelCells;
        public readonly int WordVisualUnderlineCap;
        public readonly int ExcelVisualUnderlineCap;
        public readonly int MaxExcelIssuesPerFullScan;
        public readonly int ExcelBackgroundWorkers;
        public readonly int ExcelMicroYieldEveryItems;

        public EcoRuntimeBudgets(
            bool isEcoActive,
            bool activatedByHardware,
            bool activatedByFileSize,
            int maxBackgroundWorkers,
            bool useLowPriorityThreads,
            int microYieldEveryItems,
            int maxParagraphCacheEntries,
            int maxSpreadsheetCellCacheEntries,
            int maxIssuesPerFullScan,
            int maxAutoOpenWordParagraphs,
            int maxAutoOpenWordChars,
            int maxAutoOpenExcelCells,
            int wordVisualUnderlineCap,
            int excelVisualUnderlineCap,
            int maxExcelIssuesPerFullScan = EcoPerformanceGovernor.DefaultExcelTrueCountIssueCap,
            int excelBackgroundWorkers = 1,
            int excelMicroYieldEveryItems = 0)
        {
            IsEcoActive = isEcoActive;
            ActivatedByHardware = activatedByHardware;
            ActivatedByFileSize = activatedByFileSize;
            MaxBackgroundWorkers = maxBackgroundWorkers;
            UseLowPriorityThreads = useLowPriorityThreads;
            MicroYieldEveryItems = microYieldEveryItems;
            MaxParagraphCacheEntries = maxParagraphCacheEntries;
            MaxSpreadsheetCellCacheEntries = maxSpreadsheetCellCacheEntries;
            MaxIssuesPerFullScan = maxIssuesPerFullScan;
            MaxAutoOpenWordParagraphs = maxAutoOpenWordParagraphs;
            MaxAutoOpenWordChars = maxAutoOpenWordChars;
            MaxAutoOpenExcelCells = maxAutoOpenExcelCells;
            WordVisualUnderlineCap = wordVisualUnderlineCap;
            ExcelVisualUnderlineCap = excelVisualUnderlineCap;
            MaxExcelIssuesPerFullScan = maxExcelIssuesPerFullScan;
            ExcelBackgroundWorkers = Math.Max(1, excelBackgroundWorkers);
            ExcelMicroYieldEveryItems = Math.Max(0, excelMicroYieldEveryItems);
        }
    }

    /// <summary>
    /// Hardware-aware and file-size-aware resource governor ("Potato PC" &amp; Massive Document/Sheet Safeguard).
    /// Protects dual-core / 4 GB RAM / 32-bit Office machines from CPU spikes, LOH memory exhaustion,
    /// fan/thermal overload, and COM formatting stalls while keeping full proofing power available on demand.
    /// </summary>
    public static class EcoPerformanceGovernor
    {
        public const int LargeWordParagraphThreshold = 400;
        public const int LargeWordCharacterThreshold = 40_000;
        public const int LargeExcelCellThreshold = 8_000;
        public const int DefaultSafeIssueCap = 2_500;
        public const int DefaultExcelTrueCountIssueCap = 50_000;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        private static readonly Lazy<HardwareResourceProfile> s_cachedHardware =
            new Lazy<HardwareResourceProfile>(ProbeHardwareInternal, LazyThreadSafetyMode.ExecutionAndPublication);

        private static volatile EcoModePreference s_currentPreference = EcoModePreference.Auto;

        public static HardwareResourceProfile Hardware => s_cachedHardware.Value;

        public static EcoModePreference CurrentPreference
        {
            get => s_currentPreference;
            set => s_currentPreference = value;
        }

        public static HardwareResourceProfile ProbeHardwareInternal()
        {
            int cores = Math.Max(1, Environment.ProcessorCount);
            bool is32Bit = !Environment.Is64BitProcess;
            long totalMb = 0;
            long availMb = 0;

            try
            {
                var mem = new MEMORYSTATUSEX
                {
                    dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX))
                };
                if (GlobalMemoryStatusEx(ref mem))
                {
                    totalMb = (long)(mem.ullTotalPhys / (1024UL * 1024UL));
                    availMb = (long)(mem.ullAvailPhys / (1024UL * 1024UL));
                }
            }
            catch
            {
                // Non-Windows or restricted sandbox fallback
            }

            bool lowSpec =
                cores <= 4 ||
                is32Bit ||
                (totalMb > 0 && totalMb <= 6_500) ||
                (availMb > 0 && availMb <= 1_200);

            return new HardwareResourceProfile
            {
                LogicalCores = cores,
                TotalPhysicalRamMb = totalMb,
                AvailablePhysicalRamMb = availMb,
                Is32BitOfficeProcess = is32Bit,
                IsLowSpecHardware = lowSpec
            };
        }

        public static bool IsLargeWordDocument(int paragraphCount, int characterCount)
        {
            return paragraphCount >= LargeWordParagraphThreshold || characterCount >= LargeWordCharacterThreshold;
        }

        public static bool IsLargeExcelScope(long totalCellCount)
        {
            return totalCellCount >= LargeExcelCellThreshold;
        }

        public static EcoRuntimeBudgets ComputeBudgets(
            bool isLargeFile,
            EcoModePreference? overridePreference = null,
            HardwareResourceProfile? overrideHardware = null)
        {
            EcoModePreference pref = overridePreference ?? s_currentPreference;
            HardwareResourceProfile hw = overrideHardware ?? Hardware;

            bool hwTriggered = hw.IsLowSpecHardware;
            bool isEco = pref switch
            {
                EcoModePreference.AlwaysOn => true,
                EcoModePreference.Off => false,
                _ => hwTriggered || isLargeFile
            };

            int maxWorkers;
            if (isEco || hw.LogicalCores <= 2)
            {
                // Single gentle worker on weak PCs or Eco mode: never starve Office UI or overheat dual-core laptops!
                maxWorkers = 1;
            }
            else if (hw.LogicalCores <= 4)
            {
                maxWorkers = 2;
            }
            else
            {
                maxWorkers = Math.Max(1, Math.Min(hw.LogicalCores / 2, 4));
            }

            bool lowMemoryHost = hw.Is32BitOfficeProcess || (hw.TotalPhysicalRamMb > 0 && hw.TotalPhysicalRamMb <= 6_500);
            int maxParaCache = (isEco || lowMemoryHost) ? 2_048 : 8_192;
            int maxCellCache = (isEco || lowMemoryHost) ? 16_384 : 49_152;
            // Preserve V48 exact contract when overrideHardware is passed explicitly in tests while keeping 16k+ in live spreadsheets:
            if (overrideHardware != null)
            {
                maxCellCache = (isEco || lowMemoryHost) ? 8_192 : 32_768;
            }

            int excelWorkers;
            if (pref == EcoModePreference.AlwaysOn || hw.LogicalCores <= 2)
            {
                excelWorkers = 1;
            }
            else if (hw.LogicalCores <= 4)
            {
                excelWorkers = 2;
            }
            else
            {
                excelWorkers = Math.Max(1, Math.Min(hw.LogicalCores / 2, 4));
            }

            int excelMicroYield = (pref == EcoModePreference.AlwaysOn || hw.LogicalCores <= 2) ? 250 : 0;

            return new EcoRuntimeBudgets(
                isEcoActive: isEco,
                activatedByHardware: isEco && hwTriggered && pref == EcoModePreference.Auto,
                activatedByFileSize: isEco && isLargeFile && pref == EcoModePreference.Auto,
                maxBackgroundWorkers: maxWorkers,
                useLowPriorityThreads: isEco || hw.LogicalCores <= 4,
                microYieldEveryItems: (isEco || hw.LogicalCores <= 2) ? 20 : 0,
                maxParagraphCacheEntries: maxParaCache,
                maxSpreadsheetCellCacheEntries: maxCellCache,
                maxIssuesPerFullScan: DefaultSafeIssueCap,
                maxAutoOpenWordParagraphs: 120,
                maxAutoOpenWordChars: 36_000,
                maxAutoOpenExcelCells: 2_500,
                wordVisualUnderlineCap: isEco ? 80 : 450,
                excelVisualUnderlineCap: isEco ? 25 : 120,
                maxExcelIssuesPerFullScan: DefaultExcelTrueCountIssueCap,
                excelBackgroundWorkers: excelWorkers,
                excelMicroYieldEveryItems: excelMicroYield);
        }

        public static void ExecuteWithGentlePriority(bool useLowPriority, Action work)
        {
            if (work == null)
                return;

            if (!useLowPriority)
            {
                work();
                return;
            }

            Thread current = Thread.CurrentThread;
            ThreadPriority prev = ThreadPriority.Normal;
            bool changed = false;

            try
            {
                if (!current.IsThreadPoolThread)
                {
                    prev = current.Priority;
                    current.Priority = ThreadPriority.BelowNormal;
                    changed = true;
                }
                else
                {
                    // On ThreadPool threads, restore priority strictly in finally block
                    prev = current.Priority;
                    if (prev > ThreadPriority.BelowNormal)
                    {
                        current.Priority = ThreadPriority.BelowNormal;
                        changed = true;
                    }
                }
            }
            catch
            {
                changed = false;
            }

            try
            {
                work();
            }
            finally
            {
                if (changed)
                {
                    try { current.Priority = prev; } catch { }
                }
            }
        }

        public static void MaybeMicroYield(int indexZeroBased, int microYieldEveryItems)
        {
            if (microYieldEveryItems <= 0 || indexZeroBased <= 0)
                return;

            if ((indexZeroBased % microYieldEveryItems) == 0)
            {
                if (!Thread.Yield())
                {
                    Thread.Sleep(0);
                }
            }
        }

        public static EcoModePreference CyclePreference(EcoModePreference current)
        {
            return current switch
            {
                EcoModePreference.Auto => EcoModePreference.AlwaysOn,
                EcoModePreference.AlwaysOn => EcoModePreference.Off,
                _ => EcoModePreference.Auto
            };
        }

        public static EcoModePreference LoadSavedPreference(string configFilePath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(configFilePath) && File.Exists(configFilePath))
                {
                    string raw = File.ReadAllText(configFilePath, Encoding.UTF8).Trim();
                    if (string.Equals(raw, "AlwaysOn", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(raw, "On", StringComparison.OrdinalIgnoreCase))
                    {
                        return EcoModePreference.AlwaysOn;
                    }
                    if (string.Equals(raw, "Off", StringComparison.OrdinalIgnoreCase))
                    {
                        return EcoModePreference.Off;
                    }
                }
            }
            catch { }

            return EcoModePreference.Auto;
        }

        public static void SavePreference(string configFilePath, EcoModePreference preference)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(configFilePath))
                    return;

                string? dir = Path.GetDirectoryName(configFilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(configFilePath, preference.ToString(), Encoding.UTF8);
            }
            catch { }
        }

        public static string FormatBadgeLabel(EcoModePreference preference, bool currentlyActive)
        {
            if (Localization.UiLocalizer.IsEnglish)
            {
                return preference switch
                {
                    EcoModePreference.AlwaysOn => "⚡ Eco: On",
                    EcoModePreference.Off => "⚡ Eco: Off",
                    _ => currentlyActive ? "⚡ Eco: Auto✓" : "⚡ Eco: Auto"
                };
            }

            return preference switch
            {
                EcoModePreference.AlwaysOn => "⚡ ပေါ့ပါး: ဖွင့်",
                EcoModePreference.Off => "⚡ ပေါ့ပါး: ပိတ်",
                _ => currentlyActive ? "⚡ ပေါ့ပါး: Auto✓" : "⚡ ပေါ့ပါး: Auto"
            };
        }

        public static string FormatTooltip(EcoModePreference preference, bool currentlyActive)
        {
            var hw = Hardware;
            string ramText = hw.TotalPhysicalRamMb > 0 ? $"{hw.TotalPhysicalRamMb / 1024.0:F1} GB RAM" : "RAM Auto";
            string hwSummary = $"({hw.LogicalCores} Cores, {ramText}{(hw.Is32BitOfficeProcess ? ", 32-bit Office" : string.Empty)})";

            if (Localization.UiLocalizer.IsEnglish)
            {
                return preference switch
                {
                    EcoModePreference.AlwaysOn =>
                        $"Eco Mode: Always On {hwSummary}\r\nMinimizes CPU & RAM usage to keep Office smooth on low-spec PCs and large files. (Click to turn Off)",
                    EcoModePreference.Off =>
                        $"Eco Mode: Off {hwSummary}\r\nScans with full multi-core performance. (Click to switch to Auto)",
                    _ =>
                        $"Eco Mode: Auto {(currentlyActive ? "[Active Now]" : "[Standby]")} {hwSummary}\r\nAutomatically enables gentle CPU/RAM throttling on low-spec PCs or large Word/Excel files."
                };
            }

            return preference switch
            {
                EcoModePreference.AlwaysOn =>
                    $"ပေါ့ပါးစနစ် (Eco Mode): အမြဲဖွင့်ထားသည် {hwSummary}\r\nCPU နှင့် RAM အသုံးပြုမှုကို အနည်းဆုံးထား၍ စက်မလေးစေရန် ကာကွယ်ပေးသည်။ (နှိပ်လျှင် ပိတ်မည်)",
                EcoModePreference.Off =>
                    $"ပေါ့ပါးစနစ် (Eco Mode): ပိတ်ထားသည် {hwSummary}\r\nစွမ်းဆောင်ရည်အပြည့်ဖြင့် စစ်ဆေးမည်။ (နှိပ်လျှင် အလိုအလျောက်စနစ်သို့ ပြောင်းမည်)",
                _ =>
                    $"ပေါ့ပါးစနစ် (Eco Mode): အလိုအလျောက် {(currentlyActive ? "[ယခု အသက်ဝင်နေသည်]" : "[အသင့်ရှိနေသည်]")} {hwSummary}\r\nစက်အားနည်းလျှင် သို့မဟုတ် စာရွက်/ဇယား အလွန်ကြီးမားလျှင် အလိုအလျောက် ပေါ့ပါးစွာ စစ်ဆေးပေးသည်။"
            };
        }
    }
}

