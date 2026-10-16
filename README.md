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

Pri predvolenom nastavení sa aplikácia spustí v systray a začne zaznamenávať automaticky. Prepínač `--show` vždy otvorí prehľad. Dvojklik na ikonu alebo položka **Otvoriť SolidRecords** zobrazí prehľad. Zatvorenie okna ponechá aplikáciu bežať; položka **Ukončiť aplikáciu** zastaví zber, dopíše front udalostí a aplikáciu ukončí.

Záznam sa spúšťa a zastavuje tlačidlom v okne alebo položkou kontextového menu ikony. Stav záznamu nemení nastavenie automatického záznamu pri ďalšom spustení.

Pravým kliknutím na ikonu pri hodinách otvorte **Nastavenia…**. Tlačidlo **Uložiť** uloží všetky voľby do databázy; **Zrušiť** ich zahodí.

| Nastavenie | Predvolená hodnota |
| --- | --- |
| Minimálne trvanie aktivity | 15 sekúnd; rozsah 0–600, pri 0 sa minimum vypne. |
| Automatický záznam pri spustení aplikácie | Zapnutý. |
| Spustenie programu pri prihlásení do Windows | Zapnuté. |
| Spustenie minimalizované v systray | Zapnuté. |
| Téma | Podľa systému; voliteľne svetlá alebo tmavá. |

Téma a minimum sa použijú po uložení. Pri zmene minima sa aktívny záznam dopíše a pokračuje s novým prahom; nové minimum sa použije aj na zobrazenie histórie, ale neobnoví predtým neuložené krátke aktivity. Voľby spúšťania sa prejavia pri ďalšom štarte. Automatické spustenie používa aktuálnu cestu aplikácie v `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, preto prenosnú aplikáciu spúšťajte z finálneho priečinka.

Položka **O aplikácii** v kontextovom menu ikony pri hodinách otvorí dialóg s verziou, autorom, organizáciou a odkazom na web katedry. Dialóg zatvoríte aj klávesom Esc.

## Funkcie

- Aktívna a neaktívna používateľská relácia: prihlásenie, odhlásenie, zamknutie, odomknutie, pripojenie a odpojenie RDP; zaznamenáva sa aj uspatie a obnovenie.
- Životnosť procesu odvodená z okien: od prvého aktívneho okna až po zatvorenie posledného evidovaného okna procesu.
- Aktivovanie a deaktivovanie okna, zmena názvu okna a zmena dostupnej URL.
- Kompozitná evidencia proces → okná → aktívne úseky okna alebo URL. GUI zobrazuje plochý zoznam listových aktivít s PID, názvom okna, URL a časmi. Udalosti sa v GUI nezobrazujú.
- Téma podľa systému sleduje režim aplikácií vo Windows aj počas behu; v nastaveniach možno zvoliť stálu svetlú alebo tmavú tému.
- Denná časová os s farbami aplikácií, denný súčet času v aktívnych oknách, prvá a posledná aktivita.
- Výber dňa a textové vyhľadávanie podľa aplikácie, dokumentu alebo URL. Okno je živé: po zápise zmien sa aktualizuje zoznam, súhrn aj časová os. Otvorená aktivita má pri konci a trvaní pomlčku; hodnoty sa zobrazia až po jej ukončení. Denný súčet a časová os sa priebežne aktualizujú aj počas aktivity. Tlačidlo na manuálne obnovenie nie je potrebné. Ďalšie filtre zatiaľ nie sú pridané.
- Žiadny skupinový pohľad, zaškrtávacie políčka ani pridávanie, zlučovanie a mazanie položiek timetrackingu.

Súčet sa počíta výhradne z listových aktivít: v každom okamihu sa započíta aktívne okno alebo jeho aktívna URL, nikdy oba súčasne. Životnosť procesu a otvoreného okna sa nesčítava. Aplikácia na pozadí, minimalizované okno a čas medzi opakovanými aktiváciami nepridávajú pracovný čas. Nie je to detekcia nečinnosti podľa klávesnice a myši. Zamknutie, odpojenie a uspatie aktívny úsek uzatvoria; rodičovské záznamy môžu zostať otvorené. Intervaly sa v dennom prehľade orežú na miestnu polnoc vrátane prechodov letného času. Vyhľadávanie filtruje zoznam; denný súčet a časová os zobrazujú celý deň.

Aktivita kratšia než nastavené minimum (predvolene **15 sekúnd**) sa do pracovného logu neuloží a nezobrazí. Otvorená aktivita sa objaví až po dosiahnutí tejto hranice, so zachovaním pôvodného začiatku. Každý samostatný aktívny úsek musí hranicu dosiahnuť; viac krátkych návštev sa nesčítava na splnenie minima. Ak po vyradení krátkych aktivít zostanú dve totožné aktivity priamo po sebe a medzera medzi nimi je **menšia než 15 sekúnd**, uložia sa ako jedna aktivita. Presne 15-sekundová aktivita sa prijme, presne 15-sekundová medzera sa nezlúči. Identita zahŕňa záznam procesu a okna, názov dokumentu aj URL; dve rôzne inštancie programu sa nezlúčia.

Zlúčenie zachová skutočné aktívne úseky: 20 s práce + 5 s prestávka + 20 s práce = jeden záznam s trvaním 40 s. Časová os naďalej zobrazuje prestávku. Rovnaké pravidlá sa používajú aj pri zobrazení pôvodnej histórie bez jej hromadného mazania. Minima sa posudzujú pred orezaním na deň, aby sa nestratila platná aktivita zasahujúca cez polnoc. Minimum možno meniť v nastaveniach; prah zlúčenia zostáva 15 sekúnd.

## Hranice zberu

Aplikácia funguje bez administrátorských práv. Sleduje iba reláciu, v ktorej beží, a iba počas zapnutého záznamu. Druhé spustenie otvorí okno už bežiacej inštancie. Systémové a pomocné okná (menu, taskbar, prepínač úloh, zamykacia obrazovka) sa vynechávajú; čas v nich tvorí prestávku.

URL sa zisťuje približne raz za sekundu z adresného riadka Chrome, Edge, Firefoxu, Brave, Opery a Vivaldi. Dostupnosť závisí od verzie, jazyka, accessibility podpory a oprávnení. Pri nedostupnej URL zostane názov okna. Počas písania do adresného riadka sa jeho obsah neukladá. Veľmi krátke návštevy nemusia byť zachytené; nejde o úplnú históriu prehliadača.

Pri vynútenom ukončení alebo výpadku napájania nemusia byť uložené posledné udalosti. Po ďalšom štarte sa otvorená aktivita uzavrie na poslednom potvrdenom čase, takže prestoj nevytvorí pracovný čas. Pri chybe zápisu aplikácia zobrazí chybu a prestane hlásiť zaznamenávanie; po odstránení príčiny ju treba reštartovať.

## Údaje

```text
%LOCALAPPDATA%\SolidRecords\activity.db
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

Spúšťa sa `artifacts/publish/win-x64/SolidRecords.App.exe`. Inštalátor nie je potrebný. Pri prvom spustení sa automatické spúšťanie predvolene zapne pre aktuálnu cestu.
