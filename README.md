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
- Životnosť procesu odvodená z okien: od prvého aktívneho okna až po zatvorenie posledného evidovaného okna procesu.
- Aktivovanie a deaktivovanie okna, zmena názvu okna a zmena dostupnej URL.
- Kompozitná evidencia proces → okná → aktívne úseky okna alebo URL. GUI zobrazuje plochý zoznam listových aktivít s PID, názvom okna, URL a časmi. Udalosti sa v GUI nezobrazujú.
- Denná časová os s farbami aplikácií, denný súčet času v aktívnych oknách, prvá a posledná aktivita.
- Výber dňa a pôvodné textové vyhľadávanie podľa aplikácie, dokumentu alebo URL. Okno je živé: po zápise zmien sa aktualizuje zoznam, súhrn aj časová os. Otvorená aktivita má pri konci a trvaní pomlčku; hodnoty sa zobrazia až po jej ukončení. Denný súčet a časová os sa priebežne aktualizujú aj počas aktivity. Zmeny upravujú existujúce riadky bez resetovania zoznamu. Tlačidlo na manuálne obnovenie nie je potrebné. Ďalšie filtre zatiaľ nie sú pridané.
- Žiadny skupinový pohľad, zaškrtávacie políčka ani pridávanie, zlučovanie a mazanie položiek timetrackingu.

Súčet sa počíta výhradne z listových aktivít: v každom okamihu sa započíta aktívne okno alebo jeho aktívna URL, nikdy oba súčasne. Životnosť procesu a otvoreného okna sa nesčítava. Aplikácia na pozadí, minimalizované okno a čas medzi opakovanými aktiváciami nepridávajú pracovný čas. Nie je to detekcia nečinnosti podľa klávesnice a myši. Zamknutie, odpojenie a uspatie aktívny úsek uzatvoria; rodičovské záznamy môžu zostať otvorené. Intervaly sa v dennom prehľade orežú na miestnu polnoc vrátane prechodov letného času. Vyhľadávanie filtruje zoznam; denný súčet a časová os zobrazujú celý deň.

## Zber vo Windows a jeho hranice

`SetWinEventHook` sleduje zmenu okna v popredí a názvov okien. Skrytá natívna správa cez vlastný STA message loop prijíma `WM_WTSSESSION_CHANGE` a správy o napájaní. Raz za sekundu sa stav overí aj kontrolným čítaním. Zber funguje aj pri zatvorenom hlavnom okne.

Pred zaznamenaním okna sa kontroluje jeho natívna trieda a štýl. Kontextové menu, tooltipy, taskbar, plocha, prepínač úloh, prihlasovacia a zamykacia obrazovka, pomocné okná s `WS_EX_TOOLWINDOW` / `WS_EX_NOACTIVATE`, vnorené okná, okná bez názvu a neviditeľné či cloaked okná sa do aktivít nezaraďujú. Pri procese `explorer` sa pripúšťajú okná priečinkov a bežné dialógy operácií so súbormi; celý proces sa nevylučuje. Štandardné dialógy ako Uložiť ako aj bežné UWP aplikácie zostávajú povolené. Ignorované okno uzatvorí doterajšiu aktívnu položku a vytvorí prestávku, nepridáva svoj čas pôvodnej aplikácii. Pomocné okná sa nezaraďujú ani medzi okná udržiavajúce životnosť procesu. Už evidované pracovné okno zostáva otvorené pri minimalizovaní, skrytí alebo presune na inú virtuálnu plochu.

Procesy sa priamo nemonitorujú: aplikácia nepoužíva WMI, zoznam procesov ani kontrolu ich ukončenia. Keď sa okno prvýkrát dostane do popredia, založí sa rodičovský proces a cez `EnumWindows` sa zistia jeho ďalšie viditeľné okná. Okná tohto procesu sa ďalej overujú raz za sekundu a zánik okna sa zisťuje aj cez `EVENT_OBJECT_DESTROY`. Minimalizované okná zostávajú evidované. Už evidované skryté okno zostáva otvorené až do zániku jeho HWND; nikdy nezobrazené pomocné okná sa nezaraďujú. Po zatvorení posledného evidovaného okna sa ukončí rodičovský proces. Nové okno na pozadí, ktoré vznikne a zanikne medzi kontrolami bez aktivácie, môže uniknúť detekcii.

Ide o pozorovanú životnosť podľa okien, nie skutočný čas štartu a ukončenia OS procesu. Samostatné inštancie sa rozlišujú podľa PID a dostupného času vytvorenia, čítaného iba ako identifikačný údaj procesu aktívneho okna. Pri pauze, ukončení aplikácie alebo obnove po páde sa uzatvorí hranica pozorovania. Ďalšie spustenie záznamu založí nové kompozity. Aplikácia beží bez administrátorských práv a nevyžaduje zmenu systémových oprávnení.

URL sa zisťuje raz za sekundu cez UI Automation adresného riadka v Chrome, Edge, Firefoxe, Brave, Opere a Vivaldi. Dostupnosť závisí od verzie, jazyka a accessibility podpory konkrétneho prehliadača. Číta sa len rozpoznaný adresný riadok; obsah webových dokumentov sa neprechádza. Počas písania do adresného riadka sa jeho obsah neukladá. Rozpoznávanie je pripravené pre bežné anglické, slovenské, české a nemecké označenia a známe automation ID. Pri nedostupnej URL zostáva zaznamenaný názov okna. Ak sa prvá URL zistí neskôr pre stále aktívny záznam s rovnakým názvom, oknom a procesom, doplní sa do existujúceho listu so zachovaním jeho ID a začiatku. Zmena medzi dvoma známymi URL vytvorí nový list. Nejde o garantovanú históriu každej navigácie: veľmi krátka návšteva, rovnaký názov karty alebo prehliadač bez dostupného adresného riadka môžu uniknúť detekcii.

UI Automation má vlastné MTA vlákno, aby pomalý externý provider neblokoval GUI ani natívne notifikácie. Čítanie okien spustených s vyššími oprávneniami môže Windows obmedziť. Aplikácia sleduje reláciu, v ktorej bola spustená; iné používateľské účty nesleduje. Pre jedného používateľa sa povoľuje jedna inštancia naprieč reláciami. Druhé spustenie požiada už bežiacu inštanciu o otvorenie okna.

Udalosti sa zaznamenávajú iba počas behu aplikácie so zapnutým záznamom. Pri spustení sa uloží zistený stav relácie, nie spätne vymyslené prihlásenie. Automatické spúšťanie po prihlásení je potrebné zapnúť pre každodenný zber. Pri vynútenom ukončení Windows alebo výpadku napájania nemusia byť doručené posledné udalosti.

## Údaje

```text
%LOCALAPPDATA%\SolidRecords\activity.db
%LOCALAPPDATA%\SolidRecords\settings.json
```

Údaje zostávajú lokálne; aplikácia ich nikam neposiela. Databáza nie je šifrovaná a nemá automatické mazanie ani limit retencie. Názvy dokumentov a celé dostupné URL vrátane query parametrov sú súčasťou záznamu.

SQLite používa WAL, transakcie, parametrizované príkazy, cudzie kľúče a indexy podľa času. `process_activities` obsahuje rodičovské procesy, `window_activities` ich okná a `activity_leaves` aktívne úseky s historickým názvom okna a voliteľnou URL. Každý list patrí práve jednému oknu a každé okno práve jednému procesu. Nová aktivácia a zmena názvu alebo URL vytvoria nový list pod rovnakým oknom; životnosť rodičov nezvyšuje celkový čas. Interná tabuľka `events` zostáva pre diagnostiku, GUI ju nečíta. Časy sú UTC Unix milisekundy, prezentácia ich prevádza na miestne pásmo.

Verzia schémy je `PRAGMA user_version=2`. Verzia 1 sa automaticky migruje v transakcii: pôvodné intervaly sa prenesú pod procesy a okná so zachovaním ID listov, názvov, URL a trvania. Pôvodná tabuľka `intervals` ostáva zachovaná. Staré dáta nemajú informáciu o skutočnom zániku okien, preto sú ich rodičovské hranice odvodené z historických intervalov. Novšia neznáma verzia sa odmietne otvoriť. Staršiu aplikáciu po migrácii nepoužívajte na zápis do tejto databázy.

Otvorený interval má posledný potvrdený koniec aktualizovaný heartbeat udalosťou každú sekundu. Pri ďalšom štarte sa uzatvorí na tomto čase, takže pád nevytvorí falošnú aktivitu až do nového spustenia. Pri chybe zápisu sa aplikácia prestane hlásiť ako zaznamenávajúca a zobrazí chybu; po odstránení príčiny treba aplikáciu reštartovať. Pre konzistentnú súborovú zálohu najprv aplikáciu ukončite.

## Štruktúra

| Projekt | Zodpovednosť |
| --- | --- |
| `SolidRecords.Core` | Modely, rozhrania, čistý stavový automat, serializácia záznamu a denné reporty. Bez WPF, SQLite a Windows API. |
| `SolidRecords.Storage` | SQLite implementácia úložiska. Závisí len od jadra. |
| `SolidRecords.Windows` | Win32, WTS, evidencia okien, UI Automation a voliteľné spúšťanie po prihlásení. Závisí len od jadra. |
| `SolidRecords.App` | WPF pohľady, MVVM, príkazy, systray a kompozícia služieb. Code-behind rieši len životný cyklus okna. |
| `SolidRecords.Tests` | Testy stavového automatu, SQLite, časových hraníc a riadenia záznamu. |
| `SolidRecords.DesktopChecks` | Voliteľný integračný test živého Windows zberu a vykreslenie WPF náhľadu. |

## Overenie

```powershell
dotnet test tests/SolidRecords.Tests
dotnet run --project tests/SolidRecords.DesktopChecks
```

Voliteľná diagnostika histórie len na čítanie: `dotnet run --project tests/SolidRecords.DesktopChecks -- --inspect-history`. Používa rovnaký SQLite provider ako aplikácia, vykoná kontrolu integrity a vypíše skupiny potenciálne nežiaducich aktivít bez načítania URL. Nevykonáva inicializáciu, migráciu, obnovu ani mazanie údajov.

Integračný test používa izolované databázy v `artifacts/desktop-checks`, krátko zapne zber v aktuálnej relácii a overí, že pomocný proces bez okna nie je zaznamenaný. Overí dopísanie záznamov, kompozitnú evidenciu aj opätovný štart zberu. Vygeneruje `main-window.png` s ukážkovými dátami a kontroluje WPF binding chyby. Používateľskú databázu nemení. Testy jadra overujú posledné otvorené okno, opakovanú aktiváciu, zmeny URL, viac okien procesu, čas na pozadí, recyklovanie PID a migráciu verzie 1.

Ručné overenie: otvoriť GUI cez systray; prepnúť medzi dvoma oknami; zmeniť dokument a kartu prehliadača; zamknúť a odomknúť počítač; overiť medzeru v časovej osi; zastaviť záznam a overiť, že nepribúdajú udalosti; zavrieť a znovu otvoriť GUI; ukončiť aplikáciu cez systray a overiť ďalší štart. Otestovať aj uspatie, odpojenie RDP a URL na konkrétnych nainštalovaných prehliadačoch.

## Publikovanie

Prenosná publikácia s pribaleným .NET runtime pre Windows x64:

```powershell
dotnet publish src/SolidRecords.App -c Release -r win-x64 --self-contained true -o artifacts/publish/win-x64
```

Spúšťa sa `artifacts/publish/win-x64/SolidRecords.App.exe`. Inštalátor nie je potrebný. Automatické spúšťanie sa zapína až z cieľového priečinka.

Referencie Windows API: [SetWinEventHook](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook), [WM_WTSSESSION_CHANGE](https://learn.microsoft.com/en-us/windows/win32/termserv/wm-wtssession-change), [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/).
