using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AICShared;
using AICTrainer.Injector;
using AICTrainer.Services;

namespace AICTrainer.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public ProcessMonitor Monitor { get; }
        public TrainerClient Client { get; }
        public ModConfigDto Config { get; } = new ModConfigDto();

        private bool _isGameRunning;
        private bool _isInjected;
        private string _statusText = "未检测到游戏运行";
        private string _statusColor = "#FF5555"; // 红色

        // 状态与原值
        private string _gameVersionText = "游戏未运行";
        private string _patchHealthText = "等待连接";
        private string _currentMapText = "未在游戏中";
        private int _rawHp = 100, _rawMaxHp = 100;
        private int _rawMp = 100, _rawMaxMp = 100;
        private string _currentHpText = "-- / --";
        private string _currentMpText = "-- / --";
        private string _currentMpCrackText = "--";
        private string _currentInventoryText = "--";
        private string _currentSatietyText = "-- / --";
        private string _currentMaxSatietyText = "--";
        private string _currentEpText = "--";
        private string _currentWalkSpeedText = "--";
        private string _currentRunSpeedText = "--";
        private string _currentGripText = "--";
        private string _currentKnockbackText = "--";
        private string _currentSlotText = "--";
        private string _currentDangerText = "--";
        private int _rawGold, _rawCrafts, _rawJuice, _rawBarScore, _rawGuildPoints, _rawLanthanum;
        private string _currentGoldText = "--";
        private string _currentCraftsText = "--";
        private string _currentJuiceText = "--";
        private string _currentBarScoreText = "--";
        private string _currentGuildPointsText = "--";
        private string _currentLanthanumText = "--";

        // 分类与收藏系统
        private int _selectedCategoryIndex = 0; // 0:全部, 1:收藏, 2:属性生存, 3:角色机动, 4:战斗增强, 5:地形免疫, 6:地图世界, 7:辅助便利, 8:资产货币
        private readonly HashSet<string> _favoriteKeys = new HashSet<string>();

        public MainViewModel()
        {
            try
            {
                var settings = TrainerSettings.Load();
                if (settings.Favorites != null && settings.Favorites.Count > 0)
                {
                    foreach (var fav in settings.Favorites) _favoriteKeys.Add(fav);
                }
                else
                {
                    string[] defs = { "Hp", "Mp", "OneHitKill", "InfiniteJump", "ShieldBreak" };
                    foreach (var d in defs) _favoriteKeys.Add(d);
                }
            }
            catch
            {
                string[] defs = { "Hp", "Mp", "OneHitKill", "InfiniteJump", "ShieldBreak" };
                foreach (var d in defs) _favoriteKeys.Add(d);
            }

            Monitor = new ProcessMonitor();
            Client = new TrainerClient();

            // 命令初始化
            LaunchGameCommand = new RelayCommand(async () => await LaunchGameAsync());
            InjectCommand = new RelayCommand(async () => await InjectAndConnectAsync());
            ToggleFavCommand = new RelayCommand<string>(key => ToggleFavorite(key));

            // 单次更改数值动作命令
            ApplyHpCommand = new RelayCommand(() => Client.SendAction("set_hp", TargetHp > 0 ? TargetHp : _rawMaxHp));
            ApplyMpCommand = new RelayCommand(() => Client.SendAction("set_mp", TargetMp > 0 ? TargetMp : _rawMaxMp));
            ApplyMpCrackCommand = new RelayCommand(() => Client.SendAction("set_mp_crack", TargetMpCrack));
            ApplyInventoryCommand = new RelayCommand(() => Client.SendAction("set_inventory", TargetInventoryCapacity));
            ApplyMaxSatietyCommand = new RelayCommand(() => Client.SendAction("set_max_satiety", TargetMaxSatiety));
            ApplySatietyCommand = new RelayCommand(() => Client.SendAction("set_satiety", 0, TargetSatiety));
            ApplyEpCommand = new RelayCommand(() => Client.SendAction("set_ep", TargetEp));
            ApplyWalkSpeedCommand = new RelayCommand(() => Client.SendAction("set_walk_speed", 0, WalkSpeedMultiplier));
            ApplyRunSpeedCommand = new RelayCommand(() => Client.SendAction("set_run_speed", 0, RunSpeedMultiplier));
            ApplyGripCommand = new RelayCommand(() => Client.SendAction("set_grip", 0, TargetGrip));
            ApplyKnockbackCommand = new RelayCommand(() => Client.SendAction("set_knockback", TargetKnockbackTime));
            ApplySlotCommand = new RelayCommand(() => Client.SendAction("set_slot", TargetSlotCapacity));
            ApplyDangerCommand = new RelayCommand(() => Client.SendAction("set_danger", TargetDangerLevel));
            ApplyGoldCommand = new RelayCommand(() => Client.SendAction("set_gold", TargetGold));
            ApplyCraftsCommand = new RelayCommand(() => Client.SendAction("set_crafts", TargetCrafts));
            ApplyJuiceCommand = new RelayCommand(() => Client.SendAction("set_juice", TargetJuice));
            ApplyBarScoreCommand = new RelayCommand(() => Client.SendAction("set_bar_score", TargetBarScore));
            ApplyGuildPointsCommand = new RelayCommand(() => Client.SendAction("set_guild_points", TargetGuildPoints));
            ApplyLanthanumCommand = new RelayCommand(() => Client.SendAction("set_lanthanum", TargetLanthanum));

            // 一键捷径命令
            QuickHealCommand = new RelayCommand(() => Client.SendAction("full_heal"));
            QuickCureCracksCommand = new RelayCommand(() => Client.SendAction("cure_cracks"));
            QuickCureSerCommand = new RelayCommand(() => Client.SendAction("cure_ser"));
            QuickClearSatietyCommand = new RelayCommand(() => Client.SendAction("clear_satiety"));
            QuickClearEpCommand = new RelayCommand(() => Client.SendAction("clear_ep"));

            Monitor.OnGameDetected += proc =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsGameRunning = true;
                    GameVersionText = $"版本: {Monitor.GameFileVersion}";
                    if (!IsInjected)
                    {
                        StatusText = $"游戏已运行 (PID: {proc.Id}) - 请点击【⚡ 注入 / 连接】";
                        StatusColor = "#FFAA00";
                    }
                });
            };

            Monitor.OnGameExited += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsGameRunning = false;
                    IsInjected = false;
                    GameVersionText = "游戏未运行";
                    PatchHealthText = "等待连接";
                    StatusText = "未检测到游戏运行";
                    StatusColor = "#FF5555";
                    CurrentHpText = "-- / --";
                    CurrentMpText = "-- / --";
                    CurrentMpCrackText = "--";
                    CurrentInventoryText = "--";
                    CurrentSatietyText = "-- / --";
                    CurrentMaxSatietyText = "--";
                    CurrentEpText = "--";
                    CurrentWalkSpeedText = "--";
                    CurrentRunSpeedText = "--";
                    CurrentGripText = "--";
                    CurrentKnockbackText = "--";
                    CurrentSlotText = "--";
                    CurrentDangerText = "--";
                    CurrentGoldText = "--";
                    CurrentCraftsText = "--";
                    CurrentJuiceText = "--";
                    CurrentBarScoreText = "--";
                    CurrentGuildPointsText = "--";
                    CurrentLanthanumText = "--";
                    CurrentMapText = "未在游戏中";
                });
            };

            Client.OnConnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsInjected = true;
                    StatusText = "已注入并连接游戏 (实时数据同步生效)";
                    StatusColor = "#00FF66";
                    PushConfig();
                });
            };

            Client.OnDisconnected += () =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    IsInjected = false;
                    if (IsGameRunning)
                    {
                        StatusText = "连接已断开 (可点击【⚡ 注入 / 连接】重新连接)";
                        StatusColor = "#FFAA00";
                    }
                });
            };

            Client.OnStateReceived += state =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (!string.IsNullOrEmpty(state.GameVersion))
                    {
                        GameVersionText = $"版本: {state.GameVersion}";
                    }
                    if (state.TotalPatchCount > 0)
                    {
                        PatchHealthText = $"{state.ActivePatchCount}/{state.TotalPatchCount} 补丁生效";
                    }

                    if (state.IsGameReady)
                    {
                        _rawHp = state.Hp; _rawMaxHp = state.MaxHp;
                        _rawMp = state.Mp; _rawMaxMp = state.MaxMp;
                        CurrentHpText = $"{state.Hp} / {state.MaxHp}";
                        CurrentMpText = $"{state.Mp} / {state.MaxMp}";
                        CurrentMpCrackText = $"{state.MpCrackCount} / 21";
                        CurrentInventoryText = $"{state.InventoryCapacity} 格";
                        CurrentSatietyText = $"{state.Satiety:F1} / {state.MaxSatiety}";
                        CurrentMaxSatietyText = state.MaxSatiety.ToString();
                        CurrentEpText = $"{state.Ep} / 1000";
                        CurrentWalkSpeedText = $"{state.WalkSpeed:F3}";
                        CurrentRunSpeedText = $"{state.RunSpeed:F3}";
                        CurrentGripText = $"{state.Grip:F3}";
                        CurrentKnockbackText = $"{state.KnockbackTime} 帧";
                        CurrentSlotText = $"{state.SlotCapacity} 槽";
                        CurrentDangerText = $"{state.DangerLevel}";
                        _rawGold = state.Gold;
                        _rawCrafts = state.Crafts;
                        _rawJuice = state.Juice;
                        _rawBarScore = state.BarScore;
                        _rawGuildPoints = state.GuildPoints;
                        _rawLanthanum = state.Lanthanum;
                        CurrentGoldText = state.Gold.ToString();
                        CurrentCraftsText = state.Crafts.ToString();
                        CurrentJuiceText = state.Juice.ToString();
                        CurrentBarScoreText = state.BarScore.ToString();
                        CurrentGuildPointsText = state.GuildPoints.ToString();
                        CurrentLanthanumText = state.Lanthanum.ToString();
                        CurrentMapText = string.IsNullOrEmpty(state.CurrentMap) ? "关卡载入中" : state.CurrentMap;
                    }
                    else
                    {
                        CurrentMapText = "标题画面 / 菜单";
                    }
                });
            };

            Monitor.Start();
        }

        // ======================= 基础状态绑定 =======================
        public string GameVersionText { get => _gameVersionText; set { _gameVersionText = value; OnPropertyChanged(); } }
        public string PatchHealthText { get => _patchHealthText; set { _patchHealthText = value; OnPropertyChanged(); } }
        public bool IsGameRunning
        {
            get => _isGameRunning;
            set
            {
                if (_isGameRunning != value)
                {
                    _isGameRunning = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(LaunchGameButtonVis));
                }
            }
        }
        public Visibility LaunchGameButtonVis => IsGameRunning ? Visibility.Collapsed : Visibility.Visible;
        public bool IsInjected { get => _isInjected; set { _isInjected = value; OnPropertyChanged(); } }
        public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }
        public string StatusColor { get => _statusColor; set { _statusColor = value; OnPropertyChanged(); } }
        public string CurrentMapText { get => _currentMapText; set { _currentMapText = value; OnPropertyChanged(); } }

        public string CurrentHpText { get => _currentHpText; set { _currentHpText = value; OnPropertyChanged(); } }
        public string CurrentMpText { get => _currentMpText; set { _currentMpText = value; OnPropertyChanged(); } }
        public string CurrentMpCrackText { get => _currentMpCrackText; set { _currentMpCrackText = value; OnPropertyChanged(); } }
        public string CurrentInventoryText { get => _currentInventoryText; set { _currentInventoryText = value; OnPropertyChanged(); } }
        public string CurrentSatietyText { get => _currentSatietyText; set { _currentSatietyText = value; OnPropertyChanged(); } }
        public string CurrentMaxSatietyText { get => _currentMaxSatietyText; set { _currentMaxSatietyText = value; OnPropertyChanged(); } }
        public string CurrentEpText { get => _currentEpText; set { _currentEpText = value; OnPropertyChanged(); } }
        public string CurrentWalkSpeedText { get => _currentWalkSpeedText; set { _currentWalkSpeedText = value; OnPropertyChanged(); } }
        public string CurrentRunSpeedText { get => _currentRunSpeedText; set { _currentRunSpeedText = value; OnPropertyChanged(); } }
        public string CurrentGripText { get => _currentGripText; set { _currentGripText = value; OnPropertyChanged(); } }
        public string CurrentKnockbackText { get => _currentKnockbackText; set { _currentKnockbackText = value; OnPropertyChanged(); } }
        public string CurrentSlotText { get => _currentSlotText; set { _currentSlotText = value; OnPropertyChanged(); } }
        public string CurrentDangerText { get => _currentDangerText; set { _currentDangerText = value; OnPropertyChanged(); } }
        public string CurrentGoldText { get => _currentGoldText; set { _currentGoldText = value; OnPropertyChanged(); } }
        public string CurrentCraftsText { get => _currentCraftsText; set { _currentCraftsText = value; OnPropertyChanged(); } }
        public string CurrentJuiceText { get => _currentJuiceText; set { _currentJuiceText = value; OnPropertyChanged(); } }
        public string CurrentBarScoreText { get => _currentBarScoreText; set { _currentBarScoreText = value; OnPropertyChanged(); } }
        public string CurrentGuildPointsText { get => _currentGuildPointsText; set { _currentGuildPointsText = value; OnPropertyChanged(); } }
        public string CurrentLanthanumText { get => _currentLanthanumText; set { _currentLanthanumText = value; OnPropertyChanged(); } }

        // ======================= 分类与收藏系统 =======================
        public int SelectedCategoryIndex
        {
            get => _selectedCategoryIndex;
            set
            {
                if (_selectedCategoryIndex != value)
                {
                    _selectedCategoryIndex = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(FavEmptyHintVis));
                    OnPropertyChanged(nameof(CategoryTitleText));
                    OnPropertyChanged(nameof(CategoryDescText));
                    OnPropertyChanged(nameof(CategoryBannerIcon));
                    OnPropertyChanged(nameof(CategoryActiveStatsText));
                    OnPropertyChanged(nameof(IsAllOrFavCategoryVis));
                    OnPropertyChanged(nameof(SectionVis_Survival));
                    OnPropertyChanged(nameof(SectionVis_Mobility));
                    OnPropertyChanged(nameof(SectionVis_Combat));
                    OnPropertyChanged(nameof(SectionVis_Hazards));
                    OnPropertyChanged(nameof(SectionVis_World));
                    OnPropertyChanged(nameof(SectionVis_Utility));
                    OnPropertyChanged(nameof(SectionVis_Currencies));
                    RefreshAllCardVisibilities();
                }
            }
        }

        public bool HasFavorites => _favoriteKeys.Count > 0;
        public Visibility FavEmptyHintVis => (SelectedCategoryIndex == 1 && !HasFavorites) ? Visibility.Visible : Visibility.Collapsed;

        public string CategoryTitleText => SelectedCategoryIndex switch
        {
            0 => "全部功能清单",
            1 => "我的收藏夹",
            2 => "属性与生存",
            3 => "移动与物理",
            4 => "战斗强化",
            5 => "环境免疫",
            6 => "地图与世界",
            7 => "辅助与便利",
            8 => "资产与货币",
            _ => "功能分类"
        };

        public string CategoryDescText => SelectedCategoryIndex switch
        {
            0 => "展示修改器支持的全部核心内存修改与行为锁定功能",
            1 => "点击卡片右上角【★ 收藏】星标，可将常用功能固定在此快速调整",
            2 => "实时调整生命、法力、量条破损格数、饱食度与兴奋度状态",
            3 => "控制诺艾尔行走/奔跑速度、地面附着摩擦力与受击硬直韧性",
            4 => "战斗伤害倍率结算、一击秒杀与敌对生物攻击意图控制",
            5 => "针对毒虫荆棘陷阱、深水溺水窒息与极寒结冰等环境伤害进行免疫",
            6 => "全图迷雾直接驱散点亮与关卡危险等级快速调节",
            7 => "背包扩展、装备槽上限解锁、游戏原生控制台与日志监测",
            8 => "自由调整与锁定金币、工坊代币、药饮代币、酒馆积分、公会点数及强化镧石",
            _ => ""
        };

        public System.Windows.Media.ImageSource? CategoryBannerIcon => SelectedCategoryIndex switch
        {
            0 => StickerManager.Get("0_4"),
            1 => StickerManager.Get("0_1"),
            2 => StickerManager.Get("0_1"),
            3 => StickerManager.Get("2_3"),
            4 => StickerManager.Get("2_4"),
            5 => StickerManager.Get("3_5"),
            6 => StickerManager.Get("0_4"),
            7 => StickerManager.Get("0_7"),
            8 => StickerManager.Get("0_2"),
            _ => StickerManager.Get("0_1")
        };

        public string CategoryActiveStatsText
        {
            get
            {
                int active = 0;
                int total = 0;
                switch (SelectedCategoryIndex)
                {
                    case 0: // 全部
                        total = 40;
                        if (IsHpLocked) active++;
                        if (IsMpLocked) active++;
                        if (IsMpCrackLocked) active++;
                        if (IsInventoryLocked) active++;
                        if (IsMaxSatietyLocked) active++;
                        if (IsSatietyLocked) active++;
                        if (IsEpLocked) active++;
                        if (IsImmuneAbnormalStatus) active++;
                        if (IsWalkSpeedLocked) active++;
                        if (IsRunSpeedLocked) active++;
                        if (IsGripLocked) active++;
                        if (IsNoSlipEnabled) active++;
                        if (IsKnockbackLocked) active++;
                        if (IsInfiniteJump) active++;
                        if (IsOneHitKill) active++;
                        if (IsShieldNeverBreak) active++;
                        if (IsInstantMagicCharge) active++;
                        if (IsNoBurstTired) active++;
                        if (IsJustGuardNoHit) active++;
                        if (IsExtendedJustGuard) active++;
                        if (IsNoWormTrap) active++;
                        if (IsNoSpikeDamage) active++;
                        if (IsNoThunderDamage) active++;
                        if (IsNoDrownDamage) active++;
                        if (IsNoAcidDamage) active++;
                        if (IsFastTravelAnytime) active++;
                        if (IsFastTravelAnywhere) active++;
                        if (IsDangerLocked) active++;
                        if (IsAlwaysHungryBonus) active++;
                        if (IsSaveAnywhere) active++;
                        if (IsFreezeCountdown) active++;
                        if (IsSlotLocked) active++;
                        if (IsBreakFoodAttrLimit) active++;
                        if (IsMosaicDisabled) active++;
                        if (IsGoldLocked) active++;
                        if (IsCraftsLocked) active++;
                        if (IsJuiceLocked) active++;
                        if (IsBarScoreLocked) active++;
                        if (IsGuildPointsLocked) active++;
                        if (IsLanthanumLocked) active++;
                        return $"已激活 {active} / {total} 项";
                    case 1: // 收藏
                        return $"共收藏 {_favoriteKeys.Count} 项";
                    case 2: // 生存
                        total = 8;
                        if (IsHpLocked) active++;
                        if (IsMpLocked) active++;
                        if (IsMpCrackLocked) active++;
                        if (IsInventoryLocked) active++;
                        if (IsMaxSatietyLocked) active++;
                        if (IsSatietyLocked) active++;
                        if (IsEpLocked) active++;
                        if (IsImmuneAbnormalStatus) active++;
                        return $"已激活 {active} / {total} 项";
                    case 3: // 移动
                        total = 6;
                        if (IsWalkSpeedLocked) active++;
                        if (IsRunSpeedLocked) active++;
                        if (IsGripLocked) active++;
                        if (IsNoSlipEnabled) active++;
                        if (IsKnockbackLocked) active++;
                        if (IsInfiniteJump) active++;
                        return $"已激活 {active} / {total} 项";
                    case 4: // 战斗
                        total = 6;
                        if (IsOneHitKill) active++;
                        if (IsShieldNeverBreak) active++;
                        if (IsInstantMagicCharge) active++;
                        if (IsNoBurstTired) active++;
                        if (IsJustGuardNoHit) active++;
                        if (IsExtendedJustGuard) active++;
                        return $"已激活 {active} / {total} 项";
                    case 5: // 环境
                        total = 5;
                        if (IsNoWormTrap) active++;
                        if (IsNoSpikeDamage) active++;
                        if (IsNoThunderDamage) active++;
                        if (IsNoDrownDamage) active++;
                        if (IsNoAcidDamage) active++;
                        return $"已激活 {active} / {total} 项";
                    case 6: // 世界
                        total = 4;
                        if (IsFastTravelAnytime) active++;
                        if (IsFastTravelAnywhere) active++;
                        if (IsDangerLocked) active++;
                        if (IsAlwaysHungryBonus) active++;
                        return $"已激活 {active} / {total} 项";
                    case 7: // 辅助
                        total = 5;
                        if (IsSaveAnywhere) active++;
                        if (IsFreezeCountdown) active++;
                        if (IsSlotLocked) active++;
                        if (IsBreakFoodAttrLimit) active++;
                        if (IsMosaicDisabled) active++;
                        return $"已激活 {active} / {total} 项";
                    case 8: // 资产与货币
                        total = 6;
                        if (IsGoldLocked) active++;
                        if (IsCraftsLocked) active++;
                        if (IsJuiceLocked) active++;
                        if (IsBarScoreLocked) active++;
                        if (IsGuildPointsLocked) active++;
                        if (IsLanthanumLocked) active++;
                        return $"已激活 {active} / {total} 项";
                    default:
                        return "";
                }
            }
        }

        // 分类板块可见性与子标题可见性
        public Visibility IsAllOrFavCategoryVis => (SelectedCategoryIndex == 0 || SelectedCategoryIndex == 1) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility SectionVis_Survival => ShouldShowSection("Survival", 2);
        public Visibility SectionVis_Mobility => ShouldShowSection("Mobility", 3);
        public Visibility SectionVis_Combat => ShouldShowSection("Combat", 4);
        public Visibility SectionVis_Hazards => ShouldShowSection("Hazards", 5);
        public Visibility SectionVis_World => ShouldShowSection("World", 6);
        public Visibility SectionVis_Utility => ShouldShowSection("Utility", 7);
        public Visibility SectionVis_Currencies => ShouldShowSection("Currencies", 8);

        public bool HasCategoryFavorites(string category)
        {
            var keys = category switch
            {
                "Survival" => new[] { "Hp", "Mp", "MpCrack", "Inventory", "MaxSatiety", "Satiety", "Ep", "ImmuneStatus" },
                "Mobility" => new[] { "WalkSpeed", "RunSpeed", "Grip", "NoSlip", "Knockback", "InfiniteJump" },
                "Combat" => new[] { "OneHitKill", "ShieldBreak", "InstantMagicCharge", "NoBurstTired", "JustGuardNoHit", "ExtendedJustGuard" },
                "Hazards" => new[] { "Worm", "Spike", "Thunder", "Drown", "Acid" },
                "World" => new[] { "FastTravelAnytime", "FastTravelAnywhere", "Danger", "HungryBonus" },
                "Utility" => new[] { "SaveAnywhere", "Countdown", "Slot", "BreakFood", "Mosaic" },
                "Currencies" => new[] { "Gold", "Crafts", "Juice", "BarScore", "GuildPoints", "Lanthanum" },
                _ => Array.Empty<string>()
            };
            foreach (var k in keys)
            {
                if (IsFav(k)) return true;
            }
            return false;
        }

        private Visibility ShouldShowSection(string category, int categoryIndex)
        {
            if (SelectedCategoryIndex == 0) return Visibility.Visible;
            if (SelectedCategoryIndex == categoryIndex) return Visibility.Visible;
            if (SelectedCategoryIndex == 1)
            {
                return HasCategoryFavorites(category) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public bool IsFav(string key) => _favoriteKeys.Contains(key);
        public string StarChar(string key) => IsFav(key) ? "★" : "☆";
        public string StarText(string key) => IsFav(key) ? "已收藏" : "收藏";
        public string StarColor(string key) => IsFav(key) ? "#FFD700" : "#71717A";
        public string StarTextColor(string key) => IsFav(key) ? "#FDE047" : "#9CA3AF";
        public string StarBg(string key) => IsFav(key) ? "#2D2410" : "#1B1B26";
        public string StarBorder(string key) => IsFav(key) ? "#EAB308" : "#2F2F44";

        public void ToggleFavorite(string? key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_favoriteKeys.Contains(key)) _favoriteKeys.Remove(key);
            else _favoriteKeys.Add(key);

            try
            {
                var s = TrainerSettings.Load();
                s.Favorites = new List<string>(_favoriteKeys);
                s.Save();
            }
            catch { }

            OnPropertyChanged($"IsFav_{key}");
            OnPropertyChanged($"StarChar_{key}");
            OnPropertyChanged($"StarText_{key}");
            OnPropertyChanged($"StarColor_{key}");
            OnPropertyChanged($"StarTextColor_{key}");
            OnPropertyChanged($"StarBg_{key}");
            OnPropertyChanged($"StarBorder_{key}");
            OnPropertyChanged(nameof(HasFavorites));
            OnPropertyChanged(nameof(FavEmptyHintVis));
            OnPropertyChanged(nameof(CategoryActiveStatsText));
            OnPropertyChanged(nameof(SectionVis_Survival));
            OnPropertyChanged(nameof(SectionVis_Mobility));
            OnPropertyChanged(nameof(SectionVis_Combat));
            OnPropertyChanged(nameof(SectionVis_Hazards));
            OnPropertyChanged(nameof(SectionVis_World));
            OnPropertyChanged(nameof(SectionVis_Utility));
            OnPropertyChanged(nameof(SectionVis_Currencies));
            RefreshAllCardVisibilities();
        }

        private Visibility CardVis(string category, string key)
        {
            if (SelectedCategoryIndex == 1)
            {
                return IsFav(key) ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Visible;
        }

        // 统一刷新所有卡片可见性与星标
        public void RefreshAllCardVisibilities()
        {
            string[] cardProps = new[]
            {
                "Vis_Hp", "Vis_Mp", "Vis_MpCrack", "Vis_Inventory", "Vis_MaxSatiety", "Vis_Satiety", "Vis_Ep", "Vis_ImmuneStatus",
                "Vis_WalkSpeed", "Vis_RunSpeed", "Vis_Grip", "Vis_NoSlip", "Vis_Knockback", "Vis_InfiniteJump",
                "Vis_OneHitKill", "Vis_ShieldBreak", "Vis_InstantMagicCharge", "Vis_NoBurstTired", "Vis_JustGuardNoHit", "Vis_ExtendedJustGuard",
                "Vis_Worm", "Vis_Spike", "Vis_Thunder", "Vis_Drown", "Vis_Acid",
                "Vis_FastTravelAnytime", "Vis_FastTravelAnywhere", "Vis_Danger", "Vis_HungryBonus",
                "Vis_SaveAnywhere", "Vis_Countdown", "Vis_Slot", "Vis_BreakFood", "Vis_Mosaic",
                "Vis_Gold", "Vis_Crafts", "Vis_Juice", "Vis_BarScore", "Vis_GuildPoints", "Vis_Lanthanum"
            };
            foreach (var p in cardProps) OnPropertyChanged(p);
        }

        // ======================= 卡片可见性属性绑定 =======================
        // 生存类
        public Visibility Vis_Hp => CardVis("Survival", "Hp");
        public Visibility Vis_Mp => CardVis("Survival", "Mp");
        public Visibility Vis_MpCrack => CardVis("Survival", "MpCrack");
        public Visibility Vis_Inventory => CardVis("Survival", "Inventory");
        public Visibility Vis_MaxSatiety => CardVis("Survival", "MaxSatiety");
        public Visibility Vis_Satiety => CardVis("Survival", "Satiety");
        public Visibility Vis_Ep => CardVis("Survival", "Ep");
        public Visibility Vis_ImmuneStatus => CardVis("Survival", "ImmuneStatus");

        // 机动类
        public Visibility Vis_WalkSpeed => CardVis("Mobility", "WalkSpeed");
        public Visibility Vis_RunSpeed => CardVis("Mobility", "RunSpeed");
        public Visibility Vis_Grip => CardVis("Mobility", "Grip");
        public Visibility Vis_NoSlip => CardVis("Mobility", "NoSlip");
        public Visibility Vis_Knockback => CardVis("Mobility", "Knockback");
        public Visibility Vis_InfiniteJump => CardVis("Mobility", "InfiniteJump");

        // 战斗类
        public Visibility Vis_OneHitKill => CardVis("Combat", "OneHitKill");
        public Visibility Vis_ShieldBreak => CardVis("Combat", "ShieldBreak");
        public Visibility Vis_InstantMagicCharge => CardVis("Combat", "InstantMagicCharge");
        public Visibility Vis_NoBurstTired => CardVis("Combat", "NoBurstTired");
        public Visibility Vis_JustGuardNoHit => CardVis("Combat", "JustGuardNoHit");
        public Visibility Vis_ExtendedJustGuard => CardVis("Combat", "ExtendedJustGuard");

        // 地形免疫类
        public Visibility Vis_Worm => CardVis("Hazards", "Worm");
        public Visibility Vis_Spike => CardVis("Hazards", "Spike");
        public Visibility Vis_Thunder => CardVis("Hazards", "Thunder");
        public Visibility Vis_Drown => CardVis("Hazards", "Drown");
        public Visibility Vis_Acid => CardVis("Hazards", "Acid");

        // 地图世界类
        public Visibility Vis_FastTravelAnytime => CardVis("World", "FastTravelAnytime");
        public Visibility Vis_FastTravelAnywhere => CardVis("World", "FastTravelAnywhere");
        public Visibility Vis_Danger => CardVis("World", "Danger");
        public Visibility Vis_HungryBonus => CardVis("World", "HungryBonus");

        // 辅助便利类
        public Visibility Vis_SaveAnywhere => CardVis("Utility", "SaveAnywhere");
        public Visibility Vis_Countdown => CardVis("Utility", "Countdown");
        public Visibility Vis_Slot => CardVis("Utility", "Slot");
        public Visibility Vis_BreakFood => CardVis("Utility", "BreakFood");
        public Visibility Vis_Mosaic => CardVis("Utility", "Mosaic");

        // 资产与货币类
        public Visibility Vis_Gold => CardVis("Currencies", "Gold");
        public Visibility Vis_Crafts => CardVis("Currencies", "Crafts");
        public Visibility Vis_Juice => CardVis("Currencies", "Juice");
        public Visibility Vis_BarScore => CardVis("Currencies", "BarScore");
        public Visibility Vis_GuildPoints => CardVis("Currencies", "GuildPoints");
        public Visibility Vis_Lanthanum => CardVis("Currencies", "Lanthanum");

        // 星标与颜色属性绑定助手 (胶囊背景、边框、大星标字符、状态文本与颜色)
        public bool IsFav_Hp => IsFav("Hp");
        public string StarChar_Hp => StarChar("Hp");
        public string StarText_Hp => StarText("Hp");
        public string StarColor_Hp => StarColor("Hp");
        public string StarTextColor_Hp => StarTextColor("Hp");
        public string StarBg_Hp => StarBg("Hp");
        public string StarBorder_Hp => StarBorder("Hp");
        public bool IsFav_Mp => IsFav("Mp");
        public string StarChar_Mp => StarChar("Mp");
        public string StarText_Mp => StarText("Mp");
        public string StarColor_Mp => StarColor("Mp");
        public string StarTextColor_Mp => StarTextColor("Mp");
        public string StarBg_Mp => StarBg("Mp");
        public string StarBorder_Mp => StarBorder("Mp");
        public bool IsFav_MpCrack => IsFav("MpCrack");
        public string StarChar_MpCrack => StarChar("MpCrack");
        public string StarText_MpCrack => StarText("MpCrack");
        public string StarColor_MpCrack => StarColor("MpCrack");
        public string StarTextColor_MpCrack => StarTextColor("MpCrack");
        public string StarBg_MpCrack => StarBg("MpCrack");
        public string StarBorder_MpCrack => StarBorder("MpCrack");
        public bool IsFav_Inventory => IsFav("Inventory");
        public string StarChar_Inventory => StarChar("Inventory");
        public string StarText_Inventory => StarText("Inventory");
        public string StarColor_Inventory => StarColor("Inventory");
        public string StarTextColor_Inventory => StarTextColor("Inventory");
        public string StarBg_Inventory => StarBg("Inventory");
        public string StarBorder_Inventory => StarBorder("Inventory");
        public bool IsFav_MaxSatiety => IsFav("MaxSatiety");
        public string StarChar_MaxSatiety => StarChar("MaxSatiety");
        public string StarText_MaxSatiety => StarText("MaxSatiety");
        public string StarColor_MaxSatiety => StarColor("MaxSatiety");
        public string StarTextColor_MaxSatiety => StarTextColor("MaxSatiety");
        public string StarBg_MaxSatiety => StarBg("MaxSatiety");
        public string StarBorder_MaxSatiety => StarBorder("MaxSatiety");
        public bool IsFav_Satiety => IsFav("Satiety");
        public string StarChar_Satiety => StarChar("Satiety");
        public string StarText_Satiety => StarText("Satiety");
        public string StarColor_Satiety => StarColor("Satiety");
        public string StarTextColor_Satiety => StarTextColor("Satiety");
        public string StarBg_Satiety => StarBg("Satiety");
        public string StarBorder_Satiety => StarBorder("Satiety");
        public bool IsFav_Ep => IsFav("Ep");
        public string StarChar_Ep => StarChar("Ep");
        public string StarText_Ep => StarText("Ep");
        public string StarColor_Ep => StarColor("Ep");
        public string StarTextColor_Ep => StarTextColor("Ep");
        public string StarBg_Ep => StarBg("Ep");
        public string StarBorder_Ep => StarBorder("Ep");
        public bool IsFav_ImmuneStatus => IsFav("ImmuneStatus");
        public string StarChar_ImmuneStatus => StarChar("ImmuneStatus");
        public string StarText_ImmuneStatus => StarText("ImmuneStatus");
        public string StarColor_ImmuneStatus => StarColor("ImmuneStatus");
        public string StarTextColor_ImmuneStatus => StarTextColor("ImmuneStatus");
        public string StarBg_ImmuneStatus => StarBg("ImmuneStatus");
        public string StarBorder_ImmuneStatus => StarBorder("ImmuneStatus");
        public bool IsFav_WalkSpeed => IsFav("WalkSpeed");
        public string StarChar_WalkSpeed => StarChar("WalkSpeed");
        public string StarText_WalkSpeed => StarText("WalkSpeed");
        public string StarColor_WalkSpeed => StarColor("WalkSpeed");
        public string StarTextColor_WalkSpeed => StarTextColor("WalkSpeed");
        public string StarBg_WalkSpeed => StarBg("WalkSpeed");
        public string StarBorder_WalkSpeed => StarBorder("WalkSpeed");
        public bool IsFav_RunSpeed => IsFav("RunSpeed");
        public string StarChar_RunSpeed => StarChar("RunSpeed");
        public string StarText_RunSpeed => StarText("RunSpeed");
        public string StarColor_RunSpeed => StarColor("RunSpeed");
        public string StarTextColor_RunSpeed => StarTextColor("RunSpeed");
        public string StarBg_RunSpeed => StarBg("RunSpeed");
        public string StarBorder_RunSpeed => StarBorder("RunSpeed");
        public bool IsFav_Grip => IsFav("Grip");
        public string StarChar_Grip => StarChar("Grip");
        public string StarText_Grip => StarText("Grip");
        public string StarColor_Grip => StarColor("Grip");
        public string StarTextColor_Grip => StarTextColor("Grip");
        public string StarBg_Grip => StarBg("Grip");
        public string StarBorder_Grip => StarBorder("Grip");
        public bool IsFav_NoSlip => IsFav("NoSlip");
        public string StarChar_NoSlip => StarChar("NoSlip");
        public string StarText_NoSlip => StarText("NoSlip");
        public string StarColor_NoSlip => StarColor("NoSlip");
        public string StarTextColor_NoSlip => StarTextColor("NoSlip");
        public string StarBg_NoSlip => StarBg("NoSlip");
        public string StarBorder_NoSlip => StarBorder("NoSlip");
        public bool IsFav_Knockback => IsFav("Knockback");
        public string StarChar_Knockback => StarChar("Knockback");
        public string StarText_Knockback => StarText("Knockback");
        public string StarColor_Knockback => StarColor("Knockback");
        public string StarTextColor_Knockback => StarTextColor("Knockback");
        public string StarBg_Knockback => StarBg("Knockback");
        public string StarBorder_Knockback => StarBorder("Knockback");
        public bool IsFav_InfiniteJump => IsFav("InfiniteJump");
        public string StarChar_InfiniteJump => StarChar("InfiniteJump");
        public string StarText_InfiniteJump => StarText("InfiniteJump");
        public string StarColor_InfiniteJump => StarColor("InfiniteJump");
        public string StarTextColor_InfiniteJump => StarTextColor("InfiniteJump");
        public string StarBg_InfiniteJump => StarBg("InfiniteJump");
        public string StarBorder_InfiniteJump => StarBorder("InfiniteJump");
        public bool IsFav_OneHitKill => IsFav("OneHitKill");
        public string StarChar_OneHitKill => StarChar("OneHitKill");
        public string StarText_OneHitKill => StarText("OneHitKill");
        public string StarColor_OneHitKill => StarColor("OneHitKill");
        public string StarTextColor_OneHitKill => StarTextColor("OneHitKill");
        public string StarBg_OneHitKill => StarBg("OneHitKill");
        public string StarBorder_OneHitKill => StarBorder("OneHitKill");
        public bool IsFav_ShieldBreak => IsFav("ShieldBreak");
        public string StarChar_ShieldBreak => StarChar("ShieldBreak");
        public string StarText_ShieldBreak => StarText("ShieldBreak");
        public string StarColor_ShieldBreak => StarColor("ShieldBreak");
        public string StarTextColor_ShieldBreak => StarTextColor("ShieldBreak");
        public string StarBg_ShieldBreak => StarBg("ShieldBreak");
        public string StarBorder_ShieldBreak => StarBorder("ShieldBreak");
        public bool IsFav_InstantMagicCharge => IsFav("InstantMagicCharge");
        public string StarChar_InstantMagicCharge => StarChar("InstantMagicCharge");
        public string StarText_InstantMagicCharge => StarText("InstantMagicCharge");
        public string StarColor_InstantMagicCharge => StarColor("InstantMagicCharge");
        public string StarTextColor_InstantMagicCharge => StarTextColor("InstantMagicCharge");
        public string StarBg_InstantMagicCharge => StarBg("InstantMagicCharge");
        public string StarBorder_InstantMagicCharge => StarBorder("InstantMagicCharge");
        public bool IsFav_NoBurstTired => IsFav("NoBurstTired");
        public string StarChar_NoBurstTired => StarChar("NoBurstTired");
        public string StarText_NoBurstTired => StarText("NoBurstTired");
        public string StarColor_NoBurstTired => StarColor("NoBurstTired");
        public string StarTextColor_NoBurstTired => StarTextColor("NoBurstTired");
        public string StarBg_NoBurstTired => StarBg("NoBurstTired");
        public string StarBorder_NoBurstTired => StarBorder("NoBurstTired");
        public bool IsFav_JustGuardNoHit => IsFav("JustGuardNoHit");
        public string StarChar_JustGuardNoHit => StarChar("JustGuardNoHit");
        public string StarText_JustGuardNoHit => StarText("JustGuardNoHit");
        public string StarColor_JustGuardNoHit => StarColor("JustGuardNoHit");
        public string StarTextColor_JustGuardNoHit => StarTextColor("JustGuardNoHit");
        public string StarBg_JustGuardNoHit => StarBg("JustGuardNoHit");
        public string StarBorder_JustGuardNoHit => StarBorder("JustGuardNoHit");
        public bool IsFav_ExtendedJustGuard => IsFav("ExtendedJustGuard");
        public string StarChar_ExtendedJustGuard => StarChar("ExtendedJustGuard");
        public string StarText_ExtendedJustGuard => StarText("ExtendedJustGuard");
        public string StarColor_ExtendedJustGuard => StarColor("ExtendedJustGuard");
        public string StarTextColor_ExtendedJustGuard => StarTextColor("ExtendedJustGuard");
        public string StarBg_ExtendedJustGuard => StarBg("ExtendedJustGuard");
        public string StarBorder_ExtendedJustGuard => StarBorder("ExtendedJustGuard");
        public bool IsFav_Worm => IsFav("Worm");
        public string StarChar_Worm => StarChar("Worm");
        public string StarText_Worm => StarText("Worm");
        public string StarColor_Worm => StarColor("Worm");
        public string StarTextColor_Worm => StarTextColor("Worm");
        public string StarBg_Worm => StarBg("Worm");
        public string StarBorder_Worm => StarBorder("Worm");
        public bool IsFav_Spike => IsFav("Spike");
        public string StarChar_Spike => StarChar("Spike");
        public string StarText_Spike => StarText("Spike");
        public string StarColor_Spike => StarColor("Spike");
        public string StarTextColor_Spike => StarTextColor("Spike");
        public string StarBg_Spike => StarBg("Spike");
        public string StarBorder_Spike => StarBorder("Spike");
        public bool IsFav_Thunder => IsFav("Thunder");
        public string StarChar_Thunder => StarChar("Thunder");
        public string StarText_Thunder => StarText("Thunder");
        public string StarColor_Thunder => StarColor("Thunder");
        public string StarTextColor_Thunder => StarTextColor("Thunder");
        public string StarBg_Thunder => StarBg("Thunder");
        public string StarBorder_Thunder => StarBorder("Thunder");
        public bool IsFav_Drown => IsFav("Drown");
        public string StarChar_Drown => StarChar("Drown");
        public string StarText_Drown => StarText("Drown");
        public string StarColor_Drown => StarColor("Drown");
        public string StarTextColor_Drown => StarTextColor("Drown");
        public string StarBg_Drown => StarBg("Drown");
        public string StarBorder_Drown => StarBorder("Drown");
        public bool IsFav_Acid => IsFav("Acid");
        public string StarChar_Acid => StarChar("Acid");
        public string StarText_Acid => StarText("Acid");
        public string StarColor_Acid => StarColor("Acid");
        public string StarTextColor_Acid => StarTextColor("Acid");
        public string StarBg_Acid => StarBg("Acid");
        public string StarBorder_Acid => StarBorder("Acid");
        public bool IsFav_FastTravelAnytime => IsFav("FastTravelAnytime");
        public string StarChar_FastTravelAnytime => StarChar("FastTravelAnytime");
        public string StarText_FastTravelAnytime => StarText("FastTravelAnytime");
        public string StarColor_FastTravelAnytime => StarColor("FastTravelAnytime");
        public string StarTextColor_FastTravelAnytime => StarTextColor("FastTravelAnytime");
        public string StarBg_FastTravelAnytime => StarBg("FastTravelAnytime");
        public string StarBorder_FastTravelAnytime => StarBorder("FastTravelAnytime");
        public bool IsFav_FastTravelAnywhere => IsFav("FastTravelAnywhere");
        public string StarChar_FastTravelAnywhere => StarChar("FastTravelAnywhere");
        public string StarText_FastTravelAnywhere => StarText("FastTravelAnywhere");
        public string StarColor_FastTravelAnywhere => StarColor("FastTravelAnywhere");
        public string StarTextColor_FastTravelAnywhere => StarTextColor("FastTravelAnywhere");
        public string StarBg_FastTravelAnywhere => StarBg("FastTravelAnywhere");
        public string StarBorder_FastTravelAnywhere => StarBorder("FastTravelAnywhere");
        public bool IsFav_Danger => IsFav("Danger");
        public string StarChar_Danger => StarChar("Danger");
        public string StarText_Danger => StarText("Danger");
        public string StarColor_Danger => StarColor("Danger");
        public string StarTextColor_Danger => StarTextColor("Danger");
        public string StarBg_Danger => StarBg("Danger");
        public string StarBorder_Danger => StarBorder("Danger");
        public bool IsFav_HungryBonus => IsFav("HungryBonus");
        public string StarChar_HungryBonus => StarChar("HungryBonus");
        public string StarText_HungryBonus => StarText("HungryBonus");
        public string StarColor_HungryBonus => StarColor("HungryBonus");
        public string StarTextColor_HungryBonus => StarTextColor("HungryBonus");
        public string StarBg_HungryBonus => StarBg("HungryBonus");
        public string StarBorder_HungryBonus => StarBorder("HungryBonus");
        public bool IsFav_SaveAnywhere => IsFav("SaveAnywhere");
        public string StarChar_SaveAnywhere => StarChar("SaveAnywhere");
        public string StarText_SaveAnywhere => StarText("SaveAnywhere");
        public string StarColor_SaveAnywhere => StarColor("SaveAnywhere");
        public string StarTextColor_SaveAnywhere => StarTextColor("SaveAnywhere");
        public string StarBg_SaveAnywhere => StarBg("SaveAnywhere");
        public string StarBorder_SaveAnywhere => StarBorder("SaveAnywhere");
        public bool IsFav_Countdown => IsFav("Countdown");
        public string StarChar_Countdown => StarChar("Countdown");
        public string StarText_Countdown => StarText("Countdown");
        public string StarColor_Countdown => StarColor("Countdown");
        public string StarTextColor_Countdown => StarTextColor("Countdown");
        public string StarBg_Countdown => StarBg("Countdown");
        public string StarBorder_Countdown => StarBorder("Countdown");
        public bool IsFav_Slot => IsFav("Slot");
        public string StarChar_Slot => StarChar("Slot");
        public string StarText_Slot => StarText("Slot");
        public string StarColor_Slot => StarColor("Slot");
        public string StarTextColor_Slot => StarTextColor("Slot");
        public string StarBg_Slot => StarBg("Slot");
        public string StarBorder_Slot => StarBorder("Slot");
        public bool IsFav_BreakFood => IsFav("BreakFood");
        public string StarChar_BreakFood => StarChar("BreakFood");
        public string StarText_BreakFood => StarText("BreakFood");
        public string StarColor_BreakFood => StarColor("BreakFood");
        public string StarTextColor_BreakFood => StarTextColor("BreakFood");
        public string StarBg_BreakFood => StarBg("BreakFood");
        public string StarBorder_BreakFood => StarBorder("BreakFood");
        public bool IsFav_Mosaic => IsFav("Mosaic");
        public string StarChar_Mosaic => StarChar("Mosaic");
        public string StarText_Mosaic => StarText("Mosaic");
        public string StarColor_Mosaic => StarColor("Mosaic");
        public string StarTextColor_Mosaic => StarTextColor("Mosaic");
        public string StarBg_Mosaic => StarBg("Mosaic");
        public string StarBorder_Mosaic => StarBorder("Mosaic");

        public bool IsFav_Gold => IsFav("Gold");
        public string StarChar_Gold => StarChar("Gold");
        public string StarText_Gold => StarText("Gold");
        public string StarColor_Gold => StarColor("Gold");
        public string StarTextColor_Gold => StarTextColor("Gold");
        public string StarBg_Gold => StarBg("Gold");
        public string StarBorder_Gold => StarBorder("Gold");

        public bool IsFav_Crafts => IsFav("Crafts");
        public string StarChar_Crafts => StarChar("Crafts");
        public string StarText_Crafts => StarText("Crafts");
        public string StarColor_Crafts => StarColor("Crafts");
        public string StarTextColor_Crafts => StarTextColor("Crafts");
        public string StarBg_Crafts => StarBg("Crafts");
        public string StarBorder_Crafts => StarBorder("Crafts");

        public bool IsFav_Juice => IsFav("Juice");
        public string StarChar_Juice => StarChar("Juice");
        public string StarText_Juice => StarText("Juice");
        public string StarColor_Juice => StarColor("Juice");
        public string StarTextColor_Juice => StarTextColor("Juice");
        public string StarBg_Juice => StarBg("Juice");
        public string StarBorder_Juice => StarBorder("Juice");

        public bool IsFav_BarScore => IsFav("BarScore");
        public string StarChar_BarScore => StarChar("BarScore");
        public string StarText_BarScore => StarText("BarScore");
        public string StarColor_BarScore => StarColor("BarScore");
        public string StarTextColor_BarScore => StarTextColor("BarScore");
        public string StarBg_BarScore => StarBg("BarScore");
        public string StarBorder_BarScore => StarBorder("BarScore");

        public bool IsFav_GuildPoints => IsFav("GuildPoints");
        public string StarChar_GuildPoints => StarChar("GuildPoints");
        public string StarText_GuildPoints => StarText("GuildPoints");
        public string StarColor_GuildPoints => StarColor("GuildPoints");
        public string StarTextColor_GuildPoints => StarTextColor("GuildPoints");
        public string StarBg_GuildPoints => StarBg("GuildPoints");
        public string StarBorder_GuildPoints => StarBorder("GuildPoints");

        public bool IsFav_Lanthanum => IsFav("Lanthanum");
        public string StarChar_Lanthanum => StarChar("Lanthanum");
        public string StarText_Lanthanum => StarText("Lanthanum");
        public string StarColor_Lanthanum => StarColor("Lanthanum");
        public string StarTextColor_Lanthanum => StarTextColor("Lanthanum");
        public string StarBg_Lanthanum => StarBg("Lanthanum");
        public string StarBorder_Lanthanum => StarBorder("Lanthanum");

        // ======================= 业务数值与开关绑定 =======================
        // 1. HP
        public int TargetHp { get => Config.TargetHp; set { Config.TargetHp = value; OnPropertyChanged(); } }
        public bool IsHpLocked { get => Config.LockHp; set { Config.LockHp = value; OnPropertyChanged(); PushConfig(); } }

        // 2. MP
        public int TargetMp { get => Config.TargetMp; set { Config.TargetMp = value; OnPropertyChanged(); } }
        public bool IsMpLocked { get => Config.LockMp; set { Config.LockMp = value; OnPropertyChanged(); PushConfig(); } }

        // 3. MP 破损程度
        public int TargetMpCrack { get => Config.TargetMpCrack; set { Config.TargetMpCrack = value; OnPropertyChanged(); } }
        public bool IsMpCrackLocked { get => Config.ModifyMpCrack; set { Config.ModifyMpCrack = value; OnPropertyChanged(); PushConfig(); } }

        // 4. 背包容量
        public int TargetInventoryCapacity { get => Config.TargetInventoryCapacity; set { Config.TargetInventoryCapacity = value; OnPropertyChanged(); } }
        public bool IsInventoryLocked { get => Config.ModifyInventoryCapacity; set { Config.ModifyInventoryCapacity = value; OnPropertyChanged(); PushConfig(); } }

        // 5. 饱食度上限
        public int TargetMaxSatiety { get => Config.TargetMaxSatiety; set { Config.TargetMaxSatiety = value; OnPropertyChanged(); } }
        public bool IsMaxSatietyLocked { get => Config.ModifyMaxSatiety; set { Config.ModifyMaxSatiety = value; OnPropertyChanged(); PushConfig(); } }

        // 6. 当前饱食度
        public float TargetSatiety { get => Config.TargetSatiety; set { Config.TargetSatiety = value; OnPropertyChanged(); } }
        public bool IsSatietyLocked { get => Config.ModifySatiety; set { Config.ModifySatiety = value; OnPropertyChanged(); PushConfig(); } }

        // 7. 兴奋度
        public int TargetEp { get => Config.TargetEp; set { Config.TargetEp = value; OnPropertyChanged(); } }
        public bool IsEpLocked { get => Config.ModifyEp; set { Config.ModifyEp = value; OnPropertyChanged(); PushConfig(); } }

        // 8. 走路速度 (独立块)
        public float WalkSpeedMultiplier { get => Config.WalkSpeedMultiplier; set { Config.WalkSpeedMultiplier = value; OnPropertyChanged(); } }
        public bool IsWalkSpeedLocked { get => Config.LockWalkSpeed; set { Config.LockWalkSpeed = value; OnPropertyChanged(); PushConfig(); } }

        // 9. 跑步速度 (独立块)
        public float RunSpeedMultiplier { get => Config.RunSpeedMultiplier; set { Config.RunSpeedMultiplier = value; OnPropertyChanged(); } }
        public bool IsRunSpeedLocked { get => Config.LockRunSpeed; set { Config.LockRunSpeed = value; OnPropertyChanged(); PushConfig(); } }

        // 10. 抓地力数值与防滑
        public float TargetGrip { get => Config.TargetGrip; set { Config.TargetGrip = value; OnPropertyChanged(); } }
        public bool IsGripLocked { get => Config.LockGrip; set { Config.LockGrip = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsNoSlipEnabled { get => Config.NoGripReduction; set { Config.NoGripReduction = value; OnPropertyChanged(); PushConfig(); } }

        // 11. 硬直时长
        public int TargetKnockbackTime { get => Config.TargetKnockbackTime; set { Config.TargetKnockbackTime = value; OnPropertyChanged(); } }
        public bool IsKnockbackLocked { get => Config.ModifyKnockbackTime; set { Config.ModifyKnockbackTime = value; OnPropertyChanged(); PushConfig(); } }

        // 12. 插槽容量
        public int TargetSlotCapacity { get => Config.TargetSlotCapacity; set { Config.TargetSlotCapacity = value; OnPropertyChanged(); } }
        public bool IsSlotLocked { get => Config.ModifySlotCapacity; set { Config.ModifySlotCapacity = value; OnPropertyChanged(); PushConfig(); } }

        // 13. 危险度
        public int TargetDangerLevel { get => Config.TargetDangerLevel; set { Config.TargetDangerLevel = value; OnPropertyChanged(); } }
        public bool IsDangerLocked { get => Config.LockDangerLevel; set { Config.LockDangerLevel = value; Config.ModifyDangerLevel = value; OnPropertyChanged(); PushConfig(); } }

        // 14. 资产与各类货币
        public int TargetGold { get => Config.TargetGold; set { Config.TargetGold = value; OnPropertyChanged(); } }
        public bool IsGoldLocked { get => Config.LockGold; set { Config.LockGold = value; OnPropertyChanged(); PushConfig(); } }

        public int TargetCrafts { get => Config.TargetCrafts; set { Config.TargetCrafts = value; OnPropertyChanged(); } }
        public bool IsCraftsLocked { get => Config.LockCrafts; set { Config.LockCrafts = value; OnPropertyChanged(); PushConfig(); } }

        public int TargetJuice { get => Config.TargetJuice; set { Config.TargetJuice = value; OnPropertyChanged(); } }
        public bool IsJuiceLocked { get => Config.LockJuice; set { Config.LockJuice = value; OnPropertyChanged(); PushConfig(); } }

        public int TargetBarScore { get => Config.TargetBarScore; set { Config.TargetBarScore = value; OnPropertyChanged(); } }
        public bool IsBarScoreLocked { get => Config.LockBarScore; set { Config.LockBarScore = value; OnPropertyChanged(); PushConfig(); } }

        public int TargetGuildPoints { get => Config.TargetGuildPoints; set { Config.TargetGuildPoints = value; OnPropertyChanged(); } }
        public bool IsGuildPointsLocked { get => Config.LockGuildPoints; set { Config.LockGuildPoints = value; OnPropertyChanged(); PushConfig(); } }

        public int TargetLanthanum { get => Config.TargetLanthanum; set { Config.TargetLanthanum = value; OnPropertyChanged(); } }
        public bool IsLanthanumLocked { get => Config.LockLanthanum; set { Config.LockLanthanum = value; OnPropertyChanged(); PushConfig(); } }

        // 纯开关类
        public bool IsMosaicDisabled { get => Config.DisableMosaic; set { Config.DisableMosaic = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsOneHitKill { get => Config.OneHitKill; set { Config.OneHitKill = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsShieldNeverBreak { get => Config.ShieldNeverBreak; set { Config.ShieldNeverBreak = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsInstantMagicCharge { get => Config.InstantMagicCharge; set { Config.InstantMagicCharge = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsNoBurstTired { get => Config.NoBurstTired; set { Config.NoBurstTired = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsJustGuardNoHit { get => Config.JustGuardNoHit; set { Config.JustGuardNoHit = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsExtendedJustGuard { get => Config.ExtendedJustGuard; set { Config.ExtendedJustGuard = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsFastTravelAnytime { get => Config.FastTravelAnytime; set { Config.FastTravelAnytime = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsFastTravelAnywhere { get => Config.FastTravelAnywhere; set { Config.FastTravelAnywhere = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsAlwaysHungryBonus { get => Config.AlwaysHungryBonus; set { Config.AlwaysHungryBonus = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsBreakFoodAttrLimit { get => Config.BreakFoodAttrLimit; set { Config.BreakFoodAttrLimit = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsSaveAnywhere { get => Config.SaveAnywhere; set { Config.SaveAnywhere = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsFreezeCountdown { get => Config.FreezeCountdown; set { Config.FreezeCountdown = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsInfiniteJump { get => Config.InfiniteJump; set { Config.InfiniteJump = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsImmuneAbnormalStatus { get => Config.ImmuneAbnormalStatus; set { Config.ImmuneAbnormalStatus = value; OnPropertyChanged(); PushConfig(); } }

        public bool IsNoWormTrap { get => Config.NoWormTrap; set { Config.NoWormTrap = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsNoSpikeDamage { get => Config.NoSpikeDamage; set { Config.NoSpikeDamage = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsNoThunderDamage { get => Config.NoThunderDamage; set { Config.NoThunderDamage = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsNoDrownDamage { get => Config.NoDrownDamage; set { Config.NoDrownDamage = value; OnPropertyChanged(); PushConfig(); } }
        public bool IsNoAcidDamage { get => Config.NoAcidDamage; set { Config.NoAcidDamage = value; OnPropertyChanged(); PushConfig(); } }

        // ======================= 命令接口 =======================
        public ICommand LaunchGameCommand { get; }
        public ICommand InjectCommand { get; }
        public ICommand ToggleFavCommand { get; }

        public ICommand ApplyHpCommand { get; }
        public ICommand ApplyMpCommand { get; }
        public ICommand ApplyMpCrackCommand { get; }
        public ICommand ApplyInventoryCommand { get; }
        public ICommand ApplyMaxSatietyCommand { get; }
        public ICommand ApplySatietyCommand { get; }
        public ICommand ApplyEpCommand { get; }
        public ICommand ApplyWalkSpeedCommand { get; }
        public ICommand ApplyRunSpeedCommand { get; }
        public ICommand ApplyGripCommand { get; }
        public ICommand ApplyKnockbackCommand { get; }
        public ICommand ApplySlotCommand { get; }
        public ICommand ApplyDangerCommand { get; }
        public ICommand ApplyGoldCommand { get; }
        public ICommand ApplyCraftsCommand { get; }
        public ICommand ApplyJuiceCommand { get; }
        public ICommand ApplyBarScoreCommand { get; }
        public ICommand ApplyGuildPointsCommand { get; }
        public ICommand ApplyLanthanumCommand { get; }

        public ICommand QuickHealCommand { get; }
        public ICommand QuickCureCracksCommand { get; }
        public ICommand QuickCureSerCommand { get; }
        public ICommand QuickClearSatietyCommand { get; }
        public ICommand QuickClearEpCommand { get; }

        public async Task LaunchGameAsync()
        {
            if (ProcessMonitor.LaunchGame(out string err))
            {
                StatusText = "游戏进程已拉起，等待游戏主窗口弹出...";
                StatusColor = "#FFAA00";

                var proc = await ProcessMonitor.WaitForGameWindowAsync(timeoutSeconds: 60);
                if (proc == null || proc.HasExited)
                {
                    if (IsGameRunning)
                    {
                        StatusText = "未捕获到游戏主窗口，可稍后点击【⚡ 注入 / 连接】";
                    }
                    return;
                }

                // 游戏窗口已真实弹出，等待 5000ms 后才进行注入
                for (int i = 5; i > 0; i--)
                {
                    if (proc.HasExited)
                    {
                        StatusText = "游戏已退出，自动注入已取消";
                        StatusColor = "#FF5555";
                        return;
                    }
                    StatusText = $"检测到游戏窗口已弹出，等待 {i} 秒后自动注入...";
                    StatusColor = "#FFAA00";
                    await Task.Delay(1000);
                }

                if (!proc.HasExited && !IsInjected)
                {
                    StatusText = "游戏主窗口与引擎已就绪，正在自动注入并建立连接...";
                    await InjectAndConnectAsync();
                }
            }
            else
            {
                StatusText = $"启动失败: {err}";
                StatusColor = "#FF5555";
                MessageBox.Show(err, "启动游戏失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private bool _isConnecting = false;
        public async Task<bool> TryAutoConnectAsync()
        {
            if (_isConnecting || Client.IsConnected) return false;
            _isConnecting = true;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    if (await Client.ConnectAsync(800)) return true;
                    await Task.Delay(300);
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                _isConnecting = false;
            }
        }

        public async Task<bool> InjectAndConnectAsync()
        {
            if (!IsGameRunning || Monitor.TargetProcess == null)
            {
                MessageBox.Show("游戏尚未运行，请先点击【启动游戏】或启动 Alice in Cradle！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            byte[]? payloadBytes = ProcessMonitor.GetEmbeddedPayloadBytes();
            if (payloadBytes == null || payloadBytes.Length == 0)
            {
                MessageBox.Show("未找到内置注入载荷资源，请检查构建配置！", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            StatusText = "正在向游戏纯内存注入载荷 (0磁盘释放)...";
            StatusColor = "#FFAA00";

            string injectorError = string.Empty;
            bool ok = await Task.Run(() =>
            {
                return MonoInjector.InjectFromMemory(
                    Monitor.TargetProcess.Id,
                    payloadBytes,
                    "AICModPayload",
                    "AICMod",
                    "Loader",
                    "Init",
                    out injectorError);
            });

            if (!ok)
            {
                StatusText = $"注入失败: {injectorError}";
                StatusColor = "#FF5555";
                MessageBox.Show($"内存注入失败:\n{injectorError}", "注入错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            for (int i = 0; i < 5; i++)
            {
                await Task.Delay(300);
                if (await Client.ConnectAsync(1000))
                {
                    return true;
                }
            }

            return false;
        }

        public void PushConfig()
        {
            Client.SendConfig(Config);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
