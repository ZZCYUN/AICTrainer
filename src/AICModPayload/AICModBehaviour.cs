using System;
using System.Collections.Concurrent;
using AICShared;
using m2d;
using nel;
using UnityEngine;
using XX;

namespace AICMod
{
    /// <summary>
    /// 修改器逻辑核心控制器，全部由 Unity 主渲染线程 (IN.Update) 驱动
    /// 确保 100% 线程安全，避免后台跨线程调用 Unity API 导致引擎锁死
    /// </summary>
    public static class AICModController
    {
        private static float _syncTimer;
        private const float SyncInterval = 0.1f; // 100ms 同步一次
        private static readonly ConcurrentQueue<ActionMessage> IncomingActions = new ConcurrentQueue<ActionMessage>();

        public static void Init()
        {
            IpcServer.Instance.OnActionReceived += act =>
            {
                IncomingActions.Enqueue(act);
            };
        }

        public static void MainThreadTick()
        {
            try
            {
                // 1. 在主线程中安全处理 IPC 远程指令
                while (IncomingActions.TryDequeue(out var act))
                {
                    HandleAction(act);
                }

                // 2. 状态锁定与自适应修改
                var cfg = AICModConfig.Current;
                var pr = GetPlayer();
                var nm2d = GetM2D();

                if (pr != null)
                {
                    ApplyPlayerLocks(pr, cfg);
                }

                if (nm2d != null)
                {
                    ApplyWorldLocks(nm2d, cfg);
                }

                // 3. 定时同步游戏数据到客户端
                _syncTimer += Time.unscaledDeltaTime;
                if (_syncTimer >= SyncInterval)
                {
                    _syncTimer = 0f;
                    SyncGameState(pr, nm2d);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] MainThreadTick error: " + ex.Message);
            }
        }

        private static PR? GetPlayer()
        {
            try
            {
                var m2d = M2DBase.Instance;
                if (m2d != null && m2d.curMap != null)
                {
                    return m2d.curMap.Pr as PR;
                }
            }
            catch { }
            return null;
        }

        private static NelM2DBase? GetM2D()
        {
            try
            {
                return M2DBase.Instance as NelM2DBase;
            }
            catch { }
            return null;
        }

        private static void SafeExecute(string featureName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                // 单项功能异常隔离，不影响其他功能
                Debug.LogWarning($"[AICMod] SafeExecute '{featureName}' warning: {ex.Message}");
            }
        }

        private static void ApplyPlayerLocks(PR pr, ModConfigDto cfg)
        {
            // 1. HP 锁定
            SafeExecute("LockHp", () =>
            {
                if (cfg.LockHp)
                {
                    int target = (cfg.TargetHp > 0) ? cfg.TargetHp : (int)pr.get_maxhp();
                    int curHp = (int)pr.get_hp();
                    if (curHp < target)
                    {
                        pr.cureHp(target - curHp);
                    }
                    else if (curHp > target)
                    {
                        ReflectionHelper.SetValue(pr, target, "hp", "Hp", "_hp");
                        if (UIStatus.isPr(pr)) UIStatus.Instance.fineHpRatio(false);
                    }
                }
            });

            // 2. MP 锁定
            SafeExecute("LockMp", () =>
            {
                if (cfg.LockMp)
                {
                    int target = (cfg.TargetMp > 0) ? cfg.TargetMp : (int)pr.get_maxmp();
                    int curMp = (int)pr.get_mp();
                    if (curMp < target)
                    {
                        pr.cureMp(target - curMp);
                    }
                    else if (curMp > target)
                    {
                        ReflectionHelper.SetValue(pr, target, "mp", "Mp", "_mp");
                        if (UIStatus.isPr(pr)) UIStatus.Instance.fineMpRatio(false);
                    }
                }
            });

            // 3. MP 破损程度修改与锁定
            SafeExecute("ModifyMpCrack", () =>
            {
                if (cfg.ModifyMpCrack && pr.GaugeBrk != null)
                {
                    int curCrack = ReflectionHelper.GetValue<int>(pr.GaugeBrk, "break_cnt", "breakCnt");
                    if (curCrack != cfg.TargetMpCrack)
                    {
                        ApplyMpCrackValue(pr, cfg.TargetMpCrack);
                    }
                }
            });

            // 4. 背包容量修改
            SafeExecute("ModifyInventoryCapacity", () =>
            {
                if (cfg.ModifyInventoryCapacity && pr.NM2D != null && pr.NM2D.IMNG != null)
                {
                    var inv = pr.NM2D.IMNG.getInventory();
                    if (inv != null && inv.row_max != cfg.TargetInventoryCapacity)
                    {
                        inv.row_max = cfg.TargetInventoryCapacity;
                    }
                }
            });

            // 5. 饱食度上限与当前饱食度
            SafeExecute("ModifySatiety", () =>
            {
                if (pr.MyStomach != null)
                {
                    if (cfg.ModifyMaxSatiety && pr.MyStomach.cost_max != cfg.TargetMaxSatiety)
                    {
                        pr.MyStomach.cost_max = cfg.TargetMaxSatiety;
                    }
                    if (cfg.ModifySatiety)
                    {
                        pr.MyStomach.cost = cfg.TargetSatiety;
                    }
                }
            });

            // 6. 兴奋度 (EP)
            SafeExecute("ModifyEp", () =>
            {
                if (cfg.ModifyEp)
                {
                    pr.ep = cfg.TargetEp;
                    if (pr.EpCon != null)
                    {
                        ReflectionHelper.SetValue(pr.EpCon, (float)cfg.TargetEp, "ep", "Ep", "_ep");
                    }
                }
            });

            // 7. 护盾不碎
            SafeExecute("ShieldNeverBreak", () =>
            {
                if (cfg.ShieldNeverBreak && pr.Skill != null && pr.Skill.ShE != null && pr.Skill.ShE.Shield != null)
                {
                    ReflectionHelper.SetValue(pr.Skill.ShE.Shield, 0f, "pow", "Pow");
                }
            });

            // 8. 走路速度修改与锁定 (独立块)
            SafeExecute("LockWalkSpeed", () =>
            {
                if (cfg.LockWalkSpeed)
                {
                    float baseWalk = 0.085f;
                    ReflectionHelper.SetValue(pr, baseWalk * cfg.WalkSpeedMultiplier, "walkSpeed", "walk_speed", "WalkSpeed");
                }
            });

            // 8b. 跑步速度修改与锁定 (独立块)
            SafeExecute("LockRunSpeed", () =>
            {
                if (cfg.LockRunSpeed)
                {
                    float baseRun = 0.17f;
                    ReflectionHelper.SetValue(pr, baseRun * cfg.RunSpeedMultiplier, "runSpeed", "run_speed", "RunSpeed");
                }
            });

            // 9. 硬直时长修改
            SafeExecute("ModifyKnockbackTime", () =>
            {
                if (cfg.ModifyKnockbackTime)
                {
                    ReflectionHelper.SetValue(pr, cfg.TargetKnockbackTime, "knockback_time", "knockbackTime");
                }
            });

            // 10. 插槽容量修改
            SafeExecute("ModifySlotCapacity", () =>
            {
                if (cfg.ModifySlotCapacity)
                {
                    ReflectionHelper.SetValue(typeof(ENHA), cfg.TargetSlotCapacity, "max_slot", "maxSlot", "slot_max");
                }
            });

            // 11. 抓地力数值与防滑锁定
            SafeExecute("LockGrip", () =>
            {
                if (pr.getPhysic() != null)
                {
                    if (cfg.LockGrip)
                    {
                        pr.getPhysic().fric_reduce_x = cfg.TargetGrip;
                    }
                    if (cfg.NoGripReduction)
                    {
                        pr.getPhysic().removeOnIce();
                        pr.getPhysic().clearLockSlopeSlideFrame();
                    }
                }
            });

            // 12. 异常状态无效 (若有残留立即清除)
            SafeExecute("ImmuneAbnormalStatus", () =>
            {
                if (cfg.ImmuneAbnormalStatus && pr.Ser != null)
                {
                    pr.Ser.CureAll();
                }
            });

            // 13. 秒杀：同步游戏原生 DEBUGMIGHTY 标志
            SafeExecute("OneHitKill", () =>
            {
                XX.X.DEBUGMIGHTY = cfg.OneHitKill;
            });

            // 14. 魔法蓄力秒完成 (瞬发满蓄)
            SafeExecute("InstantMagicCharge", () =>
            {
                if (pr.Skill != null)
                {
                    if (cfg.InstantMagicCharge)
                    {
                        pr.Skill.pr_chant_speed = 100f;
                        var curMg = pr.Skill.getCurMagic();
                        if (curMg != null && curMg.casttime > 0f)
                        {
                            if (curMg.t < curMg.casttime)
                            {
                                curMg.t = curMg.casttime;
                            }
                            if (curMg.sa == 0f)
                            {
                                curMg.sa = 1f;
                                curMg.chant_finished = true;
                            }
                        }
                    }
                    else if (pr.Skill.pr_chant_speed > 10f)
                    {
                        pr.Skill.pr_chant_speed = 1f;
                    }
                }
            });

            // 15. 精准防御状态维持 (展开即触发 / 举盾受击必定弹反)
            SafeExecute("JustGuardLocks", () =>
            {
                if ((cfg.JustGuardNoHit || cfg.ExtendedJustGuard) && pr.Skill?.ShE?.Shield != null)
                {
                    pr.Skill.ShE.Shield.just_guard_enable = true;
                    if (cfg.ExtendedJustGuard && pr.Skill.ShE.Shield.isActive())
                    {
                        ReflectionHelper.SetValue(pr.Skill.ShE.Shield, 10f, "t_justguard_prepare");
                    }
                }
            });
        }

        private static void ApplyWorldLocks(NelM2DBase nm2d, ModConfigDto cfg)
        {
            SafeExecute("DangerLevel", () =>
            {
                if (nm2d.NightCon != null)
                {
                    if (cfg.ModifyDangerLevel)
                    {
                        nm2d.NightCon.setAdditionalDangerLevelManual(cfg.TargetDangerLevel);
                    }
                    if (cfg.LockDangerLevel)
                    {
                        nm2d.NightCon.debug_lock_dangerousness = true;
                    }
                }
            });

            // 资产与各类货币锁定
            SafeExecute("LockCurrencies", () =>
            {
                if (cfg.LockGold)
                {
                    var ce = CoinStorage.getEntry(CoinStorage.CTYPE.GOLD);
                    if (ce != null && ce.Get() != (uint)cfg.TargetGold)
                    {
                        ce.Set((uint)Mathf.Max(0, cfg.TargetGold), true, false);
                    }
                }
                if (cfg.LockCrafts)
                {
                    var ce = CoinStorage.getEntry(CoinStorage.CTYPE.CRAFTS);
                    if (ce != null && ce.Get() != (uint)cfg.TargetCrafts)
                    {
                        ce.Set((uint)Mathf.Max(0, cfg.TargetCrafts), true, false);
                    }
                }
                if (cfg.LockJuice)
                {
                    var ce = CoinStorage.getEntry(CoinStorage.CTYPE.JUICE);
                    if (ce != null && ce.Get() != (uint)cfg.TargetJuice)
                    {
                        ce.Set((uint)Mathf.Max(0, cfg.TargetJuice), true, false);
                    }
                }
                if (cfg.LockBarScore)
                {
                    var ce = CoinStorage.getEntry(CoinStorage.CTYPE.BAR_SCORE);
                    if (ce != null && ce.Get() != (uint)cfg.TargetBarScore)
                    {
                        ce.Set((uint)Mathf.Max(0, cfg.TargetBarScore), true, false);
                    }
                }
                if (cfg.LockGuildPoints && nm2d.GUILD != null)
                {
                    if (nm2d.GUILD.gq_point != cfg.TargetGuildPoints)
                    {
                        nm2d.GUILD.gq_point = cfg.TargetGuildPoints;
                        nm2d.GUILD.need_fine_flag = true;
                    }
                }
                if (cfg.LockLanthanum && nm2d.IMNG != null && NelItem.Lanthanum != null)
                {
                    var inv = nm2d.IMNG.getInventory();
                    if (inv != null)
                    {
                        int cur = inv.getCount(NelItem.Lanthanum, -1);
                        int target = Mathf.Max(0, cfg.TargetLanthanum);
                        int diff = target - cur;
                        if (diff > 0)
                        {
                            inv.Add(NelItem.Lanthanum, diff, 0, true, true);
                        }
                        else if (diff < 0)
                        {
                            inv.Reduce(NelItem.Lanthanum, -diff, -1, true);
                        }
                    }
                }
            });

            // 全局/立绘马赛克动态实时管控
            SafeExecute("DisableMosaic", () =>
            {
                if (cfg.DisableMosaic)
                {
                    var ui = UIBase.Instance;
                    if (ui != null && ui.getCurrentPict(out var curPict) && curPict != null)
                    {
                        if (curPict.mosaic_enabled)
                        {
                            curPict.mosaic_enabled = false;
                        }
                    }
                }
            });
        }

        private static void SyncGameState(PR? pr, NelM2DBase? nm2d)
        {
            try
            {
                var state = new GameStateDto
                {
                    GameVersion = Application.version,
                    EngineVersion = Application.unityVersion,
                    ActivePatchCount = ResilientPatcher.ActivePatches,
                    TotalPatchCount = ResilientPatcher.TotalPatches
                };

                if (pr != null && nm2d != null && nm2d.curMap != null)
                {
                    try
                    {
                        state.IsGameReady = true;
                        state.CurrentMap = nm2d.curMap.key ?? "";

                        state.Hp = (int)pr.get_hp();
                        state.MaxHp = (int)pr.get_maxhp();
                        state.Mp = (int)pr.get_mp();
                        state.MaxMp = (int)pr.get_maxmp();

                        if (pr.GaugeBrk != null)
                        {
                            state.MpCrackCount = ReflectionHelper.GetValue<int>(pr.GaugeBrk, "break_cnt", "breakCnt");
                        }

                        if (nm2d.IMNG != null && nm2d.IMNG.getInventory() != null)
                        {
                            state.InventoryCapacity = nm2d.IMNG.getInventory().row_max;
                        }

                        if (pr.MyStomach != null)
                        {
                            state.Satiety = pr.MyStomach.cost;
                            state.MaxSatiety = pr.MyStomach.cost_max;
                        }

                        state.Ep = pr.ep;
                        state.WalkSpeed = pr.getDefaultWalkSpeed();
                        state.RunSpeed = ReflectionHelper.GetValue<float>(pr, "runSpeed", "run_speed", "RunSpeed");
                        if (pr.getPhysic() != null)
                        {
                            state.Grip = pr.getPhysic().fric_reduce_x;
                        }
                        state.KnockbackTime = pr.get_knockback_time();
                        state.SlotCapacity = ReflectionHelper.GetValue<int>(typeof(ENHA), "max_slot", "maxSlot", "slot_max");

                        if (nm2d.NightCon != null)
                        {
                            state.DangerLevel = nm2d.NightCon.getDangerMeterVal(true, false);
                        }

                        // 货币与资产同步
                        try
                        {
                            state.Gold = (int)CoinStorage.getCount(CoinStorage.CTYPE.GOLD);
                            state.Crafts = (int)CoinStorage.getCount(CoinStorage.CTYPE.CRAFTS);
                            state.Juice = (int)CoinStorage.getCount(CoinStorage.CTYPE.JUICE);
                            state.BarScore = (int)CoinStorage.getCount(CoinStorage.CTYPE.BAR_SCORE);
                        }
                        catch { }

                        try
                        {
                            if (nm2d.GUILD != null)
                            {
                                state.GuildPoints = nm2d.GUILD.gq_point;
                            }
                        }
                        catch { }

                        try
                        {
                            var inv = nm2d.IMNG?.getInventory();
                            if (inv != null && NelItem.Lanthanum != null)
                            {
                                state.Lanthanum = inv.getCount(NelItem.Lanthanum, -1);
                            }
                        }
                        catch { }
                    }
                    catch { }
                }
                else
                {
                    state.IsGameReady = false;
                }

                IpcServer.Instance.SendState(state);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] SyncGameState error: " + ex.Message);
            }
        }

        private static void HandleAction(ActionMessage act)
        {
            var pr = GetPlayer();
            var nm2d = GetM2D();

            try
            {
                switch (act.ActionName)
                {
                    case "full_heal":
                        if (pr != null)
                        {
                            int maxHp = (int)pr.get_maxhp();
                            int maxMp = (int)pr.get_maxmp();
                            int curHp = (int)pr.get_hp();
                            int curMp = (int)pr.get_mp();
                            if (curHp < maxHp) pr.cureHp(maxHp - curHp);
                            if (curMp < maxMp) pr.cureMp(maxMp - curMp);
                            ApplyMpCrackValue(pr, 0, force: true);
                            pr.Ser?.CureAll();
                        }
                        break;

                    case "cure_cracks":
                        ApplyMpCrackValue(pr, 0, force: true);
                        break;

                    case "cure_ser":
                        pr?.Ser?.CureAll();
                        break;

                    case "clear_satiety":
                        if (pr?.MyStomach != null)
                        {
                            pr.MyStomach.cost = 0f;
                        }
                        break;

                    case "clear_ep":
                        if (pr != null)
                        {
                            pr.ep = 0;
                        }
                        break;

                    case "set_hp":
                        if (pr != null)
                        {
                            int curHp = (int)pr.get_hp();
                            if (act.IntParam > curHp) pr.cureHp(act.IntParam - curHp);
                            else { ReflectionHelper.SetValue(pr, act.IntParam, "hp", "Hp", "_hp"); if (UIStatus.isPr(pr)) UIStatus.Instance.fineHpRatio(false); }
                        }
                        break;

                    case "set_mp":
                        if (pr != null)
                        {
                            int curMp = (int)pr.get_mp();
                            if (act.IntParam > curMp) pr.cureMp(act.IntParam - curMp);
                            else { ReflectionHelper.SetValue(pr, act.IntParam, "mp", "Mp", "_mp"); if (UIStatus.isPr(pr)) UIStatus.Instance.fineMpRatio(false); }
                        }
                        break;

                    case "set_mp_crack":
                        ApplyMpCrackValue(pr, act.IntParam, force: true);
                        break;

                    case "set_inventory":
                        if (nm2d?.IMNG?.getInventory() != null)
                        {
                            nm2d.IMNG.getInventory().row_max = act.IntParam;
                        }
                        break;

                    case "set_max_satiety":
                        if (pr?.MyStomach != null)
                        {
                            pr.MyStomach.cost_max = act.IntParam;
                        }
                        break;

                    case "set_satiety":
                        if (pr?.MyStomach != null)
                        {
                            pr.MyStomach.cost = act.FloatParam;
                        }
                        break;

                    case "set_ep":
                        if (pr != null)
                        {
                            pr.ep = act.IntParam;
                            if (pr.EpCon != null)
                            {
                                ReflectionHelper.SetValue(pr.EpCon, (float)act.IntParam, "ep", "Ep", "_ep");
                            }
                        }
                        break;

                    case "set_walk_speed":
                        if (pr != null)
                        {
                            ReflectionHelper.SetValue(pr, 0.085f * act.FloatParam, "walkSpeed", "walk_speed", "WalkSpeed");
                        }
                        break;

                    case "set_run_speed":
                        if (pr != null)
                        {
                            ReflectionHelper.SetValue(pr, 0.17f * act.FloatParam, "runSpeed", "run_speed", "RunSpeed");
                        }
                        break;

                    case "set_knockback":
                        if (pr != null)
                        {
                            ReflectionHelper.SetValue(pr, act.IntParam, "knockback_time", "knockbackTime");
                        }
                        break;

                    case "set_slot":
                        ReflectionHelper.SetValue(typeof(ENHA), act.IntParam, "max_slot", "maxSlot", "slot_max");
                        break;

                    case "set_danger":
                        if (nm2d?.NightCon != null)
                        {
                            nm2d.NightCon.setAdditionalDangerLevelManual(act.IntParam);
                        }
                        break;

                    case "set_grip":
                        if (pr?.getPhysic() != null)
                        {
                            pr.getPhysic().fric_reduce_x = act.FloatParam;
                        }
                        break;

                    case "set_gold":
                        {
                            var ce = CoinStorage.getEntry(CoinStorage.CTYPE.GOLD);
                            if (ce != null) ce.Set((uint)Mathf.Max(0, act.IntParam), true, false);
                        }
                        break;

                    case "set_crafts":
                        {
                            var ce = CoinStorage.getEntry(CoinStorage.CTYPE.CRAFTS);
                            if (ce != null) ce.Set((uint)Mathf.Max(0, act.IntParam), true, false);
                        }
                        break;

                    case "set_juice":
                        {
                            var ce = CoinStorage.getEntry(CoinStorage.CTYPE.JUICE);
                            if (ce != null) ce.Set((uint)Mathf.Max(0, act.IntParam), true, false);
                        }
                        break;

                    case "set_bar_score":
                        {
                            var ce = CoinStorage.getEntry(CoinStorage.CTYPE.BAR_SCORE);
                            if (ce != null) ce.Set((uint)Mathf.Max(0, act.IntParam), true, false);
                        }
                        break;

                    case "set_guild_points":
                        if (nm2d?.GUILD != null)
                        {
                            nm2d.GUILD.gq_point = act.IntParam;
                            nm2d.GUILD.need_fine_flag = true;
                        }
                        break;

                    case "set_lanthanum":
                        if (nm2d?.IMNG != null && NelItem.Lanthanum != null)
                        {
                            var inv = nm2d.IMNG.getInventory();
                            if (inv != null)
                            {
                                int cur = inv.getCount(NelItem.Lanthanum, -1);
                                int target = Mathf.Max(0, act.IntParam);
                                int diff = target - cur;
                                if (diff > 0)
                                {
                                    inv.Add(NelItem.Lanthanum, diff, 0, true, true);
                                }
                                else if (diff < 0)
                                {
                                    inv.Reduce(NelItem.Lanthanum, -diff, -1, true);
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError("[AICMod] HandleAction error: " + ex);
            }
        }

        public static void ApplyMpCrackValue(PR? pr, int targetCracks, bool force = false)
        {
            if (pr == null || pr.GaugeBrk == null) return;
            targetCracks = Mathf.Clamp(targetCracks, 0, 21);

            int cur = ReflectionHelper.GetValue<int>(pr.GaugeBrk, "break_cnt", "breakCnt");
            if (!force && cur == targetCracks) return;

            pr.GaugeBrk.reset();

            if (targetCracks > 0)
            {
                var agage = ReflectionHelper.GetValue<byte[]>(pr.GaugeBrk, "Agage_breaked", "agage_breaked");
                if (agage != null && agage.Length >= 3)
                {
                    int rem = targetCracks;
                    for (int i = 0; i < 3; i++)
                    {
                        int take = Mathf.Min(7, rem);
                        agage[i] = (byte)take;
                        rem -= take;
                    }
                }
                ReflectionHelper.SetValue(pr.GaugeBrk, targetCracks, "break_cnt", "breakCnt");
                ReflectionHelper.SetValue(pr.GaugeBrk, -1f, "safe_holdable_ratio_");
                _ = pr.GaugeBrk.safe_holdable_ratio;
                int actFlag = ReflectionHelper.GetValue<int>(pr.GaugeBrk, "active_flag", "activeFlag");
                ReflectionHelper.SetValue(pr.GaugeBrk, actFlag | 1, "active_flag", "activeFlag");
            }

            if (UIStatus.Instance != null)
            {
                if (targetCracks > 0)
                {
                    UIStatus.Instance.initCrack();
                }
                else
                {
                    UIStatus.Instance.quitCrack();
                }
                UIStatus.Instance.draw_crack = true;
                UIStatus.Instance.fineMpRatio(true, false);
            }
        }
    }
}
