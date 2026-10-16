---
name: solidrecords-release
description: Priprav a vydaj novú verziu SolidRecords podľa Unreleased v CHANGELOG.md, vyber SemVer verziu, zosúlaď verziu zostavenia a vytvor release commit a git tag vX.Y.Z. Použi pri požiadavke na vydanie SolidRecords, nie pri samotnej úprave changelogu alebo vytváraní skillu.
---

# Vydanie SolidRecords

Pracuj v aktuálnom repozitári SolidRecords; cestu nezakóduj napevno. Komunikuj po slovensky. Vyvolanie s požiadavkou na vydanie oprávňuje na úpravu verzie, changelogu, lokálny commit a tag bez ďalšieho potvrdzovania. Samotné čítanie, vysvetľovanie alebo úprava tohto skillu vydanie nespúšťa. Push, GitHub Release a distribúciu artefaktov vykonaj iba na výslovný pokyn používateľa.

## Zisti stav

1. Over koreň repozitára, prečítaj jeho `AGENTS.md`, `doc/development.md`, `CHANGELOG.md`, `Directory.Build.props` a príslušné projektové súbory. Over aktuálnu implementáciu a prípadné novšie pravidlá vydávania. Nevychádzaj z implicitnej SDK verzie ako z už vydanej verzie.
2. Skontroluj `git status --short`, staged aj unstaged diff, aktuálnu vetvu, tagy a posledné vydania v changelogu. Urči poslednú vydanú verziu numericky podľa SemVer, nie lexikograficky ani podľa dátumu vytvorenia tagu. Pri údržbe staršej vetvy použi jej vydávaciu líniu. Rozpor medzi tagmi, changelogom a explicitnou verziou vyrieš z histórie; ak zostáva nejednoznačný, vyžiadaj chýbajúcu informáciu.
3. Pred vydaním musia byť zmeny, ktoré sa vydávajú, commitnuté. Už existujúce rozpracované alebo staged zmeny nezahrň automaticky do release commitu. Ak používateľ už nariadil ich commitnutie, urob samostatný obsahový commit; inak pri zmenách ovplyvňujúcich vydanie vyžiadaj rozhodnutie. Nesúvisiace zmeny zachovaj a nevkladaj do commitu. Nepoužívaj reset, automatický stash ani `git add .`.
4. Prázdne alebo chýbajúce `Unreleased` nie je nové vydanie. Bez skutočných nevydaných položiek nevytváraj verziu, commit ani tag.

## Vyber verziu

Rešpektuj používateľom určené číslo alebo major/minor/patch. Číslo over podľa [SemVer 2.0.0](https://semver.org/spec/v2.0.0.html): bez úvodných núl, vyššie než základná vydaná verzia a bez kolízie s vydaním alebo tagom. Nepridávaj prerelease ani build metadata bez požiadavky. Neplatnú alebo už použitú verziu svojvoľne nenahrádzaj.

Ak používateľ neurčil verziu, rozhodni samostatne podľa celého obsahu `Unreleased`; pri nejasnom dopade over príslušný kód a históriu. Zvoľ najvyšší potrebný prírastok:

| Dopad zmien | Prírastok |
| --- | --- |
| Nekompatibilná zmena verejného kontraktu alebo odstránenie podporovanej funkcie | major: X+1.0.0 |
| Nová spätne kompatibilná funkcia alebo označenie verejnej funkcie ako deprecated | minor: X.Y+1.0 |
| Spätne kompatibilné opravy, bezpečnostné opravy bez zmeny kontraktu, drobné zlepšenia alebo údržba | patch: X.Y.Z+1 |

Pre desktop aplikáciu posudzuj používateľské funkcie, podporované spúšťacie voľby, konfiguráciu a kompatibilitu uložených dát. Interný refaktoring, zmena vzhľadu ani automatická migrácia zachovávajúca dáta samy osebe neznamenajú major. Kategórie `Changed`, `Removed` a `Security` neposudzuj mechanicky; rozhoduje dopad. Pri kombinácii opráv a nových funkcií vyhráva minor, pri nekompatibilite major.

SemVer pri 0.y.z negarantuje stabilné API. Pokiaľ projekt neurčuje inak, používaj pre túto vývojovú líniu patch na opravy a minor na funkcie aj nekompatibilné zmeny; prechod na 1.0.0 rob pri vedomom vyhlásení stabilného kontraktu alebo podľa používateľa. Ak ešte nie je žiadne vydanie ani explicitný zámer stabilnej 1.0.0, prvé vydanie je 0.1.0. Toto je predvolená projektová voľba, nie povinné pravidlo SemVer.

Pred úpravami stručne oznám vybrané číslo a konkrétny dôvod; nežiadaj schválenie bežného automatického výberu.

## Priprav vydanie

1. Celý aktuálny obsah `Unreleased` presuň pod novú hlavičku `## [X.Y.Z] - YYYY-MM-DD`. Dátum je aktuálny miestny dátum používateľa. Nad vydaním ponechaj prázdnu sekciu `## Unreleased` bez prázdnych kategórií. Zachovaj slovenské položky, ich kategórie a staršie vydania. Aktualizuj existujúce porovnávacie odkazy, ak ich changelog používa; nevymýšľaj URL. Pri prvom vydaní odstráň alebo uprav vetu tvrdiacu, že všetky funkcie sú zatiaľ nevydané.
2. Zosúlaď skutočný zdroj produktovej verzie s `X.Y.Z`. V súčasnej štruktúre bez explicitnej verzie pridaj `<Version>X.Y.Z</Version>` do existujúcej spoločnej `PropertyGroup` v `Directory.Build.props`. Ak už existuje iný centrálny mechanizmus, uprav ten a nepridávaj konkurenčný zdroj. Over výslednú MSBuild `Version` aplikácie, prípadné override a odvodené assembly/file verzie. Verzie závislostí, .NET SDK a schémy SQLite nemeníš kvôli release číslu.
3. Pri zavedení alebo zmene pravidiel verzovania aktualizuj `doc/development.md`. Dodrž `.editorconfig`, UTF-8, CRLF a finálny nový riadok. Do release poznámok nepridávaj mechanický záznam o vlastnom prečíslovaní.
4. Spusti restore, Release build a jednotkové testy podľa aktuálneho `doc/development.md`. Podľa obsahu vydania doplň desktop náhľad a požadované kontroly vzhľadu pri 150 % DPI; používaj izolované dáta v `artifacts/`. Bežnú aplikáciu nespúšťaj ako test vydania, aby sa nedotkla používateľskej databázy. Pri chybe alebo neuskutočniteľnej povinnej kontrole ponechaj pripravené súbory a oznám prekážku; commit a tag ešte nevytváraj.
5. Over `git diff --check`, výsledný diff, zhodu changelogu a zostavenej verzie a neprítomnosť cieľového tagu. Tag over ešte raz tesne pred commitom. Použi explicitné cesty pri stageovaní a prezri celý `git diff --cached`, aby commit obsahoval iba prípravu vydania.

## Commit a tag

Po úspešných kontrolách vytvor commit `Vydanie X.Y.Z`, potom anotovaný tag:

```powershell
git commit -m "Vydanie X.Y.Z"
git tag -a vX.Y.Z -m "SolidRecords X.Y.Z"
```

V príkazoch nahraď X.Y.Z zvoleným číslom. Tag vytvor iba po úspešnom commite a nasmeruj ho na tento commit. Existujúci tag neprepisuj ani nepresúvaj, nepoužívaj amend ani force. Over, že `git rev-parse HEAD` a `git rev-parse 'vX.Y.Z^{commit}'` ukazujú na rovnaký commit, prezri `git show --stat HEAD` a konečný stav pracovného stromu.

Pri zlyhaní tagovania po úspešnom commite oznám čiastočný výsledok. Pri opakovaní dokonči iba chýbajúci krok po overení pôvodného release commitu, jeho verzie a nezmeneného cieľa; nevytváraj druhý release commit ani ďalšie zvýšenie verzie. Ak už zodpovedajúci commit aj tag existujú, oznám hotové vydanie.

V závere uveď verziu, stručný dôvod jej výberu, hash release commitu, tag a výsledok kontrol. Jasne odlíš lokálne vytvorenie od prípadného pushu. Ak používateľ výslovne požiadal aj o push, odošli iba určenú vetvu a konkrétny release tag, nie všetky lokálne tagy.
