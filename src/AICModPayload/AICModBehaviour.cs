using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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
                // 0. 确保判定框渲染器就绪
                EnsureDrawer();

                // 1. 在主线程中安全处理 IPC 远程指令
                while (IncomingActions.TryDequeue(out var act))
                {
                    try
                    {
                        HandleAction(act);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AICMod] HandleAction '{act.ActionName}' error: {ex}");
                    }
                }

                // 2. 状态锁定与自适应修改
                var cfg = AICModConfig.Current;
                var pr = GetPlayer();
                var nm2d = GetM2D();

                if (pr != null)
                {
                    try { ApplyPlayerLocks(pr, cfg); } catch (Exception ex) { Debug.LogWarning("[AICMod] ApplyPlayerLocks error: " + ex.Message); }
                }

                if (nm2d != null)
                {
                    try { ApplyWorldLocks(nm2d, cfg); } catch (Exception ex) { Debug.LogWarning("[AICMod] ApplyWorldLocks error: " + ex.Message); }
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
                            SafeAddItem(inv, NelItem.Lanthanum, diff, 0);
                        }
                        else if (diff < 0)
                        {
                            SafeReduceItem(inv, NelItem.Lanthanum, -diff, -1);
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

                        if (!_hasSentMapList)
                        {
                            _hasSentMapList = true;
                            SendMapList();
                        }

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
                    case "GetMapList":
                        SendMapList();
                        break;

                    case "ChangeMap":
                        if (!string.IsNullOrEmpty(act.StringParam))
                        {
                            TransferToMap(act.StringParam);
                        }
                        break;

                    case "GetItemList":
                        SendItemList();
                        break;

                    case "AddItem":
                        if (!string.IsNullOrEmpty(act.StringParam))
                        {
                            GiveItem(act.StringParam, act.IntParam > 0 ? act.IntParam : 1);
                        }
                        break;

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
                                    SafeAddItem(inv, NelItem.Lanthanum, diff, 0);
                                }
                                else if (diff < 0)
                                {
                                    SafeReduceItem(inv, NelItem.Lanthanum, -diff, -1);
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

        private static void SafeAddItem(ItemStorage inv, NelItem item, int count, int grade = 0)
        {
            try
            {
                var method5 = typeof(ItemStorage).GetMethod("Add", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool), typeof(bool) });
                if (method5 != null)
                {
                    method5.Invoke(inv, new object[] { item, count, grade, true, true });
                    return;
                }
                var method4 = typeof(ItemStorage).GetMethod("Add", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool) });
                if (method4 != null)
                {
                    method4.Invoke(inv, new object[] { item, count, grade, true });
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] SafeAddItem error: " + ex.Message);
            }
        }

        private static void SafeReduceItem(ItemStorage inv, NelItem item, int count, int grade = -1)
        {
            try
            {
                var method5 = typeof(ItemStorage).GetMethod("Reduce", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool), typeof(bool) });
                if (method5 != null)
                {
                    method5.Invoke(inv, new object[] { item, count, grade, true, false });
                    return;
                }
                var method4 = typeof(ItemStorage).GetMethod("Reduce", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool) });
                if (method4 != null)
                {
                    method4.Invoke(inv, new object[] { item, count, grade, true });
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] SafeReduceItem error: " + ex.Message);
            }
        }

        private static AICModHitboxDrawer? _drawer;

        public static void EnsureDrawer()
        {
            if (_drawer == null)
            {
                try
                {
                    var go = new GameObject("AICMod_HitboxDrawer");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _drawer = go.AddComponent<AICModHitboxDrawer>();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[AICMod] EnsureDrawer warning: " + ex.Message);
                }
            }
        }

        #region Map List & Transfer System

        private static MapListDto? _cachedMapListDto;
        private static Dictionary<string, string>? _cachedZhNames;
        private static bool _hasSentMapList;

        public static Dictionary<string, string> LoadChineseMapNames()
        {
            if (_cachedZhNames != null) return _cachedZhNames;
            _cachedZhNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, "localization", "zh-cn", "zh-cn_tx_map_name.txt");
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path, System.Text.Encoding.UTF8))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("&&MAP_"))
                        {
                            var match = Regex.Match(trimmed, @"^&&MAP_(\w+)\s+(.+)$");
                            if (match.Success)
                            {
                                _cachedZhNames[match.Groups[1].Value] = match.Groups[2].Value.Trim();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] Failed to load zh-cn map names: " + ex.Message);
            }
            return _cachedZhNames;
        }

        public static string GetAreaChineseName(string areaKey)
        {
            switch (areaKey.ToLowerInvariant())
            {
                case "house": return "魔女之家";
                case "forest": return "纺织之森";
                case "city": return "恩惠小镇";
                case "school": return "贝尔米特学院";
                case "mount": return "陨落山脉";
                case "glacier": return "冰川";
                case "sea": return "海洋";
                case "sacred": return "圣母石";
                case "labo": return "地下研究所";
                case "mine": return "矿区";
                case "debug": return "调试地图";
                default: return areaKey;
            }
        }

        public static MapListDto GetOrCreateMapList()
        {
            if (_cachedMapListDto != null && _cachedMapListDto.Maps.Count > 0)
            {
                return _cachedMapListDto;
            }

            var allKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. 从 StreamingAssets/m2d/__m2d_list.dat 读取全量游戏地图
            try
            {
                string listDatPath = Path.Combine(Application.streamingAssetsPath, "m2d", "__m2d_list.dat");
                if (File.Exists(listDatPath))
                {
                    foreach (var rawLine in File.ReadAllLines(listDatPath))
                    {
                        string k = rawLine.Trim();
                        if (string.IsNullOrEmpty(k) || k.StartsWith("_")) continue;
                        allKeys.Add(k);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] Failed to read __m2d_list.dat: " + ex.Message);
            }

            // 2. 结合运行时 M2D 容器中的地图
            var nm2d = GetM2D();
            if (nm2d != null)
            {
                try
                {
                    var mapObj = nm2d.getMapObject();
                    if (mapObj != null)
                    {
                        foreach (var pair in mapObj)
                        {
                            string key = pair.Key;
                            Map2d map = pair.Value;
                            if (string.IsNullOrEmpty(key) || key.StartsWith("_") || (map != null && (map.is_submap || map.is_whole)))
                            {
                                continue;
                            }
                            allKeys.Add(key);
                        }
                    }
                }
                catch { }
            }

            if (allKeys.Count == 0) return new MapListDto();

            var zhMapNames = LoadChineseMapNames();
            var list = new List<MapEntryDto>();

            foreach (var key in allKeys)
            {
                string areaKey = key.Contains('_') ? key.Substring(0, key.IndexOf('_')) : key;
                string areaName = GetAreaChineseName(areaKey);

                string mapName = string.Empty;
                if (zhMapNames.TryGetValue(key, out string? zhName) && !string.IsNullOrEmpty(zhName))
                {
                    mapName = zhName;
                }
                else
                {
                    try
                    {
                        mapName = TX.Get("MAP_" + key, "");
                    }
                    catch { }
                }

                list.Add(new MapEntryDto
                {
                    Key = key,
                    Name = mapName,
                    AreaKey = areaKey,
                    AreaName = areaName
                });
            }

            // 优先排序：大区域排序，有中文名优先，再按 key 排序
            list.Sort((a, b) =>
            {
                int c = string.Compare(a.AreaKey, b.AreaKey, StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                bool aHas = !string.IsNullOrEmpty(a.Name);
                bool bHas = !string.IsNullOrEmpty(b.Name);
                if (aHas != bHas) return aHas ? -1 : 1;
                return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
            });

            _cachedMapListDto = new MapListDto { Maps = list };
            return _cachedMapListDto;
        }

        public static void SendMapList()
        {
            try
            {
                var mapList = GetOrCreateMapList();
                if (mapList.Maps.Count > 0)
                {
                    IpcServer.Instance.SendMapList(mapList);
                    Debug.Log($"[AICMod] Sent {mapList.Maps.Count} maps to client");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] SendMapList failed: " + ex.Message);
            }
        }

        public static bool TransferToMap(string mapKey)
        {
            var nm2d = GetM2D();
            if (nm2d == null || string.IsNullOrEmpty(mapKey)) return false;

            try
            {
                if (nm2d.curMap != null && string.Equals(nm2d.curMap.key, mapKey, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[AICMod] Already in map {mapKey}");
                    return true;
                }

                // 退出可能正在打开的UI菜单
                if (nm2d.GM != null)
                {
                    nm2d.GM.deactivate(true);
                }

                // 直接调用游戏官方原版内置地图传送实现：_DEBUG_GOTO_MAP
                evt.EV.stack("_DEBUG_GOTO_MAP", 0, -1, new string[1] { mapKey });
                Debug.Log($"[AICMod] Executed official _DEBUG_GOTO_MAP to {mapKey}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AICMod] Failed to execute official _DEBUG_GOTO_MAP: {ex}");
                return false;
            }
        }

        #endregion

        #region Item Get System

        public static string GetItemCategoryChineseName(string category)
        {
            switch (category)
            {
                case "OTHER": return "其他";
                case "INDIVIDUAL_GRADE": return "个体品质";
                case "CURE_HP": return "回复HP";
                case "CURE_MP": return "回复MP";
                case "CURE_EP": return "回复EP";
                case "MTR": return "素材";
                case "FOR_FISHING": return "钓鱼";
                case "WATER": return "水";
                case "CURE_MP_CRACK": return "修复裂痕";
                case "SER_APPLY": return "状态赋予";
                case "SER_CURE": return "状态解除";
                case "FRUIT": return "果实";
                case "FOOD": return "料理";
                case "DUST": return "尘土";
                case "ANC": return "古董";
                case "TOOL": return "工具";
                case "SPECIAL": return "特殊";
                case "SPECIAL_USE": return "特殊用途";
                case "ENHANCER": return "强化";
                case "BOMB": return "炸弹";
                case "BOTTLE": return "瓶子";
                default: return category;
            }
        }

        /// <summary>
        /// 从游戏程序集全量字典 NelItem.getWholeDictionary() 采集所有物品（key + 中文名 + 分类 + 持有数），
        /// 与地图列表一样每次请求实时重建，保证持有数准确
        /// </summary>
        public static ItemListDto GetItemList()
        {
            var list = new List<ItemEntryDto>();
            try
            {
                var whole = NelItem.getWholeDictionary();
                var inv = GetM2D()?.IMNG?.getInventory();

                foreach (KeyValuePair<string, NelItem> pair in whole)
                {
                    var itm = pair.Value;
                    if (itm == null) continue;

                    try { if (itm.is_cache_item) continue; } catch { }

                    string name = itm.key;
                    try
                    {
                        string n = itm.getLocalizedName(0);
                        if (string.IsNullOrWhiteSpace(n) || n == itm.key)
                        {
                            n = itm.fineNameLocalized();
                        }
                        if (!string.IsNullOrWhiteSpace(n)) name = n;
                    }
                    catch { }

                    string cat = "OTHER";
                    try { cat = itm.category.ToString(); } catch { }

                    int owned = -1;
                    try { if (inv != null) owned = inv.getCount(itm, -1); } catch { }

                    list.Add(new ItemEntryDto
                    {
                        Key = itm.key,
                        Name = name,
                        Category = cat,
                        CategoryZh = GetItemCategoryChineseName(cat),
                        OwnedCount = owned
                    });
                }

                list.Sort((a, b) =>
                {
                    int c = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
                    if (c != 0) return c;
                    bool aHas = !string.IsNullOrEmpty(a.Name);
                    bool bHas = !string.IsNullOrEmpty(b.Name);
                    if (aHas != bHas) return aHas ? -1 : 1;
                    return string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] GetItemList error: " + ex.Message);
            }
            return new ItemListDto { Items = list };
        }

        public static void SendItemList()
        {
            try
            {
                var itemList = GetItemList();
                if (itemList.Items.Count > 0)
                {
                    IpcServer.Instance.SendItemList(itemList);
                    Debug.Log($"[AICMod] Sent {itemList.Items.Count} items to client");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[AICMod] SendItemList failed: " + ex.Message);
            }
        }

        /// <summary>
        /// 获取任意物品：调用游戏官方 debug 入包方法 NelItemManager.getItem
        /// （官方 GET_ALL_ITEM 调试命令同款实现），而非自行操作 ItemStorage
        /// </summary>
        public static bool GiveItem(string itemKey, int count)
        {
            var nm2d = GetM2D();
            if (nm2d == null || nm2d.IMNG == null || string.IsNullOrEmpty(itemKey) || count <= 0) return false;

            try
            {
                var item = NelItem.GetById(itemKey, false);
                if (item == null)
                {
                    Debug.LogWarning($"[AICMod] Unknown item key: {itemKey}");
                    return false;
                }

                int added = InvokeOfficialGetItem(nm2d.IMNG, item, count);
                Debug.Log($"[AICMod] Official NelItemManager.getItem added {added} x {itemKey}");
                return added > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AICMod] GiveItem failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// 反射调用官方 NelItemManager.getItem，多签名回退防止版本升级签名漂移导致 JIT 异常
        /// </summary>
        private static int InvokeOfficialGetItem(NelItemManager imng, NelItem item, int count)
        {
            var t = typeof(NelItemManager);
            var m7 = t.GetMethod("getItem", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
            if (m7 != null) return (int)m7.Invoke(imng, new object[] { item, count, 0, false, false, false, false });
            var m6 = t.GetMethod("getItem", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool), typeof(bool), typeof(bool) });
            if (m6 != null) return (int)m6.Invoke(imng, new object[] { item, count, 0, false, false, false });
            var m5 = t.GetMethod("getItem", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool), typeof(bool) });
            if (m5 != null) return (int)m5.Invoke(imng, new object[] { item, count, 0, false, false });
            var m4 = t.GetMethod("getItem", new[] { typeof(NelItem), typeof(int), typeof(int), typeof(bool) });
            if (m4 != null) return (int)m4.Invoke(imng, new object[] { item, count, 0, false });
            var m3 = t.GetMethod("getItem", new[] { typeof(NelItem), typeof(int), typeof(int) });
            if (m3 != null) return (int)m3.Invoke(imng, new object[] { item, count, 0 });
            var m2 = t.GetMethod("getItem", new[] { typeof(NelItem), typeof(int) });
            if (m2 != null) return (int)m2.Invoke(imng, new object[] { item, count });
            var m1 = t.GetMethod("getItem", new[] { typeof(NelItem) });
            if (m1 != null) return (int)m1.Invoke(imng, new object[] { item });
            throw new MissingMethodException("NelItemManager.getItem not found");
        }

        #endregion
    }

    /// <summary>
    /// 判定框绘制组件：实时在屏幕上将受击框（蓝框）与攻击框（红框）绘制到对应单位与攻击上
    /// 完全使用 Unity 原生 OnGUI 机制，零着色器/材质入侵，极致轻量与兼容
    /// </summary>
    public class AICModHitboxDrawer : MonoBehaviour
    {
        private static Texture2D? _whiteTex;
        public static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _whiteTex.SetPixel(0, 0, Color.white);
                    _whiteTex.Apply();
                }
                return _whiteTex;
            }
        }

        private static GUIStyle? _labelStyle;
        public static GUIStyle LabelStyle
        {
            get
            {
                if (_labelStyle == null)
                {
                    _labelStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 11,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.UpperLeft,
                        wordWrap = false,
                        normal = { textColor = Color.white }
                    };
                }
                return _labelStyle;
            }
        }

        private void OnGUI()
        {
            if (!AICModConfig.Current.ShowHitboxes) return;

            var nm2d = M2DBase.Instance as NelM2DBase;
            if (nm2d == null || nm2d.curMap == null) return;
            var curMap = nm2d.curMap;

            Camera? cam = null;
            if (nm2d.Cam != null)
            {
                try { cam = nm2d.Cam.CamForMover; } catch { }
                if (cam == null || !cam.enabled)
                {
                    try { cam = nm2d.Cam.get_FinalCamera(); } catch { }
                }
                if (cam == null || !cam.enabled)
                {
                    try { cam = nm2d.Cam.CamForEditor; } catch { }
                }
            }
            if (cam == null) cam = Camera.main;
            if (cam == null) return;

            // 1. 绘制受击框（蓝框）：当前地图上所有存活的攻击目标单位（Noel、敌怪等）
            DrawHurtboxes(nm2d, curMap, cam);

            // 2. 绘制攻击判定框（红框）：当前活跃的全部攻击、魔法与投射物
            DrawHitboxes(nm2d, curMap, cam);
        }

        private void DrawHurtboxes(NelM2DBase nm2d, Map2d curMap, Camera cam)
        {
            var movers = curMap.getVectorMover();
            if (movers == null) return;

            var cfg = AICModConfig.Current;

            for (int i = 0; i < movers.Length; i++)
            {
                var mv = movers[i];
                if (mv == null || mv.destructed) continue;
                if (!(mv is M2Attackable atk) || !atk.is_alive) continue;

                Rect guiRect;
                var cc = mv.getColliderCreator();
                Collider2D? cld = cc?.Cld ?? mv.GetComponent<Collider2D>();
                if (cld != null && cld.enabled)
                {
                    var bounds = cld.bounds;
                    var p1 = WorldToGuiPoint(nm2d, cam, new Vector3(bounds.min.x, bounds.max.y, bounds.min.z));
                    var p2 = WorldToGuiPoint(nm2d, cam, new Vector3(bounds.max.x, bounds.min.y, bounds.min.z));
                    guiRect = RectFromPoints(p1, p2);
                }
                else
                {
                    var p1 = MapToGuiPoint(nm2d, curMap, cam, mv.mleft, mv.mtop);
                    var p2 = MapToGuiPoint(nm2d, curMap, cam, mv.mright, mv.mbottom);
                    guiRect = RectFromPoints(p1, p2);
                }

                if (IsOffscreen(guiRect)) continue;

                string unitName;
                int curHp = (int)atk.get_hp();
                int maxHp = (int)atk.get_maxhp();
                int curMp = (int)atk.get_mp();
                int maxMp = (int)atk.get_maxmp();
                bool hasMp = maxMp > 0;

                if (mv is PRNoel)
                {
                    unitName = "Noel";
                    hasMp = true;
                }
                else if (mv is NelEnemy en)
                {
                    unitName = $"[EN] {en.name}";
                }
                else
                {
                    unitName = $"[Unit] {mv.name}";
                }

                var lines = new List<string>(3);
                if (cfg.HitboxShowName) lines.Add(unitName);
                if (cfg.HitboxShowHp) lines.Add($"HP: {curHp}/{maxHp}");
                if (cfg.HitboxShowMp && hasMp) lines.Add($"MP: {curMp}/{maxMp}");

                string? label = (lines.Count > 0) ? string.Join("\n", lines) : null;

                DrawBox(
                    guiRect,
                    new Color(0.1f, 0.65f, 1.0f, 0.15f), // 蓝框半透明填充
                    new Color(0.1f, 0.65f, 1.0f, 0.90f), // 蓝框边框
                    2f,
                    label,
                    new Color(0.6f, 0.9f, 1.0f, 1.0f)
                );
            }
        }

        private void DrawHitboxes(NelM2DBase nm2d, Map2d curMap, Camera cam)
        {
            var mgc = nm2d.MGC;
            if (mgc == null) return;

            var cfg = AICModConfig.Current;

            int count = mgc.Length;
            for (int i = 0; i < count; i++)
            {
                var mg = mgc.getMg(i);
                if (mg == null || mg.killed || mg.closed) continue;

                Rect guiRect;
                bool isNoelAtk = mg.Caster is PR || (mg.Caster is M2Mover mvC && mvC is PR);
                float atkScale = 1f;
                if (isNoelAtk && cfg.EnableNoelAttackScale && cfg.NoelAttackScale > 1f)
                {
                    if (!cfg.NoelAttackOnlyMelee || Patches.CombatPatch.Patch_M2Ray_Cast.IsMeleeKind(mg.kind))
                    {
                        atkScale = cfg.NoelAttackScale;
                    }
                }

                if (mg.Ray != null)
                {
                    var ray = mg.Ray;
                    M2Ray.shape2Size(ray.shape, out float xl, out float yl, out _);
                    if (xl <= 0f) xl = 1f;
                    if (yl <= 0f) yl = 1f;
                    float rx = ray.radius_map * xl * atkScale;
                    float ry = ray.radius_map * yl * atkScale;
                    Vector2 mapPos = ray.getMapPos();

                    float minMapX, maxMapX, minMapY, maxMapY;
                    if (ray.lenmp <= 0.001f)
                    {
                        minMapX = mapPos.x - rx;
                        maxMapX = mapPos.x + rx;
                        minMapY = mapPos.y - ry;
                        maxMapY = mapPos.y + ry;
                    }
                    else
                    {
                        float endX = mapPos.x + ray.difmapx * atkScale;
                        float endY = mapPos.y + ray.difmapy * atkScale;
                        minMapX = Mathf.Min(mapPos.x, endX) - rx;
                        maxMapX = Mathf.Max(mapPos.x, endX) + rx;
                        minMapY = Mathf.Min(mapPos.y, endY) - ry;
                        maxMapY = Mathf.Max(mapPos.y, endY) + ry;
                    }

                    var p1 = MapToGuiPoint(nm2d, curMap, cam, minMapX, minMapY);
                    var p2 = MapToGuiPoint(nm2d, curMap, cam, maxMapX, maxMapY);
                    guiRect = RectFromPoints(p1, p2);
                }
                else
                {
                    float cx = mg.Cen.x;
                    float cy = mg.Cen.y;
                    float r = ((mg.sz > 0f) ? mg.sz : 0.6f) * atkScale;
                    var p1 = MapToGuiPoint(nm2d, curMap, cam, cx - r, cy - r);
                    var p2 = MapToGuiPoint(nm2d, curMap, cam, cx + r, cy + r);
                    guiRect = RectFromPoints(p1, p2);
                }

                if (IsOffscreen(guiRect)) continue;

                string? label = null;
                if (cfg.HitboxShowAtkInfo)
                {
                    string casterName = (mg.Caster is M2Mover mvCaster) ? mvCaster.name : "Map";
                    label = $"[ATK] {mg.kind} ({casterName})";
                }

                DrawBox(
                    guiRect,
                    new Color(1.0f, 0.25f, 0.25f, 0.20f), // 红框半透明填充
                    new Color(1.0f, 0.25f, 0.25f, 0.90f), // 红框边框
                    2f,
                    label,
                    new Color(1.0f, 0.75f, 0.75f, 1.0f)
                );
            }
        }

        private static float GetScreenScaleRatio()
        {
            try
            {
                if (IN.pixel_scale > 0.01f)
                {
                    return IN.pixel_scale;
                }
            }
            catch { }

            float sw = Screen.width;
            float sh = Screen.height;
            if (sh <= 0f) return 1f;

            float aspect = sw / sh;
            const float baseAspect = 1280f / 720f;

            if (aspect >= baseAspect)
            {
                return sh / 720f;
            }
            else
            {
                return sw / 1280f;
            }
        }

        private static Vector2 WorldToGuiPoint(NelM2DBase nm2d, Camera cam, Vector3 worldPos)
        {
            Vector3 sp = cam.WorldToScreenPoint(worldPos);
            if (sp.z < 0f) return new Vector2(-10000f, -10000f);

            float scaleRatio = GetScreenScaleRatio();
            float gx, gy;

            if (cam.targetTexture != null)
            {
                float pw = cam.pixelWidth > 0 ? cam.pixelWidth : 1280f;
                float ph = cam.pixelHeight > 0 ? cam.pixelHeight : 720f;
                gx = Screen.width * 0.5f + (sp.x - pw * 0.5f) * scaleRatio;
                gy = Screen.height * 0.5f - (sp.y - ph * 0.5f) * scaleRatio;
            }
            else
            {
                float pw = cam.pixelWidth > 0 ? cam.pixelWidth : Screen.width;
                float ph = cam.pixelHeight > 0 ? cam.pixelHeight : Screen.height;
                gx = sp.x * ((float)Screen.width / pw);
                gy = (ph - sp.y) * ((float)Screen.height / ph);
            }

            // 适配立绘展示 (ui_shift_x) 带来的全局画面水平偏移
            if (nm2d != null && nm2d.ui_shift_x != 0f)
            {
                gx += nm2d.ui_shift_x * scaleRatio;
            }

            return new Vector2(gx, gy);
        }

        private static Vector2 MapToGuiPoint(NelM2DBase nm2d, Map2d curMap, Camera cam, float mapX, float mapY)
        {
            if (curMap.gameObject != null)
            {
                float lx = curMap.map2meshx(mapX) * (1f / 64f);
                float ly = curMap.map2meshy(mapY) * (1f / 64f);
                Vector3 worldPos = curMap.gameObject.transform.TransformPoint(lx, ly, 0f);
                return WorldToGuiPoint(nm2d, cam, worldPos);
            }
            return Vector2.zero;
        }

        private static Rect RectFromPoints(Vector2 p1, Vector2 p2)
        {
            float left = Mathf.Min(p1.x, p2.x);
            float top = Mathf.Min(p1.y, p2.y);
            float width = Mathf.Max(1f, Mathf.Abs(p2.x - p1.x));
            float height = Mathf.Max(1f, Mathf.Abs(p2.y - p1.y));
            return new Rect(left, top, width, height);
        }

        private static bool IsOffscreen(Rect r)
        {
            return r.xMax < -50f || r.x > Screen.width + 50f || r.yMax < -50f || r.y > Screen.height + 50f;
        }

        private static void DrawBox(Rect rect, Color fillColor, Color borderColor, float borderWidth, string? label, Color labelColor)
        {
            // 填充背景
            if (fillColor.a > 0f)
            {
                GUI.color = fillColor;
                GUI.DrawTexture(rect, WhiteTex);
            }

            // 边框绘制
            if (borderColor.a > 0f && borderWidth > 0f)
            {
                GUI.color = borderColor;
                // 上
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, borderWidth), WhiteTex);
                // 下
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - borderWidth, rect.width, borderWidth), WhiteTex);
                // 左
                GUI.DrawTexture(new Rect(rect.x, rect.y + borderWidth, borderWidth, rect.height - borderWidth * 2f), WhiteTex);
                // 右
                GUI.DrawTexture(new Rect(rect.xMax - borderWidth, rect.y + borderWidth, borderWidth, rect.height - borderWidth * 2f), WhiteTex);
            }

            // 标签文字
            if (!string.IsNullOrEmpty(label))
            {
                GUIContent content = new GUIContent(label);
                Vector2 size = LabelStyle.CalcSize(content);
                float labelW = size.x + 8f;
                float labelH = size.y + 4f;
                float labelY = rect.y - labelH - 1f;
                if (labelY < 2f) labelY = rect.y + 2f;
                Rect labelRect = new Rect(rect.x, labelY, labelW, labelH);

                // 标签背景
                GUI.color = new Color(0f, 0f, 0f, 0.80f);
                GUI.DrawTexture(labelRect, WhiteTex);

                // 标签细框
                GUI.color = borderColor;
                GUI.DrawTexture(new Rect(labelRect.x, labelRect.y, labelRect.width, 1f), WhiteTex);

                // 标签文字
                GUI.color = labelColor;
                GUI.Label(new Rect(labelRect.x + 4f, labelY + 2f, size.x, size.y), content, LabelStyle);
            }

            GUI.color = Color.white;
        }
    }
}
