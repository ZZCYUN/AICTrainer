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

## 🎮 核心功能一览 (37 项)

### 1. 属性与生存 (Survival)
- **生命值 (HP)**：实时读取当前值，支持自定义修改目标生命与持续锁定。
- **法力值 (MP)**：实时读取当前值，支持自定义修改目标法力与持续锁定。
- **MP 量条破损格数**：读取当前破损数，支持修复或锁定破损为 0（保持蓝槽上限完整）。
- **基础背包容量**：自由扩展随身携带物品栏格数。
- **最大饱食度**：修改角色饥饿/饱食度最大上限。
- **当前饱食度**：实时读取与自定义填充饱食度。
- **EP / 欲情度**：实时监控欲情数值，支持自由修改或一键快速清零。
- **异常状态免疫**：全面免疫中毒、束缚、麻痹等一切负面异常状态。

### 2. 移动与物理 (Mobility)
- **步行速度倍率**：自由调节走路移动倍率，支持持续锁定。
- **奔跑速度倍率**：自由调节疾跑速度倍率，支持持续锁定。
- **地面摩擦抓地力**：锁定在冰面、泥泞等湿滑地形的抓地力，告别滑步。
- **防滑 / 防地面力削减**：彻底锁定地面受力不衰减，在陡坡、冰层行动自如。
- **受击击退硬直时间**：自由调整受击后的硬直判定时长（设为 0 即无敌霸体不受击打断）。
- **无限多段连跳**：打破跳跃次数限制，空中随意无限段连跳。

### 3. 战斗强化 (Combat)
- **一击必杀**：攻击命中敌人时造成巨额真实伤害秒杀目标。
- **格挡护盾不碎**：格挡与举盾判定下不消耗护盾耐久度。
- **魔法蓄力秒满**：瞬发满蓄力高阶魔法，无需读条等待。

### 4. 环境与陷阱免疫 (Hazard Immunity)
- **食人虫墙陷阱免疫**：阻断触手与食人虫墙的判定与拉扯拘束。
- **地图荆棘尖刺免伤**：踩踏地图尖刺陷阱不受伤害。
- **雷击机关免伤**：免疫机关落雷与电击陷阱扣血。
- **溺水窒息免伤**：水下行动不产生窒息判定与伤害。
- **酸池与毒气免伤**：完全免疫地图强酸液体与毒气区域腐蚀。

### 5. 地图与世界机制 (World)
- **全天候长椅快速移动**：打破夜晚危险、恶劣天气与剧情封锁，长椅传送随时放行。
- **随时随地大地图传送**：无需寻找长椅，大地图界面点击任意长椅节点直接跨区域瞬移。
- **地图危险等级**：实时读取、自定义修改并支持锁定当前地图危险度。
- **进食始终享受空腹加成**：享用料理时强制激活 1.5 倍美味增益加成。
- **随时随地呼出存档**：无论身处何地均可随时打开存档界面保存游戏进度。
- **冻结倒计时**：锁定败北处刑或 Game Over 倒计时。
- **装备槽位扩展**：自由拓展角色身上穿戴的装备槽位数量。
- **突破料理属性增益上限**：取消料理属性增益的硬性阈值上限。
- **去除全屏马赛克**：拦截原版马赛克着色器与网格渲染，还原本体画面。

### 6. 资产与多种货币修改 (Currencies)
- **金币 (Gold)**：读取与修改当前持有金币。
- **手工币 (Crafts)**：读取与修改手工制作币。
- **果汁 (Juice)**：读取与修改果汁储备。
- **酒吧积分 (Bar Score)**：读取与修改酒吧小游戏积分。
- **公会积分 (Guild Points)**：读取与修改公会探索点数。
- **镧矿材料 (Lanthanum)**：读取与修改稀有镧矿材料数量。

---

## 📖 使用方法 / Usage

### 1. 版本选择
本项目在 [Releases](https://github.com/ZZCYUN/AICTrainer/releases) 提供两种不同构建形态的二进制发布程序：
- **`AICTrainer-Full.exe`（推荐，全内置独立运行版）**：
  - 体积约 140 MB，已内置完整 .NET 10 运行时与 WPF 图形渲染引擎，**零系统环境依赖，双击即开箱即用**。
- **`AICTrainer.exe`（轻量依赖版）**：
  - 体积仅约 8 MB，启动极快，但需要使用者的系统预先安装 [.NET 10.0 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/10.0)。

### 2. 运行与注入流程
1. **放置位置（可选但推荐）**：
   - 建议直接将 `AICTrainer.exe` 或 `AICTrainer-Full.exe` 放置在游戏安装根目录下（与 `AliceInCradle.exe` 同目录）。
2. **启动与连接**：
   - **方式 A（一键启动）**：双击启动修改器，点击右上角【🚀 启动游戏】按钮，修改器将自动拉起游戏并在游戏窗口创建就绪后自动注入连接。
   - **方式 B（手动启动）**：先打开游戏进入主菜单或存档，随后打开修改器，点击右上角【⚡ 注入 / 连接】即可。
3. **状态指示**：
   - 当顶栏状态指示灯显示绿色 **`已成功连接游戏 (PID: xxxx)`** 并同步出当前地图名称时，即表示注入已就绪。
4. **个性化收藏**：
   - 在【全部功能】或各分类中，点击卡片右上角的 **【☆ 收藏】** 即可将常用功能固定在 **【🌟 我的收藏】** 标签页，便于高频使用。

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
dotnet publish src/AICTrainer/AICTrainer.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/light

# 4. 编译发布客户端 - 全内置独立运行版 (~140 MB, 零环境依赖，开箱即用)
dotnet publish src/AICTrainer/AICTrainer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/full
```

---

## 🤖 AI 协助开发声明 / AI-Assisted Development

本项目（**AICTrainer**）的整体系统架构、Mono CLR 外部线程注入机制、基于 Harmony 的细粒度沙箱容错管理器（`ResilientPatcher`）、跨进程本地 Local TCP IPC 异步协议，以及现代 WPF 霓虹暗黑交互界面的设计与代码实现，均由项目开发者与先进人工智能助手（**Google DeepMind Antigravity**）深度协同完成。

在此明确承认并感谢 AI 在以下研发环节提供的核心协助：
- **Unity Mono 底层逆向与类型分析**：动态解析游戏托管对象拓扑，梳理核心数据结构与事件钩子。
- **ResilientPatcher 容错体系设计**：为全部 31 项补丁构建独立的异常隔离沙箱，确保跨版本更新稳定性。
- **现代化 WPF 界面与贴纸系统**：设计霓虹暗黑主题风格与切图自适应渲染扩展。
- **代码重构与工程化**：模块解耦、发布脚本优化与项目全流程文档沉淀。

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

