using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using m2d;
using nel;
using nel.smnp;
using PixelLiner;
using UnityEngine;
using XX;

namespace AICMod.Patches
{
    /// <summary>
    /// 魔物召唤与复合嵌套实体、傀儡玩具及特殊机制实体的生命周期修复补丁。
    /// 确保在脱离官方关卡 Summoner（_Parent.Summoner == null）环境下：
    /// 1. 复合实体（森之主本体+囚笼+触手、五足魔物头部、多节水蛭等）能完整生成子部件并建立嵌套父子链接；
    /// 2. 特殊魔物（NelEnemy.initSummoned 族系）安全降级，不发生空引用崩溃；
    /// 3. 傀儡玩具族系（木偶/三角木马 NelNGolemToy）完成部件唤醒与渲染器装配；
    /// 4. 空中巡航魔物（法师、海胆、蜂巢等依赖 NASAirChaser 的魔物）在无 Summoner 时具备安全的回退区域容器。
    /// </summary>
    public static class SummonPatch
    {
        /// <summary>
        /// 拦截 NelEnemy.initSummoned：当没有官方关卡 Summoner 时，提供安全的状态初始化，
        /// 避免访问 Summoner.is_follower(K) 或 Summoner.is_auto_activate_first_summon 导致 NullReferenceException。
        /// 使得派生类（如 NelNGolemToy.initSummoned）能够顺利继续执行 initBorn0 完成装配。
        /// </summary>
        [HarmonyPatch(typeof(NelEnemy), "initSummoned", new[] { typeof(SmnEnemyKind), typeof(bool), typeof(int) })]
        [HarmonyPrefix]
        public static bool Prefix_NelEnemy_initSummoned(NelEnemy __instance, SmnEnemyKind K, bool is_sudden, int _dupe_count)
        {
            if (__instance.Summoner != null)
            {
                return true; // 有官方召唤器，走原版逻辑
            }

            // 无召唤器安全降级逻辑：
            __instance.is_follower = false;
            if (K != null && K.EnemyDesc.valid)
            {
                __instance.fly_enemy = K.EnemyDesc.flying_enemy;
            }

            if (!is_sudden)
            {
                __instance.changeState(NelEnemy.STATE.SUMMONED);
            }
            else
            {
                __instance.addF(NelEnemy.FLAG.NO_AUTO_LAND_EFFECT_FIRST);
            }

            __instance.fineDropItem();
            return false; // 跳过原版逻辑，严防 Summoner 空引用
        }

        /// <summary>
        /// 拦截 NelEnemyNested.CreateNest：当召唤复合魔物（如森之主 BOSS_NUSI 生成囚笼 BOSS_NUSI_CAGE 与 5 根触手 BOSS_NUSI_TENTACLE）时，
        /// 若 _Parent.Summoner 为 null，模拟官方 summonEnemyFromOther 流程，动态实例化子实体并完成嵌套父子挂载。
        /// </summary>
        [HarmonyPatch(typeof(NelEnemyNested), "CreateNest", new[] { typeof(NelEnemy), typeof(string), typeof(float), typeof(int) })]
        [HarmonyPrefix]
        public static bool Prefix_NelEnemyNested_CreateNest(NelEnemy _Parent, string enemykindkey, float mp_ratio, int array_create_capacity, ref NelEnemyNested? __result)
        {
            if (_Parent == null)
            {
                __result = null;
                return false;
            }

            if (_Parent.Summoner != null)
            {
                return true; // 官方召唤器存在，由游戏底层处理
            }

            try
            {
                var mp = _Parent.Mp;
                if (mp == null)
                {
                    __result = null;
                    return false;
                }

                var nm2d = mp.M2D as NelM2DBase;
                if (nm2d != null)
                {
                    try
                    {
                        NDAT.getResources(nm2d, enemykindkey);
                    }
                    catch (Exception resEx)
                    {
                        Debug.LogWarning($"[AICMod] CreateNest NDAT.getResources warning for {enemykindkey}: {resEx.Message}");
                    }
                }

                string gobKey = "-Nest-" + enemykindkey + "-" + UnityEngine.Random.Range(1000, 9999);
                NelEnemy nestedEnemy = NDAT.createByKey(mp, enemykindkey, gobKey);
                if (nestedEnemy == null)
                {
                    Debug.LogError("[AICMod] CreateNest failed to create enemy for key: " + enemykindkey);
                    __result = null;
                    return false;
                }

                nestedEnemy.first_mp_ratio = mp_ratio;
                if (_Parent.nattr != ENATTR.NORMAL)
                {
                    nestedEnemy.nattr = (ENATTR)((uint)_Parent.nattr & 0xFFE7FFFFu);
                }
                nestedEnemy.smn_xorsp = _Parent.smn_xorsp;

                nestedEnemy.transform.localPosition = new Vector3(mp.map2ux(_Parent.x), mp.map2uy(_Parent.y), 0f);

                // 注册到地图执行 appear(mp)
                mp.assignMover(nestedEnemy);

                // 对齐到父实体基础坐标
                nestedEnemy.setTo(_Parent.x, _Parent.y + 0.5f - nestedEnemy.sizey);

                // 觉醒初始化
                var k = new SmnEnemyKind(enemykindkey, 1, -1, mp_ratio, mp_ratio, "")
                {
                    no_add_appear_count = true,
                    nattr = _Parent.nattr
                };
                nestedEnemy.initSummoned(k, is_sudden: true, 0);

                // 结束召唤进入活跃
                nestedEnemy.quitSummonAndAppear(clearlock_on_summon: false);

                // 建立嵌套关联并返回
                if (nestedEnemy is NelEnemyNested nestChild)
                {
                    __result = nestChild.initNest(_Parent, array_create_capacity);
                    return false;
                }

                __result = null;
                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AICMod] CreateNest error for {enemykindkey}: {ex}");
                __result = null;
                return false;
            }
        }

        /// <summary>
        /// 拦截 EnemyAnimatorPxl.fineCurrentFrameData：
        /// 当新魔物生成但对应 Pxl 资源尚在加载或当前序列帧尚未就绪（getCurrentSequence() == null 或 countFrames() <= 0）时，
        /// 安全返回 null 并跳过本帧网格构建，避免在 appear() / checkFrame() 中发生 NullReferenceException。
        /// 后续帧中当 Pxl 资源解析完毕后，动画器会自动标记 need_fine 并平滑完成网格加载。
        /// </summary>
        [HarmonyPatch(typeof(EnemyAnimatorPxl), "fineCurrentFrameData")]
        [HarmonyPrefix]
        public static bool Prefix_EnemyAnimatorPxl_fineCurrentFrameData(EnemyAnimatorPxl __instance, ref PxlFrame? __result)
        {
            var sq = __instance.getCurrentSequence();
            if (sq == null || sq.countFrames() <= 0)
            {
                __result = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 拦截 EnemyAnimatorPxl.getCurrentDrawnFrame：
        /// 当动画序列尚未就绪时安全返回 null，避免调用 getCurrentSequence().getFrame(...) 引发空指针异常。
        /// </summary>
        [HarmonyPatch(typeof(EnemyAnimatorPxl), "getCurrentDrawnFrame")]
        [HarmonyPrefix]
        public static bool Prefix_EnemyAnimatorPxl_getCurrentDrawnFrame(EnemyAnimatorPxl __instance, ref PxlFrame? __result)
        {
            var sq = __instance.getCurrentSequence();
            if (sq == null || sq.countFrames() <= 0)
            {
                __result = null;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 拦截 NelNSnake.considerNormal：
        /// 修复原版游戏在 NelNSnake.cs 第 299 行遗漏判空 `Summoner != null` 直接访问 `!Summoner.isAccessable(...)` 导致的致命崩溃。
        /// 当 Summoner 为 null 时，整图均自由可达，跳过该越界惩罚分支，无缝执行常规 AI 思考。
        /// </summary>
        [HarmonyPatch(typeof(NelNSnake), "considerNormal", new[] { typeof(NAI) })]
        [HarmonyPrefix]
        public static bool Prefix_NelNSnake_considerNormal(NelNSnake __instance, NAI Nai, ref bool __result)
        {
            if (__instance.Summoner != null)
            {
                return true; // 有召唤器时执行游戏原版逻辑
            }

            if (Nai.fnAwakeBasicHead(Nai))
            {
                __result = true;
                return false;
            }

            var tkiGrab = NOD.getTackle("snake_grab");
            if (tkiGrab != null && !__instance.Useable(tkiGrab, X.Mx(1f, Nai.RANtk(4313) * 2.3f)) && Nai.AddTicketSearchAndGetManaWeed(200, 1f, -15f, 15f, 15f, -15f, 15f) != null)
            {
                __result = true;
                return false;
            }

            if (Nai.HasF(NAI.FLAG.ABSORB_FINISHED, remove_if_exist: true))
            {
                __result = Nai.AddTicketB(NAI.TYPE.WAIT, 200);
                return false;
            }

            if (Nai.HasF(NAI.FLAG.STUN_FINISHED, remove_if_exist: true) && Nai.fnBasicPunch(Nai, 200, 100f, 0f, 0f, 0f, 7111))
            {
                __result = true;
                return false;
            }

            if (!Nai.isPrAlive())
            {
                bool flag = Nai.HasF(NAI.FLAG.BOTHERED, remove_if_exist: true);
                if (Nai.target_slen_n < (__instance.nattr_has_big ? 7f : 3f) && !flag)
                {
                    if (Nai.fnBasicPunch(Nai, 200, 10f, 60f))
                    {
                        __result = true;
                        return false;
                    }
                }
                else if (Nai.fnBasicPunch(Nai, 200, flag ? 80 : 50))
                {
                    __result = true;
                    return false;
                }
                __result = Nai.AddTicketB(NAI.TYPE.WAIT, 200);
                return false;
            }

            if (!Nai.target_is_human)
            {
                if (Nai.target_sxdif <= (__instance.nattr_has_big ? 2f : 0.8f) && Nai.RANtk(5518) < 0.4f)
                {
                    Nai.AddTicket(NAI.TYPE.PUNCH_0, 200).Dep(Nai.target_x, Nai.target_y);
                    __result = true;
                    return false;
                }
                if (Nai.RANtk(4613) < X.NIL(0.4f, 0.8f, Nai.target_sxdif - 3.5f, 5f))
                {
                    Nai.AddTicket(NAI.TYPE.PUNCH, 200).Dep(Nai.target_x, Nai.target_y);
                    __result = true;
                    return false;
                }
            }
            // 原版第 299 行: else if (!Summoner.isAccessable(this, 0, Nai.target_x, Nai.target_y, use_shift_x: false))
            // 在无召唤器环境下整张地图均可到达，直接跳过此越界惩罚分支
            else if (Nai.isPrSpecialAttacking())
            {
                if (Nai.fnBasicPunch(Nai, 200, 15f, 85f, 0f, 0f, 7111))
                {
                    __result = true;
                    return false;
                }
            }
            else if (Nai.isPrMagicExploded())
            {
                if (Nai.fnBasicPunch(Nai, 200, 4f, Nai.here_dangerous ? 90 : 30, 0f, 0f, 7111))
                {
                    __result = true;
                    return false;
                }
            }
            else if (Nai.here_dangerous && Nai.canNoticeThat(2.4f))
            {
                if (Nai.fnBasicPunch(Nai, 200, 100f))
                {
                    __result = true;
                    return false;
                }
            }
            else
            {
                if (__instance.AimPr != null && Nai.target_slen_n < (__instance.nattr_has_big ? 7f : 3f) && X.Mx(X.Abs(Nai.target_lastfoot_bottom - __instance.AimPr.sizey * 1.2f - __instance.mtop), 0f) < 2.8f + __instance.sizey && Nai.target_lastfoot_bottom > __instance.mtop - 0.4f && Nai.target_lastfoot_bottom < __instance.mbottom + 0.1f && Nai.fnBasicPunch(Nai, 200, 0f, 0f, 80f))
                {
                    __result = true;
                    return false;
                }
                if (Nai.fnBasicPunch(Nai, 200, 25f))
                {
                    __result = true;
                    return false;
                }
            }

            __result = Nai.fnBasicMove(Nai);
            return false;
        }

        /// <summary>
        /// 拦截空中寻路辅助器 NASAirChaser 构造函数：当魔物没有关卡 Summoner 时，
        /// 构造一个基于全图的 M2AirRegionContainer，防止 En.Summoner.Summoner.getRegionCheckerAir() 崩溃。
        /// </summary>
        public static class Patch_NASAirChaser_Constructor
        {
            public static MethodBase? TargetMethod()
            {
                return AccessTools.Constructor(typeof(NASAirChaser), new[] { typeof(NelEnemy), typeof(NASAirChaser.FnProgressWalk) });
            }

            [HarmonyPrefix]
            public static bool Prefix(NASAirChaser __instance, NelEnemy _En, NASAirChaser.FnProgressWalk _fnProgressWalk)
            {
                if (_En?.Summoner?.Summoner != null)
                {
                    return true;
                }

                try
                {
                    var mp = _En?.Mp;
                    if (mp != null)
                    {
                        var wrc = new M2AirRegionContainer(mp, BCCLine.DANGERCHK._SUMMONER);
                        wrc.recognizeInit(mp, 0, 0, mp.clms, mp.rows);
                        wrc.recognize(-1);

                        var trav = Traverse.Create(__instance);
                        trav.Field("En").SetValue(_En);
                        trav.Field("WRC_Air").SetValue(wrc);
                        trav.Field("Cs").SetValue(new M2ChaserAir(_En, wrc));
                        trav.Field("fnProgressWalk").SetValue(_fnProgressWalk);
                        trav.Field("ADepert").SetValue(new List<Vector2>(2));
                        trav.Field("ABuf").SetValue(new List<M2AirRegion>());
                        trav.Field("hitwall_counter").SetValue(new RevCounter());
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError("[AICMod] NASAirChaser constructor patch error: " + ex);
                }
                return true;
            }
        }
    }
}
