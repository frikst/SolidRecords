# Changelog

Novinky a zmeny v aplikácii SolidRecords pre používateľov.
Formát vychádza z [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).

## Unreleased

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
