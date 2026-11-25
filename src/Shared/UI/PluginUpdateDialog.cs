// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ), Department of Geology, Kyaukse University, Kyaukse, Myanmar.

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Updates;

#if HOST_EXCEL
namespace MyanmarSpellChecker.ExcelAddIn.UI
#elif HOST_POWERPOINT
namespace MyanmarSpellChecker.PowerPointAddIn.UI
#else
namespace MyanmarSpellChecker.WordAddIn.UI
#endif
{
    /// <summary>
    /// High-DPI responsive, privacy-first Bilingual Update Manager shared across Microsoft Word, Excel, and PowerPoint.
    /// Zero background telemetry: connects to GitHub Releases ONLY when the user clicks [Check Online],
    /// and supports 100% air-gapped USB/LAN updates via [Update from Local File (.exe / .zip / .db)].
    /// </summary>
    public sealed class PluginUpdateDialog : Form
    {
#if HOST_EXCEL
        private static readonly Color HostFormBack = Color.FromArgb(244, 248, 246);
        private static readonly Color HostAccent = Color.FromArgb(16, 124, 65);
        private static readonly Color HostHeaderFore = Color.FromArgb(16, 88, 48);
#elif HOST_POWERPOINT
        private static readonly Color HostFormBack = Color.FromArgb(250, 246, 244);
        private static readonly Color HostAccent = Color.FromArgb(183, 71, 42);
        private static readonly Color HostHeaderFore = Color.FromArgb(128, 42, 22);
#else
        private static readonly Color HostFormBack = Color.FromArgb(244, 247, 252);
        private static readonly Color HostAccent = Color.FromArgb(24, 90, 188);
        private static readonly Color HostHeaderFore = Color.FromArgb(18, 48, 102);
#endif

        private readonly int _dpi;
        private readonly string? _activeDbPath;
        private readonly string? _activeDbKey;
        private readonly Action<MyanmarTrie, string>? _hotSwapCallback;
        private readonly Action? _preferencesChangedCallback;

        private readonly Label _lblInstalledBadge;
        private readonly Label _lblPrivacyNote;
        private readonly Label _lblStatusTitle;
        private readonly TextBox _txtReleaseNotes;
        private readonly ProgressBar _progressBar;
        private readonly CheckBox _chkWeeklyAutoCheck;
        private readonly Button _btnCheckOnline;
        private readonly Button _btnApplyOnlineUpdate;
        private readonly Button _btnLocalFile;
        private readonly Button _btnOpenGitHub;
        private readonly Button _btnClose;

        private UpdateCheckResult? _lastCheckResult;
        private CancellationTokenSource? _cts;

        private int S(int logicalPx) => UiFontCache.ScaleLogicalPxForDpi(_dpi, logicalPx);

        public PluginUpdateDialog(
            int loadedEntryCount,
            string? activeDbPath,
            string? activeDbKey,
            Action<MyanmarTrie, string>? hotSwapCallback,
            Action? preferencesChangedCallback = null)
        {
            _activeDbPath = activeDbPath;
            _activeDbKey = activeDbKey;
            _hotSwapCallback = hotSwapCallback;
            _preferencesChangedCallback = preferencesChangedCallback;
            _dpi = UiFontCache.GetEffectiveDpi(this);

            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = HostFormBack;
            Font = UiFontCache.GetMyanmar(9.3F, FontStyle.Regular);
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            Text = UiLocalizer.Pick(
                "Myanmar Spell Checker — Software & Dictionary Updates",
                "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း — ဗားရှင်းနှင့် အဘိဓာန် အဆင့်မြှင့်တင်ရေး");

            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min(S(590), Math.Max(S(440), workArea.Width - S(60)));
            int h = Math.Min(S(480), Math.Max(S(380), workArea.Height - S(80)));
            ClientSize = new Size(w, h);
            MinimumSize = new Size(S(430), S(360));

            string curVer = PluginUpdateService.GetCurrentInstalledVersion();
            var prefs = PluginUpdateService.LoadPreferences();

            var footerBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = S(52),
                BackColor = Color.White,
                Padding = new Padding(S(14), S(8), S(14), S(8))
            };

            var footerFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };

            _btnCheckOnline = CreateStyledButton(
                UiLocalizer.Pick("↻ Check Online (GitHub)", "↻ အွန်လိုင်းမှ စစ်ဆေးမည်"),
                HostAccent, Color.White, bold: true);
            _btnCheckOnline.Click += OnCheckOnlineClick;

            _btnApplyOnlineUpdate = CreateStyledButton(
                UiLocalizer.Pick("✓ Download & Install Update", "✓ ဒေါင်းလုဒ်ဆွဲ၍ အဆင့်မြှင့်မည်"),
                Color.FromArgb(18, 122, 60), Color.White, bold: true);
            _btnApplyOnlineUpdate.Visible = false;
            _btnApplyOnlineUpdate.Click += OnApplyOnlineUpdateClick;

            _btnLocalFile = CreateStyledButton(
                UiLocalizer.Pick("+ Update from File (USB)", "+ ဖိုင်မှတစ်ဆင့် အဆင့်မြှင့်မည်"),
                Color.FromArgb(234, 242, 255), Color.FromArgb(18, 72, 158), bold: true);
            _btnLocalFile.Click += OnUpdateFromLocalFileClick;

            _btnOpenGitHub = CreateStyledButton(
                UiLocalizer.Pick("Releases Web", "GitHub စာမျက်နှာ"),
                Color.FromArgb(242, 244, 248), Color.FromArgb(44, 56, 74), bold: false);
            _btnOpenGitHub.Click += (s, e) =>
            {
                string url = _lastCheckResult?.ReleasePageUrl ?? PluginUpdateService.ReleasesWebUrl;
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            };

            _btnClose = CreateStyledButton(
                UiLocalizer.Pick("Close", "ပိတ်မည်"),
                Color.FromArgb(242, 244, 248), Color.FromArgb(52, 62, 78), bold: false);
            _btnClose.Click += (s, e) => Close();

            footerFlow.Controls.Add(_btnCheckOnline);
            footerFlow.Controls.Add(_btnApplyOnlineUpdate);
            footerFlow.Controls.Add(_btnLocalFile);
            footerFlow.Controls.Add(_btnOpenGitHub);
            footerFlow.Controls.Add(_btnClose);
            footerBar.Controls.Add(footerFlow);

            var mainCard = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(S(16), S(14), S(16), S(10)),
                BackColor = HostFormBack
            };
            mainCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            mainCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainCard.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var lblHeader = new Label
            {
                Text = UiLocalizer.Pick(
                    "Software & Offline Dictionary Update Center (Word • Excel • PowerPoint)",
                    "ဗားရှင်းအသစ်နှင့် အော့ဖ်လိုင်းအဘိဓာန် အဆင့်မြှင့်တင်ရေးစင်တာ"),
                Font = UiFontCache.GetMyanmar(10.5F, FontStyle.Bold),
                ForeColor = HostHeaderFore,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, S(6))
            };

            _lblInstalledBadge = new Label
            {
                Text = UiLocalizer.Pick(
                    $"✓ Installed Version: v{curVer}   |   Loaded Lexicon & Encyclopedia: {loadedEntryCount:N0} entries",
                    $"✓ လက်ရှိဗားရှင်း - v{curVer}   |   သတ်ပုံကျမ်းနှင့် အဘိဓာန် စုစုပေါင်း {loadedEntryCount:N0} ပုဒ်"),
                Font = UiFontCache.GetMyanmar(9.1F, FontStyle.Bold),
                ForeColor = Color.FromArgb(16, 98, 48),
                BackColor = Color.FromArgb(232, 247, 237),
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(S(10), S(6), S(10), S(6)),
                Margin = new Padding(0, 0, 0, S(6))
            };

            _lblPrivacyNote = new Label
            {
                Text = UiLocalizer.Pick(
                    "Privacy Guarantee: 100% Zero Document Telemetry. Internet is never contacted unless you click 'Check Online' or opt into weekly checks below. Air-gapped PCs can update offline via USB (.exe / .zip / .db).",
                    "လုံခြုံရေးအာမခံချက် - သင်၏ စာတမ်းအချက်အလက်များကို အင်တာနက်သို့ လုံးဝပေးပို့ခြင်း မရှိပါ။ အင်တာနက်မရှိသော ကွန်ပျူတာများတွင် USB ဖိုင် (.exe / .zip / .db) ဖြင့် တိုက်ရိုက် အဆင့်မြှင့်နိုင်ပါသည်။"),
                Font = UiFontCache.GetMyanmar(8.6F, FontStyle.Regular),
                ForeColor = Color.FromArgb(54, 66, 84),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Padding = new Padding(S(9), S(6), S(9), S(6)),
                Margin = new Padding(0, 0, 0, S(8))
            };

            var detailsPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                Padding = new Padding(S(10)),
                Margin = new Padding(0, 0, 0, S(6))
            };

            _lblStatusTitle = new Label
            {
                Dock = DockStyle.Top,
                Height = S(26),
                Font = UiFontCache.GetMyanmar(9.3F, FontStyle.Bold),
                ForeColor = HostHeaderFore,
                Text = UiLocalizer.Pick(
                    "Ready — Click 'Check Online (GitHub)' or '+ Update from File (USB)'.",
                    "အသင့်ရှိပါသည် — 'အွန်လိုင်းမှ စစ်ဆေးမည်' သို့မဟုတ် '+ ဖိုင်မှတစ်ဆင့် အဆင့်မြှင့်မည်' ကို နှိပ်ပါ။")
            };

            _txtReleaseNotes = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.None,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(36, 48, 66),
                Font = UiFontCache.GetMyanmar(9.0F, FontStyle.Regular),
                Text = UiLocalizer.Pick(
                    "How Two-Speed Smart Updates Work:\r\n" +
                    "  1. Dictionary-Only Updates (mlc_thatpon.db): Applied live in < 2 seconds with ZERO Office restart!\r\n" +
                    "  2. Full Suite Updates (MyanmarSpellChecker-Setup.exe / .zip): Verified with SHA-256 and staged safely; automatically finishes in 2 seconds as soon as you save & close Word, Excel, and PowerPoint.\r\n" +
                    "  3. Your Custom Words, Bookmarks, and Style Preferences are 100% preserved.",
                    "အဆင့်မြှင့်တင်မှု နည်းလမ်း (၂) မျိုး -\r\n" +
                    "  ၁။ အဘိဓာန်သီးသန့် အဆင့်မြှင့်ခြင်း (mlc_thatpon.db) - Office ပိတ်ရန်မလိုဘဲ ၂ စက္ကန့်အတွင်း တိုက်ရိုက် အသက်ဝင်ပါသည်။\r\n" +
                    "  ၂။ စနစ်အပြည့်အစုံ အဆင့်မြှင့်ခြင်း (Setup.exe / .zip) - SHA-256 စစ်ဆေးပြီး အသင့်ပြင်ထားပေးကာ Word၊ Excel နှင့် PowerPoint ပိတ်လိုက်သည်နှင့် အလိုအလျောက် အဆင့်မြှင့်ပေးပါသည်။\r\n" +
                    "  ၃။ ကိုယ်ပိုင်မှတ်သားစာလုံးများနှင့် Bookmarks များ ၁၀၀% မပျက်စီးဘဲ ကျန်ရှိနေပါမည်။")
            };

            detailsPanel.Controls.Add(_txtReleaseNotes);
            detailsPanel.Controls.Add(_lblStatusTitle);

            _progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = S(16),
                Visible = false,
                Margin = new Padding(0, 0, 0, S(6))
            };

            _chkWeeklyAutoCheck = new CheckBox
            {
                AutoSize = true,
                Checked = prefs.WeeklyAutoCheckEnabled,
                Font = UiFontCache.GetMyanmar(8.8F, FontStyle.Regular),
                ForeColor = Color.FromArgb(44, 56, 74),
                Text = UiLocalizer.Pick(
                    "Check GitHub Releases once a week when online (opt-in; sends zero user data)",
                    "အင်တာနက်ရှိချိန် တစ်ပတ်တစ်ကြိမ် ဗားရှင်းအသစ် အလိုအလျောက် စစ်ဆေးမည် (ကိုယ်ရေးအချက်အလက် လုံးဝမပို့ပါ)")
            };
            _chkWeeklyAutoCheck.CheckedChanged += (s, e) =>
            {
                var p = PluginUpdateService.LoadPreferences();
                p.WeeklyAutoCheckEnabled = _chkWeeklyAutoCheck.Checked;
                PluginUpdateService.SavePreferences(p);
                _preferencesChangedCallback?.Invoke();
            };

            mainCard.Controls.Add(lblHeader, 0, 0);
            mainCard.Controls.Add(_lblInstalledBadge, 0, 1);
            mainCard.Controls.Add(_lblPrivacyNote, 0, 2);
            mainCard.Controls.Add(detailsPanel, 0, 3);
            mainCard.Controls.Add(_progressBar, 0, 4);
            mainCard.Controls.Add(_chkWeeklyAutoCheck, 0, 5);

            Controls.Add(mainCard);
            Controls.Add(footerBar);

            FormClosed += (s, e) =>
            {
                try { _cts?.Cancel(); _cts?.Dispose(); } catch { }
            };
        }

        private async void OnCheckOnlineClick(object? sender, EventArgs e)
        {
            SetBusy(true, indeterminate: true);
            _btnApplyOnlineUpdate.Visible = false;
            _lblStatusTitle.Text = UiLocalizer.Pick(
                "Checking GitHub Releases (https://github.com/Hashyz/MyanmarSpellChecker)...",
                "GitHub Releases တွင် ဗားရှင်းအသစ် စစ်ဆေးနေပါသည်...");

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            var res = await PluginUpdateService.CheckGitHubReleaseAsync(_cts.Token);
            if (IsDisposed) return;

            _lastCheckResult = res;
            SetBusy(false, indeterminate: false);
            _preferencesChangedCallback?.Invoke();

            if (!res.Success)
            {
                _lblStatusTitle.ForeColor = Color.FromArgb(172, 42, 36);
                _lblStatusTitle.Text = UiLocalizer.Pick("Connection Notice", "ချိတ်ဆက်မှု အသိပေးချက်");
                _txtReleaseNotes.Text = UiLocalizer.Pick(res.ErrorMessageEn, res.ErrorMessageMm);
                return;
            }

            if (!res.IsUpdateAvailable)
            {
                _lblStatusTitle.ForeColor = Color.FromArgb(16, 108, 52);
                _lblStatusTitle.Text = UiLocalizer.Pick(
                    $"✓ You are running the latest version (v{res.CurrentVersion})!",
                    $"✓ သင်သည် နောက်ဆုံးထွက်ဗားရှင်း (v{res.CurrentVersion}) ကို အသုံးပြုနေပါသည်!");
                _txtReleaseNotes.Text = UiLocalizer.Pick(
                    $"Installed Version: v{res.CurrentVersion}\r\nLatest GitHub Release: v{res.LatestVersion}\r\n\r\nNo update is needed right now.",
                    $"လက်ရှိဗားရှင်း - v{res.CurrentVersion}\r\nနောက်ဆုံးထွက်ဗားရှင်း - v{res.LatestVersion}\r\n\r\nလောလောဆယ် အဆင့်မြှင့်ရန် မလိုအပ်ပါ။");
                return;
            }

            _lblStatusTitle.ForeColor = HostAccent;
            string modeBadge = res.IsDictionaryOnlyUpdate
                ? UiLocalizer.Pick("[Instant Dictionary Hot-Swap — Zero Restart]", "[အဘိဓာန်သီးသန့် — Office ပိတ်ရန်မလိုပါ]")
                : UiLocalizer.Pick("[Full Suite Update]", "[စနစ်အပြည့်အစုံ အဆင့်မြှင့်ခြင်း]");
            _lblStatusTitle.Text = UiLocalizer.Pick(
                $"↑ New Version Available: v{res.LatestVersion} {modeBadge}",
                $"↑ ဗားရှင်းအသစ် ထွက်ရှိနေပါပြီ - v{res.LatestVersion} {modeBadge}");

            string notes = UiLocalizer.IsEnglish
                ? (res.Manifest?.ReleaseNotesEn ?? string.Empty)
                : (res.Manifest?.ReleaseNotesMm ?? string.Empty);
            _txtReleaseNotes.Text =
                $"Current: v{res.CurrentVersion}  ->  Latest: v{res.LatestVersion}\r\n" +
                (res.SignatureVerified ? "Security: ✓ RSA-2048 & SHA-256 Verified Manifest\r\n\r\n" : "Security: ✓ TLS 1.2 GitHub HTTPS + SHA-256 Verified\r\n\r\n") +
                notes;

            _btnApplyOnlineUpdate.Text = res.IsDictionaryOnlyUpdate
                ? UiLocalizer.Pick("✓ Hot-Swap Dictionary Now", "✓ အဘိဓာန် ချက်ချင်းအဆင့်မြှင့်မည်")
                : UiLocalizer.Pick($"✓ Update to v{res.LatestVersion}", $"✓ v{res.LatestVersion} သို့ အဆင့်မြှင့်မည်");
            _btnApplyOnlineUpdate.Visible = true;
        }

        private async void OnApplyOnlineUpdateClick(object? sender, EventArgs e)
        {
            if (_lastCheckResult?.Manifest == null)
                return;

            var m = _lastCheckResult.Manifest;
            SetBusy(true, indeterminate: false);
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            try
            {
                if (_lastCheckResult.IsDictionaryOnlyUpdate && !string.IsNullOrWhiteSpace(m.DatabaseDownloadUrl))
                {
                    string tempDb = Path.Combine(Path.GetTempPath(), "msc_dl_" + Guid.NewGuid().ToString("N") + ".db");
                    _lblStatusTitle.Text = UiLocalizer.Pick("Downloading verified mlc_thatpon.db...", "mlc_thatpon.db အဘိဓာန်အသစ် ဒေါင်းလုဒ်ဆွဲနေပါသည်...");
                    await PluginUpdateService.DownloadVerifiedAssetAsync(
                        m.DatabaseDownloadUrl,
                        tempDb,
                        m.DatabaseSha256,
                        UpdateProgressUi,
                        _cts.Token);

                    int words = PluginUpdateService.ApplyDictionaryHotSwap(tempDb, _activeDbPath, _activeDbKey, _hotSwapCallback);
                    try { File.Delete(tempDb); } catch { }

                    SetBusy(false, indeterminate: false);
                    _btnApplyOnlineUpdate.Visible = false;
                    _lblStatusTitle.ForeColor = Color.FromArgb(16, 108, 52);
                    _lblStatusTitle.Text = UiLocalizer.Pick(
                        $"✓ Live Dictionary Hot-Swapped ({words:N0} headwords) — Zero Restart Needed!",
                        $"✓ အဘိဓာန်အသစ် ({words:N0} ပုဒ်) တိုက်ရိုက် အဆင့်မြှင့်ပြီးပါပြီ (Office ပိတ်ရန်မလိုပါ)!");
                    MessageBox.Show(this, _lblStatusTitle.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                bool hasSetupUrl = !string.IsNullOrWhiteSpace(m.SetupExeDownloadUrl);
                string dlUrl = hasSetupUrl ? m.SetupExeDownloadUrl : m.EasySetupZipDownloadUrl;
                string expectedSha = hasSetupUrl ? m.SetupExeSha256 : m.EasySetupZipSha256;
                string ext = hasSetupUrl ? ".exe" : ".zip";
                LocalUpdatePackageKind pkgKind = hasSetupUrl
                    ? LocalUpdatePackageKind.SingleFileSetupExe
                    : LocalUpdatePackageKind.EasySetupZipBundle;

                if (string.IsNullOrWhiteSpace(dlUrl))
                {
                    SetBusy(false, indeterminate: false);
                    Process.Start(new ProcessStartInfo(_lastCheckResult.ReleasePageUrl) { UseShellExecute = true });
                    return;
                }

                string tempPkg = Path.Combine(Path.GetTempPath(), "MyanmarSpellChecker-Update-" + m.Version + ext);
                _lblStatusTitle.Text = UiLocalizer.Pick(
                    $"Downloading v{_lastCheckResult.LatestVersion} installer...",
                    $"ဗားရှင်း v{_lastCheckResult.LatestVersion} ကို ဒေါင်းလုဒ်ဆွဲနေပါသည်...");

                await PluginUpdateService.DownloadVerifiedAssetAsync(dlUrl, tempPkg, expectedSha, UpdateProgressUi, _cts.Token);
                PluginUpdateService.StageFullUpdateAndLaunchDeferredApplier(tempPkg, pkgKind);

                SetBusy(false, indeterminate: false);
                _btnApplyOnlineUpdate.Visible = false;
                _lblStatusTitle.ForeColor = Color.FromArgb(16, 108, 52);
                _lblStatusTitle.Text = UiLocalizer.Pick(
                    "✓ Verified & Staged! Save your work and close Office to finish in 2 seconds.",
                    "✓ စစ်ဆေးအသင့်ပြင်ပြီးပါပြီ! လက်ရှိဖိုင်များကို Save လုပ်၍ Office ပိတ်လိုက်ပါက ၂ စက္ကန့်အတွင်း အဆင့်မြှင့်ပေးပါမည်။");
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    SetBusy(false, indeterminate: false);
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnUpdateFromLocalFileClick(object? sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = UiLocalizer.Pick(
                    "Select MyanmarSpellChecker-Setup.exe, EasySetup.zip, or mlc_thatpon.db",
                    "MyanmarSpellChecker-Setup.exe၊ EasySetup.zip သို့မဟုတ် mlc_thatpon.db ဖိုင်ကို ရွေးချယ်ပါ");
                ofd.Filter = "Update Packages (*.exe;*.zip;*.db)|*.exe;*.zip;*.db|Setup Installer (*.exe)|*.exe|EasySetup Archive (*.zip)|*.zip|Dictionary Database (mlc_thatpon.db)|*.db";

                if (ofd.ShowDialog(this) != DialogResult.OK)
                    return;

                var inspect = PluginUpdateService.InspectLocalUpdatePackage(ofd.FileName, _activeDbKey);
                if (!inspect.IsValid)
                {
                    MessageBox.Show(
                        this,
                        UiLocalizer.Pick(inspect.ErrorMessageEn, inspect.ErrorMessageMm),
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                try
                {
                    if (inspect.Kind == LocalUpdatePackageKind.DictionaryDatabaseOnly)
                    {
                        int words = PluginUpdateService.ApplyDictionaryHotSwap(ofd.FileName, _activeDbPath, _activeDbKey, _hotSwapCallback);
                        _lblInstalledBadge.Text = UiLocalizer.Pick(
                            $"✓ Installed Version: v{PluginUpdateService.GetCurrentInstalledVersion()}   |   Hot-Swapped Dictionary: {words:N0} headwords",
                            $"✓ လက်ရှိဗားရှင်း - v{PluginUpdateService.GetCurrentInstalledVersion()}   |   အဘိဓာန်အသစ် {words:N0} ပုဒ် တိုက်ရိုက်အသက်ဝင်ပြီး");
                        _lblStatusTitle.ForeColor = Color.FromArgb(16, 108, 52);
                        _lblStatusTitle.Text = UiLocalizer.Pick(
                            $"✓ Dictionary Hot-Swapped Live ({words:N0} headwords, Zero Office Restart)!",
                            $"✓ အဘိဓာန်အသစ် ({words:N0} ပုဒ်) Office ပိတ်ရန်မလိုဘဲ တိုက်ရိုက် အဆင့်မြှင့်ပြီးပါပြီ!");
                        MessageBox.Show(this, _lblStatusTitle.Text, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    string confirm = UiLocalizer.Pick(
                        $"Verified local update package:\r\n• File: {Path.GetFileName(ofd.FileName)}\r\n• Version: v{inspect.PackageVersion}\r\n• SHA-256: {inspect.FileSha256.Substring(0, 16)}...\r\n\r\nStage this update now? It will apply automatically as soon as you save and close Word, Excel, and PowerPoint.",
                        $"ဖိုင်စစ်ဆေးပြီး အတည်ပြုချက် -\r\n• ဖိုင်အမည် - {Path.GetFileName(ofd.FileName)}\r\n• ဗားရှင်း - v{inspect.PackageVersion}\r\n\r\nယခု အသင့်ပြင်ထားမည်လား? Word၊ Excel နှင့် PowerPoint ပိတ်လိုက်သည်နှင့် အလိုအလျောက် အဆင့်မြှင့်ပေးပါမည်။");
                    if (MessageBox.Show(this, confirm, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;

                    PluginUpdateService.StageFullUpdateAndLaunchDeferredApplier(ofd.FileName, inspect.Kind);
                    _lblStatusTitle.ForeColor = Color.FromArgb(16, 108, 52);
                    _lblStatusTitle.Text = UiLocalizer.Pick(
                        $"✓ Staged v{inspect.PackageVersion}! Save your work and close Office to finish.",
                        $"✓ ဗားရှင်း v{inspect.PackageVersion} အသင့်ပြင်ပြီးပါပြီ! ဖိုင်များကို Save လုပ်၍ Office ပိတ်ပေးပါ။");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void UpdateProgressUi(long downloaded, long total)
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => UpdateProgressUi(downloaded, total))); } catch { }
                return;
            }

            if (total > 0)
            {
                _progressBar.Style = ProgressBarStyle.Continuous;
                int pct = (int)Math.Min(100L, (downloaded * 100L) / total);
                _progressBar.Value = Math.Max(0, pct);
                double dlMb = downloaded / (1024.0 * 1024.0);
                double totMb = total / (1024.0 * 1024.0);
                _lblStatusTitle.Text = UiLocalizer.Pick(
                    $"Downloading verified package: {dlMb:F1} MB / {totMb:F1} MB ({pct}%)",
                    $"ဒေါင်းလုဒ်ဆွဲနေပါသည် - {dlMb:F1} MB / {totMb:F1} MB ({pct}%)");
            }
            else
            {
                _progressBar.Style = ProgressBarStyle.Marquee;
            }
        }

        private void SetBusy(bool busy, bool indeterminate)
        {
            _btnCheckOnline.Enabled = !busy;
            _btnApplyOnlineUpdate.Enabled = !busy;
            _btnLocalFile.Enabled = !busy;
            _progressBar.Visible = busy;
            _progressBar.Style = indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            if (!busy) _progressBar.Value = 0;
        }

        private Button CreateStyledButton(string text, Color back, Color fore, bool bold)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = back,
                ForeColor = fore,
                FlatStyle = FlatStyle.Flat,
                Font = UiFontCache.GetMyanmar(9.0F, bold ? FontStyle.Bold : FontStyle.Regular),
                Padding = new Padding(S(8), S(4), S(8), S(4)),
                Margin = new Padding(0, 0, S(6), 0),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = ControlPaint.Dark(back, 0.08f);
            return btn;
        }
    }
}
