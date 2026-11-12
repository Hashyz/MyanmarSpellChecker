// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Extensibility;
using Microsoft.Win32;
using MyanmarSpellChecker.Core.Dictionary;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Nlp;
using MyanmarSpellChecker.Core.Official;
using MyanmarSpellChecker.Core.Security;
using MyanmarSpellChecker.WordAddIn.Interop;
using MyanmarSpellChecker.WordAddIn.UI;
using Office = Microsoft.Office.Core;
using Word = Microsoft.Office.Interop.Word;

namespace MyanmarSpellChecker.WordAddIn
{
    /// <summary>
    /// Enterprise VSTO / COM Add-in Entry Point for Microsoft Word (Desktop Windows).
    /// Supports Multi-Document Window Synchronization, StoryRanges (Body + Tables + Headers/Footers),
    /// Live Word.Range Tracking, Original Formatting Preservation, and Instant သတ်ပုံကျမ်း Dictionary Lookup.
    /// </summary>
    [ComVisible(true)]
    [Guid("8F5A9E42-6D11-4F3B-9C80-7B12E5D94A11")]
    [ProgId("MyanmarSpellChecker.WordAddIn")]
    public class ThisAddIn : IDTExtensibility2, Office.IRibbonExtensibility, Office.ICustomTaskPaneConsumer
    {
        public const string DefaultSqlCipherKey = "MM-GOV-THATPON-SQLCIPHER-2026!";

        public const string DefaultVendorPublicKeyXml =
            "<RSAKeyValue><Modulus>w6uP9e7V5r2X1a9K0l8m3N4b5V6c7X8z9A0s1D2f3G4h5J6k7L8p9O0i1U2y3T4r5E6w7Q8a9S0d1F2g3H4j5K6l7Z8x9C0v1B2n3M4q5W6e7R8t9Y0u1I2o3P4a5S6d7F8g9H0j1K2l3Z4x5C6v7B8n9M0q1W2e3R4t5Y6u7I8o9P0a1S2d3F4g5H6j7K8l9Z0x1C2v3B4n5M6q7W8e9R0t1Y2u3I4o5P6a7S8d9F0g1H2j3K4l5Z6x7C8v9B0n1M2q3W4e5R6t7Y8u9I0o1P2a3S4d5F6g7H8j9K0l1Z2x3C4v5B6n7M8q9W0e1R2t3Y4u5I6o7P8a9S0d1F2g3H4j5K6l7Z8==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private static readonly object s_instanceGate = new object();
        private static bool s_primaryInstanceActive;

        private bool _isPrimaryInstance;
        private Word.Application? _wordApp;
        private Office.IRibbonUI? _ribbonUi;
        private Office.ICTPFactory? _ctpFactory;
        private Control? _uiMarshaller;
        private Timer? _startupTaskPaneTimer;
        private int _startupTaskPaneRetries;
        private MyanmarProofingEngine? _proofingEngine;
        private ProofingStyleMode _pendingStyleMode = ProofingStyleMode.GeneralStandard;
        private bool _pendingLiveAutoScan = true;
        private int _lastCheckedLangDocPos = -1;
        private SafeUnderlineManager? _underlineManager;
        private StaDocumentScanner? _documentScanner;
        private WordContextMenuHandler? _contextMenuHandler;
        private readonly WordDocumentToolkitService _toolkitService = new WordDocumentToolkitService();

        // Per-Window Task Pane map (keyed by Word.Window.Hwnd) for Multi-Document SDI support
        private readonly Dictionary<int, (Office.CustomTaskPane Pane, ProofingTaskPaneControl Control)> _windowTaskPanes =
            new Dictionary<int, (Office.CustomTaskPane Pane, ProofingTaskPaneControl Control)>();

        private string _appDataFolder = string.Empty;
        private string _styleModeConfigPath = string.Empty;
        private string _customDictFilePath = string.Empty;
        private string _licenseFilePath = string.Empty;
        private string _databaseFilePath = string.Empty;
        private string _activeDatabaseKey = string.Empty;
        private DateTime _loadedDbLastWriteUtc = DateTime.MinValue;
        private int _dbHotReloadInProgress;

        static ThisAddIn()
        {
            // Ensure MS Word (WINWORD.EXE) resolves dependent assemblies from the Add-in's installation directory
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                try
                {
                    string? addInDir = Path.GetDirectoryName(typeof(ThisAddIn).Assembly.Location);
                    if (string.IsNullOrEmpty(addInDir))
                        return null;

                    var asmName = new System.Reflection.AssemblyName(args.Name);
                    string candidatePath = Path.Combine(addInDir, asmName.Name + ".dll");
                    if (File.Exists(candidatePath))
                    {
                        return System.Reflection.Assembly.LoadFrom(candidatePath);
                    }
                }
                catch
                {
                    // Ignore
                }
                return null;
            };
        }

        public void OnConnection(object application, ext_ConnectMode connectMode, object addInInst, ref Array custom)
        {
            // Guard against duplicate Office Add-in instantiation if both HKLM and HKCU Addins keys exist on a machine
            lock (s_instanceGate)
            {
                if (s_primaryInstanceActive)
                {
                    _isPrimaryInstance = false;
                    return;
                }
                s_primaryInstanceActive = true;
                _isPrimaryInstance = true;
            }

            _wordApp = application as Word.Application;
            if (_wordApp == null)
                return;

            AddInDiagnostics.SafeExecute("OnConnection", () =>
            {
                _uiMarshaller = new Control();
                IntPtr _ = _uiMarshaller.Handle;

                InitializePathsAndStartAsyncEngineLoad();
                HookWordEvents();
            });
        }

        private void InitializePathsAndStartAsyncEngineLoad()
        {
            string? installDir = Path.GetDirectoryName(typeof(ThisAddIn).Assembly.Location);
            string programDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "MyanmarSpellChecker");
            string localAppDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MyanmarSpellChecker");

            _appDataFolder = Directory.Exists(programDataDir) ? programDataDir : localAppDataDir;
            Directory.CreateDirectory(_appDataFolder);
            Directory.CreateDirectory(localAppDataDir);
            AddInDiagnostics.ConfigureLogDirectory(localAppDataDir);

            _styleModeConfigPath = Path.Combine(localAppDataDir, "style_mode.txt");
            _customDictFilePath = Path.Combine(localAppDataDir, "custom_dictionary.txt");
            string ministryCustomDictPath = Path.Combine(programDataDir, "ministry_custom_dictionary.txt");

            // Prefer license.lic and mlc_thatpon.db in C:\Program Files\MyanmarSpellChecker or %ProgramData%\MyanmarSpellChecker
            string installLic = !string.IsNullOrEmpty(installDir) ? Path.Combine(installDir, "license.lic") : string.Empty;
            string progDataLic = Path.Combine(programDataDir, "license.lic");
            _licenseFilePath = File.Exists(installLic)
                ? installLic
                : (File.Exists(progDataLic) ? progDataLic : Path.Combine(_appDataFolder, "license.lic"));

            string installDb = !string.IsNullOrEmpty(installDir) ? Path.Combine(installDir, "mlc_thatpon.db") : string.Empty;
            string progDataDb = Path.Combine(programDataDir, "mlc_thatpon.db");
            string localAppDb = Path.Combine(localAppDataDir, "mlc_thatpon.db");

            // Always prefer a full compiled dictionary (>= 100 KB) and newest timestamp over any small seed fallback DB
            var candidateDbs = new[] { installDb, progDataDb, localAppDb }
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.Length >= 100_000 ? 1 : 0)
                .ThenByDescending(fi => fi.LastWriteTimeUtc)
                .ThenByDescending(fi => fi.Length)
                .ToList();

            _databaseFilePath = candidateDbs.Count > 0
                ? candidateDbs[0].FullName
                : Path.Combine(_appDataFolder, "mlc_thatpon.db");

            _pendingStyleMode = LoadSavedStyleMode();
            _underlineManager = new SafeUnderlineManager(_wordApp!);

            string dbPathSnapshot = _databaseFilePath;
            string licPathSnapshot = _licenseFilePath;
            string userDictSnapshot = _customDictFilePath;
            ProofingStyleMode initialMode = _pendingStyleMode;

            // Decrypt 12.4 MB database, build 118,764-entry Trie, and pre-warm all inverted search indexes off Word's UI thread!
            Task.Run(() =>
            {
                AddInDiagnostics.SafeExecute("AsyncEngineWarmup", () =>
                {
                    SuppressOfficeMissingBurmeseBannerRegistry();

                    string activeDbKey = AntiTamperGuard.ReconstructProtectedFallbackKey();
                    if (File.Exists(licPathSnapshot))
                    {
                        string licJson = File.ReadAllText(licPathSnapshot);
                        var licResult = LicenseService.ValidateLicenseJson(licJson, DefaultVendorPublicKeyXml);
                        if (licResult.IsValid && !string.IsNullOrWhiteSpace(licResult.SqlCipherDatabaseKey))
                        {
                            activeDbKey = licResult.SqlCipherDatabaseKey!;
                        }
                    }

                    var repo = new EncryptedDictionaryRepository(dbPathSnapshot);
                    repo.EnsureDatabaseExists(activeDbKey);
                    MyanmarTrie trie = repo.LoadIntoTrie(activeDbKey);
                    DateTime loadedStampUtc = File.Exists(dbPathSnapshot)
                        ? File.GetLastWriteTimeUtc(dbPathSnapshot)
                        : DateTime.MinValue;

                    var engine = new MyanmarProofingEngine(trie, initialMode);
                    engine.LoadCustomDictionaryFile(ministryCustomDictPath);
                    engine.LoadCustomDictionaryFile(userDictSnapshot);
                    trie.PrewarmSearchIndexes();

                    Control? marshaller = _uiMarshaller;
                    if (marshaller != null && marshaller.IsHandleCreated && !marshaller.IsDisposed)
                    {
                        marshaller.BeginInvoke(new Action(() =>
                        {
                            _activeDatabaseKey = activeDbKey;
                            _loadedDbLastWriteUtc = loadedStampUtc;
                            AddInDiagnostics.SafeExecute("AttachEngineOnStaThread", () => AttachEngineOnStaThread(engine));
                        }));
                    }
                });
            });
        }

        private void AttachEngineOnStaThread(MyanmarProofingEngine engine)
        {
            if (!_isPrimaryInstance || _wordApp == null || _underlineManager == null)
                return;

            engine.StyleMode = _pendingStyleMode;
            _proofingEngine = engine;

            _documentScanner = new StaDocumentScanner(_wordApp, _proofingEngine, _underlineManager)
            {
                IsLiveAutoScanEnabled = _pendingLiveAutoScan
            };
            _documentScanner.UserTypingDetected += () => _contextMenuHandler?.CloseActivePopup();
            _documentScanner.ScanStarted += doc =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                ctrl?.SetScanningState(true);
            };
            _documentScanner.ScanCompleted += (doc, items) =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                if (ctrl != null)
                {
                    ctrl.RenderIssues(items);
                    // Use precomputed background QualityReport for full scans; invalidate lazily for incremental keystrokes!
                    ctrl.UpdateQualityReport(_documentScanner.GetCachedQualityReport(doc));
                }
                _contextMenuHandler?.RefreshAtCurrentSelectionIfOnIssue();
            };

            _contextMenuHandler = new WordContextMenuHandler(
                _wordApp,
                _proofingEngine,
                _underlineManager,
                _documentScanner,
                () =>
                {
                    if (_wordApp?.ActiveDocument != null)
                    {
                        var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                        ctrl?.RenderIssues(_documentScanner.GetCachedIssues(_wordApp.ActiveDocument));
                    }
                },
                _customDictFilePath,
                cursorItem =>
                {
                    var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                    ctrl?.HighlightIssueFromDocumentCursor(cursorItem);
                },
                lookupWord =>
                {
                    var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                    ctrl?.OpenDictionaryWithQuery(lookupWord);
                });

            foreach (var kvp in _windowTaskPanes.Values)
            {
                kvp.Control.SetLexiconStats(
                    _proofingEngine.Trie.WordCount,
                    _proofingEngine.Trie.OfficialThatponCount,
                    _proofingEngine.Trie.RuleCount,
                    _proofingEngine.Trie.ReferenceCount,
                    _proofingEngine.Trie.EncyclopediaCount);
                kvp.Control.SetStyleMode(_proofingEngine.StyleMode);
                kvp.Control.RefreshActiveTabAfterEngineReady();
            }

            _ribbonUi?.Invalidate();

            // If a non-empty document is already open when background loading finishes, kick off initial scan seamlessly
            try
            {
                Word.Document? activeDoc = _wordApp.ActiveDocument;
                if (activeDoc != null && activeDoc.Content.End > 1)
                {
                    _documentScanner.ScanFullDocumentAsync();
                }
            }
            catch { }
        }

        private void HookWordEvents()
        {
            if (_wordApp == null) return;

            _wordApp.WindowSelectionChange += OnWindowSelectionChange;
            _wordApp.WindowBeforeRightClick += OnWindowBeforeRightClick;
            _wordApp.WindowActivate += OnWindowActivate;
            _wordApp.WindowDeactivate += OnWindowDeactivate;
            _wordApp.DocumentOpen += OnDocumentOpen;
            ((Word.ApplicationEvents4_Event)_wordApp).NewDocument += OnNewDocument;
            _wordApp.DocumentBeforeSave += OnDocumentBeforeSave;
            _wordApp.DocumentBeforePrint += OnDocumentBeforePrint;
            _wordApp.DocumentBeforeClose += OnDocumentBeforeClose;
        }

        private void OnWindowDeactivate(Word.Document doc, Word.Window wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowDeactivate", () => _contextMenuHandler?.CloseActivePopup());
        }

        private void OnWindowSelectionChange(Word.Selection sel)
        {
            AddInDiagnostics.SafeExecute("OnWindowSelectionChange", () =>
            {
                SuppressBuiltInBurmeseProofingOnSelection(sel);
                _documentScanner?.NotifySelectionActivity(sel);
                _contextMenuHandler?.OnWindowSelectionChange(sel);
            });
        }

        private static void SuppressOfficeMissingBurmeseBannerRegistry()
        {
            // Microsoft Office has no built-in Burmese (my-MM / LCID 1109) proofing DLL and otherwise shows
            // "MISSING PROOFING TOOLS: Proofing is not available for Burmese." until the user clicks "Don't show again".
            // Pre-populate Office's "Don't show again" language list for my-MM automatically for every user.
            foreach (string ver in new[] { "16.0", "15.0" })
            {
                try
                {
                    string subKeyPath = $@"Software\Microsoft\Office\{ver}\Common\LanguageResources";
                    using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true))
                    {
                        if (key == null)
                            continue;

                        string existing = (key.GetValue("NotificationsNeverShowAgainLanguages") as string) ?? string.Empty;
                        var parts = existing
                            .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .ToList();

                        bool changed = false;
                        foreach (string tag in new[] { "my-MM", "my" })
                        {
                            if (!parts.Any(p => string.Equals(p, tag, StringComparison.OrdinalIgnoreCase)))
                            {
                                parts.Add(tag);
                                changed = true;
                            }
                        }

                        if (changed)
                        {
                            key.SetValue("NotificationsNeverShowAgainLanguages", string.Join(";", parts), RegistryValueKind.String);
                        }
                    }
                }
                catch
                {
                    // Ignore registry access errors
                }
            }
        }

        private void SuppressBuiltInBurmeseProofingOnSelection(Word.Selection? sel)
        {
            if (sel == null)
                return;

            try
            {
                int pos = sel.Start;
                if (pos == _lastCheckedLangDocPos)
                    return;
                _lastCheckedLangDocPos = pos;

                // LCID 1109 (0x0455) = Burmese (my-MM). Leave English (1033) untouched so native English spellcheck still works.
                if ((int)sel.LanguageID == 1109 && sel.NoProofing != 1)
                {
                    sel.NoProofing = 1;
                }
            }
            catch
            {
                // Ignore protected/locked selection
            }
        }

        private void OnWindowBeforeRightClick(Word.Selection sel, ref bool cancel)
        {
            bool localCancel = cancel;
            AddInDiagnostics.SafeExecute("OnWindowBeforeRightClick", () =>
            {
                _contextMenuHandler?.OnWindowBeforeRightClick(sel, ref localCancel);
            });
            cancel = localCancel;
        }

        private void OnWindowActivate(Word.Document doc, Word.Window wn)
        {
            AddInDiagnostics.SafeExecute("OnWindowActivate", () =>
            {
                _lastCheckedLangDocPos = -1;
                PruneClosedWindowTaskPanes();

                // Synchronize Task Pane for the newly activated document window
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                if (ctrl != null && _documentScanner != null)
                {
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(doc));
                }
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
            });
        }

        private void OnDocumentOpen(Word.Document doc)
        {
            AddInDiagnostics.SafeExecute("OnDocumentOpen", () =>
            {
                _lastCheckedLangDocPos = -1;
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                _documentScanner?.ScanFullDocumentAsync();
            });
        }

        private void OnNewDocument(Word.Document doc)
        {
            AddInDiagnostics.SafeExecute("OnNewDocument", () =>
            {
                _lastCheckedLangDocPos = -1;
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            });
        }

        private void OnDocumentBeforeSave(Word.Document doc, ref bool saveAsUI, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnDocumentBeforeSave", () =>
            {
                // Strip temporary Red/Blue wavy underlines (restoring original user underlines like wdUnderlineSingle)
                // before Word writes the .docx file, then automatically restore wavy underlines on unfixed issues 450ms later
                _underlineManager?.TemporarilyStripForSaveOrPrint(doc);
            });
        }

        private void OnDocumentBeforePrint(Word.Document doc, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnDocumentBeforePrint", () =>
            {
                // Strip temporary Red/Blue wavy underlines before printing, then restore 450ms later
                _underlineManager?.TemporarilyStripForSaveOrPrint(doc);
            });
        }

        private void OnDocumentBeforeClose(Word.Document doc, ref bool cancel)
        {
            AddInDiagnostics.SafeExecute("OnDocumentBeforeClose", () =>
            {
                _lastCheckedLangDocPos = -1;
                _contextMenuHandler?.CloseActivePopup();
                _documentScanner?.PurgeDocument(doc);
                _underlineManager?.PurgeClosedDocument(doc);
                PruneClosedWindowTaskPanes();
            });
        }

        private void PruneClosedWindowTaskPanes()
        {
            if (_wordApp == null || _windowTaskPanes.Count == 0)
                return;

            try
            {
                var liveHwnds = new HashSet<int>();
                foreach (Word.Window win in _wordApp.Windows)
                {
                    try
                    {
                        if (win.Hwnd != 0)
                            liveHwnds.Add(win.Hwnd);
                    }
                    catch { }
                }

                var staleKeys = _windowTaskPanes.Keys.Where(h => !liveHwnds.Contains(h)).ToList();
                foreach (int staleHwnd in staleKeys)
                {
                    _windowTaskPanes.Remove(staleHwnd);
                }
            }
            catch
            {
                // Ignore transient COM enumeration states
            }
        }

        public void CTPFactoryAvailable(Office.ICTPFactory ctpFactoryInst)
        {
            if (!_isPrimaryInstance)
                return;

            AddInDiagnostics.SafeExecute("CTPFactoryAvailable", () =>
            {
                _ctpFactory = ctpFactoryInst;
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            });
        }

        private ProofingTaskPaneControl? GetOrCreateActiveWindowTaskPaneControl(bool autoShow)
        {
            if (!_isPrimaryInstance || _ctpFactory == null || _wordApp == null)
                return null;

            try
            {
                Word.Window? activeWin = null;
                try { activeWin = _wordApp.ActiveWindow; } catch { }
                int hwnd = activeWin?.Hwnd ?? 0;

                // Never create an orphan (hwnd == 0) Task Pane before ActiveWindow is ready — that causes duplicate panes once Document1 opens!
                if (activeWin == null || hwnd == 0)
                {
                    if (autoShow)
                    {
                        ScheduleDeferredTaskPaneCreation();
                    }
                    return null;
                }

                if (_windowTaskPanes.TryGetValue(hwnd, out var existing))
                {
                    if (autoShow)
                        existing.Pane.Visible = true;
                    return existing.Control;
                }

                Office.CustomTaskPane pane = _ctpFactory.CreateCTP(
                    "MyanmarSpellChecker.ProofingTaskPaneControl",
                    "မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း",
                    activeWin);

                var ctrl = (ProofingTaskPaneControl)pane.ContentControl;
                if (_proofingEngine != null)
                {
                    ctrl.SetLexiconStats(
                        _proofingEngine.Trie.WordCount,
                        _proofingEngine.Trie.OfficialThatponCount,
                        _proofingEngine.Trie.RuleCount,
                        _proofingEngine.Trie.ReferenceCount,
                        _proofingEngine.Trie.EncyclopediaCount);
                    ctrl.SetStyleMode(_proofingEngine.StyleMode);
                }
                else
                {
                    ctrl.SetEngineWarmupState();
                    ctrl.SetStyleMode(_pendingStyleMode);
                }
                WireTaskPaneControlEvents(ctrl);

                pane.DockPosition = Office.MsoCTPDockPosition.msoCTPDockPositionRight;
                pane.Width = 440;
                pane.VisibleStateChange += ctp =>
                {
                    try { _ribbonUi?.InvalidateControl("BtnToggleTaskPane"); } catch { }
                };
                if (autoShow)
                    pane.Visible = true;

                _windowTaskPanes[hwnd] = (pane, ctrl);
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
                return ctrl;
            }
            catch
            {
                return null;
            }
        }

        private void ScheduleDeferredTaskPaneCreation()
        {
            if (_startupTaskPaneTimer != null)
                return;

            _startupTaskPaneRetries = 0;
            _startupTaskPaneTimer = new Timer { Interval = 300 };
            _startupTaskPaneTimer.Tick += (s, e) =>
            {
                _startupTaskPaneRetries++;
                Word.Window? win = null;
                try { win = _wordApp?.ActiveWindow; } catch { }
                if ((win != null && win.Hwnd != 0) || _startupTaskPaneRetries >= 20)
                {
                    _startupTaskPaneTimer?.Stop();
                    _startupTaskPaneTimer?.Dispose();
                    _startupTaskPaneTimer = null;

                    if (win != null && win.Hwnd != 0)
                    {
                        GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                    }
                }
            };
            _startupTaskPaneTimer.Start();
        }

        private void WireTaskPaneControlEvents(ProofingTaskPaneControl ctrl)
        {
            ctrl.RequestFullDocumentScan += () => _documentScanner?.ScanFullDocumentAsync();
            ctrl.RequestFullDocumentScan += TriggerManualDocumentScanWithHotReload;
            ctrl.RequestFixAllIssues += ExecuteFixAllInActiveDocument;
            ctrl.RequestClearUnderlines += () =>
            {
                if (_wordApp?.ActiveDocument != null)
                {
                    _underlineManager?.StripAllProofingUnderlines(_wordApp.ActiveDocument);
                    _documentScanner?.ClearCachedIssues(_wordApp.ActiveDocument);
                }
            };
            ctrl.RequestLicenseDialog += ShowLicenseDialog;
            ctrl.RequestSelectIssueInDocument += item => _underlineManager?.SelectAndScrollToIssue(item);
            ctrl.RequestApplySuggestion += (item, replacement) =>
            {
                if (_wordApp?.ActiveDocument != null)
                {
                    _underlineManager?.ReplaceIssueInDocument(_wordApp.ActiveDocument, item, replacement);
                    _documentScanner?.InvalidateParagraphCache(_wordApp.ActiveDocument);
                }
            };
            ctrl.RequestApplyBatchSuggestion += (originalText, replacementText) =>
            {
                Word.Document? doc = _wordApp?.ActiveDocument;
                if (doc != null && _documentScanner != null && _underlineManager != null)
                {
                    var cached = _documentScanner.GetCachedIssues(doc);
                    int replaced = _underlineManager.ReplaceMatchingIssuesInDocument(doc, cached, originalText, replacementText);
                    _documentScanner.InvalidateParagraphCache(doc);
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(doc));
                    if (replaced > 0)
                    {
                        ctrl.ShowToastBanner(
                            $"✓ တူညီသောအမှား ({replaced}) ခုလုံးကို \"{replacementText}\" ဖြင့် တစ်ပြိုင်နက် ပြင်ဆင်ပြီးပါပြီ (Ctrl+Z ပြန်ဆုတ်နိုင်သည်)။");
                    }
                }
            };
            ctrl.RequestIgnoreOnce += item =>
            {
                _proofingEngine?.IgnoreWordInSession(item.Issue.OriginalText);
                item.IsResolved = true;
                if (item.TargetRange != null)
                {
                    try
                    {
                        item.TargetRange.Font.Underline = item.OriginalUnderline;
                        item.TargetRange.Font.UnderlineColor = item.OriginalUnderlineColor;
                    }
                    catch { }
                }
            };
            ctrl.RequestAddToDictionary += item =>
            {
                _proofingEngine?.AddCustomWord(item.Issue.OriginalText, _customDictFilePath);
                string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(item.Issue.OriginalText.Trim());
                if (_wordApp?.ActiveDocument != null && _documentScanner != null)
                {
                    foreach (var cached in _documentScanner.GetCachedIssues(_wordApp.ActiveDocument))
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                        {
                            cached.IsResolved = true;
                            if (cached.TargetRange != null)
                            {
                                try
                                {
                                    cached.TargetRange.Font.Underline = cached.OriginalUnderline;
                                    cached.TargetRange.Font.UnderlineColor = cached.OriginalUnderlineColor;
                                }
                                catch { }
                            }
                        }
                    }
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(_wordApp.ActiveDocument));
                }
            };
            ctrl.RequestDictionarySearch += query =>
                _proofingEngine?.SearchDictionary(query, 45) ?? new List<DictionaryEntry>();
            ctrl.RequestSearchReferenceCorpus += (query, incProverbs, incEncyc) =>
                _proofingEngine?.SearchReferenceCorpus(query, incProverbs, incEncyc, 40) ?? new List<DictionaryEntry>();
            ctrl.RequestGetSynonyms += word =>
                _proofingEngine?.GetSynonyms(word, 8) ?? new List<string>();
            ctrl.RequestGetAntonyms += word =>
                _proofingEngine?.GetAntonyms(word, 8) ?? new List<string>();
            ctrl.RequestGetFeaturedKnowledge += () =>
                _proofingEngine?.GetFeaturedKnowledgeEntries(8) ?? new List<DictionaryEntry>();
            ctrl.RequestGetSelectedDocumentText += () =>
            {
                try
                {
                    Word.Selection? sel = _wordApp?.Selection;
                    if (sel == null || sel.Range == null || sel.Range.Start == sel.Range.End)
                        return string.Empty;

                    string raw = (sel.Range.Text ?? string.Empty).Trim(' ', '\t', '\r', '\n', '\a', '၊', '။', '"', '\'', '“', '”', '(', ')');
                    return raw.Length <= 80 ? raw : raw.Substring(0, 80).Trim();
                }
                catch
                {
                    return string.Empty;
                }
            };
            ctrl.RequestHomophoneSummary += word =>
                _proofingEngine?.GetHomophoneSiblingsSummary(word) ?? string.Empty;
            ctrl.RequestChangeStyleMode += ApplyStyleModeAndRescan;
            ctrl.RequestInsertWordAtCursor += word =>
            {
                try
                {
                    if (_wordApp?.Selection != null)
                    {
                        _wordApp.Selection.TypeText(word);
                    }
                }
                catch { }
            };
            ctrl.RequestGetCustomWords += () =>
                _proofingEngine?.GetCustomWords() ?? Array.Empty<string>();
            ctrl.RequestAddCustomWordManual += word =>
            {
                if (_proofingEngine == null || !_proofingEngine.AddCustomWord(word, _customDictFilePath))
                    return false;

                string normAdded = UnicodeNormalizer.NormalizeCanonicalOrder(UnicodeNormalizer.FixLookalikeCharacters(word.Trim()));
                if (_wordApp?.ActiveDocument != null && _documentScanner != null)
                {
                    foreach (var cached in _documentScanner.GetCachedIssues(_wordApp.ActiveDocument))
                    {
                        if (UnicodeNormalizer.NormalizeCanonicalOrder(cached.Issue.OriginalText.Trim()) == normAdded)
                        {
                            cached.IsResolved = true;
                            if (cached.TargetRange != null)
                            {
                                try
                                {
                                    cached.TargetRange.Font.Underline = cached.OriginalUnderline;
                                    cached.TargetRange.Font.UnderlineColor = cached.OriginalUnderlineColor;
                                }
                                catch { }
                            }
                        }
                    }
                    ctrl.RenderIssues(_documentScanner.GetCachedIssues(_wordApp.ActiveDocument));
                }
                return true;
            };
            ctrl.RequestRemoveCustomWord += word =>
            {
                if (_proofingEngine == null || !_proofingEngine.RemoveCustomWord(word, _customDictFilePath))
                    return false;

                if (_wordApp?.ActiveDocument != null && _documentScanner != null)
                {
                    _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
                    _documentScanner.ScanFullDocumentAsync();
                }
                return true;
            };

            // Flagship V11/V12 Document Toolkit & Official Ministry Suite Events
            ctrl.RequestRewriteToOfficialStyle += selectionOnly => ExecuteToolkitRewriteOfficialProse(selectionOnly);
            ctrl.RequestConvertLegacyFonts += selectionOnly => ExecuteToolkitConvertFonts(selectionOnly);
            ctrl.RequestFormatNumerals += (toMyanmarDigits, selectionOnly) => ExecuteToolkitFormatNumerals(toMyanmarDigits, selectionOnly);
            ctrl.RequestFormatPunctuation += selectionOnly => ExecuteToolkitFormatPunctuation(selectionOnly);
            ctrl.RequestInsertOfficialTemplate += (kind, dt) => ExecuteInsertOfficialTemplate(kind, dt);
            ctrl.RequestRefreshQualityReport += () => BuildActiveDocumentQualityReport();
            ctrl.RequestExportAuditCertificate += ExecuteExportAuditCertificate;
            ctrl.RequestValidateOfficialLetter += () =>
            {
                string plain = _toolkitService.ExtractFullDocumentPlainText(_wordApp?.ActiveDocument);
                return OfficialLetterSuite.ValidateOfficialLetterStructure(plain);
            };
        }

        private DocumentQualityReport BuildActiveDocumentQualityReport()
        {
            Word.Document? doc = _wordApp?.ActiveDocument;
            if (doc != null && _documentScanner != null)
            {
                var cachedReport = _documentScanner.GetCachedQualityReport(doc);
                if (cachedReport != null)
                    return cachedReport;
            }

            var cached = (doc != null && _documentScanner != null)
                ? _documentScanner.GetCachedIssues(doc)
                : null;
            return BuildDocumentQualityReport(doc, cached);
        }

        private DocumentQualityReport BuildDocumentQualityReport(Word.Document? doc, IReadOnlyList<DocumentProofingItem>? items)
        {
            string plain = _toolkitService.ExtractFullDocumentPlainText(doc);
            IReadOnlyList<ProofingIssue>? unresolvedIssues = items != null
                ? items.Where(i => !i.IsResolved).Select(i => i.Issue).ToList()
                : null;
            return DocumentQualityAnalyzer.Analyze(plain, unresolvedIssues, _proofingEngine);
        }

        private void ExecuteToolkitConvertFonts(bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            ToolkitActionSummary res = _toolkitService.ConvertLegacyFontsInDocument(_wordApp, doc, selectionOnly);
            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync();
            }

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner(res.UserMessage, res.Success);
        }

        private void ExecuteToolkitFormatNumerals(bool toMyanmarDigits, bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            ToolkitActionSummary res = _toolkitService.FormatNumeralsInDocument(_wordApp, doc, toMyanmarDigits, selectionOnly);
            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync();
            }

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner(res.UserMessage, res.Success);
        }

        private void ExecuteToolkitFormatPunctuation(bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            ToolkitActionSummary res = _toolkitService.FormatOfficialPunctuationInDocument(_wordApp, doc, selectionOnly);
            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync();
            }

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner(res.UserMessage, res.Success);
        }

        private void ExecuteToolkitRewriteOfficialProse(bool selectionOnly)
        {
            if (_wordApp == null) return;
            Word.Document? doc = _wordApp.ActiveDocument;
            if (doc != null) _underlineManager?.StripAllProofingUnderlines(doc);

            int changed = _toolkitService.RewriteProseToOfficialStyle(_wordApp, doc, selectionOnly);
            if (doc != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(doc);
                _documentScanner.ScanFullDocumentAsync();
            }

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            string msg = changed > 0
                ? $"✓ စကားပြောဟန် အသုံးအနှုန်း ({changed}) နေရာကို ရုံးသုံး/စာပေဟန်သို့ တစ်ချက်တည်းဖြင့် ပြောင်းလဲပြီးပါပြီ (Ctrl+Z ဖြင့် ပြန်ပြင်နိုင်သည်)။"
                : "✓ ပြောင်းလဲရန် စကားပြောဟန် အသုံးအနှုန်း မတွေ့ရှိပါ (ရုံးသုံးစာပေဟန် အသင့်ဖြစ်ပြီးဖြစ်သည်)။";
            ctrl?.ShowToastBanner(msg, isSuccess: true);
        }

        private void ExecuteInsertOfficialTemplate(OfficialTemplateKind kind, DateTime referenceDate)
        {
            if (_wordApp == null) return;
            _toolkitService.InsertOfficialTemplateAtCursor(_wordApp, kind, referenceDate);
            if (_wordApp.ActiveDocument != null && _documentScanner != null)
            {
                _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
                _documentScanner.ScanFullDocumentAsync();
            }

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner("✓ ရုံးသုံး စံပုံစံကြမ်းကို Pyidaungsu ဖောင့်ဖြင့် ထည့်သွင်းပြီးပါပြီ။");
        }

        private void ExecuteExportAuditCertificate()
        {
            if (_wordApp == null) return;
            Word.Document? sourceDoc = _wordApp.ActiveDocument;
            DocumentQualityReport report = BuildActiveDocumentQualityReport();
            bool ok = _toolkitService.GeneratePrintableAuditCertificateDocument(_wordApp, sourceDoc, report);
            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
            ctrl?.ShowToastBanner(
                ok
                    ? "✓ မြန်မာစာ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (Word စာတမ်းအသစ်) ထုတ်ပေးပြီးပါပြီ။"
                    : "⚠ အစီရင်ခံစာ ထုတ်ယူစဉ် အခက်အခဲ ဖြစ်ပေါ်ပါသည်။",
                ok);
        }

        private void ExecuteFixAllInActiveDocument(IssueFilterMode filterMode = IssueFilterMode.All)
        {
            Word.Document? doc = _wordApp?.ActiveDocument;
            if (doc == null || _documentScanner == null || _underlineManager == null)
                return;

            var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);

            Func<DocumentProofingItem, bool> predicate = filterMode switch
            {
                IssueFilterMode.SpellingOnly => i => !i.Issue.IsGrammarOrStyle && !i.Issue.IsContextualWarning,
                IssueFilterMode.GrammarOnly => i => i.Issue.IsGrammarOrStyle,
                IssueFilterMode.ContextOnly => i => i.Issue.IsContextualWarning,
                _ => _ => true
            };

            string scopeLabel = filterMode switch
            {
                IssueFilterMode.SpellingOnly => "သတ်ပုံအမှား",
                IssueFilterMode.GrammarOnly => "သဒ္ဒါ/စာဟန်အမှား",
                IssueFilterMode.ContextOnly => "အနက်သံသယ စိစစ်ချက်",
                _ => "အမှားစုစုပေါင်း"
            };

            var cached = _documentScanner.GetCachedIssues(doc);
            var candidates = cached
                .Where(i => !i.IsResolved &&
                            i.TargetRange != null &&
                            i.Issue.Suggestions != null &&
                            i.Issue.Suggestions.Count > 0 &&
                            predicate(i))
                .ToList();

            if (candidates.Count == 0)
            {
                ctrl?.ShowToastBanner("✓ ပြင်ဆင်ရန် အမှားအယွင်း မကျန်ရှိတော့ပါ။");
                return;
            }

            var previewEntries = candidates
                .Select(item => new FixPreviewEntry<DocumentProofingItem>(
                    item,
                    $"{item.StoryName} • ပိုဒ် #{item.ParagraphIndex}",
                    TryExtractWordParagraphSnippet(item),
                    item.Issue))
                .ToList();

            using (var dlg = new FixAllPreviewDialog<DocumentProofingItem>(
                previewEntries,
                $"Word — {scopeLabel}",
                target => _underlineManager.SelectAndScrollToIssue(target)))
            {
                IWin32Window? ownerWin = ctrl != null && ctrl.IsHandleCreated ? ctrl : null;
                DialogResult result = ownerWin != null ? dlg.ShowDialog(ownerWin) : dlg.ShowDialog();
                if (result != DialogResult.OK || dlg.ApprovedReplacements.Count == 0)
                {
                    return;
                }

                int skippedCount = candidates.Count - dlg.ApprovedReplacements.Count;
                int count = _underlineManager.ReplaceApprovedIssuesInDocument(doc, cached, dlg.ApprovedReplacements);
                _documentScanner.InvalidateParagraphCache(doc);

                ctrl?.RenderIssues(_documentScanner.GetCachedIssues(doc));

                if (count > 0)
                {
                    string skipTail = skippedCount > 0
                        ? $" (ချန်လှပ်ထားသည် {skippedCount} ခု)"
                        : string.Empty;
                    ctrl?.ShowToastBanner(
                        $"✓ စိစစ်အတည်ပြုထားသော ({count}) ခုကို တစ်ပြိုင်နက် ပြင်ဆင်ပြီးပါပြီ{skipTail} (Ctrl+Z ဖြင့် မူလအတိုင်း ပြန်ပြင်နိုင်သည်)။");
                }
            }
        }

        private static string TryExtractWordParagraphSnippet(DocumentProofingItem item)
        {
            try
            {
                if (item.TargetRange != null && item.TargetRange.Paragraphs.Count > 0)
                {
                    string raw = item.TargetRange.Paragraphs[1].Range.Text ?? string.Empty;
                    string cleaned = raw.Replace("\r", " ").Replace("\a", " ").Replace("\n", " ").Trim();
                    if (!string.IsNullOrEmpty(cleaned))
                        return cleaned;
                }
            }
            catch { }

            return item.Issue.OriginalText ?? string.Empty;
        }

        private ProofingStyleMode LoadSavedStyleMode()
        {
            try
            {
                if (!string.IsNullOrEmpty(_styleModeConfigPath) && File.Exists(_styleModeConfigPath))
                {
                    string raw = File.ReadAllText(_styleModeConfigPath).Trim();
                    if (Enum.TryParse(raw, ignoreCase: true, out ProofingStyleMode parsed))
                        return parsed;
                }
            }
            catch
            {
                // Ignore
            }
            return ProofingStyleMode.GeneralStandard;
        }

        private void ApplyStyleModeAndRescan(ProofingStyleMode mode)
        {
            _pendingStyleMode = mode;
            try
            {
                if (!string.IsNullOrEmpty(_styleModeConfigPath))
                {
                    File.WriteAllText(_styleModeConfigPath, mode.ToString());
                }
            }
            catch
            {
                // Ignore
            }

            foreach (var kv in _windowTaskPanes.Values)
            {
                kv.Control.SetStyleMode(mode);
            }
            _ribbonUi?.InvalidateControl("BtnToggleFormalStyle");

            if (_proofingEngine == null)
                return;

            _proofingEngine.StyleMode = mode;

            if (_wordApp?.ActiveDocument != null)
            {
                _underlineManager?.StripAllProofingUnderlines(_wordApp.ActiveDocument);
                _documentScanner?.InvalidateParagraphCache(_wordApp.ActiveDocument);
                _documentScanner?.ScanFullDocumentAsync();
            }
        }

        public string GetCustomUI(string ribbonId)
        {
            if (!_isPrimaryInstance)
                return string.Empty;

            return @"
<customUI xmlns='http://schemas.microsoft.com/office/2009/07/customui' onLoad='OnRibbonLoad'>
  <ribbon>
    <tabs>
      <tab id='TabMyanmarProofing' label='မြန်မာစာ စစ်ဆေးရေး'>
        <group id='GrpProofingActions' label='စာတမ်း စစ်ဆေးရေးနှင့် ပြင်ဆင်မှု'>
          <button id='BtnScanFullDocument'
                  label='စာတမ်းတစ်ခုလုံး စစ်ဆေးမည်'
                  size='large'
                  imageMso='SpellingAndGrammar'
                  onAction='OnRibbonScanDocument'
                  screentip='စာကိုယ်၊ ဇယားကွက်နှင့် ခေါင်းစီး/အောက်ခြေ အားလုံးကို စစ်ဆေးရန်' />
          <button id='BtnFixAllIssues'
                  label='အားလုံး တစ်ပြိုင်နက် ပြင်မည်'
                  size='large'
                  imageMso='AcceptInvitation'
                  onAction='OnRibbonFixAllIssues'
                  screentip='တွေ့ရှိသော အမှားအားလုံးကို သတ်ပုံကျမ်းအမှန်ဖြင့် တစ်ပြိုင်နက် အစားထိုးရန် (Single Ctrl+Z)' />
          <button id='BtnClearUnderlines'
                  label='မျဉ်းလှိုင်းများ ဖယ်ရှားမည်'
                  size='normal'
                  imageMso='ClearFormatting'
                  onAction='OnRibbonClearUnderlines'
                  screentip='စာတမ်းပေါ်ရှိ စစ်ဆေးရေး မျဉ်းလှိုင်းများကို ဖယ်ရှားရန်' />
        </group>
        <group id='GrpStyleAndLive' label='အရေးအသားနှင့် အလိုအလျောက်စနစ်'>
          <toggleButton id='BtnToggleFormalStyle'
                        label='စာပေ / ရုံးသုံး အရေးစကားစနစ်'
                        size='large'
                        imageMso='FileDocumentInspect'
                        getPressed='GetFormalStylePressed'
                        onAction='OnRibbonToggleFormalStyle'
                        screentip='ပိတ်ထားပါက အထွေထွေ စံသတ်ပုံစနစ်ဖြင့် စစ်ဆေးပြီး၊ ဖွင့်ထားပါက စာပေနှင့် ရုံးသုံးအရေးစကား (ပါတယ်->ပါသည်၊ တွေ->များ) ပါ စစ်ဆေးပေးပါမည်' />
          <toggleButton id='BtnToggleLiveScan'
                        label='အလိုအလျောက် စစ်ဆေးမှု (Live)'
                        size='large'
                        imageMso='ReviewTrackChanges'
                        getPressed='GetLiveScanPressed'
                        onAction='OnRibbonToggleLiveScan'
                        screentip='စာရိုက်နေစဉ် အလိုအလျောက် စစ်ဆေးမှုကို ဖွင့်/ပိတ်ရန်' />
        </group>
        <group id='GrpDictionaryAndTools' label='အဘိဓာန်နှင့် စစ်ဆေးရေးပြကွက် (Offline)'>
          <button id='BtnSearchDictionary'
                  label='သတ်ပုံကျမ်းနှင့် အဘိဓာန် ရှာရန်'
                  size='large'
                  imageMso='LookUpReference'
                  onAction='OnRibbonSearchDictionary'
                  screentip='သတ်ပုံကျမ်း၊ မြန်မာအဘိဓာန် နှင့် စကားပုံများကို အမြန်ရှာဖွေရန်' />
          <toggleButton id='BtnToggleTaskPane'
                        label='စစ်ဆေးရေးပြကွက် ပြ/ဖျောက်'
                        size='large'
                        imageMso='ResearchPane'
                        getPressed='GetTaskPanePressed'
                        onAction='OnRibbonToggleTaskPane'
                        screentip='ညာဘက် စစ်ဆေးရေးပြကွက် (Task Pane) ကို ဖွင့်/ပိတ် ပြုလုပ်ရန်' />
          <button id='BtnManageCustomWords'
                  label='မှတ်သားဝေါဟာရ စီမံရန်'
                  size='normal'
                  imageMso='BookmarkInsert'
                  onAction='OnRibbonManageCustomWords'
                  screentip='ကိုယ်ပိုင် / ရုံးသုံး မှတ်သားထားသော စာလုံးများကို ထည့်သွင်း/ဖျက်သိမ်းရန်' />
          <button id='BtnLicenseActivation'
                  label='ဖန်တီးသူနှင့် လိုင်စင် (GPLv3)'
                  size='normal'
                  imageMso='Info'
                  onAction='OnRibbonOpenLicense'
                  screentip='စနစ်အချက်အလက်၊ ဖန်တီးသူ ထက်ရာဇာ (ကျောက်ဆည်တက္ကသိုလ်) နှင့် GNU GPL v3.0 Open-Source လိုင်စင် ကြည့်ရှုရန်' />
        </group>
        <group id='GrpTypographyToolkit' label='ဖောင့်နှင့် ကိန်းဂဏန်း ကိရိယာ'>
          <button id='BtnConvertFonts'
                  label='WinInnwa/Zawgyi ➔ Pyidaungsu'
                  size='large'
                  imageMso='FontDialog'
                  onAction='OnRibbonConvertFonts'
                  screentip='WinInnwa, WinResearcher, CE နှင့် Zawgyi ဖောင့်များကို အင်္ဂလိပ်ဖောင့်နှင့် ပုံစံမပျက်စေဘဲ Pyidaungsu သို့ တစ်ချက်နှိပ် ပြောင်းလဲရန်' />
          <button id='BtnNumeralsToMyanmar'
                  label='0-9 ➔ ၀-၉ (Smart)'
                  size='normal'
                  imageMso='BulletsAndNumberingDialog'
                  onAction='OnRibbonNumeralsToMyanmar'
                  screentip='URL, Email, COVID-19, ISO-9001, A4, 5G တို့ကို မထိခိုက်ဘဲ မြန်မာဂဏန်း ပြောင်းလဲရန်' />
          <button id='BtnCleanPunctuation'
                  label='ပုဒ်ဖြတ် ၊ / ။ ရှင်းမည်'
                  size='normal'
                  imageMso='ParagraphMarks'
                  onAction='OnRibbonCleanPunctuation'
                  screentip='ပုဒ်ထီး (၊)၊ ပုဒ်မ (။) အကွာအဝေးနှင့် မမြင်ရသော ZWSP များကို သန့်စင်ညှိနှိုင်းရန်' />
        </group>
        <group id='GrpOfficialSuite' label='ရုံးစာနှင့် အရည်အသွေး စစ်ဆေးချက်'>
          <button id='BtnOpenToolkitPane'
                  label='ကိရိယာ နှင့် ပြက္ခဒိန်'
                  size='large'
                  imageMso='DateAndTimeInsert'
                  onAction='OnRibbonOpenToolkitPane'
                  screentip='ကျပ်ငွေ (သိန်း) စာသားပြောင်းစက်၊ မြန်မာသက္ကရာဇ် ပြက္ခဒိန် နှင့် ရုံးစာပြကွက် ဖွင့်ရန်' />
          <button id='BtnRewriteOfficialProse'
                  label='ရုံးသုံးစာပေဟန် ပြောင်းမည်'
                  size='normal'
                  imageMso='Translate'
                  onAction='OnRibbonRewriteOfficialProse'
                  screentip='စကားပြောဟန် (ပါတယ်၊ တွေ၊ နဲ့၊ ဒီနေ့...) များကို အစိုးရရုံးသုံး စာပေအရေးအသားသို့ တစ်ချက်နှိပ် ပြောင်းလဲရန် (Single Ctrl+Z)' />
          <button id='BtnInsertOfficialLetter'
                  label='ရုံးစာ ပုံစံကြမ်း ထည့်ရန်'
                  size='normal'
                  imageMso='FileNewDefault'
                  onAction='OnRibbonInsertOfficialLetter'
                  screentip='မြန်မာသက္ကရာဇ် ရက်စွဲပါဝင်သော ရုံးစာ ပုံစံကြမ်းကို လက်ရှိနေရာ၌ ထည့်သွင်းရန်' />
          <button id='BtnExportAuditReport'
                  label='အရည်အသွေး အစီရင်ခံစာ'
                  size='normal'
                  imageMso='FilePrintPreview'
                  onAction='OnRibbonExportAuditReport'
                  screentip='စာတမ်း၏ စာလုံးပေါင်းနှင့် ရုံးသုံးစံနှုန်း အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (Word) ထုတ်ယူရန်' />
        </group>
      </tab>
    </tabs>
  </ribbon>
</customUI>";
        }

        public void OnRibbonLoad(Office.IRibbonUI ribbonUi)
        {
            _ribbonUi = ribbonUi;
        }

        public bool GetFormalStylePressed(Office.IRibbonControl control)
        {
            return (_proofingEngine?.StyleMode ?? _pendingStyleMode) == ProofingStyleMode.FormalOfficial;
        }

        public void OnRibbonToggleFormalStyle(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleFormalStyle", () =>
            {
                ApplyStyleModeAndRescan(pressed ? ProofingStyleMode.FormalOfficial : ProofingStyleMode.GeneralStandard);
            });
        }

        public bool GetLiveScanPressed(Office.IRibbonControl control)
        {
            return _documentScanner?.IsLiveAutoScanEnabled ?? _pendingLiveAutoScan;
        }

        public void OnRibbonToggleLiveScan(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleLiveScan", () =>
            {
                _pendingLiveAutoScan = pressed;
                if (_documentScanner != null)
                {
                    _documentScanner.IsLiveAutoScanEnabled = pressed;
                }
                _ribbonUi?.InvalidateControl("BtnToggleLiveScan");
            });
        }

        public bool GetTaskPanePressed(Office.IRibbonControl control)
        {
            if (_wordApp == null)
                return false;

            int hwnd = 0;
            try { hwnd = _wordApp.ActiveWindow?.Hwnd ?? 0; } catch { }
            return hwnd != 0 && _windowTaskPanes.TryGetValue(hwnd, out var entry) && entry.Pane.Visible;
        }

        public void OnRibbonScanDocument(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonScanDocument", () =>
            {
                GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                _documentScanner?.ScanFullDocumentAsync();
                TriggerManualDocumentScanWithHotReload();
            });
        }

        private void TriggerManualDocumentScanWithHotReload()
        {
            if (_proofingEngine == null || _documentScanner == null)
                return;

            string dbPath = _databaseFilePath;
            if (!string.IsNullOrWhiteSpace(dbPath) &&
                File.Exists(dbPath) &&
                File.GetLastWriteTimeUtc(dbPath) > _loadedDbLastWriteUtc &&
                System.Threading.Interlocked.CompareExchange(ref _dbHotReloadInProgress, 1, 0) == 0)
            {
                string keySnapshot = !string.IsNullOrWhiteSpace(_activeDatabaseKey)
                    ? _activeDatabaseKey
                    : AntiTamperGuard.ReconstructProtectedFallbackKey();
                string ministryDict = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "MyanmarSpellChecker",
                    "ministry_custom_dictionary.txt");

                var activeCtrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                activeCtrl?.SetScanningState(true);

                Task.Run(() =>
                {
                    try
                    {
                        var repo = new EncryptedDictionaryRepository(dbPath);
                        MyanmarTrie freshTrie = repo.LoadIntoTrie(keySnapshot);
                        DateTime newStamp = File.GetLastWriteTimeUtc(dbPath);
                        freshTrie.PrewarmSearchIndexes();
                        _proofingEngine.HotSwapTrie(freshTrie);
                        _proofingEngine.LoadCustomDictionaryFile(ministryDict);

                        Control? marshaller = _uiMarshaller;
                        if (marshaller != null && marshaller.IsHandleCreated && !marshaller.IsDisposed)
                        {
                            marshaller.BeginInvoke(new Action(() =>
                            {
                                _loadedDbLastWriteUtc = newStamp;
                                foreach (var kvp in _windowTaskPanes.Values)
                                {
                                    kvp.Control.SetLexiconStats(
                                        freshTrie.WordCount,
                                        freshTrie.OfficialThatponCount,
                                        freshTrie.RuleCount,
                                        freshTrie.ReferenceCount,
                                        freshTrie.EncyclopediaCount);
                                }

                                if (_wordApp?.ActiveDocument != null)
                                {
                                    _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
                                }
                                _documentScanner.ScanFullDocumentAsync();
                            }));
                        }
                    }
                    catch
                    {
                        Control? marshaller = _uiMarshaller;
                        if (marshaller != null && marshaller.IsHandleCreated && !marshaller.IsDisposed)
                        {
                            marshaller.BeginInvoke(new Action(() => _documentScanner?.ScanFullDocumentAsync()));
                        }
                    }
                    finally
                    {
                        System.Threading.Interlocked.Exchange(ref _dbHotReloadInProgress, 0);
                    }
                });
                return;
            }

            if (_wordApp?.ActiveDocument != null)
            {
                _documentScanner.InvalidateParagraphCache(_wordApp.ActiveDocument);
            }
            _documentScanner.ScanFullDocumentAsync();
        }

        public void OnRibbonFixAllIssues(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonFixAllIssues", () => ExecuteFixAllInActiveDocument(IssueFilterMode.All));
        }

        public void OnRibbonSearchDictionary(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonSearchDictionary", () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.FocusDictionarySearchBox();
            });
        }

        public void OnRibbonManageCustomWords(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonManageCustomWords", () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.OpenCustomWordsTab();
            });
        }

        public void OnRibbonToggleTaskPane(Office.IRibbonControl control, bool pressed)
        {
            AddInDiagnostics.SafeExecute("OnRibbonToggleTaskPane", () =>
            {
                if (_wordApp == null) return;
                int hwnd = 0;
                try { hwnd = _wordApp.ActiveWindow?.Hwnd ?? 0; } catch { }

                if (_windowTaskPanes.TryGetValue(hwnd, out var entry))
                {
                    entry.Pane.Visible = pressed;
                }
                else if (pressed)
                {
                    GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                }
                _ribbonUi?.InvalidateControl("BtnToggleTaskPane");
            });
        }

        public void OnRibbonClearUnderlines(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonClearUnderlines", () =>
            {
                if (_wordApp?.ActiveDocument != null)
                {
                    _underlineManager?.StripAllProofingUnderlines(_wordApp.ActiveDocument);
                    _documentScanner?.ClearCachedIssues(_wordApp.ActiveDocument);
                    var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: false);
                    ctrl?.ShowToastBanner("စစ်ဆေးရေး မျဉ်းလှိုင်းများကို ဖယ်ရှားပြီးပါပြီ။");
                }
            });
        }

        public void OnRibbonOpenLicense(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenLicense", ShowLicenseDialog);
        }

        public void OnRibbonConvertFonts(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonConvertFonts", () => ExecuteToolkitConvertFonts(selectionOnly: false));
        }

        public void OnRibbonNumeralsToMyanmar(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonNumeralsToMyanmar", () => ExecuteToolkitFormatNumerals(toMyanmarDigits: true, selectionOnly: false));
        }

        public void OnRibbonCleanPunctuation(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonCleanPunctuation", () => ExecuteToolkitFormatPunctuation(selectionOnly: false));
        }

        public void OnRibbonOpenToolkitPane(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonOpenToolkitPane", () =>
            {
                var ctrl = GetOrCreateActiveWindowTaskPaneControl(autoShow: true);
                ctrl?.OpenToolkitTab();
            });
        }

        public void OnRibbonRewriteOfficialProse(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonRewriteOfficialProse", () =>
            {
                bool selOnly = false;
                try
                {
                    Word.Selection? sel = _wordApp?.Selection;
                    if (sel?.Range != null && sel.Range.Start != sel.Range.End)
                        selOnly = true;
                }
                catch { }
                ExecuteToolkitRewriteOfficialProse(selectionOnly: selOnly);
            });
        }

        public void OnRibbonInsertOfficialLetter(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonInsertOfficialLetter", () =>
            {
                ExecuteInsertOfficialTemplate(OfficialTemplateKind.MinistryOfficialLetter, DateTime.Today);
            });
        }

        public void OnRibbonExportAuditReport(Office.IRibbonControl control)
        {
            AddInDiagnostics.SafeExecute("OnRibbonExportAuditReport", ExecuteExportAuditCertificate);
        }

        private void ShowLicenseDialog()
        {
            int totalEntries = (_proofingEngine?.Trie.WordCount ?? 0) + (_proofingEngine?.Trie.ReferenceCount ?? 0);
            using (var dlg = new LicenseActivationForm(DefaultVendorPublicKeyXml, _licenseFilePath, totalEntries))
            {
                dlg.ShowDialog();
            }
        }

        public void OnDisconnection(ext_DisconnectMode removeMode, ref Array custom)
        {
            try
            {
                if (_isPrimaryInstance)
                {
                    lock (s_instanceGate)
                    {
                        s_primaryInstanceActive = false;
                    }
                    _isPrimaryInstance = false;
                }

                _startupTaskPaneTimer?.Stop();
                _startupTaskPaneTimer?.Dispose();
                _startupTaskPaneTimer = null;

                if (_wordApp != null)
                {
                    _wordApp.WindowSelectionChange -= OnWindowSelectionChange;
                    _wordApp.WindowBeforeRightClick -= OnWindowBeforeRightClick;
                    _wordApp.WindowActivate -= OnWindowActivate;
                    _wordApp.WindowDeactivate -= OnWindowDeactivate;
                    _wordApp.DocumentOpen -= OnDocumentOpen;
                    ((Word.ApplicationEvents4_Event)_wordApp).NewDocument -= OnNewDocument;
                    _wordApp.DocumentBeforeSave -= OnDocumentBeforeSave;
                    _wordApp.DocumentBeforePrint -= OnDocumentBeforePrint;
                    _wordApp.DocumentBeforeClose -= OnDocumentBeforeClose;
                }
                _contextMenuHandler?.CloseActivePopup();
                _contextMenuHandler?.ClearCustomContextMenuItems();
                _documentScanner?.Dispose();
                _underlineManager?.Dispose();
                _uiMarshaller?.Dispose();
                _uiMarshaller = null;
            }
            catch
            {
                // Ignore
            }
        }

        public void OnAddInsUpdate(ref Array custom) { }
        public void OnStartupComplete(ref Array custom) { }
        public void OnBeginShutdown(ref Array custom) { }
    }
}
