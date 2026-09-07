using System;

namespace AICShared
{
    public static class PipeConstants
    {
        public const string PipeName = "AliceInCradle_Trainer_Pipe";
        public const int IpcPort = 48123;
    }

    [Serializable]
    public class GameStateDto
    {
        public bool IsGameReady;
        public string CurrentMap = string.Empty;

        // 数值类 - 当前与上限
        public int Hp;
        public int MaxHp;
        public int Mp;
        public int MaxMp;
        public int MpCrackCount; // 破损数 0~21
        public int InventoryCapacity; // 背包行数 (默认12)
        public float Satiety; // 当前饱食度
        public int MaxSatiety; // 饱食度上限 (默认28)
        public int Ep; // 兴奋度 (0~1000)
        public float WalkSpeed; // 走速 (默认0.085)
        public float RunSpeed; // 跑速 (默认0.17)
        public float Grip; // 当前抓地力/摩擦力 (默认0.04)
        public int KnockbackTime; // 受创硬直帧数 (默认40)
        public int SlotCapacity; // 强化槽容量
        public int DangerLevel; // 危险度数值

        // 资产与各类货币当前值
        public int Gold;
        public int Crafts;
        public int Juice;
        public int BarScore;
        public int GuildPoints;
        public int Lanthanum;

        // 版本与补丁健康度 (版本自适应)
        public string GameVersion = string.Empty;
        public string EngineVersion = string.Empty;
        public int ActivePatchCount;
        public int TotalPatchCount;
    }

    [Serializable]
    public class ModConfigDto
    {
        // 1. 自定义Mosaic（马赛克）是否显示 (true: 隐藏马赛克, false: 正常显示)
        public bool DisableMosaic;

        // 2. 更改HP/MP并锁定
        public bool LockHp;
        public int TargetHp = -1; // -1表示不强制更改特定值，仅锁定满值
        public bool LockMp;
        public int TargetMp = -1;

        // 3. 更改MP及量条破损程度
        public bool ModifyMpCrack;
        public int TargetMpCrack = 0; // 0 = 完好无损

        // 4. 更改背包容量
        public bool ModifyInventoryCapacity;
        public int TargetInventoryCapacity = 40;

        // 5~9. 地形与环境免疫
        public bool NoWormTrap; // 虫墙无效
        public bool NoSpikeDamage; // 荆棘无效
        public bool NoThunderDamage; // 电击无效
        public bool NoDrownDamage; // 溺水无效
        public bool NoAcidDamage; // 酸液无效

        // 10. 秒杀（现成函数）
        public bool OneHitKill;

        // 11~12. 饱食度
        public bool ModifyMaxSatiety; // 饱食度上限修改
        public int TargetMaxSatiety = 100;
        public bool ModifySatiety; // 更改饱食度
        public float TargetSatiety = 0f;

        // 13. 更改兴奋度
        public bool ModifyEp;
        public int TargetEp = 0;

        // 14. 护盾不碎
        public bool ShieldNeverBreak;

        // 15~16. 快速移动
        public bool FastTravelAnytime; // 全天可快速移动
        public bool FastTravelAnywhere; // 随地可快速移动

        // 17. 始终空腹加成
        public bool AlwaysHungryBonus;

        // 18. 更改走路/跑步速度 (独立分块调控)
        public bool LockWalkSpeed;
        public float WalkSpeedMultiplier = 1.0f;
        public bool LockRunSpeed;
        public float RunSpeedMultiplier = 1.0f;

        // 19. 更改硬直时长
        public bool ModifyKnockbackTime;
        public int TargetKnockbackTime = 1; // 1 = 瞬解

        // 20. 突破食物属性上限
        public bool BreakFoodAttrLimit;

        // 21. 更改插槽容量
        public bool ModifySlotCapacity;
        public int TargetSlotCapacity = 15;

        // 22. 启用随意保存
        public bool SaveAnywhere;

        // 23. 启用锁定倒计时
        public bool FreezeCountdown;

        // 24. 更改危险度
        public bool ModifyDangerLevel;
        public int TargetDangerLevel = 0;
        public bool LockDangerLevel;

        // 25. 抓地力数值修改与防滑
        public bool LockGrip;
        public float TargetGrip = 0.04f;
        public bool NoGripReduction; // 冰面/斜坡防滑

        // 26. 可连跳
        public bool InfiniteJump;

        // 27. 异常状态无效
        public bool ImmuneAbnormalStatus;

        // 28. 魔法蓄力秒完成 (瞬发满蓄)
        public bool InstantMagicCharge;

        // 29. 圣光爆发不触发眩晕
        public bool NoBurstTired;

        // 30. 资产与各类货币锁定
        public bool LockGold;
        public int TargetGold = 999999;
        public bool LockCrafts;
        public int TargetCrafts = 99999;
        public bool LockJuice;
        public int TargetJuice = 99999;
        public bool LockBarScore;
        public int TargetBarScore = 99999;
        public bool LockGuildPoints;
        public int TargetGuildPoints = 9999;
        public bool LockLanthanum;
        public int TargetLanthanum = 99;

        // 31. 展开护盾就触发精准防御 (无需受击)
        public bool JustGuardNoHit;

        // 32. 展开护盾受到攻击就精准防御 (拉长时间)
        public bool ExtendedJustGuard;

        // 33. 武馆小游戏必胜 (无论是否正确按正确判定)
        public bool DojoAlwaysWin;

        // 34. 禁止受击判定 (完全免疫受击/攻击穿透)
        public bool DisableHitCheck;

        // 35. 判定框展示 (蓝框为受击框，红框为攻击框)
        public bool ShowHitboxes;
        public bool HitboxShowName = true; // 受击框显示名字 (第1行)
        public bool HitboxShowHp = true;   // 受击框显示HP (第2行)
        public bool HitboxShowMp = true;   // 受击框显示MP (第3行)
        public bool HitboxShowAtkInfo = true; // 攻击框显示攻击信息

        public ModConfigDto Clone()
        {
            var copy = new ModConfigDto();
            copy.CopyFrom(this);
            return copy;
        }

        public void CopyFrom(ModConfigDto other)
        {
            if (other == null) return;
            DisableMosaic = other.DisableMosaic;
            LockHp = other.LockHp;
            TargetHp = other.TargetHp;
            LockMp = other.LockMp;
            TargetMp = other.TargetMp;
            ModifyMpCrack = other.ModifyMpCrack;
            TargetMpCrack = other.TargetMpCrack;
            ModifyInventoryCapacity = other.ModifyInventoryCapacity;
            TargetInventoryCapacity = other.TargetInventoryCapacity;
            NoWormTrap = other.NoWormTrap;
            NoSpikeDamage = other.NoSpikeDamage;
            NoThunderDamage = other.NoThunderDamage;
            NoDrownDamage = other.NoDrownDamage;
            NoAcidDamage = other.NoAcidDamage;
            OneHitKill = other.OneHitKill;
            ModifyMaxSatiety = other.ModifyMaxSatiety;
            TargetMaxSatiety = other.TargetMaxSatiety;
            ModifySatiety = other.ModifySatiety;
            TargetSatiety = other.TargetSatiety;
            ModifyEp = other.ModifyEp;
            TargetEp = other.TargetEp;
            ShieldNeverBreak = other.ShieldNeverBreak;
            FastTravelAnytime = other.FastTravelAnytime;
            FastTravelAnywhere = other.FastTravelAnywhere;
            AlwaysHungryBonus = other.AlwaysHungryBonus;
            LockWalkSpeed = other.LockWalkSpeed;
            WalkSpeedMultiplier = other.WalkSpeedMultiplier;
            LockRunSpeed = other.LockRunSpeed;
            RunSpeedMultiplier = other.RunSpeedMultiplier;
            ModifyKnockbackTime = other.ModifyKnockbackTime;
            TargetKnockbackTime = other.TargetKnockbackTime;
            BreakFoodAttrLimit = other.BreakFoodAttrLimit;
            ModifySlotCapacity = other.ModifySlotCapacity;
            TargetSlotCapacity = other.TargetSlotCapacity;
            SaveAnywhere = other.SaveAnywhere;
            FreezeCountdown = other.FreezeCountdown;
            ModifyDangerLevel = other.ModifyDangerLevel;
            TargetDangerLevel = other.TargetDangerLevel;
            LockDangerLevel = other.LockDangerLevel;
            LockGrip = other.LockGrip;
            TargetGrip = other.TargetGrip;
            NoGripReduction = other.NoGripReduction;
            InfiniteJump = other.InfiniteJump;
            ImmuneAbnormalStatus = other.ImmuneAbnormalStatus;
            InstantMagicCharge = other.InstantMagicCharge;
            NoBurstTired = other.NoBurstTired;
            LockGold = other.LockGold;
            TargetGold = other.TargetGold;
            LockCrafts = other.LockCrafts;
            TargetCrafts = other.TargetCrafts;
            LockJuice = other.LockJuice;
            TargetJuice = other.TargetJuice;
            LockBarScore = other.LockBarScore;
            TargetBarScore = other.TargetBarScore;
            LockGuildPoints = other.LockGuildPoints;
            TargetGuildPoints = other.TargetGuildPoints;
            LockLanthanum = other.LockLanthanum;
            TargetLanthanum = other.TargetLanthanum;
            JustGuardNoHit = other.JustGuardNoHit;
            ExtendedJustGuard = other.ExtendedJustGuard;
            DojoAlwaysWin = other.DojoAlwaysWin;
            DisableHitCheck = other.DisableHitCheck;
            ShowHitboxes = other.ShowHitboxes;
            HitboxShowName = other.HitboxShowName;
            HitboxShowHp = other.HitboxShowHp;
            HitboxShowMp = other.HitboxShowMp;
            HitboxShowAtkInfo = other.HitboxShowAtkInfo;
        }
    }

    [Serializable]
    public class IpcMessage
    {
        public string Type = string.Empty; // "StateSync", "ApplyConfig", "Action", "Ping", "Pong"
        public string JsonData = string.Empty;
    }

    [Serializable]
    public class ActionMessage
    {
        public string ActionName = string.Empty;
        public int IntParam;
        public float FloatParam;
    }
}
