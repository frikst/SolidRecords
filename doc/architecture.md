# Architektúra

## Projekty

| Projekt | Zodpovednosť a vstupné súbory |
| --- | --- |
| `src/SolidRecords.Core` | Čisté modely a kontrakty (`Models.cs`), stavový automat (`RecordingEngine.cs`), pravidlá prijatia a zlúčenia (`ActivityLogPolicy.cs`), koordinácia (`Recorder.cs`), denné reporty (`DailyReport.cs`). Target `net10.0`, bez externých balíkov. |
| `src/SolidRecords.Storage` | `SqliteActivityStore.cs`, `SqliteSchema.cs`; Microsoft.Data.Sqlite 10.0.12. Závisí len od Core. |
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
- `BrowserUrlReader` číta adresný riadok cez UI Automation na samostatnom MTA vlákne. Pomalý externý provider tak neblokuje GUI ani message loop zberu.
- `RecordingEngine.Apply` je synchrónny stavový automat bez I/O. `Recorder` serializuje signály cez Channel, aplikuje politiku a zapisuje dávky. `StopAsync` zastaví zdroj a dopíše prijaté signály. Chyba zberu/zápisu vypne stav zaznamenávania; zotavenie vyžaduje reštart.
- `RecordsChanged` sa vyvolá až po úspešnom uložení. View model prenesie notifikáciu na UI dispatcher a zoskupí požiadavky na obnovu cez 150 ms timer. Čítanie reportu beží cez `Task.Run`, pretože SQLite vykonáva časť async API synchrónne.
- Riadky v `ObservableCollection` sa pridávajú, presúvajú a aktualizujú podľa ID. Odpoveď pre už zmenený deň sa nezobrazí. Vyhľadávanie filtruje len zoznam; súčet a časová os patria celému vybranému dňu.

## Životný cyklus aplikácie

`App` vytvorí jeden store, Recorder, report service, view model, hlavné okno a systray. Recorder aj report dostanú spoločné `ActivityTrackingOptions`. Bez `--show` zostane aplikácia v systray. Zatvorenie okna ho skryje; ukončenie cez systray uvoľní view model, dopíše Recorder a zavrie aplikáciu.

Globálny mutex podľa používateľského SID povoľuje jednu inštanciu naprieč reláciami; druhé spustenie signalizuje otvorenie existujúceho okna cez EventWaitHandle. `settings.json` uchováva iba `RecordOnLaunch`, predvolene true. Spúšťanie po prihlásení spravuje `StartupRegistration` cez HKCU Run pre aktuálnu cestu aplikácie.

## Vzhľad

`App.xaml` nastavuje WPF Fluent `ThemeMode="System"`, ktorý sleduje režim aplikácií vo Windows aj počas behu. Vlastné štýly a hlavné okno používajú `DynamicResource` na štetce Fluent; `TimelineControl` ich viaže cez dependency properties s `AffectsRender`, aby sa pri prepnutí prekreslil. Natívne WPF ovládacie prvky vrátane kalendára a posuvníkov preberajú tému Fluent. WinForms menu systémovej lišty pri každom otvorení preberá aktuálne štetce Fluent cez vlastný `ProfessionalColorTable`, aj po zmene témy pri skrytom okne. Vyhľadávanie používa vlastnú šablónu `SearchTextBox` so spoločným rámom pre ikonu a text; klávesový fokus zvýrazní celý rám. Voľba témy sa neukladá do nastavení.
