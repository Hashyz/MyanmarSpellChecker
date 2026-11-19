# မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း — Enterprise Offline Myanmar Proofing, Grammar & Dictionary Suite for Microsoft Word, Excel & PowerPoint

[![CI Gate](https://github.com/Hashyz/MyanmarSpellChecker/actions/workflows/ci.yml/badge.svg)](./.github/workflows/ci.yml)
[![License: GPL v3](https://img.shields.io/badge/License-GPLv3%20%2B%20Section%207b%20Attribution-blue.svg)](./LICENSE)
[![Platform](https://img.shields.io/badge/Office-Word%20%7C%20Excel%20%7C%20PowerPoint-107C41.svg)](#how-to-install--use-the-plugin-word-excel--powerpoint-user-guide)
[![Runtime](https://img.shields.io/badge/.NET%20Framework-4.8%20(100%25%20Offline)-512BD4.svg)](#)
[![Author](https://img.shields.io/badge/Author-Htet%20Yar%20Zar%20(%E1%80%91%E1%80%80%E1%80%BA%E1%80%9B%E1%80%AC%E1%80%87%E1%80%AC)-0B57D0.svg)](./NOTICE)

100% Offline, Air-Gapped **Microsoft Word + Microsoft Excel + Microsoft PowerPoint** Unified 3-in-1 Desktop COM Add-in Suite (`.NET Framework 4.8`, `AnyCPU`) engineered for Government Ministries (`B2G`), Universities, Enterprises, Publishers, Accountants, Educators, and Presenters.

Built on the complete 462-page **"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း"** (Myanmar Language Commission), **"မြန်မာအဘိဓာန်"**, **57,064+ Bilingual English ⇄ Myanmar Lexicon (`အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်`)**, **25,000+ Offline Encyclopedia (`စွယ်စုံဗဟုသုတကျမ်း`)**, **MLIS/MOI Union Statutory Laws**, **MIMU v9.7 / GAD Multi-Tier Gazetteer**, **"ရုံးသုံးမြန်မာစာ"**, and **8 Specialized Academic, Government & Enterprise Domains** (including **Geology & Earth Sciences / ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ**, **Public Administration & Civil Service**, and **Higher Education & Universities**).

> **Complete Bilingual User Guide & Tool Handbook (`EN + မြန်မာ`):**
> - **GitHub Online Edition:** [`docs/USER_GUIDE.md`](./docs/USER_GUIDE.md)
> - **Interactive Searchable Offline HTML Guide:** [`docs/Myanmar-Spell-Checker-User-Guide.html`](./docs/Myanmar-Spell-Checker-User-Guide.html) *(also accessible inside Word, Excel & PowerPoint via **`Myanmar Spell Checker ➔ User Guide ▾`**)*
> - **Printable ISO A4 Word Manual (`.docx`):** [`docs/Myanmar-Spell-Checker-User-Guide.docx`](./docs/Myanmar-Spell-Checker-User-Guide.docx)

---

## Author, Special Dedication & Collaborators

- **Original Creator & Lead Developer:** **Htet Yar Zar (ထက်ရာဇာ)** ([@Hashyz](https://github.com/Hashyz))
- **Academic Credential:** **M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed)** *(မဟာသိပ္ပံ ဒုတိယနှစ် [M.Sc. Part II] ဘူမိဗေဒ အထူးပြု — M.Sc. Part I အောင်မြင်ပြီး)*
- **Department & University:** **Department of Geology, Kyaukse University** *(ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ်)*
- **Location:** **Kyaukse, Mandalay Region, Myanmar** *(ကျောက်ဆည်မြို့၊ မန္တလေးတိုင်းဒေသကြီး၊ မြန်မာနိုင်ငံ)*
- **Special Dedication — Original Idea & Inspiration (`မူလစိတ်ကူးလမ်းညွှန်ပေးသူ`):** **U Moe Kyaw Thu (ဦးမိုးကျော်သူ)** — *Beloved Father of Htet Yar Zar, who originally inspired and gave the idea to build this Myanmar Spell & Grammar Suite (ဤမြန်မာစာ စစ်ဆေးရေးစနစ် ဖန်တီးပြုစုရန် မူလစိတ်ကူးနှင့် လမ်းညွှန်မှုပေးခဲ့သူ ကျေးဇူးရှင် ဖခင်ကြီး ဦးမိုးကျော်သူ)*
- **Project Collaborators (`ပူးပေါင်းပါဝင်သူများ`):** **Hein Htet Zaw (ဟိန်းထက်ဇော်)** & **Thu Reain Khant (သူရိန်ခန့်)**

---

## License & Anti-Plagiarism Notice (GNU GPL v3.0 + Section 7b)

This project is open-source software licensed under the **[GNU General Public License v3.0 (GPL-3.0-or-later)](./LICENSE)** together with mandatory **[Section 7(b) & 7(c) Attribution & Origin Preservation Terms](./NOTICE)**:

1. **Copyleft (No Closed-Source Hijacking):** Any derivative work, modified version, or redistributed binary/installer **must** remain open-source under the GNU GPL v3.0 with full source code made available. Closed-source commercial rebranding is strictly prohibited.
2. **Mandatory Preservation of Author Attribution (GPLv3 Section 7b):** Removing, hiding, or altering the [`NOTICE`](./NOTICE) file, source copyright headers, assembly metadata (`Directory.Build.props`), or visible in-app **Creator & Academic Attribution** credits (`Htet Yar Zar (ထက်ရာဇာ) — Department of Geology, Kyaukse University, Kyaukse, Myanmar`) is legally prohibited.
3. **No Misrepresentation of Origin (GPLv3 Section 7c):** You may **not** claim original authorship of this software or present unmodified/modified copies as your own uncredited academic, institutional, or commercial project. For academic and software citation metadata, see [`CITATION.cff`](./CITATION.cff) (or click **"Cite this repository"** on GitHub).

---

## How to Install & Use the Plugin (Word, Excel & PowerPoint User Guide)

### System Requirements
- **OS:** Windows 10 or Windows 11 (64-bit or 32-bit)
- **Microsoft Office:** Microsoft Word, Microsoft Excel & Microsoft PowerPoint **2016 / 2019 / 2021 / Office 365** (Desktop Edition)
- **Runtime:** `.NET Framework 4.8` (already pre-installed on Windows 10/11 — **no `.NET SDK` required** for installation; `.NET SDK` is only needed if editing/recompiling C# source code)
- **Recommended Font:** **Pyidaungsu** Unicode Font (for crisp Myanmar rendering)

---

### Step 1 — Install & Register the Unified 3-in-1 Suite (Word + Excel + PowerPoint)

#### Option A — 1-Click Friendly Setup (Recommended for All Users — Zero `.NET SDK` / Zero Admin Needed)
1. **Download or Clone:** Click **`Code ➔ Download ZIP`** on GitHub *(or download `MyanmarSpellChecker-EasySetup-OfficeSuite.zip`)* and **Right-click the `.zip` file ➔ `Extract All...`** into a normal folder first.
2. **Close Microsoft Word, Excel, and PowerPoint** (save any open documents first).
3. **Double-click `Install-MyanmarSpellChecker.bat`** inside the extracted folder *(or **`Install-EasyUser.bat`** inside the standalone Release ZIP — **no PowerShell commands, no `.NET SDK`, and no Administrator password required!**)*.
4. A friendly confirmation popup (`✓ Installed successfully!`) will appear once the **15.17 MB (`96,276` spellcheck headwords + `57,064` Bilingual `EN ⇄ MM` entries + `25,001` Encyclopedia articles = `183,000+` compiled entries)** offline dictionary and all three **Word + Excel + PowerPoint** Add-ins are installed into `%LocalAppData%\MyanmarSpellChecker\app`.
5. **Open Microsoft Word, Excel, or PowerPoint** — you will see the **`Myanmar Spell Checker`** Ribbon tab (plus a quick-launch button inside Office's **`Review`** tab) and the **4-Tab Myanmar Spell Checker Task Pane** docked on the right!
   - **Default UI Language is English (`🌐 EN`)** — click **`🌐 EN` ⇄ `🌐 မြန်မာ`** in the Task Pane header (or **`UI: English ➔ မြန်မာ`** on the Ribbon) anytime to switch the entire interface live between **English (`Myanmar Spell Checker`)** and **Myanmar (`မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး`)** without restarting Word, Excel, or PowerPoint!

#### Option B — Developer CLI Setup (From `git clone`)
1. **Clone and build Release binaries** (`data\mlc_thatpon.db` is tracked directly in Git and automatically copied to `bin\Release\net48\`):
   ```powershell
   git clone https://github.com/Hashyz/MyanmarSpellChecker.git
   cd MyanmarSpellChecker
   dotnet build .\MyanmarSpellChecker.sln -c Release
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Register-DevAddIn.ps1
   ```
   *(Optional — for IT / Ministry-wide deployment across all users on a PC, run `Build-EnterpriseRelease.ps1` and execute `artifacts\release-package\Install-MinistryGPO.cmd` as Administrator, or share the generated `artifacts\release-package` folder as a `.zip` with `Install-EasyUser.bat`.)*

> **Tip (If the Ribbon Tab Does Not Appear):** In Word, Excel, or PowerPoint, go to **File ➔ Options ➔ Add-ins ➔ Manage: COM Add-ins ➔ Go...**, check **`Myanmar Spell Checker`**, and click **OK**.

---

### Step 2 — How to Use in Microsoft Word (`Myanmar Spell Checker` / `မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး` Tab)

#### A. Real-Time & Full-Document Proofing
- **Live Typing & Paste Scan:** As you type or paste paragraphs (including web/Facebook/PDF text containing hidden zero-width characters), the add-in automatically checks your text in the background without freezing Word.
- **Scan Full Document (`စာတမ်းတစ်ခုလုံး စစ်ဆေးမည်`):** Scans Main Body, Tables, Headers, and Footers all at once.
- **Color-Coded Wavy Underlines:**
  - **Red Wavy Underline (`သတ်ပုံအမှား`):** Spelling errors, wrong medial/coda/tone, misplaced kinzi (`သကေင်္တ ➔ သင်္ကေတ`), Zawgyi/lookalike characters (`၀ ➔ ဝ`, `၄င်း ➔ ၎င်း`), and high-certainty wrong-meaning homophones (`ကြိုးချဉ် ➔ ကြိုးချည်`).
  - **Blue Wavy Underline (`သဒ္ဒါ / ရုံးသုံးစာဟန်`):** Grammar rules (`တ / တစ်`, `ခုနစ် / ခုနှစ်`, `မ...ဘဲ`, `လဲ / လည်း`, numeral classifiers) and colloquial-to-literary style (`ပါတယ် ➔ ပါသည်` when Formal mode is on).
  - **Amber / Orange Wavy Underline (`အနက်သံသယ`):** Contextual meaning/collocation warnings (`ဥပဒေ ချိုးဖဲ့ ➔ ချိုးဖောက်`) surfaced for human review — **automatically skipped** by blind *Fix All* so valid sentences are never overwritten by accident!
- **3 Ways to Fix Any Error:**
  1. **Click directly on an underlined word** in the document to open the **On-Cursor Quick-Fix Popup** with ranked suggestions, meanings, and `မှတ်သားမည်` (*Add to Custom Dictionary*) / `ကျော်မည်` (*Ignore*).
  2. **Right-click an underlined word** to pick corrections directly from Word's context menu.
  3. **Click any Issue Card in the right-hand Task Pane (`စာစစ်` tab)** to jump to that exact word in the document and click a green `✓ ပြင်မည် ➔ ...` button (or click **`အားလုံးပြင်`** to fix all safe spelling & grammar errors in a single `Ctrl+Z` undo step).
- **Safe Save & Print:** All temporary red/blue/amber proofing underlines are automatically stripped before saving or printing so your `.docx` file stays 100% clean.

#### B. Choosing Writing Style Mode (3-Mode Selector on Ribbon & Task Pane)
- **`Casual / Chat` (`ပေါ့ပါးဟန်`):** Checks official spelling and homophones while allowing conversational particles and everyday Pali-sans-that shortcuts (`ပြဿနာ`, `ပုဒ်မ`, `ဥပမာ`).
- **`Standard` (`အထွေထွေစံ` — General Standard Mode):** Checks official 462-page spelling (`သတ်ပုံကျမ်း`), homophones, and core grammar while allowing natural polite spoken particles (`တယ်`, `မယ်`, `တွေ`, `နဲ့`, `ဘူး`).
- **`Official` (`ရုံးသုံးစာပေ` — Formal / Official Mode):** Enforces full formal literary and government ministry prose standards (`ပါတယ် ➔ ပါသည်`, `တွေ ➔ များ`, `နဲ့ ➔ နှင့်`, `ဒါပေမယ့် ➔ သို့သော်လည်း`).

#### C. Using the 4-Tab Custom Task Pane in Word
1. **`စာစစ်` (Proofing Tab):** Filter issues by **All (`အားလုံး`)**, **Spelling (`သတ်ပုံ`)**, **Grammar (`သဒ္ဒါ`)**, or **Context (`အနက်`)**, step through issues with **`↑` / `↓`**, and read deep Burmese linguistic & Pali etymological explanations on every card.
2. **`အဘိဓာန်` (Bilingual Dictionary, Phonetics & Encyclopedia Tab):** Instant offline search across **181,400+ entries**:
   - **Bilingual English ⇄ Myanmar (`57,064` English Headwords & Idioms + `9,790+` Enriched Myanmar-to-English Headwords):** Search in English (`"abandon"`, `"negotiate"`), Myanmar (`"သတ်ပုံ"`), **Romanized Burmese** (`"mingalaba"`, `"thatpon"`, `"kyaukse"`), or pasted **Zawgyi** (auto-converted to Unicode on the fly).
   - **Dual Spoken Pronunciation + Offline English Audio (`▶ EN`):** Displays Burmese spoken sandhi + easy Romanization (`သပ်-ပုံ • that-pon`, IPA tooltip `[θaʔ.bòʊɴ]`) and zero-dependency offline Windows SAPI English TTS playback.
   - **Numbered Multi-Sense Cards, Corpus Collocations (`▸`) & Shared Bookmarks (`★ Saved`):** Clean `1.`, `2.` sense formatting, real-world corpus collocation explorer backed by `110,000` bigrams, `← Back` / `Next →` lookup history, and `★ Saved` bookmarks live-synced across Word & Excel.
   - Filter by **`သတ်ပုံကျမ်း`**, **`အဘိဓာန်`**, **`ဗဟုသုတ` (25,000+ Encyclopedia articles)**, **`နယ်ပယ်သုံး` (Geology `ဘူမိဗေဒ`, Public Administration & Civil Service, Universities & Research, Legal, Finance, Medical, Pali, IT)**, **`ဒေသ/အမည်` (9,200+ MIMU v9.7 / GAD Districts, 330+ Townships, Towns, Wards & Village Tracts)**, and **`စကားပုံ` (Proverbs)**.
3. **`ရုံးစာကိရိယာ` (Official Writing Toolkit Tab):**
   - **Legacy Font Converter (`WinInnwa/Zawgyi ➔ Pyidaungsu`):** Converts legacy fonts across the document or selection without breaking English text, tables, or bold/italic formatting.
   - **Smart Numeral Converter (`0-9 ➔ ၀-၉`):** Converts digits to Myanmar numerals while preserving `ISO-9001`, `COVID-19`, `A4`, `5G`, URLs, and emails.
   - **Kyat Lakh Number-to-Words (`ကျပ်သိန်း စာသားပြောင်းစက်`):** Converts numbers like `1,500,000` into official Burmese banking/budget words (`ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ`).
   - **Traditional Myanmar Calendar (`မြန်မာသက္ကရာဇ် ပြက္ခဒိန်`):** Converts any Gregorian date to `သာသနာသက္ကရာဇ်`, `မြန်မာသက္ကရာဇ်`,waxing/waning moon day, and Sabbath (`ဥပုသ်`) status.
   - **Official Letter Templates & Printable Quality Audit (`အရည်အသွေး အစီရင်ခံစာ`):** Inserts standard `ရုံးစာ` templates and generates a printable `A+`–`D` quality audit certificate.
4. **`ကိုယ်ပိုင်စာလုံး` (Custom Words Tab):** Add personal names, university/department terms, or local place names once — shared live between Word and Excel!

---

### Step 3 — How to Use in Microsoft Excel (`Myanmar Spell Checker` / `မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး` Tab)

The Excel Add-in provides **100% Non-Destructive Screen-Only Wavy Underlines, Word-Style On-Cursor Quick-Fix Popups & Formula-Safe Accounting Tools**:

1. **Screen-Only Red / Blue / Amber Wavy Underlines (Zero Cell Color Mutation — Never Saved into `.xlsx`):**
   - Flagged words inside visible plain-text cells are highlighted on screen via a High-DPI click-through Win32 layered overlay using **Red Wavy Underline (`သတ်ပုံအမှား`)**, **Blue Wavy Underline (`သဒ္ဒါ/စာဟန်`)**, or **Amber Wavy Underline (`အနက်သံသယ`)**.
   - **Your cell text color, font formatting, Clipboard (`Ctrl+C`), native `Ctrl+Z`, and saved `.xlsx` file are 100% untouched** — opening the spreadsheet on another PC without the plugin installed always shows clean normal black text.
2. **Word-Style On-Cursor Quick-Fix Popup + Right-Click Menu:**
   - **Click or arrow-navigate onto any flagged cell** (e.g. `A1`) to see the floating **Word-Style Quick-Fix Popup** directly below the cell (without stealing keyboard focus!) with 1-click suggestions, multi-issue cell stepping (`1/2 ▶`), batch same-word fixes, expandable dictionary/grammar notes, `Ignore`, and `+Add Word`.
   - **Right-click any flagged cell** to pick top suggestions directly from Excel's native cell context menu.
3. **Instant Open + Non-Blocking Streaming Full Scan (46,000–1,000,000 Cells):**
   - **Zero `"Excel Not Responding"` Freeze on Huge Spreadsheets:** Workbooks with `46,000`–`1,000,000` cells open instantaneously (`< 0.2 s`), render visible screen wavy underlines immediately via viewport-first priority slicing, and gently stream 100% of the active sheet in background `250 × 20` slices (`26–110 ms` UI breathing cadence) with live progress (`⏳ Checking 46,000 cells... 68% (312 issues)`) and a **`[■ Stop]`** button.
   - **Full True Count (Up to `50,000` Issues) + 1-Click Repeated-Word Batch Fix (`⚡ Repeated Issues`):** Displays the exact true issue count across massive sheets and groups top repeated typos at the top of the Task Pane so identical spelling errors across thousands of rows can be fixed in 1 click!
   - **Choose Scan Scope (Ribbon or Task Pane):** **`Scan Sheet`**, **`Scan Selection`**, **`Scan Workbook`**, and **`Live Check`** (automatic incremental cell check on `Enter`).
4. **100% Formula & Number Protection Guarantee:**
   - Cells containing Excel formulas (`=SUM(...)`, `=VLOOKUP(...)`, `=IF(...)`, `=TEXT(...)`), pure accounting numbers, currencies, percentages, and dates are **strictly shielded** and **never** modified during single fixes, `Fix All`, font conversion, or numeral conversion.
5. **Click-to-Navigate, Fix & 25-Step Undo (`↶ Undo`):**
   - Click any issue card (e.g. `Sheet1!B4`) in the Task Pane to jump straight to that sheet and cell in Excel.
   - Click **`Undo Last` / `↶ Undo`** on the Ribbon or Task Pane at any time to restore up to **25 levels** of previous cell edits!
6. **Excel Accounting & Audit Tools:**
   - Select any numeric budget cell in Excel and open the **`Toolkit` (`ကိရိယာ`)** tab — the number is automatically captured and converted into **Kyat Lakh (`ကျပ်သိန်း...တိတိ`)** Burmese words ready to copy or insert!
   - Click **`Audit Report` (`အရည်အသွေး အစီရင်ခံစာ`)** to generate a formatted audit report workbook listing quality scores and cell-by-cell findings.

---

### Step 4 — How to Use in Microsoft PowerPoint (`Myanmar Spell Checker` / `မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး` Tab)

1. **Deep Multi-Container Slide & Notes Scanning:**
   - Checks **Slide TextBoxes, Titles, Subtitles, Bullet Placeholders, Slide Tables (`[Row, Col]`), Recursively Nested Grouped Shapes (`GroupItems`), and Speaker Notes** — plus an optional **`Include Masters`** toggle for Slide Masters & Custom Layouts!
   - **Live Active-Slide Auto-Scan (`450 ms` idle debounce) + 1-Click `All Slides` Deck Scan** with cross-slide variant consistency harmonization (e.g., `ကော်မတီ` vs. `ကော်မီတီ` across slides).
2. **100% Zero-Formatting-Mutation Wavy Overlay & Slide Show (`F5`) Shield:**
   - Draws crisp Red, Blue, and Amber wavy squiggles on a click-through Win32 transparent overlay via `TextRange` screen bounds — **never modifying `.pptx` font colors, underlines, themes, or animations**.
   - **Automatically suspends** scanning, overlays, and popups whenever **Slide Show (`F5` / Presenter View)** starts so live presentations run at 100% native frame rate with zero popups!
3. **Surgical Character-Span Fixes & Presentation Toolkit (`ကိရိယာ`):**
   - Applies replacements via 1-based `TextRange.Characters(start + 1, len).Text` in descending offset order wrapped in `StartNewUndoEntry()` + **30-step Presentation Undo** so bold, font size, RGB colors, hyperlinks, and slide animations stay 100% intact.
   - Includes **Legacy `WinInnwa / Zawgyi ➔ Pyidaungsu Unicode`**, **1-Click `Unify All Myanmar Slide Runs ➔ Pyidaungsu`** (preserving English brand Latin fonts), and **Slide Title & Bullet Smart-ZWSP Line-Wrap Studio** so Burmese titles never break mid-syllable!

---

### Step 5 — Uninstalling or Updating
- **1-Click Friendly Uninstall:** Close Word, Excel & PowerPoint and double-click **`Uninstall-MyanmarSpellChecker.bat`** (or `Uninstall-EasyUser.bat`).
- **Developer CLI Uninstall:**
  ```powershell
  powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Register-DevAddIn.ps1 -Unregister
  ```

---

## 1. Architecture & Technical Highlights

- **183,038 Compiled Lexicon & Reference Entries (`96,276` Unique Active Headwords + `57,064` Lazy Bilingual `EN ⇄ MM` Entries + `25,001` Encyclopedia Articles + `1,929` Proverbs), `1,747` Orthography Rules, `712` Contextual Rules + `109,880` Empirical & Statutory Bigrams:**
  - Complete 462-page Official **မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း** (Part 1 + Part 2 `ခွဲထား` + Preface `တ / တစ်` + Arrow Rules).
  - Authoritative **မြန်မာအဘိဓာန်** definitions, dual Burmese spoken + IPA pronunciations (`myG2P` + sandhi G2P), numeral classifiers, etymologies, synonyms, antonyms, and **57,064 orthography-sanitized English ⇄ Myanmar bilingual entries** (aggregated from GPLv3 Ornagai, CC BY-SA Wiktionary, and Public Domain U Hoke Sein / Judson sources).
  - **25,001 Offline Myanmar Encyclopedia (`စွယ်စုံဗဟုသုတကျမ်း`)** articles, **MLIS/MOI Union Statutory Laws & By-Laws**, **U Hoke Sein Pali-Myanmar** cognates, classical proverbs (`စကားပုံ` / `ဆိုရိုးစကား`), and **9,221 MIMU v9.7 / GAD / MOFA Multi-Tier Gazetteer** proper nouns (global countries/cities + all Myanmar States/Regions, expanded 2022–2026 Districts, Townships, Towns, Named Urban Wards, and Verified Village Tracts with Pcode metadata).
  - **8 Specialized Domains:** **Geology & Earth Sciences (`ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ` — Kyaukse University & DGSE/MONREC)**, **Public Administration & Civil Service (`ပြည်သူ့အုပ်ချုပ်ရေးနှင့် နိုင်ငံ့ဝန်ထမ်းရေးရာ` — 28 Union Ministries, Directorates, UCSB Ranks & Procurement)**, **Higher Education & Universities (`အဆင့်မြင့်ပညာရေးနှင့် တက္ကသိုလ်များ`)**, Legal & Judicial, Banking/Tax/Finance, Medical & Health, Pali & Religious Affairs, and IT/Cybersecurity + Global Currencies & Scientific Units.
- **High-Throughput & Low-Memory Architecture:**
  - **Streaming Binary Trie + Lazy Bilingual & Encyclopedia Hydration:** ~`85 MB` managed heap (`~120 MB` steady-state RAM) with `~1.0 s` cold encrypted database load (`15.17 MB` `mlc_thatpon.db`) across `96,276` unique active headwords, `83,994` lazy-hydrated bilingual/encyclopedia/proverb reference entries, and `109,880` word bigrams.
  - **Sub-5 ms Real-Time Proofing:** Zero-Regex O(N) syllable tokenization (`~0.02 ms/paragraph`), deferred Viterbi lattice issue materialization (`~2.5–4.8 ms` per unique uncached paragraph; `< 0.1 ms` cached), and 45-bit base-consonant branch-and-bound suggestion pruning (`~0.3–4.9 ms` uncached).
- **Zero Internet Calls & 100% Free Open-Source Runtime:**
  - Pure `.NET Framework 4.8` FIPS-compliant `AES-256` + `PBKDF2-SHA256` + `HMAC-SHA256` binary dictionary (`mlc_thatpon.db`) — **100% free & active out-of-the-box under GNU GPL v3.0 (no `.lic` activation key required)**.

---

## 2. Repository Structure

```text
SpellChecker/
├── .gitignore
├── CITATION.cff                                   # Academic & GitHub citation metadata (Htet Yar Zar, Kyaukse University)
├── Directory.Build.props                          # Assembly Author, University, Copyright & GPL-3.0 metadata
├── LICENSE                                        # GNU General Public License v3.0
├── NOTICE                                         # Mandatory Section 7(b)/(c) Author & Origin Preservation Notice
├── MyanmarSpellChecker.sln
├── README.md
├── data/
│   ├── ministry_orthography_rules.json            # Core Ministry headwords & orthography rules
│   ├── mlc_thatpon_sample_import.csv              # Optional CSV bulk-import template
│   ├── mlc_thatpon.db                             # Ready-to-run AES-256 + HMAC-SHA256 encrypted runtime DB
│   └── sources/                                   # Modular upstream & curated linguistic data
│       ├── official/                              # 462p သတ်ပုံကျမ်း JSON, NRC townships, MOFA/GAD gazetteer, *.gov.mm & books
│       ├── dictionaries/                          # မြန်မာအဘိဓာန်, 25k Encyclopedia, U Hoke Sein Pali, proverbs, idioms
│       ├── domains/                               # Auto-discovered domain TSVs (geology, legal, finance, medical, pali, IT, units)
│       ├── corpora/                               # Unigram/bigram frequency corpora
│       └── rules/                                 # Curated orthography & contextual wrong-meaning TSVs + YAMLs
├── src/
│   ├── MyanmarSpellChecker.Core/                  # Pure .NET 4.8 Proofing, NLP, Spreadsheet Engine, Official Suite & Security
│   ├── MyanmarSpellChecker.WordAddIn/             # Microsoft Word COM Add-in
│   ├── MyanmarSpellChecker.ExcelAddIn/            # Microsoft Excel Formula-Safe COM Add-in
│   ├── MyanmarSpellChecker.PowerPointAddIn/       # Microsoft PowerPoint Zero-Mutation Slide & Notes COM Add-in
│   └── MyanmarSpellChecker.AdminTools/            # CLI for Lexicon Distillation, DB Encryption & Offline License Signing
├── installer/
│   ├── Build-MasterLexicon.ps1                    # Rebuilds data/mlc_thatpon_master.json (and mlc_thatpon.db) from data/sources/
│   ├── Build-EnterpriseRelease.ps1                # Compiles Release DLLs, encrypts DB, signs binaries & builds GPO package
│   ├── Manage-EnterpriseTools.ps1                 # Hardware Machine ID inspection & RSA-2048 offline .lic signing
│   ├── Register-DevAddIn.ps1                      # Zero-Admin HKCU COM registration for local MS Word, Excel & PowerPoint
│   └── Product.wxs                                # WiX MSI installer definition (64-bit & 32-bit COM CLSID + Addins)
└── tests/
    └── MyanmarSpellChecker.Core.Tests/            # Automated xUnit test suite (117 end-to-end tests)
```

---

## 3. Developer & Maintainer Commands

### Run All 117 Automated Tests
*(On a fresh Git clone, `TestPaths.MasterJsonPath` automatically builds the intermediate `mlc_thatpon_master.json` from `data/sources/` on the first test run.)*
```powershell
dotnet test .\tests\MyanmarSpellChecker.Core.Tests\MyanmarSpellChecker.Core.Tests.csproj -c Release
```

### Extending Vocabulary & Rebuilding `mlc_thatpon.db`
Drop any new UTF-8 `.tsv` file into `data/sources/domains/` or append rules to `data/sources/rules/`, then rebuild:
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-MasterLexicon.ps1 -CompileEncryptedDb
```

### Build Authenticode-Signed Enterprise / Ministry GPO Release Bundle
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Build-EnterpriseRelease.ps1
```
