# Vývoj a overovanie

## Prostredie a príkazy

Windows 10/11, .NET 10 SDK. `global.json` požaduje 10.0.100 s `rollForward=latestFeature`; spoločné vlastnosti a centrálna produktová `Version` sú v `Directory.Build.props`. Dialóg O aplikácii zobrazuje odvodenú `AssemblyInformationalVersion` vrátane prípony vývojovej verzie, bez metadát za `+`. Riešenie používa formát `SolidRecords.slnx`.

Z koreňa repozitára:

```powershell
dotnet restore SolidRecords.slnx
dotnet build SolidRecords.slnx -c Release --no-restore
dotnet test tests/SolidRecords.Tests -c Release --no-restore
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore -- --startup-only
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore -- --startup-only --paused
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore -- --updates-only
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore -- --preview-only
git diff --check
```

`--no-restore` je vhodné po úspešnom restore, zvlášť v prostredí s obmedzeným prístupom k NuGet konfigurácii alebo sieti; nenahrádza chýbajúce balíky/assets po zmene závislostí. Samotná úprava dokumentácie nevyžaduje build ani desktop testy.

Samostatná izolovaná kontrola priority WTS správ voči priebežnej kontrole plochy: `dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore -- --session-only`. Overuje zamknutie, odpojenie, odhlásenie, ukončenie relácie a dvojicu lock/unlock bez falošných medziľahlých udalostí. Nemení skutočný stav relácie ani používateľskú databázu; beží aj ako súčasť náhľadu.

## Rozsah kontrol

| Kontrola | Čo overuje |
| --- | --- |
| `SolidRecords.Tests` | Stavový automat, životnosti rodičov, PID/URL zmeny, Recorder start/stop a chyby, SQLite perzistenciu a migrácie, retenciu vrátane hraníc, otvorených aktivít a súbežného zberu, kompatibilitu pracovného rozvrhu so staršími nastaveniami, pracovné hranice vrátane letného času, automatické prechody a rozsah osi, prahy, zlúčenie/rozdelenie úsekov a hranice dňa. |
| DesktopChecks `--startup-only` (aj s `--paused`) | Samostatný proces overí skutočný životný cyklus `App` s validovaným DI kontajnerom: spoločné inštancie okna a view modelu, štart so zapnutým/vypnutým záznamom, otvorenie a skrytie okna, nezávislosť preferencie štartu od prepnutia záznamu, prevzatie JSON, validáciu pracovných hodín a výber dní v editore, uloženie všetkých nastavení do DB, okamžité použitie pracovného rozvrhu a zachovanie ručnej pauzy medzi hranicami, zmenu spoločného minima a ukončenie s jediným uvoľnením zdroja. Používa izolovanú SQLite DB v `artifacts/startup-checks/` a prázdny zdroj bez natívneho zberu. Vytvorí dočasnú systray ikonu; nemení register ani používateľské dáta. |
| DesktopChecks `--preview-only` | Natívny hit-test oboch riadkov hlavičky vrátane štatistík a plochy pod logom pri bežnej aj minimálnej šírke; tlačidlá, kalendár a časová os zostávajú klientskou oblasťou. Filter okien, číselné triedenie trvania, trojkrokové triedenie všetkých stĺpcov a návrat k pôvodnému poradiu, dostupnosť dní v kalendári a preskakovanie prázdnych dní vrátane obnovy po retencii, odlíšenie dostupných dní a vypnutie hit-testu zakázaných dní v otvorenom kalendári (náhľady svetlej a tmavej témy pri 100 % a 150 % v súboroch s prefixom calendar-), časové oddeľovače relácie vrátane skrytia počiatočných stavov záznamu zo staršej histórie a zachovania pri vyhľadávaní a obnove, navigáciu šípkami cez viac oddeľovačov aj virtualizované riadky, farby pozadia a textu aktivít po prepnutí témy; živé aktualizácie WPF zoznamu vrátane predvolených časových pravidiel a doplnenia uloženej ikony bez výmeny riadka, vykreslenie svetlej a tmavej témy do `artifacts/desktop-checks/main-window*.png` pri 100 % a 150 % mierke, prepnutie témy na existujúcom okne so zachovaním výberu a binding chyby. Vzorky zoznamu obsahujú ikonu testovacieho EXE získanú cez Shell, aby náhľad nezávisel od nainštalovaných aplikácií. Vykresľuje aj všetky štyri záložky dialógu Nastavenia (`settings-{general,recording,work-hours,updates}-*.png`) vrátane príkazu ručnej aktualizácie s falošným výsledkom a dialóg O aplikácii v oboch témach a mierkach (`about-*.png`) a vypíše jeho skutočné DPI. Systray náhľad používa rovnakú factory položiek a prepínanie textov ako bežiaca aplikácia. Samostatné menu vykreslí pre oba stavy záznamu s položkou Nastavenia v oboch témach (`tray-menu-*.png`), bez vytvorenia NotifyIcon a zásahu do registra. Otvorí aj skutočné natívne menu, overí `GUI_INMENUMODE`, zhodnú polohu prvého/opakovaného otvorenia, zrušenie a klávesovú akciu vykonanú až po opustení menu slučky. Jeho izolovaný živý snímok uloží ako `tray-native-*.png`. Pomocný WinForms náhľad má navyše kontrolu šípok, Enter, Esc a kliknutia mimo cez WPF filter správ. Správanie panelu skrytých ikon Windows vyžaduje ručnú kontrolu. Menu sa otvorí pri skutočnom DPI monitora; exporty `-150.png` sú prepočítané na 150 % a nenahrádzajú živú kontrolu pri tomto DPI. Overuje aj prepínač detailov, prepínanie aplikácie podľa výberu, úplnosť procesov a aktivít pri filtrovaní, zachovanie jediného uzla toho istého procesu po obnovení zberu a vyprázdnenie pri prázdnom výsledku. Náhľady `main-window-details*.png` zobrazujú panel aj pri minimálnej šírke a 150 % mierke. Nespúšťa natívny zber ani test životnosti skutočných okien. |
| DesktopChecks bez prepínača | Navyše krátky reálny zber v aktuálnej relácii a test posledného pracovného okna procesu s pomocným oknom. Používa izolované DB, neotvára používateľskú históriu. |
| DesktopChecks `--updates-only` | Izolovaná súborová cache aktualizácií, MSI veľkosť a SHA-256, odstránenie neúplných súborov a tri akcie toast XML. Nepoužíva sieť, register ani skutočný inštalátor; podrobnosti a ručné kontroly sú v [updates.md](updates.md). |
| DesktopChecks `--inspect-history` | Samostatná read-only diagnostika používateľskej DB; podrobnosti v [storage.md](storage.md). Nie je súčasťou bežného testovania. |

Plné desktop overenie:

```powershell
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore
```

Náhľad nespúšťa reálne `App.OnStartup`, takže neinicializuje používateľskú DB ani single-instance mechanizmus. Vygenerovaný obrázok vizuálne prezri pri zmene layoutu; render nenahrádza kontrolu skutočného 150 % DPI. Testy nevytvárajú dôkaz podpory všetkých verzií prehliadačov ani všetkých Windows shell okien.

Na ručné overenie titulkového riadka použi `--preview-only --interactive-preview`: po kontrolách otvorí okno s ukážkovými dátami a prázdnym zdrojom zberu. Over presúvanie, dvojklik a pravý klik v oboch riadkoch hlavičky vrátane štatistík a loga, zmenu veľkosti, minimalizáciu, maximalizáciu a kalendár. Konzola vypíše skutočné DPI okna; tento režim zatvorením ukončí náhľad. Náhľad sleduje systémovú tému: v Nastaveniach Windows → Prispôsobenie → Farby prepni režim aplikácií medzi svetlým a tmavým, aj pri skrytom okne. Over text, výber riadka, vyhľadávanie, otvorený kalendár, tooltipy a rám. Menu systémovej lišty over v bežiacej aplikácii. Prepínač `--preview-only --search-preview` krátko zobrazí izolované okno a vytvorí aj náhľady vyhľadávania so skutočným klávesovým fokusom v oboch témach (`main-window-search-*-150.png`); konzola vypíše DPI okna.

Ručne podľa zmeny over: systray a skrytie/opätovné otvorenie GUI, start/stop z oboch miest, prepínanie okien a kariet, zamknutie/odomknutie a prestávku v časovej osi, uspatie/RDP, posledné okno procesu, dostupné prehliadače, navigáciu medzi dňami a rozloženie pri 150 % DPI. Pri testovaní prahov drž návštevu aspoň 15 s; kratšia aktivita má chýbať.

## Zmeny databázovej schémy

Mapovanie a EF migrácie patria do Storage. Po zmene `ActivityDbContext` vytvor migráciu z koreňa repozitára (nástroj `dotnet-ef` používaj vo verzii 10.0.12):

```powershell
dotnet ef migrations add NazovZmeny --project src/SolidRecords.Storage --output-dir Migrations
```

`ActivityDbContextFactory` používa iba návrhovú cestu `artifacts/ef-design.db`; nástroj nesmie cieliť na používateľskú históriu. Commitni migráciu, designer aj model snapshot a over upgrade na izolovanej DB v `artifacts/`. Test `FreshDatabaseAppliesEfMigrationsAndRepeatedInitializationKeepsLiveActivityOpen` kontroluje aj zhodu snapshotu s modelom. Migrácie pri štarte aplikuje aplikácia automaticky; `EnsureCreated` sa nepoužíva. Prevzatie starých verzií a transakčné hranice opisuje [storage.md](storage.md).

## Spúšťanie a publikácia

Ikona aplikácie používa monogram S v modrom štvorci. Vektorový zdroj je `src/SolidRecords.App/Assets/SolidRecords.xaml`; hlavička ho načítava priamo ako `DrawingImage` vo veľkosti 48 logických bodov cez titulkový a štatistický riadok. Po úprave zdroja spusti `powershell -NoProfile -ExecutionPolicy Bypass -STA -File tools/Generate-AppIcon.ps1`. Skript vytvorí `SolidRecords.ico` s veľkosťami 16, 20, 24, 32, 40, 48, 64, 96, 128 a 256 px a náhľady v `artifacts/icon-preview/`. Rovnaké ICO používa EXE, systémová ikona okna aj systémová lišta.

```powershell
dotnet run --project src/SolidRecords.App -- --show
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Vývojový režim je konfigurácia `Debug` (predvolená pre `dotnet run`); používa samostatné dáta podľa [storage.md](storage.md). Môže bežať súčasne s nainštalovanou Release aplikáciou. Pred overením novej zostavy ukonči starú zostavu rovnakého režimu a repozitára cez systray: druhá inštancia iba otvorí už bežiacu. Spustenie Release aplikácie používa reálnu históriu a môže migrovať databázu. Diagnostika `--inspect-history` naďalej cieli na používateľskú Release databázu aj pri spustení v Debug konfigurácii. Prenosnú publikáciu spúšťaj z finálneho priečinka; prvý štart predvolene nastaví autostart pre aktuálnu cestu. MSI z GitHub Releases inštaluje rovnakú self-contained aplikáciu do Program Files.

## Automatické vydanie

Workflow `.github/workflows/release.yml` reaguje na push tagu `vX.Y.Z`. Overí presný formát bez úvodných núl, MSI limity major ≤ 255, minor ≤ 255 a patch ≤ 65535 a práve jednu neprázdnu sekciu danej verzie v `CHANGELOG.md`. Pred pushom musí byť v označenom commite pripravená hlavička `## [X.Y.Z] - YYYY-MM-DD` s položkami zmien; `Unreleased` sa do poznámok vydania nepreberá. Pred zostavením sa kontroluje aj presná zhoda verzie tagu bez `v` s jedinou `<Version>` v commitnutom `Directory.Build.props`; chýbajúca, duplicitná alebo odlišná verzia (aj s vývojovou príponou) zastaví workflow. Samotný push tagu bez príslušnej sekcie skončí chybou. Verziu vydania priprav podľa projektového release postupu; workflow ju pri zostavení explicitne preberá z tagu cez MSBuild `Version`.

Na runneri `windows-2025` sa obnoví a zostaví riešenie, spustia xUnit testy a DesktopChecks `--updates-only` aj `--preview-only`. Až po ich úspechu nasleduje netrimovaná self-contained publikácia `win-x64`, WiX 5.0.2 MSI a prenosný ZIP. WiX je pripnutý na konkrétnu verziu a inštaluje sa do izolovaného `artifacts/wix`. `tools/Build-Installer.ps1` kontroluje prítomnosť runtime, zhodu súborovej verzie aplikácie s MSI aj zhodu publikovaného súboru `LICENSE` s repozitárom. Pri balení pridá pripnuté rozšírenie `WixToolset.UI.wixext/5.0.2` do lokálnej cache `.wix/` (pri prvom použití potrebuje NuGet). Z koreňového `LICENSE` vytvorí `artifacts/installer/LICENSE.rtf` pre slovenský licenčný dialóg `WixUI_Minimal`. Súbor `LICENSE` sa pri zostavení a publikácii kopíruje vedľa aplikácie, takže ho obsahujú MSI aj prenosný ZIP. Dočasný release artefakt v Actions sa uchováva 1 deň, diagnostika testov a náhľady 3 dni. Táto retencia neovplyvňuje MSI a ZIP pripojené ku GitHub Release.

Samostatný job s oprávnením `contents: write` vytvorí GitHub Release pre existujúci tag cez `gh release create --verify-tag`. Pripojí `SolidRecords-X.Y.Z-win-x64.msi` a `SolidRecords-X.Y.Z-win-x64.zip`; telo vydania tvorí presný obsah verziovej sekcie changelogu so zachovanými kategóriami. Stačí vstavaný `GITHUB_TOKEN`, ďalší secret netreba. Existujúce vydanie sa neprepisuje; opakovaný beh po úspešnom publikovaní skončí pri vytvorení release chybou.

MSI je x64, inštaluje pre všetkých používateľov do `%ProgramFiles%\SolidRecords` a potrebuje administrátorské oprávnenia na inštaláciu. Aplikácia sa potom spúšťa bez administrátorských práv, cez odkaz v ponuke Štart s `--show`. Posledná stránka novej inštalácie alebo major upgradu obsahuje predvolene zapnutú voľbu spustenia aplikácie; akcia sa vykoná až tlačidlom Dokončiť, s `--show` v kontexte klienta inštalátora. Pri oprave, odinštalovaní a tichej inštalácii sa táto UI akcia nevykoná. MSI ukladá cestu inštalácie do HKLM pre reštart po aktualizácii; jeho postup a oprávnenia opisuje [updates.md](updates.md). Stabilný `UpgradeCode` zabezpečuje aktualizáciu starších MSI a blokuje downgrade. Pred ručnou aktualizáciou alebo odinštalovaním aplikáciu ukonči cez systray; autostart pred odinštalovaním vypni v aplikácii. Inštalátor autostart nezapína ani nečistí používateľské HKCU nastavenia. Používateľskú databázu a nastavenia neobsahuje ani neodstraňuje. MSI a EXE sa zatiaľ digitálne nepodpisujú.

Lokálne overenie balenia (ukážková verzia musí zodpovedať publikácii):

```powershell
./tools/Test-ReleaseMetadata.ps1
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -p:Version=1.2.3 -p:PublishSingleFile=false -p:PublishTrimmed=false -o artifacts/publish/win-x64
dotnet tool install wix --version 5.0.2 --tool-path artifacts/wix
./tools/Build-Installer.ps1 -Version 1.2.3
```

WiX stačí nainštalovať raz; výstup je v `artifacts/release/`. Pri kontrole MSI over slovenský licenčný dialóg s úplným textom GPL v3, predvolene zapnutú voľbu spustenia na poslednej stránke aj jej vypnutie a prítomnosť `LICENSE` v nainštalovanom priečinku aj v ZIPe. Over tiež major upgrade, opravu a odinštalovanie bez nechceného spustenia aplikácie. Inštaláciu, upgrade a odinštalovanie overuj v izolovanom Windows prostredí, bez spustenia nad používateľskou históriou. Test extrakcie changelogu používa vlastné súbory pod `artifacts/release-metadata-tests/`.

## Aktuálny stav a hranice

- Hotové: systray, lokálny zber relácie/okien/URL, kompozitná SQLite evidencia, minimum a zlúčenie aktivít, živý denný plochý zoznam s voliteľným stromom detailov aplikácie, časová os, textové vyhľadávanie a voliteľný autostart.
- Kompaktné GUI spája titulkový riadok, záznam a výber dňa do jednej hlavičky cez WPF `WindowChrome`, s rovnakým pozadím ako štatistiky. Tenký systémový rám zachováva `GlassFrameThickness="1"`; na Windows 11 sa pri vytvorení okna žiada zaoblenie cez DWM (`WindowAppearance`). Skutočný rám, rohy a tieň over na živom okne, obsahový PNG náhľad ich nevykresľuje. Celá hlavička vrátane riadka so štatistikami a loga tvorí titulkovú oblasť podľa svojej skutočnej výšky; podporuje presúvanie, dvojklik na maximalizáciu/obnovenie a systémové menu pravým kliknutím. Interaktívne ovládacie prvky si zachovávajú vlastné ovládanie; tlačidlá okna používajú `SystemCommands`, zatvorenie naďalej skryje aplikáciu do systray. Počiatočná veľkosť sa obmedzuje dostupnou pracovnou plochou. Chýba manuálne tlačidlo Obnoviť; zoznam žije zo zápisov Recorderu.
- Zámerne bez priameho monitorovania procesov, checkboxov a timetracking integrácie. GUI zobrazuje udalosti relácie ako časové oddeľovače; ostatné interné udalosti zostávajú skryté.
- Zatiaľ bez ďalších filtrov, konfigurácie prahu zlúčenia, detekcie idle podľa vstupu a šifrovania. Používateľské nastavenia vrátane minima aktivity sa ukladajú do databázy.
- Zber vidí len reláciu bežiacej aplikácie; neobnovuje udalosti z obdobia, keď nebežala alebo bola pozastavená. URL a privilegované okná závisia od dostupnosti Windows/accessibility API.
- Pri poslednej implementácii pravidiel aktivity prešlo 49 xUnit prípadov a desktop náhľad so živými aktualizáciami. Ide o historický výsledok; po zmene spusti príslušné kontroly znova.
