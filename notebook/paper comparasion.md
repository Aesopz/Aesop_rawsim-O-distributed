# Energy-Aware Phase-Asymmetric RMFS Routing：系統性文獻重複度調查報告

本研究提出的 MAPPO + Congestion-Aware A* 混合架構在 RMFS 場景中的創新組合，**在現有文獻（2020–2026）中未發現完全重疊的研究**。經系統性搜尋 Google Scholar、IEEE Xplore、ScienceDirect、arXiv 及 ACM DL，共識別出 **23 篇高度相關論文**，但無任何單一論文同時涵蓋本研究的五大核心貢獻：Phase-Asymmetric 架構、MAPPO 走廊選擇、Corridor Density 觀測信號、連續運動學能耗模型、以及 Active Mask 的任務相位擴展。本研究的整體創新性評估為 **7.5/10**，主要風險來自個別模組技術（MAPPO、A*、能耗模型）已有獨立文獻支撐，需在「系統性整合」層面強化差異化論述。

---

## 一、高相關度論文逐篇比對分析

### 🟡 中度風險論文（部分重疊但有明確差異）

**1. GNMAPP — GNN-Based MAPPO for Multi-AGV Path Planning**
- **出處**：Shi, Yu, Huang, Ao, Li, Zhou (2025), *Knowledge-Based Systems*, Elsevier
- **重疊面向**：同樣使用 **MAPPO + CTDE 架構** 進行多 AGV 路徑規劃，Dec-POMDP 建模框架一致
- **差異化點**：GNMAPP 使用 GNN 動態異質圖（非走廊抽象），無能耗模型、無 Phase-Asymmetric 設計、無 RMFS 專屬場景、無擁塞感知 A*。本研究的 CNN + Corridor Density 觀測設計與其 GNN + 注意力機制在架構層面完全不同
- **風險等級**：🟡中 — MAPPO+CTDE 框架重疊，但應用場景與核心創新不同

**2. PRIMAL₂ — Pathfinding via RL and Imitation Multi-Agent Learning**
- **出處**：Damani, Luo, Wenzel, Sartoretti (2021), *IEEE RA-L*, Vol. 6(2), pp. 2666–2673
- **重疊面向**：混合 **RL + A\* 導引**方法、warehouse 網格環境、分散推論、走廊行為學習（corridor conventions）。其 blocking map 隱含了局部密度資訊
- **差異化點**：使用 A3C（非 MAPPO），無 Phase-Asymmetric 設計，A* 僅作為啟發式導引（非 congestion-weighted 微導航），無能耗建模，無 RMFS 專屬操作（如 pod 搬運、picking station）。本研究的走廊密度是結構化觀測信號，而 PRIMAL₂ 的走廊認知是隱式學習的行為慣例
- **風險等級**：🟡中 — RL+A* 混合架構概念相似，但技術實現差異顯著

**3. DPPDRL — Decentralized Path Planning via Deep RL**
- **出處**：Guo, Ji, Yao (2024), *Computers & Electrical Engineering*
- **重疊面向**：**直接針對 RMFS 環境**的深度強化學習多 AGV 路徑規劃，分散推論
- **差異化點**：採用完全分散的 DTDE 範式（非 CTDE），無中心化 Critic、無 A* 整合、無能耗模型、無 Phase-Asymmetric。**該論文明確承認在任務密集區域效能下降** — 本研究的 Congestion-Aware A* 正好解決此弱點
- **風險等級**：🟡中 — 同為 RMFS+DRL，但架構範式和擁塞處理策略根本不同

**4. Rizqi, Chou, Oscar — Energy-Efficient RMFS Simulation**
- **出處**：Zakka Ugih Rizqi, Shuo-Yan Chou, Adi Dharma Oscar (2025), *Applied Soft Computing*, Vol. 176, Art. 113141
- **重疊面向**：提出 **RMFS 專屬能耗物理模型**（加速、減速、定速、轉彎、舉升），**區分 loaded/unloaded 狀態對路徑的影響**（Underneath Pod vs. Aisles Only 路由策略），優先權交通控制啟發式
- **差異化點**：使用 simulation-optimization（NetLogo + DEA + 元啟發式），**非 MARL 方法**。本研究借用其能耗公式 E1–E5 但整合進 MAPPO reward shaping，形成 Phase-dependent 動態質量模型，這是 Rizqi 未做的
- **風險等級**：🟡中 — 能耗模型為本研究的直接基礎文獻，需明確引用並說明整合創新

**5. VP* — Via-Point Star for Warehouse MAPF with Real Robot Dynamics**
- **出處**：Lehoux-Lebacque, Silander, Loiodice, Lee, Wang, Michel (ECAI 2024), NAVER Labs Europe, arXiv:2408.14527
- **重疊面向**：連續運動學 + **有向路由圖（directed routing multi-graph）** + 真實倉庫驗證，處理相互依賴的取貨送貨任務
- **差異化點**：純搜尋方法（非 RL），未建模能耗，loaded/unloaded 未使用差異化運動學參數。本研究的連續模擬 + MARL 組合與其古典規劃方法形成互補而非競爭
- **風險等級**：🟡中 — 連續運動學 + 有向圖面向重疊，但方法論（搜尋 vs. MARL）完全不同

**6. Ye, Deng, Shi, Shen — Energy-Efficient Routing with MADDPG**
- **出處**：Ye, Deng, Shi, Shen (2023), *Sensors*, 23(12), 5615
- **重疊面向**：**MARL + 能耗最佳化** + 多 AGV 路由，結合 D* Lite 進行路徑規劃
- **差異化點**：使用 MADDPG（非 MAPPO），非 RMFS 場景，無 Phase-Asymmetric，無走廊密度觀測，無單向圖約束
- **風險等級**：🟡中 — MARL+能耗概念重疊，但演算法與場景不同

**7. MAGEC — GNN-based Multi-Agent Coordination**
- **出處**：Goeckner, Sui, Martinet, Li, Zhu (IROS 2024), pp. 5732–5739, Northwestern Univ.
- **重疊面向**：使用 **multi-agent PPO + GNN** 進行分散式協調，CTDE
- **差異化點**：應用於巡邏場景（非 RMFS/倉庫），聚焦於 agent 韌性（通訊中斷、agent 損失），無能耗、無擁塞路由、無 Phase-Asymmetric
- **風險等級**：🟢低 — 演算法類似但領域完全不同

**8. JOTP-RL — Real-Time Task Planning for RMFS**
- **出處**：多作者 (2025), *Scientific Reports* (Nature), 15:7331
- **重疊面向**：**RMFS 環境** + CTDE (Q-Mix) + **擁塞時間感知** 路徑規劃（THA* 演算法）
- **差異化點**：使用 Q-Mix（非 MAPPO），聯合最佳化訂單分配+路徑規劃，無 Phase-Asymmetric，無能耗模型，無走廊密度觀測
- **風險等級**：🟡中 — RMFS + CTDE + 擁塞感知 A* 面向有部分重疊

**9. RL-RH-PP — Learning-guided Prioritized Planning for Lifelong MAPF**
- **出處**：Zheng, Ma, Araki, Chen, Wu (2026), *JAIR*, Vol. 85, arXiv:2603.23838
- **重疊面向**：**PPO 訓練** 優先權策略 + 倉庫自動化 + 擁塞感知（學習到擁塞區域優先調度）
- **差異化點**：使用單 agent PPO 生成優先權序列（非 MAPPO 多代理策略），搜尋式規劃骨幹（非 A*+RL 混合），無能耗模型，無 Phase-Asymmetric，非 RMFS pod 搬運
- **風險等級**：🟢低 — 概念上擁塞感知+PPO+倉庫有交集，但方法論完全不同

**10. Congestion-Aware Path Planning for RMFS (GECCO 2025)**
- **出處**：多作者 (2025), *GECCO 2025*, ACM
- **重疊面向**：**RMFS 場景** + **擁塞預測模型**（DNN 預測擁塞）+ Multi-Agent Pickup and Delivery
- **差異化點**：非 MARL 方法（演化計算），擁塞預測是獨立 DNN 而非 corridor density 觀測嵌入 MARL state，無 Phase-Asymmetric，無能耗
- **風險等級**：🟡中 — RMFS + 擁塞預測面向直接相關

**11. CRAMP — Crowd-Aware Multi-Agent RL for MAPF**
- **出處**：多作者 (2023), arXiv:2309.10275
- **重疊面向**：**crowd-aware reward** 基於局部 agent 密度引導 agent 避開擁擠區域，GNN 通訊
- **差異化點**：密度資訊嵌入 reward（非 observation state 的結構化走廊密度向量），無走廊抽象，無 RMFS 場景，無能耗，無 Phase-Asymmetric
- **風險等級**：🟡中 — 密度感知 MARL 概念相似，需在論文中明確比較差異

**12. Bhaskar, Chandrasekar, Subramanian — Congestion-Aware RL for RMFS**
- **出處**：Bhaskar et al. (2025), *Industry 4.0 and Advanced Manufacturing* (Springer), I-4AM 2024
- **重疊面向**：**RL + 擁塞感知 + RMFS** — 最直接的三重重疊
- **差異化點**：非 MARL（單 agent RL），動態動作空間設計（本研究使用走廊選擇），無 Phase-Asymmetric，無能耗物理模型，無 MAPPO
- **風險等級**：🟡中 — RMFS+RL+擁塞感知三重重疊，但技術深度與本研究不同

**13. Müller — MARL for Deadlock Handling with Unidirectional Aisles**
- **出處**：Marcel Müller (2025), PhD Dissertation, Univ. of Halle-Wittenberg
- **重疊面向**：**MARL (PPO, IMPALA) + CTDE + 單向通道**，明確討論單向佈局消除 deadlock 的取捨
- **差異化點**：聚焦 deadlock 而非擁塞最佳化，無 Phase-Asymmetric，無能耗，無走廊密度觀測，非 RMFS pod 搬運
- **風險等級**：🟢低 — 背景高度相關但研究焦點不同

**14. MTPPO — Multi-Task PPO for Dynamic Storage with Congestion**
- **出處**：多作者 (2025), ScienceDirect
- **重疊面向**：**PPO + CNN-GNN 混合** + 局部擁塞模式提取 + 多 AGV 調度
- **差異化點**：聚焦儲位最佳化（非路徑規劃），無 Phase-Asymmetric，非 RMFS，無能耗
- **風險等級**：🟢低 — 技術元件相似但研究問題不同

### 🟢 低風險但需引用的背景論文

**15. MAPF-HR — Roundtrip MAPF with Heterogeneous Edges**
- **出處**：(2021), *Knowledge-Based Systems*
- **重疊面向**：MAPF 中 roundtrip（去程+回程）+ 異質邊權。概念上最接近 Phase-Asymmetric
- **差異化點**：搜尋式方法（ICBS-HB），異質性在邊權而非策略切換，無 RL
- **風險等級**：🟢低 — 概念前導，應引用以定位 Phase-Asymmetric 的文獻脈絡

**16. C-MAPF — Constrained MAPF on Directed Graphs**
- **出處**：(2024), *Automatica*, Vol. 163
- **重疊面向**：**有向圖 + 容量約束** MAPF，嚴格定義 NP-hard 問題結構
- **差異化點**：純理論/古典方法，非 RL
- **風險等級**：🟢低 — 理論基礎文獻

**17. Traffic Flow Optimisation for Lifelong MAPF**
- **出處**：多作者 (2023), arXiv:2308.11234
- **重疊面向**：**擁塞成本函數** 引導路徑 + 邊級別交通流量觀測
- **差異化點**：搜尋式方法（PIBT/LaCAM*），非 RL，走廊挑戰僅被觀察到而非系統解決
- **風險等級**：🟢低

**18. Yu & Wolf (Amazon Robotics) — Congestion Prediction for Large Fleets**
- **出處**：Ge Yu, Michael T. Wolf (ICRA 2023), Amazon Robotics
- **重疊面向**：**DNN 擁塞預測** + grid-level 密度圖 + 大規模倉庫 AGV
- **差異化點**：單 agent 規劃（非 MARL），預測模型是獨立的（非嵌入 MARL observation），非開源
- **風險等級**：🟢低

**19. MAPPO 原論文 — Death Mask 與 Action Mask**
- **出處**：Yu, Velu, Vinitsky, Gao, Wang, Bayen, Wu (NeurIPS 2022), arXiv:2103.01955
- **重疊面向**：定義了 **death mask 和 action mask** 機制，為本研究 Active Mask 擴展的基礎
- **差異化點**：death mask 僅處理 agent 存活/死亡二元狀態；本研究將其擴展至任務相位（active_mask=0/1 依據 Phase A/B/C 動態切換），這是概念性創新
- **風險等級**：🟢低 — 基礎文獻，應明確引用並突出擴展創新

**20. AlertMask — Dynamic Collision Alert Mask for Scalable MAPF**
- **出處**：多作者 (2025), arXiv:2510.09469
- **重疊面向**：動態 observation mask 機制應用於 MAPF，將碰撞風險資訊作為觀測通道
- **差異化點**：聚焦碰撞警報（非擁塞密度），集中式碰撞偵測模組設計不同
- **風險等級**：🟢低 — mask 在 MAPF 中的觀測創新可作為相關工作引用

**21. URMFS — Unidirectional RMFS Velocity-Based Storage Assignment**
- **出處**：(2024), *Transportation Research Part E*
- **重疊面向**：**專門研究單向 RMFS (URMFS)**，揭示 Manhattan 距離在單向佈局中的偏差（實際距離可達 3 倍）
- **差異化點**：聚焦儲位分配（非路徑規劃），非 RL 方法
- **風險等級**：🟢低 — 重要背景引用，證明單向 RMFS 的研究需求

**22. Highways for Warehouse MAPF**
- **出處**：Rybář et al. (ICAART 2022)
- **重疊面向**：倉庫 MAPF 中的 **highway（有向邊偏好）** 概念，偏置 H 值鼓勵方向性流動
- **差異化點**：搜尋式方法（CBS），非 RL
- **風險等級**：🟢低

**23. CCBS — Continuous-Time Conflict-Based Search**
- **出處**：Andreychuk, Yakovlev, Stern, Harabor (2022), *Artificial Intelligence*, Vol. 305
- **重疊面向**：連續時間 MAPF，non-uniform 動作時長，幾何 agent
- **差異化點**：古典搜尋方法，非 RL，無倉庫/RMFS 專屬設計
- **風險等級**：🟢低

---

## 二、重疊面向交叉矩陣

下表呈現各高相關論文與本研究六大核心設計元素的重疊情況：

| 論文 | MAPPO/CTDE | Phase-Asymmetric | Congestion-Aware A* | Corridor Density | Energy Model | Unidirectional |
|------|:---:|:---:|:---:|:---:|:---:|:---:|
| GNMAPP (2025) | ✅ | ❌ | ❌ | ❌ | ❌ | ❌ |
| PRIMAL₂ (2021) | ❌ | ❌ | 部分 | 隱式 | ❌ | ❌ |
| DPPDRL (2024) | ❌ | ❌ | ❌ | ❌ | ❌ | ❌ |
| Rizqi (2025) | ❌ | 部分 | ❌ | ❌ | ✅ | ❌ |
| VP* (ECAI 2024) | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ |
| Ye et al. (2023) | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| JOTP-RL (2025) | 部分 | ❌ | 部分 | ❌ | ❌ | ❌ |
| Bhaskar (2025) | ❌ | ❌ | ✅ | ❌ | ❌ | ❌ |
| CRAMP (2023) | ❌ | ❌ | ❌ | 部分 | ❌ | ❌ |

**關鍵發現**：沒有任何現有論文同時覆蓋兩個以上的核心設計元素。本研究的創新核心在於 **六大元素的系統性整合**，而非單一技術突破。

---

## 三、整體創新性評估

**整體評分：7.5 / 10**

本研究的創新性來自以下層次的貢獻：

**高創新性元素（文獻中未見先例）：**
- **Phase-Asymmetric 架構**（空車 A* vs. 載貨 MAPPO 策略切換）在 MAPF/MARL 文獻中無直接前例。現有 heterogeneous MAPF 聚焦於 agent 物理差異（大小、運動學），而非同一 agent 的任務相位差異化路由。這是最強的差異化貢獻
- **Active Mask 從 death mask 擴展至任務相位**：將 MAPPO 原始的二元存活/死亡語義擴展為 Phase-dependent 啟用/停用，概念新穎
- **Corridor Density 作為結構化 MARL 觀測信號**：現有文獻使用 FOV-based 隱式密度（PRIMAL₂）、crowd-aware reward（CRAMP）、或獨立 DNN 擁塞預測（Amazon），但無人將走廊密度向量直接嵌入 MARL observation space

**中等創新性元素（組合創新）：**
- MAPPO + Congestion-Aware A* 的階層式混合（走廊選擇 + 微導航）
- ETA 預測 + 四階優先權速度協商（取代 Reservation Table）
- Rizqi 能耗公式整合進 MAPPO reward 的 Phase-dependent 動態質量

**較低創新性元素（現有技術的應用）：**
- MAPPO + CTDE 已有多篇倉庫應用（GNMAPP）
- Congestion-aware A* 的基本概念已存在
- 能耗物理模型取自 Rizqi et al.

---

## 四、委員會最可能質疑的核心問題

**質疑 1：Phase-Asymmetric 是否為真正的架構創新，還是簡單的 if-else 分支？** 委員會可能認為「Phase A 用 A*、Phase B/C 用 MAPPO」只是一個工程決策而非學術貢獻。**建議回應策略**：透過 ablation study 證明 Phase-Asymmetric 架構相比 uniform MAPPO（所有 phase 都用 MAPPO）和 uniform A*（所有 phase 都用 A*）在吞吐量、能耗、擁塞指標上的統計顯著改善。同時應從理論層面論述空車 phase 的低擁塞交互特性使 MARL 的 credit assignment 成本不正當——這不只是「簡化」，而是基於問題結構的最優資源分配

**質疑 2：Corridor Density 觀測與 Stigmergy 或 FOV-based 密度的真正區別？** PRIMAL₂ 的 blocking map 已隱式編碼局部密度，CRAMP 用 crowd-aware reward 處理密度。**建議回應策略**：強調本研究的 corridor density 是 **結構化抽象**（6 條走廊的離散密度向量），而非像素級 FOV 或全局 reward 塑形。這種抽象使 MARL 的 action space 從 grid-level movement 提升至 corridor-level selection，降低了維度且保留了擁塞資訊

**質疑 3：30 台 AGV 的規模是否具代表性？** 與 PRIMAL₂（可擴展至 2048 agents）和 Amazon 的大規模系統相比，30 台 AGV 的規模較小。**建議回應策略**：強調本研究的連續運動學模擬（含加速減速、轉彎、舉升）使每台 AGV 的計算開銷遠高於離散格子模型；30 台在 49×31 單向圖的 AGV 密度實際上相當高

**質疑 4：能耗模型直接引用 Rizqi，本研究的能耗貢獻為何？** 如果 E1–E5 公式來自 Rizqi，且本研究僅「嵌入」至 reward function，增量貢獻可能被質疑。**建議回應策略**：聚焦 Phase-dependent 動態質量的整合——空車質量 vs. 載貨質量對 E1–E5 的差異化參數化，以及能耗如何反向影響 MAPPO 的走廊選擇策略（closed-loop 而非 post-hoc evaluation）

---

## 五、最需強調差異化的面向

根據文獻重複度分析，以下三個面向最需在論文中強力論述差異化：

1. **Phase-Asymmetric 架構的理論動機與經驗驗證**：這是本研究最獨特的貢獻，無任何現有文獻直接競爭。需建構完整的理論框架說明「為何不同任務相位應使用不同決策範式」，並提供充分的 ablation 證據。建議對標 Rizqi (2025) 的 loaded/unloaded 路由策略差異（Underneath Pod vs. Aisles Only）作為物理層面的佐證

2. **Corridor Density 觀測的資訊論價值**：需與 PRIMAL₂ 的 FOV-based 隱式密度、CRAMP 的 crowd-aware reward、Amazon (Yu & Wolf 2023) 的 DNN 擁塞預測進行正面比較。建議量化 corridor density observation 的資訊增益（relative to raw FOV observation）以及其對 MAPPO 收斂速度的影響

3. **Active Mask 的語義擴展**：需明確引用 Yu et al. (NeurIPS 2022) 的 death mask 原始定義，並精確論述本研究將其從「agent 存活狀態」擴展至「任務相位相關性」的泛化機制。建議對標 AlertMask (2025) 的 dynamic observation mask 概念

---

## 六、建議補充引用文獻方向

為強化論文的文獻覆蓋度和理論厚度，建議補充以下方向的引用：

**理論定位類（定義問題空間）：**
- C-MAPF on Directed Graphs (Automatica 2024) — 為單向有向圖 MAPF 提供 NP-hard 理論基礎
- MAPF-HR with Heterogeneous Edges (KBS 2021) — Phase-Asymmetric 概念的最近前導
- URMFS 儲位研究 (TRE 2024) — 證明單向 RMFS 的距離偏差問題（Manhattan 距離失效），支撐本研究場景的現實性

**技術比較類（強化 baseline 比較）：**
- GNMAPP (KBS 2025) — 同為 MAPPO+CTDE 在 AGV 路徑規劃，最直接的同期競品
- CRAMP (arXiv 2023) — 密度感知 MARL 的直接比較對象
- Bhaskar et al. (Springer 2025) — 同為 RMFS + 擁塞感知 + RL
- Traffic Flow Optimisation (arXiv 2023) — 擁塞成本函數設計的比較對象
- VP* (ECAI 2024) — 連續運動學 + 有向圖倉庫 MAPF 的搜尋式對照方法

**Mask 機制類（Active Mask 論述支撐）：**
- Huang & Ontañón (FLAIRS 2022) — action masking 的理論正當性證明
- Hu et al. (arXiv 2021) — death agent masking 的系統性分析
- AlertMask (arXiv 2025) — 動態觀測 mask 在 MAPF 的創新應用

**能耗類（能耗模型脈絡）：**
- Ye et al. (Sensors 2023) — MARL + 能耗最佳化的直接比較
- Rizqi et al. 在 *Omega* (2024) 的能耗排隊模型 — 補充其 Applied Soft Computing 論文的理論深度

---

## 結論與關鍵洞見

本研究在文獻中的定位清晰：**沒有高重複度風險的直接競爭論文**。最大威脅來自 GNMAPP (MAPPO+CTDE)、DPPDRL (RMFS+DRL)、和 Bhaskar (RMFS+擁塞+RL) 三篇論文的部分重疊，但它們各自只覆蓋本研究的一到兩個面向。

本研究的學術定位應強調三個核心論點：第一，**Phase-Asymmetric 是一種基於任務結構特性的最優策略分配範式**，超越了簡單的工程 if-else；第二，**Corridor Density 是一種介於 pixel-level FOV 和 global reward shaping 之間的結構化中間抽象**，平衡了資訊豐富度與 MARL 學習效率；第三，**六大元素的系統性整合本身就是創新**——在 RMFS 實際場景中，單一技術無法同時解決擁塞、能耗、單向約束、和規模化問題，而本研究首次提出了一個完整的解決框架。

DUAL (Xin et al., 2026) 在本次搜尋中未能找到公開全文，若該論文確實存在且使用離散格子動作空間於 RMFS，則可能成為最直接的比較對象——建議作者在投稿前取得該論文全文進行詳細比對。