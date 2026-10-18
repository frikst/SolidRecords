# Architektúra

## Projekty

| Projekt | Zodpovednosť a vstupné súbory |
| --- | --- |
| `src/SolidRecords.Core` | Spoločné modely, kontrakty a nastavenia v koreni (`Models.cs`, `ActivityTrackingOptions.cs`); `Recording/` obsahuje stavový automat, pravidlá prijatia a zlúčenia a Recorder; `Reporting/` denné reporty a ich službu. Target `net10.0`, bez externých balíkov. |
| `src/SolidRecords.Storage` | `SqliteActivityStore.cs`, `ActivityDbContext.cs`, `SqliteSchema.cs`, `Migrations/`; Entity Framework Core SQLite 10.0.12. Závisí len od Core. |
| `src/SolidRecords.Windows` | `Capture/` obsahuje zdroj signálov, register a filter okien, čítanie URL a ikon; `Interop/` spoločné Win32 volania; `Shell/` autostart a vzhľad okna. Target `net10.0-windows10.0.17763.0`, WPF pre natívne message window; závisí len od Core. |
| `src/SolidRecords.App` | WPF/MVVM, vlastné príkazy a observable modely, WinForms NotifyIcon. `Program.cs` spúšťa aplikáciu, `ServiceRegistration.cs` registruje DI služby a `App.xaml.cs` riadi životný cyklus; `Presentation/` obsahuje view model, riadky a časovú os; `Tray/` ikonu a menu systémovej lišty; `Infrastructure/` dátové cesty, preferencie a jednu inštanciu aplikácie. Závisí od troch implementačných projektov. |
| `tests/SolidRecords.Tests` | xUnit testy Core a SQLite, target `net10.0`. |
| `tests/SolidRecords.DesktopChecks` | STA konzolový program pre Windows integráciu, klasifikáciu okien, živé GUI a WPF náhľad; referencuje App. |

Namespace zodpovedajú priečinkom pod koreňom projektu (napr. `SolidRecords.Windows.Capture`). Spoločné Core kontrakty zostávajú v `SolidRecords.Core`; Storage tvorí jeden celok s oddelenými EF migráciami v `SolidRecords.Storage.Migrations`.

## Tok dát a vlákna

```text
WindowsActivitySource → CaptureSignal → Recorder (Channel, jeden reader)
    → RecordingEngine → RecordingBatch → ActivityLogPolicy
    → IActivityStore / SQLite transakcia → RecordsChanged
    → MainViewModel → DailyReportService → DailyReport → riadky a časová os
```

- Zdroj má vlastné STA vlákno, dispatcher a skryté `HwndSource`. WinEvent hooky sledujú foreground, názov a zánik okna; WTS a power správy sledujú reláciu a uspatie. Sekundový timer overuje stav, okná, žiada URL a vydáva heartbeat.
- `ApplicationIconReader` na vlákne zberu získava ikonu okna cez časovo obmedzené `WM_GETICON`, prípadne z EXE cez Windows Shell. Výsledok vrátane nedostupnosti cacheuje podľa názvu aplikácie do zastavenia zberu; získané shell ikony uvoľňuje. PNG ide cez `CaptureSignal` a `RecordingBatch` do samostatnej tabuľky. Nie je súčasťou identity `WindowInfo` ani časových pravidiel. Pri čítaní sa pripojí k aktivitám; prezentácia ho dekóduje mimo UI vlákna, zmrazí a zdieľa medzi riadkami. Zoznam zobrazuje ikonu v 16 logických bodoch vedľa názvu aplikácie.
- `BrowserUrlReader` číta adresný riadok cez UI Automation na samostatnom MTA vlákne. Pomalý externý provider tak neblokuje GUI ani message loop zberu.
- `RecordingEngine.Apply` je synchrónny stavový automat bez I/O. `Recorder` serializuje signály cez Channel, aplikuje politiku a zapisuje dávky. `StopAsync` zastaví zdroj a dopíše prijaté signály. Chyba zberu/zápisu vypne stav zaznamenávania; zotavenie vyžaduje reštart.
- `RecordsChanged` sa vyvolá až po úspešnom uložení. View model prenesie notifikáciu na UI dispatcher a zoskupí požiadavky na obnovu cez 150 ms timer. Čítanie reportu beží cez `Task.Run`, pretože SQLite vykonáva časť async API synchrónne.
- Riadky v `ObservableCollection` sa pridávajú, presúvajú a aktualizujú podľa ID. Odpoveď pre už zmenený deň sa nezobrazí. Triedenie hlavičkou cykluje vzostupne → zostupne → pôvodné poradie od najnovších záznamov; stĺpec Trvanie používa skutočný čas vrátane aktívneho záznamu, hoci sa pri ňom zobrazuje pomlčka. Triedenie sa priebežne prispôsobuje zmenám riadkov. Vyhľadávanie filtruje len zoznam; súčet a časová os patria celému vybranému dňu.

## Životný cyklus aplikácie

`Program.Main` s atribútom `[STAThread]` je vstupný bod aplikácie. Na hlavnom STA vlákne overí jednu inštanciu a zostaví kontajner Microsoft.Extensions.DependencyInjection s validáciou registrácií. `ServiceRegistration.AddSolidRecords` registruje singleton store, Recorder, report service, view model, hlavné okno, nastavenia a aplikáciu. Recorder aj report dostanú tú istú inštanciu `ActivityTrackingOptions`; natívny zdroj vlastní a uvoľňuje Recorder. `App` dostáva závislosti konštruktorom, inicializuje store mimo UI vlákna a až po načítaní WPF zdrojov vytvorí okno cez odloženú DI factory. `TrayIconFactory` pripojí systray k akciám životného cyklu. Po načítaní databázových nastavení aplikuje minimum, tému a autostart. Bez `--show` rozhoduje o otvorení okna `StartMinimized`. Zatvorenie okna ho skryje; ukončenie cez systray zastaví a dopíše Recorder a zavrie aplikáciu. Po návrate z WPF slučky Program asynchrónne uvoľní kontajner vrátane odhlásenia view modelu a uvoľnenia Recorderu.

`SingleInstance` spravuje mutex, signalizačnú udalosť a odhlásenie callbacku. Druhá inštancia končí ešte pred vytvorením DI služieb alebo prístupom do databázy. Globálny mutex podľa používateľského SID povoľuje jednu inštanciu naprieč reláciami; druhé spustenie signalizuje otvorenie existujúceho okna cez EventWaitHandle. Debug zostava pridáva k názvom oboch objektov vývojovú príponu s hashom dátového priečinka, takže môže bežať súčasne s Release zostavou aj s Debug zostavou z iného repozitára. `PreferencesStore` načíta `ApplicationSettings` cez Core kontrakt `ISettingsStore` z SQLite; pri prvom načítaní bez uložených nastavení prevezme `RecordOnLaunch` zo starého `settings.json`. JSON sa ďalej nezapisuje. `SettingsViewModel` spravuje editáciu a validáciu, `SettingsWindow` životný cyklus dialógu. Uloženie prebehne mimo UI vlákna; pri zmene minima App dopíše a obnoví záznam a aktualizuje spoločné `ActivityTrackingOptions`. Zmena bežného stavu záznamu nemení voľbu záznamu pri štarte. Pracovné hranice vyhodnocuje Core `RecordingAutomation` a App vykonáva jeho rozhodnutia sekundovým timerom aj pri skrytom okne; časový rozvrh a rozsah osi opisuje [recording.md](recording.md). Spúšťanie po prihlásení spravuje `StartupRegistration` cez HKCU Run pre aktuálnu cestu aplikácie.

## Aktualizácie

Core `Updates/` obsahuje pravidlá verzií, denný interval, GitHub REST klienta a kontrakty cache a notifikácií. App `Updates/` riadi slučku, súborovú cache a akcie; Windows `Shell/` vytvára natívne toast notifikácie a sťahuje/odovzdáva MSI pomocníkovi. Aktualizácie bežia nezávisle od otvorenia hlavného okna a záznamu. Detaily sú v [updates.md](updates.md).

## Vzhľad

`App.xaml` nastavuje WPF Fluent `ThemeMode="System"`, ktorý sleduje režim aplikácií vo Windows aj počas behu. Vlastné štýly a hlavné okno používajú `DynamicResource` na štetce Fluent; `TimelineControl` ich viaže cez dependency properties s `AffectsRender`, aby sa pri prepnutí prekreslil. Natívne WPF ovládacie prvky vrátane kalendára a posuvníkov preberajú tému Fluent. Systémová lišta používa WinForms `NotifyIcon`, ale pravý klik otvára natívne owner-drawn `HMENU` cez `NativeTrayMenu` a `TrackPopupMenuEx`. Windows tak počas otvorenia beží v skutočnom menu režime (`GUI_INMENUMODE`), ktorý potrebuje aj panel skrytých ikon. Skryté top-level owner okno sa pred otvorením aktivuje cez `SetForegroundWindow`; po návrate sa mu odošle `WM_NULL`. Akcie sa vykonajú až po opustení menu slučky; HMENU a GDI štetec sa vždy uvoľnia, owner okno žije do uvoľnenia systray. `TrayMenu` uchováva položky a spoločný plochý renderer so vzdušnejšími riadkami a vektorovými ikonami podľa DPI; WinForms popup slúži iba izolovanému náhľadu. Rozmery položiek sú pripravené pred natívnym otvorením. Pri každom otvorení preberá aktuálne štetce Fluent aj po zmene témy pri skrytom okne; položka Nastavenia otvára dialóg a ikona spustenia/zastavenia vyjadruje aktuálny stav. Vyhľadávanie používa vlastnú šablónu `SearchTextBox` so spoločným rámom pre ikonu a text; klávesový fokus zvýrazní celý rám. Voľba System/Light/Dark sa ukladá do databázy a nastavuje `Application.ThemeMode`.
