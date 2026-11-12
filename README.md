# မြန်မာစာ စာလုံးပေါင်းသတ်ပုံကျမ်း — Enterprise Offline Myanmar Proofing, Grammar & Dictionary Suite for Microsoft Word & Excel

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3%20%2B%20Section%207b%20Attribution-blue.svg)](./LICENSE)
[![Platform](https://img.shields.io/badge/Office-Microsoft%20Word%20%26%20Excel-107C41.svg)](#how-to-install--use-the-plugin-word--excel-user-guide)
[![Runtime](https://img.shields.io/badge/.NET%20Framework-4.8%20(100%25%20Offline)-512BD4.svg)](#)
[![Author](https://img.shields.io/badge/Author-Htet%20Yar%20Zar%20(%E1%80%91%E1%80%80%E1%80%BA%E1%80%9B%E1%80%AC%E1%80%87%E1%80%AC)-0B57D0.svg)](./NOTICE)

100% Offline, Air-Gapped **Microsoft Word + Microsoft Excel** Desktop COM Add-in Suite (`.NET Framework 4.8`, `AnyCPU`) engineered for Government Ministries (`B2G`), Universities, Enterprises, Publishers, Accountants, and General Writers.

Built on the complete 462-page **"မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း"** (Myanmar Language Commission), **"မြန်မာအဘိဓာန်"**, **25,000+ Offline Encyclopedia (`စွယ်စုံဗဟုသုတကျမ်း`)**, **"ရုံးသုံးမြန်မာစာ"**, and **6 Specialized Academic & Enterprise Domains** (including **Geology & Earth Sciences / ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ**).

---

## Author, Academic Affiliation & Origin Attribution

- **Original Creator & Author:** **Htet Yar Zar (ထက်ရာဇာ)** ([@Hashyz](https://github.com/Hashyz))
- **Academic Credential:** **M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed)** *(မဟာသိပ္ပံ ဒုတိယနှစ် [M.Sc. Part II] ဘူမိဗေဒ အထူးပြု — M.Sc. Part I အောင်မြင်ပြီး)*
- **Department & University:** **Department of Geology, Kyaukse University** *(ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ်)*
- **Location:** **Kyaukse, Mandalay Region, Myanmar** *(ကျောက်ဆည်မြို့၊ မန္တလေးတိုင်းဒေသကြီး၊ မြန်မာနိုင်ငံ)*

---

## License & Anti-Plagiarism Notice (GNU GPL v3.0 + Section 7b)

This project is open-source software licensed under the **[GNU General Public License v3.0 (GPL-3.0-or-later)](./LICENSE)** together with mandatory **[Section 7(b) & 7(c) Attribution & Origin Preservation Terms](./NOTICE)**:

1. **Copyleft (No Closed-Source Hijacking):** Any derivative work, modified version, or redistributed binary/installer **must** remain open-source under the GNU GPL v3.0 with full source code made available. Closed-source commercial rebranding is strictly prohibited.
2. **Mandatory Preservation of Author Attribution (GPLv3 Section 7b):** Removing, hiding, or altering the [`NOTICE`](./NOTICE) file, source copyright headers, assembly metadata (`Directory.Build.props`), or visible in-app **Creator & Academic Attribution** credits (`Htet Yar Zar (ထက်ရာဇာ) — Department of Geology, Kyaukse University, Kyaukse, Myanmar`) is legally prohibited.
3. **No Misrepresentation of Origin (GPLv3 Section 7c):** You may **not** claim original authorship of this software or present unmodified/modified copies as your own uncredited academic, institutional, or commercial project. For academic and software citation metadata, see [`CITATION.cff`](./CITATION.cff) (or click **"Cite this repository"** on GitHub).

---

## How to Install & Use the Plugin (Word & Excel User Guide)

### System Requirements
- **OS:** Windows 10 or Windows 11 (64-bit or 32-bit)
- **Microsoft Office:** Microsoft Word & Microsoft Excel **2016 / 2019 / 2021 / Office 365** (Desktop Edition)
- **Runtime:** `.NET Framework 4.8` (pre-installed on Windows 10/11) and `.NET SDK` (to build from source)
- **Recommended Font:** **Pyidaungsu** Unicode Font (for crisp Myanmar rendering)

---

### Step 1 — Install & Register the Add-in (Word + Excel)

1. **Clone the repository and build Release binaries:**
   ```powershell
   git clone https://github.com/Hashyz/MyanmarSpellChecker.git
   cd MyanmarSpellChecker
   dotnet build .\MyanmarSpellChecker.sln -c Release
   ```
2. **Close Microsoft Word and Microsoft Excel**, then run the Zero-Admin installer script (registers both Word and Excel COM Add-ins under your current Windows user and deploys `data\mlc_thatpon.db` automatically):
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Register-DevAddIn.ps1
   ```
   *(Optional — for IT / Ministry-wide deployment across all users on a PC, run `Build-EnterpriseRelease.ps1` and execute `artifacts\release-package\Install-MinistryGPO.cmd` as Administrator.)*
3. **Open Microsoft Word or Microsoft Excel** — you will see a new top Ribbon tab named **`မြန်မာစာ စစ်ဆေးရေး`** and the **4-Tab Myanmar Proofing Task Pane** docked on the right!

> **Tip (If the Ribbon Tab Does Not Appear):** In Word or Excel, go to **File ➔ Options ➔ Add-ins ➔ Manage: COM Add-ins ➔ Go...**, check **`Myanmar Official Spell & Grammar Suite`**, and click **OK**.

---

### Step 2 — How to Use in Microsoft Word (`မြန်မာစာ စစ်ဆေးရေး` Tab)

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

#### B. Choosing Writing Style Mode
- **`အထွေထွေစံ` (General Standard Mode):** Checks official spelling (`သတ်ပုံကျမ်း`), homophones, and core grammar while allowing natural polite spoken particles (`တယ်`, `မယ်`, `တွေ`, `နဲ့`, `ဘူး`).
- **`ရုံးသုံးစာပေ` / `စာပေ / ရုံးသုံး အရေးစကားစနစ်` (Formal / Official Mode):** Enforces full formal literary and government ministry prose standards (`ပါတယ် ➔ ပါသည်`, `တွေ ➔ များ`, `နဲ့ ➔ နှင့်`, `ဒါပေမယ့် ➔ သို့သော်လည်း`).

#### C. Using the 4-Tab Custom Task Pane in Word
1. **`စာစစ်` (Proofing Tab):** Filter issues by **All (`အားလုံး`)**, **Spelling (`သတ်ပုံ`)**, **Grammar (`သဒ္ဒါ`)**, or **Context (`အနက်`)**, step through issues with **`↑` / `↓`**, and read deep Burmese linguistic & Pali etymological explanations on every card.
2. **`အဘိဓာန်` (Dictionary & Encyclopedia Tab):** Instant offline search across **118,600+ entries**:
   - Filter by **`သတ်ပုံကျမ်း`**, **`အဘိဓာန်`**, **`ဗဟုသုတ` (25,000+ Encyclopedia articles)**, **`နယ်ပယ်သုံး` (Geology `ဘူမိဗေဒ`, Legal, Finance, Medical, Pali, IT)**, **`ဒေသ/အမည်` (330 Townships & Gazetteer)**, and **`စကားပုံ` (Proverbs)**.
   - Shows Burmese pronunciation, synonyms (`ကြောင်းတူသံကွဲ`), antonyms (`ဆန့်ကျင်ဘက်`), and homophone siblings.
3. **`ရုံးစာကိရိယာ` (Official Writing Toolkit Tab):**
   - **Legacy Font Converter (`WinInnwa/Zawgyi ➔ Pyidaungsu`):** Converts legacy fonts across the document or selection without breaking English text, tables, or bold/italic formatting.
   - **Smart Numeral Converter (`0-9 ➔ ၀-၉`):** Converts digits to Myanmar numerals while preserving `ISO-9001`, `COVID-19`, `A4`, `5G`, URLs, and emails.
   - **Kyat Lakh Number-to-Words (`ကျပ်သိန်း စာသားပြောင်းစက်`):** Converts numbers like `1,500,000` into official Burmese banking/budget words (`ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ`).
   - **Traditional Myanmar Calendar (`မြန်မာသက္ကရာဇ် ပြက္ခဒိန်`):** Converts any Gregorian date to `သာသနာသက္ကရာဇ်`, `မြန်မာသက္ကရာဇ်`,waxing/waning moon day, and Sabbath (`ဥပုသ်`) status.
   - **Official Letter Templates & Printable Quality Audit (`အရည်အသွေး အစီရင်ခံစာ`):** Inserts standard `ရုံးစာ` templates and generates a printable `A+`–`D` quality audit certificate.
4. **`ကိုယ်ပိုင်စာလုံး` (Custom Words Tab):** Add personal names, university/department terms, or local place names once — shared live between Word and Excel!

---

### Step 3 — How to Use in Microsoft Excel (`မြန်မာစာ စစ်ဆေးရေး` Tab)

The Excel Add-in provides **In-Cell Color-Coded Underlines + Formula-Safe Cell-by-Cell Proofing & Accounting Tools**:

1. **In-Cell Red / Blue / Amber Underlines (Save & Print Safe):**
   - Misspelled words inside plain-text cells are highlighted directly inside the cell with **Red Single Underline (`သတ်ပုံအမှား`)**, **Blue Double Underline (`သဒ္ဒါ/စာဟန်`)**, or **Amber Single Underline (`အနက်သံသယ`)**.
   - All temporary proofing underlines are **automatically restored to clean normal text** as soon as you fix the word, click **`မျဉ်းရှင်း`**, or press **`Ctrl+S` (Save) / `Ctrl+P` (Print)** so proofing underlines are **never** baked into your saved `.xlsx` file. You can also toggle **`အနီမျဉ်း ပြသမှု (Underlines)`** on/off from the Ribbon at any time.
2. **Choose Scan Scope (Ribbon or Task Pane):**
   - **`လက်ရှိ Sheet စစ်မည်` (`Sheet`):** Scans all text cells in the active worksheet in milliseconds using bulk 2D `SAFEARRAY` reads.
   - **`ရွေးကွက် စစ်မည်` (`ရွေးကွက်`):** Scans only your highlighted cell range.
   - **`Workbook လုံး စစ်မည်` (`စာအုပ်လုံး`):** Scans every worksheet across the entire `.xlsx` workbook.
   - **`အလိုအလျောက် စစ်ဆေးမှု (Live)`:** Automatically checks cells right after you press `Enter` to finish editing a cell.
3. **100% Formula & Number Protection Guarantee:**
   - Cells containing Excel formulas (`=SUM(...)`, `=VLOOKUP(...)`, `=IF(...)`, `=TEXT(...)`), pure accounting numbers, currencies, percentages, and dates are **strictly shielded** and **never** modified during single fixes, `အားလုံးပြင်`, underlining, font conversion, or numeral conversion.
4. **Click-to-Navigate, Fix & 25-Step Undo (`↶ ပြန်ဆုတ်`):**
   - Click any issue card (e.g. `စာရင်း(၁)!B4`) in the Task Pane to jump straight to that sheet and cell in Excel.
   - Click `✓ ပြင်မည်` for a single fix or `အားလုံးပြင်` for a batch fix across cells.
   - Because COM writes normally clear Excel's native `Ctrl+Z`, the add-in includes its own **25-step Multi-Level Undo stack** — click **`ပြန်ဆုတ်မည် (Undo)`** on the Ribbon or **`↶ ပြန်ဆုတ်`** in the Task Pane at any time to restore previous cell values and fonts!
5. **Excel Accounting & Audit Tools:**
   - Select any numeric budget cell in Excel and open the **`ကိရိယာ` (Toolkit)** tab — the number is automatically captured and converted into **Kyat Lakh (`ကျပ်သိန်း...တိတိ`)** Burmese words ready to copy or insert!
   - Click **`အရည်အသွေး အစီရင်ခံစာ`** to generate a formatted printable audit report sheet (`မြန်မာစာ_စစ်ဆေးချက်`) listing quality scores and cell-by-cell findings.

---

### Step 4 — Uninstalling or Updating
```powershell
# Unregister COM Add-ins from Word & Excel cleanly
powershell -NoProfile -ExecutionPolicy Bypass -File .\installer\Register-DevAddIn.ps1 -Unregister
```

---

## 1. Architecture & Technical Highlights

- **118,659 Lexicon & Reference Entries (`89,573` Unique Headwords) + `85,000` Empirical Bigrams:**
  - Complete 462-page Official **မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း** (Part 1 + Part 2 `ခွဲထား` + Preface `တ / တစ်` + Arrow Rules).
  - Authoritative **မြန်မာအဘိဓာန်** definitions, Burmese pronunciations, numeral classifiers, etymologies, synonyms, and antonyms.
  - **25,001 Offline Myanmar Encyclopedia (`စွယ်စုံဗဟုသုတကျမ်း`)** articles, **U Hoke Sein Pali-Myanmar** cognates, classical proverbs (`စကားပုံ` / `ဆိုရိုးစကား`), MOFA/GAD global countries, cities, and all **330 Myanmar townships**.
  - **6 Specialized Domains:** **Geology & Earth Sciences (`ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ` — Kyaukse University)**, Legal & Judicial, Banking/Tax/Finance, Medical & Health, Pali & Religious Affairs, and IT/Cybersecurity + Global Currencies & Scientific Units.
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
│   └── MyanmarSpellChecker.AdminTools/            # CLI for Lexicon Distillation, DB Encryption & Offline License Signing
├── installer/
│   ├── Build-MasterLexicon.ps1                    # Rebuilds data/mlc_thatpon_master.json (and mlc_thatpon.db) from data/sources/
│   ├── Build-EnterpriseRelease.ps1                # Compiles Release DLLs, encrypts DB, signs binaries & builds GPO package
│   ├── Manage-EnterpriseTools.ps1                 # Hardware Machine ID inspection & RSA-2048 offline .lic signing
│   ├── Register-DevAddIn.ps1                      # Zero-Admin HKCU COM registration for local MS Word + MS Excel
│   └── Product.wxs                                # WiX MSI installer definition
└── tests/
    └── MyanmarSpellChecker.Core.Tests/            # Automated xUnit test suite (55 end-to-end tests)
```

---

## 3. Developer & Maintainer Commands

### Run All 55 Automated Tests
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
