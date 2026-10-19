# Changelog

Novinky a zmeny v aplikácii SolidRecords pre používateľov.
Formát vychádza z [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).

## Unreleased

## [1.3.0] - 2026-10-19

### Added

- Voliteľné globálne klávesové skratky na otvorenie/skrytie okna a spustenie/zastavenie zaznamenávania sa nastavujú na samostatnej záložke Skratky. Predvolene nie je nastavená žiadna.

- Zoznam aktivít obsahuje časové oddeľovače aktivácie a deaktivácie relácie s typom zachytenej udalosti, napríklad zamknutie, odomknutie, odhlásenie alebo vypnutie počítača. Čas oddeľovača je zarovnaný pod stĺpcom Začiatok.

- Na konci inštalácie je predvolene zapnutá voľba na okamžité spustenie SolidRecords; pred dokončením ju môžete vypnúť.

### Fixed

- Zamknutá alebo odpojená relácia sa pri priebežnej kontrole plochy už nesprávne neoznačí za aktívnu.

- Spustenie alebo obnovenie zaznamenávania sa už nezobrazuje ako aktivácia relácie; nadbytočné oddeľovače zmiznú aj zo staršej histórie.

- Aktivity zostávajú čitateľné aj v tmavej téme a šípky hore/dole preskakujú oddeľovače relácie pri výbere ďalšej aktivity.

- Po úspešnej aktualizácii spustenej z aplikácie sa SolidRecords automaticky znovu spustí a pokračuje podľa uložených nastavení.

## [1.2.0] - 2026-10-19

### Added

- Prepínač Detaily pri vyhľadávaní otvorí pravý panel so stromom aplikácie, jej procesov a všetkých ich aktivít za vybraný deň. Panel sleduje vybranú aktivitu v zozname aj pri vyhľadávaní.

- Samostatná karta Aktualizácie v nastaveniach s tlačidlom „Aktualizovať teraz“ na ručnú kontrolu, stiahnutie a inštaláciu novej verzie aj pri vypnutej dennej kontrole.

### Changed

- Textové polia v nastaveniach majú jednotný vzhľad so zvyškom aplikácie, väčšie vnútorné okraje a rovnakú výšku ako rozbaľovacie polia.

### Fixed

- Rovnaký proces sa v detailoch aplikácie nezobrazuje viackrát po pauze alebo reštarte zaznamenávania, ak je dostupný čas jeho vytvorenia. Oprava spojí aj staršie pozorovania a zachová aktivity aj prestávky.

## [1.1.0] - 2026-10-18

### Added

- Automatické mazanie starších záznamov po nastavenom počte dní. Predvolená voľba „Nikdy“ uchováva históriu bez časového obmedzenia.
- Nastavenie pracovných hodín a dní (predvolene 7:00–15:00, pondelok až piatok) s voliteľným automatickým záznamom iba v pracovnom čase. Časová os zahŕňa celý pracovný rozsah aj bez aktivít a pri skoršej či neskoršej práci sa rozšíri. Predvolené zaznamenávanie zostáva bez časového obmedzenia.
- Denná kontrola nových verzií s Windows notifikáciou: stiahnutie a tichá inštalácia po potvrdení oprávnenia správcu, pripomenutie o 4 hodiny alebo ignorovanie konkrétnej verzie. Kontrolu možno vypnúť v nastaveniach.

### Changed

- Nastavenia sú rozdelené do záložiek Všeobecné, Zaznamenávanie a Pracovný čas, aby bolo okno menšie a prehľadnejšie.

## [1.0.0] - 2026-10-16

### Added

- Okno Nastavenia z menu ikony pri hodinách: minimum aktivity 0–600 sekúnd, automatický záznam, spustenie po prihlásení, štart v systray a systémová, svetlá alebo tmavá téma. Voľby sa ukladajú spolu s históriou; automatický záznam a spustenie po prihlásení sú predvolene zapnuté.
- Ikony aplikácií vedľa ich názvov v zozname záznamov. Získané ikony zostávajú dostupné aj po zatvorení sledovaných aplikácií.

- Záznam používaných aplikácií, názvov otvorených dokumentov a času stráveného v jednotlivých oknách. Rozhranie je v slovenčine a údaje zostávajú iba vo vašom počítači, nikam sa neposielajú.
- Denný prehľad s farebnou časovou osou, celkovým časom v aktívnych oknách a časom prvej a poslednej aktivity. Prehľad sa priebežne aktualizuje a umožňuje prezerať aj predchádzajúce dni.
- Vyhľadávanie záznamov podľa aplikácie, názvu dokumentu alebo webovej adresy. Denný súčet a časová os aj pri vyhľadávaní zobrazujú celý deň.
- Zaznamenávanie navštívených webových adries v prehliadačoch Chrome, Edge, Firefox, Brave, Opera a Vivaldi, ak ich prehliadač sprístupní. Ak adresu nemožno zistiť, zostane názov okna. Text počas písania do adresného riadka sa neukladá.
- Zaznamenávanie pokračuje aj po zatvorení hlavného okna. Aplikáciu môžete znova otvoriť cez ikonu pri hodinách a záznam spustiť alebo zastaviť v okne aj cez túto ikonu. Automatický záznam pri ďalšom spustení sa volí samostatne v nastaveniach.
- Voliteľné automatické spustenie po prihlásení do Windows. Ak aplikáciu spustíte znova, otvorí sa okno už bežiacej aplikácie.
- Do celkového času sa počíta iba čas v aktívnom okne, nie čas aplikácií na pozadí. Zamknutie počítača, uspatie alebo odpojenie vzdialenej plochy zaznamenávanie aktivity prerušia. Pohyb myši ani písanie na klávesnici sa na rozpoznávanie nečinnosti zatiaľ nepoužívajú.
- Krátke prepnutia do iného okna kratšie než nastavené minimum (predvolene 15 sekúnd) sa do prehľadu neukladajú. Po ich vynechaní sa susedné záznamy tej istej aktivity spoja, ak je medzi nimi prestávka kratšia než 15 sekúnd. Prestávka sa do celkového času nezapočíta a zostane viditeľná na časovej osi.
- Systémové a pomocné okná, napríklad ponuka Štart či prepínač okien, sa do záznamu práce nezahŕňajú.
- Kompaktné okno so svetlým a tmavým vzhľadom, ktorý sa automaticky prispôsobuje nastaveniu Windows.
- Po páde aplikácie alebo výpadku napájania sa pri ďalšom spustení posledná aktivita ukončí podľa posledného uloženého času. Obdobie, keď aplikácia nebežala, sa tak nepripočíta k práci.
- Aplikácia je dostupná pod licenciou GPL v3. Jej úplné znenie nájdete aj pri aplikácii a v inštalátore.
- Inštalátor a prenosný ZIP pre 64-bitový Windows. Potrebné súčasti sú pribalené, takže netreba samostatne inštalovať .NET. Prenosnú verziu stačí rozbaliť a spustiť.

### Fixed

- Záznamy sa podľa trvania radia podľa skutočného času. Tretí klik na rovnakú hlavičku zruší triedenie a vráti poradie od najnovších záznamov.
