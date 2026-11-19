// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.IO;
using System.Text;

#if HOST_EXCEL
namespace MyanmarSpellChecker.ExcelAddIn
#elif HOST_POWERPOINT
namespace MyanmarSpellChecker.PowerPointAddIn
#else
namespace MyanmarSpellChecker.WordAddIn
#endif
{
    /// <summary>
    /// Global COM boundary exception shield and bounded offline diagnostic logger (&lt;= 256 KB).
    /// Ensures no unhandled managed exception ever escapes into native WINWORD.EXE, EXCEL.EXE, or POWERPNT.EXE.
    /// </summary>
    internal static class AddInDiagnostics
    {
        private const long MaxLogBytes = 256 * 1024;
        private static readonly object s_logLock = new object();
        private static string? s_logFilePath;

#if HOST_EXCEL
        private const string DefaultLogFileName = "excel_addin_diagnostics.log";
#elif HOST_POWERPOINT
        private const string DefaultLogFileName = "powerpoint_addin_diagnostics.log";
#else
        private const string DefaultLogFileName = "addin_diagnostics.log";
#endif

        public static void ConfigureLogDirectory(string localAppDataDir)
        {
            if (string.IsNullOrWhiteSpace(localAppDataDir))
                return;

            try
            {
                Directory.CreateDirectory(localAppDataDir);
                s_logFilePath = Path.Combine(localAppDataDir, DefaultLogFileName);
            }
            catch
            {
                // Ignore locked filesystem
            }
        }

        public static void SafeExecute(string context, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogException(context, ex);
            }
        }

        public static T SafeExecute<T>(string context, Func<T> func, T fallback)
        {
            try
            {
                return func();
            }
            catch (Exception ex)
            {
                LogException(context, ex);
                return fallback;
            }
        }

        public static void LogException(string context, Exception ex)
        {
            string? path = s_logFilePath;
            if (string.IsNullOrEmpty(path))
                return;

            try
            {
                lock (s_logLock)
                {
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxLogBytes)
                    {
                        string backup = path + ".old";
                        try
                        {
                            if (File.Exists(backup))
                                File.Delete(backup);
                            File.Move(path, backup);
                        }
                        catch
                        {
                            File.WriteAllText(path, string.Empty, Encoding.UTF8);
                        }
                    }

                    string entry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}";
                    File.AppendAllText(path, entry, Encoding.UTF8);
                }
            }
            catch
            {
                // Never throw from diagnostic logger
            }
        }
    }
}
