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
| DesktopChecks `--preview-only` | Filter okien, živé aktualizácie WPF zoznamu vrátane predvolených časových pravidiel, vykreslenie `artifacts/desktop-checks/main-window.png` a binding chyby. Nespúšťa natívny zber ani test životnosti skutočných okien. |
| DesktopChecks bez prepínača | Navyše krátky reálny zber v aktuálnej relácii a test posledného pracovného okna procesu s pomocným oknom. Používa izolované DB, neotvára používateľskú históriu. |
| DesktopChecks `--inspect-history` | Samostatná read-only diagnostika používateľskej DB; podrobnosti v [storage.md](storage.md). Nie je súčasťou bežného testovania. |

Plné desktop overenie:

```powershell
dotnet run --project tests/SolidRecords.DesktopChecks -c Release --no-restore
```

Náhľad nespúšťa reálne `App.OnStartup`, takže neinicializuje používateľskú DB ani single-instance mechanizmus. Vygenerovaný obrázok vizuálne prezri pri zmene layoutu; render nenahrádza kontrolu skutočného 150 % DPI. Testy nevytvárajú dôkaz podpory všetkých verzií prehliadačov ani všetkých Windows shell okien.

Na ručné overenie titulkového riadka použi `--preview-only --interactive-preview`: po kontrolách otvorí okno s ukážkovými dátami a prázdnym zdrojom zberu. Over presúvanie, dvojklik na voľnú plochu hlavičky, zmenu veľkosti, minimalizáciu, maximalizáciu a kalendár. Konzola vypíše skutočné DPI okna; tento režim zatvorením ukončí náhľad.

Ručne podľa zmeny over: systray a skrytie/opätovné otvorenie GUI, start/stop z oboch miest, prepínanie okien a kariet, zamknutie/odomknutie a prestávku v časovej osi, uspatie/RDP, posledné okno procesu, dostupné prehliadače, navigáciu medzi dňami a rozloženie pri 150 % DPI. Pri testovaní prahov drž návštevu aspoň 15 s; kratšia aktivita má chýbať.

## Spúšťanie a publikácia

Ikona aplikácie používa monogram S v modrom štvorci. Vektorový zdroj je `src/SolidRecords.App/Assets/SolidRecords.xaml`; hlavička ho načítava priamo ako `DrawingImage` vo veľkosti 48 logických bodov cez titulkový a štatistický riadok. Po úprave zdroja spusti `powershell -NoProfile -ExecutionPolicy Bypass -STA -File tools/Generate-AppIcon.ps1`. Skript vytvorí `SolidRecords.ico` s veľkosťami 16, 20, 24, 32, 40, 48, 64, 96, 128 a 256 px a náhľady v `artifacts/icon-preview/`. Rovnaké ICO používa EXE, systémová ikona okna aj systémová lišta.

```powershell
dotnet run --project src/SolidRecords.App -- --show
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Pred overením novej zostavy ukonči starú cez systray: druhá inštancia iba otvorí už bežiacu. Bežné spustenie aplikácie používa reálnu históriu a môže migrovať databázu. Publikácia je prenosná, bez inštalátora; autostart nastavuj až z finálneho priečinka.

## Aktuálny stav a hranice

- Hotové: systray, lokálny zber relácie/okien/URL, kompozitná SQLite evidencia, minimum a zlúčenie aktivít, živý denný plochý zoznam, časová os, textové vyhľadávanie a voliteľný autostart.
- Kompaktné GUI spája titulkový riadok, záznam a výber dňa do jednej hlavičky cez WPF `WindowChrome`, s rovnakým pozadím ako štatistiky. Tenký systémový rám zachováva `GlassFrameThickness="1"`; na Windows 11 sa pri vytvorení okna žiada zaoblenie cez DWM (`WindowAppearance`). Skutočný rám, rohy a tieň over na živom okne, obsahový PNG náhľad ich nevykresľuje. Voľná plocha hlavičky presúva okno a podporuje systémové gestá; tlačidlá okna používajú `SystemCommands`, zatvorenie naďalej skryje aplikáciu do systray. Počiatočná veľkosť sa obmedzuje dostupnou pracovnou plochou. Chýba manuálne tlačidlo Obnoviť; zoznam žije zo zápisov Recorderu.
- Zámerne bez priameho monitorovania procesov, skupinového pohľadu, checkboxov a timetracking integrácie. Interné udalosti sa v GUI nezobrazujú.
- Zatiaľ bez ďalších filtrov, používateľskej konfigurácie 15 s prahov, detekcie idle podľa vstupu, šifrovania, retencie a inštalátora. `settings.json` neobsahuje prahy.
- Zber vidí len reláciu bežiacej aplikácie; neobnovuje udalosti z obdobia, keď nebežala alebo bola pozastavená. URL a privilegované okná závisia od dostupnosti Windows/accessibility API.
- Pri poslednej implementácii pravidiel aktivity prešlo 49 xUnit prípadov a desktop náhľad so živými aktualizáciami. Ide o historický výsledok; po zmene spusti príslušné kontroly znova.
