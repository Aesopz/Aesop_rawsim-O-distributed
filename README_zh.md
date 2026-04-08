# RAWSim-O（分散式 RL 擴充版）— 你可以用這個 Repo 做什麼？

> **版本：** 本文件針對 [Aesopz/Aesop_rawsim-O-distributed](https://github.com/Aesopz/Aesop_rawsim-O-distributed)，
> 在原版 [merschformann/RAWSim-O](https://github.com/merschformann/RAWSim-O) 基礎上加入了 RL Gym 擴充套件。

---

## 目錄

1. [這個 Repo 是什麼？](#1-這個-repo-是什麼)
2. [你可以用它來做什麼？](#2-你可以用它來做什麼)
3. [快速開始](#3-快速開始)
4. [GUI 可視化介面](#4-gui-可視化介面)
5. [命令列（無介面）模擬](#5-命令列無介面模擬)
6. [強化學習 Gym 環境](#6-強化學習-gym-環境)
7. [倉庫佈局生成器](#7-倉庫佈局生成器)
8. [研究計畫文件](#8-研究計畫文件)
9. [架構概覽](#9-架構概覽)
10. [建置與測試](#10-建置與測試)
11. [授權](#11-授權)

---

## 1. 這個 Repo 是什麼？

**RAWSim-O** 是一個離散事件式模擬框架，專門用於研究 **Robotic Mobile Fulfillment Systems（RMFS）**——也就是「機器人自動倉儲」系統。在這類系統中，移動機器人（AGV）將儲物架（Pod）搬運到人工揀貨站，實現高效訂單處理。

本 Repo 在原版 RAWSim-O 之上新增了一套 **強化學習（RL）介面**，讓你能用多智能體強化學習演算法控制倉庫機器人。

---

## 2. 你可以用它來做什麼？

### 🏭 倉儲系統模擬
- 模擬真實的機器人倉儲佈局：設定倉庫網格大小、機器人數量、儲物架、輸入/輸出站
- 在 2D 或 3D 視圖中即時觀察模擬過程
- 蒐集詳細的統計數據（吞吐量、機器人路徑熱圖、訂單完成率等）

### 🤖 決策演算法研究
框架為以下決策問題提供多種可替換演算法：

| 決策問題 | 可用演算法數量 |
|----------|--------------|
| 路徑規劃（Path Planning）| 9 種（WHCAvStar、CBS、BCP、FAR、PAS 等）|
| 任務分配（Bot/Task Management）| 6 種 |
| 物品儲存（Item Storage）| 8 種 |
| 貨架存放（Pod Storage）| 8 種 |
| 訂單批次（Order Batching）| 10+ 種 |
| 站點啟用（Station Activation）| 4 種 |
| 貨架重定位（Repositioning）| 5 種 |

你可以輕鬆實作自己的演算法並插入模擬框架，無需修改核心邏輯。

### 🧠 強化學習訓練
- 透過 **PettingZoo ParallelEnv** 介面使用任意 RL 框架（如 RLlib、Stable-Baselines3）訓練多智能體策略
- 每個機器人接收來自車載攝影機的 64×64 RGB 圖像作為觀測值
- 動作空間：方向（N/S/E/W/停止）× 加速模式 × 減速模式
- 獎勵函數：`α × 完成訂單數 − β × 碰撞次數 − γ × 閒置率`

### 📊 基準測試與資料蒐集
- 使用內建 A* 演算法作為基準，蒐集專家示範資料
- 匯出觀測序列用於訓練世界模型（World Model / VAE + MDN-RNN）
- 評估不同決策策略的效能並比較

### 🗺️ 自訂倉庫佈局
- 使用 Python 腳本生成自訂的倉庫佈局（`.xinst` 格式）
- 內建「Citi-Lab」佈局：31 列 × 49 行，300 個儲物架，30 台機器人，3 個輸出站

---

## 3. 快速開始

### 前置需求

- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0) 或更新版本
- Python 3.8+（用於 RL Gym 環境，可選）

### 建置

```bash
dotnet build RAWSimO.sln
```

### 執行一個基準模擬

```bash
dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- \
  Material/Instances/CoreBenchmark/jenkinsinstance1.xinst \
  Material/Instances/CoreBenchmark/jenkinssetting1.xsett \
  Material/Instances/CoreBenchmark/jenkinsconfig1.xconf \
  output/ 42
```

結果會輸出到 `output/<實例名>-<設定名>-<配置名>-<種子>/` 資料夾，包含控制日誌和統計 CSV 檔案。

---

## 4. GUI 可視化介面

> **僅限 Windows**（需要 WPF / .NET 6.0-windows）

```bash
dotnet run --project RAWSimO.Visualization/RAWSimO.Visualization.csproj
```

在 GUI 中你可以：
- 透過圖形介面設定倉庫佈局、模擬參數和控制演算法
- 即時以 2D 或 3D 視圖觀察模擬過程
- 產生機器人移動熱圖
- 匯出和儲存設定檔（`.xinst`、`.xsett`、`.xconf`）

---

## 5. 命令列（無介面）模擬

適合在 Linux/macOS 伺服器上批次執行實驗：

```bash
dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- \
  <Instance.xinst> <Setting.xsett> <Config.xconf> <OutputDir> <Seed> [Tag]
```

**可用的 Benchmark 檔案** （位於 `Material/Instances/CoreBenchmark/`）：

| 類型 | 檔案 |
|------|------|
| 實例（`.xinst`）| `jenkinsinstance1` ~ `jenkinsinstance3`、`jenPPinstance`、`1-1-1-2-22` 等 |
| 設定（`.xsett`）| `jenkinssetting1` ~ `jenkinssetting3`、`jenPP` |
| 控制配置（`.xconf`）| `jenkinsconfig1` ~ `jenkinsconfig3`、`jenPPFAR`、`jenPPBCP`、`jenPPCBS` 等 |

---

## 6. 強化學習 Gym 環境

### 6.1 啟動 GymServer（C# 伺服器端）

```bash
dotnet run --project RAWSimO.GymServer/RAWSimO.GymServer.csproj -- \
  <instance.xinst> <setting.xsett> <config.xconf> [port] [step_duration] [max_steps] [--baseline]
```

- 預設連接埠：`7654`（可用 `RMFS_GYM_PORT` 環境變數覆蓋）
- `--baseline`：讓內建 A* 演算法控制機器人目的地，Python 動作將被忽略（用於蒐集基準資料）
- **必須**使用 `AgentAStar` 路徑規劃配置（`DecentralAStarPathPlanningConfiguration`）

### 6.2 Python 端使用（PettingZoo 環境）

安裝 Python 套件：

```bash
pip install -e rmfs_gym/
```

使用範例：

```python
from rmfs_gym.envs import RMFSParallelEnv

env = RMFSParallelEnv(host="localhost", port=7654)
obs, infos = env.reset(seed=42)

for step in range(1000):
    # obs: dict[agent_id -> np.ndarray of shape (64, 64, 3)]
    actions = {agent: env.action_space(agent).sample() for agent in env.agents}
    obs, rewards, dones, truncs, infos = env.step(actions)
    if not env.agents:
        break

env.close()
```

### 6.3 觀測、動作與獎勵

| 項目 | 說明 |
|------|------|
| **觀測空間** | 每台機器人：64×64×3 RGB 圖像（來自車載攝影機） |
| **動作空間** | `MultiDiscrete([5, 3, 3])` — 方向 × 加速模式 × 減速模式 |
| **獎勵函數** | `1.0 × 完成訂單 − 0.5 × 碰撞 − 0.1 × 閒置率` |
| **infos 欄位** | `action_mask`（可行動作遮罩）、`collision_count`（碰撞次數）|

### 6.4 TCP 通訊協定

Python 端與 C# 端透過 TCP Socket 通訊，格式為：
```
[4 bytes little-endian uint32 JSON 長度][JSON bytes][原始圖像 bytes]
```

支援的命令：
- `{"cmd": "reset", "seed": N}` — 重設環境
- `{"cmd": "step", "actions": {...}}` — 執行一步
- `{"cmd": "close"}` — 關閉伺服器

---

## 7. 倉庫佈局生成器

生成 Citi-Lab 倉庫佈局（31×49 網格）：

```bash
python scripts/generate_citi_lab.py
# 輸出：Material/Instances/CoreBenchmark/citi-lab.xinst
```

佈局規格：
- **網格大小：** 31 列 × 49 行
- **機器人數量：** 30 台
- **儲物架數量：** 300 個
- **輸出站：** 3 個
- **輸入站：** 1 個

其他輔助腳本（位於 `scripts/`）：

| 腳本 | 用途 |
|------|------|
| `benchmark_astar.py` | 執行 A* 基準測試 |
| `collect_baseline.py` | 蒐集基準資料集（狀態-動作序列）|
| `dataset_to_video.py` | 將資料集轉換為影片 |
| `inspect_dataset.py` | 檢查資料集內容 |
| `obs_preview.py` | 預覽觀測影像 |

---

## 8. 研究計畫文件

`ResearchPlan/` 資料夾包含本研究方向的詳細技術計畫：

| 文件 | 內容 |
|------|------|
| `PLAN_AgentAStar_OnboardCam.md` | Plan 1：A* 路徑規劃 + 車載攝影機觀測實作 |
| `PLAN_GymEnvironment.md` | Plan 2：PettingZoo Gym 環境與 GymServer 架構 |
| `PLAN_WorldModel.md` | Plan 3：世界模型（VAE + MDN-RNN）訓練流程 |
| `INSTANCE_citi-lab.md` | Citi-Lab 倉庫佈局規格說明 |

---

## 9. 架構概覽

```
RAWSim-O (C#)
├── RAWSimO.Core          # 核心模擬引擎（實例、機器人、統計）
├── RAWSimO.CLI           # 無介面命令列執行器
├── RAWSimO.Visualization # WPF GUI（僅 Windows）
├── RAWSimO.GymServer     # TCP 伺服器，對外提供 reset/step/close API
├── RAWSimO.MultiAgentPathFinding  # 多智能體路徑規劃演算法
└── Tests/RAWSimO.Core.Tests       # xUnit 測試套件

rmfs_gym (Python)
├── envs/                 # PettingZoo ParallelEnv 實作
├── connection/           # TCP Socket 通訊
└── utils/                # 輔助工具

scripts/                  # Python 輔助腳本
Material/Instances/CoreBenchmark/  # 基準測試用倉庫佈局檔案
ResearchPlan/             # 研究計畫文件
```

---

## 10. 建置與測試

```bash
# 建置整個方案
dotnet build RAWSimO.sln

# Release 模式建置
dotnet build -c Release RAWSimO.sln

# 執行測試
dotnet test

# 詳細輸出
dotnet test --verbosity normal
```

---

## 11. 授權

本程式依據 **GNU General Public License v3.0** 授權發布。  
詳見 [LICENSE](LICENSE) 檔案。

---

> 原版 RAWSim-O 由 Marius Merschformann 等人開發，發表於 *Logistics Research (2018)*，  
> doi: [10.23773/2018_8](https://www.bvl.de/lore/all-volumes--issues/volume-11/issue-1/rawsim-o-a-simulation-framework-for-robotic-mobile-fulfillment-systems)
