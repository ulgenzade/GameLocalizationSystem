---
name: avalonia-fluent-architect
description: >-
  Architects, styles, and debugs desktop user interfaces built with Avalonia UI (v11+),
  CommunityToolkit.Mvvm, and Windows 11 Fluent 2 / WinUI 3 design aesthetics.
  Use when designing XAML views, DataGrid columns, Mica/Acrylic themes, async UI dispatching,
  or resolving Avalonia styling, layout, and memory leak issues.
---

# Avalonia UI Fluent 2 & MVVM Architecture Guide

Bu rehber, **GameLocalizationSystem (GLS)** ve modern C# masaüstü projelerinde Avalonia UI 11+ ve CommunityToolkit.Mvvm kullanarak yüksek performanslı, akıcı ve WinUI 3 / Windows 11 Fluent 2 standartlarında arayüzler geliştirmek için kullanılır.

## 🎨 Tasarım Standartları (WinUI 3 Fluent 2)

1. **Katmanlı Koyu Mod (Dark Theme) Renk Paleti:**
   - Pencere Arka Planı (`SolidBackgroundFillColorBase`): `#202020`
   - Kart / Katman Arka Planı (`LayerFillColorDefault`): `#262626`
   - Kontrol Arka Planı (`CardBackgroundFillColorDefault`): `#2D2D2D`
   - Kenarlık (Border Stroke): `#3A3A3A` (1px, Subtle)
   - Vurgu Rengi (Accent): `#60CDFF` (Yazılar için), `#0078D4` (Buton dolgusu için)

2. **Mica ve Acrylic Desteği:**
   ```xml
   <Window ...
           TransparencyLevelHint="Mica, AcrylicBlur"
           Background="Transparent">
   ```

3. **DataGrid & Sanallaştırma (Virtualization):**
   - On binlerce satırlık veri için daima UI Virtualization açık tutulmalıdır:
     ```xml
     <DataGrid CanUserReorderColumns="False"
               CanUserResizeColumns="True"
               CanUserSortColumns="True"
               VirtualizationMode="Recycling"
               RowHeight="36" />
     ```

## ⚡ MVVM ve Asenkron UI Güncelleme Kuralları

1. **Dispatcher.UIThread:**
   - Arka plan iş parçacıklarından gelen veriler doğrudan ObservableCollection'a eklenemez.
   - Daima Avalonia `Dispatcher.UIThread.Post(...)` veya `InvokeAsync(...)` kullanılmalıdır:
     ```csharp
     Dispatcher.UIThread.Post(() =>
     {
         StatusMessage = "İşlem tamamlandı.";
         IsLoading = false;
     });
     ```

2. **CommunityToolkit.Mvvm Özellikleri:**
   - `[ObservableProperty]` ile backing field tanımlanırken bildirim kancaları `partial void On{PropertyName}Changed` ile yakalanır.
   - Komutlar `[RelayCommand]` ile asenkron (`async Task`) tanımlanır ve `NotifyCanExecuteChanged()` ile UI buton durumları yenilenir.

3. **Bellek Sızıntısı Önleme:**
   - ViewModel içinde event dinleyicileri veya `PropertyChanged` event'leri bağlanıyorsa zayıf referans (WeakReference) veya Unsubscribe kalıpları kullanılmalıdır.
   - Pencereler kapatılırken (`Closed` olayı) arkadaki CancellationTokenSource'lar iptal edilmeli ve dispose edilmelidir.
