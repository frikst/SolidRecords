# Vývoj a overovanie

## Prostredie a príkazy

Windows 10/11, .NET 10 SDK. `global.json` požaduje 10.0.100 s `rollForward=latestFeature`; spoločné vlastnosti sú v `Directory.Build.props`. Riešenie používa formát `SolidRecords.slnx`.

Z koreňa repozitára:

```powershell
dotnet restore SolidRecords.slnx
dotnet build SolidRecords.slnx -c Release --no-restore
dotnet test tests/SolidRecords.Tests -c Release --no-restore
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore -- --preview-only
git diff --check
```

`--no-restore` je vhodné po úspešnom restore, zvlášť v prostredí s obmedzeným prístupom k NuGet konfigurácii alebo sieti; nenahrádza chýbajúce balíky/assets po zmene závislostí. Samotná úprava dokumentácie nevyžaduje build ani desktop testy.

## Rozsah kontrol

| Kontrola | Čo overuje |
| --- | --- |
| `SolidRecords.Tests` | Stavový automat, životnosti rodičov, PID/URL zmeny, Recorder start/stop a chyby, SQLite perzistenciu a migrácie, prahy, zlúčenie/rozdelenie úsekov a hranice dňa. |
| DesktopChecks `--preview-only` | Filter okien, živé aktualizácie WPF zoznamu vrátane predvolených časových pravidiel, vykreslenie svetlej a tmavej témy do `artifacts/desktop-checks/main-window*.png` pri 100 % a 150 % mierke, prepnutie témy na existujúcom okne so zachovaním výberu a binding chyby. Nespúšťa natívny zber ani test životnosti skutočných okien. |
| DesktopChecks bez prepínača | Navyše krátky reálny zber v aktuálnej relácii a test posledného pracovného okna procesu s pomocným oknom. Používa izolované DB, neotvára používateľskú históriu. |
| DesktopChecks `--inspect-history` | Samostatná read-only diagnostika používateľskej DB; podrobnosti v [storage.md](storage.md). Nie je súčasťou bežného testovania. |

Plné desktop overenie:

```powershell
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore
```

Náhľad nespúšťa reálne `App.OnStartup`, takže neinicializuje používateľskú DB ani single-instance mechanizmus. Vygenerovaný obrázok vizuálne prezri pri zmene layoutu; render nenahrádza kontrolu skutočného 150 % DPI. Testy nevytvárajú dôkaz podpory všetkých verzií prehliadačov ani všetkých Windows shell okien.

Na ručné overenie titulkového riadka použi `--preview-only --interactive-preview`: po kontrolách otvorí okno s ukážkovými dátami a prázdnym zdrojom zberu. Over presúvanie, dvojklik na voľnú plochu hlavičky, zmenu veľkosti, minimalizáciu, maximalizáciu a kalendár. Konzola vypíše skutočné DPI okna; tento režim zatvorením ukončí náhľad. Náhľad sleduje systémovú tému: v Nastaveniach Windows → Prispôsobenie → Farby prepni režim aplikácií medzi svetlým a tmavým, aj pri skrytom okne. Over text, výber riadka, vyhľadávanie, otvorený kalendár, tooltipy a rám. Menu systémovej lišty over v bežiacej aplikácii. Prepínač `--preview-only --search-preview` krátko zobrazí izolované okno a vytvorí aj náhľady vyhľadávania so skutočným klávesovým fokusom v oboch témach (`main-window-search-*-150.png`); konzola vypíše DPI okna.

Ručne podľa zmeny over: systray a skrytie/opätovné otvorenie GUI, start/stop z oboch miest, prepínanie okien a kariet, zamknutie/odomknutie a prestávku v časovej osi, uspatie/RDP, posledné okno procesu, dostupné prehliadače, navigáciu medzi dňami a rozloženie pri 150 % DPI. Pri testovaní prahov drž návštevu aspoň 15 s; kratšia aktivita má chýbať.

## Spúšťanie a publikácia

Ikona aplikácie používa monogram S v modrom štvorci. Vektorový zdroj je `src/SolidRecords.App/Assets/SolidRecords.xaml`; hlavička ho načítava priamo ako `DrawingImage` vo veľkosti 48 logických bodov cez titulkový a štatistický riadok. Po úprave zdroja spusti `powershell -NoProfile -ExecutionPolicy Bypass -STA -File tools/Generate-AppIcon.ps1`. Skript vytvorí `SolidRecords.ico` s veľkosťami 16, 20, 24, 32, 40, 48, 64, 96, 128 a 256 px a náhľady v `artifacts/icon-preview/`. Rovnaké ICO používa EXE, systémová ikona okna aj systémová lišta.

```powershell
dotnet run --project src/SolidRecords.App -- --show
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Pred overením novej zostavy ukonči starú cez systray: druhá inštancia iba otvorí už bežiacu. Bežné spustenie aplikácie používa reálnu históriu a môže migrovať databázu. Prenosnú publikáciu spúšťaj z finálneho priečinka; až potom nastav autostart. MSI z GitHub Releases inštaluje rovnakú self-contained aplikáciu do Program Files.

## Automatické vydanie

Workflow `.github/workflows/release.yml` reaguje na push tagu `vX.Y.Z`. Overí presný formát bez úvodných núl, MSI limity major ≤ 255, minor ≤ 255 a patch ≤ 65535 a práve jednu neprázdnu sekciu danej verzie v `CHANGELOG.md`. Pred pushom musí byť v označenom commite pripravená hlavička `## [X.Y.Z] - YYYY-MM-DD` s položkami zmien; `Unreleased` sa do poznámok vydania nepreberá. Samotný push tagu bez príslušnej sekcie skončí chybou. Verziu vydania priprav podľa projektového release postupu; workflow ju pri zostavení explicitne preberá z tagu cez MSBuild `Version`.

Na runneri `windows-2025` sa obnoví a zostaví riešenie, spustia xUnit testy a DesktopChecks `--preview-only`. Až po ich úspechu nasleduje netrimovaná self-contained publikácia `win-x64`, WiX 5.0.2 MSI a prenosný ZIP. WiX je pripnutý na konkrétnu verziu a inštaluje sa do izolovaného `artifacts/wix`. `tools/Build-Installer.ps1` kontroluje prítomnosť runtime aj zhodu súborovej verzie aplikácie s MSI. Dočasný release artefakt v Actions sa uchováva 1 deň, diagnostika testov a náhľady 3 dni. Táto retencia neovplyvňuje MSI a ZIP pripojené ku GitHub Release.

Samostatný job s oprávnením `contents: write` vytvorí GitHub Release pre existujúci tag cez `gh release create --verify-tag`. Pripojí `SolidRecords-X.Y.Z-win-x64.msi` a `SolidRecords-X.Y.Z-win-x64.zip`; telo vydania tvorí presný obsah verziovej sekcie changelogu so zachovanými kategóriami. Stačí vstavaný `GITHUB_TOKEN`, ďalší secret netreba. Existujúce vydanie sa neprepisuje; opakovaný beh po úspešnom publikovaní skončí pri vytvorení release chybou.

MSI je x64, inštaluje pre všetkých používateľov do `%ProgramFiles%\SolidRecords` a potrebuje administrátorské oprávnenia na inštaláciu. Aplikácia sa potom spúšťa bez administrátorských práv, cez odkaz v ponuke Štart s `--show`. Stabilný `UpgradeCode` zabezpečuje aktualizáciu starších MSI a blokuje downgrade. Pred aktualizáciou alebo odinštalovaním aplikáciu ukonči cez systray; autostart pred odinštalovaním vypni v aplikácii. Inštalátor autostart nezapína ani nečistí používateľské HKCU nastavenia. Používateľskú databázu a nastavenia neobsahuje ani neodstraňuje. MSI a EXE sa zatiaľ digitálne nepodpisujú.

Lokálne overenie balenia (ukážková verzia musí zodpovedať publikácii):

```powershell
./tools/Test-ReleaseMetadata.ps1
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -p:Version=1.2.3 -p:PublishSingleFile=false -p:PublishTrimmed=false -o artifacts/publish/win-x64
dotnet tool install wix --version 5.0.2 --tool-path artifacts/wix
./tools/Build-Installer.ps1 -Version 1.2.3
```

WiX stačí nainštalovať raz; výstup je v `artifacts/release/`. Inštaláciu, upgrade a odinštalovanie overuj v izolovanom Windows prostredí, bez spustenia nad používateľskou históriou. Test extrakcie changelogu používa vlastné súbory pod `artifacts/release-metadata-tests/`.

## Aktuálny stav a hranice

- Hotové: systray, lokálny zber relácie/okien/URL, kompozitná SQLite evidencia, minimum a zlúčenie aktivít, živý denný plochý zoznam, časová os, textové vyhľadávanie a voliteľný autostart.
- Kompaktné GUI spája titulkový riadok, záznam a výber dňa do jednej hlavičky cez WPF `WindowChrome`, s rovnakým pozadím ako štatistiky. Tenký systémový rám zachováva `GlassFrameThickness="1"`; na Windows 11 sa pri vytvorení okna žiada zaoblenie cez DWM (`WindowAppearance`). Skutočný rám, rohy a tieň over na živom okne, obsahový PNG náhľad ich nevykresľuje. Voľná plocha hlavičky presúva okno a podporuje systémové gestá; tlačidlá okna používajú `SystemCommands`, zatvorenie naďalej skryje aplikáciu do systray. Počiatočná veľkosť sa obmedzuje dostupnou pracovnou plochou. Chýba manuálne tlačidlo Obnoviť; zoznam žije zo zápisov Recorderu.
- Zámerne bez priameho monitorovania procesov, skupinového pohľadu, checkboxov a timetracking integrácie. Interné udalosti sa v GUI nezobrazujú.
- Zatiaľ bez ďalších filtrov, používateľskej konfigurácie 15 s prahov, detekcie idle podľa vstupu, šifrovania a retencie. `settings.json` neobsahuje prahy.
- Zber vidí len reláciu bežiacej aplikácie; neobnovuje udalosti z obdobia, keď nebežala alebo bola pozastavená. URL a privilegované okná závisia od dostupnosti Windows/accessibility API.
- Pri poslednej implementácii pravidiel aktivity prešlo 49 xUnit prípadov a desktop náhľad so živými aktualizáciami. Ide o historický výsledok; po zmene spusti príslušné kontroly znova.
