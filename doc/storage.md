# Úložisko a diagnostika

## Súbory a schéma

Používateľské dáta sú v `%LOCALAPPDATA%\SolidRecords`: `activity.db` a `settings.json`. Databáza obsahuje názvy dokumentov a celé dostupné URL vrátane query parametrov. Nie je šifrovaná, bez automatickej retencie; aplikácia dáta neposiela na sieť.

SQLite používa Microsoft.Data.Sqlite, WAL, `synchronous=FULL`, cudzie kľúče, parametrizované SQL, transakcie a časové/rodičovské indexy. `SaveAsync` zapisuje celú dávku v jednej transakcii. Čítanie kompozitov má spoločnú read transakciu, aby heartbeat medzi SELECTmi nevytvoril nekonzistentný report. Časy sú UTC Unix milisekundy; GUID sa ukladajú ako text.

| Tabuľka | Význam |
| --- | --- |
| `application_icons` | Posledná dostupná PNG ikona podľa názvu aplikácie (bez rozlíšenia veľkosti písmen), spoločná aj pre staršie záznamy. |
| `process_activities` | Pozorovaná životnosť procesu a identita v rámci záznamu. |
| `window_activities` | Životnosť okna, FK na proces. |
| `activity_leaves` | Prijatá aktivita, historický názov a voliteľná URL, FK na okno. Začiatok/koniec sú obálka aktívnych úsekov. |
| `activity_spans` | Skutočné aktívne úseky, FK na list s ON DELETE CASCADE. Zdroj pracovného trvania. |
| `events` | Interné udalosti vrátane heartbeat, GUI ich nečíta. |
| `intervals` | Zachovaná pôvodná tabuľka po migrácii verzie 1. |

Pri zmene zlúčenia sa najprv zapíšu rodičia/listy, potom sa upsertnú a priradia úseky a až potom sa odstránia nahradené listy. Zmena tohto poradia môže cez kaskádové mazanie stratiť presunuté úseky.

`ReadProcessesAsync` vracia celé aktivity a úseky zasahujúce do rozsahu; report ich filtruje/normalizuje pred denným orezaním. `ReadIntervalsAsync` už oreže na požadovaný rozsah. Nepoužívaj ho namiesto reportu na posudzovanie minimálnej dĺžky cez polnoc.

## Migrácie a obnova

Aktuálna verzia je `PRAGMA user_version=4`, spravovaná v `SqliteSchema.cs`. Inicializácia je v rámci inštancie store jednorazová a zamknutá semaforom.

- Verzia 1: intervaly sa prenesú pod nové procesy a okná, ID listov, názvy, URL a časy zostávajú. Pôvodná tabuľka sa nemaže. Rodičovské životnosti sú odvodené z histórie, skutočný zánik starých okien nie je známy.
- Verzia 2: doplnia sa `activity_spans`, každý pôvodný list dostane jeden úsek.
- Verzia 3: pridá sa samostatná tabuľka ikon bez zmeny ID, časov a aktívnych úsekov histórie. Ikony sa dopĺňajú pri ďalšom pozorovaní aplikácií; bez dostupnej ikony zostáva miesto vedľa názvu prázdne. PNG sa ukladá raz na názov aplikácie ako BLOB, nie pri každej aktivite.
- Migrácia aj uzavretie otvorených procesov/okien/listov prebiehajú v transakcii. Po páde sa nemení posledný potvrdený koniec, iba sa zruší `is_open`, aby prestoj nevytvoril pracovný čas.
- Novšia neznáma verzia sa odmietne otvoriť. Staršiu aplikáciu nepoužívaj na zápis po migrácii. Zmeny schémy musia mať test migrácie s izolovanou databázou.

## Diagnostika bez zmien histórie

```powershell
dotnet run --project tests/SolidRecords.DesktopChecks -- --inspect-history
```

`HistoryDiagnostics` otvorí používateľskú DB read-only bez poolingu, vypíše cestu, SQLite engine, journal mode a schému. SQLite backup API vytvorí konzistentný snapshot v `artifacts/database-diagnostics/`; kontrola `quick_check(5)` prebehne nad snapshotom aj zdrojom. Pri podporovanej schéme vypíše skupiny potenciálne nežiaducich aktivít bez URL. Diagnostické súčty používajú obálku `end-start`, nie sú pracovným reportom zlúčených úsekov.

Diagnostika nevolá inicializáciu store, migráciu, recovery ani checkpoint. Snapshot stále obsahuje citlivú históriu, preto ho nezverejňuj. Pri súborovej zálohe aplikáciu najprv ukonči; pri bežiacej aplikácii použi backup API, nie samostatnú kópiu `.db` bez WAL.

Pri predchádzajúcom vyšetrovaní boli výsledky kontroly živých dát a neskoršieho snapshotu rozdielne; príčina poškodenia nebola preukázaná. Pri ďalšom hlásení najprv zachyť konzistentný stav, verziu schémy a skutočne používaný provider. Samotný fakt, že GUI funguje, ani chyba jednej kontroly neurčujú príčinu.
