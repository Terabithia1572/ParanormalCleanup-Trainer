using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ParanormalTrainer
{
    public sealed class TrainerWindow : Window
    {
        private static readonly Brush BackgroundBrush = Brush("#202122");
        private static readonly Brush PanelBrush = Brush("#292A2D");
        private static readonly Brush RaisedBrush = Brush("#333438");
        private static readonly Brush LineBrush = Brush("#3A3B3F");
        private static readonly Brush AccentBrush = Brush("#EF8700");
        private static readonly Brush TextBrush = Brush("#F5F2EC");
        private static readonly Brush MutedBrush = Brush("#9D9EA5");
        private static readonly Brush GreenBrush = Brush("#79C997");

        private readonly TrainerEngine _engine;
        private readonly SemaphoreSlim _engineGate = new SemaphoreSlim(1, 1);
        private readonly DispatcherTimer _timer;
        private readonly List<FeatureRow> _rows = new List<FeatureRow>();
        private readonly List<QuickRow> _quickRows = new List<QuickRow>();
        private readonly HashSet<int> _registeredHotkeys = new HashSet<int>();
        private readonly CultureInfo _searchCulture = CultureInfo.GetCultureInfo("tr-TR");
        private Button _attachButton, _disableButton, _hotkeyButton, _quickTabButton, _allTabButton, _categoryButton;
        private TextBlock _statusTitle, _statusText, _processText, _topStatus, _hotkeyStatus, _countText, _messageText;
        private Ellipse _connectionDot;
        private Border _messageBar;
        private TextBox _search;
        private ScrollViewer _quickScroll, _allScroll;
        private StackPanel _allRows;
        private FrameworkElement _quickPage, _allPage;
        private FrameworkElement _houseArt;
        private string _category = "Tüm kategoriler";
        private bool _busy, _tickRunning, _closing, _allowClose, _hotkeysEnabled = true;
        private bool _lastAttached, _lastReady;
        private IntPtr _windowHandle;
        private HwndSource _source;
        private string _lastStatus = "Önce oyunu aç, ardından bağlantıyı başlat.";
        private int _readCursor;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public TrainerWindow()
        {
            _engine = new TrainerEngine();
            Title = "Paranormal Cleanup • Trainer";
            Width = 1040;
            Height = 780;
            MinWidth = 860;
            MinHeight = 620;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = BackgroundBrush;
            Foreground = TextBrush;
            FontFamily = new FontFamily("Segoe UI");
            FontSize = 13;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Resources.Add(typeof(Button), MakeButtonStyle());
            Resources.Add(typeof(TextBox), MakeTextBoxStyle());
            Content = BuildLayout();
            BuildFeatureRows();
            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(250);
            _timer.Tick += OnTimer;
            SourceInitialized += OnSourceInitialized;
            Closing += OnClosing;
            SizeChanged += delegate { UpdateCompactLayout(ActualHeight); };
            Loaded += delegate { _timer.Start(); RefreshControls(); };
        }

        private static Brush Brush(string color)
        {
            SolidColorBrush brush = (SolidColorBrush)new BrushConverter().ConvertFromInvariantString(color);
            brush.Freeze();
            return brush;
        }

        private static Style MakeButtonStyle()
        {
            Style style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, RaisedBrush));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, LineBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 9, 14, 9)));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Shell";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            FrameworkElementFactory presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(FrameworkElement.MarginProperty, new System.Windows.Data.Binding("Padding") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.AppendChild(presenter);
            template.VisualTree = border;
            Trigger hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.83, "Shell"));
            template.Triggers.Add(hover);
            Trigger disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "Shell"));
            template.Triggers.Add(disabled);
            Trigger focused = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focused.Setters.Add(new Setter(Border.BorderBrushProperty, AccentBrush, "Shell"));
            template.Triggers.Add(focused);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style MakeTextBoxStyle()
        {
            Style style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("#1C1D1F")));
            style.Setters.Add(new Setter(Control.ForegroundProperty, TextBrush));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, LineBrush));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            style.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, AccentBrush));
            style.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, AccentBrush));
            Trigger focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, AccentBrush));
            style.Triggers.Add(focus);
            return style;
        }

        private static TextBlock Text(string value, double size, Brush foreground, FontWeight weight)
        {
            return new TextBlock { Text = value, FontSize = size, Foreground = foreground, FontWeight = weight, TextWrapping = TextWrapping.Wrap };
        }

        private static Button Button(string label, RoutedEventHandler click)
        {
            Button button = new Button { Content = label };
            if (click != null) button.Click += click;
            return button;
        }

        private FrameworkElement BuildLayout()
        {
            Grid root = new Grid { Background = BackgroundBrush };
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Border header = new Border { Background = AccentBrush, Padding = new Thickness(24, 0, 24, 0) };
            Grid headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(Text("P / C", 20, Brush("#1B1D20"), FontWeights.Black));
            brand.Children.Add(new Border { Width = 1, Height = 22, Background = Brush("#B56400"), Margin = new Thickness(18, 0, 18, 0) });
            brand.Children.Add(Text("PARANORMAL CLEANUP", 16, Brush("#1B1D20"), FontWeights.Bold));
            headerGrid.Children.Add(brand);
            TextBlock edition = Text("TRAINER  /  01", 11, Brush("#402800"), FontWeights.Bold);
            edition.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(edition, 1);
            headerGrid.Children.Add(edition);
            header.Child = headerGrid;
            root.Children.Add(header);

            Grid body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(228) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            body.Children.Add(BuildSidebar());
            FrameworkElement main = BuildMain();
            Grid.SetColumn(main, 1);
            body.Children.Add(main);
            root.Children.Add(body);

            Border footer = new Border { Background = Brush("#1A1B1D"), BorderBrush = LineBrush, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(20, 9, 20, 9) };
            Grid footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.Children.Add(Text("Yunus İNAN tarafından yazılmıştır.  •  CT: ndck76", 11, MutedBrush, FontWeights.Normal));
            TextBlock footerHint = Text("Ctrl + Num 0  ·  Bağlan / ayır", 11, MutedBrush, FontWeights.Normal);
            Grid.SetColumn(footerHint, 1);
            footerGrid.Children.Add(footerHint);
            footer.Child = footerGrid;
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            return root;
        }

        private FrameworkElement BuildSidebar()
        {
            Border sidebar = new Border { Background = Brush("#191B1D"), Padding = new Thickness(22, 28, 22, 20), BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 1, 0) };
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            StackPanel top = new StackPanel();
            _houseArt = BuildHouse();
            top.Children.Add(_houseArt);
            TextBlock title = Text("PARANORMAL\nCLEANUP", 21, TextBrush, FontWeights.Black);
            title.TextWrapping = TextWrapping.NoWrap;
            title.LineHeight = 29;
            title.Margin = new Thickness(0, 19, 0, 8);
            top.Children.Add(title);
            top.Children.Add(Text("GECE VARDİYASI", 10, AccentBrush, FontWeights.Bold));
            TextBlock description = Text("Temizlik işi biraz daha kolaylaştı.", 12, MutedBrush, FontWeights.Normal);
            description.Margin = new Thickness(0, 14, 0, 24);
            top.Children.Add(description);
            grid.Children.Add(top);

            StackPanel bottom = new StackPanel();
            Border statusCard = new Border { Background = PanelBrush, CornerRadius = new CornerRadius(8), Padding = new Thickness(13), BorderBrush = LineBrush, BorderThickness = new Thickness(1) };
            StackPanel status = new StackPanel();
            _processText = Text("OYUN BAĞLANTISI", 9, MutedBrush, FontWeights.Bold);
            status.Children.Add(_processText);
            StackPanel statusHeading = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 11, 0, 8) };
            _connectionDot = new Ellipse { Width = 7, Height = 7, Fill = MutedBrush, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            statusHeading.Children.Add(_connectionDot);
            _statusTitle = Text("Bağlantı yok", 13, TextBrush, FontWeights.SemiBold);
            statusHeading.Children.Add(_statusTitle);
            status.Children.Add(statusHeading);
            _statusText = Text(_lastStatus, 11, MutedBrush, FontWeights.Normal);
            _statusText.LineHeight = 16;
            status.Children.Add(_statusText);
            statusCard.Child = status;
            bottom.Children.Add(statusCard);
            _attachButton = Button("Oyuna bağlan", OnAttachClick);
            _attachButton.Background = AccentBrush;
            _attachButton.Foreground = Brush("#211500");
            _attachButton.BorderBrush = AccentBrush;
            _attachButton.Margin = new Thickness(0, 12, 0, 12);
            bottom.Children.Add(_attachButton);
            TextBlock note = Text("Oyunda bir bölüme gir. Oyuncu bulunduğunda özellikler açılır.", 10, MutedBrush, FontWeights.Normal);
            note.LineHeight = 15;
            bottom.Children.Add(note);
            Grid.SetRow(bottom, 2);
            grid.Children.Add(bottom);
            sidebar.Child = grid;
            return sidebar;
        }

        private static FrameworkElement BuildHouse()
        {
            Canvas canvas = new Canvas { Width = 184, Height = 166, ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left };
            Border sky = new Border { Width = 184, Height = 166, Background = Brush("#23282A"), CornerRadius = new CornerRadius(9) };
            canvas.Children.Add(sky);
            Ellipse halo = new Ellipse { Width = 100, Height = 100, Fill = Brush("#2D3536") };
            Canvas.SetLeft(halo, 64); Canvas.SetTop(halo, 10); canvas.Children.Add(halo);
            Ellipse moon = new Ellipse { Width = 46, Height = 46, Fill = Brush("#CFBF99") };
            Canvas.SetLeft(moon, 107); Canvas.SetTop(moon, 21); canvas.Children.Add(moon);
            Ellipse shadow = new Ellipse { Width = 43, Height = 43, Fill = Brush("#2D3536") };
            Canvas.SetLeft(shadow, 94); Canvas.SetTop(shadow, 13); canvas.Children.Add(shadow);
            Polygon hill = new Polygon { Fill = Brush("#161B1C"), Points = new PointCollection { new Point(0, 148), new Point(35, 138), new Point(84, 141), new Point(129, 136), new Point(184, 146), new Point(184, 166), new Point(0, 166) } };
            canvas.Children.Add(hill);
            Rectangle house = new Rectangle { Width = 94, Height = 64, Fill = Brush("#161A1B"), Stroke = Brush("#555044"), StrokeThickness = 1 };
            Canvas.SetLeft(house, 43); Canvas.SetTop(house, 77); canvas.Children.Add(house);
            Polygon roof = new Polygon { Fill = Brush("#15191A"), Stroke = Brush("#555044"), StrokeThickness = 1, Points = new PointCollection { new Point(34, 81), new Point(90, 43), new Point(146, 81) } };
            canvas.Children.Add(roof);
            Rectangle chimney = new Rectangle { Width = 10, Height = 25, Fill = Brush("#171B1C") };
            Canvas.SetLeft(chimney, 116); Canvas.SetTop(chimney, 46); canvas.Children.Add(chimney);
            AddWindow(canvas, 58, 88); AddWindow(canvas, 113, 88);
            Rectangle door = new Rectangle { Width = 19, Height = 36, Fill = Brush("#A56B20") };
            Canvas.SetLeft(door, 81); Canvas.SetTop(door, 106); canvas.Children.Add(door);
            Rectangle doorShade = new Rectangle { Width = 12, Height = 36, Fill = Brush("#48381E") };
            Canvas.SetLeft(doorShade, 81); Canvas.SetTop(doorShade, 106); canvas.Children.Add(doorShade);
            Ellipse attic = new Ellipse { Width = 11, Height = 11, Fill = AccentBrush };
            Canvas.SetLeft(attic, 85); Canvas.SetTop(attic, 63); canvas.Children.Add(attic);
            Polygon path = new Polygon { Fill = Brush("#353229"), Points = new PointCollection { new Point(81, 142), new Point(100, 142), new Point(124, 166), new Point(57, 166) } };
            canvas.Children.Add(path);
            Line tree = new Line { X1 = 22, Y1 = 141, X2 = 18, Y2 = 74, Stroke = Brush("#131819"), StrokeThickness = 4 };
            canvas.Children.Add(tree);
            canvas.Children.Add(new Line { X1 = 19, Y1 = 105, X2 = 6, Y2 = 89, Stroke = Brush("#131819"), StrokeThickness = 3 });
            canvas.Children.Add(new Line { X1 = 19, Y1 = 96, X2 = 31, Y2 = 81, Stroke = Brush("#131819"), StrokeThickness = 3 });
            return canvas;
        }

        private static void AddWindow(Canvas canvas, double left, double top)
        {
            Rectangle window = new Rectangle { Width = 13, Height = 18, Fill = AccentBrush, Opacity = 0.85 };
            Canvas.SetLeft(window, left); Canvas.SetTop(window, top); canvas.Children.Add(window);
            canvas.Children.Add(new Line { X1 = left + 6.5, X2 = left + 6.5, Y1 = top, Y2 = top + 18, Stroke = Brush("#25251F"), StrokeThickness = 2 });
            canvas.Children.Add(new Line { X1 = left, X2 = left + 13, Y1 = top + 8, Y2 = top + 8, Stroke = Brush("#25251F"), StrokeThickness = 2 });
        }

        private FrameworkElement BuildMain()
        {
            Grid main = new Grid { Margin = new Thickness(26, 24, 24, 18) };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid heading = new Grid { Margin = new Thickness(0, 0, 0, 20) };
            heading.ColumnDefinitions.Add(new ColumnDefinition());
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(Text("Kontrol sende.", 25, TextBrush, FontWeights.SemiBold));
            _topStatus = Text("Oyuna bağlanarak başla.", 12, MutedBrush, FontWeights.Normal);
            _topStatus.Margin = new Thickness(0, 7, 0, 0);
            title.Children.Add(_topStatus);
            heading.Children.Add(title);
            _disableButton = Button("Tümünü kapat", OnDisableAllClick);
            _disableButton.VerticalAlignment = VerticalAlignment.Top;
            _disableButton.FontSize = 11;
            _disableButton.ToolTip = "Hızlı özellikleri kapatır, dondurulan değerleri serbest bırakır.";
            Grid.SetColumn(_disableButton, 1);
            heading.Children.Add(_disableButton);
            main.Children.Add(heading);

            Border tabsBorder = new Border { BorderBrush = LineBrush, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 12), Margin = new Thickness(0, 0, 0, 18) };
            StackPanel tabs = new StackPanel { Orientation = Orientation.Horizontal };
            _quickTabButton = Button("Hızlı özellikler", delegate { ShowPage(false); });
            _quickTabButton.Margin = new Thickness(0, 0, 8, 0);
            _allTabButton = Button("Tüm değerler", delegate { ShowPage(true); });
            tabs.Children.Add(_quickTabButton); tabs.Children.Add(_allTabButton);
            tabsBorder.Child = tabs;
            Grid.SetRow(tabsBorder, 1); main.Children.Add(tabsBorder);

            Grid pageHost = new Grid();
            _quickPage = BuildQuickPage();
            _allPage = BuildAllPage();
            pageHost.Children.Add(_quickPage); pageHost.Children.Add(_allPage);
            Grid.SetRow(pageHost, 2); main.Children.Add(pageHost);

            _messageBar = new Border { Background = Brush("#3E3022"), BorderBrush = Brush("#79552B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            Grid messageGrid = new Grid(); messageGrid.ColumnDefinitions.Add(new ColumnDefinition()); messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _messageText = Text("", 11, Brush("#F0C38A"), FontWeights.Normal);
            _messageText.VerticalAlignment = VerticalAlignment.Center;
            messageGrid.Children.Add(_messageText);
            Button dismiss = Button("×", delegate { _messageBar.Visibility = Visibility.Collapsed; });
            dismiss.Padding = new Thickness(8, 2, 8, 2); dismiss.Background = Brushes.Transparent; dismiss.BorderThickness = new Thickness(0); dismiss.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(dismiss, 1); messageGrid.Children.Add(dismiss); _messageBar.Child = messageGrid;
            Grid.SetRow(_messageBar, 3); main.Children.Add(_messageBar);

            Grid hotkeys = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            hotkeys.ColumnDefinitions.Add(new ColumnDefinition()); hotkeys.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _hotkeyStatus = Text("Kısayollar hazırlanıyor…", 10, MutedBrush, FontWeights.Normal);
            _hotkeyStatus.VerticalAlignment = VerticalAlignment.Center;
            hotkeys.Children.Add(_hotkeyStatus);
            _hotkeyButton = Button("Kısayollar: açık", delegate { ToggleHotkeys(); });
            _hotkeyButton.FontSize = 10; _hotkeyButton.Padding = new Thickness(10, 6, 10, 6);
            _hotkeyButton.ToolTip = "Ctrl + Shift + Home ile de açılıp kapanır.";
            Grid.SetColumn(_hotkeyButton, 1); hotkeys.Children.Add(_hotkeyButton);
            Grid.SetRow(hotkeys, 4); main.Children.Add(hotkeys);
            ShowPage(false);
            return main;
        }

        private FrameworkElement BuildQuickPage()
        {
            _quickScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 7, 0) };
            StackPanel stack = new StackPanel();
            TextBlock intro = Text("Bir dokunuşla etkinleştir. Kapatınca önceki değerler geri yüklenir.", 11, MutedBrush, FontWeights.Normal);
            intro.Margin = new Thickness(0, 0, 0, 15); stack.Children.Add(intro);
            string[,] quicks = new string[,] {
                {"health", "Can sabitle", "Canı mevcut üst sınırında tutar.", "01"},
                {"stamina", "Dayanıklılık sabitle", "Dayanıklılığı mevcut üst sınırında tutar.", "02"},
                {"sprint", "Hızlı koşu", "Koşu avantajı çarpanı: 2×.", "03"},
                {"jump", "Yüksek zıplama", "Zıplama kuvveti: 900.", "04"},
                {"revive", "Hızlı canlandırma", "Canlandırma süresi: 0,25 saniye.", "05"},
                {"light", "Güçlü el feneri", "Fener avantajı çarpanı: 3×.", "06"},
                {"fov", "Geniş görüş", "Görüş açısı: 110°.", "07"}
            };
            for (int i = 0; i < quicks.GetLength(0); i++)
            {
                QuickRow row = new QuickRow { Key = quicks[i, 0], HotkeyId = i + 1 };
                Border card = new Border { Background = PanelBrush, CornerRadius = new CornerRadius(7), BorderBrush = LineBrush, BorderThickness = new Thickness(1), Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(0, 0, 0, 6) };
                Grid grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                grid.ColumnDefinitions.Add(new ColumnDefinition());
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Hotkey = Text("NUM\n" + quicks[i, 3], 10, MutedBrush, FontWeights.Bold);
                row.Hotkey.TextAlignment = TextAlignment.Center; row.Hotkey.VerticalAlignment = VerticalAlignment.Center;
                Border key = new Border { Background = Brush("#212225"), CornerRadius = new CornerRadius(4), BorderBrush = LineBrush, BorderThickness = new Thickness(1), Width = 36, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, Child = row.Hotkey };
                grid.Children.Add(key);
                StackPanel names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                names.Children.Add(Text(quicks[i, 1], 13, TextBrush, FontWeights.SemiBold));
                TextBlock detail = Text(quicks[i, 2], 10, MutedBrush, FontWeights.Normal); detail.Margin = new Thickness(0, 4, 0, 0); names.Children.Add(detail);
                Grid.SetColumn(names, 1); grid.Children.Add(names);
                QuickRow capture = row;
                row.Toggle = Button("KAPALI", delegate { ToggleQuick(capture); });
                row.Toggle.Width = 79; row.Toggle.FontSize = 10; row.Toggle.VerticalAlignment = VerticalAlignment.Center; row.Toggle.Padding = new Thickness(7, 9, 7, 9);
                row.Toggle.ToolTip = quicks[i, 1] + " • Num " + (i + 1).ToString(CultureInfo.InvariantCulture) + "\n" + quicks[i, 2];
                Grid.SetColumn(row.Toggle, 2); grid.Children.Add(row.Toggle);
                card.Child = grid; stack.Children.Add(card); _quickRows.Add(row);
            }
            TextBlock hint = Text("Num tuşları diğer uygulamalarda da çalışır. Değer yazarken kısayolları kapatabilirsin.", 10, MutedBrush, FontWeights.Normal);
            hint.Margin = new Thickness(0, 6, 0, 4); hint.LineHeight = 15; stack.Children.Add(hint);
            _quickScroll.Content = stack;
            return _quickScroll;
        }

        private FrameworkElement BuildAllPage()
        {
            Grid page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid filters = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            filters.ColumnDefinitions.Add(new ColumnDefinition()); filters.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _search = new TextBox { ToolTip = "Türkçe veya özgün özellik adına göre ara", Margin = new Thickness(0, 0, 9, 0), MinWidth = 100 };
            _search.TextChanged += delegate { ApplyFilter(); };
            _search.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { _search.Clear(); e.Handled = true; } };
            filters.Children.Add(_search);
            _categoryButton = Button("Tüm kategoriler  ▾", OnCategoryClick);
            _categoryButton.FontSize = 11; _categoryButton.MaxWidth = 200;
            Grid.SetColumn(_categoryButton, 1); filters.Children.Add(_categoryButton); page.Children.Add(filters);
            _countText = Text("Özellikler yükleniyor…", 10, MutedBrush, FontWeights.Normal);
            _countText.Margin = new Thickness(0, 0, 0, 12); Grid.SetRow(_countText, 1); page.Children.Add(_countText);
            _allRows = new StackPanel();
            _allScroll = new ScrollViewer { Content = _allRows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 7, 0) };
            Grid.SetRow(_allScroll, 2); page.Children.Add(_allScroll);
            return page;
        }

        private void BuildFeatureRows()
        {
            foreach (Feature feature in _engine.Features)
            {
                FeatureRow row = new FeatureRow { Feature = feature };
                Border card = new Border { Background = PanelBrush, BorderBrush = LineBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(13, 11, 13, 11) };
                Grid grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid info = new Grid(); info.ColumnDefinitions.Add(new ColumnDefinition()); info.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel names = new StackPanel();
                TextBlock title = Text(String.IsNullOrWhiteSpace(feature.TurkishName) ? feature.Name : feature.TurkishName, 12, TextBrush, FontWeights.SemiBold);
                title.ToolTip = feature.Name;
                names.Children.Add(title);
                TextBlock original = Text(feature.Name + "  ·  " + feature.Category, 9, MutedBrush, FontWeights.Normal); original.Margin = new Thickness(0, 4, 0, 0); names.Children.Add(original);
                info.Children.Add(names);
                TextBlock kind = Text(feature.Writable ? feature.Type : "SALT OKUNUR", 9, feature.Writable ? MutedBrush : Brush("#C6AD7D"), FontWeights.SemiBold); kind.Margin = new Thickness(10, 1, 0, 0); Grid.SetColumn(kind, 1); info.Children.Add(kind);
                grid.Children.Add(info);

                Grid values = new Grid { Margin = new Thickness(0, 10, 0, 0) };
                values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                values.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(115) });
                values.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                values.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                StackPanel current = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                current.Children.Add(Text("ANLIK DEĞER", 8, MutedBrush, FontWeights.SemiBold));
                row.Current = Text("—", 12, TextBrush, FontWeights.SemiBold); row.Current.TextTrimming = TextTrimming.CharacterEllipsis; row.Current.TextWrapping = TextWrapping.NoWrap; row.Current.Margin = new Thickness(0, 2, 0, 0); current.Children.Add(row.Current); values.Children.Add(current);
                row.Input = new TextBox { Text = "", FontSize = 11, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 7, 0), ToolTip = "Yeni değer; ondalık için nokta veya virgül kullanabilirsin." };
                row.Input.IsReadOnly = !feature.Writable;
                row.Input.IsEnabled = feature.Writable;
                FeatureRow capture = row;
                row.Input.TextChanged += delegate { if (!capture.SettingText) capture.UserEdited = true; };
                row.Input.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter && capture.Apply.IsEnabled) { ApplyFeature(capture); e.Handled = true; } };
                Grid.SetColumn(row.Input, 1); values.Children.Add(row.Input);
                row.Apply = Button("Uygula", delegate { ApplyFeature(capture); }); row.Apply.FontSize = 10; row.Apply.Padding = new Thickness(10, 7, 10, 7); row.Apply.Margin = new Thickness(0, 0, 6, 0);
                Grid.SetColumn(row.Apply, 2); values.Children.Add(row.Apply);
                row.Freeze = Button("Sabitle", delegate { FreezeFeature(capture); }); row.Freeze.FontSize = 10; row.Freeze.Padding = new Thickness(10, 7, 10, 7); row.Freeze.MinWidth = 63;
                row.Freeze.ToolTip = "Değeri sürekli uygular. Tekrar tıklayarak serbest bırak.";
                Grid.SetColumn(row.Freeze, 3); values.Children.Add(row.Freeze);
                if (!feature.Writable)
                {
                    row.Input.Visibility = Visibility.Collapsed;
                    row.Apply.Visibility = Visibility.Collapsed;
                    row.Freeze.Visibility = Visibility.Collapsed;
                    Grid.SetColumnSpan(current, 4);
                }
                Grid.SetRow(values, 1); grid.Children.Add(values); card.Child = grid; row.Root = card;
                _allRows.Children.Add(card); _rows.Add(row);
            }
            _allTabButton.Content = "Tüm değerler  ·  " + _rows.Count.ToString(CultureInfo.InvariantCulture);
            ApplyFilter();
        }

        private void ShowPage(bool all)
        {
            _quickPage.Visibility = all ? Visibility.Collapsed : Visibility.Visible;
            _allPage.Visibility = all ? Visibility.Visible : Visibility.Collapsed;
            _quickTabButton.Background = all ? Brushes.Transparent : RaisedBrush;
            _quickTabButton.Foreground = all ? MutedBrush : AccentBrush;
            _quickTabButton.BorderBrush = all ? Brushes.Transparent : LineBrush;
            _allTabButton.Background = all ? RaisedBrush : Brushes.Transparent;
            _allTabButton.Foreground = all ? AccentBrush : MutedBrush;
            _allTabButton.BorderBrush = all ? LineBrush : Brushes.Transparent;
        }

        private void UpdateCompactLayout(double windowHeight)
        {
            if (_houseArt != null) _houseArt.Visibility = windowHeight < 700 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnCategoryClick(object sender, RoutedEventArgs e)
        {
            ContextMenu menu = new ContextMenu { Background = PanelBrush, Foreground = TextBrush, BorderBrush = LineBrush, PlacementTarget = _categoryButton, Placement = PlacementMode.Bottom, MaxHeight = 360 };
            List<string> categories = new List<string> { "Tüm kategoriler" };
            categories.AddRange(_rows.Select(delegate(FeatureRow row) { return row.Feature.Category; }).Where(delegate(string category) { return !String.IsNullOrWhiteSpace(category); }).Distinct().OrderBy(delegate(string category) { return category; }, StringComparer.Create(_searchCulture, true)));
            foreach (string category in categories)
            {
                string capture = category;
                MenuItem item = new MenuItem { Header = category, Foreground = TextBrush, Background = PanelBrush, IsCheckable = true, IsChecked = _category == category, Padding = new Thickness(8, 5, 8, 5) };
                item.Click += delegate { _category = capture; _categoryButton.Content = capture + "  ▾"; ApplyFilter(); };
                menu.Items.Add(item);
            }
            _categoryButton.ContextMenu = menu; menu.IsOpen = true;
        }

        private void ApplyFilter()
        {
            if (_search == null || _countText == null) return;
            string query = _search.Text.Trim();
            int visible = 0;
            foreach (FeatureRow row in _rows)
            {
                bool categoryMatch = _category == "Tüm kategoriler" || row.Feature.Category == _category;
                string haystack = (row.Feature.TurkishName ?? "") + " " + row.Feature.Name + " " + row.Feature.Category;
                bool textMatch = query.Length == 0 || _searchCulture.CompareInfo.IndexOf(haystack, query, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
                row.Root.Visibility = categoryMatch && textMatch ? Visibility.Visible : Visibility.Collapsed;
                if (row.Root.Visibility == Visibility.Visible) visible++;
            }
            _countText.Text = visible.ToString(CultureInfo.InvariantCulture) + " / " + _rows.Count.ToString(CultureInfo.InvariantCulture) + " değer  •  Ada göre ara; Uygula ile yaz, Sabitle ile koru.";
        }

        private void OnAttachClick(object sender, RoutedEventArgs e)
        {
            if (_busy || _closing) return;
            bool detach = _lastAttached;
            RunOperation(detach ? "Bağlantı kesiliyor…" : "Oyun aranıyor ve tablo eşleştiriliyor…", delegate { if (detach) _engine.Detach(); else _engine.Attach(); }, null);
        }

        private void OnDisableAllClick(object sender, RoutedEventArgs e)
        {
            RunOperation("Özellikler kapatılıyor…", delegate { _engine.DisableAll(); }, "Hızlı özellikler kapatıldı; sabitlenen değerler serbest bırakıldı.");
        }

        private void ToggleQuick(QuickRow row)
        {
            if (!_lastReady || _busy || _closing) return;
            bool next = !row.Enabled;
            RunOperation("Özellik güncelleniyor…", delegate { _engine.ToggleQuick(row.Key, next); }, null);
        }

        private void ApplyFeature(FeatureRow row)
        {
            if (!_lastReady || _busy || _closing || !row.Feature.Writable) return;
            string value = row.Input.Text.Trim();
            if (value.Length == 0) { ShowMessage("Önce uygulanacak değeri yaz."); row.Input.Focus(); return; }
            RunOperation("Değer uygulanıyor…", delegate { _engine.Apply(row.Feature, value); }, "Değer uygulandı: " + (row.Feature.TurkishName ?? row.Feature.Name));
        }

        private void FreezeFeature(FeatureRow row)
        {
            if (!_lastReady || _busy || _closing || !row.Feature.Writable) return;
            string value = row.Input.Text.Trim();
            bool next = !row.Frozen;
            if (next && value.Length == 0) { ShowMessage("Sabitlenecek değeri yaz."); row.Input.Focus(); return; }
            RunOperation(next ? "Değer sabitleniyor…" : "Değer serbest bırakılıyor…", delegate { _engine.Freeze(row.Feature, value, next); }, null);
        }

        private async void RunOperation(string working, Action action, string success)
        {
            if (_busy || _closing) return;
            _busy = true;
            _topStatus.Text = working;
            _messageBar.Visibility = Visibility.Collapsed;
            RefreshControls();
            await _engineGate.WaitAsync();
            try
            {
                Exception failure = null;
                try
                {
                    EngineSnapshot snapshot = await Task.Run(delegate { action(); return Snapshot(null); });
                    RenderSnapshot(snapshot);
                    if (success != null) ShowMessage(success);
                }
                catch (Exception ex) { failure = ex; }
                if (failure != null)
                {
                    // Failed attach/detach can retain ownership when restoration needs a retry.
                    // Reflect that state so the disconnect action remains available.
                    try { RenderSnapshot(await Task.Run(delegate { return Snapshot(null); })); }
                    catch { }
                    ShowMessage(failure.Message);
                }
            }
            finally
            {
                _engineGate.Release();
                _busy = false;
                RefreshControls();
                if (!_closing) _topStatus.Text = _lastReady ? "Bağlantı hazır. Özellikleri kullanabilirsin." : (_lastAttached ? "Oyuncu bekleniyor. Oyunda bir bölüme gir." : "Oyuna bağlanarak başla.");
            }
        }

        private async void OnTimer(object sender, EventArgs e)
        {
            if (_busy || _tickRunning || _closing) return;
            _tickRunning = true;
            List<Feature> read = VisibleFeatures();
            await _engineGate.WaitAsync();
            try
            {
                EngineSnapshot snapshot = await Task.Run(delegate { _engine.Tick(); return Snapshot(read); });
                if (!_closing) RenderSnapshot(snapshot);
            }
            catch (Exception ex) { if (!_closing) { ShowMessage(ex.Message); _topStatus.Text = "Bağlantı denetlenirken hata oluştu."; } }
            finally { _engineGate.Release(); _tickRunning = false; }
        }

        private List<Feature> VisibleFeatures()
        {
            List<Feature> result = new List<Feature>();
            if (!_lastReady || _allPage.Visibility != Visibility.Visible) return result;
            List<FeatureRow> candidates = new List<FeatureRow>();
            foreach (FeatureRow row in _rows)
            {
                if (row.Root.Visibility != Visibility.Visible) continue;
                try
                {
                    Rect bounds = row.Root.TransformToAncestor(_allScroll).TransformBounds(new Rect(new Point(0, 0), row.Root.RenderSize));
                    if (bounds.Bottom >= 0 && bounds.Top <= _allScroll.ActualHeight) candidates.Add(row);
                }
                catch (InvalidOperationException) { }
            }
            if (candidates.Count == 0) return result;
            // Bound work per tick even when the viewport is unusually tall.
            int count = Math.Min(14, candidates.Count);
            for (int i = 0; i < count; i++) result.Add(candidates[(_readCursor + i) % candidates.Count].Feature);
            _readCursor = (_readCursor + count) % candidates.Count;
            return result;
        }

        private EngineSnapshot Snapshot(List<Feature> read)
        {
            EngineSnapshot snapshot = new EngineSnapshot { Attached = _engine.IsAttached, Ready = _engine.IsReady, Status = _engine.Status, ProcessId = _engine.ProcessId };
            foreach (QuickRow row in _quickRows) snapshot.Quick[row.Key] = _engine.QuickEnabled(row.Key);
            foreach (FeatureRow row in _rows) snapshot.Frozen[row.Feature.Id] = row.Feature.Frozen;
            if (read != null && snapshot.Ready)
            {
                foreach (Feature feature in read)
                {
                    try { snapshot.Values[feature.Id] = _engine.Read(feature); }
                    catch (Exception ex) { snapshot.Values[feature.Id] = "Okunamadı"; snapshot.ReadErrors[feature.Id] = ex.Message; }
                }
            }
            return snapshot;
        }

        private void RenderSnapshot(EngineSnapshot snapshot)
        {
            bool lost = _lastAttached && !snapshot.Attached;
            _lastAttached = snapshot.Attached; _lastReady = snapshot.Ready;
            _lastStatus = snapshot.Status;
            _statusTitle.Text = snapshot.Ready ? "Oyuncu bulundu" : (snapshot.Attached ? "Oyuncu bekleniyor" : "Bağlantı yok");
            _statusText.Text = String.IsNullOrWhiteSpace(snapshot.Status) ? (snapshot.Ready ? "Özellikler kullanıma hazır." : "Oyunu açıp bağlantıyı başlat.") : snapshot.Status;
            _processText.Text = snapshot.Attached ? "PID " + snapshot.ProcessId.ToString(CultureInfo.InvariantCulture) + "  ·  64 BİT" : "OYUN BAĞLANTISI";
            _connectionDot.Fill = snapshot.Ready ? GreenBrush : (snapshot.Attached ? AccentBrush : MutedBrush);
            if (!_busy) _topStatus.Text = snapshot.Ready ? "Bağlantı hazır. Özellikleri kullanabilirsin." : (snapshot.Attached ? "Oyuncu bekleniyor. Oyunda bir bölüme gir." : "Oyuna bağlanarak başla.");
            foreach (QuickRow row in _quickRows)
            {
                row.Enabled = snapshot.Quick[row.Key];
                row.Toggle.Content = row.Enabled ? "●  AÇIK" : "KAPALI";
                row.Toggle.Background = row.Enabled ? AccentBrush : RaisedBrush;
                row.Toggle.Foreground = row.Enabled ? Brush("#281800") : MutedBrush;
                row.Toggle.BorderBrush = row.Enabled ? AccentBrush : LineBrush;
            }
            foreach (FeatureRow row in _rows)
            {
                row.Frozen = snapshot.Frozen[row.Feature.Id];
                row.Freeze.Content = row.Frozen ? "Sabit ✓" : "Sabitle";
                row.Freeze.Foreground = row.Frozen ? AccentBrush : TextBrush;
                row.Freeze.BorderBrush = row.Frozen ? AccentBrush : LineBrush;
                string value;
                if (snapshot.Values.TryGetValue(row.Feature.Id, out value))
                {
                    row.Current.Text = value;
                    string error; row.Current.ToolTip = snapshot.ReadErrors.TryGetValue(row.Feature.Id, out error) ? error : value;
                    if (!row.UserEdited && !row.Input.IsKeyboardFocused && value != "Okunamadı" && value != "—")
                    {
                        row.SettingText = true; row.Input.Text = value; row.SettingText = false;
                    }
                }
                else if (!snapshot.Ready) { row.Current.Text = "—"; row.Current.ToolTip = "Oyuncu bağlantısı bekleniyor."; }
            }
            if (lost) ShowMessage("Oyun bağlantısı sona erdi. Yeniden bağlanabilirsin.");
            RefreshControls();
        }

        private void RefreshControls()
        {
            bool available = !_busy && !_closing;
            _attachButton.IsEnabled = available;
            _attachButton.Content = _busy ? "Lütfen bekle…" : (_lastAttached ? "Bağlantıyı kes" : "Oyuna bağlan");
            _disableButton.IsEnabled = available && _lastAttached;
            _hotkeyButton.IsEnabled = available;
            foreach (QuickRow row in _quickRows) row.Toggle.IsEnabled = available && _lastReady;
            foreach (FeatureRow row in _rows)
            {
                row.Input.IsEnabled = available && row.Feature.Writable;
                row.Apply.IsEnabled = available && _lastReady && row.Feature.Writable && !row.Frozen;
                row.Freeze.IsEnabled = available && _lastReady && row.Feature.Writable;
            }
        }

        private void ShowMessage(string message)
        {
            _messageText.Text = message;
            _messageBar.Visibility = Visibility.Visible;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _windowHandle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_windowHandle);
            if (_source != null) _source.AddHook(WindowMessage);
            RegisterHotkeys();
        }

        private void RegisterHotkeys()
        {
            if (_windowHandle == IntPtr.Zero) return;
            UnregisterHotkeys();
            TryHotkey(101, 2u | 4u | 0x4000u, 0x24u);
            TryHotkey(100, 2u | 0x4000u, 0x60u);
            if (_hotkeysEnabled)
                for (int i = 1; i <= 7; i++) TryHotkey(i, 0x4000u, (uint)(0x60 + i));
            UpdateHotkeyStatus();
        }

        private void TryHotkey(int id, uint modifier, uint key)
        {
            if (RegisterHotKey(_windowHandle, id, modifier, key)) _registeredHotkeys.Add(id);
        }

        private void UnregisterHotkeys()
        {
            foreach (int id in _registeredHotkeys) UnregisterHotKey(_windowHandle, id);
            _registeredHotkeys.Clear();
        }

        private void UpdateHotkeyStatus()
        {
            int registered = _registeredHotkeys.Count(delegate(int id) { return id >= 1 && id <= 7; });
            _hotkeyButton.Content = _hotkeysEnabled ? "Kısayollar: açık" : "Kısayollar: kapalı";
            _hotkeyButton.Foreground = _hotkeysEnabled ? AccentBrush : MutedBrush;
            if (!_hotkeysEnabled) _hotkeyStatus.Text = "Num kısayolları kapalı  ·  Ctrl + Shift + Home ile aç";
            else if (registered == 7) _hotkeyStatus.Text = "Num 1–7 etkin  ·  Ctrl + Shift + Home ile kapat";
            else _hotkeyStatus.Text = registered.ToString(CultureInfo.InvariantCulture) + "/7 kısayol etkin  ·  Bazıları başka uygulamalarda kullanımda";
            if (!_registeredHotkeys.Contains(101)) _hotkeyStatus.Text += "  ·  Aç/kapat kısayolu kullanılamıyor";
            if (!_registeredHotkeys.Contains(100)) _hotkeyStatus.Text += "  ·  Bağlantı kısayolu kullanılamıyor";
            foreach (QuickRow row in _quickRows)
            {
                bool active = _registeredHotkeys.Contains(row.HotkeyId);
                row.Hotkey.Foreground = active ? AccentBrush : MutedBrush;
                row.Hotkey.ToolTip = active ? "Kısayol etkin" : (_hotkeysEnabled ? "Kısayol başka bir uygulamada kullanımda; düğmeyi kullan." : "Kısayollar kapalı");
            }
        }

        private void ToggleHotkeys()
        {
            if (_closing) return;
            _hotkeysEnabled = !_hotkeysEnabled;
            RegisterHotkeys();
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != 0x0312) return IntPtr.Zero;
            int id = wParam.ToInt32();
            if (!_registeredHotkeys.Contains(id)) return IntPtr.Zero;
            handled = true;
            if (_closing) return IntPtr.Zero;
            if (id == 101) ToggleHotkeys();
            else if (id == 100) OnAttachClick(this, new RoutedEventArgs());
            else if (id >= 1 && id <= _quickRows.Count && _hotkeysEnabled) ToggleQuick(_quickRows[id - 1]);
            return IntPtr.Zero;
        }

        private async void OnClosing(object sender, CancelEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            if (_closing) return;
            _closing = true;
            _timer.Stop();
            UnregisterHotkeys();
            _topStatus.Text = "Özellikler geri yükleniyor ve bağlantı kapatılıyor…";
            RefreshControls();
            await _engineGate.WaitAsync();
            try
            {
                await Task.Run(delegate { _engine.Dispose(); });
                if (_source != null) _source.RemoveHook(WindowMessage);
                _allowClose = true;
            }
            catch (Exception ex)
            {
                _closing = false;
                ShowMessage("Güvenli kapanış tamamlanamadı: " + ex.Message + " Bağlantıyı kesmeyi yeniden deneyebilirsin.");
                RegisterHotkeys();
                _timer.Start();
                RefreshControls();
            }
            finally { _engineGate.Release(); }
            if (_allowClose) Close();
        }

        private sealed class QuickRow
        {
            public string Key;
            public int HotkeyId;
            public bool Enabled;
            public Button Toggle;
            public TextBlock Hotkey;
        }

        private sealed class FeatureRow
        {
            public Feature Feature;
            public FrameworkElement Root;
            public TextBlock Current;
            public TextBox Input;
            public Button Apply, Freeze;
            public bool Frozen, UserEdited, SettingText;
        }

        private sealed class EngineSnapshot
        {
            public bool Attached, Ready;
            public string Status;
            public int ProcessId;
            public readonly Dictionary<string, bool> Quick = new Dictionary<string, bool>();
            public readonly Dictionary<int, bool> Frozen = new Dictionary<int, bool>();
            public readonly Dictionary<int, string> Values = new Dictionary<int, string>();
            public readonly Dictionary<int, string> ReadErrors = new Dictionary<int, string>();
        }
    }
}
