# Riders Mirroring — Hub TabDesk-like (oct. 2026)

## Contexte
TabDesk (tabdesk.app) est propriétaire — leur repo GitHub
(`TabDeskApp/tabdesk-release`) ne contient que les binaires release
(.dmg/.msi/.exe), pas le code source. On ne peut donc PAS extraire
l'UI mirroring — uniquement s'en inspirer.

## UI TabDesk retenue comme inspiration
- **Chrome-style tabs** : une rangée d'onglets en haut, un par session de
  mirroring, avec un bouton `×` pour fermer chaque onglet
- **Multi-instance** : un onglet par device (ou par session app sur le
  même device), pas de limite pratique
- **Switch instantané** entre onglets (pas de re-création de fenêtre)
- **Tab actif** mis en évidence (bordure or, fond surface)
- **Indicateur live** (point vert) sur l'onglet pendant que le stream coule
- **Tooltip** sur l'onglet affiche serial + status + FPS

## Architecture implémentée

```
HubViewModel
  ObservableCollection<MirrorTabViewModel> Tabs
  MirrorTabViewModel? SelectedTab
  bool HasTabs
  Func<ScrcpySession, ScrcpySessionViewModel>? _sessionVmFactory (test seam)
  ─────────────────────────────────────────
  OpenTab(session)              → ajoute ou re-focus
  CloseTab(serial | tab)        → dispose + sélectionne voisin
  SelectTab(serial) [RelayCmd]
──────────────────────────────────────────
MirrorTabViewModel (1 par onglet)
  ScrcpySessionViewModel Session
  string Serial
  string Title
  string StatusLabel
  bool IsStreaming
  bool IsActive            ← flag visuel
  double CurrentFps
  string Tooltip
  [RelayCommand] StartAsync / StopAsync
──────────────────────────────────────────
HubView.xaml
  Border (top) avec ItemsControl horizontal → ChromeTabTemplate
  ContentControl → Grid avec Image binding Session.CurrentFrame
  Empty-state quand HasTabs=false
──────────────────────────────────────────
MainViewModel.Hub (instance unique)
AppView.Hub (enum ajouté — entre Mirror et AirPlay)
```

## Détails XAML à ne pas oublier

- `ItemsControl.ItemsPanel` doit être défini **soit** via Resource
  `{StaticResource Foo}` **soit** inline `<ItemsControl.ItemsPanel>...`,
  **jamais les deux** → erreur MC3024
- Converter `BooleanToVisibilityConverter` accepte `ConverterParameter="invert"`
- `DataTrigger` à l'intérieur d'un `DataTemplate.Triggers` permet de
  changer le style de la Border en fonction de `IsActive`
- `x:Shared="False"` n'est PAS nécessaire sur les DataTemplate : WPF
  instancie automatiquement un template par item de ItemsControl

## Tests (9 nouveaux)

- Constructor_HasNoTabs_AndHasTabsIsFalse
- OpenTab_AddsTab_AndSelectsIt
- OpenTab_TwiceForSameSerial_ReturnsExistingTab
- OpenTab_DifferentSerials_CreatesIndependentTabs
- CloseTab_RemovesTab_AndSelectsNeighbour
- CloseTab_UnknownSerial_IsNoOp
- CloseAllTabs_SelectedTabBecomesNull
- Dispose_RemovesAllTabs
- SelectingTab_UpdatesIsActiveFlagOnAllTabs

## Résultats finaux
- **261/261 verts** (156 Core + 105 Desktop) en Debug + Release
- Build 0 erreur
- Audio dépriorisé (fichiers partiels en place : ScrcpyAudioStreamHeader
  + ScrcpyAudioStream + IScrcpyAudioDecoder) mais pas câblés dans
  ScrcpyServer (`audio=false` toujours).
