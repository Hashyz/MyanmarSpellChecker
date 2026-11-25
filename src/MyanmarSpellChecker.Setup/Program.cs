// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MyanmarSpellChecker.Setup
{
    internal static class Program
    {
        internal const string PayloadResourceName = "MyanmarSpellChecker.Payload.zip";
        internal const string UninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MyanmarSpellChecker";

        internal sealed class ComClassSpec
        {
            public string ProgId { get; set; } = string.Empty;
            public string Clsid { get; set; } = string.Empty;
            public string TypeName { get; set; } = string.Empty;
            public string AsmFullName { get; set; } = string.Empty;
            public string DllName { get; set; } = string.Empty;
            public bool IsControl { get; set; }
        }

        internal static readonly ComClassSpec[] ComClasses =
        {
            new ComClassSpec
            {
                ProgId = "MyanmarSpellChecker.WordAddIn",
                Clsid = "{8F5A9E42-6D11-4F3B-9C80-7B12E5D94A11}",
                TypeName = "MyanmarSpellChecker.WordAddIn.ThisAddIn",
                AsmFullName = "MyanmarSpellChecker.WordAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                DllName = "MyanmarSpellChecker.WordAddIn.dll",
                IsControl = false
            },
            new ComClassSpec
            {
                ProgId = "MyanmarSpellChecker.ProofingTaskPaneControl",
                Clsid = "{B3A2C741-9D18-4E2A-8C55-1F7B8A9C2D33}",
                TypeName = "MyanmarSpellChecker.WordAddIn.UI.ProofingTaskPaneControl",
                AsmFullName = "MyanmarSpellChecker.WordAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                DllName = "MyanmarSpellChecker.WordAddIn.dll",
                IsControl = true
            },
            new ComClassSpec
            {
                ProgId = "MyanmarSpellChecker.ExcelAddIn",
                Clsid = "{9A6B4D53-7E22-4C8A-AD91-8C23F6E05B22}",
                TypeName = "MyanmarSpellChecker.ExcelAddIn.ExcelThisAddIn",
                AsmFullName = "MyanmarSpellChecker.ExcelAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                DllName = "MyanmarSpellChecker.ExcelAddIn.dll",
                IsControl = false
            },
            new ComClassSpec
            {
                ProgId = "MyanmarSpellChecker.ExcelTaskPaneControl",
                Clsid = "{C4B3D852-AE29-4F3B-9D66-2E8C9B0D3E44}",
                TypeName = "MyanmarSpellChecker.ExcelAddIn.UI.ExcelProofingTaskPaneControl",
                AsmFullName = "MyanmarSpellChecker.ExcelAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                DllName = "MyanmarSpellChecker.ExcelAddIn.dll",
                IsControl = true
            },
            new ComClassSpec
            {
                ProgId = "MyanmarSpellChecker.PowerPointAddIn",
                Clsid = "{A7C5E964-8F33-4D9B-BE12-9D34A7F16C55}",
                TypeName = "MyanmarSpellChecker.PowerPointAddIn.PowerPointThisAddIn",
                AsmFullName = "MyanmarSpellChecker.PowerPointAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                DllName = "MyanmarSpellChecker.PowerPointAddIn.dll",
                IsControl = false
            },
            new ComClassSpec
            {
                ProgId = "MyanmarSpellChecker.PowerPointTaskPaneControl",
                Clsid = "{D5C4E963-BF3A-4A4C-AE77-3F9DA01E4F66}",
                TypeName = "MyanmarSpellChecker.PowerPointAddIn.UI.PowerPointProofingTaskPaneControl",
                AsmFullName = "MyanmarSpellChecker.PowerPointAddIn, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                DllName = "MyanmarSpellChecker.PowerPointAddIn.dll",
                IsControl = true
            }
        };

        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            SetupInstallerEngine.CleanupOldSelfBackups();

            bool validateOnly = HasFlag(args, "--validate", "/validate");
            bool silent = HasFlag(args, "--silent", "/S", "/silent");
            bool uninstall = HasFlag(args, "--uninstall", "/uninstall");
            bool repair = HasFlag(args, "--repair", "/repair");
            string? stagedDir = GetOptionValue(args, "--apply-staged");

            if (validateOnly)
            {
                return RunValidateCli();
            }

            if (!string.IsNullOrWhiteSpace(stagedDir))
            {
                using (var waitForm = new DeferredStagedApplyForm(stagedDir!))
                {
                    Application.Run(waitForm);
                    return waitForm.ExitCode;
                }
            }

            if (uninstall)
            {
                try
                {
                    if (!silent && !PromptCloseOfficeIfRunning(isEnglish: true))
                        return 1;
                    SetupInstallerEngine.ExecuteUninstall();
                    if (!silent)
                    {
                        MessageBox.Show(
                            "✓ Myanmar Official Spell Checker was uninstalled cleanly.\r\nYour Custom Words and Bookmarks were safely preserved.\r\n\r\n(မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း Add-in ကို အောင်မြင်စွာ ဖြုတ်သိမ်းပြီးပါပြီ။)",
                            "Myanmar Official Spell Checker — Uninstall",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return 0;
                }
                catch (Exception ex)
                {
                    if (!silent)
                        MessageBox.Show(ex.Message, "Uninstall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }

            if (repair)
            {
                try
                {
                    SetupInstallerEngine.RegisterComAndOfficeKeys(SetupInstallerEngine.GetAppInstallDir());
                    return 0;
                }
                catch
                {
                    return 1;
                }
            }

            if (silent)
            {
                try
                {
                    SetupInstallerEngine.ExecuteInstallOrUpdate(sourceOverrideDir: null, statusCallback: null);
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("SETUP_ERROR: " + ex.Message);
                    return 1;
                }
            }

            using (var wizard = new SetupWizardForm())
            {
                Application.Run(wizard);
                return wizard.ExitCode;
            }
        }

        private static int RunValidateCli()
        {
            try
            {
                string ver = SetupInstallerEngine.GetSetupVersion();
                using (Stream? res = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName))
                {
                    if (res != null && res.Length > 5_000_000)
                    {
                        Console.WriteLine($"SETUP_VALIDATE_OK|Mode=EmbeddedSingleFile|Version={ver}|PayloadBytes={res.Length}");
                        return 0;
                    }
                }

                if (SetupInstallerEngine.TryLocateExternalSourceFiles(null, out string core, out _, out _, out _, out string db, out _))
                {
                    long dbBytes = new FileInfo(db).Length;
                    Console.WriteLine($"SETUP_VALIDATE_OK|Mode=ExternalFallback|Version={ver}|Core={core}|DbBytes={dbBytes}");
                    return 0;
                }

                Console.Error.WriteLine("SETUP_VALIDATE_FAIL|Missing embedded payload and external fallback files.");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SETUP_VALIDATE_FAIL|" + ex.Message);
                return 1;
            }
        }

        internal static bool HasRunningOfficeProcesses(out string runningNames)
        {
            var active = new List<string>(3);
            if (Process.GetProcessesByName("WINWORD").Length > 0) active.Add("Word");
            if (Process.GetProcessesByName("EXCEL").Length > 0) active.Add("Excel");
            if (Process.GetProcessesByName("POWERPNT").Length > 0) active.Add("PowerPoint");
            runningNames = string.Join(", ", active);
            return active.Count > 0;
        }

        internal static bool PromptCloseOfficeIfRunning(bool isEnglish)
        {
            while (HasRunningOfficeProcesses(out string runningNames))
            {
                string msg = isEnglish
                    ? $"Microsoft {runningNames} is currently open.\r\n\r\nPlease save your documents, close Word, Excel, and PowerPoint, and click 'Retry' to continue safely.\r\n\r\n(ကျေးဇူးပြု၍ လက်ရှိဖိုင်များကို Save လုပ်၍ Word၊ Excel နှင့် PowerPoint ကို ခေတ္တပိတ်ပြီး 'Retry' ကို နှိပ်ပါ။)"
                    : $"လက်ရှိတွင် Microsoft {runningNames} ပွင့်နေပါသည်။\r\n\r\nဖိုင်များ မပျက်စီးစေရန် Save လုပ်၍ Word၊ Excel နှင့် PowerPoint တို့ကို ခေတ္တပိတ်ပြီး 'Retry' ကို နှိပ်ပါ။";
                var choice = MessageBox.Show(
                    msg,
                    "Myanmar Official Spell Checker — Setup",
                    MessageBoxButtons.RetryCancel,
                    MessageBoxIcon.Warning);
                if (choice != DialogResult.Retry)
                    return false;
            }
            return true;
        }

        private static bool HasFlag(string[] args, params string[] flags)
        {
            for (int i = 0; i < args.Length; i++)
            {
                for (int f = 0; f < flags.Length; f++)
                {
                    if (string.Equals(args[i], flags[f], StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            return false;
        }

        private static string? GetOptionValue(string[] args, string optionName)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return null;
        }
    }

    #region Online GitHub Release & Offline File Updater (Zero-Telemetry Manual Trigger Only)

    [DataContract]
    internal sealed class SetupUpdateManifestDto
    {
        [DataMember(Name = "version", Order = 1)] public string Version { get; set; } = string.Empty;
        [DataMember(Name = "tag_name", Order = 2)] public string TagName { get; set; } = string.Empty;
        [DataMember(Name = "published_utc", Order = 3)] public string PublishedUtc { get; set; } = string.Empty;
        [DataMember(Name = "setup_exe_file", Order = 4)] public string SetupExeFileName { get; set; } = "MyanmarSpellChecker-Setup.exe";
        [DataMember(Name = "setup_exe_sha256", Order = 5)] public string SetupExeSha256 { get; set; } = string.Empty;
        [DataMember(Name = "setup_exe_bytes", Order = 6)] public long SetupExeBytes { get; set; }
        [DataMember(Name = "setup_exe_url", Order = 7)] public string SetupExeDownloadUrl { get; set; } = string.Empty;
        [DataMember(Name = "easysetup_zip_file", Order = 8)] public string EasySetupZipFileName { get; set; } = "MyanmarSpellChecker-EasySetup-OfficeSuite.zip";
        [DataMember(Name = "easysetup_zip_sha256", Order = 9)] public string EasySetupZipSha256 { get; set; } = string.Empty;
        [DataMember(Name = "easysetup_zip_bytes", Order = 10)] public long EasySetupZipBytes { get; set; }
        [DataMember(Name = "easysetup_zip_url", Order = 11)] public string EasySetupZipDownloadUrl { get; set; } = string.Empty;
        [DataMember(Name = "database_file", Order = 12)] public string DatabaseFileName { get; set; } = "mlc_thatpon.db";
        [DataMember(Name = "database_sha256", Order = 13)] public string DatabaseSha256 { get; set; } = string.Empty;
        [DataMember(Name = "database_bytes", Order = 14)] public long DatabaseBytes { get; set; }
        [DataMember(Name = "database_url", Order = 15)] public string DatabaseDownloadUrl { get; set; } = string.Empty;
        [DataMember(Name = "core_dll_sha256", Order = 16)] public string CoreDllSha256 { get; set; } = string.Empty;
        [DataMember(Name = "word_dll_sha256", Order = 17)] public string WordDllSha256 { get; set; } = string.Empty;
        [DataMember(Name = "excel_dll_sha256", Order = 18)] public string ExcelDllSha256 { get; set; } = string.Empty;
        [DataMember(Name = "powerpoint_dll_sha256", Order = 19)] public string PowerPointDllSha256 { get; set; } = string.Empty;
        [DataMember(Name = "release_notes_en", Order = 20)] public string ReleaseNotesEn { get; set; } = string.Empty;
        [DataMember(Name = "release_notes_mm", Order = 21)] public string ReleaseNotesMm { get; set; } = string.Empty;
        [DataMember(Name = "rsa_signature_base64", Order = 22, EmitDefaultValue = false)] public string? RsaSignatureBase64 { get; set; }
    }

    [DataContract]
    internal sealed class SetupGitHubAssetDto
    {
        [DataMember(Name = "name")] public string? Name { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
        [DataMember(Name = "browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }

    [DataContract]
    internal sealed class SetupGitHubReleaseDto
    {
        [DataMember(Name = "tag_name")] public string? TagName { get; set; }
        [DataMember(Name = "name")] public string? Name { get; set; }
        [DataMember(Name = "published_at")] public string? PublishedAt { get; set; }
        [DataMember(Name = "body")] public string? Body { get; set; }
        [DataMember(Name = "html_url")] public string? HtmlUrl { get; set; }
        [DataMember(Name = "assets")] public List<SetupGitHubAssetDto>? Assets { get; set; }
    }

    internal sealed class SetupGitHubCheckResult
    {
        public bool Success { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public bool IsDictionaryOnlyUpdate { get; set; }
        public bool SignatureVerified { get; set; }
        public string CurrentVersion { get; set; } = string.Empty;
        public string LatestVersion { get; set; } = string.Empty;
        public string ReleasePageUrl { get; set; } = SetupOnlineUpdater.ReleasesWebUrl;
        public SetupUpdateManifestDto? Manifest { get; set; }
        public string ErrorMessageEn { get; set; } = string.Empty;
        public string ErrorMessageMm { get; set; } = string.Empty;
    }

    internal static class SetupOnlineUpdater
    {
        public const string ReleasesWebUrl = "https://github.com/Hashyz/MyanmarSpellChecker/releases";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/Hashyz/MyanmarSpellChecker/releases/latest";

        private const string DefaultEmbeddedPublicKeyXml =
            "<RSAKeyValue><Modulus>7r2b9bsBP1DbjeffMCmqwWZH2JH7R0VvL0InPZr400EfbKrewBzRmQid4Y37HVYBwipyyhqxE6qQ9D3e28tM0oFHeMat/Ou5vhdpYGbvXhgZSsW6oNMwCVpoYFIyv7v68ULbZ9xACwnqWbrvXhTakIO1b5CKNwTlKH9kCglxheRPhrjEZIyL26AGIb0gKRE/6hJUEiRyc9Hq6N5n+Cw0Nsn/ebFfz/+5QLsuzbwoP5Uj0743ppVWvefrsk0rf91tWF5HYimqKUsiVgX0rcmNq+fMakssEFn6BiDEmUR641WyWpDcG0KzXMJOyTJueyZ/kUBkLtBwbFAl3fHa/GGHkQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        public static string NormalizeVersionString(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "1.0.0.0";

            string s = raw!.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(1).Trim();

            int dashIdx = s.IndexOf('-');
            if (dashIdx > 0)
                s = s.Substring(0, dashIdx);

            string[] parts = s.Split('.');
            int[] nums = new int[4];
            for (int i = 0; i < 4 && i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out nums[i]) || nums[i] < 0)
                    nums[i] = 0;
            }
            return string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}.{3}", nums[0], nums[1], nums[2], nums[3]);
        }

        public static bool IsNewerVersion(string? candidateVersion, string? currentVersion)
        {
            if (!Version.TryParse(NormalizeVersionString(candidateVersion), out Version? cand) || cand == null)
                return false;
            if (!Version.TryParse(NormalizeVersionString(currentVersion), out Version? cur) || cur == null)
                return true;
            return cand > cur;
        }

        public static string ComputeFileSha256(string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        public static bool VerifyManifestRsaSignature(SetupUpdateManifestDto? m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.RsaSignatureBase64))
                return false;

            try
            {
                string canonical = string.Join("|",
                    NormalizeVersionString(m.Version),
                    m.TagName ?? string.Empty,
                    (m.SetupExeSha256 ?? string.Empty).ToLowerInvariant(),
                    m.SetupExeBytes.ToString(CultureInfo.InvariantCulture),
                    (m.EasySetupZipSha256 ?? string.Empty).ToLowerInvariant(),
                    m.EasySetupZipBytes.ToString(CultureInfo.InvariantCulture),
                    (m.DatabaseSha256 ?? string.Empty).ToLowerInvariant(),
                    m.DatabaseBytes.ToString(CultureInfo.InvariantCulture),
                    (m.CoreDllSha256 ?? string.Empty).ToLowerInvariant(),
                    (m.WordDllSha256 ?? string.Empty).ToLowerInvariant(),
                    (m.ExcelDllSha256 ?? string.Empty).ToLowerInvariant(),
                    (m.PowerPointDllSha256 ?? string.Empty).ToLowerInvariant());

                byte[] data = Encoding.UTF8.GetBytes(canonical);
                byte[] sig = Convert.FromBase64String(m.RsaSignatureBase64!);

                string pubXml = DefaultEmbeddedPublicKeyXml;
                foreach (var folder in new[] { Environment.SpecialFolder.CommonApplicationData, Environment.SpecialFolder.LocalApplicationData })
                {
                    string cand = Path.Combine(Environment.GetFolderPath(folder), "MyanmarSpellChecker", "vendor_public_key.xml");
                    if (File.Exists(cand))
                    {
                        string xml = File.ReadAllText(cand, Encoding.UTF8).Trim();
                        if (xml.IndexOf("<RSAKeyValue>", StringComparison.Ordinal) >= 0)
                        {
                            pubXml = xml;
                            break;
                        }
                    }
                }

                using (var rsa = new RSACryptoServiceProvider(2048))
                {
                    rsa.PersistKeyInCsp = false;
                    rsa.FromXmlString(pubXml);
                    return rsa.VerifyData(data, CryptoConfig.MapNameToOID("SHA256")!, sig);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsDictionaryOnlyUpdate(SetupUpdateManifestDto? m)
        {
            if (m == null || string.IsNullOrWhiteSpace(m.DatabaseSha256) || string.IsNullOrWhiteSpace(m.DatabaseDownloadUrl))
                return false;
            if (string.IsNullOrWhiteSpace(m.CoreDllSha256) ||
                string.IsNullOrWhiteSpace(m.WordDllSha256) ||
                string.IsNullOrWhiteSpace(m.ExcelDllSha256) ||
                string.IsNullOrWhiteSpace(m.PowerPointDllSha256))
            {
                return false;
            }

            string appDir = SetupInstallerEngine.GetAppInstallDir();
            string coreDll = Path.Combine(appDir, "MyanmarSpellChecker.Core.dll");
            string wordDll = Path.Combine(appDir, "MyanmarSpellChecker.WordAddIn.dll");
            string excelDll = Path.Combine(appDir, "MyanmarSpellChecker.ExcelAddIn.dll");
            string pptDll = Path.Combine(appDir, "MyanmarSpellChecker.PowerPointAddIn.dll");
            string dbFile = Path.Combine(appDir, "mlc_thatpon.db");

            if (!File.Exists(coreDll) || !File.Exists(wordDll) || !File.Exists(excelDll) || !File.Exists(pptDll))
                return false;

            try
            {
                bool dllsSame =
                    string.Equals(ComputeFileSha256(coreDll), m.CoreDllSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ComputeFileSha256(wordDll), m.WordDllSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ComputeFileSha256(excelDll), m.ExcelDllSha256, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(ComputeFileSha256(pptDll), m.PowerPointDllSha256, StringComparison.OrdinalIgnoreCase);
                if (!dllsSame)
                    return false;
                if (!File.Exists(dbFile))
                    return true;
                return !string.Equals(ComputeFileSha256(dbFile), m.DatabaseSha256, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static async Task<SetupGitHubCheckResult> CheckGitHubReleaseAsync(CancellationToken cancellationToken = default)
        {
            string baselineVer = SetupInstallerEngine.GetCurrentlyInstalledVersion() ?? SetupInstallerEngine.GetSetupVersion();
            string currentVer = NormalizeVersionString(baselineVer);

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var client = CreateHttpClient(TimeSpan.FromSeconds(15), currentVer))
                using (var resp = await client.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false))
                {
                    if (resp.StatusCode == HttpStatusCode.NotFound)
                    {
                        return new SetupGitHubCheckResult
                        {
                            Success = true,
                            IsUpdateAvailable = false,
                            CurrentVersion = currentVer,
                            LatestVersion = currentVer
                        };
                    }

                    resp.EnsureSuccessStatusCode();
                    string releaseJson = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var relSer = new DataContractJsonSerializer(typeof(SetupGitHubReleaseDto));
                    SetupGitHubReleaseDto? relDto;
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(releaseJson)))
                    {
                        relDto = relSer.ReadObject(ms) as SetupGitHubReleaseDto;
                    }

                    if (relDto == null)
                    {
                        return new SetupGitHubCheckResult
                        {
                            Success = false,
                            CurrentVersion = currentVer,
                            ErrorMessageEn = "GitHub release metadata could not be parsed.",
                            ErrorMessageMm = "GitHub မှ ဗားရှင်းအချက်အလက်ကို ဖတ်ရှု၍မရပါ။"
                        };
                    }

                    SetupUpdateManifestDto? manifest = null;
                    var assets = relDto.Assets ?? new List<SetupGitHubAssetDto>();
                    var manifestAsset = assets.FirstOrDefault(a =>
                        string.Equals(a.Name, "update-manifest.json", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl));

                    if (manifestAsset != null)
                    {
                        string manifestJson = await client.GetStringAsync(manifestAsset.BrowserDownloadUrl!).ConfigureAwait(false);
                        var manSer = new DataContractJsonSerializer(typeof(SetupUpdateManifestDto));
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson)))
                        {
                            manifest = manSer.ReadObject(ms) as SetupUpdateManifestDto;
                        }
                    }

                    if (manifest == null)
                    {
                        string tagVer = NormalizeVersionString(relDto.TagName ?? relDto.Name ?? currentVer);
                        var setupAsset = assets.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
                        var zipAsset = assets.FirstOrDefault(a => a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
                        var dbAsset = assets.FirstOrDefault(a => string.Equals(a.Name, "mlc_thatpon.db", StringComparison.OrdinalIgnoreCase));

                        manifest = new SetupUpdateManifestDto
                        {
                            Version = tagVer,
                            TagName = relDto.TagName ?? ("v" + tagVer),
                            PublishedUtc = relDto.PublishedAt ?? string.Empty,
                            SetupExeFileName = setupAsset?.Name ?? "MyanmarSpellChecker-Setup.exe",
                            SetupExeBytes = setupAsset?.Size ?? 0,
                            SetupExeDownloadUrl = setupAsset?.BrowserDownloadUrl ?? string.Empty,
                            EasySetupZipFileName = zipAsset?.Name ?? "MyanmarSpellChecker-EasySetup-OfficeSuite.zip",
                            EasySetupZipBytes = zipAsset?.Size ?? 0,
                            EasySetupZipDownloadUrl = zipAsset?.BrowserDownloadUrl ?? string.Empty,
                            DatabaseFileName = dbAsset?.Name ?? "mlc_thatpon.db",
                            DatabaseBytes = dbAsset?.Size ?? 0,
                            DatabaseDownloadUrl = dbAsset?.BrowserDownloadUrl ?? string.Empty
                        };
                    }

                    string latestVer = NormalizeVersionString(manifest.Version);
                    bool newer = IsNewerVersion(latestVer, currentVer);
                    bool dictOnly = newer && IsDictionaryOnlyUpdate(manifest);
                    bool sigOk = VerifyManifestRsaSignature(manifest);

                    return new SetupGitHubCheckResult
                    {
                        Success = true,
                        IsUpdateAvailable = newer,
                        IsDictionaryOnlyUpdate = dictOnly,
                        SignatureVerified = sigOk,
                        CurrentVersion = currentVer,
                        LatestVersion = latestVer,
                        ReleasePageUrl = !string.IsNullOrWhiteSpace(relDto.HtmlUrl) ? relDto.HtmlUrl! : ReleasesWebUrl,
                        Manifest = manifest
                    };
                }
            }
            catch (Exception ex)
            {
                return new SetupGitHubCheckResult
                {
                    Success = false,
                    CurrentVersion = currentVer,
                    ErrorMessageEn = "Could not reach GitHub Releases (" + ex.Message + "). Use '+ From File...' if offline.",
                    ErrorMessageMm = "GitHub သို့ ချိတ်ဆက်၍မရပါ (" + ex.Message + ")။ အင်တာနက်မရှိပါက '+ ဖိုင်မှတင်မည်...' ကို အသုံးပြုပါ။"
                };
            }
        }

        public static async Task DownloadVerifiedAssetAsync(
            string downloadUrl,
            string destinationFilePath,
            string? expectedSha256,
            Action<long, long>? progressCallback,
            CancellationToken cancellationToken = default)
        {
            string? dir = Path.GetDirectoryName(destinationFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            string tempPart = destinationFilePath + ".part";
            if (File.Exists(tempPart))
            {
                try { File.Delete(tempPart); } catch { }
            }

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var client = CreateHttpClient(TimeSpan.FromMinutes(6), SetupInstallerEngine.GetSetupVersion()))
            using (var resp = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long totalBytes = resp.Content.Headers.ContentLength ?? -1L;

                using (var netStream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var outStream = new FileStream(tempPart, FileMode.Create, FileAccess.Write, FileShare.None, 65536, useAsync: true))
                {
                    byte[] buffer = new byte[65536];
                    long downloaded = 0;
                    int read;
                    while ((read = await netStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        await outStream.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                        downloaded += read;
                        progressCallback?.Invoke(downloaded, totalBytes);
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                string actualSha = ComputeFileSha256(tempPart);
                if (!string.Equals(actualSha, expectedSha256!.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tempPart); } catch { }
                    throw new CryptographicException($"SHA-256 mismatch! Expected {expectedSha256}, got {actualSha}. Update aborted for safety.");
                }
            }

            if (File.Exists(destinationFilePath))
                File.Delete(destinationFilePath);
            File.Move(tempPart, destinationFilePath);
        }

        private static HttpClient CreateHttpClient(TimeSpan timeout, string version)
        {
            var client = new HttpClient { Timeout = timeout };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyanmarSpellChecker-Setup", NormalizeVersionString(version)));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }
    }

    #endregion

    internal static class SetupInstallerEngine
    {
        public static string GetLocalRootDir() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyanmarSpellChecker");

        public static string GetAppInstallDir() =>
            Path.Combine(GetLocalRootDir(), "app");

        public static void CleanupOldSelfBackups()
        {
            try
            {
                string root = GetLocalRootDir();
                if (!Directory.Exists(root))
                    return;
                foreach (string oldFile in Directory.GetFiles(root, "MyanmarSpellChecker-Setup.exe.old_*"))
                {
                    try { File.Delete(oldFile); } catch { }
                }
            }
            catch { }
        }

        public static void SafeReplacePersistedSetupExe(string sourceSetupExePath)
        {
            if (string.IsNullOrWhiteSpace(sourceSetupExePath) || !File.Exists(sourceSetupExePath))
                return;

            string localRoot = GetLocalRootDir();
            Directory.CreateDirectory(localRoot);
            string persistedSetupExe = Path.Combine(localRoot, "MyanmarSpellChecker-Setup.exe");

            if (string.Equals(Path.GetFullPath(sourceSetupExePath), Path.GetFullPath(persistedSetupExe), StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                File.Copy(sourceSetupExePath, persistedSetupExe, overwrite: true);
            }
            catch
            {
                // If persistedSetupExe is currently running (e.g. launched via "မိုး" Desktop shortcut),
                // Windows NTFS allows renaming the running image before writing the new Setup.exe in its place!
                try
                {
                    string backupPath = persistedSetupExe + ".old_" + Environment.TickCount.ToString(CultureInfo.InvariantCulture);
                    if (File.Exists(persistedSetupExe))
                        File.Move(persistedSetupExe, backupPath);
                    File.Copy(sourceSetupExePath, persistedSetupExe, overwrite: true);
                }
                catch { }
            }
        }

        public static string GetSetupVersion()
        {
            try
            {
                string loc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(loc) && File.Exists(loc))
                {
                    string? fv = FileVersionInfo.GetVersionInfo(loc).FileVersion;
                    if (!string.IsNullOrWhiteSpace(fv))
                        return fv!;
                }
            }
            catch { }

            return Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version ?? "1.0.87.0";
        }

        public static string? GetCurrentlyInstalledVersion()
        {
            string coreDll = Path.Combine(GetAppInstallDir(), "MyanmarSpellChecker.Core.dll");
            if (!File.Exists(coreDll))
                return null;
            try
            {
                return FileVersionInfo.GetVersionInfo(coreDll).FileVersion;
            }
            catch
            {
                return null;
            }
        }

        public static bool HasEmbeddedPayload(out long payloadBytes)
        {
            payloadBytes = 0;
            using (Stream? s = Assembly.GetExecutingAssembly().GetManifestResourceStream(Program.PayloadResourceName))
            {
                if (s != null && s.Length > 5_000_000)
                {
                    payloadBytes = s.Length;
                    return true;
                }
            }
            return false;
        }

        public static bool TryLocateExternalSourceFiles(
            string? sourceOverrideDir,
            out string coreDll,
            out string wordDll,
            out string excelDll,
            out string pptDll,
            out string dbFile,
            out string? cerFile)
        {
            coreDll = wordDll = excelDll = pptDll = dbFile = string.Empty;
            cerFile = null;

            string baseDir = !string.IsNullOrWhiteSpace(sourceOverrideDir) && Directory.Exists(sourceOverrideDir)
                ? Path.GetFullPath(sourceOverrideDir!)
                : Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppDomain.CurrentDomain.BaseDirectory;

            var searchDirs = new List<string> { baseDir };
            string? cur = baseDir;
            for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(cur); depth++)
            {
                searchDirs.Add(Path.Combine(cur, "artifacts", "release-package"));
                searchDirs.Add(Path.Combine(cur, "installer", "prebuilt"));
                searchDirs.Add(Path.Combine(cur, "prebuilt"));
                cur = Path.GetDirectoryName(cur);
            }

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;
                string c = Path.Combine(dir, "MyanmarSpellChecker.Core.dll");
                string w = Path.Combine(dir, "MyanmarSpellChecker.WordAddIn.dll");
                string e = Path.Combine(dir, "MyanmarSpellChecker.ExcelAddIn.dll");
                string p = Path.Combine(dir, "MyanmarSpellChecker.PowerPointAddIn.dll");
                if (File.Exists(c) && File.Exists(w) && File.Exists(e))
                {
                    coreDll = c;
                    wordDll = w;
                    excelDll = e;
                    pptDll = File.Exists(p) ? p : string.Empty;
                    string cerCand = Path.Combine(dir, "MyanmarSpellChecker-EnterpriseCA.cer");
                    if (File.Exists(cerCand)) cerFile = cerCand;
                    break;
                }
            }

            if (string.IsNullOrEmpty(coreDll))
            {
                cur = baseDir;
                for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(cur); depth++)
                {
                    string wRel = Path.Combine(cur, @"src\MyanmarSpellChecker.WordAddIn\bin\Release\net48\MyanmarSpellChecker.WordAddIn.dll");
                    string eRel = Path.Combine(cur, @"src\MyanmarSpellChecker.ExcelAddIn\bin\Release\net48\MyanmarSpellChecker.ExcelAddIn.dll");
                    string pRel = Path.Combine(cur, @"src\MyanmarSpellChecker.PowerPointAddIn\bin\Release\net48\MyanmarSpellChecker.PowerPointAddIn.dll");
                    string cRel = Path.Combine(cur, @"src\MyanmarSpellChecker.Core\bin\Release\net48\MyanmarSpellChecker.Core.dll");
                    if (File.Exists(cRel) && File.Exists(wRel) && File.Exists(eRel))
                    {
                        coreDll = cRel;
                        wordDll = wRel;
                        excelDll = eRel;
                        pptDll = File.Exists(pRel) ? pRel : string.Empty;
                        break;
                    }
                    cur = Path.GetDirectoryName(cur);
                }
            }

            cur = baseDir;
            for (int depth = 0; depth < 6 && !string.IsNullOrEmpty(cur); depth++)
            {
                foreach (string relDb in new[]
                {
                    "mlc_thatpon.db",
                    @"data\mlc_thatpon.db",
                    @"artifacts\release-package\mlc_thatpon.db",
                    @"artifacts\mlc_thatpon.db"
                })
                {
                    string cand = Path.Combine(cur, relDb);
                    if (File.Exists(cand) && new FileInfo(cand).Length >= 5_000_000)
                    {
                        dbFile = cand;
                        break;
                    }
                }
                if (!string.IsNullOrEmpty(dbFile)) break;
                cur = Path.GetDirectoryName(cur);
            }

            return !string.IsNullOrEmpty(coreDll) && !string.IsNullOrEmpty(wordDll) &&
                   !string.IsNullOrEmpty(excelDll) && !string.IsNullOrEmpty(dbFile);
        }

        public static void ExecuteInstallOrUpdate(string? sourceOverrideDir, Action<string>? statusCallback)
        {
            string localRoot = GetLocalRootDir();
            string appDir = GetAppInstallDir();
            string guideDir = Path.Combine(localRoot, "Guide");
            Directory.CreateDirectory(localRoot);
            Directory.CreateDirectory(appDir);
            Directory.CreateDirectory(guideDir);

            statusCallback?.Invoke("Extracting signed add-in binaries & 183,000+ entry offline dictionary...");

            bool usedOverride = !string.IsNullOrWhiteSpace(sourceOverrideDir) &&
                                Directory.Exists(sourceOverrideDir) &&
                                File.Exists(Path.Combine(sourceOverrideDir!, "MyanmarSpellChecker.Core.dll"));

            if (!usedOverride && HasEmbeddedPayload(out _))
            {
                using (Stream? resStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Program.PayloadResourceName))
                using (var archive = new ZipArchive(resStream!, ZipArchiveMode.Read))
                {
                    foreach (var entry in archive.Entries)
                    {
                        string name = Path.GetFileName(entry.FullName);
                        if (string.IsNullOrEmpty(name)) continue;

                        if (name.StartsWith("Myanmar-Spell-Checker-User-Guide", StringComparison.OrdinalIgnoreCase))
                        {
                            entry.ExtractToFile(Path.Combine(guideDir, name), overwrite: true);
                        }
                        else
                        {
                            string dest = Path.Combine(appDir, name);
                            entry.ExtractToFile(dest, overwrite: true);
                            if (string.Equals(name, "mlc_thatpon.db", StringComparison.OrdinalIgnoreCase))
                            {
                                File.Copy(dest, Path.Combine(localRoot, "mlc_thatpon.db"), overwrite: true);
                            }
                        }
                    }
                }
            }
            else if (TryLocateExternalSourceFiles(sourceOverrideDir, out string coreDll, out string wordDll, out string excelDll, out string pptDll, out string dbFile, out string? cerFile))
            {
                File.Copy(coreDll, Path.Combine(appDir, "MyanmarSpellChecker.Core.dll"), overwrite: true);
                File.Copy(wordDll, Path.Combine(appDir, "MyanmarSpellChecker.WordAddIn.dll"), overwrite: true);
                File.Copy(excelDll, Path.Combine(appDir, "MyanmarSpellChecker.ExcelAddIn.dll"), overwrite: true);
                if (!string.IsNullOrEmpty(pptDll) && File.Exists(pptDll))
                    File.Copy(pptDll, Path.Combine(appDir, "MyanmarSpellChecker.PowerPointAddIn.dll"), overwrite: true);

                File.Copy(dbFile, Path.Combine(appDir, "mlc_thatpon.db"), overwrite: true);
                File.Copy(dbFile, Path.Combine(localRoot, "mlc_thatpon.db"), overwrite: true);
                if (!string.IsNullOrEmpty(cerFile) && File.Exists(cerFile))
                    File.Copy(cerFile!, Path.Combine(appDir, "MyanmarSpellChecker-EnterpriseCA.cer"), overwrite: true);

                string searchBase = !string.IsNullOrWhiteSpace(sourceOverrideDir) ? sourceOverrideDir! : Path.GetDirectoryName(coreDll) ?? string.Empty;
                foreach (string gf in new[] { "Myanmar-Spell-Checker-User-Guide.html", "Myanmar-Spell-Checker-User-Guide.docx" })
                {
                    string gCand = Path.Combine(searchBase, gf);
                    if (File.Exists(gCand))
                        File.Copy(gCand, Path.Combine(guideDir, gf), overwrite: true);
                }

                string stagedSetupCand = Path.Combine(searchBase, "MyanmarSpellChecker-Setup.exe");
                if (File.Exists(stagedSetupCand))
                {
                    SafeReplacePersistedSetupExe(stagedSetupCand);
                }
            }
            else
            {
                throw new FileNotFoundException("Installer payload could not be located. Please download MyanmarSpellChecker-Setup.exe from GitHub Releases.");
            }

            // Persist Setup.exe into %LocalAppData%\MyanmarSpellChecker\ so Windows Settings & "မိုး" Desktop shortcut stay up to date
            string selfExe = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrWhiteSpace(selfExe) && File.Exists(selfExe))
            {
                SafeReplacePersistedSetupExe(selfExe);
            }

            string installedCer = Path.Combine(appDir, "MyanmarSpellChecker-EnterpriseCA.cer");
            if (File.Exists(installedCer))
            {
                try
                {
                    var cert = new X509Certificate2(installedCer);
                    using (var store = new X509Store(StoreName.TrustedPublisher, StoreLocation.CurrentUser))
                    {
                        store.Open(OpenFlags.ReadWrite);
                        store.Add(cert);
                    }
                }
                catch { }
            }

            statusCallback?.Invoke("Registering Word, Excel & PowerPoint COM Add-ins (64-bit & 32-bit)...");
            RegisterComAndOfficeKeys(appDir);
            RegisterWindowsUninstallEntry(localRoot, appDir);
        }

        public static void ApplyStandaloneSetupExeUpdate(string newSetupExePath)
        {
            var fvi = FileVersionInfo.GetVersionInfo(newSetupExePath);
            if (new FileInfo(newSetupExePath).Length < 16_384 ||
                string.IsNullOrWhiteSpace(fvi.ProductName) ||
                fvi.ProductName!.IndexOf("Myanmar", StringComparison.OrdinalIgnoreCase) < 0)
            {
                throw new InvalidDataException("Selected .exe is not a valid Myanmar Official Spell Checker Setup package.");
            }

            SafeReplacePersistedSetupExe(newSetupExePath);

            var psi = new ProcessStartInfo
            {
                FileName = newSetupExePath,
                Arguments = "--silent",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            using (var proc = Process.Start(psi)!)
            {
                string err = proc.StandardError.ReadToEnd();
                proc.WaitForExit(120_000);
                if (proc.ExitCode != 0)
                {
                    throw new InvalidOperationException("Setup update returned exit code " + proc.ExitCode + ": " + err);
                }
            }
        }

        public static void ApplyDictionaryDatabaseFile(string verifiedDbFile)
        {
            var fi = new FileInfo(verifiedDbFile);
            if (!fi.Exists || fi.Length < 5_000_000)
                throw new InvalidDataException("Dictionary database file is invalid or smaller than 5 MB.");

            string localRoot = GetLocalRootDir();
            string appDir = GetAppInstallDir();
            Directory.CreateDirectory(localRoot);
            Directory.CreateDirectory(appDir);

            foreach (string target in new[]
            {
                Path.Combine(localRoot, "mlc_thatpon.db"),
                Path.Combine(appDir, "mlc_thatpon.db")
            })
            {
                if (string.Equals(Path.GetFullPath(verifiedDbFile), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                    continue;
                string tmp = target + ".upd_tmp";
                File.Copy(verifiedDbFile, tmp, overwrite: true);
                if (File.Exists(target))
                    File.Delete(target);
                File.Move(tmp, target);
            }
        }

        public static void ApplyLocalUpdateFile(string filePath, Action<string>? statusCallback)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
                throw new FileNotFoundException("Selected update file was not found.", filePath);

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext == ".db")
            {
                statusCallback?.Invoke("Verifying & installing mlc_thatpon.db dictionary...");
                ApplyDictionaryDatabaseFile(filePath);
                return;
            }

            if (ext == ".exe")
            {
                statusCallback?.Invoke("Installing verified Setup.exe package & self-updating launcher...");
                ApplyStandaloneSetupExeUpdate(filePath);
                return;
            }

            if (ext == ".zip")
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "msc_setup_zip_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    statusCallback?.Invoke("Extracting & verifying EasySetup.zip package...");
                    ZipFile.ExtractToDirectory(filePath, tempDir);
                    ExecuteInstallOrUpdate(tempDir, statusCallback);
                }
                finally
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
                return;
            }

            throw new InvalidDataException("Unsupported file format. Choose MyanmarSpellChecker-Setup.exe, EasySetup.zip, or mlc_thatpon.db.");
        }

        public static void RegisterComAndOfficeKeys(string appDir)
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                {
                    foreach (var c in Program.ComClasses)
                    {
                        string dllPath = Path.GetFullPath(Path.Combine(appDir, c.DllName));
                        if (!File.Exists(dllPath)) continue;
                        string codeBaseUri = new Uri(dllPath).AbsoluteUri.Replace("%20", " ");

                        using (var progKey = hkcu.CreateSubKey(@"Software\Classes\" + c.ProgId))
                        {
                            progKey?.SetValue("", c.TypeName);
                        }
                        using (var progClsid = hkcu.CreateSubKey(@"Software\Classes\" + c.ProgId + @"\CLSID"))
                        {
                            progClsid?.SetValue("", c.Clsid);
                        }
                        using (var clsidKey = hkcu.CreateSubKey(@"Software\Classes\CLSID\" + c.Clsid))
                        {
                            clsidKey?.SetValue("", c.TypeName);
                        }
                        using (var inproc = hkcu.CreateSubKey(@"Software\Classes\CLSID\" + c.Clsid + @"\InprocServer32"))
                        {
                            if (inproc != null)
                            {
                                inproc.SetValue("", "mscoree.dll");
                                inproc.SetValue("ThreadingModel", "Apartment");
                                inproc.SetValue("Class", c.TypeName);
                                inproc.SetValue("Assembly", c.AsmFullName);
                                inproc.SetValue("RuntimeVersion", "v4.0.30319");
                                inproc.SetValue("CodeBase", codeBaseUri);
                            }
                        }
                        using (var verKey = hkcu.CreateSubKey(@"Software\Classes\CLSID\" + c.Clsid + @"\InprocServer32\1.0.0.0"))
                        {
                            if (verKey != null)
                            {
                                verKey.SetValue("Class", c.TypeName);
                                verKey.SetValue("Assembly", c.AsmFullName);
                                verKey.SetValue("RuntimeVersion", "v4.0.30319");
                                verKey.SetValue("CodeBase", codeBaseUri);
                            }
                        }
                        using (var pidKey = hkcu.CreateSubKey(@"Software\Classes\CLSID\" + c.Clsid + @"\ProgId"))
                        {
                            pidKey?.SetValue("", c.ProgId);
                        }
                        using (hkcu.CreateSubKey(@"Software\Classes\CLSID\" + c.Clsid + @"\Implemented Categories\{62C8FE65-4EBB-45e7-B440-6E39B2CDBF29}")) { }

                        if (c.IsControl)
                        {
                            using (hkcu.CreateSubKey(@"Software\Classes\CLSID\" + c.Clsid + @"\Control")) { }
                            using (hkcu.CreateSubKey(@"Software\Classes\CLSID\" + c.Clsid + @"\Implemented Categories\{40FC6ED4-2438-11CF-A3DB-080036F12502}")) { }
                        }
                    }

                    RegisterOfficeAddInKey(hkcu, @"Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn",
                        "Myanmar Spell Checker", "100% Offline Myanmar Spell and Grammar Checker (Word)");
                    RegisterOfficeAddInKey(hkcu, @"Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn",
                        "Myanmar Spell Checker", "100% Offline Formula-Safe Myanmar Spell and Grammar Checker (Excel)");
                    if (File.Exists(Path.Combine(appDir, "MyanmarSpellChecker.PowerPointAddIn.dll")))
                    {
                        RegisterOfficeAddInKey(hkcu, @"Software\Microsoft\Office\PowerPoint\Addins\MyanmarSpellChecker.PowerPointAddIn",
                            "Myanmar Spell Checker", "100% Offline Zero-Mutation Myanmar Spell and Grammar Checker (PowerPoint)");
                    }

                    foreach (string ver in new[] { "16.0", "15.0", "14.0" })
                    {
                        foreach (var pair in new[]
                        {
                            ("Word", "MyanmarSpellChecker.WordAddIn"),
                            ("Excel", "MyanmarSpellChecker.ExcelAddIn"),
                            ("PowerPoint", "MyanmarSpellChecker.PowerPointAddIn")
                        })
                        {
                            try { hkcu.DeleteSubKeyTree($@"Software\Microsoft\Office\{ver}\{pair.Item1}\Resiliency\DisabledItems", throwOnMissingSubKey: false); } catch { }
                            try { hkcu.DeleteSubKeyTree($@"Software\Microsoft\Office\{ver}\{pair.Item1}\Resiliency\CrashingAddinList", throwOnMissingSubKey: false); } catch { }
                            try
                            {
                                using (var dnd = hkcu.CreateSubKey($@"Software\Microsoft\Office\{ver}\{pair.Item1}\Resiliency\DoNotDisableAddinList"))
                                {
                                    dnd?.SetValue(pair.Item2, 1, RegistryValueKind.DWord);
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        private static void RegisterOfficeAddInKey(RegistryKey hkcu, string subKeyPath, string friendlyName, string description)
        {
            using (var k = hkcu.CreateSubKey(subKeyPath))
            {
                if (k != null)
                {
                    k.SetValue("FriendlyName", friendlyName, RegistryValueKind.String);
                    k.SetValue("Description", description, RegistryValueKind.String);
                    k.SetValue("LoadBehavior", 3, RegistryValueKind.DWord);
                }
            }
        }

        private static void RegisterWindowsUninstallEntry(string localRoot, string appDir)
        {
            try
            {
                string setupExe = Path.Combine(localRoot, "MyanmarSpellChecker-Setup.exe");
                string ver = GetCurrentlyInstalledVersion() ?? GetSetupVersion();
                long totalBytes = Directory.GetFiles(appDir).Sum(f => new FileInfo(f).Length);
                int estimatedKb = (int)Math.Max(1024L, totalBytes / 1024L);

                using (var key = Registry.CurrentUser.CreateSubKey(Program.UninstallRegSubKey))
                {
                    if (key != null)
                    {
                        key.SetValue("DisplayName", "Myanmar Official Spell Checker Suite (Word + Excel + PowerPoint)", RegistryValueKind.String);
                        key.SetValue("DisplayVersion", ver, RegistryValueKind.String);
                        key.SetValue("Publisher", "Htet Yar Zar (ထက်ရာဇာ) — Department of Geology, Kyaukse University", RegistryValueKind.String);
                        key.SetValue("InstallLocation", appDir, RegistryValueKind.String);
                        key.SetValue("URLInfoAbout", "https://github.com/Hashyz/MyanmarSpellChecker", RegistryValueKind.String);
                        key.SetValue("URLUpdateInfo", "https://github.com/Hashyz/MyanmarSpellChecker/releases", RegistryValueKind.String);
                        if (File.Exists(setupExe))
                        {
                            key.SetValue("DisplayIcon", setupExe, RegistryValueKind.String);
                            key.SetValue("UninstallString", "\"" + setupExe + "\" --uninstall", RegistryValueKind.String);
                            key.SetValue("QuietUninstallString", "\"" + setupExe + "\" --uninstall --silent", RegistryValueKind.String);
                            key.SetValue("ModifyPath", "\"" + setupExe + "\"", RegistryValueKind.String);
                        }
                        key.SetValue("NoModify", 0, RegistryValueKind.DWord);
                        key.SetValue("NoRepair", 0, RegistryValueKind.DWord);
                        key.SetValue("EstimatedSize", estimatedKb, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public const string MoeDesktopShortcutFileName = "\u1019\u102D\u102F\u1038.lnk"; // မိုး.lnk

        public static string GetMoeDesktopShortcutPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), MoeDesktopShortcutFileName);

        public static void CreateMoeDesktopShortcut()
        {
            try
            {
                string localRoot = GetLocalRootDir();
                string setupExe = Path.Combine(localRoot, "MyanmarSpellChecker-Setup.exe");
                if (!File.Exists(setupExe))
                {
                    setupExe = Assembly.GetExecutingAssembly().Location;
                }
                if (string.IsNullOrWhiteSpace(setupExe) || !File.Exists(setupExe))
                    return;

                string lnkPath = GetMoeDesktopShortcutPath();
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null)
                    return;

                object? shell = Activator.CreateInstance(shellType);
                if (shell == null)
                    return;

                object? shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                if (shortcut != null)
                {
                    Type scType = shortcut.GetType();
                    scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { setupExe });
                    scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { localRoot });
                    scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { setupExe + ",0" });
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { "မိုး — Myanmar Official Spell Checker & Offline Dictionary Suite" });
                    scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                }
            }
            catch { }
        }

        public static void ExecuteUninstall()
        {
            try
            {
                string moeLnk = GetMoeDesktopShortcutPath();
                if (File.Exists(moeLnk))
                    File.Delete(moeLnk);
            }
            catch { }
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using (var hkcu = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, view))
                {
                    foreach (var c in Program.ComClasses)
                    {
                        try { hkcu.DeleteSubKeyTree(@"Software\Classes\" + c.ProgId, throwOnMissingSubKey: false); } catch { }
                        try { hkcu.DeleteSubKeyTree(@"Software\Classes\CLSID\" + c.Clsid, throwOnMissingSubKey: false); } catch { }
                    }
                    try { hkcu.DeleteSubKeyTree(@"Software\Microsoft\Office\Word\Addins\MyanmarSpellChecker.WordAddIn", throwOnMissingSubKey: false); } catch { }
                    try { hkcu.DeleteSubKeyTree(@"Software\Microsoft\Office\Excel\Addins\MyanmarSpellChecker.ExcelAddIn", throwOnMissingSubKey: false); } catch { }
                    try { hkcu.DeleteSubKeyTree(@"Software\Microsoft\Office\PowerPoint\Addins\MyanmarSpellChecker.PowerPointAddIn", throwOnMissingSubKey: false); } catch { }
                }
            }

            try { Registry.CurrentUser.DeleteSubKeyTree(Program.UninstallRegSubKey, throwOnMissingSubKey: false); } catch { }

            string appDir = GetAppInstallDir();
            if (Directory.Exists(appDir))
            {
                try { Directory.Delete(appDir, recursive: true); } catch { }
            }
        }
    }

    internal sealed class SetupWizardForm : Form
    {
        private bool _isEnglish = true;
        private SetupGitHubCheckResult? _pendingGitHubRelease;

        private readonly Label _lblTitle;
        private readonly Label _lblSubTitle;
        private readonly Panel _statusPanel;
        private readonly Label _lblStatusCard;
        private readonly Button _btnCheckGitHub;
        private readonly Button _btnFromFile;
        private readonly Label _lblFeatures;
        private readonly Label _lblAuthor;
        private readonly CheckBox _chkDesktopMoeShortcut;
        private readonly ProgressBar _progressBar;
        private readonly Label _lblProgress;
        private readonly Button _btnLang;
        private readonly Button _btnInstall;
        private readonly Button _btnGuide;
        private readonly Button _btnRepair;
        private readonly Button _btnUninstall;
        private readonly Button _btnClose;

        public int ExitCode { get; private set; }

        public SetupWizardForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(620, 496);
            BackColor = Color.FromArgb(244, 247, 252);
            Font = CreateMyanmarFont(9.5f, FontStyle.Regular);
            try
            {
                string selfLoc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(selfLoc) && File.Exists(selfLoc))
                    Icon = Icon.ExtractAssociatedIcon(selfLoc);
            }
            catch { }

            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 84,
                BackColor = Color.FromArgb(20, 74, 158),
                Padding = new Padding(18, 12, 18, 10)
            };

            var picLogo = new PictureBox
            {
                Location = new Point(18, 13),
                Size = new Size(58, 58),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = RenderSetupHeaderLogoBitmap(256)
            };

            _lblTitle = new Label
            {
                ForeColor = Color.White,
                Font = CreateMyanmarFont(12.0f, FontStyle.Bold),
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(86, 13)
            };
            _lblSubTitle = new Label
            {
                ForeColor = Color.FromArgb(218, 232, 255),
                Font = CreateMyanmarFont(9.0f, FontStyle.Regular),
                AutoSize = true,
                UseMnemonic = false,
                Location = new Point(86, 46)
            };
            _btnLang = new Button
            {
                Size = new Size(108, 30),
                Location = new Point(494, 14),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(34, 96, 190),
                ForeColor = Color.White,
                Font = CreateMyanmarFont(8.8f, FontStyle.Bold),
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            _btnLang.FlatAppearance.BorderColor = Color.FromArgb(120, 168, 240);
            _btnLang.Click += (s, e) =>
            {
                _isEnglish = !_isEnglish;
                RefreshLocalizedText();
            };

            headerPanel.Controls.Add(_btnLang);
            headerPanel.Controls.Add(picLogo);
            headerPanel.Controls.Add(_lblTitle);
            headerPanel.Controls.Add(_lblSubTitle);

            var bodyCard = new Panel
            {
                Location = new Point(18, 96),
                Size = new Size(584, 330),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(14)
            };

            _statusPanel = new Panel
            {
                Location = new Point(14, 12),
                Size = new Size(554, 60),
                BackColor = Color.FromArgb(232, 247, 237),
                BorderStyle = BorderStyle.FixedSingle
            };

            _lblStatusCard = new Label
            {
                Location = new Point(8, 4),
                Size = new Size(394, 50),
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(16, 98, 48),
                Font = CreateMyanmarFont(9.0f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                UseMnemonic = false
            };

            _btnCheckGitHub = CreateActionButton(new Point(408, 5), new Size(138, 23), Color.FromArgb(24, 86, 182), Color.White, bold: true, fontPt: 8.3f);
            _btnCheckGitHub.Click += OnCheckGitHubClick;

            _btnFromFile = CreateActionButton(new Point(408, 31), new Size(138, 22), Color.FromArgb(242, 246, 253), Color.FromArgb(24, 70, 148), bold: false, fontPt: 8.2f);
            _btnFromFile.Click += OnFromFileClick;

            _statusPanel.Controls.Add(_lblStatusCard);
            _statusPanel.Controls.Add(_btnCheckGitHub);
            _statusPanel.Controls.Add(_btnFromFile);

            _lblFeatures = new Label
            {
                Location = new Point(14, 80),
                Size = new Size(554, 106),
                ForeColor = Color.FromArgb(36, 48, 66),
                Font = CreateMyanmarFont(9.0f, FontStyle.Regular),
                UseMnemonic = false
            };

            _lblAuthor = new Label
            {
                Location = new Point(14, 190),
                Size = new Size(554, 46),
                BackColor = Color.FromArgb(245, 248, 255),
                ForeColor = Color.FromArgb(34, 54, 92),
                BorderStyle = BorderStyle.FixedSingle,
                Font = CreateMyanmarFont(8.6f, FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 4, 8, 4),
                UseMnemonic = false
            };

            _chkDesktopMoeShortcut = new CheckBox
            {
                Location = new Point(14, 242),
                Size = new Size(554, 30),
                Checked = File.Exists(SetupInstallerEngine.GetMoeDesktopShortcutPath()),
                ForeColor = Color.FromArgb(22, 56, 108),
                Font = CreateMyanmarFont(9.0f, FontStyle.Bold),
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };

            _progressBar = new ProgressBar
            {
                Location = new Point(14, 276),
                Size = new Size(554, 14),
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Continuous,
                Visible = false
            };

            _lblProgress = new Label
            {
                Location = new Point(14, 294),
                Size = new Size(554, 24),
                ForeColor = Color.FromArgb(20, 90, 178),
                Font = CreateMyanmarFont(8.8f, FontStyle.Bold),
                UseMnemonic = false
            };

            bodyCard.Controls.Add(_statusPanel);
            bodyCard.Controls.Add(_lblFeatures);
            bodyCard.Controls.Add(_lblAuthor);
            bodyCard.Controls.Add(_chkDesktopMoeShortcut);
            bodyCard.Controls.Add(_progressBar);
            bodyCard.Controls.Add(_lblProgress);

            var footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 58,
                BackColor = Color.White,
                Padding = new Padding(18, 10, 18, 10)
            };

            _btnInstall = CreateActionButton(new Point(18, 11), new Size(194, 36), Color.FromArgb(18, 120, 60), Color.White, bold: true);
            _btnInstall.Click += OnInstallClick;

            _btnGuide = CreateActionButton(new Point(218, 11), new Size(92, 36), Color.FromArgb(235, 246, 239), Color.FromArgb(16, 104, 52), bold: true);
            _btnGuide.Click += (s, e) =>
            {
                string htmlGuide = Path.Combine(SetupInstallerEngine.GetLocalRootDir(), "Guide", "Myanmar-Spell-Checker-User-Guide.html");
                if (File.Exists(htmlGuide))
                {
                    try { Process.Start(new ProcessStartInfo(htmlGuide) { UseShellExecute = true }); return; } catch { }
                }
                MessageBox.Show(this,
                    _isEnglish ? "Click 'Install Suite' first to unpack the offline HTML & A4 Word User Guides." : "Offline လမ်းညွှန်စာအုပ် ဖွင့်ရန် ဦးစွာ Install နှိပ်၍ ထည့်သွင်းပေးပါ။",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            _btnRepair = CreateActionButton(new Point(316, 11), new Size(122, 36), Color.FromArgb(232, 241, 255), Color.FromArgb(20, 74, 158), bold: true);
            _btnRepair.Click += OnRepairClick;

            _btnUninstall = CreateActionButton(new Point(444, 11), new Size(86, 36), Color.FromArgb(253, 240, 240), Color.FromArgb(164, 38, 38), bold: false);
            _btnUninstall.Click += OnUninstallClick;

            _btnClose = CreateActionButton(new Point(536, 11), new Size(66, 36), Color.FromArgb(242, 244, 248), Color.FromArgb(52, 62, 78), bold: false);
            _btnClose.Click += (s, e) => Close();

            footerPanel.Controls.Add(_btnInstall);
            footerPanel.Controls.Add(_btnGuide);
            footerPanel.Controls.Add(_btnRepair);
            footerPanel.Controls.Add(_btnUninstall);
            footerPanel.Controls.Add(_btnClose);

            Controls.Add(bodyCard);
            Controls.Add(footerPanel);
            Controls.Add(headerPanel);

            RefreshLocalizedText();
        }

        private void RefreshLocalizedText()
        {
            string setupVer = SetupInstallerEngine.GetSetupVersion();
            string? instVer = SetupInstallerEngine.GetCurrentlyInstalledVersion();
            string activeVer = instVer ?? setupVer;

            Text = _isEnglish
                ? $"Myanmar Official Spell Checker v{activeVer} — 1-Click Setup & GitHub Updater"
                : $"မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း v{activeVer} — ထည့်သွင်းခြင်းနှင့် GitHub အဆင့်မြှင့်စနစ်";

            _btnLang.Text = _isEnglish ? "A/က မြန်မာ" : "A/က English";

            _lblTitle.Text = _isEnglish
                ? "Myanmar Official Spell Checker Suite"
                : "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း စစ်ဆေးရေးစနစ်";
            _lblSubTitle.Text = _isEnglish
                ? $"Unified 100% Offline Plugin for Microsoft Word, Excel & PowerPoint (v{activeVer})"
                : $"Microsoft Word၊ Excel နှင့် PowerPoint သုံးမျိုးလုံးအတွက် 100% Offline စနစ် (v{activeVer})";

            _btnCheckGitHub.Text = _isEnglish ? "\u2601 Check GitHub" : "\u2601 GitHub စစ်မည်";
            _btnFromFile.Text = _isEnglish ? "+ From File..." : "+ ဖိုင်မှတင်မည်...";

            if (_pendingGitHubRelease != null && _pendingGitHubRelease.IsUpdateAvailable)
            {
                _statusPanel.BackColor = Color.FromArgb(224, 242, 255);
                _lblStatusCard.ForeColor = Color.FromArgb(12, 68, 148);
                string badge = _pendingGitHubRelease.SignatureVerified ? "RSA-2048 + SHA-256" : "SHA-256";
                _lblStatusCard.Text = _isEnglish
                    ? $"\u26A1 New GitHub Release: v{_pendingGitHubRelease.LatestVersion} ({badge})\r\n   Click '↓ Update to v{_pendingGitHubRelease.LatestVersion}' below to install!"
                    : $"\u26A1 GitHub ဗားရှင်းအသစ် တွေ့ရှိပါသည် - v{_pendingGitHubRelease.LatestVersion}\r\n   အောက်ပါ '↓ v{_pendingGitHubRelease.LatestVersion} မြှင့်မည်' ကိုနှိပ်ပါ။";
            }
            else if (string.IsNullOrEmpty(instVer))
            {
                _statusPanel.BackColor = Color.FromArgb(232, 247, 237);
                _lblStatusCard.ForeColor = Color.FromArgb(16, 98, 48);
                _lblStatusCard.Text = _isEnglish
                    ? $"✓ Ready to Install v{setupVer}\r\n   Zero Admin Password • Zero .NET SDK Required"
                    : $"✓ ဗားရှင်း v{setupVer} ထည့်သွင်းရန် အသင့်ရှိပါသည်\r\n   Admin Password နှင့် .NET SDK လုံးဝ မလိုအပ်ပါ";
            }
            else
            {
                _statusPanel.BackColor = Color.FromArgb(232, 247, 237);
                _lblStatusCard.ForeColor = Color.FromArgb(16, 98, 48);
                _lblStatusCard.Text = _isEnglish
                    ? $"✓ Installed: v{instVer}  ->  Package: v{setupVer}\r\n   Custom Words, Bookmarks & Settings 100% preserved."
                    : $"✓ လက်ရှိ v{instVer}  ->  ပက်ကေ့ချ် v{setupVer}\r\n   ကိုယ်ပိုင်စာလုံးနှင့် Bookmarks များ ၁၀၀% မပျက်စီးပါ။";
            }

            _lblFeatures.Text = _isEnglish
                ? "• Installs Word, Excel & PowerPoint COM Add-ins (64-bit & 32-bit Office 2016–365)\r\n" +
                  "• Includes 183,000+ Official Thatpon, Bilingual EN-MM Dictionary & Encyclopedia\r\n" +
                  "• Linked with GitHub Releases ('\u2601 Check GitHub') + Offline USB File Updater\r\n" +
                  "• Includes Offline Interactive HTML & Printable A4 Word (.docx) User Guides"
                : "• Word၊ Excel နှင့် PowerPoint (64-bit & 32-bit) သုံးမျိုးလုံး တစ်ပြိုင်နက် ထည့်သွင်းပေးသည်\r\n" +
                  "• သတ်ပုံကျမ်း၊ အင်္ဂလိပ်-မြန်မာ အဘိဓာန်နှင့် စွယ်စုံဗဟုသုတ ၁၈၃,၀၀၀+ ပါဝင်သည်\r\n" +
                  "• GitHub မှ ဗားရှင်းအသစ် တိုက်ရိုက်စစ်၍ အဆင့်မြှင့်နိုင်သလို USB ဖိုင်မှလည်း တင်နိုင်သည်\r\n" +
                  "• Offline HTML လမ်းညွှန်နှင့် ပရင့်ထုတ်နိုင်သော A4 Word (.docx) စာအုပ် ပါဝင်သည်";

            _lblAuthor.Text = _isEnglish
                ? "Creator: Htet Yar Zar (ထက်ရာဇာ) — Department of Geology, Kyaukse University, Myanmar\r\n" +
                  "100% Free & Open-Source Software (GNU GPL v3.0 + Section 7b Attribution)"
                : "ဖန်တီးသူ - ထက်ရာဇာ (Htet Yar Zar)၊ ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ်\r\n" +
                  "၁၀၀% အခမဲ့ Open-Source စနစ် (GNU GPL v3.0 + Section 7b Attribution)";

            _chkDesktopMoeShortcut.Text = _isEnglish
                ? "Create Desktop shortcut named \"မိုး\" (Quick Guide / Repair / GitHub Update)"
                : "Desktop ပေါ်တွင် \"မိုး\" အိုင်ကွန် တင်ထားမည် (လမ်းညွှန် / ပြင်ရန် / ဗားရှင်းမြှင့်ရန်)";

            if (_pendingGitHubRelease != null && _pendingGitHubRelease.IsUpdateAvailable)
            {
                _btnInstall.BackColor = Color.FromArgb(14, 98, 196);
                _btnInstall.Text = _isEnglish
                    ? $"↓ Update to v{_pendingGitHubRelease.LatestVersion}"
                    : $"↓ v{_pendingGitHubRelease.LatestVersion} မြှင့်မည်";
            }
            else
            {
                _btnInstall.BackColor = Color.FromArgb(18, 120, 60);
                _btnInstall.Text = string.IsNullOrEmpty(instVer)
                    ? (_isEnglish ? "✓ Install Suite" : "✓ စတင်ထည့်သွင်းမည်")
                    : (_isEnglish ? $"✓ Update v{setupVer}" : $"✓ v{setupVer} မြှင့်မည်");
            }

            _btnGuide.Text = _isEnglish ? "Guide" : "လမ်းညွှန်";
            _btnRepair.Text = _isEnglish ? "↻ Repair" : "↻ ပြင်မည်";
            _btnUninstall.Text = _isEnglish ? "✕ Remove" : "✕ ဖြုတ်မည်";
            _btnClose.Text = _isEnglish ? "Close" : "ပိတ်မည်";
        }

        private async void OnCheckGitHubClick(object? sender, EventArgs e)
        {
            SetButtonsEnabled(false);
            _progressBar.Visible = true;
            _progressBar.Style = ProgressBarStyle.Marquee;
            _lblProgress.Text = _isEnglish
                ? "Checking https://github.com/Hashyz/MyanmarSpellChecker/releases/latest ..."
                : "GitHub Releases တွင် ဗားရှင်းအသစ် ရှိမရှိ စစ်ဆေးနေပါသည်...";

            try
            {
                SetupGitHubCheckResult res = await SetupOnlineUpdater.CheckGitHubReleaseAsync();
                _progressBar.Style = ProgressBarStyle.Continuous;
                _progressBar.Value = 0;
                _progressBar.Visible = false;

                if (!res.Success)
                {
                    _lblProgress.Text = _isEnglish ? res.ErrorMessageEn : res.ErrorMessageMm;
                    MessageBox.Show(this, _lblProgress.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!res.IsUpdateAvailable)
                {
                    _pendingGitHubRelease = null;
                    RefreshLocalizedText();
                    string upToDate = _isEnglish
                        ? $"✓ You are on the latest GitHub release (v{res.LatestVersion})."
                        : $"✓ လက်ရှိဗားရှင်း (v{res.LatestVersion}) သည် GitHub မှ နောက်ဆုံးထွက်ဗားရှင်း ဖြစ်ပါသည်။";
                    _lblProgress.Text = upToDate;
                    MessageBox.Show(this, upToDate, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                _pendingGitHubRelease = res;
                RefreshLocalizedText();
                string foundMsg = _isEnglish
                    ? $"\u26A1 New version v{res.LatestVersion} is available on GitHub (installed: v{res.CurrentVersion})!\r\n\r\n" +
                      (res.IsDictionaryOnlyUpdate
                          ? "Only the dictionary database changed — fast dictionary update ready.\r\n"
                          : "Full Office Suite + Desktop 'မိုး' updater self-upgrade ready.\r\n") +
                      "\r\nInstall v" + res.LatestVersion + " from GitHub right now?"
                    : $"\u26A1 GitHub တွင် ဗားရှင်းအသစ် v{res.LatestVersion} ထွက်ရှိနေပါပြီ (လက်ရှိ v{res.CurrentVersion})!\r\n\r\nယခုပင် ဒေါင်းလုဒ်ဆွဲ၍ အဆင့်မြှင့်မည်လား?";

                if (MessageBox.Show(this, foundMsg, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                {
                    await DownloadAndApplyGitHubReleaseAsync(res);
                }
                else
                {
                    _lblProgress.Text = _isEnglish
                        ? $"\u26A1 Ready: click '↓ Update to v{res.LatestVersion}' anytime to download & install."
                        : $"\u26A1 အဆင့်မြှင့်လိုသည့်အချိန်တွင် '↓ v{res.LatestVersion} မြှင့်မည်' ကိုနှိပ်ပါ။";
                }
            }
            finally
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                SetButtonsEnabled(true);
            }
        }

        private async Task DownloadAndApplyGitHubReleaseAsync(SetupGitHubCheckResult releaseInfo)
        {
            if (releaseInfo.Manifest == null)
                return;

            if (!releaseInfo.IsDictionaryOnlyUpdate && !Program.PromptCloseOfficeIfRunning(_isEnglish))
                return;

            SetButtonsEnabled(false);
            _progressBar.Visible = true;
            _progressBar.Style = ProgressBarStyle.Continuous;
            _progressBar.Value = 0;

            string dlDir = Path.Combine(SetupInstallerEngine.GetLocalRootDir(), "staging", "gh_" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dlDir);

            try
            {
                var m = releaseInfo.Manifest;
                Action<long, long> onProgress = (done, total) =>
                {
                    if (InvokeRequired)
                    {
                        BeginInvoke(new Action(() => UpdateDownloadProgressUi(done, total, releaseInfo.LatestVersion)));
                    }
                    else
                    {
                        UpdateDownloadProgressUi(done, total, releaseInfo.LatestVersion);
                    }
                };

                if (releaseInfo.IsDictionaryOnlyUpdate && !string.IsNullOrWhiteSpace(m.DatabaseDownloadUrl))
                {
                    string dlDb = Path.Combine(dlDir, "mlc_thatpon.db");
                    await SetupOnlineUpdater.DownloadVerifiedAssetAsync(m.DatabaseDownloadUrl, dlDb, m.DatabaseSha256, onProgress);
                    _lblProgress.Text = _isEnglish ? "Applying verified dictionary update..." : "အဘိဓာန်အသစ်ကို ထည့်သွင်းနေပါသည်...";
                    Application.DoEvents();
                    SetupInstallerEngine.ApplyDictionaryDatabaseFile(dlDb);
                }
                else if (!string.IsNullOrWhiteSpace(m.SetupExeDownloadUrl))
                {
                    string dlExe = Path.Combine(dlDir, "MyanmarSpellChecker-Setup.exe");
                    await SetupOnlineUpdater.DownloadVerifiedAssetAsync(m.SetupExeDownloadUrl, dlExe, m.SetupExeSha256, onProgress);
                    _lblProgress.Text = _isEnglish
                        ? $"SHA-256 verified! Installing v{releaseInfo.LatestVersion} & updating 'မိုး' launcher..."
                        : $"SHA-256 စစ်ဆေးပြီးပါပြီ! ဗားရှင်း v{releaseInfo.LatestVersion} ကို ထည့်သွင်းနေပါသည်...";
                    Application.DoEvents();
                    SetupInstallerEngine.ApplyStandaloneSetupExeUpdate(dlExe);
                }
                else if (!string.IsNullOrWhiteSpace(m.EasySetupZipDownloadUrl))
                {
                    string dlZip = Path.Combine(dlDir, "MyanmarSpellChecker-EasySetup-OfficeSuite.zip");
                    await SetupOnlineUpdater.DownloadVerifiedAssetAsync(m.EasySetupZipDownloadUrl, dlZip, m.EasySetupZipSha256, onProgress);
                    SetupInstallerEngine.ApplyLocalUpdateFile(dlZip, msg => { _lblProgress.Text = msg; Application.DoEvents(); });
                }
                else
                {
                    throw new InvalidOperationException("GitHub release does not contain a downloadable Setup.exe or EasySetup.zip asset.");
                }

                if (_chkDesktopMoeShortcut.Checked)
                {
                    SetupInstallerEngine.CreateMoeDesktopShortcut();
                }

                _pendingGitHubRelease = null;
                _progressBar.Value = 100;
                RefreshLocalizedText();

                string newVer = SetupInstallerEngine.GetCurrentlyInstalledVersion() ?? releaseInfo.LatestVersion;
                _lblProgress.Text = _isEnglish
                    ? $"✓ Updated to v{newVer} from GitHub! Ready in Word, Excel & PowerPoint."
                    : $"✓ GitHub မှ ဗားရှင်း v{newVer} သို့ အောင်မြင်စွာ အဆင့်မြှင့်ပြီးပါပြီ!";
                MessageBox.Show(this, _lblProgress.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                _lblProgress.Text = "Update error: " + ex.Message;
                MessageBox.Show(this, ex.Message, "GitHub Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _progressBar.Visible = false;
                try { Directory.Delete(dlDir, recursive: true); } catch { }
                SetButtonsEnabled(true);
            }
        }

        private void UpdateDownloadProgressUi(long downloadedBytes, long totalBytes, string version)
        {
            double doneMb = downloadedBytes / (1024.0 * 1024.0);
            if (totalBytes > 0)
            {
                int pct = (int)Math.Max(0, Math.Min(100, (downloadedBytes * 100L) / totalBytes));
                double totalMb = totalBytes / (1024.0 * 1024.0);
                _progressBar.Value = pct;
                _lblProgress.Text = _isEnglish
                    ? $"Downloading v{version} from GitHub... {doneMb:F1} / {totalMb:F1} MB ({pct}%)"
                    : $"GitHub မှ v{version} ဒေါင်းလုဒ်ဆွဲနေပါသည်... {doneMb:F1} / {totalMb:F1} MB ({pct}%)";
            }
            else
            {
                _lblProgress.Text = _isEnglish
                    ? $"Downloading v{version} from GitHub... {doneMb:F1} MB"
                    : $"GitHub မှ v{version} ဒေါင်းလုဒ်ဆွဲနေပါသည်... {doneMb:F1} MB";
            }
        }

        private void OnFromFileClick(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = _isEnglish
                    ? "Select MyanmarSpellChecker-Setup.exe, EasySetup.zip, or mlc_thatpon.db"
                    : "Setup.exe၊ EasySetup.zip သို့မဟုတ် mlc_thatpon.db ဖိုင်ကို ရွေးချယ်ပါ";
                ofd.Filter = "Myanmar Spell Checker Packages (*.exe;*.zip;*.db)|*.exe;*.zip;*.db|All Files (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK)
                    return;

                bool isDbOnly = string.Equals(Path.GetExtension(ofd.FileName), ".db", StringComparison.OrdinalIgnoreCase);
                if (!isDbOnly && !Program.PromptCloseOfficeIfRunning(_isEnglish))
                    return;

                SetButtonsEnabled(false);
                try
                {
                    SetupInstallerEngine.ApplyLocalUpdateFile(ofd.FileName, msg =>
                    {
                        _lblProgress.Text = msg;
                        Application.DoEvents();
                    });

                    if (_chkDesktopMoeShortcut.Checked)
                    {
                        SetupInstallerEngine.CreateMoeDesktopShortcut();
                    }

                    _pendingGitHubRelease = null;
                    RefreshLocalizedText();
                    string inst = SetupInstallerEngine.GetCurrentlyInstalledVersion() ?? SetupInstallerEngine.GetSetupVersion();
                    _lblProgress.Text = _isEnglish
                        ? $"✓ Applied local update package (v{inst}) cleanly!"
                        : $"✓ ဖိုင်မှတစ်ဆင့် ဗားရှင်း (v{inst}) ကို အောင်မြင်စွာ ထည့်သွင်းပြီးပါပြီ!";
                    MessageBox.Show(this, _lblProgress.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Local File Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    SetButtonsEnabled(true);
                }
            }
        }

        private async void OnInstallClick(object? sender, EventArgs e)
        {
            if (_pendingGitHubRelease != null && _pendingGitHubRelease.IsUpdateAvailable)
            {
                await DownloadAndApplyGitHubReleaseAsync(_pendingGitHubRelease);
                return;
            }

            if (!Program.PromptCloseOfficeIfRunning(_isEnglish))
                return;

            SetButtonsEnabled(false);
            try
            {
                SetupInstallerEngine.ExecuteInstallOrUpdate(
                    sourceOverrideDir: null,
                    statusCallback: msg =>
                    {
                        _lblProgress.Text = msg;
                        Application.DoEvents();
                    });

                if (_chkDesktopMoeShortcut.Checked)
                {
                    SetupInstallerEngine.CreateMoeDesktopShortcut();
                }

                _lblProgress.Text = _isEnglish
                    ? "✓ Completed! Open Word, Excel, or PowerPoint to use the plugin."
                    : "✓ အောင်မြင်စွာ ထည့်သွင်းပြီးပါပြီ။ Word၊ Excel သို့မဟုတ် PowerPoint ကို ဖွင့်နိုင်ပါပြီ။";
                RefreshLocalizedText();

                string doneMsg = _isEnglish
                    ? "✓ Myanmar Official Spell Checker & Dictionary Suite installed successfully!\r\n\r\n" +
                      "• Ready in Microsoft Word, Excel, and PowerPoint\r\n" +
                      "• Custom Words & Bookmarks safely preserved\r\n\r\n" +
                      "You can now open Word, Excel, or PowerPoint."
                    : "✓ မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း Add-in ကို အောင်မြင်စွာ ထည့်သွင်း/အဆင့်မြှင့်ပြီးပါပြီ!\r\n\r\n" +
                      "ယခု Microsoft Word၊ Excel သို့မဟုတ် PowerPoint ကို ဖွင့်၍ စတင်အသုံးပြုနိုင်ပါပြီ။";
                MessageBox.Show(this, doneMsg, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                MessageBox.Show(this, ex.Message, "Setup Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void OnRepairClick(object? sender, EventArgs e)
        {
            if (!Program.PromptCloseOfficeIfRunning(_isEnglish))
                return;

            SetButtonsEnabled(false);
            try
            {
                string appDir = SetupInstallerEngine.GetAppInstallDir();
                if (!File.Exists(Path.Combine(appDir, "MyanmarSpellChecker.WordAddIn.dll")))
                {
                    SetupInstallerEngine.ExecuteInstallOrUpdate(null, msg => { _lblProgress.Text = msg; Application.DoEvents(); });
                }
                else
                {
                    SetupInstallerEngine.RegisterComAndOfficeKeys(appDir);
                }

                if (_chkDesktopMoeShortcut.Checked)
                {
                    SetupInstallerEngine.CreateMoeDesktopShortcut();
                }

                _lblProgress.Text = _isEnglish
                    ? "✓ Office Ribbon & COM registration repaired for Word, Excel & PowerPoint."
                    : "✓ Word၊ Excel နှင့် PowerPoint အတွက် Office Ribbon ပြန်လည်ပြင်ဆင်ပြီးပါပြီ။";
                MessageBox.Show(this, _lblProgress.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Repair Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void OnUninstallClick(object? sender, EventArgs e)
        {
            string confirm = _isEnglish
                ? "Uninstall Myanmar Official Spell Checker from Word, Excel, and PowerPoint?\r\n(Your personal Custom Words and Bookmarks will be kept safe.)"
                : "Word၊ Excel နှင့် PowerPoint တို့မှ Add-in ကို ဖြုတ်သိမ်းလိုပါသလား?\r\n(သင်မှတ်သားထားသော ကိုယ်ပိုင်စာလုံးများ မပျက်စီးဘဲ ကျန်ရှိနေပါမည်။)";
            if (MessageBox.Show(this, confirm, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            if (!Program.PromptCloseOfficeIfRunning(_isEnglish))
                return;

            SetButtonsEnabled(false);
            try
            {
                SetupInstallerEngine.ExecuteUninstall();
                _pendingGitHubRelease = null;
                RefreshLocalizedText();
                _lblProgress.Text = _isEnglish ? "✓ Uninstalled cleanly." : "✓ အောင်မြင်စွာ ဖြုတ်သိမ်းပြီးပါပြီ။";
                MessageBox.Show(this, _lblProgress.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Uninstall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                SetButtonsEnabled(true);
            }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _btnInstall.Enabled = enabled;
            _btnCheckGitHub.Enabled = enabled;
            _btnFromFile.Enabled = enabled;
            _btnGuide.Enabled = enabled;
            _btnRepair.Enabled = enabled;
            _btnUninstall.Enabled = enabled;
            _btnClose.Enabled = enabled;
        }

        private static Button CreateActionButton(Point loc, Size size, Color back, Color fore, bool bold, float fontPt = 9.0f)
        {
            var btn = new Button
            {
                Location = loc,
                Size = size,
                BackColor = back,
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                Font = CreateMyanmarFont(fontPt, bold ? FontStyle.Bold : FontStyle.Regular),
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ControlPaint.Dark(back, 0.08f);
            return btn;
        }

        internal static Bitmap RenderSetupHeaderLogoBitmap(int size)
        {
            var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                float s = size / 512f;
                float pad = Math.Max(1f, 20f * s);
                float w = size - 2f * pad;
                float r = 108f * s;
                using (var gp = new System.Drawing.Drawing2D.GraphicsPath())
                {
                    float d = r * 2f;
                    gp.AddArc(pad, pad, d, d, 180, 90);
                    gp.AddArc(pad + w - d, pad, d, d, 270, 90);
                    gp.AddArc(pad + w - d, pad + w - d, d, d, 0, 90);
                    gp.AddArc(pad, pad + w - d, d, d, 90, 90);
                    gp.CloseFigure();
                    using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(
                        new PointF(0, 0), new PointF(size, size),
                        Color.FromArgb(11, 46, 107), Color.FromArgb(34, 116, 224)))
                    {
                        g.FillPath(bg, gp);
                    }
                    using (var rim = new Pen(Color.FromArgb(175, 190, 224, 255), Math.Max(1.2f, 8f * s)))
                    {
                        g.DrawPath(rim, gp);
                    }
                }

                using (var goldBrush = new SolidBrush(Color.FromArgb(240, 255, 218, 106)))
                {
                    g.FillRectangle(goldBrush, 128f * s, 366f * s, 256f * s, 24f * s);
                }
                using (var whitePage = new SolidBrush(Color.FromArgb(245, 255, 255, 255)))
                {
                    g.FillRectangle(whitePage, 140f * s, 350f * s, 232f * s, 20f * s);
                }

                // Prefer 'Myanmar Text' for emblem crest rendering so vertical -ိ / -ု marks have crisp separation
                FontFamily emblemFamily = CreateEmblemFontFamily();
                using (var moeFont = new Font(emblemFamily, Math.Max(8f, 134f * s), FontStyle.Bold, GraphicsUnit.Pixel))
                using (var sf = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString("\u1019\u102D\u102F\u1038", moeFont, Brushes.White, new PointF(256f * s, 206f * s), sf);
                }

                float cx = 388f * s, cy = 388f * s, ri = 74f * s;
                var chkRect = new RectangleF(cx - ri, cy - ri, ri * 2f, ri * 2f);
                using (var chkBrush = new System.Drawing.Drawing2D.LinearGradientBrush(chkRect, Color.FromArgb(18, 150, 79), Color.FromArgb(34, 197, 107), 45f))
                {
                    g.FillEllipse(chkBrush, chkRect);
                }
                using (var ringPen = new Pen(Color.White, Math.Max(1.2f, 10f * s)))
                {
                    g.DrawEllipse(ringPen, chkRect);
                }
                using (var tickPen = new Pen(Color.White, Math.Max(1.6f, 18f * s))
                {
                    StartCap = System.Drawing.Drawing2D.LineCap.Round,
                    EndCap = System.Drawing.Drawing2D.LineCap.Round,
                    LineJoin = System.Drawing.Drawing2D.LineJoin.Round
                })
                {
                    g.DrawLines(tickPen, new[]
                    {
                        new PointF(cx - 35f * s, cy + 2f * s),
                        new PointF(cx - 10f * s, cy + 26f * s),
                        new PointF(cx + 38f * s, cy - 25f * s)
                    });
                }
            }
            return bmp;
        }

        private static FontFamily CreateEmblemFontFamily()
        {
            foreach (string family in new[] { "Myanmar Text", "Pyidaungsu", "Segoe UI" })
            {
                try
                {
                    var f = new Font(family, 10f, FontStyle.Bold);
                    bool exact = string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase);
                    var fam = f.FontFamily;
                    f.Dispose();
                    if (exact)
                        return fam;
                }
                catch { }
            }
            return SystemFonts.MessageBoxFont.FontFamily;
        }

        internal static Font CreateMyanmarFont(float sizePt, FontStyle style)
        {
            foreach (string family in new[] { "Pyidaungsu", "Myanmar Text", "Segoe UI" })
            {
                try
                {
                    var f = new Font(family, sizePt, style);
                    if (string.Equals(f.Name, family, StringComparison.OrdinalIgnoreCase))
                        return f;
                    f.Dispose();
                }
                catch { }
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, sizePt, style);
        }
    }

    internal sealed class DeferredStagedApplyForm : Form
    {
        private readonly string _stagedDir;
        private readonly Label _lblInfo;
        private readonly System.Windows.Forms.Timer _pollTimer;
        private bool _applying;

        public int ExitCode { get; private set; }

        public DeferredStagedApplyForm(string stagedDir)
        {
            _stagedDir = stagedDir;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(490, 195);
            BackColor = Color.White;
            Text = "Myanmar Spell Checker — Ready to Finish Update";
            Font = SetupWizardForm.CreateMyanmarFont(9.3f, FontStyle.Regular);
            try
            {
                string selfLoc = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(selfLoc) && File.Exists(selfLoc))
                    Icon = Icon.ExtractAssociatedIcon(selfLoc);
            }
            catch { }

            _lblInfo = new Label
            {
                Location = new Point(18, 16),
                Size = new Size(454, 118),
                ForeColor = Color.FromArgb(28, 44, 68),
                UseMnemonic = false
            };

            var btnCancel = new Button
            {
                Text = "Cancel Deferred Update (နောက်မှ အဆင့်မြှင့်မည်)",
                Location = new Point(125, 144),
                Size = new Size(240, 34),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(242, 244, 248),
                ForeColor = Color.FromArgb(52, 62, 78),
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };
            btnCancel.Click += (s, e) =>
            {
                ExitCode = 1;
                Close();
            };

            Controls.Add(_lblInfo);
            Controls.Add(btnCancel);

            _pollTimer = new System.Windows.Forms.Timer { Interval = 800 };
            _pollTimer.Tick += OnPollTick;
            Shown += (s, e) =>
            {
                UpdateStatusLabel();
                _pollTimer.Start();
                OnPollTick(this, EventArgs.Empty);
            };
            FormClosed += (s, e) => _pollTimer.Dispose();
        }

        private void UpdateStatusLabel()
        {
            Program.HasRunningOfficeProcesses(out string runningNames);
            string apps = string.IsNullOrEmpty(runningNames) ? "Office" : runningNames;
            _lblInfo.Text =
                $"Update verified and staged! Waiting for Microsoft {apps} to close...\r\n" +
                "Please save your open documents and close Word, Excel & PowerPoint — the update will apply automatically in 2 seconds.\r\n\r\n" +
                $"(လက်ရှိဖွင့်ထားသော {apps} ဖိုင်များကို Save လုပ်၍ ပိတ်ပေးပါ — ပိတ်ပြီးသည်နှင့် စက္ကန့်ပိုင်းအတွင်း အလိုအလျောက် အဆင့်မြှင့်ပေးပါမည်။)";
        }

        private void OnPollTick(object? sender, EventArgs e)
        {
            if (_applying) return;
            if (Program.HasRunningOfficeProcesses(out _))
            {
                UpdateStatusLabel();
                return;
            }

            _applying = true;
            _pollTimer.Stop();
            try
            {
                _lblInfo.Text = "Applying verified update to %LocalAppData%\\MyanmarSpellChecker\\app...\r\n(ဗားရှင်းအသစ်ကို ထည့်သွင်းနေပါသည်...)";
                Application.DoEvents();

                SetupInstallerEngine.ExecuteInstallOrUpdate(_stagedDir, null);
                ExitCode = 0;

                string ver = SetupInstallerEngine.GetCurrentlyInstalledVersion() ?? SetupInstallerEngine.GetSetupVersion();
                MessageBox.Show(
                    this,
                    $"✓ Updated to Myanmar Official Spell Checker v{ver}!\r\nYou can reopen Word, Excel, or PowerPoint now.\r\n\r\n(ဗားရှင်း v{ver} သို့ အောင်မြင်စွာ အဆင့်မြှင့်ပြီးပါပြီ။ Word၊ Excel သို့မဟုတ် PowerPoint ကို ပြန်ဖွင့်နိုင်ပါပြီ။)",
                    "Myanmar Official Spell Checker — Update Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                Close();
            }
            catch (Exception ex)
            {
                ExitCode = 1;
                MessageBox.Show(this, "Update apply failed: " + ex.Message, "Update Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }
    }
}
