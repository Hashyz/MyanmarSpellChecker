// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Htet Yar Zar (ထက်ရာဇာ)
// M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University
// Kyaukse, Mandalay Region, Myanmar. Preserving author attribution is mandatory under GPLv3 Section 7(b) (see NOTICE).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MyanmarSpellChecker.Core.Localization;
using MyanmarSpellChecker.Core.Models;
using MyanmarSpellChecker.Core.Spreadsheet;

namespace MyanmarSpellChecker.ExcelAddIn.UI
{
    /// <summary>
    /// Modern High-DPI floating Quick-Fix Choice Card for Microsoft Excel.
    /// Appears directly beneath (or above) a flagged cell when the user selects or clicks a cell with Myanmar
    /// spelling, grammar, encoding, or contextual meaning issues — matching Microsoft Word's MyanmarQuickFixPopup
    /// while keeping Excel's keyboard/cell navigation focus 100% intact.
    /// </summary>
    public sealed class ExcelQuickFixPopup : Form
    {
        private const int CS_DROPSHADOW = 0x00020000;
        private const int SW_SHOWNOACTIVATE = 4;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(
            IntPtr hWnd,
            IntPtr hWndInsertAfter,
            int x,
            int y,
            int cx,
            int cy,
            uint uFlags);

        private readonly Color _accentColor;
        private readonly Color _borderColor = Color.FromArgb(190, 202, 220);
        private bool _closingProgrammatically;
        private Rectangle? _lastAnchorRect;
        private Rectangle? _currentScreenBounds;

        public event Action? PopupClosedSafe;

        public Rectangle? CurrentScreenBounds => (!_closingProgrammatically && Visible && !IsDisposed) ? _currentScreenBounds : null;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= CS_DROPSHADOW;
                return cp;
            }
        }

        public ExcelQuickFixPopup(
            SpreadsheetCellIssue item,
            Action<string> onSelectReplacement,
            Action onIgnoreOnce,
            Action? onAddToDictionary = null,
            int duplicateCount = 1,
            Action<string>? onBatchReplaceAllMatching = null,
            int cellIssueIndex = 0,
            int totalCellIssues = 1,
            Action<int>? onStepCellIssue = null)
        {
            if (item == null)
                throw new ArgumentNullException(nameof(item));

            ProofingIssue issue = item.Issue;
            bool isGrammar = issue.IsGrammarOrStyle;
            bool isContextWarning = issue.IsContextualWarning;
            bool isEncoding = issue.IssueType == ProofingIssueType.ZawgyiEncodingError ||
                              issue.IssueType == ProofingIssueType.LookalikeCharacterError;

            _accentColor = (isEncoding || isContextWarning)
                ? Color.FromArgb(217, 138, 18)
                : (isGrammar ? Color.FromArgb(26, 115, 232) : Color.FromArgb(217, 48, 37));

            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            ResizeRedraw = true;
            FormBorderStyle = FormBorderStyle.None;
            ControlBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.White;
            Font = UiFontCache.GetMyanmar(9.5F, FontStyle.Regular);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(380, 118);
            MaximumSize = new Size(480, 740);
            Padding = new Padding(14, 13, 14, 12);

            const int innerWrapWidth = 440;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                BackColor = Color.White,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

            int row = 0;

            // 1. Header Bar: Category Badge + Cell Location & Multi-Issue Stepper (if >= 2 issues in cell)
            var headerRow = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = totalCellIssues >= 2 && onStepCellIssue != null ? 3 : 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 4),
                Padding = new Padding(0)
            };
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            if (totalCellIssues >= 2 && onStepCellIssue != null)
            {
                headerRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            }
            headerRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            string categoryTitle = isEncoding
                ? UiLocalizer.Pick("● Unicode Encoding / Character Fix", "● ယူနီကုဒ် အက္ခရာ စံနှုန်း ပြင်ဆင်ရန်")
                : (isContextWarning
                    ? UiLocalizer.Pick("● Contextual Meaning Check", "● အနက်သံသယ စိစစ်ရန် (Context Check)")
                    : (isGrammar
                        ? UiLocalizer.Pick("● Grammar & Style Suggestion", "● သဒ္ဒါနှင့် အရေးအသား ရွေးချယ်ပြင်ဆင်ရန်")
                        : UiLocalizer.Pick("● Spelling Suggestion", "● စာလုံးပေါင်းအမှန် ရွေးချယ်ပြင်ဆင်ရန်")));

            var lblCategory = new Label
            {
                Text = categoryTitle,
                Font = UiFontCache.GetMyanmar(9F, FontStyle.Bold),
                ForeColor = _accentColor,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(2, 1, 2, 1)
            };

            string locBadge = !string.IsNullOrWhiteSpace(item.CellAddress)
                ? (totalCellIssues >= 2
                    ? $"{item.CellAddress} ({cellIssueIndex + 1}/{totalCellIssues})"
                    : item.CellAddress)
                : UiLocalizer.Pick("Esc to close", "Esc ပိတ်ရန်");

            var lblLocation = new Label
            {
                Text = locBadge,
                Font = UiFontCache.GetMyanmar(8.2F, FontStyle.Bold),
                ForeColor = Color.FromArgb(88, 100, 120),
                TextAlign = ContentAlignment.MiddleRight,
                AutoSize = true,
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 0, 2, 0),
                Padding = new Padding(2, 2, 2, 1)
            };

            headerRow.Controls.Add(lblCategory, 0, 0);
            headerRow.Controls.Add(lblLocation, 1, 0);

            if (totalCellIssues >= 2 && onStepCellIssue != null)
            {
                var btnNextInCell = new Button
                {
                    Text = "▶",
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(238, 243, 252),
                    ForeColor = Color.FromArgb(24, 78, 165),
                    Font = UiFontCache.GetMyanmar(7.6F, FontStyle.Bold),
                    Padding = new Padding(3, 0, 3, 0),
                    Margin = new Padding(2, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    UseMnemonic = false
                };
                btnNextInCell.FlatAppearance.BorderColor = Color.FromArgb(196, 212, 238);
                btnNextInCell.Click += (s, e) =>
                {
                    ClosePopupSafely();
                    onStepCellIssue.Invoke((cellIssueIndex + 1) % totalCellIssues);
                };
                headerRow.Controls.Add(btnNextInCell, 2, 0);
            }

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(headerRow, 0, row++);

            // 2. Highlighted Original -> Best Replacement Callout Banner
            SuggestionCandidate? topCand = issue.Suggestions.Count > 0 ? issue.Suggestions[0] : null;
            string diffHeader = topCand != null
                ? $"\"{issue.OriginalText}\"   ➔   \"{topCand.Replacement}\""
                : UiLocalizer.Pick($"Original: \"{issue.OriginalText}\"", $"မူရင်းစာသား: \"{issue.OriginalText}\"");

            Color diffBg = (isEncoding || isContextWarning)
                ? Color.FromArgb(255, 248, 235)
                : (isGrammar ? Color.FromArgb(240, 246, 255) : Color.FromArgb(254, 243, 242));

            var lblOriginal = new Label
            {
                Text = diffHeader,
                Font = UiFontCache.GetMyanmar(10.8F, FontStyle.Bold),
                ForeColor = (isEncoding || isContextWarning)
                    ? Color.FromArgb(165, 80, 0)
                    : (isGrammar ? Color.FromArgb(20, 70, 160) : Color.FromArgb(175, 25, 25)),
                BackColor = diffBg,
                AutoSize = true,
                MaximumSize = new Size(innerWrapWidth, 0),
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 0, 6),
                Padding = new Padding(8, 5, 8, 5)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(lblOriginal, 0, row++);

            // 3. Clickable Replacement Choices First
            var shortcutActions = new List<Action>();
            if (issue.Suggestions.Count > 0)
            {
                var lblChoosePrompt = new Label
                {
                    Text = issue.Suggestions.Count > 1
                        ? UiLocalizer.Pick(
                            $"Click a replacement option ({Math.Min(5, issue.Suggestions.Count)} choices) —",
                            $"ရွေးချယ်လိုသော အမှန်ကို နှိပ်ပါ ({Math.Min(5, issue.Suggestions.Count)} မျိုး) —")
                        : UiLocalizer.Pick(
                            "Click the suggested correction to apply —",
                            "အကြံပြုထားသော စာလုံးပေါင်းအမှန်ကို နှိပ်၍ ပြင်ဆင်ပါ —"),
                    Font = UiFontCache.GetMyanmar(8.6F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(22, 96, 54),
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 4),
                    Padding = new Padding(2, 0, 2, 0)
                };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(lblChoosePrompt, 0, row++);

                int maxButtons = Math.Min(5, issue.Suggestions.Count);
                for (int idx = 0; idx < maxButtons; idx++)
                {
                    SuggestionCandidate candidate = issue.Suggestions[idx];
                    bool isPrimary = idx == 0;
                    string keyHint = isPrimary ? "[1 / Enter]" : $"[{idx + 1}]";

                    Color baseBg = isContextWarning
                        ? (isPrimary ? Color.FromArgb(254, 243, 199) : Color.FromArgb(255, 251, 235))
                        : isGrammar
                            ? (isPrimary ? Color.FromArgb(226, 240, 255) : Color.FromArgb(242, 248, 255))
                            : (isPrimary ? Color.FromArgb(220, 245, 230) : Color.FromArgb(240, 250, 244));
                    Color hoverBg = isContextWarning
                        ? Color.FromArgb(253, 230, 138)
                        : isGrammar
                            ? Color.FromArgb(204, 226, 255)
                            : Color.FromArgb(196, 236, 212);
                    Color borderClr = isContextWarning
                        ? (isPrimary ? Color.FromArgb(217, 138, 18) : Color.FromArgb(245, 194, 107))
                        : isGrammar
                            ? (isPrimary ? Color.FromArgb(130, 178, 242) : Color.FromArgb(182, 208, 245))
                            : (isPrimary ? Color.FromArgb(108, 192, 140) : Color.FromArgb(168, 216, 184));

                    var btnSuggest = new WordWrapChoiceButton
                    {
                        Text = $"✓  {candidate.DisplayLabel}   {keyHint}",
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        MaximumSize = new Size(innerWrapWidth, 0),
                        Dock = DockStyle.Fill,
                        TextAlign = ContentAlignment.MiddleLeft,
                        BackColor = baseBg,
                        ForeColor = Color.FromArgb(14, 38, 72),
                        FlatStyle = FlatStyle.Flat,
                        Font = UiFontCache.GetMyanmar(isPrimary ? 9.8F : 9.3F, FontStyle.Bold),
                        Padding = new Padding(10, 6, 10, 6),
                        Margin = new Padding(0, 0, 0, 5),
                        Cursor = Cursors.Hand
                    };
                    btnSuggest.FlatAppearance.BorderSize = 1;
                    btnSuggest.FlatAppearance.BorderColor = borderClr;
                    btnSuggest.FlatAppearance.MouseOverBackColor = hoverBg;

                    string replacement = candidate.Replacement;
                    Action applyAction = () =>
                    {
                        ClosePopupSafely();
                        onSelectReplacement?.Invoke(replacement);
                    };
                    shortcutActions.Add(applyAction);
                    btnSuggest.Click += (s, e) => applyAction();

                    root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    root.Controls.Add(btnSuggest, 0, row++);
                }
            }

            Action? triggerBatchAllMatching = null;
            if (duplicateCount >= 2 && topCand != null && onBatchReplaceAllMatching != null)
            {
                string batchRep = topCand.Replacement;
                triggerBatchAllMatching = () =>
                {
                    ClosePopupSafely();
                    onBatchReplaceAllMatching.Invoke(batchRep);
                };

                var btnBatchSame = new WordWrapChoiceButton
                {
                    Text = UiLocalizer.Pick(
                        $"✓✓  Fix all {duplicateCount} occurrences of \"{issue.OriginalText}\" ➔ {batchRep}   [A]",
                        $"✓✓  စာရင်းဇယားရှိ \"{issue.OriginalText}\" ({duplicateCount}) ခုလုံး ပြင်မည် ➔ {batchRep}   [A]"),
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    MaximumSize = new Size(innerWrapWidth, 0),
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.FromArgb(215, 244, 236),
                    ForeColor = Color.FromArgb(10, 82, 60),
                    FlatStyle = FlatStyle.Flat,
                    Font = UiFontCache.GetMyanmar(9.0F, FontStyle.Bold),
                    Padding = new Padding(10, 5, 10, 5),
                    Margin = new Padding(0, 0, 0, 5),
                    Cursor = Cursors.Hand
                };
                btnBatchSame.FlatAppearance.BorderSize = 1;
                btnBatchSame.FlatAppearance.BorderColor = Color.FromArgb(102, 190, 165);
                btnBatchSame.FlatAppearance.MouseOverBackColor = Color.FromArgb(192, 236, 224);
                btnBatchSame.Click += (s, e) => triggerBatchAllMatching();

                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(btnBatchSame, 0, row++);
            }

            // 4. Concise Issue Message
            if (!string.IsNullOrWhiteSpace(issue.Message))
            {
                var lblExplanation = new Label
                {
                    Text = issue.Message,
                    Font = UiFontCache.GetMyanmar(8.8F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(48, 58, 74),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrapWidth, 0),
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 2, 0, 4),
                    Padding = new Padding(2, 2, 2, 2)
                };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(lblExplanation, 0, row++);
            }

            // 5. Expandable Linguistic & Dictionary Details Box
            var details = new List<string>(6);
            if (!string.IsNullOrWhiteSpace(issue.GrammaticalReasoning))
            {
                details.Add(UiLocalizer.Pick($"• Analysis: {issue.GrammaticalReasoning}", $"• သုံးသပ်ချက်: {issue.GrammaticalReasoning}"));
            }

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.DiagnosticDetail) &&
                (string.IsNullOrWhiteSpace(issue.GrammaticalReasoning) ||
                 issue.GrammaticalReasoning.IndexOf(topCand.DiagnosticDetail, StringComparison.Ordinal) < 0))
            {
                details.Add(UiLocalizer.Pick($"• Distinction: {topCand.DiagnosticDetail}", $"• ကွာခြားချက်: {topCand.DiagnosticDetail}"));
            }

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.AbidanSummary))
            {
                details.Add(UiLocalizer.Pick($"• Dictionary Note: {topCand.AbidanSummary}", $"• အဘိဓာန်မှတ်ချက်: {topCand.AbidanSummary}"));
            }

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.SyllableBreakdown))
            {
                string posText = DictionaryEntry.FormatLocalizedPosTag(topCand.PosTag);
                string auth = topCand.IsOfficialThatpon
                    ? UiLocalizer.Pick("Official Thatpon ✓", "သတ်ပုံကျမ်းဝင် ✓")
                    : UiLocalizer.Pick("Standard Usage", "ခေတ်သုံး စံသတ်ပုံ");
                details.Add(UiLocalizer.Pick(
                    $"• Syllables: {topCand.SyllableBreakdown}  |  {posText} ({auth})",
                    $"• ဝဏ္ဏခွဲ: {topCand.SyllableBreakdown}  |  {posText} ({auth})"));
            }

            if (topCand != null && !string.IsNullOrWhiteSpace(topCand.HomophoneSiblings))
            {
                details.Add(UiLocalizer.Pick($"• Homophones: {topCand.HomophoneSiblings}", $"• အသံတူသတိပြုရန်: {topCand.HomophoneSiblings}"));
            }

            if (!string.IsNullOrWhiteSpace(issue.AuthoritativeSource))
            {
                details.Add(UiLocalizer.Pick($"• Reference: {issue.AuthoritativeSource}", $"• ကျမ်းကိုး: {issue.AuthoritativeSource}"));
            }

            if (details.Count > 0)
            {
                var lblDetailBox = new Label
                {
                    Text = string.Join("\n", details),
                    Font = UiFontCache.GetMyanmar(8.4F, FontStyle.Regular),
                    ForeColor = Color.FromArgb(35, 52, 78),
                    BackColor = Color.FromArgb(245, 248, 253),
                    AutoSize = true,
                    MaximumSize = new Size(innerWrapWidth, 0),
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 6),
                    Padding = new Padding(8, 6, 8, 6),
                    Visible = false
                };

                var btnToggleDetails = new Label
                {
                    Text = UiLocalizer.Pick("▾ Show Grammar & Dictionary Details", "▾ သဒ္ဒါ/အဘိဓာန် သုံးသပ်ချက် အသေးစိတ်ကြည့်ရန်"),
                    Font = UiFontCache.GetMyanmar(8.4F, FontStyle.Bold),
                    ForeColor = Color.FromArgb(24, 88, 178),
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Margin = new Padding(0, 0, 0, 5),
                    Padding = new Padding(2, 1, 2, 2),
                    Cursor = Cursors.Hand
                };
                btnToggleDetails.Click += (s, e) =>
                {
                    bool expand = !lblDetailBox.Visible;
                    SuspendLayout();
                    lblDetailBox.Visible = expand;
                    btnToggleDetails.Text = expand
                        ? UiLocalizer.Pick("▴ Hide Details", "▴ အသေးစိတ် သုံးသပ်ချက် ပြန်ဖျောက်ရန်")
                        : UiLocalizer.Pick("▾ Show Grammar & Dictionary Details", "▾ သဒ္ဒါ/အဘိဓာန် သုံးသပ်ချက် အသေးစိတ်ကြည့်ရန်");
                    ResumeLayout(true);
                    RepositionWithinScreenBounds();
                };

                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(btnToggleDetails, 0, row++);
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.Controls.Add(lblDetailBox, 0, row++);
            }

            // 6. Bottom Footer Row (Ignore [I] | +Add Word [D] | Close [Esc])
            bool showAddDict = !isGrammar && !isContextWarning && issue.IssueType == ProofingIssueType.SpellingError && onAddToDictionary != null;
            var footerGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = showAddDict ? 3 : 2,
                RowCount = 1,
                Margin = new Padding(0, 2, 0, 0)
            };
            if (showAddDict)
            {
                footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
                footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39F));
                footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));
            }
            else
            {
                footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
                footerGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            }
            footerGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            Action triggerIgnore = () =>
            {
                ClosePopupSafely();
                onIgnoreOnce?.Invoke();
            };

            var btnIgnore = CreateFooterButton(
                showAddDict
                    ? UiLocalizer.Pick("Ignore (I)", "ကျော်မည် (I)")
                    : UiLocalizer.Pick("Ignore Once (I)", "ယခုစာအုပ်၌ ကျော်မည် (I)"),
                Color.FromArgb(243, 245, 249),
                Color.FromArgb(65, 72, 84),
                Color.FromArgb(215, 220, 230),
                new Padding(0, 0, 5, 0));
            btnIgnore.Click += (s, e) => triggerIgnore();

            var btnClose = CreateFooterButton(
                UiLocalizer.Pick("Close (Esc)", "ပိတ်မည် (Esc)"),
                Color.FromArgb(243, 245, 249),
                Color.FromArgb(65, 72, 84),
                Color.FromArgb(215, 220, 230),
                new Padding(0));
            btnClose.Click += (s, e) => ClosePopupSafely();

            footerGrid.Controls.Add(btnIgnore, 0, 0);
            if (showAddDict)
            {
                var btnAddDict = CreateFooterButton(
                    UiLocalizer.Pick("+Add Word (D)", "+အဘိဓာန်မှတ်မည် (D)"),
                    Color.FromArgb(236, 244, 255),
                    Color.FromArgb(20, 72, 155),
                    Color.FromArgb(185, 210, 245),
                    new Padding(0, 0, 5, 0));
                btnAddDict.Click += (s, e) =>
                {
                    ClosePopupSafely();
                    onAddToDictionary?.Invoke();
                };
                footerGrid.Controls.Add(btnAddDict, 1, 0);
                footerGrid.Controls.Add(btnClose, 2, 0);
            }
            else
            {
                footerGrid.Controls.Add(btnClose, 1, 0);
            }

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(footerGrid, 0, row++);

            Controls.Add(root);
            UiFontCache.DisableMnemonicsRecursive(this);

            Deactivate += (s, e) => ClosePopupSafely();
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                {
                    e.Handled = true;
                    ClosePopupSafely();
                    return;
                }

                if (e.KeyCode == Keys.Enter && shortcutActions.Count > 0)
                {
                    e.Handled = true;
                    shortcutActions[0].Invoke();
                    return;
                }

                int digitIndex = -1;
                if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5)
                    digitIndex = e.KeyCode - Keys.D1;
                else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad5)
                    digitIndex = e.KeyCode - Keys.NumPad1;

                if (digitIndex >= 0 && digitIndex < shortcutActions.Count)
                {
                    e.Handled = true;
                    shortcutActions[digitIndex].Invoke();
                    return;
                }

                if (e.KeyCode == Keys.I)
                {
                    e.Handled = true;
                    triggerIgnore();
                    return;
                }

                if (e.KeyCode == Keys.D && showAddDict)
                {
                    e.Handled = true;
                    ClosePopupSafely();
                    onAddToDictionary?.Invoke();
                    return;
                }

                if (e.KeyCode == Keys.A && triggerBatchAllMatching != null)
                {
                    e.Handled = true;
                    triggerBatchAllMatching.Invoke();
                }
            };
        }

        private static Button CreateFooterButton(string text, Color bg, Color fg, Color border, Padding margin)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                BackColor = bg,
                ForeColor = fg,
                FlatStyle = FlatStyle.Flat,
                Font = UiFontCache.GetMyanmar(8.5F, FontStyle.Regular),
                Padding = new Padding(5, 4, 5, 4),
                Margin = margin,
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            btn.FlatAppearance.BorderColor = border;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(
                Math.Max(0, bg.R - 12),
                Math.Max(0, bg.G - 10),
                Math.Max(0, bg.B - 6));
            return btn;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            using (var accentBrush = new SolidBrush(_accentColor))
            {
                e.Graphics.FillRectangle(accentBrush, 0, 0, Width, 4);
            }

            using (var borderPen = new Pen(_borderColor, 1F))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
            }
        }

        public void ShowNearCellOrCursor(Rectangle? cellScreenRect, bool focusPopup)
        {
            _lastAnchorRect = cellScreenRect;
            Point targetPt = ComputePopupLocation(cellScreenRect, out int popupW, out int popupH);
            Location = targetPt;
            _currentScreenBounds = new Rectangle(targetPt.X, targetPt.Y, popupW, popupH);

            if (focusPopup)
            {
                Show();
                Activate();
            }
            else
            {
                IntPtr handle = Handle;
                ShowWindow(handle, SW_SHOWNOACTIVATE);
                SetWindowPos(handle, HWND_TOPMOST, targetPt.X, targetPt.Y, popupW, popupH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        private void RepositionWithinScreenBounds()
        {
            Point pt = ComputePopupLocation(_lastAnchorRect, out int popupW, out int popupH);
            Location = pt;
            Size = new Size(popupW, popupH);
            _currentScreenBounds = new Rectangle(pt.X, pt.Y, popupW, popupH);
        }

        private Point ComputePopupLocation(Rectangle? cellScreenRect, out int popupW, out int popupH)
        {
            PerformLayout();
            Size measured = GetPreferredSize(new Size(480, 0));
            popupW = Math.Max(MinimumSize.Width, Math.Min(MaximumSize.Width, measured.Width));
            popupH = Math.Max(MinimumSize.Height, Math.Min(MaximumSize.Height, measured.Height));

            Point refPt;
            int x;
            int y;

            if (cellScreenRect.HasValue && cellScreenRect.Value.Height > 0)
            {
                Rectangle anchor = cellScreenRect.Value;
                refPt = new Point(anchor.Left, anchor.Bottom);
                x = anchor.Left;
                y = anchor.Bottom + 6;
            }
            else
            {
                refPt = Cursor.Position;
                x = refPt.X + 8;
                y = refPt.Y + 20;
            }

            Screen screen = Screen.FromPoint(refPt);
            Rectangle workingArea = screen.WorkingArea;

            if (x + popupW > workingArea.Right - 8)
            {
                x = Math.Max(workingArea.Left + 8, workingArea.Right - popupW - 10);
            }
            if (x < workingArea.Left + 8)
            {
                x = workingArea.Left + 8;
            }

            if (y + popupH > workingArea.Bottom - 8)
            {
                int anchorTop = cellScreenRect.HasValue && cellScreenRect.Value.Height > 0
                    ? cellScreenRect.Value.Top
                    : refPt.Y;
                int flippedY = anchorTop - popupH - 8;
                y = flippedY >= workingArea.Top + 8
                    ? flippedY
                    : Math.Max(workingArea.Top + 8, workingArea.Bottom - popupH - 8);
            }

            return new Point(x, y);
        }

        public void ClosePopupSafely()
        {
            if (_closingProgrammatically)
                return;

            _closingProgrammatically = true;
            _currentScreenBounds = null;
            try
            {
                Close();
            }
            catch
            {
                // Ignore
            }
            finally
            {
                PopupClosedSafe?.Invoke();
            }
        }
    }
}
