# Instance: citi-lab — RAWSim-O Port dari netlogo-rmfs

**Versi:** 1.0
**Tanggal:** 2026-03-26
**Status:** Done ✅ — file tersedia di `Material/Instances/CoreBenchmark/citi-lab.*`
**Scope:** Replikasi topologi warehouse netlogo-rmfs (layout, pod, station, robot) ke format `.xinst` RAWSim-O

---

## Table of Contents

1. [Latar Belakang](#1-latar-belakang)
2. [Sumber Parameter — netlogo-rmfs](#2-sumber-parameter--netlogo-rmfs)
3. [Layout Warehouse](#3-layout-warehouse)
4. [Zone Breakdown](#4-zone-breakdown)
5. [Posisi Station](#5-posisi-station)
6. [Mapping Parameter ke RAWSim-O](#6-mapping-parameter-ke-rawsim-o)
7. [Struktur File yang Dihasilkan](#7-struktur-file-yang-dihasilkan)
8. [Waypoint Graph](#8-waypoint-graph)
9. [Cara Menjalankan Simulasi](#9-cara-menjalankan-simulasi)
10. [Cara Regenerasi Instance](#10-cara-regenerasi-instance)
11. [Catatan Keterbatasan](#11-catatan-keterbatasan)

---

## 1. Latar Belakang

Project **netlogo-rmfs** mensimulasikan sistem RMFS (Robotic Mobile Fulfillment System) berbasis
NetLogo/Python dengan layout warehouse khusus yang telah digunakan untuk riset routing RL
(MDRL, AgentAStar). Agar eksperimen yang sama dapat dibandingkan di framework **RAWSim-O-CITI**
— yang mendukung pathfinding multi-agent, Gym environment, dan world model controller — perlu
dibuat sebuah *instance simulasi* yang setara.

Instance **citi-lab** dibuat dengan mereplikasi:
- Dimensi grid dan zona warehouse dari netlogo-rmfs
- Jumlah dan posisi pod aktif
- Jumlah dan posisi station (picking & replenishment)
- Jumlah robot

Sehingga benchmark performa algoritma (throughput orders, jarak tempuh, energi) dapat
dibandingkan secara apple-to-apple antara kedua framework.

---

## 2. Sumber Parameter — netlogo-rmfs

Semua parameter diambil dari dua file konfigurasi utama netlogo-rmfs:

| File | Path |
|---|---|
| Config utama | `netlogo-rmfs/environment/config.py` |
| Layout logic | `netlogo-rmfs/world/layout.py` |

### Parameter Kunci

| Parameter | Nilai | Sumber |
|---|---|---|
| `pod_batch_horizontal` | 5 | pods per batch secara horizontal |
| `pod_batch_vertical` | 2 | pods per batch secara vertikal |
| `pod_batch_horizontal_max` | 5 | jumlah grup horizontal |
| `pod_batch_vertical_max` | 10 | jumlah grup vertikal |
| `reserved_column_start` | 9 | lebar zona kiri (junction + station) |
| `reserved_column_end` | 9 | lebar zona kanan (junction + station) |
| `reserved_column_station` | 5 | lebar area station |
| `order_picker_total` | 3 | jumlah OutputStation (picking) |
| `order_replenishment_total` | 1 | jumlah InputStation (replenishment) |
| `total_pods_active` | 300 | pod aktif dari 500 slot tersedia |
| `total_charging_stations` | 10 | slot pod yang dikonversi ke charging |
| `tick_to_second` | 0.15 s | durasi satu tick simulasi |
| `max_ticks` | 28.800 | total durasi = 8 jam |

### Jumlah Robot

Dari konfigurasi default `world/warehouse.py`:

```
robots = 30
```

---

## 3. Layout Warehouse

### Dimensi Grid

```
total_rows = (pod_batch_vertical_max × pod_batch_vertical) + pod_batch_vertical_max + 1
           = (10 × 2) + 10 + 1
           = 31 baris

total_cols = (reserved_column_start + 1) + pod_space + (reserved_column_end + 1)
           = 10 + 29 + 10
           = 49 kolom

pod_space  = (pod_batch_horizontal × pod_batch_horizontal_max) + pod_batch_horizontal_max − 1
           = (5 × 5) + 5 − 1 = 29
```

**Skala:** 1 cell = 1.0 meter → Tier RAWSim-O: **49 m × 31 m**

### Pola Baris

```
row % 3 == 0  →  cross-aisle (horizontal aisle)   ← rows 0, 3, 6, ..., 30  (11 aisles)
row % 3 != 0  →  pod row                           ← 20 baris berisi pod/rail
```

### Pola Kolom (zona pod, cols 9–39)

```
(col − 9) % 6 == 0  →  vertical aisle  ← cols 9, 15, 21, 27, 33, 39
(col − 9) % 6 != 0  →  pod / vertical rail
```

### Kapasitas Pod

```
500 slot total (25 kolom pod × 20 baris pod)
│
├── 300  → aktif (tile T_POD = 1)
├──  10  → charging station (tile T_CHARGING = 2)
└── 190  → deaktivasi (tile 0, tetap PodStorageLocation=true tapi tanpa pod)
```

Pod yang diaktifkan/dinonaktifkan dipilih secara random dengan **seed=42** untuk reprodusibilitas.

---

## 4. Zone Breakdown

```
Col:  0    4 5   8 9                          39 40   43 44   48
      │    │ │   │ │                          │  │    │  │    │
      ▼    ▼ ▼   ▼ ▼                          ▼  ▼    ▼  ▼    ▼
  ┌────────┬──────┬──────────────────────────┬──────┬────────┐
  │ Output │Left  │        Pod Area          │Right │ Input  │
  │Station │Junct │  300 pods + 10 charging  │Junct │Station │
  │  x 3   │(rail)│  + 190 empty slots       │(rail)│  x 1   │
  │        │      │                          │      │        │
  │ 5 cols │4 cols│       29 cols            │4 cols│ 5 cols │
  └────────┴──────┴──────────────────────────┴──────┴────────┘
    cols    cols   cols 9 ────────────── 39   cols   cols
    0–4     5–8                              40–43   44–48
```

### Tile Codes (dari netlogo-rmfs/environment/config.py)

| Kode | Nama | Navigable | PodStorageLocation |
|---|---|:---:|:---:|
| 0 | Slot pod deaktivasi | ✓ | ✓ |
| 1 | Pod aktif | ✓ | ✓ |
| 2 | Charging station | ✓ | — |
| 3 | Cross-aisle (horizontal) | ✓ | — |
| 4 / 5 | Horizontal rail A/B | ✓ | — |
| 6 / 7 | Vertical rail A/B | ✓ | — |
| 11 | Station picker (kiri) | ✓ | — |
| 21 | Station picker mirrored (kanan) | ✓ | — |
| 12–19 | Approach rail station kiri | ✓ | — |
| 22–29 | Approach rail station kanan | ✓ | — |
| **99** | **Blank space** | **✗** | — |

---

## 5. Posisi Station

### Logika Penempatan (dari layout.py)

Station ditempatkan pada slot-slot yang dipilih dari 5 slot vertikal yang tersedia
(`determine_station_limits() = (10+1)//2 = 5`). Untuk 3 OutputStation dan 1 InputStation,
algoritma memilih slot dari tengah ke luar secara bergantian.

**Slot yang terpilih untuk OutputStation (3 stations):** slot 2, 3, 4

```
Slot 2 → start_row = (2−1) × 6 = 6,  end_row = 6 + 3 = 9
Slot 3 → start_row = (3−1) × 6 = 12, end_row = 12 + 3 = 15
Slot 4 → start_row = (4−1) × 6 = 18, end_row = 18 + 3 = 21
```

**Slot yang terpilih untuk InputStation (1 station):** slot 3

```
Slot 3 → start_row = 12, end_row = 15
```

### Koordinat Station (dalam meter, 1 cell = 1 m)

| ID | Tipe | Row | Col | X (m) | Y (m) | Keterangan |
|---|---|---|---|---|---|---|
| OutputStation 0 | Picking | 7 | 1 | 1.5 | 7.5 | Slot 2 |
| OutputStation 1 | Picking | 13 | 1 | 1.5 | 13.5 | Slot 3 |
| OutputStation 2 | Picking | 19 | 1 | 1.5 | 19.5 | Slot 4 |
| InputStation 0 | Replenishment | 13 | 47 | 47.5 | 13.5 | Slot 3 |

### Queue Waypoints per Station

Setiap station memiliki 7 queue waypoints yang membentuk jalur pendekatan:

```
OutputStation (kiri):
  Robot datang dari kanan → melewati cols 8,7,6,5,4,3,2 → tiba di station col=1
  Queue: [wp(row,8), wp(row,7), ..., wp(row,2)]

InputStation (kanan):
  Robot datang dari kiri → melewati cols 40,41,42,43,44,45,46 → tiba di station col=47
  Queue: [wp(row,40), wp(row,41), ..., wp(row,46)]
```

---

## 6. Mapping Parameter ke RAWSim-O

### Robot (Bot)

| Parameter RAWSim | Nilai | Sumber / Alasan |
|---|---|---|
| `MaxVelocity` | 1.0 m/s | Standar RAWSim (kecepatan 1.5 cell/tick tidak langsung dikonversi) |
| `MaxAcceleration` | 0.25 m/s² | Standar RAWSim |
| `MaxDeceleration` | 0.25 m/s² | Standar RAWSim |
| `TurnSpeed` | 1.0 rad/s | Standar RAWSim |
| `Radius` | 0.35 m | Sesuai dimensi robot RMFS nyata |
| `PodTransferTime` | 3.0 s | Standar RAWSim |
| `CollisionPenaltyTime` | 0.5 s | Standar RAWSim |
| `Tier` | 0 | Single-tier warehouse |

### Pod

| Parameter RAWSim | Nilai | Sumber / Alasan |
|---|---|---|
| `Radius` | 0.4 m | Sesuai pod RMFS fisik |
| `Capacity` | 500 | Rata-rata slot: ~3000 slot total / 6 tipe pod |
| `Tier` | 0 | Single-tier |

### Station

| Parameter RAWSim | Nilai | Sumber / Alasan |
|---|---|---|
| `OutputStation.ItemTransferTime` | 8.0 s | Standar picking time |
| `OutputStation.Capacity` | 20 | Max order dalam antrian |
| `InputStation.ItemBundleTransferTime` | 10.0 s | Standar replenishment time |
| `InputStation.Capacity` | 1000 | Buffer replenishment besar |

### Tier / Dimensi

| Parameter | Nilai |
|---|---|
| `Length` | 49.0 m (jumlah kolom × 1 m) |
| `Width` | 31.0 m (jumlah baris × 1 m) |

---

## 7. Struktur File yang Dihasilkan

Tiga file di `Material/Instances/CoreBenchmark/`:

```
citi-lab.xinst   ← layout warehouse (utama)
citi-lab.xsett   ← parameter simulasi (durasi, inventory, order rate)
citi-lab.xconf   ← konfigurasi algoritma controller
```

### `citi-lab.xinst` — Ringkasan Isi

```xml
<Instance Name="citi-lab">
  <Bots>           30 robot  </Bots>
  <Pods>          300 pod    </Pods>
  <Elevators/>                      <!-- single-tier, tidak ada elevator -->
  <InputStations>   1 station</InputStations>
  <OutputStations>  3 station</OutputStations>
  <Tiers>           49m × 31m</Tiers>
  <Waypoints>    1261 node   </Waypoints>
  <Semaphores>      4 guard  </Semaphores>
</Instance>
```

| Elemen | Jumlah | Detail |
|---|---|---|
| Bots | 30 | Ditempatkan di cross-aisle junction (deterministik) |
| Pods | 300 | Di zona pod, linked ke waypoint masing-masing |
| InputStations | 1 | Kanan, row 13, col 47 |
| OutputStations | 3 | Kiri, col 1, rows 7/13/19 |
| Waypoints | 1.261 | 1 per cell navigable (tile ≠ 99) |
| PodStorageLocation=true | 490 | 300 aktif + 190 kosong |
| Queue waypoints | 28 | 7 per station × 4 stations |
| Semaphores | 4 | Entry guard tiap station |

### `citi-lab.xsett` — Setting Utama

| Parameter | Nilai | Sumber |
|---|---|---|
| `SimulationDuration` | 28.800 s | 8 jam × 3600 s = max\_ticks × tick\_to\_second |
| `OrderPositionCountMin/Max` | 1 – 12 | Sama dengan netlogo-rmfs order quantity range |
| `OrderPositionCountMean` | 6 | Rata-rata order netlogo-rmfs |
| `OrderMode` | Fill | Isi pod hingga kapasitas terpenuhi |
| `InitialInventory` | 0.7 | 70% pod terisi di awal |
| `DemandInventoryConfiguration.OrderCount` | 100 | Pool order aktif |

### `citi-lab.xconf` — Algoritma Default

| Komponen | Algoritma | Keterangan |
|---|---|---|
| Path Planning | FAR (Flow-Aware Routing) | Default proven, cocok untuk grid besar |
| Task Allocation | BalancedTaskAllocation | 30 bots / 4 stations, limit 8 bots/station |
| Item Storage | RandomItemStorage | Acak dengan stick-to-pod |
| Pod Storage | StationBasedPodStorage | Pod disimpan dekat station tujuannya |
| Order Batching | RandomOrderBatching | Dengan recycle |

---

## 8. Waypoint Graph

### Topologi

Graf navigasi dibangun sebagai **grid 4-arah bidireksional** — setiap waypoint terhubung ke
4 tetangga (atas, bawah, kiri, kanan) jika keduanya navigable.

```
         col
    ←─────────────────→
  ↑ ●─●─●─●─●─●─●─●─●  row 0  (cross-aisle)
  │ ●─●─●─●─●─●─●─●─●  row 1  (pod row)
  │ ●─●─●─●─●─●─●─●─●  row 2  (pod row)
row ●─●─●─●─●─●─●─●─●  row 3  (cross-aisle)
  │ ...
  ↓ ●─●─●─●─●─●─●─●─●  row 30
```

Cells dengan tile=99 (blank space) **tidak memiliki waypoint** dan tidak membentuk edge.

### Semaphore

4 semaphore dipasang di entry tiap station (kapasitas=2):

| Semaphore | Guard From → To | Fungsi |
|---|---|---|
| 0 | OutputStation 0 entry (col 8 → 7) | Batas antrian picking 0 |
| 1 | OutputStation 1 entry (col 8 → 7) | Batas antrian picking 1 |
| 2 | OutputStation 2 entry (col 8 → 7) | Batas antrian picking 2 |
| 3 | InputStation 0 entry (col 39 → 44) | Batas antrian replenishment |

---

## 9. Cara Menjalankan Simulasi

### Headless (CLI)

```bash
cd RAWSim-O-CITI

dotnet run --project RAWSimO.CLI/RAWSimO.CLI.csproj -- \
  Material/Instances/CoreBenchmark/citi-lab.xinst \
  Material/Instances/CoreBenchmark/citi-lab.xsett \
  Material/Instances/CoreBenchmark/citi-lab.xconf \
  output/ 42
```

Output ditulis ke: `output/citi-lab-citi-lab-citi-lab-42/`

### GUI (Windows + WPF)

```bash
dotnet run --project RAWSimO.Visualization/RAWSimO.Visualization.csproj
```

Kemudian load ketiga file dari menu File → Open Instance.

### Dengan Gym Environment (Plan 2)

Instance ini kompatibel dengan GymServer (Plan 2). Gunakan sebagai `instance_path` di
konfigurasi Python:

```python
env = RMFSParallelEnv(
    instance_path="Material/Instances/CoreBenchmark/citi-lab.xinst",
    setting_path="Material/Instances/CoreBenchmark/citi-lab.xsett",
    config_path="Material/Instances/CoreBenchmark/citi-lab.xconf",
)
```

---

## 10. Cara Regenerasi Instance

Instance dihasilkan oleh script Python deterministik:

```bash
cd RAWSim-O-CITI
python scripts/generate_citi_lab.py
```

Script: `scripts/generate_citi_lab.py`

Script mereplikasi logika `layout.py` netlogo-rmfs dengan:
- **Seed = 42** untuk pemilihan pod deaktivasi yang deterministik
- Semua parameter dikonfigurasikan sebagai konstanta di bagian atas file
- Output selalu ke `Material/Instances/CoreBenchmark/citi-lab.xinst`

### Parameter yang Dapat Diubah di Script

| Konstanta | Default | Efek |
|---|---|---|
| `SEED` | 42 | Pola pod aktif/deaktivasi |
| `TOTAL_PODS_ACTIVE` | 300 | Jumlah pod aktif |
| `BOT_COUNT` | 30 | Jumlah robot |
| `CELL_SIZE` | 1.0 m | Skala fisik grid |
| `BOT_MAX_VELOCITY` | 1.0 m/s | Kecepatan maksimal robot |
| `POD_CAPACITY` | 500 | Kapasitas item per pod |

---

## 11. Catatan Keterbatasan

| Aspek | Keterbatasan | Penjelasan |
|---|---|---|
| **Kecepatan robot** | Tidak identik | NetLogo: 1.5 cell/tick × 0.15 s/tick = tidak direct-mapping ke m/s karena unit cell bersifat abstrak. RAWSim menggunakan 1.0 m/s (standar). |
| **Pod assignment SKU** | Tidak direplikasi | netlogo-rmfs memiliki `items_dictionary.csv` + `pods.csv` dengan assignment item ke pod. RAWSim menggunakan inventory fill mode tanpa fixed SKU-pod assignment. |
| **Charging station** | Tidak sebagai entitas eksplisit | 10 charging slot ada di grid (tile=2) sebagai waypoints navigable, tapi RAWSim tidak memiliki elemen `<ChargingStation>` terpisah. Bot di RAWSim tidak perlu charge dalam simulasi standar. |
| **Arah RL** | Tidak direplikasi | Tile 4/5 (horizontal rail A/B) dan 6/7 (vertical rail A/B) di NetLogo menandakan arah aliran satu-arah. RAWSim menggunakan bidirektional dengan pathfinding algorithm yang menangani collision. |
| **Posisi pod** | Random setiap regenerasi jika seed diubah | Layout NetLogo juga random; seed=42 dipilih sebagai nilai tetap untuk reprodusibilitas. |
