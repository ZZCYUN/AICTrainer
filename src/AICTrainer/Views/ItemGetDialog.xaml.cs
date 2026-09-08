using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AICShared;
using AICTrainer.Services;
using AICTrainer.ViewModels;

namespace AICTrainer.Views
{
    public partial class ItemGetDialog : Window
    {
        private readonly MainViewModel _vm;
        private readonly List<ItemDisplayItem> _allDisplayItems = new List<ItemDisplayItem>();
        private readonly ObservableCollection<ItemDisplayItem> _filteredItems = new ObservableCollection<ItemDisplayItem>();
        private string _selectedCategoryFilter = "ALL";
        private int _selectedGrade;

        private static readonly HashSet<string> KnownChipCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MTR", "FOOD", "FRUIT", "CURE_HP", "CURE_MP", "TOOL", "BOMB", "FOR_FISHING", "SPECIAL"
        };

        public ItemGetDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            ItemListBox.ItemsSource = _filteredItems;

            _vm.Client.OnItemListReceived += OnRemoteItemListReceived;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (s, e) =>
            {
                _vm.Client.OnItemListReceived -= OnRemoteItemListReceived;
                _vm.PropertyChanged -= OnVmPropertyChanged;
            };

            LoadItems();
        }

        private void OnRemoteItemListReceived(List<ItemEntryDto> items)
        {
            DiagLog.Write($"ItemGetDialog: OnRemoteItemListReceived -> reload with {(items?.Count ?? 0)} items");
            Dispatcher.Invoke(() =>
            {
                LoadItems(items);
            });
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.AllItems))
            {
                DiagLog.Write($"ItemGetDialog: AllItems changed -> reload with {_vm.AllItems.Count} items");
                Dispatcher.Invoke(() => LoadItems());
            }
        }

        private void LoadItems(List<ItemEntryDto>? items = null)
        {
            _allDisplayItems.Clear();

            items ??= _vm.AllItems;
            DiagLog.Write($"ItemGetDialog: LoadItems using {(items?.Count ?? 0)} items");

            if (items != null && items.Count > 0)
            {
                foreach (var it in items)
                {
                    _allDisplayItems.Add(new ItemDisplayItem
                    {
                        Key = it.Key,
                        Name = it.Name,
                        Category = string.IsNullOrEmpty(it.Category) ? "OTHER" : it.Category,
                        CategoryZh = string.IsNullOrEmpty(it.CategoryZh) ? "其他" : it.CategoryZh,
                        OwnedCount = it.OwnedCount
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

            // 1. 分类筛选：物品可能属于多个分类（位标志组合），只要命中任一即显示
            if (!string.Equals(_selectedCategoryFilter, "ALL", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(_selectedCategoryFilter, "OTHER", StringComparison.OrdinalIgnoreCase))
                {
                    filtered = filtered.Where(x => !x.Categories.Any(c => KnownChipCategories.Contains(c)));
                }
                else
                {
                    filtered = filtered.Where(x => x.Categories.Contains(_selectedCategoryFilter, StringComparer.OrdinalIgnoreCase));
                }
            }

            // 2. 文本搜索 (支持匹配中文名、英文 key、分类中文)
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(x =>
                    (x.Name != null && x.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.Key != null && x.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.CategoryZh != null && x.CategoryZh.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                );
            }

            _filteredItems.Clear();
            foreach (var item in filtered)
            {
                _filteredItems.Add(item);
            }

            SubtitleText.Text = $"符合条件: {_filteredItems.Count} / {_allDisplayItems.Count} 个物品";

            if (_filteredItems.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                ItemListBox.Visibility = Visibility.Collapsed;
                EmptyStateText.Text = _allDisplayItems.Count == 0
                    ? "正在从游戏获取物品列表，请稍候..."
                    : "暂无符合条件的物品";
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                ItemListBox.Visibility = Visibility.Visible;
                if (ItemListBox.SelectedItem == null)
                {
                    ItemListBox.SelectedIndex = 0;
                }
            }
        }

        private int GetCurrentCount()
        {
            if (int.TryParse(CountBox.Text?.Trim(), out int c) && c > 0)
            {
                return c > 9999 ? 9999 : c;
            }
            return 1;
        }

        private int GetCurrentGrade()
        {
            return _selectedGrade;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (ItemListBox.SelectedItem is ItemDisplayItem selected)
                {
                    AddItem(selected.Key);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down)
            {
                ItemListBox.Focus();
                e.Handled = true;
            }
        }

        private void OnCountPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        private void OnCategoryFilterChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                _selectedCategoryFilter = tag;
                ApplyFilter();
            }
        }

        private void OnGradeFilterChanged(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && int.TryParse(rb.Tag as string, out int grade))
            {
                _selectedGrade = grade;
            }
        }

        private void OnRefreshItemsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Client.RequestItemList();
            }
            catch { }

            LoadItems();
        }

        private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ItemListBox.SelectedItem is ItemDisplayItem selected)
            {
                AddItem(selected.Key);
            }
        }

        private void OnGetItemButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string key)
            {
                AddItem(key);
            }
        }

        private void OnGetSelectedClick(object sender, RoutedEventArgs e)
        {
            if (ItemListBox.SelectedItem is ItemDisplayItem selected)
            {
                AddItem(selected.Key);
            }
            else
            {
                MessageBox.Show("请先在列表中选中要获取的物品！", "未选择物品", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void AddItem(string key)
        {
            if (string.IsNullOrEmpty(key)) return;

            int count = GetCurrentCount();
            int grade = GetCurrentGrade();
            _vm.ExecuteAddItem(key, count, grade);

            // 获取后刷新列表，让持有数实时更新（弹窗保持打开，便于连续获取多种物品）
            try
            {
                _vm.Client.RequestItemList();
            }
            catch { }
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

    public class ItemDisplayItem : INotifyPropertyChanged
    {
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Category { get; set; } = "OTHER";
        public string CategoryZh { get; set; } = "其他";
        public int OwnedCount { get; set; } = -1;

        // 物品可能属于多个分类（位标志组合，Category 逗号分隔），用于筛选
        public string[] Categories => string.IsNullOrEmpty(Category)
            ? new[] { "OTHER" }
            : Category.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

        public string PrimaryDisplayName => !string.IsNullOrEmpty(Name) ? Name : Key;
        public string KeyDisplay => $"标识: {Key}";
        public string OwnedText => OwnedCount >= 0 ? $"持有: {OwnedCount}" : "持有: --";
        public Brush NameColor => !string.IsNullOrEmpty(Name)
            ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
