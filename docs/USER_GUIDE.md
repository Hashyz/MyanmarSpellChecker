# Myanmar Spell Checker — Complete User Guide & Architecture Handbook
## မြန်မာစာ စာလုံးပေါင်းစစ်ဆေးရေး — အသုံးပြုပုံလမ်းညွှန်အပြည့်အစုံ (Word • Excel • PowerPoint)

> **100% Offline Unified 3-in-1 Microsoft Office Suite** (`Word + Excel + PowerPoint`, `.NET Framework 4.8`)  
> **Author:** **Htet Yar Zar (ထက်ရာဇာ)** — Department of Geology, Kyaukse University, Kyaukse, Myanmar  
> **Special Dedication:** **U Moe Kyaw Thu (ဦးမိုးကျော်သူ)** • **Collaborators:** **Hein Htet Zaw (ဟိန်းထက်ဇော်)** & **Thu Reain Khant (သူရိန်ခန့်)**  
> **License:** GNU GPL v3.0 + Mandatory Section 7(b) Attribution (`181,497` Compiled Entries • Zero Internet Calls)

## Table of Contents / မာတိကာ
- [**01. Quick-Start & 1-Click Offline Installation** — *အမြန်စတင်အသုံးပြုနည်းနှင့် Offline ထည့်သွင်းနည်း*](#quick-start)
- [**02. Complete Directory of Available Tools (28 Built-In Tools)** — *ပါဝင်သော ကိရိယာများ အားလုံးစာရင်း (စုစုပေါင်း ၂၈ မျိုး)*](#all-tools)
- [**03. How to Use Proofing: Underline Colors, 3 Modes & Safe Fixing** — *စာစစ်ဆေးနည်း — မျဉ်းလှိုင်းအရောင် (၃) မျိုး၊ အရေးအသားစနစ် (၃) မျိုးနှင့် ပြင်ဆင်နည်း*](#proofing-guide)
- [**04. Host-by-Host Guide: Microsoft Word, Excel & PowerPoint** — *Word၊ Excel နှင့် PowerPoint တစ်ခုချင်းစီအတွက် အထူးလုပ်ဆောင်ချက်များ*](#office-hosts)
- [**05. Offline Bilingual Dictionary (EN ⇄ MM), Audio & 25,001 Encyclopedia** — *အော့ဖ်လိုင်း အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်၊ အသံထွက်နှင့် စွယ်စုံဗဟုသုတကျမ်း*](#dictionary-suite)
- [**06. Official Typography Toolkit & Printable A4 Quality Audit Reports** — *ရုံးစာကိရိယာများ (Toolkit) နှင့် ပုံနှိပ်ရန်အသင့် A4 အစီရင်ခံစာ ထုတ်ယူနည်း*](#toolkit-and-reports)
- [**07. How It Works Under the Hood: Hybrid NLP Architecture & Safety Engine** — *စနစ်အလုပ်လုပ်ပုံ နည်းပညာဗိသုကာ (Hybrid Statistical + Symbolic Engine)*](#how-it-works)
- [**08. Troubleshooting, Multi-PC Tips & Author Attribution** — *ပြဿနာဖြေရှင်းနည်းများ (FAQ) နှင့် ဖန်တီးသူ ဂုဏ်ပြုမှတ်တမ်း*](#troubleshooting)

---

<a id="quick-start"></a>
## 01. Quick-Start & 1-Click Offline Installation
### အမြန်စတင်အသုံးပြုနည်းနှင့် Offline ထည့်သွင်းနည်း

> **EN:** Install once in 10 seconds with zero internet, zero admin password, and zero .NET SDK — works across Microsoft Word, Excel, and PowerPoint.  
> **MM:** အင်တာနက်မလို၊ Admin Password မလို၊ .NET SDK မလိုဘဲ စက္ကန့်ပိုင်းအတွင်း သွင်းယူ၍ Microsoft Word၊ Excel နှင့် PowerPoint သုံးမျိုးလုံးတွင် တစ်ပြိုင်နက် အသုံးပြုနိုင်ပါသည်။

#### 1.1 System Requirements & 3-Step Setup / ၁.၁ လိုအပ်သော စနစ်များနှင့် အဆင့် (၃) ဆင့်ဖြင့် သွင်းယူနည်း

- **Supported System: Windows 10 or Windows 11 (64-bit or 32-bit) with Microsoft Office 2016 / 2019 / 2021 / Microsoft 365 Desktop (Word, Excel & PowerPoint). .NET Framework 4.8 is already built into Windows 10/11.**  
  *အသုံးပြုနိုင်သော စနစ် — Windows 10 သို့မဟုတ် Windows 11 (64-bit / 32-bit) နှင့် Microsoft Office 2016 / 2019 / 2021 / 365 (Word, Excel, PowerPoint) တို့တွင် အသုံးပြုနိုင်ပါသည်။*
- **Step 1 (Extract First): Right-click the downloaded ZIP file and click 'Extract All...' into a normal folder before running Setup.**  
  *အဆင့် ၁ (ZIP အရင်ဖြည်ပါ) — ဒေါင်းလုဒ်ဆွဲထားသော .ZIP ဖိုင်ပေါ်တွင် Right-click နှိပ်ပြီး 'Extract All...' ဖြင့် ဖိုဒါအဖြစ် အရင်ဖြည်ပါ။*
- **Step 2 (Close Office & Double-Click Setup): Save open files, close Word/Excel/PowerPoint, and double-click 'Install-MyanmarSpellChecker.bat' (or 'Install-EasyUser.bat').**  
  *အဆင့် ၂ (Office ခေတ္တပိတ်၍ သွင်းပါ) — Word, Excel, PowerPoint တို့ကို ခေတ္တပိတ်ပြီး 'Install-MyanmarSpellChecker.bat' သို့မဟုတ် 'Install-EasyUser.bat' ကို Double-click နှိပ်ပါ။*
- **Step 3 (Open Office & Start): Open Word, Excel, or PowerPoint! You will see the 'Myanmar Spell Checker' Ribbon tab at the top, a quick launcher inside Office's 'Review' tab, and the 4-Tab Task Pane on the right.**  
  *အဆင့် ၃ (စတင်အသုံးပြုပါ) — Word, Excel သို့မဟုတ် PowerPoint ဖွင့်လိုက်သည်နှင့် အပေါ်ဘက်တွင် 'Myanmar Spell Checker' တက်ဘ်၊ 'Review' တက်ဘ်အတွင်း အမြန်ဖွင့်ခလုတ် နှင့် ညာဘက် Task Pane ပြကွက်တို့ကို တွေ့ရပါမည်။*
- **Instant Bilingual UI Switch ([A/က EN ⇄ မြန်မာ]): Click the [A/က EN] button in the Task Pane header (or Ribbon) anytime to toggle every button, tooltip, and explanation live between English and Myanmar without restarting Office.**  
  *ဘာသာစကား ချက်ချင်းပြောင်းခြင်း ([A/က EN ⇄ မြန်မာ]) — ညာဘက်ပြကွက်ထိပ်ရှိ [A/က EN] ခလုတ်ကို နှိပ်ရုံဖြင့် စနစ်တစ်ခုလုံးကို အင်္ဂလိပ် ⇄ မြန်မာ အချိန်မရွေး တိုက်ရိုက်ပြောင်းလဲနိုင်ပါသည်။*

---

<a id="all-tools"></a>
## 02. Complete Directory of Available Tools (28 Built-In Tools)
### ပါဝင်သော ကိရိယာများ အားလုံးစာရင်း (စုစုပေါင်း ၂၈ မျိုး)

> **EN:** Every tool available across the 4-Tab Task Pane (Proofing, Dictionary, Toolkit, Custom) and the Ribbon across Word, Excel, and PowerPoint.  
> **MM:** ညာဘက် Task Pane တက်ဘ် (၄) ခု (Proofing, Dictionary, Toolkit, Custom) နှင့် အပေါ်ဘက် Ribbon တို့တွင် ပါဝင်သော ကိရိယာ (၂၈) မျိုးလုံး၏ လုပ်ဆောင်ချက်များ။

#### 2.1 Workspace Tabs & Tool Reference Matrix / ၂.၁ ကိရိယာတစ်ခုချင်းစီ၏ တည်နေရာနှင့် အသုံးဝင်ပုံ ဇယား

| Location / Tab | Tool Name (EN / မြန်မာ) | Office Hosts | What It Does & When to Use It |
| :--- | :--- | :--- | :--- |
| Proofing (စာစစ်) | Scan Doc / Sheet / Slide (စစ်ဆေးမည်) | Word, Excel, PPT | Scans full document, active sheet/workbook, or slide/deck/notes for spelling, grammar & meaning issues. (စာတမ်း၊ Sheet သို့မဟုတ် Slide အားလုံးကို စစ်ဆေးပေးသည်။) |
| Proofing (စာစစ်) | Scan Selection (ရွေးကွက် စစ်မည်) | Word, Excel, PPT | Checks only highlighted paragraphs, selected cells, or selected slide shapes. (ရွေးချယ်ထားသော အပိုင်းကိုသာ သီးသန့် အမြန်စစ်ဆေးသည်။) |
| Proofing (စာစစ်) | 3-Mode Style Selector: Casual \| Standard \| Official | Word, Excel, PPT | Switches proofing strictness between chat/casual, general standard, and formal ministry/literary Burmese. (ပေါ့ပါးဟန်၊ အထွေထွေစံ နှင့် ရုံးသုံးစာပေဟန် ၃ မျိုး ပြောင်းနိုင်သည်။) |
| Proofing (စာစစ်) | Review & Fix / Fix All Studio (အားလုံးပြင် စိစစ်ပြကွက်) | Word, Excel, PPT | Opens docked or floating checklist with checkboxes, 'Select Sure Only', search filter & 1-step Undo. (အမှားများကို အမှန်ခြစ်ရွေး၍ စိတ်ချစွာ တစ်ပြိုင်နက် ပြင်ဆင်နိုင်သည်။) |
| Proofing (စာစစ်) | On-Cursor Quick-Fix Popup & Right-Click Menu | Word, Excel, PPT | Click or right-click any flagged word/cell/textbox to pick top ranked suggestions, Ignore, or +Add Word. (အနီ/အပြာမျဉ်းပေါ် နှိပ်ရုံဖြင့် အကြံပြုချက် ရွေးနိုင်သည်။) |
| Proofing (စာစစ်) | Undo / Restore (Ctrl+Z / ↶ ပြန်ဆုတ်) | Word, Excel, PPT | Reverts single or batch fixes cleanly (native Ctrl+Z in Word; 25-step Undo in Excel; 30-step Undo in PPT). (ပြင်ပြီးသားကို မူလအတိုင်း ပြန်ဆုတ်နိုင်သည်။) |
| Proofing (စာစစ်) | Clear Marks (မျဉ်းရှင်းမည်) | Word, Excel, PPT | Hides all temporary proofing underlines and suppresses stray Word English red squiggles on Burmese words. (စစ်ဆေးရေးမျဉ်းများကို ချက်ချင်းဖယ်ရှားပေးသည်။) |
| Proofing (စာစစ်) | Category Filters & ↑ / ↓ Stepper | Word, Excel, PPT | Filters cards by All, Spelling (သတ်ပုံ), Grammar (သဒ္ဒါ), or Context (အနက်) and jumps issue-by-issue. (အမှားအမျိုးအစားအလိုက် ခွဲထုတ်ကြည့်ရှုနိုင်သည်။) |
| Proofing (စာစစ်) | ≡ Report ▾ (5-Format Quality Audit Exporter) | Word, Excel, PPT | Exports Printable A4 Word (.docx), Multi-Sheet Excel (.xlsx), Executive PPT (.pptx), CSV, or HTML/PDF report. (A4 ပုံနှိပ်အသင့် အစီရင်ခံစာ ၅ မျိုး ထုတ်ပေးသည်။) |
| Dictionary (အဘိဓာန်) | 181,497-Entry Bilingual EN ⇄ MM Search | Word, Excel, PPT | Instant offline lookup by Myanmar, English, Romanized Burmese ('mingalaba', 'kyaukse'), or Zawgyi. (မြန်မာ၊ အင်္ဂလိပ်၊ ရိုမန်အသံထွက် တို့ဖြင့် ရှာနိုင်သည်။) |
| Dictionary (အဘိဓာန်) | Dual Spoken Sandhi + IPA & ▶ EN Audio TTS | Word, Excel, PPT | Shows natural Burmese spoken pronunciation + IPA and speaks English headwords aloud offline. (မြန်မာအသံထွက်နှင့် အင်္ဂလိပ် အသံထွက် ▶ EN နားထောင်နိုင်သည်။) |
| Dictionary (အဘိဓာန်) | 25,001 Encyclopedia, 1,929 Proverbs & 8 Domains | Word, Excel, PPT | Filter by သတ်ပုံကျမ်း, အဘိဓာန်, ဗဟုသုတ Encyclopedia, 8 Domains (Geology ဘူမိဗေဒ, Legal, Medical...), Gazetteer & Proverbs. (စွယ်စုံကျမ်း၊ စကားပုံနှင့် နယ်ပယ် ၈ ခု ပါဝင်သည်။) |
| Dictionary (အဘိဓာန်) | Corpus Collocations (▸), Synonyms & ★ Bookmarks | Word, Excel, PPT | Displays real-world word pairings from 109,880 bigrams and syncs ★ Saved words across Office. (တွဲသုံးလေ့ရှိသော စကားလုံးများနှင့် ★ မှတ်သားချက်များ ပါဝင်သည်။) |
| Toolkit (ကိရိယာ) | WinInnwa / Zawgyi ➔ Pyidaungsu Converter | Word, Excel, PPT | Converts legacy fonts to standard Unicode while preserving English fonts, bold/colors, and Excel formulas. (အင်္ဂလိပ်ဖောင့်နှင့် Formula မပျက်စေဘဲ ယူနီကုဒ်ပြောင်းပေးသည်။) |
| Toolkit (ကိရိယာ) | Repair Garbled PDF & Paste Clean (PDF/ZG) | Word, Excel, PPT | Reconstructs broken Burmese PDF copy-paste (detached ေ, ြ, kinzi, vowel swaps) in place or from Clipboard. (PDF မှ ကူးယူရာတွင် ပျက်နေသော မြန်မာစာများကို ပြန်ဆက်ပေးသည်။) |
| Toolkit (ကိရိယာ) | Smart Numerals 0-9 ➔ ၀-၉ (Formula-Safe) | Word, Excel, PPT | Converts digits to Myanmar numerals while protecting URLs, emails, ISO-9001, COVID-19, A4, 5G & numeric cells. (URL, A4, 5G နှင့် တွက်ချက်မှုဂဏန်းများ မထိခိုက်ဘဲ မြန်မာဂဏန်းပြောင်းသည်။) |
| Toolkit (ကိရိယာ) | Clean Punctuation (၊ / ။) & Invisible ZWSP | Word, Excel, PPT | Normalizes Myanmar comma/full-stop spacing and cleans stray zero-width characters. (ပုဒ်ထီး၊ ပုဒ်မ အကွာအဝေးနှင့် ZWSP သန့်စင်ပေးသည်။) |
| Toolkit (ကိရိယာ) | Rewrite Colloquial ➔ Official Prose | Word, Excel, PPT | Transforms spoken endings/particles (ပါတယ် ➔ ပါသည်, တွေ ➔ များ, နဲ့ ➔ နှင့်) into formal prose. (စကားပြောဟန်ကို ရုံးသုံးစာပေဟန်သို့ ပြောင်းပေးသည်။) |
| Toolkit (ကိရိယာ) | Unify Myanmar Font & Smart Slide Line-Wrap ZWSP | PowerPoint | Standardizes Burmese runs to Pyidaungsu and injects word-boundary ZWSP so slide titles never wrap mid-syllable. (ဆလိုက်ခေါင်းစဉ်များ စာလုံးအလယ်မှ မပြတ်ကျစေရန် ညှိပေးသည်။) |
| Toolkit (ကိရိယာ) | Kyat Lakh (ကျပ်သိန်း...တိတိ) Words Converter | Word, Excel, PPT | Converts numbers like 1,500,000 into official Burmese budget/banking words (auto-reads selected Excel cell!). (ဂဏန်းများကို ကျပ်တစ်ဆယ့်ငါးသိန်းတိတိ ဟု စာသားပြောင်းပေးသည်။) |
| Toolkit (ကိရိယာ) | Traditional Myanmar Calendar & Sabbath (ဥပုသ်) | Word, Excel, PPT | Converts Gregorian dates into သာသနာသက္ကရာဇ်, မြန်မာသက္ကရာဇ်, lunar phase & Sabbath status ready to insert. (သာသနာ/မြန်မာသက္ကရာဇ်၊ လဆန်း/လဆုတ်နှင့် ဥပုသ်နေ့ တွက်ပေးသည်။) |
| Toolkit (ကိရိယာ) | Official Letter Templates (ရုံးစာ ပုံစံကြမ်း) | Word | Inserts standard Burmese official letter headers/closings with auto-filled Myanmar calendar date. (မြန်မာရက်စွဲပါ ရုံးစာပုံစံကြမ်း ထည့်သွင်းပေးသည်။) |
| Custom (မှတ်သားစာ) | Shared Personal & Organization Dictionary | Word, Excel, PPT | Add personal names, brand terms, or local places once — immediately shared across Word, Excel & PowerPoint. (ကိုယ်ပိုင်အမည်နှင့် ရုံးသုံးစာလုံးများ မှတ်သားထားနိုင်သည်။) |
| Header & Ribbon | Eco Mode (Auto / On / Off), Slim UI & User Guide | Word, Excel, PPT | Controls CPU/RAM footprint for low-spec laptops, compacts header height, and opens offline HTML/Word guides. (စက်မလေးစေသော Eco စနစ်နှင့် Offline လမ်းညွှန်စာအုပ် ပါဝင်သည်။) |

---

<a id="proofing-guide"></a>
## 03. How to Use Proofing: Underline Colors, 3 Modes & Safe Fixing
### စာစစ်ဆေးနည်း — မျဉ်းလှိုင်းအရောင် (၃) မျိုး၊ အရေးအသားစနစ် (၃) မျိုးနှင့် ပြင်ဆင်နည်း

> **EN:** Understand what Red, Blue, and Amber wavy underlines mean, when to pick Casual vs Standard vs Official mode, and how the Review Needed lock protects your meaning.  
> **MM:** အနီ၊ အပြာ၊ လိမ္မော် မျဉ်းလှိုင်းတို့၏ အဓိပ္ပာယ်၊ Casual / Standard / Official စနစ် (၃) မျိုး ကွာခြားချက်နှင့် အမှားများကို အန္တရာယ်ကင်းစွာ ပြင်ဆင်နည်း။

#### 3.1 Color-Coded Wavy Underline Legend (Red / Blue / Amber) / ၃.၁ မျဉ်းလှိုင်းအရောင် (၃) မျိုး၏ အဓိပ္ပာယ်နှင့် Auto-Fix လုံခြုံရေးစနစ်

| Underline Color | Category (အမျိုးအစား) | Real Examples (ဥပမာများ) | Batch Fix Behavior |
| :--- | :--- | :--- | :--- |
| Red Wavy (အနီရောင်) | Spelling & High-Certainty Homophone (သတ်ပုံအမှား) | ပီးပီ ➔ ပြီးပြီ, ဥက္ကဌ ➔ ဥက္ကဋ္ဌ, သကေင်္တ ➔ သင်္ကေတ, စာတိုက်ကုတ် ➔ ကုဒ် | ✓ Auto-Checked (Safe for 1-click Fix All) |
| Blue Wavy (အပြာရောင်) | Grammar, Classifier & Register Style (သဒ္ဒါနှင့် စာဟန်) | တခု ➔ တစ်ခု, မသိပဲ ➔ မသိဘဲ, ဖိနပ် တစ်ခု ➔ တစ်ရံ, ပါတယ် ➔ ပါသည် | ✓ Auto-Checked (Safe for 1-click Fix All) |
| Amber Wavy (လိမ္မော်ရောင်) | Contextual Meaning, Intruder Word & SOV Order (အနက်သံသယ / စိစစ်ရန်) | ဥပဒေ ချိုးဖဲ့ ➔ ချိုးဖောက်, နည်းပညာ စား အသစ် ➔ အသစ်, တင်သွင်းခဲ့သည် အစီရင်ခံစာကို ➔ SOV | ☐ Unchecked by Default ('Review Needed' — human confirms first!) |

#### 3.2 Choosing Between the 3 Writing Modes (Casual | Standard | Official) / ၃.၂ အရေးအသားစနစ် (၃) မျိုး (Casual | Standard | Official) ရွေးချယ်နည်း

| Mode (စနစ်) | Best For (သင့်တော်သော စာများ) | Allowed Naturally (ခွင့်ပြုချက်) | Flagged as Issues (စစ်ဆေးပြင်ဆင်ပေးမည့် အရာများ) |
| :--- | :--- | :--- | :--- |
| Casual / Chat (ပေါ့ပါးဟန်) | Social media, captions, informal notes, chat scripts | ကျနော်, တခု, ဒါပေမယ့်, ပြဿနာ, spoken particles (တယ်/နဲ့/တွေ) | Real spelling errors (ပီးပီ ➔ ပြီးပြီ, ဥက္ကဌ ➔ ဥက္ကဋ္ဌ) & wrong homophones |
| General Standard (အထွေထွေစံ) | Articles, emails, business campaigns, speeches, textbooks | Polite spoken particles (ပါတယ်, မယ်, တွေ, နဲ့, ဘူး) | All 462-page သတ်ပုံကျမ်း spelling, တ/တစ် rules, پဲ/ဘဲ, လဲ/လည်း, classifiers & majority-register clashes |
| Official / Literary (ရုံးသုံးစာပေ) | Ministry letters (ရုံးစာ), academic theses, contracts, laws | Formal literary Burmese (သည်, ၏, ၌, နှင့်, များ, သို့သော်လည်း) | Everything in Standard PLUS all colloquial particles (ပါတယ် ➔ ပါသည်, တွေ ➔ များ, နဲ့ ➔ နှင့်) |

---

<a id="office-hosts"></a>
## 04. Host-by-Host Guide: Microsoft Word, Excel & PowerPoint
### Word၊ Excel နှင့် PowerPoint တစ်ခုချင်းစီအတွက် အထူးလုပ်ဆောင်ချက်များ

> **EN:** Each Office host uses a tailored, document-safe architecture so your formatting, Excel formulas, and PowerPoint animations are never harmed.  
> **MM:** Word ခေါင်းစဉ်မျဉ်းများ၊ Excel Formula တွက်ချက်မှုများနှင့် PowerPoint ဒီဇိုင်း/Animation များ လုံးဝမပျက်စီးစေရန် သီးသန့်တည်ဆောက်ထားပုံ။

#### 4.1 Microsoft Word — Mixed Burmese+English Shield & Safe Save/Print / ၄.၁ Microsoft Word — မြန်မာ+အင်္ဂလိပ် ရောနှောစာတမ်း ကာကွယ်မှုနှင့် Save/Print စနစ်

- **Full Multi-Story Coverage: Scans Main Body, Tables, Headers, Footers, Footnotes, Endnotes, and Floating TextBoxes.**  
  *စာကိုယ် (Body)၊ ဇယားကွက် (Tables)၊ Header/Footer၊ အောက်ခြေမှတ်စုနှင့် TextBox အားလုံးကို စစ်ဆေးပေးပါသည်။*
- **Mixed Myanmar + English Smart Shield: Automatically marks Burmese script spans with NoProofing=1 so Word's US-English spellchecker never underlines valid Burmese words in red, while keeping English words (e.g., 'Tiktok', 'Licencing') checked in English and preserving your intentional single/double heading underlines!**  
  *မြန်မာနှင့် အင်္ဂလိပ် ရောရေးထားသော စာပိုဒ်များတွင် မြန်မာစာသားများကို Word ၏ English Spellcheck အနီမျဉ်း မတက်စေရန် အလိုအလျောက် ကာကွယ်ပေးပြီး အင်္ဂလိပ်စာလုံးပေါင်းစစ်ဆေးမှုနှင့် ခေါင်းစဉ်မျဉ်းသားထားမှု (Single Underline) များကို မူလအတိုင်း ထိန်းသိမ်းပေးပါသည်။*
- **Clean .docx Save & Print: Temporary proofing wavy underlines are automatically stripped right before Save (Ctrl+S) and Print (Ctrl+P), so colleagues without the add-in always receive clean documents.**  
  *ဖိုင်သိမ်းဆည်းချိန် (Save) နှင့် ပရင့်ထုတ်ချိန် (Print) တို့တွင် ယာယီစစ်ဆေးရေးမျဉ်းများကို အလိုအလျောက် ဖယ်ရှားပေးသဖြင့် .docx ဖိုင်အတွင်း မျဉ်းလှိုင်းများ လုံးဝမကျန်ခဲ့ပါ။*

#### 4.2 Microsoft Excel — 100% Formula Protection & Huge-Sheet Streaming / ၄.၂ Microsoft Excel — Formula အပြည့်အဝ ကာကွယ်မှုနှင့် Sheet အကြီးကြီးများ စစ်ဆေးနည်း

- **Zero Formula or Cell-Color Mutation: Formulas (=SUM, =IF, =VLOOKUP, =XLOOKUP), numbers, dates, and cell font colors are NEVER modified. Red/Blue/Amber wavy lines are drawn on a transparent screen-only overlay above cells.**  
  *Excel တွက်ချက်မှု Formula (=SUM, =IF, =VLOOKUP) များ၊ ကိန်းဂဏန်းများနှင့် Cell အရောင်များကို လုံးဝမထိခိုက်ပါ။ မျဉ်းလှိုင်းများကို ဖန်သားပြင်ပေါ်၌သာ ပြသသဖြင့် .xlsx ဖိုင်မူရင်း လုံးဝမပြောင်းလဲပါ။*
- **46,000 to 1,000,000 Cell Streaming + ⚡ Repeated Issues: Huge workbooks open in < 0.2s without freezing Excel and group identical typos across thousands of rows at the top of the Task Pane for 1-click batch fixing (with 25-step ↶ Undo).**  
  *အကွက် သိန်းချီရှိသော Excel ဖိုင်ကြီးများကို စက်မထစ်ဘဲ နောက်ခံတွင် အပိုင်းလိုက် စစ်ဆေးပေးပြီး ထပ်ခါထပ်ခါ မှားနေသော စာလုံးတူများကို ⚡ Repeated Issues ဖြင့် တစ်ချက်တည်း ပြင်နိုင်ပါသည် (၂၅ ဆင့် Undo ပြန်ဆုတ်နိုင်သည်)။*

#### 4.3 Microsoft PowerPoint — Zero-Mutation Overlay & Slide Show (F5) Shield / ၄.၃ Microsoft PowerPoint — ဒီဇိုင်းမပျက် စစ်ဆေးမှုနှင့် Slide Show (F5) အလိုအလျောက် ကာကွယ်မှု

- **Deep Slide & Notes Scanning: Checks Slide Titles, Subtitles, Bullet Boxes, Tables [Row, Col], Recursively Grouped Shapes, and Speaker Notes (plus optional ☑ Master for Slide Masters & Layouts).**  
  *ဆလိုက်ခေါင်းစဉ်၊ စာသားကွက်၊ ဇယား၊ Group ဖွဲ့ထားသော ပုံများနှင့် Speaker Notes အားလုံးကို စစ်ဆေးပေးပြီး လိုအပ်ပါက ☑ Master ဖွင့်၍ Slide Master ပါ စစ်ဆေးနိုင်ပါသည်။*
- **Automatic Slide Show (F5) Pause & Surgical Edits: Overlays and popups automatically hide the instant you start Slide Show (F5 / Presenter View), and fixes edit exact character spans so RGB colors, font sizes, bolding, and animations stay 100% intact.**  
  *Slide Show (F5) ပြသချိန်တွင် မျဉ်းလှိုင်းနှင့် Popup အားလုံး အလိုအလျောက် ရပ်နားပေးပြီး၊ စာပြင်ရာတွင်လည်း ဒီဇိုင်းအရောင်၊ ဖောင့်အရွယ်အစားနှင့် Animation များ ၁၀၀% မူလအတိုင်း ကျန်ရှိပါသည်။*

---

<a id="dictionary-suite"></a>
## 05. Offline Bilingual Dictionary (EN ⇄ MM), Audio & 25,001 Encyclopedia
### အော့ဖ်လိုင်း အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန်၊ အသံထွက်နှင့် စွယ်စုံဗဟုသုတကျမ်း

> **EN:** Search 96,263 official Burmese headwords, 57,064 English-Myanmar entries, 25,001 Encyclopedia articles, 1,929 Proverbs, 9,221 Gazetteer places, and 8 Specialized Domains with zero internet.  
> **MM:** အင်တာနက်လုံးဝမလိုဘဲ သတ်ပုံကျမ်း (၉၆,၂၆၃ ပုဒ်)၊ အင်္ဂလိပ် ⇄ မြန်မာ အဘိဓာန် (၅၇,၀၆၄ ပုဒ်)၊ စွယ်စုံဗဟုသုတ (၂၅,၀၀၁ ပုဒ်)၊ စကားပုံ (၁,၉၂၉ ပုဒ်) နှင့် ပညာရပ်နယ်ပယ် (၈) ခုကို ရှာဖွေနိုင်ပါသည်။

#### 5.1 Smart 4-Way Search, Spoken Pronunciation & Specialized Domains / ၅.၁ နည်းလမ်း (၄) မျိုးဖြင့် ရှာဖွေခြင်း၊ အသံထွက်နှင့် အထူးပြုပညာရပ် နယ်ပယ်များ

- **4-Way Input: Type in Myanmar Unicode ('သတ်ပုံ'), English ('negotiate'), Romanized Burmese ('mingalaba', 'thatpon', 'kyaukse'), or paste Zawgyi (auto-converted on the fly).**  
  *မြန်မာယူနီကုဒ်၊ အင်္ဂလိပ်စာလုံး၊ အင်္ဂလိပ်အသံထွက် ('mingalaba', 'kyaukse') သို့မဟုတ် ဇော်ဂျီစာသား ကူးထည့်၍ အလွယ်တကူ ရှာဖွေနိုင်ပါသည်။*
- **Spoken Burmese Sandhi + Offline English Speech (▶ EN): View real Burmese spoken sandhi ('သပ်-ပုံ • that-pon', IPA '[θaʔ.bòʊɴ]') and click [▶ EN] to hear offline Windows SAPI English pronunciation.**  
  *မြန်မာစကားပြော အသံထွက် (Sandhi + IPA) ကို ဖော်ပြပေးသည့်အပြင် [▶ EN] ခလုတ်နှိပ်၍ အင်္ဂလိပ်အသံထွက်ကို အော့ဖ်လိုင်း နားထောင်နိုင်ပါသည်။*
- **8 Specialized Academic & B2G Domains: Geology & Earth Sciences (ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ), Public Administration & Civil Service, Higher Education & Universities, Legal & Statutory Laws, Banking/Tax/Finance, Medical & Health, Pali & Religious Studies, and IT/Cybersecurity.**  
  *အထူးပြုနယ်ပယ် (၈) ခု — ဘူမိဗေဒနှင့် မြေကမ္ဘာသိပ္ပံ၊ ပြည်သူ့အုပ်ချုပ်ရေးနှင့် ဝန်ထမ်းရေးရာ၊ အဆင့်မြင့်ပညာရေး၊ ဥပဒေ၊ ဘဏ္ဍာရေး၊ ဆေးပညာ၊ ပါဠိ/သာသနာရေး နှင့် သတင်းအချက်အလက်နည်းပညာ ဝေါဟာရများ ပါဝင်ပါသည်။*

---

<a id="toolkit-and-reports"></a>
## 06. Official Typography Toolkit & Printable A4 Quality Audit Reports
### ရုံးစာကိရိယာများ (Toolkit) နှင့် ပုံနှိပ်ရန်အသင့် A4 အစီရင်ခံစာ ထုတ်ယူနည်း

> **EN:** One-click legacy font conversion, broken PDF reconstruction, financial Kyat Lakh words, Myanmar calendar, and 5-format ISO A4 audit reports.  
> **MM:** WinInnwa/ဇော်ဂျီ ဖောင့်ပြောင်းခြင်း၊ PDF စာပျက်ပြင်ခြင်း၊ ကျပ်သိန်းစာသားပြောင်းခြင်း၊ မြန်မာပြက္ခဒိန် နှင့် A4 အစီရင်ခံစာ ထုတ်ယူနည်း။

#### 6.1 Exporting the 5-Format Proofing & Quality Audit Report (≡ Report ▾) / ၆.၁ အရည်အသွေး စစ်ဆေးချက် အစီရင်ခံစာ (၅) မျိုး ထုတ်ယူနည်း (≡ Report ▾)

| Report Format | Best Use Case | Key Features Included |
| :--- | :--- | :--- |
| Printable A4 Word (.docx) — Recommended | Printing on A4 paper, signing & filing official QA records | True ISO A4 Portrait (210×297 mm), Quality Grade (A+..D), Per-Page/Sheet/Slide table, Confirmed Errors vs Review Needed, Repeating table headers, Page X of Y & Sign-Off box |
| Multi-Sheet Excel (.xlsx) | Sorting/filtering large audit tables across sheets | 4 dedicated sheets: Summary, Confirmed Wrong, Suspicious Review, and Fixed in Session |
| Executive PowerPoint (.pptx) | Briefing management or QA meetings | 4 widescreen 16:9 executive summary slides with KPI cards & top findings |
| UTF-8 CSV (.csv) | Importing into Python, R, Power BI, or databases | UTF-8 BOM spreadsheet covering every audited finding with location & suggestion |
| Interactive HTML / Save as PDF | Sharing standalone clickable report or saving PDF | Zero-dependency browser report with live search & ⎙ Print / Save as PDF |

---

<a id="how-it-works"></a>
## 07. How It Works Under the Hood: Hybrid NLP Architecture & Safety Engine
### စနစ်အလုပ်လုပ်ပုံ နည်းပညာဗိသုကာ (Hybrid Statistical + Symbolic Engine)

> **EN:** A transparent look inside the 6-stage offline Burmese NLP pipeline combining official 462-page orthography rules with statistical beam search.  
> **MM:** မြန်မာစာလုံးပေါင်းသတ်ပုံကျမ်း စည်းမျဉ်းများနှင့် ခေတ်မီ Statistical Language Model တို့ကို ပေါင်းစပ်ထားသော အဆင့် (၆) ဆင့် စစ်ဆေးရေးနည်းပညာ။

#### 7.1 The 6-Stage Real-Time Proofing Pipeline / ၇.၁ အဆင့် (၆) ဆင့် အချိန်နှင့်တစ်ပြေးညီ မြန်မာစာ စစ်ဆေးရေး လုပ်ငန်းစဉ်

| Stage | Component | How It Works & Why It Matters |
| :--- | :--- | :--- |
| Stage 1 | Canonical Unicode & O(N) Syllable FSM | Normalizes diacritic order (U+1031/U+103A/U+1037), detects Zawgyi/WinInnwa/garbled PDF encoding, fixes lookalikes (၀ ➔ ဝ, ၄င်း ➔ ၎င်း), and segments syllables in ~0.02 ms without regex backtracking. |
| Stage 2 | AES-256 + HMAC-SHA256 Binary Trie | Loads 96,263 active headwords and 109,880 open-addressed O(1) word bigrams into a compact Trie while keeping 83,994 bilingual/encyclopedia definitions lazily hydrated on disk. |
| Stage 3 | K=3 Beam Viterbi + Bigram LM + 45-Bit Pruner | Segments unspaced Burmese text using a Hypothesis-Recombining Beam Viterbi lattice scored by an interpolated Witten-Bell Bigram/Unigram Language Model, pruning typo candidates via 45-bit consonant masks and phonetic confusion matrices. |
| Stage 4 | Two-Tier Confusion Sets, Intruder & SOV Parser | Catches real-word homophones (လျော်/လျှော်, ပစ်မှု/ပြစ်မှု, ကုတ်/ကုဒ်), statistical 1-syllable intruder typos inside compounds, temporal/aspect clashes, Majority-Register Concord, and leaked post-verbal SOV arguments. |
| Stage 5 | Host-Safe Non-Destructive Rendering | Uses live Word.Range bindings + surgical Burmese-span NoProofing=1 in Word, and per-pixel alpha click-through Win32 layered overlays in Excel & PowerPoint so .xlsx/.pptx files are never mutated. |
| Stage 6 | EcoPerformanceGovernor Time-Slicing | Adapts chunk sizes and caches to CPU/RAM hardware (< 35 MB core proofing heap, p50 ~ 2.1 ms per sentence) so Office stays responsive even on 4 GB RAM laptops. |

---

<a id="troubleshooting"></a>
## 08. Troubleshooting, Multi-PC Tips & Author Attribution
### ပြဿနာဖြေရှင်းနည်းများ (FAQ) နှင့် ဖန်တီးသူ ဂုဏ်ပြုမှတ်တမ်း

> **EN:** Quick answers for installing on another PC, Protected View files, COM Add-ins settings, and open-source GPLv3 attribution.  
> **MM:** အခြားကွန်ပျူတာတွင် သွင်းယူရာ၌ ကြုံတတ်သော အချက်များ ဖြေရှင်းနည်းနှင့် ဖန်တီးသူ မှတ်တမ်း။

#### 8.1 Common Questions & Instant Fixes / ၈.၁ အမေးများသော ပြဿနာများနှင့် အမြန်ဖြေရှင်းနည်း

| Symptom (ကြုံတွေ့ရသော အခြေအနေ) | Cause (အကြောင်းရင်း) | 1-Step Solution (ဖြေရှင်းနည်း) |
| :--- | :--- | :--- |
| PowerPoint/Excel downloaded file won't scan | File opened in Office 'Protected View' (Read-Only) from Internet/Telegram/Email | Click 'Enable Editing' on the yellow bar at the top of Office, then click Scan. (အပေါ်ဘက် အဝါရောင်တန်းရှိ Enable Editing ကို အရင်နှိပ်ပါ။) |
| 'Myanmar Spell Checker' tab not visible on Ribbon | Add-in unchecked in Office COM Add-ins or Office was open during install | Go to File ➔ Options ➔ Add-ins ➔ Manage: COM Add-ins ➔ Go..., check 'Myanmar Spell Checker', and click OK (or re-run Install-EasyUser.bat with Office closed). |
| Installer says 'Extract ZIP First' | Double-clicked .bat inside Windows Explorer ZIP preview without extracting | Right-click the .zip file ➔ 'Extract All...' ➔ run Install-MyanmarSpellChecker.bat / Install-EasyUser.bat from the extracted folder. |
| Valid Burmese words show red lines in a mixed English+Myanmar Word file | Word's native US-English spellchecker ran before initial scan | Click 'Scan Doc' or 'Clear Marks' once — Burmese spans get NoProofing=1 automatically while English typos stay checked! |
| Myanmar letters look like squares or overlapping circles | PC does not have Pyidaungsu Unicode font installed | Install the standard Pyidaungsu font on Windows, and use Toolkit ➔ 'WinInnwa/Zawgyi ➔ Pyidaungsu' if the file uses legacy fonts. |

#### 8.2 Creator, Special Dedication, Collaborators & GNU GPL v3.0 License / ၈.၂ ဖန်တီးသူ၊ အထူးဂုဏ်ပြုမှတ်တမ်း၊ ပူးပေါင်းပါဝင်သူများနှင့် GNU GPL v3.0 လိုင်စင်

- **Original Creator & Lead Developer: Htet Yar Zar (ထက်ရာဇာ) — M.Sc. Part II Candidate in Geology (M.Sc. Part I Completed), Department of Geology, Kyaukse University, Kyaukse, Mandalay Region, Myanmar (https://github.com/Hashyz/MyanmarSpellChecker).**  
  *မူလဖန်တီးသူနှင့် ဦးဆောင်ရေးသားသူ — ထက်ရာဇာ (Htet Yar Zar)၊ မဟာသိပ္ပံ ဒုတိယနှစ် [M.Sc. Part II] ဘူမိဗေဒ အထူးပြု (M.Sc. Part I အောင်မြင်ပြီး)၊ ဘူမိဗေဒဌာန၊ ကျောက်ဆည်တက္ကသိုလ်၊ ကျောက်ဆည်မြို့၊ မန္တလေးတိုင်းဒေသကြီး။*
- **Special Dedication (မူလစိတ်ကူးလမ်းညွှန်ပေးသူ): U Moe Kyaw Thu (ဦးမိုးကျော်သူ) — Beloved Father of Htet Yar Zar, who originally inspired and gave the idea to create this Myanmar Spell & Grammar Suite.**  
  *အထူးဂုဏ်ပြုမှတ်တမ်း — ဤမြန်မာစာ စစ်ဆေးရေးစနစ် ဖန်တီးပြုစုရန် မူလစိတ်ကူးနှင့် လမ်းညွှန်မှုပေးခဲ့သူ ကျေးဇူးရှင် ဖခင်ကြီး ဦးမိုးကျော်သူ (U Moe Kyaw Thu)။*
- **Project Collaborators (ပူးပေါင်းပါဝင်သူများ): Hein Htet Zaw (ဟိန်းထက်ဇော်) & Thu Reain Khant (သူရိန်ခန့်).**  
  *ပူးပေါင်းပါဝင်သူများ — ဟိန်းထက်ဇော် (Hein Htet Zaw) နှင့် သူရိန်ခန့် (Thu Reain Khant)။*
- **Open-Source License: 100% Free & Open-Source under GNU General Public License v3.0 (GPL-3.0-or-later) with mandatory Section 7(b)/(c) author & origin preservation.**  
  *လိုင်စင် — GNU GPL v3.0 (Section 7b/7c မူလဖန်တီးသူအမည် မပယ်ဖျက်ရ စည်းကမ်းချက်အပါအဝင်) ဖြင့် အခမဲ့ Open-Source ဖြန့်ချိထားပါသည်။*

---

