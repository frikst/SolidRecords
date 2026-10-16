# Písanie changelogu

Pri významnej zmene funkcií, správania, oprave chyby alebo zmene dôležitej pre používateľa aktualizuj aj sekciu `Unreleased` v [CHANGELOG.md](../CHANGELOG.md). Nové zmeny zostávajú v `Unreleased`, kým sa nepripravuje vydanie.

- Changelog je pre bežného používateľa bez technických znalostí. Píš stručne po slovensky: čo teraz môže urobiť, čo sa mu zobrazuje inak alebo aký problém už nenastáva. Každá položka má byť zrozumiteľná bez znalosti kódu a vývojového postupu.
- Opisuj výsledok v aplikácii, nie spôsob implementácie. Namiesto „migrácia SQLite a uzavretie otvorených záznamov“ napíš napríklad „Po páde aplikácie sa čas, keď nebežala, nepripočíta k práci“. Technický názov uveď iba vtedy, ak používateľovi pomáha pri používaní alebo inštalácii, napríklad názov prehliadača či podporovanú verziu Windows.
- Interné úpravy kódu, testy, diagnostické nástroje, pokyny pre agentov a automatizáciu zostavenia do changelogu nezapisuj, pokiaľ nemajú priamy dopad na používateľa. Ak ho majú, opíš tento dopad; technické podrobnosti patria do vývojovej dokumentácie.
- Zachovaj podstatné podmienky a obmedzenia, napríklad časové hranice alebo dostupnosť webových adries. Nesľubuj viac, než aplikácia skutočne robí. Súvisiace zmeny zhrň do jednej prehľadnej položky a rovnakú novinku neopakuj.
- Dodržuj [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/): používaj relevantné kategórie `Added`, `Changed`, `Deprecated`, `Removed`, `Fixed` a `Security`. Nepridávaj prázdne kategórie ani mechanický výpis commitov. Už vydané verzie, dátumy a zaradenie zmien zachovaj; jazyková úprava nie je nové vydanie.
