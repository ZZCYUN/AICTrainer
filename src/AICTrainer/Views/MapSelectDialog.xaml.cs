using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AICShared;
using AICTrainer.ViewModels;

namespace AICTrainer.Views
{
    public partial class MapSelectDialog : Window
    {
        private readonly MainViewModel _vm;
        private readonly List<MapDisplayItem> _allDisplayItems = new List<MapDisplayItem>();
        private readonly ObservableCollection<MapDisplayItem> _filteredItems = new ObservableCollection<MapDisplayItem>();
        private string _selectedAreaFilter = "ALL";

        public MapSelectDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            MapListBox.ItemsSource = _filteredItems;

            _vm.Client.OnMapListReceived += OnRemoteMapListReceived;
            Closed += (s, e) =>
            {
                _vm.Client.OnMapListReceived -= OnRemoteMapListReceived;
            };

            LoadMaps();
        }

        private void OnRemoteMapListReceived(List<MapEntryDto> maps)
        {
            Dispatcher.Invoke(() =>
            {
                LoadMaps();
            });
        }

        private void LoadMaps()
        {
            _allDisplayItems.Clear();

            var maps = _vm.AllMaps;
            if (maps == null || maps.Count == 0)
            {
                maps = _vm.FallbackLoadGameMaps();
            }

            string curMap = _vm.CurrentMapText;

            if (maps != null && maps.Count > 0)
            {
                foreach (var m in maps)
                {
                    bool isCur = string.Equals(m.Key, curMap, StringComparison.OrdinalIgnoreCase) ||
                                 (!string.IsNullOrEmpty(m.Name) && string.Equals(m.Name, curMap, StringComparison.OrdinalIgnoreCase));

                    _allDisplayItems.Add(new MapDisplayItem
                    {
                        Key = m.Key,
                        Name = m.Name,
                        AreaKey = m.AreaKey,
                        AreaDisplayName = string.IsNullOrEmpty(m.AreaName) ? m.AreaKey : m.AreaName,
                        IsCurrent = isCur
                    });
                }
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            string query = SearchBox.Text?.Trim() ?? "";
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(query) ? Visibility.Collapsed : Visibility.Visible;

            var filtered = _allDisplayItems.AsEnumerable();

            // 1. 区域筛选
            if (!string.Equals(_selectedAreaFilter, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(_selectedAreaFilter, "OTHER", StringComparison.OrdinalIgnoreCase))
                {
                    var knownAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    {
                        "house", "forest", "city", "school", "mount", "glacier", "labo", "mine", "sacred"
                    };
                    filtered = filtered.Where(x => !knownAreas.Contains(x.AreaKey));
                }
                else
                {
                    filtered = filtered.Where(x => string.Equals(x.AreaKey, _selectedAreaFilter, StringComparison.OrdinalIgnoreCase));
                }
            }

            // 2. 文本搜索 (支持匹配中文名、英文 key、大区域名)
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(x =>
                    (x.Name != null && x.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.Key != null && x.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.AreaDisplayName != null && x.AreaDisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                );
            }

            _filteredItems.Clear();
            foreach (var item in filtered)
            {
                _filteredItems.Add(item);
            }

            SubtitleText.Text = $"当前所在: {_vm.CurrentMapText}  |  符合条件: {_filteredItems.Count} / {_allDisplayItems.Count} 个地图";

            if (_filteredItems.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                MapListBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                MapListBox.Visibility = Visibility.Visible;
                if (MapListBox.SelectedItem == null)
                {
                    MapListBox.SelectedIndex = 0;
                }
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (MapListBox.SelectedItem is MapDisplayItem selected)
                {
                    TeleportTo(selected.Key);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down)
            {
                MapListBox.Focus();
                e.Handled = true;
            }
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        private void OnAreaFilterChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _selectedAreaFilter = tag;
                ApplyFilter();
            }
        }

        private void OnRefreshMapsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Client.RequestMapList();
            }
            catch { }

            _vm.FallbackLoadGameMaps();
            LoadMaps();
        }

        private void OnMapItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (MapListBox.SelectedItem is MapDisplayItem selected)
            {
                TeleportTo(selected.Key);
            }
        }

        private void OnTeleportButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
            {
                TeleportTo(key);
            }
        }

        private void OnTeleportSelectedClick(object sender, RoutedEventArgs e)
        {
            if (MapListBox.SelectedItem is MapDisplayItem selected)
            {
                TeleportTo(selected.Key);
            }
            else
            {
                MessageBox.Show("请先在列表中选中目标地图！", "未选择地图", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void TeleportTo(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            _vm.ExecuteChangeMap(key);
            Close();
        }

        private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    public class MapDisplayItem : INotifyPropertyChanged
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string AreaKey { get; set; } = string.Empty;
        public string AreaDisplayName { get; set; } = string.Empty;
        public bool IsCurrent { get; set; }

        public string PrimaryDisplayName => !string.IsNullOrEmpty(Name) ? Name : Key;
        public string KeyDisplay => !string.IsNullOrEmpty(Name) ? $"标识: {Key}" : "未命名的游戏场景";
        public Brush NameColor => !string.IsNullOrEmpty(Name)
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));

        public Visibility CurrentBadgeVis => IsCurrent ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
