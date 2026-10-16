# Architektúra

## Projekty

| Projekt | Zodpovednosť a vstupné súbory |
| --- | --- |
| `src/SolidRecords.Core` | Čisté modely a kontrakty (`Models.cs`), stavový automat (`RecordingEngine.cs`), pravidlá prijatia a zlúčenia (`ActivityLogPolicy.cs`), koordinácia (`Recorder.cs`), denné reporty (`DailyReport.cs`). Target `net10.0`, bez externých balíkov. |
| `src/SolidRecords.Storage` | `SqliteActivityStore.cs`, `ActivityDbContext.cs`, `SqliteSchema.cs`, `Migrations/`; Entity Framework Core SQLite 10.0.12. Závisí len od Core. |
| `src/SolidRecords.Windows` | `WindowsActivitySource.cs`, `ObservedWindowRegistry.cs`, `WindowFilter.cs`, `BrowserUrlReader.cs`, `StartupRegistration.cs`, Win32 interop. Target `net10.0-windows`, WPF pre natívne message window; závisí len od Core. |
| `src/SolidRecords.App` | WPF/MVVM, vlastné príkazy a observable modely, WinForms NotifyIcon. `App.xaml.cs` skladá služby a riadi životný cyklus; `Presentation/` obsahuje view model, riadky a časovú os. Závisí od troch implementačných projektov. |
| `tests/SolidRecords.Tests` | xUnit testy Core a SQLite, target `net10.0`. |
| `tests/SolidRecords.DesktopChecks` | STA konzolový program pre Windows integráciu, klasifikáciu okien, živé GUI a WPF náhľad; referencuje App. |

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

`App` vytvorí jeden store, Recorder, report service, view model, hlavné okno a systray. Recorder aj report dostanú spoločné `ActivityTrackingOptions`. Bez `--show` zostane aplikácia v systray. Zatvorenie okna ho skryje; ukončenie cez systray uvoľní view model, dopíše Recorder a zavrie aplikáciu.

Globálny mutex podľa používateľského SID povoľuje jednu inštanciu naprieč reláciami; druhé spustenie signalizuje otvorenie existujúceho okna cez EventWaitHandle. Debug zostava pridáva k názvom oboch objektov vývojovú príponu s hashom dátového priečinka, takže môže bežať súčasne s Release zostavou aj s Debug zostavou z iného repozitára. `settings.json` uchováva iba `RecordOnLaunch`, predvolene true. Spúšťanie po prihlásení spravuje `StartupRegistration` cez HKCU Run pre aktuálnu cestu aplikácie.

## Vzhľad

`App.xaml` nastavuje WPF Fluent `ThemeMode="System"`, ktorý sleduje režim aplikácií vo Windows aj počas behu. Vlastné štýly a hlavné okno používajú `DynamicResource` na štetce Fluent; `TimelineControl` ich viaže cez dependency properties s `AffectsRender`, aby sa pri prepnutí prekreslil. Natívne WPF ovládacie prvky vrátane kalendára a posuvníkov preberajú tému Fluent. Systémová lišta používa WinForms `NotifyIcon`, ale pravý klik otvára natívne owner-drawn `HMENU` cez `NativeTrayMenu` a `TrackPopupMenuEx`. Windows tak počas otvorenia beží v skutočnom menu režime (`GUI_INMENUMODE`), ktorý potrebuje aj panel skrytých ikon. Skryté top-level owner okno sa pred otvorením aktivuje cez `SetForegroundWindow`; po návrate sa mu odošle `WM_NULL`. Akcie sa vykonajú až po opustení menu slučky; HMENU a GDI štetec sa vždy uvoľnia, owner okno žije do uvoľnenia systray. `TrayMenu` uchováva položky a spoločný plochý renderer so vzdušnejšími riadkami a vektorovými ikonami podľa DPI; WinForms popup slúži iba izolovanému náhľadu. Rozmery položiek sú pripravené pred natívnym otvorením. Pri každom otvorení preberá aktuálne štetce Fluent aj po zmene témy pri skrytom okne; zaškrtnutie autostartu a ikona spustenia/zastavenia vyjadrujú aktuálny stav. Vyhľadávanie používa vlastnú šablónu `SearchTextBox` so spoločným rámom pre ikonu a text; klávesový fokus zvýrazní celý rám. Voľba témy sa neukladá do nastavení.
