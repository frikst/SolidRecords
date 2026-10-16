# Changelog

Novinky a zmeny v aplikácii SolidRecords pre používateľov.
Formát vychádza z [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).
Všetky doterajšie funkcie sú zatiaľ nevydané a patria do sekcie `Unreleased`.

## Unreleased

### Added

- Záznam používaných aplikácií, názvov otvorených dokumentov a času stráveného v jednotlivých oknách. Rozhranie je v slovenčine a údaje zostávajú iba vo vašom počítači, nikam sa neposielajú.
- Denný prehľad s farebnou časovou osou, celkovým časom v aktívnych oknách a časom prvej a poslednej aktivity. Prehľad sa priebežne aktualizuje a umožňuje prezerať aj predchádzajúce dni.
- Vyhľadávanie záznamov podľa aplikácie, názvu dokumentu alebo webovej adresy. Denný súčet a časová os aj pri vyhľadávaní zobrazujú celý deň.
- Zaznamenávanie navštívených webových adries v prehliadačoch Chrome, Edge, Firefox, Brave, Opera a Vivaldi, ak ich prehliadač sprístupní. Ak adresu nemožno zistiť, zostane názov okna. Text počas písania do adresného riadka sa neukladá.
- Zaznamenávanie pokračuje aj po zatvorení hlavného okna. Aplikáciu môžete znova otvoriť cez ikonu pri hodinách a záznam spustiť alebo zastaviť v okne aj cez túto ikonu. Zvolený stav si pamätá aj pri ďalšom spustení.
- Voliteľné automatické spustenie po prihlásení do Windows. Ak aplikáciu spustíte znova, otvorí sa okno už bežiacej aplikácie.
- Do celkového času sa počíta iba čas v aktívnom okne, nie čas aplikácií na pozadí. Zamknutie počítača, uspatie alebo odpojenie vzdialenej plochy zaznamenávanie aktivity prerušia. Pohyb myši ani písanie na klávesnici sa na rozpoznávanie nečinnosti zatiaľ nepoužívajú.
- Krátke prepnutia do iného okna kratšie než 15 sekúnd sa do prehľadu neukladajú. Po ich vynechaní sa susedné záznamy tej istej aktivity spoja, ak je medzi nimi prestávka kratšia než 15 sekúnd. Prestávka sa do celkového času nezapočíta a zostane viditeľná na časovej osi.
- Systémové a pomocné okná, napríklad ponuka Štart či prepínač okien, sa do záznamu práce nezahŕňajú.
- Kompaktné okno so svetlým a tmavým vzhľadom, ktorý sa automaticky prispôsobuje nastaveniu Windows.
- Po páde aplikácie alebo výpadku napájania sa pri ďalšom spustení posledná aktivita ukončí podľa posledného uloženého času. Obdobie, keď aplikácia nebežala, sa tak nepripočíta k práci.
- Inštalátor a prenosný ZIP pre 64-bitový Windows. Potrebné súčasti sú pribalené, takže netreba samostatne inštalovať .NET. Prenosnú verziu stačí rozbaliť a spustiť.
