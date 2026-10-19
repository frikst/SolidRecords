# Úložisko a diagnostika

## Súbory a schéma

Release zostava ukladá používateľské dáta do `%LOCALAPPDATA%\SolidRecords`: `activity.db` vrátane nastavení. Pôvodný `settings.json` sa číta iba pri jednorazovom prevzatí preferencie záznamu. Debug zostava používa databázu v `artifacts/dev/` v repozitári, ktorý je celý ignorovaný Gitom. Absolútna vývojová cesta sa vloží pri zostavení, takže nezávisí od pracovného priečinka pri spustení. Existujúce dáta sa medzi týmito priečinkami nekopírujú. Databáza obsahuje názvy dokumentov a celé dostupné URL vrátane query parametrov. Nie je šifrovaná; automatické mazanie je predvolene vypnuté; aplikácia históriu neposiela na sieť. Vedľa databázy sa ukladá nezávislá cache kontroly vydaní `updates.json`; formát a sieťové správanie opisuje [updates.md](updates.md).

SQLite používa Entity Framework Core 10.0.12 s providerom `Microsoft.EntityFrameworkCore.Sqlite` (pod ním Microsoft.Data.Sqlite), WAL, `synchronous=FULL`, cudzie kľúče, parametrizované príkazy, transakcie a časové/rodičovské indexy. `ActivityDbContext` mapuje samostatné perzistenčné entity na pôvodné tabuľky; modely Core nezávisia od EF. Každá operácia store vytvára vlastný kontext, takže Recorder a report nezdieľajú change tracker. Čítanie používa LINQ bez trackovania, zápis sledované entity a `SaveChangesAsync`; hromadné mazanie a recovery používajú EF príkazy. Pri udalostiach zostáva parametrizovaný `FromSqlInterpolated`, aby sa zachovalo historické poradie `timestamp,rowid`. `SaveAsync` zapisuje celú dávku v jednej transakcii. Čítanie kompozitov má spoločnú read transakciu, aby heartbeat medzi SELECTmi nevytvoril nekonzistentný report. Časy sú UTC Unix milisekundy; GUID sa ukladajú ako text.

| Tabuľka | Význam |
| --- | --- |
| `settings` | Jeden riadok s ID 1 a JSON hodnotou `ApplicationSettings`; minimum, téma, pracovné hodiny a maska dní sa validujú pred zápisom aj po načítaní. Staršie JSON hodnoty bez rozvrhu preberú predvolené pracovné hodiny a dni, s vypnutým obmedzením záznamu na pracovný čas; schéma sa nemení. |
| `application_icons` | Posledná dostupná PNG ikona podľa názvu aplikácie (bez rozlíšenia veľkosti písmen), spoločná aj pre staršie záznamy. |
| `process_activities` | Spoločná identita procesu podľa PID a času vytvorenia naprieč záznamami a rozsah jeho pozorovaní. Pôvodný stĺpec `recording_id` zostáva iba pre kompatibilitu starého rozloženia a pri nových procesoch obsahuje prázdny GUID; identitu ani čítanie aktivít neurčuje. |
| `window_activities` | Životnosť okna, FK na proces. |
| `activity_leaves` | Prijatá aktivita s vlastným `recording_id`, historický názov a voliteľná URL, FK na okno. Začiatok/koniec sú obálka aktívnych úsekov. |
| `activity_spans` | Skutočné aktívne úseky, FK na list s ON DELETE CASCADE. Zdroj pracovného trvania. |
| `events` | Interné udalosti vrátane heartbeat; report pre GUI číta iba aktiváciu/deaktiváciu relácie s dôvodom. |
| `intervals` | Zachovaná pôvodná tabuľka po migrácii verzie 1. |

Pri zmene zlúčenia sa najprv zapíšu rodičia/listy, potom sa upsertnú a priradia úseky a až potom sa odstránia nahradené listy. Zmena tohto poradia môže cez kaskádové mazanie stratiť presunuté úseky.

`ReadProcessesAsync` vracia celé aktivity a úseky zasahujúce do rozsahu; report ich filtruje/normalizuje pred denným orezaním. `ReadIntervalsAsync` už oreže na požadovaný rozsah. Nepoužívaj ho namiesto reportu na posudzovanie minimálnej dĺžky cez polnoc.

## Automatické mazanie

`ApplicationSettings.ActivityRetentionDays` je voliteľný kladný počet dní; `null` znamená „Nikdy“ a použije sa aj pri starších nastaveniach bez tejto hodnoty. Zmena nevyžaduje migráciu schémy. Core `ActivityRetention` počíta hranicu ako aktuálny UTC čas mínus počet 24-hodinových dní, nezávisle od letného času.

App spúšťa mazanie po načítaní nastavení pri štarte, po ich uložení a potom každú hodinu aj pri skrytom okne alebo zastavenom zázname. Recorder ho zaradí do rovnakého pracovného radu ako zápisy, mimo UI vlákna. Chyba mazania sa zobrazí používateľovi a nezastaví zber; periodické opakovanie počká hodinu.

`DeleteBeforeAsync` v jednej transakcii odstráni ukončené listy s koncom striktne pred hranicou (úseky cez kaskádu), staršie ukončené okná/procesy bez potomkov, interné udalosti pred hranicou a staré riadky pôvodnej tabuľky `intervals`, ak existuje. Otvorené aktivity a záznamy zasahujúce cez hranicu zostávajú celé, so skutočnými úsekmi a pôvodnými ID. Nastavenia a spoločné ikony sa nemažú. Ide o logické odstránenie histórie, nie bezpečný prepis databázového súboru či záloh.

## Migrácie a obnova

Schému spravujú EF migrácie v `Migrations/` a tabuľka `__EFMigrationsHistory`. `SqliteSchema.InitializeAsync` ich automaticky aplikuje cez `Database.MigrateAsync` pri štarte ešte pred vytvorením Recorderu a GUI, aj pri vypnutom zázname po spustení. Aplikácia inicializáciu vykonáva cez `Task.Run`, pretože SQLite async API môže blokovať volajúce vlákno. Inicializácia je v rámci inštancie store jednorazová a zamknutá semaforom; volá sa aj pri samostatnom použití store.

Úvodná migrácia `AdoptExistingSchema` vytvorí chýbajúce tabuľky a indexy cez `IF NOT EXISTS`, takže prevezme aj databázy verzie 1–4 bez prepisu histórie. Starý marker `PRAGMA user_version=4` zostáva pre kompatibilitu diagnostiky; ďalšie zmeny eviduje EF. Pri starších databázach sa najprv aplikuje úvodná migrácia, potom sa v samostatnej transakcii prevedú dáta a nastaví starý marker. Až po dokončení prevodu sa aplikujú prípadné ďalšie EF migrácie. Ak sa prevod preruší, marker zostane pôvodný a ďalší štart ho zopakuje bez duplikovania dát. Úvodná migrácia nepodporuje downgrade, ktorý by odstránil prevzatú históriu.

- Verzia 1: intervaly sa prenesú pod nové procesy a okná, ID listov, názvy, URL a časy zostávajú. Pôvodná tabuľka sa nemaže. Rodičovské životnosti sú odvodené z histórie, skutočný zánik starých okien nie je známy.
- Verzia 2: doplnia sa `activity_spans`, každý pôvodný list dostane jeden úsek.
- Verzia 3: pridá sa samostatná tabuľka ikon bez zmeny ID, časov a aktívnych úsekov histórie. Ikony sa dopĺňajú pri ďalšom pozorovaní aplikácií; bez dostupnej ikony zostáva miesto vedľa názvu prázdne. PNG sa ukladá raz na názov aplikácie ako BLOB, nie pri každej aktivite.
- EF migrácia `AddApplicationSettings` pridáva tabuľku nastavení bez zmeny histórie. Chýbajúce nastavenia sa pri štarte vytvoria s predvolenými hodnotami, prípadne s prevzatým `RecordOnLaunch` zo starého JSON. Existujúce databázové nastavenia majú vždy prednosť; starý súbor sa nemení.
- EF migrácia `UnifyProcessIdentity` najprv prenesie RecordingId z rodičov na jednotlivé aktivity. Pozorovania so známou dvojicou PID a času vytvorenia spojí pod ID najstaršieho pozorovania, presunie podň pôvodné okná a rozšíri rozsah pozorovaní. ID okien, aktivít a úsekov, ich časy aj hranice záznamov zostávajú zachované. Neznáme časy vytvorenia ani rovnaké PID s iným časom sa nespájajú. Čiastočný unikátny index `ix_process_identity` vynucuje jediný proces pre známu dvojicu; zlučovanie pozorovaní nemá spätnú migráciu.
- EF migrácie spravujú vlastné transakcie. Prevod starých dát aj následné uzavretie otvorených procesov/okien/listov majú samostatné transakcie. Po páde sa nemení posledný potvrdený koniec, iba sa zruší `is_open`, aby prestoj nevytvoril pracovný čas.
- Novšia neznáma hodnota `user_version` alebo neznámy záznam v `__EFMigrationsHistory` sa odmietne ešte pred migráciami a recovery. Staršiu aplikáciu nepoužívaj na zápis po migrácii. Zmeny schémy musia mať test migrácie s izolovanou databázou.

## Diagnostika bez zmien histórie

```powershell
dotnet run --project tests/SolidRecords.DesktopChecks -- --inspect-history
```

`HistoryDiagnostics` otvorí používateľskú DB read-only bez poolingu, vypíše cestu, SQLite engine, journal mode a schému. SQLite backup API vytvorí konzistentný snapshot v `artifacts/database-diagnostics/`; kontrola `quick_check(5)` prebehne nad snapshotom aj zdrojom. Pri podporovanej schéme vypíše skupiny potenciálne nežiaducich aktivít bez URL. Diagnostické súčty používajú obálku `end-start`, nie sú pracovným reportom zlúčených úsekov.

Diagnostika nevolá inicializáciu store, migráciu, recovery ani checkpoint. Snapshot stále obsahuje citlivú históriu, preto ho nezverejňuj. Pri súborovej zálohe aplikáciu najprv ukonči; pri bežiacej aplikácii použi backup API, nie samostatnú kópiu `.db` bez WAL.

Pri predchádzajúcom vyšetrovaní boli výsledky kontroly živých dát a neskoršieho snapshotu rozdielne; príčina poškodenia nebola preukázaná. Pri ďalšom hlásení najprv zachyť konzistentný stav, verziu schémy a skutočne používaný provider. Samotný fakt, že GUI funguje, ani chyba jednej kontroly neurčujú príčinu.
