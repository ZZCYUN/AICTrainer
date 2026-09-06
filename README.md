# AICTrainer - 《Alice in Cradle》专属内存注入修改器

[![License: GPL v3](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%20(x64)-informational.svg)](#)
[![Framework](https://img.shields.io/badge/.NET-10.0%20%7C%20WPF-purple.svg)](#)
[![Target Game](https://img.shields.io/badge/Game-Alice%20in%20Cradle-orange.svg)](#)

一款为横板动作 RPG 独立游戏《**Alice in Cradle**》量身打造的高性能、沉浸式、现代化桌面内存修改器。

采用 **原生 Mono CLR 线程注入 + Harmony 细粒度沙箱补丁 + 本地 Local TCP IPC + WPF MVVM 霓虹响应式界面** 架构设计。完全摒弃传统修改器脆弱的“静态内存地址/指针基址”，全流程基于游戏底层元数据动态寻址，具备极强的跨版本自适应与单点容错能力。

---

## 📸 界面预览

![界面预览](docs/preview.png)

---

## 🛠 技术架构与核心亮点

```
[ AICTrainer 客户端 (WPF / .NET 10) ]
        │
        ▼ Local TCP IPC (127.0.0.1:48123)
[ AICModPayload (C# / netstandard2.1) ]
        │
        ├── Mono 虚拟机进程内驻留 (mono_runtime_invoke)
        ├── ResilientPatcher (31 项沙箱隔离的容错 Harmony 补丁)
        └── 动态反射与事件拦截 (零硬编码内存地址，纯元数据动态绑定)
```

1. **零硬编码内存基址（Zero Hardcoded Addresses）**
   - 不依赖容易在版本更新后移位的静态偏移（如 `base+0x12345`），全部通过游戏 Mono 托管堆内存的对象引用、类元数据及反射查找。
2. **细粒度沙箱容错补丁管理器（ResilientPatcher）**
   - 包含 31 项功能独立补丁，全部隔离在独立的 Try-Catch 沙箱内。
   - 即使游戏小版本更新修改了其中某一个函数的形参，也只会安全跳过该单一补丁，绝不会导致整个修改器瘫痪或游戏闪退。
3. **安全双向 IPC 实时同步**
   - 客户端与注入模块通过 Local Loopback TCP 进行轻量级二进制/JSON 协议通信，双向心跳保活与状态秒级同步。
4. **游戏窗口就绪自动注入**
   - 支持一键启动游戏，自动监测游戏主窗口创建后延时就绪注入，彻底告别手动选择进程与繁琐操作。

---

## 🎮 核心功能一览

### 1. 玩家生存与状态 (Player)
- **无限生命 (HP)**：生命值持续锁定最大值。
- **无限精力 (SP)**：跑步、翻滚闪避不消耗体力。
- **无限魔力 (MP)**：施法不耗蓝。
- **锁定魔力破损为 0**：魔力容量上限始终保持完整，防止碎裂。
- **魔法蓄力秒满**：瞬发满蓄力高阶魔法。
- **无敌霸体**：免疫敌人攻击硬直、受击打断与击退。
- **异常状态无效**：免疫所有异常状态施加。

### 2. 战斗强化 (Combat)
- **一刀秒杀**：攻击命中敌人时造成基于生命倍率的秒杀伤害。
- **护盾不碎**：格挡护盾坚不可摧。
- **无限连跳**：打破跳跃段数限制，空中随意多段跳跃。
- **防滑防摔 (防地面力削减)**：锁定冰面与陡峭斜坡抓地力，免除滑步与失足。

### 3. 环境与陷阱免疫 (Hazard Immunity)
- **虫墙无效**：阻断食人虫墙拉扯判定。
- **荆棘伤害无效**：踩踏地图尖刺不扣血。
- **电击伤害无效**：免疫地图机关雷电与怪物雷属性攻击。
- **溺水窒息无效**：水下行动不产生窒息伤害。
- **酸液/毒雾无效**：完全免疫地图酸池与毒气区域。

### 4. 世界机制与传送 (World & Travel)
- **全天候快速移动**：打破夜间危险、恶劣天气与剧情封锁，长椅传送随时放行。
- **随时随地快速移动**：无需寻找长椅，大地图界面点击任意探索点长椅图标即可瞬间传送（虚拟沙箱长椅注入，跨区域无障碍瞬移）。
- **始终空腹加成**：进食始终享受 1.5 倍增益。
- **突破食物属性上限**：取消料理加成属性的上限钳位。
- **随意保存**：随时随地执行存档。
- **冻结倒计时**：锁定 Game Over 倒计时。
- **去除马赛克**：拦截原版马赛克着色器与网格渲染，还原本体画面。

### 5. 多种货币与资源修改 (Currencies)
- **金币 (Gold)**、**手工币 (Crafts)**、**果汁 (Juice)**、**酒吧积分 (Bar Score)**、**公会积分 (Guild Points)**、**镧矿材料 (Lanthanum)** 自由数值读取与写入。

---

## 🏗 开发与编译指南

### 1. 开发环境准备
- Windows 10 / 11 (x64)
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Visual Studio 2022 / JetBrains Rider / VS Code

### 2. 依赖引用配置
由于游戏程序集属于专有版权资产，仓库不直接附带游戏原版 DLL。请在编译 `AICModPayload` 之前，确保你的本地或环境中有游戏安装目录的 `Managed` 程序集：
- 默认路径：`C:\AliceInCradle\AliceInCradle_Data\Managed\`
  - `Assembly-CSharp.dll`
  - `unsafeAssem.dll`
  - `better.dll`
  - `pixelliner.dll`
  - `UnityEngine.dll` 等

### 3. 构建命令

```bash
# 1. 编译核心注入载荷
dotnet build src/AICModPayload/AICModPayload.csproj -c Release

# 2. 将编译后的 DLL 同步至客户端资源目录
copy src\AICModPayload\bin\Release\netstandard2.1\AICModPayload.dll src\AICTrainer\Resources\AICModPayload.dll

# 3. 编译发布客户端 - 轻量依赖版 (~8 MB, 需预装 .NET 10 运行时)
dotnet publish src/AICTrainer/AICTrainer.csproj -c Release -r win-x64 --self-contained false -o publish/light

# 4. 编译发布客户端 - 全内置独立运行版 (~147 MB, 零环境依赖，开箱即用)
dotnet publish src/AICTrainer/AICTrainer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/full
```

---

## 📄 免责声明 / Disclaimer

1. **技术研究目的**：本项目仅供 C# / .NET 逆向工程、Unity Mono 运行时扩展与 WPF 桌面开发的技术学习与学术交流使用。
2. **适用范围**：本工具仅适用于《Alice in Cradle》单机个人离线游玩，**严禁用于任何商业用途、倒卖牟利或非法传播**。
3. **游戏版权声明**：游戏中所有原始代码、剧本、角色、音乐与美术资产的知识产权均归属于游戏原开发团队 **NanameHacha**。
4. **第三方美术资产授权**：
   - 看板娘立绘插画来源于 Pixiv 优秀画师作品：[Pixiv #149248561](https://www.pixiv.net/artworks/149248561)，版权归原作者所有。
   - 表情包切图来源于《Alice in Cradle》官方表情图集。
5. **请勿向官方反馈修改异常**：使用本修改器导致的一切存档异常、数据损坏或游戏报错由使用者自行承担，**切勿将开启修改器引发的任何异常向游戏官方提出 BUG 反馈**，以免给原作者造成负面困扰。

---

## ⚖️ 开源协议 / License

本项目遵循 [GNU General Public License v3.0 (GPLv3)](LICENSE) 开源协议。
