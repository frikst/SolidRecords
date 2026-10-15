# SolidRecords

Lokálny záznam práce na počítači pre Windows, postavený na **.NET 10 / WPF**. Aplikácia beží v systémovej lište a ukladá udalosti a časové intervaly do SQLite. Rozhranie je v slovenčine.

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
- Spustenie a ukončenie procesov v relácii zaznamenávajúceho používateľa. Procesy, ktoré bežali už pri začiatku záznamu, majú samostatnú udalosť **Už spustený program**.
- Aktivovanie a deaktivovanie okna, zmena názvu okna a zmena dostupnej URL.
- Denná časová os s farbami aplikácií, denný súčet času v aktívnych oknách, prvá a posledná aktivita, zoznam intervalov a zoznam surových udalostí.
- Výber dňa, vyhľadávanie podľa aplikácie, dokumentu, URL či typu udalosti; automatické obnovenie každých päť sekúnd.
- Žiadny skupinový pohľad, zaškrtávacie políčka ani pridávanie, zlučovanie a mazanie položiek timetrackingu.

Súčet predstavuje čas, keď bolo okno v popredí v aktívnej relácii počas zapnutého záznamu. Nie je to detekcia nečinnosti podľa klávesnice a myši. Zamknutie, odpojenie a uspatie interval uzatvoria. Intervaly sa v dennom prehľade orežú na miestnu polnoc; zohľadňuje sa aj prechod letného času. Vyhľadávanie filtruje tabuľky; denný súčet a časová os naďalej zobrazujú celý deň.

## Zber vo Windows a jeho hranice

`SetWinEventHook` sleduje zmenu okna v popredí a názvov okien. Skrytá natívna správa cez vlastný STA message loop prijíma `WM_WTSSESSION_CHANGE` a správy o napájaní. Raz za sekundu sa stav overí aj kontrolným čítaním. Zber funguje aj pri zatvorenom hlavnom okne.

Procesy sa sledujú cez WMI `Win32_ProcessStartTrace` / `Win32_ProcessStopTrace`. Ak bezpečnostná politika WMI nepovolí, aplikácia použije sekundové porovnávanie procesov a zobrazí upozornenie. V tomto režime môžu chýbať procesy, ktoré sa spustia aj skončia medzi dvoma kontrolami; čas je čas detekcie. Aplikácia nevyžaduje administrátorské spustenie.

Ak hlásenie uvádza **Windows zamietol prístup k WMI udalostiam procesov**, samotná služba WMI môže fungovať správne; odmietnutý je odber týchto udalostí pod aktuálnymi oprávneniami. Zber relácie, okien a dostupných URL pokračuje, náhradný režim sa týka procesov. Pre odber WMI môžete skúsiť aplikáciu spustiť ako správca: najprv ju ukončite cez systray položkou **Ukončiť aplikáciu**, potom kliknite pravým tlačidlom na `SolidRecords.App.exe` a vyberte **Spustiť ako správca**. Existujúca inštancia musí byť ukončená, inak sa iba otvorí jej okno. Bežná voľba spúšťania po prihlásení cez register administrátorské oprávnenia neudeľuje. Aplikácia nemení oprávnenia WMI ani systémovú bezpečnostnú politiku.

URL sa zisťuje raz za sekundu cez UI Automation adresného riadka v Chrome, Edge, Firefoxe, Brave, Opere a Vivaldi. Dostupnosť závisí od verzie, jazyka a accessibility podpory konkrétneho prehliadača. Číta sa len rozpoznaný adresný riadok; obsah webových dokumentov sa neprechádza. Počas písania do adresného riadka sa jeho obsah neukladá. Rozpoznávanie je pripravené pre bežné anglické, slovenské, české a nemecké označenia a známe automation ID. Pri nedostupnej URL zostáva zaznamenaný názov okna. Nejde o garantovanú históriu každej navigácie: veľmi krátka návšteva, rovnaký názov karty alebo prehliadač bez dostupného adresného riadka môžu uniknúť detekcii. Zmena názvu a následné zistenie URL môžu vytvoriť dva krátke intervaly.

UI Automation má vlastné MTA vlákno, aby pomalý externý provider neblokoval GUI ani natívne notifikácie. Čítanie okien spustených s vyššími oprávneniami môže Windows obmedziť. Aplikácia sleduje reláciu, v ktorej bola spustená; iné používateľské účty nesleduje. Pre jedného používateľa sa povoľuje jedna inštancia naprieč reláciami. Druhé spustenie požiada už bežiacu inštanciu o otvorenie okna.

Udalosti sa zaznamenávajú iba počas behu aplikácie so zapnutým záznamom. Pri spustení sa uloží zistený stav relácie, nie spätne vymyslené prihlásenie. Automatické spúšťanie po prihlásení je potrebné zapnúť pre každodenný zber. Pri vynútenom ukončení Windows alebo výpadku napájania nemusia byť doručené posledné udalosti.

## Údaje

```text
%LOCALAPPDATA%\SolidRecords\activity.db
%LOCALAPPDATA%\SolidRecords\settings.json
```

Údaje zostávajú lokálne; aplikácia ich nikam neposiela. Databáza nie je šifrovaná a nemá automatické mazanie ani limit retencie. Názvy dokumentov a celé dostupné URL vrátane query parametrov sú súčasťou záznamu.

SQLite používa WAL, transakcie, parametrizované príkazy a indexy podľa času. Tabuľka `events` obsahuje surové udalosti; `intervals` obsahuje odvodené úseky práce v okne. Časy sú UTC Unix milisekundy, prezentácia ich prevádza na miestne pásmo. Udalosti a zmeny intervalov jedného kroku sa ukladajú v jednej transakcii. Verzia schémy je `PRAGMA user_version=1`; novšia neznáma verzia sa odmietne otvoriť.

Otvorený interval má posledný potvrdený koniec aktualizovaný heartbeat udalosťou každých päť sekúnd. Pri ďalšom štarte sa uzatvorí na tomto čase, takže pád nevytvorí falošnú aktivitu až do nového spustenia. Pri chybe zápisu sa aplikácia prestane hlásiť ako zaznamenávajúca a zobrazí chybu; po odstránení príčiny treba aplikáciu reštartovať. Pre konzistentnú súborovú zálohu najprv aplikáciu ukončite.

## Štruktúra

| Projekt | Zodpovednosť |
| --- | --- |
| `SolidRecords.Core` | Modely, rozhrania, čistý stavový automat, serializácia záznamu a denné reporty. Bez WPF, SQLite a Windows API. |
| `SolidRecords.Storage` | SQLite implementácia úložiska. Závisí len od jadra. |
| `SolidRecords.Windows` | Win32, WTS, WMI, UI Automation a voliteľné spúšťanie po prihlásení. Závisí len od jadra. |
| `SolidRecords.App` | WPF pohľady, MVVM, príkazy, systray a kompozícia služieb. Code-behind rieši len životný cyklus okna. |
| `SolidRecords.Tests` | Testy stavového automatu, SQLite, časových hraníc a riadenia záznamu. |
| `SolidRecords.DesktopChecks` | Voliteľný integračný test živého Windows zberu a vykreslenie WPF náhľadu. |

## Overenie

```powershell
dotnet test tests/SolidRecords.Tests
dotnet run --project tests/SolidRecords.DesktopChecks
```

Integračný test používa izolované databázy v `artifacts/desktop-checks`, krátko zapne zber v aktuálnej relácii, spustí a ukončí pomocný proces a overí dopísanie udalostí aj opätovný štart zberu. Vygeneruje `main-window.png` s ukážkovými dátami a kontroluje WPF binding chyby. Používateľskú databázu nemení.

Ručné overenie: otvoriť GUI cez systray; prepnúť medzi dvoma oknami; zmeniť dokument a kartu prehliadača; zamknúť a odomknúť počítač; overiť medzeru v časovej osi; zastaviť záznam a overiť, že nepribúdajú udalosti; zavrieť a znovu otvoriť GUI; ukončiť aplikáciu cez systray a overiť ďalší štart. Otestovať aj uspatie, odpojenie RDP a URL na konkrétnych nainštalovaných prehliadačoch.

## Publikovanie

Prenosná publikácia s pribaleným .NET runtime pre Windows x64:

```powershell
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Spúšťa sa `artifacts/publish/win-x64/SolidRecords.App.exe`. Inštalátor nie je potrebný. Automatické spúšťanie sa zapína až z cieľového priečinka.

Referencie Windows API: [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook), [WM_WTSSESSION_CHANGE](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change), [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/).
