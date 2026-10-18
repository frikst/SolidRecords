# Aktualizácie aplikácie

Kontrola je predvolene zapnutá cez `ApplicationSettings.CheckForUpdates`. `UpdateChecker` v Core serializuje kontrolu a voľby používateľa; `GitHubReleaseClient` používa verejné REST API `GET https://api.github.com/repos/frikst/SolidRecords/releases/latest`, bez GitHub CLI, účtu či tokenu. Posiela User-Agent, verziu API a uložený ETag v `If-None-Match`. Pri 304 ponechá posledné metadáta vrátane MSI; pri 200 nahradí cache aj ETag. Ponúka len stabilné tagy `vX.Y.Z` s vyššou číselnou verziou než bežiaca zostava a presným assetom `SolidRecords-X.Y.Z-win-x64.msi` z rovnakého repozitára. Draft, prerelease, downgrade a vydanie bez použiteľného MSI alebo SHA-256 digestu sa neponúkajú.

`UpdateController` v App spúšťa kontrolu mimo UI vlákna po načítaní nastavení a každú minútu overuje lokálne termíny. Požiadavka na API sa vykoná najviac raz za 24 hodín; termín sa uloží ešte pred sieťovým I/O, takže ho rešpektuje aj reštart alebo chyba siete. Pri 403, 429 a 503 sa rešpektujú aj neskoršie `Retry-After` a `X-RateLimit-Reset`. Chyba kontroly nezastaví záznam. Vypnutie v nastaveniach zastaví ďalšie požiadavky a odstráni ponuku z centra notifikácií; už prebiehajúca požiadavka môže dobehnúť. Zmena nastavení nečaká na odpoveď API ani nepredlžuje prerušenie záznamu pri zmene minima.

ETag šetrí prenos a umožňuje 304 bez opätovného parsovania vydania. GitHub garantuje nezapočítanie podmienených 304 do primárneho limitu pri autentifikovaných požiadavkách; pri verejnej kontrole bez tokenu nemožno garantovať odstránenie limitu zdieľanej IP za NAT. Denný interval a rešpektovanie limitov preto zostávajú potrebné. Pozri [GitHub REST best practices](https://docs.github.com/en/rest/using-the-rest-api/best-practices-for-using-the-rest-api).

## Cache a rozhodnutia používateľa

`UpdateStateStore` v App zapisuje `updates.json` v rovnakom dátovom priečinku ako databázu, atomickým nahradením dočasného súboru. Obsahuje ETag, posledné metadáta vydania, termín ďalšieho pokusu, termín notifikácie a ignorované tagy. Neobsahuje históriu práce. Stav aktualizácií je oddelený od používateľských nastavení, aby súbežné uloženie dialógu neprepísalo cache. Poškodená cache zastaví sieťové kontroly do opravy súboru; neobnovuje sa opakovanými požiadavkami na API. Zmena databázovej schémy nie je potrebná; chýbajúca voľba v pôvodnom JSON nastavení má hodnotu `true`.

`UpdateNotifications` vo Windows vrstve používa Microsoft.Toolkit.Uwp.Notifications 7.1.3 a natívne centrum notifikácií Windows pre aplikáciu bez MSIX identity. Registrácia aktivácie je pre aktuálneho používateľa. Notifikácia ponúka stiahnutie a inštaláciu, pripomenutie o 4 hodiny alebo trvalé ignorovanie konkrétneho tagu. Kliknutie na telo tiež odloží pripomenutie o 4 hodiny; zatvorenie ponechá bežné pripomenutie po 24 hodinách. Novšia verzia sa ponúkne aj po ignorovaní predchádzajúcej. Vypnuté notifikácie alebo režim sústredenia vo Windows môžu zabrániť zobrazeniu banneru.

Aktivácia sa spracuje aj po zatvorení aplikácie: Windows ju spustí a callback počká na dokončenie štartu. Pri súbehu s existujúcou inštanciou sa akcia odošle cez named pipe obmedzenú na aktuálneho používateľa. Obsah správy má pevný limit a akcia sa vždy overí voči aktuálnej cache, verzii a nastaveniu. Kliknutie na starú, ignorovanú či už nainštalovanú verziu nespustí inštalátor.

## Stiahnutie a inštalácia

`UpdateInstaller` vo Windows vrstve sťahuje iba uvedený MSI z HTTPS URL daného vydania do náhodného podpriečinka `updates/` pod dátovým priečinkom. Overí presnú veľkosť, limit 512 MiB a SHA-256 z GitHub API. Čiastočný alebo poškodený súbor odstráni. Celé sťahovanie má limit 15 minút; kontrola API 30 sekúnd. Zber počas sťahovania pokračuje.

Po overení sa spustí skrytý pomocný Windows PowerShell proces s `runas`. Používateľ potvrdí UAC; pri odmietnutí aplikácia pokračuje v zázname. Po potvrdení App zastaví kontrolu, dopíše Recorder a ukončí sa vrátane uvoľnenia DI služieb. Pomocník čaká na koniec pôvodného procesu najviac 120 sekúnd, znovu overí SHA-256 a spustí `msiexec /i ... /quiet /norestart /L*v ...`. Neinštaluje, ak aplikácia v limite neskončí. Úspešný MSI odstráni, protokol ponechá vedľa neho; chybu oznámi dialógom. Aplikácia sa po inštalácii automaticky znovu nespúšťa. Vyššie oprávnenia má iba pomocník a inštalátor.

Prenosná zostava ponúka rovnaké MSI; jeho použitím vznikne inštalácia v Program Files, pôvodný prenosný priečinok sa neaktualizuje. Autostart si ponecháva pôvodnú cestu, preto pri prechode z prenosnej zostavy spustite nainštalovanú aplikáciu a vypnite/zapnite jej autostart. Pri ručnej inštalácii platí postup v [development.md](development.md).

## Overenie

Unit testy používajú falošné HTTP, hodiny a úložisko: denný interval a reštart, ETag/304, limity, stabilné verzie, odloženie, ignorovanie a kompatibilita nastavení. `DesktopChecks --updates-only` používa izolovaný priečinok `artifacts/update-checks/` a falošné MSI bajty; overí súborovú cache, SHA-256, dĺžku, odstránenie chybných sťahovaní tri akcie v skutočnom toast XML a zrušenie/odtečenie sťahovania pri ukončení. Vygeneruje aj zdroj pomocného skriptu pre kontrolu syntaxe; nespúšťa MSI, sieť ani registráciu notifikácií.

V izolovanom Windows prostredí treba ručne overiť skutočný banner a tlačidlá za behu aj po ukončení aplikácie, reštart po odložení/ignorovaní, vypnutie kontroly, odmietnutie UAC a upgrade dvoch skutočných MSI vrátane zachovania histórie. Testovanie inštalácie nesmie použiť živú používateľskú databázu. WPF nastavenia overuje bežný desktop náhľad v oboch témach a mierkach.
