# Pravidlá záznamu a rozhodnutia

## Model a čas

- Hierarchia je `ProcessActivity → WindowActivity → ActivityInterval → ActivitySpan`. List s URL predstavuje návštevu URL; list bez URL aktívne okno. V jednom okamihu sa započíta len jeden list.
- Proces sa založí až po prvom prípustnom foreground okne. `ObservedWindowRegistry` potom enumeruje okná jeho PID a ukončí pozorovaný proces až po zániku posledného evidovaného okna. Nejde o skutočný štart/koniec OS procesu. Čas vytvorenia procesu sa číta len ako identita proti recyklovaniu PID.
- Už prijaté okno zostáva evidované aj skryté, minimalizované či na inej virtuálnej ploche. Nové okná sa evidujú pred uzavretím zaniknutých, aby výmena okien nevytvorila falošný koniec procesu. Okno na pozadí vzniknuté a zatvorené medzi kontrolami môže uniknúť.
- Čas rodičov sa nikdy nesčítava. Zamknutie, odpojenie relácie a uspatie ukončia aktívny list; rodičia môžu zostať otvorení. Pauza a ukončenie záznamu uzavrú aj rodičov, ďalší štart má nové RecordingId.
- `End` otvoreného záznamu je posledný potvrdený čas, nie odhad do budúcnosti. Heartbeat ho aktualizuje každú sekundu. GUI pre otvorený list zobrazuje koniec a trvanie ako `-`, ale súčet a časová os sa aktualizujú.
- Report používa miestne polnoci prevedené do UTC, vrátane letného času. Normalizácia prebehne pred orezaním na deň; inak by platná aktivita cez polnoc mohla vypadnúť ako príliš krátka.

## Minimum a zlúčenie

`ActivityTrackingOptions` obsahuje jeden predvolený prah 15 s a dva samostatné parametre: `MinimumActivityDuration` a `MaximumMergeGap`. Konfigurácia cez GUI ani settings zatiaľ nie je zapojená.

1. Každá samostatná súvislá aktivita musí dosiahnuť minimum. Kandidát kratší než 15 s zostáva len v pamäti zberu; list sa neuloží ani nezobrazí. Interné udalosti a rodičovská metadata tým nie sú filtrované. Pri presne 15 s sa prijme s pôvodným začiatkom. Viac krátkych návštev sa na splnenie minima nesčítava.
2. Po vyradení krátkych kandidátov sa zlučujú chronologicky susedné totožné aktivity s nezápornou medzerou striktne menšou než 15 s. Presne 15 s medzera sa nezlúči. Identita zahŕňa RecordingId, ID rodičovského procesu/okna a celý WindowInfo (HWND, PID, aplikácia, názov, URL, dostupný čas vytvorenia procesu).
3. Zlúčenie zachová prvé ID a skutočné úseky `ActivitySpan`. Trvanie je ich súčet, nie `End - Start`: 20 s + 5 s medzera + 20 s = 40 s. Aj časová os kreslí úseky, takže prestávka zostane viditeľná.

`ActivityLogPolicy.Apply` drží prijaté pozorovania aktuálneho záznamu a publikuje iba zmenené/zrušené normalizované listy. Pri `RecordingStarted` stav vyčistí. Report aplikuje pravidlá aj na staršiu históriu bez hromadného prepisovania zdrojových dát. Doplnenie URL môže dodatočne zmeniť skupinu zlúčenia vrátane jej rozdelenia; úložisko preto podporuje presun úsekov medzi listami.

## Výber okien a URL

`WindowFilter` vynecháva menu, tooltipy, plochu, taskbar, task switcher, prihlasovanie/odomykanie a ďalšie shell procesy/triedy, vnorené okná, prázdne názvy a štýly `WS_EX_TOOLWINDOW`/`WS_EX_NOACTIVATE`. Foreground musí byť viditeľný a nesmie byť cloaked. Explorer nie je zakázaný celý: prípustné sú triedy priečinkov a súborových dialógov. Bežné dialógy a UWP okná zostávajú podporované. Ignorovaný foreground uzavrie pôvodný list a vytvorí prestávku; pomocné okná nepredlžujú životnosť procesu.

URL sa číta zo známeho adresného riadka Chrome, Edge, Firefoxu, Brave, Opery a Vivaldi; obsah webových dokumentov sa neprechádza. Rozpoznávanie používa známe automation ID a označenia EN/SK/CZ/DE. Počas písania do adresného riadka sa obsah neukladá. Podpora závisí od accessibility providera a oprávnení; pri nedostupnej URL zostane názov okna.

Prvá neskôr zistená URL pri rovnakom stále aktívnom okne, procese a názve doplní existujúci list a zachová ID/začiatok. Tým sa predchádza dvojici riadkov bez URL a s URL po prepnutí karty. Zmena medzi dvoma známymi URL vytvorí nový list. Sekundové čítanie negarantuje zachytenie každej navigácie.
