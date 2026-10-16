# Pokyny pre vývoj SolidRecords

SolidRecords je lokálny Windows tracker (.NET 10, WPF, SQLite). Rozhranie a dokumentácia sú v slovenčine, identifikátory v kóde v angličtine. Aktuálnu implementáciu overuj v zdrojovom kóde; dokumentácia nevymedzuje nové funkcie.

## Kde začať

- [Architektúra a mapa kódu](doc/architecture.md).
- [Pravidlá záznamu a návrhové rozhodnutia](doc/recording.md).
- [Úložisko, migrácie a diagnostika](doc/storage.md).
- [Zostavenie, overenie a aktuálne hranice](doc/development.md).
- [README](README.md) je určené používateľovi aplikácie.

## Konvencie a hranice

- Striktne oddeľuj prezentáciu a biznis logiku. Core nesmie závisieť od WPF, Windows API ani SQLite. Storage a Windows závisia len od Core; App skladá služby.
- Biznis pravidlá patria do Core, natívny zber do Windows, SQL a migrácie do Storage, bindingy a formátovanie do App/Presentation. Code-behind rieši životný cyklus a veľkosť okna.
- Zachovaj existujúci štýl: file-scoped namespaces, records pre dátové modely, nullable referencie, async metódy s príponou Async, uvoľňovanie natívnych zdrojov a odhlasovanie udalostí.
- Rešpektuj `.editorconfig`: UTF-8, CRLF, finálny nový riadok, 4 medzery; XAML, csproj, props, slnx a JSON majú 2 medzery. `Directory.Build.props` zapína C# 14, nullable a warnings-as-errors.
- Prahy aktivity definuj cez `ActivityTrackingOptions`; Recorder a report musia dostať rovnaké nastavenie. Časové pravidlá neopakuj v GUI ani SQL.
- Nezapočítavaj životnosť rodičov ani prestávky do pracovného času. Pri zmene normalizácie zachovaj skutočné `ActivitySpan` a stabilitu ID.
- Zber musí fungovať bez otvoreného GUI a bez administrátorských práv. Procesy sa nesledujú cez WMI ani priamym sledovaním ich ukončenia.
- GUI musí zostať kompaktné aj pri 150 % DPI. Živý zoznam aktualizuj podľa ID bez resetovania kolekcie; blokujúce SQLite a UI Automation volania nepatria na UI vlákno.

## Pracovný postup

- Pred zmenou prečítaj príslušný dokument a implementáciu. Pri zmene správania, schémy alebo postupov aktualizuj aj príslušný dokument; nekopíruj rovnaký popis na viac miest.
- Pri významnej zmene funkcií, správania, oprave chyby alebo zmene dôležitej pre používateľa aktualizuj aj sekciu `Unreleased` v [CHANGELOG.md](CHANGELOG.md). Nové zmeny zostávajú v `Unreleased`, kým sa nepripravuje vydanie. Pravidlá obsahu a formátu sú v sekcii „Písanie changelogu“ nižšie.
- Overuj podľa rozsahu zmeny postupmi v `doc/development.md`. Regresie časových pravidiel a migrácií pokrývaj izolovanými testami; zmenu vzhľadu over aj WPF náhľadom a pri 150 % DPI.
- Testy a náhľady používajú izolované databázy v `artifacts/`. Pri diagnostike používateľskej histórie nepoužívaj `SqliteActivityStore.InitializeAsync`: migruje schému a uzatvára otvorené záznamy.
- Používateľskú databázu neopravuj, nemaž ani nemigruj ako vedľajší efekt kontroly. Použi read-only diagnostiku a konzistentný SQLite backup; samotná kópia živého `.db` nemusí obsahovať WAL.
- Nepridávaj do repozitára databázy, URL z používateľskej histórie, screenshoty histórie ani zostavy. `artifacts/`, `bin/`, `obj/` a SQLite súbory sú ignorované.

## Písanie changelogu

- Changelog je pre bežného používateľa bez technických znalostí. Píš stručne po slovensky: čo teraz môže urobiť, čo sa mu zobrazuje inak alebo aký problém už nenastáva. Každá položka má byť zrozumiteľná bez znalosti kódu a vývojového postupu.
- Opisuj výsledok v aplikácii, nie spôsob implementácie. Namiesto „migrácia SQLite a uzavretie otvorených záznamov“ napíš napríklad „Po páde aplikácie sa čas, keď nebežala, nepripočíta k práci“. Technický názov uveď iba vtedy, ak používateľovi pomáha pri používaní alebo inštalácii, napríklad názov prehliadača či podporovanú verziu Windows.
- Interné úpravy kódu, testy, diagnostické nástroje, pokyny pre agentov a automatizáciu zostavenia do changelogu nezapisuj, pokiaľ nemajú priamy dopad na používateľa. Ak ho majú, opíš tento dopad; technické podrobnosti patria do vývojovej dokumentácie.
- Zachovaj podstatné podmienky a obmedzenia, napríklad časové hranice alebo dostupnosť webových adries. Nesľubuj viac, než aplikácia skutočne robí. Súvisiace zmeny zhrň do jednej prehľadnej položky a rovnakú novinku neopakuj.
- Dodržuj [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/): používaj relevantné kategórie `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed` a `Security`. Nepridávaj prázdne kategórie ani mechanický výpis commitov. Už vydané verzie, dátumy a zaradenie zmien zachovaj; jazyková úprava nie je nové vydanie.
