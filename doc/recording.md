# Pravidlá záznamu a rozhodnutia

## Model a čas

- Hierarchia je `ProcessActivity → WindowActivity → ActivityInterval → ActivitySpan`. List s URL predstavuje návštevu URL; list bez URL aktívne okno. V jednom okamihu sa započíta len jeden list.
- Proces sa založí až po prvom prípustnom foreground okne. Jeho identitu tvorí PID a dostupný čas vytvorenia OS procesu; Recorder pred prvým pozorovaním v novom zbere vyhľadá uložený proces a obnoví jeho pôvodné ID. Rovnaký proces sa opätovne používa aj po pauze, reštarte aplikácie alebo zatvorení a opätovnom otvorení posledného okna. Bez času vytvorenia sa oddelené pozorovania nespájajú, pretože PID môže Windows recyklovať.
- `ObservedWindowRegistry` enumeruje okná prijatého PID a ukončí pozorovanie po zániku posledného evidovaného okna. `ProcessActivity.Start/End` ohraničujú pozorovania a `IsOpen` vyjadruje aktuálne pozorovanie, nie skutočnú životnosť OS procesu. Obnovenie pozorovania nevytvára aktivitu za prestávku.
- Už prijaté okno zostáva evidované aj skryté, minimalizované či na inej virtuálnej ploche. Nové okná sa evidujú pred uzavretím zaniknutých, aby výmena okien nevytvorila falošný koniec procesu. Okno na pozadí vzniknuté a zatvorené medzi kontrolami môže uniknúť.
- Čas rodičov sa nikdy nesčítava. Zamknutie, odpojenie relácie a uspatie ukončia aktívny list; rodičia môžu zostať otvorení. Pauza a ukončenie záznamu uzavrú aj pozorovanie rodičov, ďalší štart má nové RecordingId na aktivitách a udalostiach. Identita procesu je od RecordingId nezávislá; okná nového zberu sú nové pozorovania aj pri rovnakom HWND, ktorý môže Windows recyklovať.
- `End` otvoreného záznamu je posledný potvrdený čas, nie odhad do budúcnosti. Heartbeat ho aktualizuje každú sekundu. GUI pre otvorený list zobrazuje koniec a trvanie ako `-`, ale súčet a časová os sa aktualizujú.
- Report používa miestne polnoci prevedené do UTC, vrátane letného času. Normalizácia prebehne pred orezaním na deň; inak by platná aktivita cez polnoc mohla vypadnúť ako príliš krátka.

## Udalosti relácie v zozname

Uložené `SessionActive` a `SessionInactive` sa zobrazujú ako oddeľovače s časom a dôvodom: prihlásenie/odhlásenie, odomknutie/zamknutie, pripojenie/odpojenie relácie alebo uspatie/prebudenie. `WM_QUERYENDSESSION` rozlišuje odhlásenie a vypnutie; staré `SessionEnding` zostáva označené spoločným textom. Pri neznámom dôvode sa zobrazuje všeobecná zmena stavu. `RecordingBaseline` označuje iba počiatočný stav zberu. Report ho nezobrazuje ako oddeľovač ani podľa neho nesprístupňuje deň v kalendári, a to aj v staršej histórii. V úložisku zostáva pre diagnostiku; spustenie alebo obnovenie záznamu nie je aktivácia relácie. Presný čas studeného štartu PC sa nezbiera a udalosti z obdobia bez záznamu sa spätne nedopĺňajú. Oddeľovače nemenia skutočné aktívne úseky ani pracovné trvanie. Explicitné zamknutie, odpojenie a odhlásenie majú prednosť pred priebežnou kontrolou plochy; opačná WTS udalosť uvoľňuje iba príslušnú prekážku. Uspatie ani opätovné pripojenie nerušia zamknutie. Kontrola plochy iba číta `UOI_IO` plochy vlákna a stav pripojenia WTS; plochu neprepína. Pri zrušenom vypnutí sa prekážka ukončenia relácie zruší. Staršie nesprávne zachytené prechody sa automaticky neprepisujú.

## Pracovný rozvrh a automatika

`ApplicationSettings` definuje miestny začiatok/koniec v minútach od polnoci (predvolene 420–900), masku `WorkDays` (pondelok až piatok) a `RecordOnlyDuringWorkHours` (false). Začiatok musí byť pred koncom v rovnaký deň; 1440 je prípustné len ako koniec. Prázdna maska znamená žiadny pracovný čas. `WorkSchedule` pre vybraný deň vytvára rozsah s miestnymi offsetmi. Začiatok je zahrnutý, koniec vylúčený. Pri prechode letného času sa chýbajúci čas posunie na prvú platnú minútu; pri dvojznačnom čase sa použije štandardný offset. Ak tým rozsah zanikne, deň nemá pracovný čas.

`RecordingAutomation` rozhoduje o automatických prechodoch. `RecordOnLaunch=false` zachová ručné spúšťanie; true bez obmedzenia spustí záznam pri štarte. Pri obmedzení ho spustí iba počas pracovného času a prepína na hraniciach rozvrhu. App kontroluje stav sekundovým timerom nezávisle od viditeľnosti okna, pod spoločným zámkom s ukladaním nastavení a ukončením. Po oneskorení alebo prebudení použije aktuálny čas, nevytvára spätné aktivity. Ručné prepnutie medzi hranicami nemení nastavenia a zostáva platné do ďalšieho automatického prechodu; zmena pracovného režimu alebo rozvrhu sa použije hneď po uložení. Zmena samotnej preferencie štartu medzi ručným a neobmedzeným režimom nemení aktuálny stav.

`TimelineRange` spája rozsah skutočných aktivít s pracovným časom vybraného dňa nezávisle od režimu záznamu. Pracovný deň bez aktivít zobrazuje pracovný rozsah; bez rozvrhu zostáva pôvodný náhradný rozsah 06:00–18:00. Aktivity zachovávajú zaokrúhlenie rozsahu na hodiny a minimálnu šírku dve hodiny. Rozvrh nepridáva aktivitu ani pracovné trvanie a nefiltruje históriu.

## Minimum a zlúčenie

`ActivityTrackingOptions` obsahuje jeden predvolený prah 15 s a dva samostatné parametre: `MinimumActivityDuration` a `MaximumMergeGap`. Minimum je nastaviteľné v rozsahu 0–600 s (0 vypne minimum); zlúčenie zostáva 15 s. Recorder a report používajú spoločné nastavenia, jeden report si vytvorí nemenný snapshot prahov.

1. Každá samostatná súvislá aktivita musí dosiahnuť minimum. Kandidát kratší než nastavené minimum (predvolene 15 s) zostáva len v pamäti zberu; list sa neuloží ani nezobrazí. Interné udalosti a rodičovská metadata tým nie sú filtrované. Pri presnom dosiahnutí minima sa prijme s pôvodným začiatkom. Viac krátkych návštev sa na splnenie minima nesčítava.
2. Po vyradení krátkych kandidátov sa zlučujú chronologicky susedné totožné aktivity s nezápornou medzerou striktne menšou než 15 s. Presne 15 s medzera sa nezlúči. Identita zahŕňa RecordingId, ID rodičovského procesu/okna a celý WindowInfo (HWND, PID, aplikácia, názov, URL, dostupný čas vytvorenia procesu).
3. Zlúčenie zachová prvé ID a skutočné úseky `ActivitySpan`. Trvanie je ich súčet, nie `End - Start`: 20 s + 5 s medzera + 20 s = 40 s. Aj časová os kreslí úseky, takže prestávka zostane viditeľná.

`ActivityLogPolicy.Apply` drží prijaté pozorovania aktuálneho záznamu a publikuje iba zmenené/zrušené normalizované listy. Pri `RecordingStarted` stav vyčistí. Report aplikuje pravidlá aj na staršiu históriu bez hromadného prepisovania zdrojových dát. Doplnenie URL môže dodatočne zmeniť skupinu zlúčenia vrátane jej rozdelenia; úložisko preto podporuje presun úsekov medzi listami.

## Výber okien a URL

`WindowFilter` vynecháva menu, tooltipy, plochu, taskbar, task switcher, prihlasovanie/odomykanie a ďalšie shell procesy/triedy, vnorené okná, prázdne názvy a štýly `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE`. Foreground musí byť viditeľný a nesmie byť cloaked. Explorer nie je zakázaný celý: prípustné sú triedy priečinkov a súborových dialógov. Bežné dialógy a UWP okná zostávajú podporované. Ignorovaný foreground uzavrie pôvodný list a vytvorí prestávku; pomocné okná nepredlžujú životnosť procesu.

URL sa číta zo známeho adresného riadka Chrome, Edge, Firefoxu, Brave, Opery a Vivaldi; obsah webových dokumentov sa neprechádza. Rozpoznávanie používa známe automation ID a označenia EN/SK/CZ/DE. Počas písania do adresného riadka sa obsah neukladá. Podpora závisí od accessibility providera a oprávnení; pri nedostupnej URL zostane názov okna.

Prvá neskôr zistená URL pri rovnakom stále aktívnom okne, procese a názve doplní existujúci list a zachová ID/začiatok. Tým sa predchádza dvojici riadkov bez URL a s URL po prepnutí karty. Zmena medzi dvoma známymi URL vytvorí nový list. Sekundové čítanie negarantuje zachytenie každej navigácie.
