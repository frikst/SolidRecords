# SolidRecords

Lokálny záznam práce na počítači pre Windows, postavený na **.NET 10 / WPF**. Aplikácia beží v systémovej lište a ukladá udalosti a časové intervaly do SQLite. Rozhranie je v slovenčine.

## Inštalácia

V GitHub Releases repozitára nájdete MSI inštalátor a prenosný ZIP pre Windows x64 s pribaleným .NET runtime. MSI vyžaduje administrátorské oprávnenia na inštaláciu a pridá SolidRecords do ponuky Štart. Samotná aplikácia ich nevyžaduje. Pred aktualizáciou aplikáciu ukončite cez systray; pred odinštalovaním vypnite aj spúšťanie po prihlásení. Odinštalovanie ponechá lokálnu históriu a nastavenia.

## Spustenie

Vyžaduje Windows 10/11 a .NET 10 SDK pre zostavenie, prípadne .NET 10 Desktop Runtime pre framework-dependent publikáciu.

```powershell
dotnet build SolidRecords.slnx
dotnet run --project src/SolidRecords.App -- --show
```

Pri bežnom spustení bez `--show` sa aplikácia otvorí v systray. Prvýkrát začne zaznamenávať automaticky. Dvojklik na ikonu alebo položka **Otvoriť SolidRecords** zobrazí prehľad. Zatvorenie okna ponechá aplikáciu bežať; položka **Ukončiť aplikáciu** zastaví zber, dopíše front udalostí a aplikáciu ukončí.

Záznam sa spúšťa a zastavuje tlačidlom v okne alebo položkou kontextového menu ikony. Voľba zostane uložená aj pri ďalšom spustení. **Spúšťať po prihlásení** v kontextovom menu voliteľne pridá aktuálnu cestu aplikácie do `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. Zapínajte ju z finálneho priečinka publikovanej aplikácie; po presunutí aplikácie ju vypnite a zapnite znova.

## Funkcie

- Aktívna a neaktívna používateľská relácia: prihlásenie, odhlásenie, zamknutie, odomknutie, pripojenie a odpojenie RDP; zaznamenáva sa aj uspatie a obnovenie.
- Životnosť procesu odvodená z okien: od prvého aktívneho okna až po zatvorenie posledného evidovaného okna procesu.
- Aktivovanie a deaktivovanie okna, zmena názvu okna a zmena dostupnej URL.
- Kompozitná evidencia proces → okná → aktívne úseky okna alebo URL. GUI zobrazuje plochý zoznam listových aktivít s PID, názvom okna, URL a časmi. Udalosti sa v GUI nezobrazujú.
- Svetlá a tmavá téma sa automaticky riadi nastavením režimu aplikácií vo Windows a prepína sa aj počas behu aplikácie.
- Denná časová os s farbami aplikácií, denný súčet času v aktívnych oknách, prvá a posledná aktivita.
- Výber dňa a textové vyhľadávanie podľa aplikácie, dokumentu alebo URL. Okno je živé: po zápise zmien sa aktualizuje zoznam, súhrn aj časová os. Otvorená aktivita má pri konci a trvaní pomlčku; hodnoty sa zobrazia až po jej ukončení. Denný súčet a časová os sa priebežne aktualizujú aj počas aktivity. Tlačidlo na manuálne obnovenie nie je potrebné. Ďalšie filtre zatiaľ nie sú pridané.
- Žiadny skupinový pohľad, zaškrtávacie políčka ani pridávanie, zlučovanie a mazanie položiek timetrackingu.

Súčet sa počíta výhradne z listových aktivít: v každom okamihu sa započíta aktívne okno alebo jeho aktívna URL, nikdy oba súčasne. Životnosť procesu a otvoreného okna sa nesčítava. Aplikácia na pozadí, minimalizované okno a čas medzi opakovanými aktiváciami nepridávajú pracovný čas. Nie je to detekcia nečinnosti podľa klávesnice a myši. Zamknutie, odpojenie a uspatie aktívny úsek uzatvoria; rodičovské záznamy môžu zostať otvorené. Intervaly sa v dennom prehľade orežú na miestnu polnoc vrátane prechodov letného času. Vyhľadávanie filtruje zoznam; denný súčet a časová os zobrazujú celý deň.

Aktivita kratšia než **15 sekúnd** sa do pracovného logu neuloží a nezobrazí. Otvorená aktivita sa objaví až po dosiahnutí tejto hranice, so zachovaním pôvodného začiatku. Každý samostatný aktívny úsek musí hranicu dosiahnuť; viac krátkych návštev sa nesčítava na splnenie minima. Ak po vyradení krátkych aktivít zostanú dve totožné aktivity priamo po sebe a medzera medzi nimi je **menšia než 15 sekúnd**, uložia sa ako jedna aktivita. Presne 15-sekundová aktivita sa prijme, presne 15-sekundová medzera sa nezlúči. Identita zahŕňa záznam procesu a okna, názov dokumentu aj URL; dve rôzne inštancie programu sa nezlúčia.

Zlúčenie zachová skutočné aktívne úseky: 20 s práce + 5 s prestávka + 20 s práce = jeden záznam s trvaním 40 s. Časová os naďalej zobrazuje prestávku. Rovnaké pravidlá sa používajú aj pri zobrazení pôvodnej histórie bez jej hromadného mazania. Minima sa posudzujú pred orezaním na deň, aby sa nestratila platná aktivita zasahujúca cez polnoc. Oba prahy sú zatiaľ pevné; nastavenie cez GUI nie je pridané.

## Hranice zberu

Aplikácia funguje bez administrátorských práv. Sleduje iba reláciu, v ktorej beží, a iba počas zapnutého záznamu. Druhé spustenie otvorí okno už bežiacej inštancie. Systémové a pomocné okná (menu, taskbar, prepínač úloh, zamykacia obrazovka) sa vynechávajú; čas v nich tvorí prestávku.

URL sa zisťuje približne raz za sekundu z adresného riadka Chrome, Edge, Firefoxu, Brave, Opery a Vivaldi. Dostupnosť závisí od verzie, jazyka, accessibility podpory a oprávnení. Pri nedostupnej URL zostane názov okna. Počas písania do adresného riadka sa jeho obsah neukladá. Veľmi krátke návštevy nemusia byť zachytené; nejde o úplnú históriu prehliadača.

Pri vynútenom ukončení alebo výpadku napájania nemusia byť uložené posledné udalosti. Po ďalšom štarte sa otvorená aktivita uzavrie na poslednom potvrdenom čase, takže prestoj nevytvorí pracovný čas. Pri chybe zápisu aplikácia zobrazí chybu a prestane hlásiť zaznamenávanie; po odstránení príčiny ju treba reštartovať.

## Údaje

```text
%LOCALAPPDATA%\SolidRecords\activity.db
%LOCALAPPDATA%\SolidRecords\settings.json
```

Údaje zostávajú lokálne; aplikácia ich nikam neposiela. Databáza nie je šifrovaná a nemá automatické mazanie ani limit retencie. Názvy dokumentov a celé dostupné URL vrátane query parametrov sú súčasťou záznamu.

Pred súborovou zálohou aplikáciu ukončite. Databáza sa pri spustení automaticky aktualizuje na aktuálnu schému; po migrácii do nej nezapisujte staršou verziou aplikácie.

## Licencia

SolidRecords je dostupný pod licenciou **GNU General Public License, verzia 3 (GPL-3.0-only)**. Úplné znenie je v súbore [LICENSE](LICENSE), ktorý je pribalený aj k nainštalovanej a prenosnej aplikácii. Aplikácia sa poskytuje bez záruky v rozsahu uvedenom v licencii. Použité súčasti tretích strán si zachovávajú vlastné licencie.

## Pre vývojárov

Vstupným bodom sú [AGENTS.md](AGENTS.md), [architektúra](doc/architecture.md), [pravidlá záznamu](doc/recording.md), [úložisko a diagnostika](doc/storage.md) a [vývojový postup](doc/development.md).

## Publikovanie

Prenosná publikácia s pribaleným .NET runtime pre Windows x64:

```powershell
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Spúšťa sa `artifacts/publish/win-x64/SolidRecords.App.exe`. Inštalátor nie je potrebný. Automatické spúšťanie sa zapína až z cieľového priečinka.
