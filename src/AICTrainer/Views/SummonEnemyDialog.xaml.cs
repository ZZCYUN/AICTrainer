using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AICShared;
using AICTrainer.ViewModels;

namespace AICTrainer.Views
{
    public partial class SummonEnemyDialog : Window
    {
        private readonly MainViewModel _vm;
        private readonly List<EnemyDisplayItem> _allDisplayItems = new List<EnemyDisplayItem>();
        private readonly ObservableCollection<EnemyDisplayItem> _filteredItems = new ObservableCollection<EnemyDisplayItem>();
        private string _selectedCategory = "ALL";
        private string _searchText = string.Empty;

        public SummonEnemyDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            EnemyListBox.ItemsSource = _filteredItems;

            _vm.Client.OnEnemyListReceived += OnRemoteEnemyListReceived;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (s, e) =>
            {
                _vm.Client.OnEnemyListReceived -= OnRemoteEnemyListReceived;
                _vm.PropertyChanged -= OnVmPropertyChanged;
            };

            Loaded += (s, e) =>
            {
                _vm.Client.RequestEnemyList();
            };

            LoadEnemies();
        }

        private void OnRemoteEnemyListReceived(List<EnemyEntryDto> enemies)
        {
            Dispatcher.Invoke(() =>
            {
                LoadEnemies(enemies);
            });
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.AllEnemies))
            {
                Dispatcher.Invoke(() => LoadEnemies());
            }
        }

        private void LoadEnemies(List<EnemyEntryDto>? enemies = null)
        {
            _allDisplayItems.Clear();

            enemies ??= _vm.AllEnemies;

            if (enemies != null && enemies.Count > 0)
            {
                foreach (var en in enemies)
                {
                    _allDisplayItems.Add(new EnemyDisplayItem
                    {
                        Id = en.Id,
                        Key = en.Key,
                        Name = en.Name,
                        Category = en.Category,
                        CategoryZh = en.CategoryZh,
                        DefaultHp = en.DefaultHp,
                        DefaultMp = en.DefaultMp
                    });
                }
            }

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            _filteredItems.Clear();

            var query = _allDisplayItems.AsEnumerable();

            // 1. 分类筛选
            if (_selectedCategory != "ALL")
            {
                query = query.Where(i => string.Equals(i.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase));
            }

            // 2. 搜索框文本匹配
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string term = _searchText.Trim();
                query = query.Where(i =>
                    (!string.IsNullOrEmpty(i.Name) && i.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(i.Key) && i.Key.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(i.CategoryZh) && i.CategoryZh.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            foreach (var item in query)
            {
                _filteredItems.Add(item);
            }

            // 更新标题数量统计
            SubtitleText.Text = $"共 {_allDisplayItems.Count} 种魔物可用 · 当前显示 {_filteredItems.Count} 种";

            // 空状态提示
            if (_filteredItems.Count == 0)
            {
                EnemyListBox.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
                EmptyStateText.Text = _allDisplayItems.Count == 0
                    ? "正在从游戏引擎加载魔物列表..."
                    : "暂无符合筛选条件的魔物";
            }
            else
            {
                EnemyListBox.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                if (EnemyListBox.SelectedItem == null)
                {
                    EnemyListBox.SelectedIndex = 0;
                }
            }
        }

        private void OnCategoryFilterChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _selectedCategory = tag;
                ApplyFilter();
            }
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = SearchBox.Text;
            SearchPlaceholder.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Visible : Visibility.Collapsed;
            ClearSearchBtn.Visibility = string.IsNullOrEmpty(_searchText) ? Visibility.Collapsed : Visibility.Visible;
            ApplyFilter();
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = string.Empty;
            SearchBox.Focus();
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (!string.IsNullOrEmpty(SearchBox.Text))
                {
                    SearchBox.Text = string.Empty;
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Enter)
            {
                if (EnemyListBox.SelectedItem is EnemyDisplayItem selected)
                {
                    SummonEnemy(selected);
                    e.Handled = true;
                }
            }
        }

        private void OnEnemyDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (EnemyListBox.SelectedItem is EnemyDisplayItem selected)
            {
                SummonEnemy(selected);
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void OnSummonButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EnemyDisplayItem item)
            {
                SummonEnemy(item);
            }
        }

        private void OnSummonSelectedClick(object sender, RoutedEventArgs e)
        {
            if (EnemyListBox.SelectedItem is EnemyDisplayItem selected)
            {
                SummonEnemy(selected);
            }
            else
            {
                MessageBox.Show("请先在列表中选中要召唤的魔物！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SummonEnemy(EnemyDisplayItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Key)) return;

            var dto = BuildSummonDto(item.Key);
            _vm.ExecuteSummonEnemy(dto);
        }

        private SummonEnemyDto BuildSummonDto(string enemyKey)
        {
            // 1. 属性标志位 ENATTR
            uint attrBits = 0;
            if (AttrFireCheck.IsChecked == true) attrBits |= 0x100;
            if (AttrIceCheck.IsChecked == true) attrBits |= 0x200;
            if (AttrThunderCheck.IsChecked == true) attrBits |= 0x400;
            if (AttrSlimyCheck.IsChecked == true) attrBits |= 0x800;
            if (AttrAcmeCheck.IsChecked == true) attrBits |= 0x1000;
            if (AttrInvisibleCheck.IsChecked == true) attrBits |= 0x10000;
            if (AttrBigCheck.IsChecked == true) attrBits |= 0x20000;
            if (AttrAtkCheck.IsChecked == true) attrBits |= 0x1;
            if (AttrDefCheck.IsChecked == true) attrBits |= 0x2;
            if (AttrMpStableCheck.IsChecked == true) attrBits |= 0x4;

            // 2. 状态开关
            bool isOverDrive = OverDriveCheck.IsChecked == true;
            bool isDummy = DummyCheck.IsChecked == true;

            // 3. 生成位置
            int posMode = 0;
            if (FindVisualChildren<RadioButton>(this).FirstOrDefault(r => r.GroupName == "PosMode" && r.IsChecked == true) is RadioButton checkedPos && checkedPos.Tag is string posTag && int.TryParse(posTag, out int p))
            {
                posMode = p;
            }

            // 4. 数量
            int count = 1;
            if (int.TryParse(CountBox.Text, out int c))
            {
                count = Math.Clamp(c, 1, 20);
            }

            // 5. HP倍率
            float hpMult = 1.0f;
            if (float.TryParse(HpMultBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float mult) && mult > 0.01f)
            {
                hpMult = mult;
            }

            return new SummonEnemyDto
            {
                EnemyKey = enemyKey,
                Count = count,
                AttrBits = attrBits,
                IsOverDrive = isOverDrive,
                HpMultiplier = hpMult,
                PositionMode = posMode,
                IsDummy = isDummy
            };
        }

        private void OnCountDecClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CountBox.Text, out int c))
            {
                CountBox.Text = Math.Max(1, c - 1).ToString();
            }
            else
            {
                CountBox.Text = "1";
            }
        }

        private void OnCountIncClick(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(CountBox.Text, out int c))
            {
                CountBox.Text = Math.Min(20, c + 1).ToString();
            }
            else
            {
                CountBox.Text = "1";
            }
        }

        private void OnCountPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        private void OnHpMultPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9.]+$");
        }

        private void OnHpMultPresetChecked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                if (HpMultBox != null)
                {
                    HpMultBox.Text = tag;
                }
            }
        }

        private void OnResetAttrsClick(object sender, RoutedEventArgs e)
        {
            AttrFireCheck.IsChecked = false;
            AttrIceCheck.IsChecked = false;
            AttrThunderCheck.IsChecked = false;
            AttrSlimyCheck.IsChecked = false;
            AttrAcmeCheck.IsChecked = false;
            AttrBigCheck.IsChecked = false;
            AttrInvisibleCheck.IsChecked = false;
            AttrAtkCheck.IsChecked = false;
            AttrDefCheck.IsChecked = false;
            AttrMpStableCheck.IsChecked = false;

            OverDriveCheck.IsChecked = false;
            DummyCheck.IsChecked = false;

            CountBox.Text = "1";
            HpMultBox.Text = "1.0";

            var defaultPos = FindVisualChildren<RadioButton>(this).FirstOrDefault(r => r.GroupName == "PosMode" && (string)r.Tag == "0");
            if (defaultPos != null) defaultPos.IsChecked = true;

            var defaultHp = FindVisualChildren<RadioButton>(this).FirstOrDefault(r => r.GroupName == "HpMultPreset" && (string)r.Tag == "1.0");
            if (defaultHp != null) defaultHp.IsChecked = true;
        }

        private void OnRefreshEnemiesClick(object sender, RoutedEventArgs e)
        {
            _vm.Client.RequestEnemyList();
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

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                {
                    yield return t;
                }
                foreach (var childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }
    }

    public class EnemyDisplayItem : INotifyPropertyChanged
    {
        public uint Id { get; set; }
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "NORMAL";
        public string CategoryZh { get; set; } = "普通魔物";
        public int DefaultHp { get; set; }
        public int DefaultMp { get; set; }

        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Key;
        public string KeyDisplay => $"标识: {Key}";
        public string StatsDisplay => DefaultHp > 0
            ? (DefaultMp > 0 ? $"基础 HP: {DefaultHp} · MP: {DefaultMp}" : $"基础 HP: {DefaultHp}")
            : string.Empty;

        public Brush NameColor => Category == "BOSS"
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF7B72"))
            : (Category == "MACHINE"
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCD34D"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F3F4F6")));

        public Brush CategoryBgColor => Category switch
        {
            "BOSS" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#331215")),
            "MACHINE" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E2010")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#142230"))
        };

        public Brush CategoryBorderColor => Category switch
        {
            "BOSS" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444")),
            "MACHINE" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"))
        };

        public Brush CategoryTextColor => Category switch
        {
            "BOSS" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5")),
            "MACHINE" => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FDE68A")),
            _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"))
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
