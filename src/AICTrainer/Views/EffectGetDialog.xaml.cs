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
using AICTrainer.ViewModels;

namespace AICTrainer.Views
{
    public partial class EffectGetDialog : Window
    {
        private readonly MainViewModel _vm;
        private readonly List<EffectDisplayItem> _allDisplayItems = new List<EffectDisplayItem>();
        private readonly ObservableCollection<EffectDisplayItem> _filteredItems = new ObservableCollection<EffectDisplayItem>();

        public EffectGetDialog(MainViewModel vm)
        {
            InitializeComponent();
            _vm = vm;

            EffectListBox.ItemsSource = _filteredItems;

            _vm.Client.OnEffectListReceived += OnRemoteEffectListReceived;
            _vm.PropertyChanged += OnVmPropertyChanged;
            Closed += (s, e) =>
            {
                _vm.Client.OnEffectListReceived -= OnRemoteEffectListReceived;
                _vm.PropertyChanged -= OnVmPropertyChanged;
            };

            LoadEffects();
        }

        private void OnRemoteEffectListReceived(List<EffectEntryDto> effects)
        {
            Dispatcher.Invoke(() =>
            {
                LoadEffects(effects);
            });
        }

        private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.AllEffects))
            {
                Dispatcher.Invoke(() => LoadEffects());
            }
        }

        private void LoadEffects(List<EffectEntryDto>? effects = null)
        {
            _allDisplayItems.Clear();

            effects ??= _vm.AllEffects;

            if (effects != null && effects.Count > 0)
            {
                foreach (var ef in effects)
                {
                    // 无等级（上限0）：只显示原名一行，Level=1（内部0）
                    if (ef.MaxLevel <= 0)
                    {
                        _allDisplayItems.Add(new EffectDisplayItem
                        {
                            Id = ef.Id,
                            Key = ef.Key,
                            Name = ef.Name,
                            Level = 1,
                            MaxLevel = 0
                        });
                        continue;
                    }

                    // 有等级：行号 Lv.1(内部0) .. Lv.{MaxLevel+1}(内部MaxLevel)
                    int rowCount = ef.MaxLevel + 1;

                    // 等级上限 > 10 的效果默认折叠成一组头（全选时仍会选中其全部等级行）
                    EffectDisplayItem? header = null;
                    if (ef.MaxLevel > 10)
                    {
                        header = new EffectDisplayItem
                        {
                            Id = ef.Id,
                            Key = ef.Key,
                            Name = ef.Name,
                            Level = -1,
                            MaxLevel = ef.MaxLevel,
                            IsGroupHeader = true,
                            IsExpanded = false
                        };
                        _allDisplayItems.Add(header);
                    }

                    for (int k = 0; k < rowCount; k++)
                    {
                        _allDisplayItems.Add(new EffectDisplayItem
                        {
                            Id = ef.Id,
                            Key = ef.Key,
                            Name = ef.Name,
                            Level = k + 1,       // 行号 = 内部等级 + 1
                            MaxLevel = ef.MaxLevel,
                            Group = header       // 属于折叠组（null = 不折叠）
                        });
                    }
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

            // 文本搜索 (支持匹配中文名（含 Lv 后缀）、SER 枚举名)
            if (!string.IsNullOrEmpty(query))
            {
                filtered = filtered.Where(x =>
                    (x.Name != null && x.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.DisplayName != null && x.DisplayName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (x.Key != null && x.Key.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                );
            }

            _filteredItems.Clear();
            foreach (var item in filtered)
            {
                _filteredItems.Add(item);
            }

            SubtitleText.Text = $"符合条件: {_filteredItems.Count} / {_allDisplayItems.Count} 个效果";

            if (_filteredItems.Count == 0)
            {
                EmptyStatePanel.Visibility = Visibility.Visible;
                EffectListBox.Visibility = Visibility.Collapsed;
                EmptyStateText.Text = _allDisplayItems.Count == 0
                    ? "正在从游戏获取效果列表，请稍候..."
                    : "暂无符合条件的效果";
            }
            else
            {
                EmptyStatePanel.Visibility = Visibility.Collapsed;
                EffectListBox.Visibility = Visibility.Visible;
                if (EffectListBox.SelectedItem == null)
                {
                    EffectListBox.SelectedIndex = 0;
                }
            }
        }

        private int GetCurrentDurationSeconds()
        {
            if (int.TryParse(DurationBox.Text?.Trim(), out int s) && s > 0)
            {
                return s > 9999 ? 9999 : s;
            }
            return 30;
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void OnSearchBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (EffectListBox.SelectedItem is EffectDisplayItem selected)
                {
                    ApplyEffect(selected);
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Down)
            {
                EffectListBox.Focus();
                e.Handled = true;
            }
        }

        private void OnDurationPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void OnClearSearchClick(object sender, RoutedEventArgs e)
        {
            SearchBox.Text = "";
            SearchBox.Focus();
        }

        private void OnRefreshEffectsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _vm.Client.RequestEffectList();
            }
            catch { }

            LoadEffects();
        }

        private void OnEffectDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (EffectListBox.SelectedItem is EffectDisplayItem selected)
            {
                ApplyEffect(selected);
            }
        }

        private void OnAddEffectButtonClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is EffectDisplayItem item)
            {
                if (item.IsGroupHeader)
                {
                    item.Toggle();   // 折叠组头：点按钮展开/收起
                }
                else
                {
                    ApplyEffect(item);
                }
            }
        }

        private void OnAddSelectedClick(object sender, RoutedEventArgs e)
        {
            var selected = EffectListBox.SelectedItems.Cast<EffectDisplayItem>()
                .Where(x => x.Level >= 1)   // 跳过折叠组头
                .ToList();
            if (selected.Count == 0)
            {
                MessageBox.Show("请先在列表中选中要添加的效果！（Ctrl/Shift 可多选）", "未选择效果", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int seconds = GetCurrentDurationSeconds();

            // 同一效果被选中多种等级时，按最高等级添加一次，而不是全部塞给 MOD
            var grouped = selected
                .GroupBy(x => x.Id)
                .Select(g => new { Id = g.Key, Level = g.Max(x => x.Level) });

            foreach (var g in grouped)
            {
                _vm.ExecuteApplyEffect(g.Id, g.Level, seconds);
            }
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            EffectListBox.SelectedItems.Clear();
            // 全选包含折叠组内的全部等级行（布局折叠不影响选择）
            foreach (var item in _filteredItems)
            {
                if (item.Level >= 1)
                {
                    EffectListBox.SelectedItems.Add(item);
                }
            }
            EffectListBox.Focus();
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 已选项数按效果去重统计（同效果多等级算一项，忽略组头）
            int count = EffectListBox.SelectedItems.Cast<EffectDisplayItem>()
                .Where(x => x.Level >= 1)
                .Select(x => x.Id).Distinct().Count();
            SelectedCountText.Text = $"已选 {count} 项";
        }

        private void ApplyEffect(EffectDisplayItem item)
        {
            if (item == null || item.Id < 0) return;

            int seconds = GetCurrentDurationSeconds();
            _vm.ExecuteApplyEffect(item.Id, item.Level, seconds);
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

    public class EffectDisplayItem : INotifyPropertyChanged
    {
        public int Id { get; set; } = -1;
        public string Key { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Level { get; set; } = 1;      // 行号：无等级=1；有等级=内部+1；组头=-1
        public int MaxLevel { get; set; } = 0;   // 该效果最高内部等级（0 = 无等级）
        public bool IsGroupHeader { get; set; } = false;   // 是否折叠组头（上限>10的效果）
        public bool IsExpanded { get; set; } = false;      // 组头是否展开
        public EffectDisplayItem? Group { get; set; } = null;  // 等级行归属的组头（null = 不折叠）

        // 组头: "蛛网 · 等级 1~99 ▸/▾"；有等级行: "效果名 Lv.X"；无等级: 原名
        public string DisplayName =>
            IsGroupHeader ? $"{Name} · 等级 1~{MaxLevel + 1} {(IsExpanded ? "▾" : "▸")}"
            : MaxLevel > 0 ? $"{Name} Lv.{Level}" : Name;

        public string KeyDisplay =>
            IsGroupHeader ? "" : (string.IsNullOrEmpty(Key) ? "" : $"SER: {Key}");

        public string ExpandCollapseText => IsExpanded ? "折叠 ▾" : "展开 ▸";

        public Brush NameColor =>
            IsGroupHeader ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C084FC"))
            : MaxLevel > 0 ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00F0FF"))
            : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E5E7EB"));

        public void Toggle()
        {
            IsExpanded = !IsExpanded;
            OnPropertyChanged(nameof(IsExpanded));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(ExpandCollapseText));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
