using System;
using System.IO;
using System.Text;

namespace MyanmarSpellChecker.WordAddIn
{
    /// <summary>
    /// Global COM boundary exception shield and bounded offline diagnostic logger (&lt;= 256 KB).
    /// Ensures no unhandled managed exception ever escapes into native WINWORD.EXE.
    /// </summary>
    internal static class AddInDiagnostics
    {
        private const long MaxLogBytes = 256 * 1024;
        private static readonly object s_logLock = new object();
        private static string? s_logFilePath;

        public static void ConfigureLogDirectory(string localAppDataDir)
        {
            if (string.IsNullOrWhiteSpace(localAppDataDir))
                return;

            try
            {
                Directory.CreateDirectory(localAppDataDir);
                s_logFilePath = Path.Combine(localAppDataDir, "addin_diagnostics.log");
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

