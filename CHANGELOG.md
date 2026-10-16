# Changelog

Významné zmeny projektu SolidRecords sú zaznamenané v tomto súbore.
Formát vychádza z [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/).
Všetky doterajšie funkcie sú zatiaľ nevydané a patria do sekcie `Unreleased`.

## Unreleased

### Added

- Lokálny záznam práce pre Windows v slovenčine, s ukladaním udalostí a aktivít do SQLite bez odosielania údajov na sieť.
- Beh v systémovej lište aj pri zatvorenom hlavnom okne, ovládanie záznamu z okna aj menu lišty a zapamätanie stavu záznamu pri ďalšom spustení.
- Voliteľné spúšťanie po prihlásení a ochrana pred spustením viacerých inštancií; opätovné spustenie otvorí okno bežiacej aplikácie.
- Zber zmien používateľskej relácie vrátane zamknutia, odomknutia, pripojenia a odpojenia RDP, uspania a obnovenia počítača.
- Evidencia procesov a okien s aktívnymi úsekmi, názvami okien a dostupnými URL. Životnosť procesu sa odvodzuje od jeho evidovaných okien.
- Zisťovanie URL z adresného riadka Chrome, Edge, Firefoxu, Brave, Opery a Vivaldi podľa dostupnosti UI Automation; obsah počas písania do adresného riadka sa neukladá.
- Vynechávanie systémových a pomocných okien zo záznamu. Zamknutie, odpojenie relácie a uspatie uzatvoria aktívny úsek.
- Prijímanie aktivít od 15 sekúnd a zlučovanie susedných totožných aktivít s medzerou kratšou než 15 sekúnd. Zlúčené záznamy zachovávajú skutočné aktívne úseky a nezapočítavajú prestávky.
- Živý denný zoznam aktivít s aplikáciou, PID, názvom okna, URL a časmi, s výberom dňa a textovým vyhľadávaním podľa aplikácie, dokumentu alebo URL.
- Denná časová os s farbami aplikácií, súčet aktívneho času a prvá a posledná aktivita. Report započítava iba aktívne úseky a rešpektuje miestnu polnoc aj zmeny letného času.
- Kompaktné WPF rozhranie s vlastnou hlavičkou, ikonou aplikácie a automatickým prepínaním svetlej a tmavej témy podľa Windows, vrátane menu systémovej lišty.
- Automatické migrácie databázy a uzavretie otvorených záznamov po páde na poslednom potvrdenom čase, aby sa prestoj nezapočítal do práce.
- Read-only diagnostika histórie s konzistentnou SQLite zálohou a kontrolou integrity, izolované testy pravidiel a migrácií a WPF náhľady pri 100 % a 150 % mierke.
- Prenosná publikácia pre Windows x64 s pribaleným .NET runtime, bez potreby inštalátora.
