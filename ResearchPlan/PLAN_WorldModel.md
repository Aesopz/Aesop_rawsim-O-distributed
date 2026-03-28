# Plan: World Model Controller untuk RMFS Throughput Optimization
**Version:** 2.6 — Action space corrected: MultiDiscrete([5,3,3]) N/S/E/W/Stay; concatenated one-hot (11,) per dim; LSTM input_size 113→43; C-model output 15→11 logits; gmm_linear (256,327)
**Date:** 2026-03-26
**Status:** Pending — requires Plan 2 Phase 4 (baseline dataset) minimum
**Scope:** (1) V-Model: VAE visual encoder, (2) M-Model: MDN-RNN dynamics model,
(3) C-Model: Controller via CMA-ES, (4) Deployment via Plan 2 Gym env

**Referensi arsitektur:** Ha & Schmidhuber, "World Models" (2018)
**Adaptasi:** Action space discrete MultiDiscrete([5,3,3]), reward berbasis throughput RMFS

---

## Table of Contents
1. [Objective](#1-objective)
2. [Synchronization dengan Plan 1 & 2](#2-synchronization-dengan-plan-1--2)
3. [Architecture Overview — V, M, C](#3-architecture-overview--v-m-c)
4. [Observation & Action Contract](#4-observation--action-contract)
5. [V-Model — VAE Visual Encoder](#5-v-model--vae-visual-encoder)
6. [M-Model — MDN-RNN Dynamics](#6-m-model--mdn-rnn-dynamics)
7. [C-Model — Controller via CMA-ES](#7-c-model--controller-via-cma-es)
8. [Training Phase A — VAE](#8-training-phase-a--vae)
9. [Training Phase B — MDN-RNN](#9-training-phase-b--mdn-rnn)
10. [Training Phase C — Controller CMA-ES](#10-training-phase-c--controller-cma-es)
11. [Deployment ke Gymnasium Env](#11-deployment-ke-gymnasium-env)
12. [Evaluation & Benchmarking](#12-evaluation--benchmarking)
13. [Implementation Phases](#13-implementation-phases)
14. [Risk Matrix](#14-risk-matrix)
15. [File Index](#15-file-index)

---

## 1. Objective

Melatih **world model berbasis visual** menggunakan arsitektur V-M-C (Ha & Schmidhuber 2018)
yang dilatih secara **sequential dalam 3 fase terpisah**:

```
Phase A: V-Model (VAE)
  → Compress pixel observation 64×64×3 ke latent vector z ∈ R^32
  → Unsupervised, dari raw frames

Phase B: M-Model (MDN-RNN)
  → Belajar "dynamics": given (z_t, a_t, h_t) → distribusi z_{t+1}
  → Memory tentang urutan kejadian di warehouse

Phase C: C-Model (Controller)
  → Belajar policy: given (z_t, h_t) → action (waypoint, accel, decel)
  → Di-train dengan CMA-ES menggunakan V+M untuk simulasi imajinasi
```

**Goal akhir:** Controller yang di-deploy ke Plan 2 Gym env menghasilkan throughput
lebih tinggi dari AgentAStar baseline (Plan 1) melalui routing yang lebih optimal.

**Decentralized (konsisten dengan Plan 1):** Setiap robot punya set V+M+C sendiri.
Tidak ada shared model atau komunikasi antar robot selama inference.

---

## 2. Synchronization dengan Plan 1 & 2

### 2.1 Interface Contracts yang Harus Identik

| Contract | Plan 2 (mendefinisikan) | Plan 3 (menggunakan) |
|---|---|---|
| **Observation format** | `Box(64, 64, 3, uint8)` per bot | Input V-Model Encoder |
| **Action format** | `MultiDiscrete([5, 3, 3])` | Output C-Model, identik |
| **Reward function** | `ALPHA×orders - BETA×collision - GAMMA×idle` | Reward head MDN-RNN + fitness CMA-ES |
| **Step duration** | 1.0 detik per step | Temporal unit MDN-RNN sequence |
| **Waypoint candidates K** | K=8 + STAY = 9 | Controller head waypoint = 9 |
| **Dataset format** | `obs:(T,64,64,3) uint8`, `actions:(T,3) int32`, `rewards:(T,) float32`, `dones:(T,) bool_` — lihat §2.3 | Phase A + B training |

### 2.2 Dependensi Plan 1 → Plan 3

| Komponen Plan 1 | Relevansi Plan 3 |
|---|---|
| `AgentAStarPathManager` | Sumber dataset Phase A & B (baseline mode), baseline evaluasi Phase C |
| Per-agent design | V+M+C per bot, tidak shared |

### 2.3 Dependensi Plan 2 → Plan 3

```
Plan 2 Phase 4 menghasilkan dataset:
  dataset/
    episode_0001/
      bot_0.npz: {obs:(T,64,64,3) uint8, actions:(T,3) int32, rewards:(T,) float32, dones:(T,) bool_}
      bot_1.npz: ...
    ...
    episode_N/

Phase A menggunakan semua frame (obs saja, tidak perlu actions/rewards).
Phase B menggunakan sequences (obs_t, a_t, obs_{t+1}, r_t).
Phase C menggunakan V+M beku (frozen) untuk rollout imajinasi.
Phase C (fine-tune opsional) menggunakan Plan 2 Gym env live.
```

---

## 3. Architecture Overview — V, M, C

```
┌─────────────────────────────────────────────────────────────────────┐
│  INFERENCE (satu bot, satu timestep)                                │
│                                                                     │
│  obs_t (64,64,3) ──[V: Encoder]──→ z_t (32,)                      │
│                                                                     │
│  concat(z_t, h_t) ──[C: Controller]──→ a_t ([5,3,3])              │
│        ↑                                                            │
│  h_t = hidden state MDN-RNN dari timestep sebelumnya               │
│                                                                     │
│  Setelah action di-apply:                                           │
│  concat(z_t, a_flat_t) ──[M: MDN-RNN]──→ h_{t+1}  (update memory)│
│                         ──[M: MDN head]──→ p(z_{t+1}) (only dream)│
│  a_flat_t = encode_action_onehot(a_t) ∈ R^11                      │
└─────────────────────────────────────────────────────────────────────┘

Input ke Controller: concat(z_t, h_t) ∈ R^(32+256) = R^288
Output Controller:   waypoint_logits(9,) + accel_logits(3,) + decel_logits(3,)

Training (sequential, masing-masing harus selesai sebelum fase berikutnya):
  Phase A → melatih V (VAE)                     → V di-freeze setelah Phase A selesai
  Phase B → melatih M (MDN-RNN), V frozen ✓     → V+M di-freeze setelah Phase B selesai
  Phase C → melatih C (Controller), V+M frozen ✓
```

### 3.1 Dimensi Kunci

| Simbol | Dimensi | Komponen |
|--------|---------|----------|
| `z_t` | `(32,)` | VAE latent vector |
| `h_t` | `(256,)` | LSTM hidden state |
| `c_t` | `(256,)` | LSTM cell state |
| `a_t` | `(3,)` int | MultiDiscrete action |
| `a_flat` | `(11,)` | Concatenated one-hot per dim (5+3+3) |
| Input C | `(288,)` | concat(z_t, h_t) |
| Output C | `(15,)` | waypoint(9) + accel(3) + decel(3) logits |

---

## 4. Observation & Action Contract

### 4.1 Observation

```python
obs_t: np.ndarray, shape=(64, 64, 3), dtype=uint8
# RGB rendered dari WPF onboard camera (Plan 2 BotCameraRenderer)
```

### 4.2 Action Encoding

```python
# Raw action dari MultiDiscrete
action_t: np.ndarray, shape=(3,), dtype=int
# [direction_idx ∈ [0,4] (N/S/E/W/Stay), accel_mode ∈ [0,2], decel_mode ∈ [0,2]]

# Concatenated one-hot per dimensi untuk MDN-RNN input
# Alasan: preserves independence antar sub-action; lebih kompak (11 vs 45)
def encode_action_onehot(action: np.ndarray) -> np.ndarray:
    """MultiDiscrete [5,3,3] → concatenated one-hot (11,)
    [dir_oh(5) | accel_oh(3) | decel_oh(3)]
    """
    dir_idx, acc_idx, dec_idx = int(action[0]), int(action[1]), int(action[2])
    return np.concatenate([
        np.eye(5)[dir_idx],   # (5,)
        np.eye(3)[acc_idx],   # (3,)
        np.eye(3)[dec_idx],   # (3,)
    ])                        # (11,)
```

**Catatan:** `encode_action_onehot()` dan `decode_action_flat()` didefinisikan **sekali**
di `rmfs_gym/utils/action_utils.py` (Plan 2 — canonical).
Plan 3 menggunakan fungsi yang sama melalui import:
```python
from rmfs_gym.utils.action_utils import encode_action_onehot, decode_action_flat
```
Tidak ada duplikasi file `action_utils.py` di `rmfs_worldmodel/`.

---

## 5. V-Model — VAE Visual Encoder

### 5.1 Arsitektur

```
Encoder:
  Input:  obs_t (3, 64, 64) uint8
          → normalize INSIDE encoder: x = x / 255.0  → float [0, 1]
  Conv2d(3,   32,  4, stride=2) → (32, 31, 31)  + ReLU
  Conv2d(32,  64,  4, stride=2) → (64, 14, 14)  + ReLU
  Conv2d(64, 128,  4, stride=2) → (128,  6,  6) + ReLU
  Conv2d(128,256,  4, stride=2) → (256,  2,  2) + ReLU
  Flatten                        → (1024,)
  Linear(1024, 32)               → μ (32,)
  Linear(1024, 32)               → log σ (32,)    ← dua head terpisah (paper asli)
  Reparameterize: z = μ + exp(log σ) × ε, ε ~ N(0, I)   → z (32,)

Decoder:
  Input:  z (32,)
  Linear(32, 1024) → (1024,)
  Reshape          → (1024, 1, 1)                  ← paper asli: 1×1 bukan 2×2
  ConvTranspose2d(1024,128, 5, stride=2) → (128,  5,  5) + ReLU
  ConvTranspose2d(128,  64, 5, stride=2) → (64,  13, 13) + ReLU
  ConvTranspose2d(64,   32, 6, stride=2) → (32,  30, 30) + ReLU
  ConvTranspose2d(32,    3, 6, stride=2) → (3,   64, 64) + Sigmoid
  ← Tidak ada Upsample: decoder langsung menghasilkan 64×64
```

### 5.2 Loss Function

```python
def vae_loss(recon_target, obs_recon, mu, logsigma, beta=1.0):
    """
    recon_target: (B, 3, 64, 64) float [0,1]  — obs/255.0
    obs_recon:    (B, 3, 64, 64) float [0,1]  — sigmoid decoder output
    mu:           (B, 32)
    logsigma:     (B, 32)  ← log σ (bukan log σ²); sesuai paper asli
    """
    B = recon_target.size(0)
    # Reconstruction: MSE per pixel (both in [0,1] range)
    recon_loss = F.mse_loss(obs_recon, recon_target, reduction='sum') / B

    # KL divergence: -0.5 × Σ(1 + 2·log σ - μ² - σ²)
    kl_loss = -0.5 * torch.sum(1 + 2 * logsigma - mu.pow(2) - (2 * logsigma).exp()) / B

    return recon_loss + beta * kl_loss, recon_loss.item(), kl_loss.item()

# beta = 1.0 (standard VAE, sesuai paper Ha & Schmidhuber)
# Tidak menggunakan β-VAE (β > 1) — prioritas rekonstruksi visual untuk controller
```

---

## 6. M-Model — MDN-RNN Dynamics

### 6.1 Arsitektur

```
Input per timestep: concat(z_t, a_flat_t) ∈ R^(32+11) = R^43

LSTM:
  input_size  = 43       (32 latent + 11 concatenated one-hot action)
  hidden_size = 256
  num_layers  = 1
  → h_{t+1} ∈ R^256, c_{t+1} ∈ R^256

GMM Linear (satu layer tunggal — paper asli ctallec/world-models):
  Input:  h_{t+1} (256,)
  Linear(256, (2×32 + 1)×5 + 2) = Linear(256, 327)
  Output raw (327,) di-split menjadi:
    mus    = raw[  0:160] → reshape (5, 32)              → μ  per mixture component
    sigmas = raw[160:320] → exp(sigmas) → (5, 32)        → σ  per mixture component
    pi     = raw[320:325] → softmax     → (5,)            → π  mixture weights
    r̂     = raw[325]     → scalar                        → reward prediction (raw, MSE loss)
    d̂     = raw[326]     → sigmoid     → scalar ∈ [0,1]  → done probability

← TIDAK ada hidden layer terpisah untuk reward/done;
  semua output dari satu Linear(256, 327) — persis paper Ha & Schmidhuber 2018
```

### 6.2 Sampling dari MDN

```python
def forward_mdn_rnn(mdn_rnn, z_seq, a_flat_seq, h, c):
    """
    z_seq:     (B, T, 32)
    a_flat_seq:(B, T, 11)
    h, c:      (1, B, 256)  LSTM hidden/cell state

    Returns: mus, sigmas, pi, rs, ds  — shape (B, T, ...)
    """
    lstm_input = torch.cat([z_seq, a_flat_seq], dim=-1)  # (B, T, 43)
    # PyTorch LSTM expects (T, B, input_size)
    out, (h, c) = mdn_rnn.lstm(lstm_input.transpose(0,1), (h, c))
    out = out.transpose(0, 1)                            # (B, T, 256)

    gmm_out = mdn_rnn.gmm_linear(out)                   # (B, T, 327)

    # Split output sesuai paper ctallec/world-models
    K, L = 5, 32                                         # K=gaussians, L=latent_size
    mus    = gmm_out[:, :, :K*L].view(-1, gmm_out.size(1), K, L)      # (B,T,5,32)
    sigmas = torch.exp(gmm_out[:, :, K*L:2*K*L].view(-1, gmm_out.size(1), K, L))  # exp → σ > 0
    pi     = F.softmax(gmm_out[:, :, 2*K*L:2*K*L+K], dim=-1)          # (B,T,5)
    rs     = gmm_out[:, :, 2*K*L+K]                                    # (B,T) reward
    ds     = torch.sigmoid(gmm_out[:, :, 2*K*L+K+1])                  # (B,T) done ∈ [0,1]

    return mus, sigmas, pi, rs, ds, h, c


def sample_next_z(pi, mu, sigma, temperature=1.0):
    """
    pi:    (K,)      mixture weights (sudah softmax)
    mu:    (K, 32)   component means
    sigma: (K, 32)   component stds (sudah exp)
    temperature: τ — 1.0 saat training, 0.5 saat dreaming (lebih deterministik)

    Returns: z_next (32,)  — inference step tunggal (numpy)
    """
    # Temperature scaling pada log pi
    log_pi = np.log(pi + 1e-8) / temperature
    pi_scaled = np.exp(log_pi - log_pi.max())
    pi_scaled /= pi_scaled.sum()

    # Pilih mixture component
    k = np.random.choice(len(pi_scaled), p=pi_scaled)

    # Sample z dari N(μ_k, σ_k × τ)
    return mu[k] + sigma[k] * np.random.randn(32) * temperature
```

### 6.3 Loss Function

```python
def mdn_rnn_loss(z_next_actual, mus, sigmas, pi, rs, ds, r_actual, d_actual):
    """
    Sesuai paper ctallec/world-models — semua output dari single gmm_linear.

    z_next_actual: (B, T, 32)  — encoded dengan VAE frozen (target z_{t+1})
    mus:    (B, T, K, 32)      — dari gmm_linear split
    sigmas: (B, T, K, 32)      — sudah exp (σ > 0)
    pi:     (B, T, K)          — sudah softmax
    rs:     (B, T)             — raw reward prediction
    ds:     (B, T)             — sudah sigmoid (done probability)
    r_actual: (B, T)           — dari dataset Plan 2
    d_actual: (B, T)           — done flag (bool → float)
    """
    B, T, K, L = mus.shape   # K=5, L=32

    # --- GMM NLL Loss (z_{t+1} prediction) ---
    z_exp    = z_next_actual.unsqueeze(2).expand(-1, -1, K, -1)  # (B,T,K,32)
    log_prob = Normal(mus, sigmas).log_prob(z_exp).sum(-1)        # (B,T,K) — σ dari exp, selalu > 0
    log_mix  = log_prob + torch.log(pi + 1e-8)                   # (B,T,K)
    nll_loss = -torch.logsumexp(log_mix, dim=-1).mean()

    # --- Reward Prediction Loss ---
    reward_loss = F.mse_loss(rs, r_actual)

    # --- Done Prediction Loss ---
    done_loss = F.binary_cross_entropy(ds, d_actual.float())

    total = NLL_WEIGHT * nll_loss + REWARD_WEIGHT * reward_loss + DONE_WEIGHT * done_loss
    return total, nll_loss.item(), reward_loss.item(), done_loss.item()

# Loss weights (sesuai paper):
NLL_WEIGHT    = 1.0
REWARD_WEIGHT = 1.0
DONE_WEIGHT   = 1.0
```

---

## 7. C-Model — Controller via CMA-ES

### 7.1 Arsitektur Controller

```
Input:  concat(z_t, h_t) ∈ R^(32 + 256) = R^288
Hidden: Linear(288, 128) + Tanh                ← OPSIONAL: bisa pure linear
Output: Linear(128, 11)                        ← 5 + 3 + 3 = 11 logits

Atau pure linear (minimal):
  Output: Linear(288, 11)
  Parameter count: 288 × 11 + 11 = 3,179 parameter
  → Lebih sedikit parameter dari sebelumnya (3,179 vs lama 4,335) = CMA-ES lebih efisien
```

**Reward constants (identik dengan Plan 2 §6.1 — tidak didefinisikan ulang di sini):**

| Konstanta | Nilai | Peran |
|---|---|---|
| `ALPHA` | 1.0 | Throughput weight (orders completed) |
| `BETA`  | 0.5 | Collision penalty weight |
| `GAMMA` | 0.1 | Idle penalty weight |

Reward yang diprediksi MDN-RNN reward head dan yang diakumulasikan selama CMA-ES fitness rollout menggunakan konstanta yang sama.

**Alasan CMA-ES (bukan gradient-based):**
- Controller parameter count kecil (≤ 3,179) → population-based feasible
- Tidak perlu backprop melalui MDN-RNN (non-differentiable sampling)
- CMA-ES lebih stabil untuk optimization landscape dengan reward sparse
- Sesuai referensi asli Ha & Schmidhuber 2018

### 7.2 Action Sampling dari Controller Output

```python
def controller_act(z_t, h_t, controller_params, deterministic=False):
    """
    z_t:     (32,) numpy
    h_t:     (256,) numpy
    controller_params: flat numpy array (3179,) — parameter CMA-ES
    """
    # Reconstruct weight matrix dari flat params
    W = controller_params[:288*11].reshape(288, 11)
    b = controller_params[288*11:]                    # (11,)

    inp    = np.concatenate([z_t, h_t])              # (288,)
    logits = np.tanh(inp @ W + b)                    # (11,)

    dir_logits = logits[0:5]
    acc_logits = logits[5:8]
    dec_logits = logits[8:11]

    if deterministic:
        direction = np.argmax(dir_logits)
        accel     = np.argmax(acc_logits)
        decel     = np.argmax(dec_logits)
    else:
        # Softmax sampling untuk eksplorasi
        direction = np.random.choice(5, p=softmax(dir_logits))
        accel     = np.random.choice(3, p=softmax(acc_logits))
        decel     = np.random.choice(3, p=softmax(dec_logits))

    return np.array([direction, accel, decel])
```

### 7.3 CMA-ES Fitness Function

```python
def fitness_function(controller_params, vae, mdn_rnn, n_rollouts=16, horizon=16,
                     use_dream=True):
    """
    Evaluasi satu set parameter controller.
    use_dream=True  → rollout dalam imajinasi (M model, tidak butuh env)
    use_dream=False → rollout dalam Plan 2 Gym env (lebih akurat, lebih lambat)

    Returns: negative cumulative reward (CMA-ES minimizes)
    """
    total_reward = 0.0

    for _ in range(n_rollouts):
        # Ambil starting state dari replay buffer (real obs + real h)
        obs_0, h_0 = sample_starting_state_from_buffer()

        # Encode starting obs
        z_0 = vae.encode(obs_0)
        h_t, c_t = h_0, np.zeros(256)

        ep_reward = 0.0
        for t in range(horizon):
            # Controller memilih action
            a_t = controller_act(z_0 if t == 0 else z_t, h_t, controller_params)

            if use_dream:
                # MDN-RNN prediksi state berikutnya (tanpa env nyata)
                z_cur   = z_t if t > 0 else z_0
                a_flat  = encode_action_onehot(a_t)                   # (11,)
                inp     = np.concatenate([z_cur, a_flat])[None, None] # (1,1,43) torch
                inp_t   = torch.FloatTensor(inp)
                with torch.no_grad():
                    lstm_out, (h_next_t, c_next_t) = mdn_rnn.lstm(
                        inp_t.transpose(0,1),
                        (torch.FloatTensor(h_t[None]), torch.FloatTensor(c_t[None])))
                    gmm = mdn_rnn.gmm_linear(lstm_out.transpose(0,1).squeeze(1))  # (1,327)
                K, L = 5, 32
                mus_np    = gmm[0, :K*L].view(K, L).numpy()
                sigmas_np = torch.exp(gmm[0, K*L:2*K*L]).view(K, L).numpy()
                pi_np     = F.softmax(gmm[0, 2*K*L:2*K*L+K], dim=0).numpy()
                r_hat     = gmm[0, 2*K*L+K].item()

                h_next = h_next_t.squeeze(0).squeeze(0).numpy()   # (256,)
                c_next = c_next_t.squeeze(0).squeeze(0).numpy()   # (256,)
                z_next = sample_next_z(pi_np, mus_np, sigmas_np, temperature=DREAM_TEMPERATURE)

                ep_reward += r_hat
                z_t, h_t, c_t = z_next, h_next, c_next
            else:
                # Rollout di Plan 2 Gym env (fine-tuning saja)
                obs_next, r, done, _ = gym_env.step_single_bot(a_t)
                z_next = vae.encode(obs_next)
                a_flat = encode_action_onehot(a_t)
                h_next, c_next, *_ = mdn_rnn.step(
                    np.concatenate([z_next, a_flat]), h_t, c_t)

                ep_reward += r
                z_t, h_t, c_t = z_next, h_next, c_next
                if done:
                    break

        total_reward += ep_reward

    return -total_reward / n_rollouts   # negatif karena CMA-ES minimizes

# Parameter dreaming:
DREAM_TEMPERATURE = 0.5   # lebih deterministik saat dream (kurangi noise MDN)
```

---

## 8. Training Phase A — VAE

### 8.1 Data

```
Input:   Semua frame dari Plan 2 Phase 4 dataset
         Hanya obs_t — tidak perlu actions atau rewards
         ~1M frames total (N_episodes × T_steps × N_bots)
Split:   90% train / 10% validation
Format:  obs (64,64,3) uint8, dishuffle (urutan tidak penting untuk VAE)
```

### 8.2 Training Loop

```python
# rmfs_worldmodel/training/train_vae.py

optimizer = Adam(vae.parameters(), lr=1e-4)
scheduler = CosineAnnealingLR(optimizer, T_max=N_EPOCHS)

for epoch in range(N_EPOCHS):                          # N_EPOCHS = 100
    for batch_obs in dataloader:                       # batch_size = 64
        # batch_obs: (B, 3, 64, 64) uint8 — normalization done INSIDE vae.encoder.forward()
        # Reconstruction target: [0,1] range (matches sigmoid decoder output)
        recon_target = batch_obs.float() / 255.0

        # Forward — encoder normalizes internally: x/255.0 → [0,1]
        obs_recon, mu, logsigma = vae(batch_obs)

        # Loss: recon against [0,1] target, KL against N(0,I)
        loss, recon_l, kl_l = vae_loss(recon_target, obs_recon, mu, logsigma, beta=1.0)

        # Update
        optimizer.zero_grad()
        loss.backward()
        torch.nn.utils.clip_grad_norm_(vae.parameters(), max_norm=100.0)
        optimizer.step()

    scheduler.step()

    # Validasi setiap 10 epoch
    if epoch % 10 == 0:
        val_loss = evaluate_vae(vae, val_dataloader)
        log(f"Epoch {epoch}: train={loss:.4f}, val={val_loss:.4f}, "
            f"recon={recon_l:.4f}, kl={kl_l:.4f}")
        save_reconstruction_samples(vae, val_dataloader, epoch)   # visual check
        torch.save(vae.state_dict(), f"checkpoints/vae_epoch{epoch}.pt")
```

### 8.3 Hyperparameter Phase A

| Parameter | Nilai | Keterangan |
|---|---|---|
| `z_dim` | 32 | Latent dimension |
| `beta` | 1.0 | Standard VAE (bukan β-VAE) |
| Batch size | 64 | Per gradient step |
| Epochs | 100 | ~1.5M gradient steps total |
| LR | 1e-4 | Adam |
| LR schedule | CosineAnnealing | T_max = N_EPOCHS |
| Grad clip | 100.0 | Max norm |

### 8.4 Acceptance Criteria Phase A

- Validation reconstruction loss (MSE) < **0.02** (normalized [0,1])
- KL divergence stabil: tidak collapse ke 0, tidak explode > 50
- Visual inspection: reconstructed frames menunjukkan pod, koridor, lantai yang recognizable
- `z_t` dari frame berbeda di area warehouse berbeda memiliki jarak Euclidean yang berbeda
  (latent space bermakna secara spatial)

**Setelah criteria terpenuhi:** Freeze VAE. Phase B menggunakan encoder VAE (frozen) untuk
encode semua obs dalam dataset.

---

## 9. Training Phase B — MDN-RNN

### 9.1 Data Preparation

```python
# Sebelum training MDN-RNN:
# 1. Encode semua frame dalam dataset menggunakan VAE (frozen)
# 2. Simpan sebagai z-sequence per episode per bot

# rmfs_worldmodel/data/encode_dataset.py
vae.eval()
with torch.no_grad():
    for episode in dataset:
        for bot_id, traj in episode.items():
            obs_seq    = traj["obs"]                           # (T, 64, 64, 3) uint8, HWC
            # Permute HWC → CHW (PyTorch Conv2d expects channels-first)
            obs_chw    = torch.from_numpy(obs_seq).permute(0, 3, 1, 2)  # (T, 3, 64, 64)
            z_seq      = vae.encode_batch(obs_chw)             # (T, 32)
            np.save(f"z_dataset/{episode_id}_{bot_id}_z.npy", z_seq)
            # actions dan rewards tetap dari traj["actions"], traj["rewards"]

# Input MDN-RNN training:
#   z_t (32,) + a_flat_t (11,) → predict z_{t+1} (32,), r_t (1,), d_t (1,)
```

### 9.2 Training Loop

```python
# rmfs_worldmodel/training/train_mdn_rnn.py

optimizer = Adam(mdn_rnn.parameters(), lr=1e-3)
scheduler = ReduceLROnPlateau(optimizer, patience=5, factor=0.5)

for epoch in range(N_EPOCHS):                              # N_EPOCHS = 50
    for z_seq, a_seq, r_seq, d_seq in sequence_dataloader:
        # z_seq: (B, T, 32), a_seq: (B, T, 3), r_seq: (B, T), d_seq: (B, T)
        B, T, _ = z_seq.shape

        # Encode actions ke one-hot
        a_flat = encode_action_onehot_batch(a_seq)         # (B, T, 11)

        # Init hidden state
        h = torch.zeros(1, B, 256)
        c = torch.zeros(1, B, 256)

        # Truncated BPTT: feed z_t + a_t, predict z_{t+1}, r_t, d_t
        lstm_input = torch.cat([z_seq[:, :-1], a_flat[:, :-1]], dim=-1)  # (B, T-1, 43)
        lstm_out, _ = mdn_rnn.lstm(lstm_input.transpose(0,1), (h, c))
        lstm_out = lstm_out.transpose(0, 1)                # (B, T-1, 256)

        # Single gmm_linear head — sesuai paper ctallec/world-models
        gmm_out = mdn_rnn.gmm_linear(lstm_out)             # (B, T-1, 327)
        K, L = 5, 32
        mus    = gmm_out[:, :, :K*L].view(B, -1, K, L)                      # (B,T-1,5,32)
        sigmas = torch.exp(gmm_out[:, :, K*L:2*K*L].view(B, -1, K, L))     # (B,T-1,5,32)
        pi     = F.softmax(gmm_out[:, :, 2*K*L:2*K*L+K], dim=-1)           # (B,T-1,5)
        rs     = gmm_out[:, :, 2*K*L+K]                                     # (B,T-1)
        ds     = torch.sigmoid(gmm_out[:, :, 2*K*L+K+1])                   # (B,T-1)

        # Target: z_{t+1} = z_seq[:, 1:]
        loss, nll_l, rew_l, done_l = mdn_rnn_loss(
            z_seq[:, 1:], mus, sigmas, pi, rs, ds,
            r_seq[:, :-1], d_seq[:, :-1])

        optimizer.zero_grad()
        loss.backward()
        torch.nn.utils.clip_grad_norm_(mdn_rnn.parameters(), max_norm=100.0)
        optimizer.step()

    val_loss = evaluate_mdn_rnn(mdn_rnn, vae, val_dataloader)
    scheduler.step(val_loss)
    log(f"Epoch {epoch}: nll={nll_l:.4f}, reward={rew_l:.4f}, done={done_l:.4f}")
    torch.save(mdn_rnn.state_dict(), f"checkpoints/mdn_rnn_epoch{epoch}.pt")
```

### 9.3 Hyperparameter Phase B

| Parameter | Nilai | Keterangan |
|---|---|---|
| Sequence length T | 64 | Timestep per training sequence |
| Batch size | 32 sequences | Per gradient step |
| LSTM hidden dim | 256 | `h_t` dimension |
| MDN mixture K | 5 | Gaussian mixture components |
| Epochs | 50 | ~250K gradient steps |
| LR awal | 1e-3 | Adam |
| LR schedule | ReduceLROnPlateau | patience=5, factor=0.5 |
| Grad clip | 100.0 | Max norm |
| Temperature (sampling) | 1.0 saat train, 0.5 saat dream | |

### 9.4 Acceptance Criteria Phase B

- NLL loss validasi < **-0.5** (log-likelihood z_{t+1} yang cukup tinggi)
- Reward prediction R² > **0.70** pada validation split
- **Dream sanity check:** mulai dari z_0 nyata, unroll 16 langkah tanpa obs →
  frame hasil decode MDN secara visual masih menyerupai warehouse (corridor, pod)
  (bukan noise random) — ini validasi kualitas dynamics model
- Done prediction accuracy > **90%** (episode end terdeteksi dengan benar)

**Setelah criteria terpenuhi:** Freeze VAE + MDN-RNN. Phase C menggunakan keduanya
sebagai "mesin simulasi imajinasi" yang tidak di-update lagi.

---

## 10. Training Phase C — Controller CMA-ES

### 10.1 Setup CMA-ES

```python
# rmfs_worldmodel/training/train_controller.py
import cma

# Jumlah parameter controller
# Linear: W(288×11) + b(11) = 3,179
# MLP 1 hidden layer: W1(288×128) + b1(128) + W2(128×15) + b2(15) = 39,055
# → Gunakan PURE LINEAR untuk efisiensi CMA-ES

N_PARAMS   = 288 * 11 + 11    # = 3,179
POPULATION = 64                # ukuran populasi CMA-ES per generasi
SIGMA0     = 0.1               # initial standard deviation

# Init CMA-ES
x0  = np.zeros(N_PARAMS)       # mulai dari zero (bukan random — lebih stabil)
es  = cma.CMAEvolutionStrategy(x0, SIGMA0, {
    'popsize': POPULATION,
    'maxiter': 200,             # max generasi
    'tolx':    1e-6,            # konvergensi tolerance
    'verbose': 1,
})
```

### 10.2 Training Loop CMA-ES

```python
# Freeze VAE + MDN-RNN
vae.eval();     [p.requires_grad_(False) for p in vae.parameters()]
mdn_rnn.eval(); [p.requires_grad_(False) for p in mdn_rnn.parameters()]

best_reward = -np.inf
generation  = 0

while not es.stop():
    # 1. Sample populasi (POPULATION=64 kandidat parameter)
    solutions = es.ask()                                   # list of (3179,) arrays

    # 2. Evaluasi fitness setiap kandidat (bisa diparalelisasi)
    fitnesses = parallel_map(
        lambda params: fitness_function(
            params, vae, mdn_rnn,
            n_rollouts=N_ROLLOUTS,                         # N_ROLLOUTS = 16
            horizon=HORIZON,                               # HORIZON = 16 steps
            use_dream=True),
        solutions)

    # 3. Update CMA-ES distribution
    es.tell(solutions, fitnesses)

    # 4. Log progress
    best_in_gen = -min(fitnesses)
    log(f"Gen {generation}: best_reward={best_in_gen:.3f}, "
        f"mean={-np.mean(fitnesses):.3f}, sigma={es.sigma:.4f}")

    if best_in_gen > best_reward:
        best_reward = best_in_gen
        best_params = solutions[np.argmin(fitnesses)]
        np.save("checkpoints/controller_best.npy", best_params)

    generation += 1

log(f"Training selesai. Best reward: {best_reward:.3f}")
```

### 10.3 Hyperparameter Phase C

| Parameter | Nilai | Keterangan |
|---|---|---|
| Controller params | 3,179 | Linear W + b |
| Population size | 64 | Kandidat per generasi |
| σ₀ (initial std) | 0.1 | CMA-ES initial spread |
| Max generations | 200 | ~12,800 fitness evaluations total |
| Rollouts per eval | 16 | Average fitness over 16 starting states |
| Horizon per rollout | 16 | Langkah imajinasi (= 16 detik simulasi) |
| Dream temperature τ | 0.5 | MDN sampling kurang noise |
| Parallelism | 8 workers | `multiprocessing.Pool` |

### 10.4 Parallelisasi Fitness Evaluation

```python
# Setiap worker menjalankan 1 kandidat × 16 rollouts × 16 steps
# Tidak ada gradient → fully CPU-parallel, tidak butuh GPU sync

from multiprocessing import Pool

def evaluate_worker(args):
    params, vae_state, mdn_state, buffer_sample = args
    # Load model state (worker process punya copy sendiri)
    vae_w = build_vae(); vae_w.load_state_dict(vae_state)
    mdn_w = build_mdn_rnn(); mdn_w.load_state_dict(mdn_state)
    return fitness_function(params, vae_w, mdn_w,
                            n_rollouts=16, horizon=16, use_dream=True)

with Pool(8) as pool:
    fitnesses = pool.map(evaluate_worker,
        [(sol, vae.state_dict(), mdn_rnn.state_dict(), buf)
         for sol in solutions])
```

### 10.5 Fine-tuning Opsional di Real Env

Setelah CMA-ES konvergen, evaluasi controller terbaik di Plan 2 Gym env:

```python
# Jika gap antara dream reward dan real reward > threshold:
# → Fine-tune dengan CMA-ES di real env: population size lebih kecil (16), horizon = full episode

if (dream_best_reward - real_eval_reward) > FINE_TUNE_THRESHOLD:  # threshold = 0.3
    es_finetune = cma.CMAEvolutionStrategy(
        best_params, sigma0=0.01,   # mulai dari best, spread kecil
        {'popsize': 16, 'maxiter': 50})

    while not es_finetune.stop():
        solutions = es_finetune.ask()
        # Evaluasi di REAL GYM ENV (bukan dream)
        fitnesses = [fitness_function(p, vae, mdn_rnn,
                                      n_rollouts=4, use_dream=False)
                     for p in solutions]
        es_finetune.tell(solutions, fitnesses)
```

### 10.6 Acceptance Criteria Phase C

- Dream reward > **AgentAStar baseline reward** (dari Plan 2 Phase 4)
- Real env reward (setelah fine-tune jika diperlukan) > AgentAStar dengan confidence 95%
  (N=20 evaluation episodes)
- Entropy action distribution tidak collapse (bot tidak selalu pilih waypoint yang sama)
- **CMA-ES stopping criteria:**
  - Internal CMA-ES stop: `es.stop()` returns True ketika `maxiter=200` tercapai
    ATAU `tolx=1e-6` terpenuhi (parameter variance sangat kecil)
  - Post-hoc acceptance check (terpisah dari CMA-ES internal): `es.sigma < 1e-3`
    → distribusi pencarian sudah sangat sempit = solusi stabil
  - Jika `es.stop()` karena `maxiter` tapi `sigma >= 1e-3`: tandai sebagai belum konvergen,
    pertimbangkan naikkan `maxiter` atau kurangi noise rollout

---

## 11. Deployment ke Gymnasium Env

### 11.1 Inference Stack Per Bot

```python
# rmfs_worldmodel/inference/deploy.py

class WorldModelBot:
    """
    Menjalankan V+M+C untuk satu bot. Stateful (maintains h_t, c_t).
    """
    def __init__(self, vae_ckpt, mdn_rnn_ckpt, controller_params_path):
        self.vae      = build_vae()
        self.mdn_rnn  = build_mdn_rnn()
        self.vae.load_state_dict(torch.load(vae_ckpt))
        self.mdn_rnn.load_state_dict(torch.load(mdn_rnn_ckpt))
        self.vae.eval(); self.mdn_rnn.eval()
        self.ctrl_params = np.load(controller_params_path)
        self.reset()

    def reset(self):
        self.h_t = np.zeros(256)
        self.c_t = np.zeros(256)

    def act(self, obs: np.ndarray) -> np.ndarray:
        """
        obs: (64, 64, 3) uint8
        returns: (3,) int [direction_idx, accel_mode, decel_mode]
        """
        # V: encode observation
        with torch.no_grad():
            z_t = self.vae.encode(obs).numpy()          # (32,)

        # C: select action
        action = controller_act(z_t, self.h_t, self.ctrl_params)

        # M: update hidden state (untuk step berikutnya)
        a_flat  = encode_action_onehot(action)           # (11,)
        lstm_in = torch.FloatTensor(
            np.concatenate([z_t, a_flat])[None, None])  # (1,1,43)
        h_t = torch.FloatTensor(self.h_t[None, None])   # (1,1,256)
        c_t = torch.FloatTensor(self.c_t[None, None])   # (1,1,256)
        with torch.no_grad():
            _, (h_next, c_next) = self.mdn_rnn.lstm(
                lstm_in.transpose(0,1), (h_t.transpose(0,1), c_t.transpose(0,1)))
        self.h_t = h_next.squeeze().numpy()              # (256,)
        self.c_t = c_next.squeeze().numpy()              # (256,)

        return action
```

### 11.2 Integrasi dengan RMFSParallelEnv

```python
env = RMFSParallelEnv(host="localhost", port=7654)
bots = {
    agent: WorldModelBot(
        vae_ckpt="checkpoints/vae_final.pt",
        mdn_rnn_ckpt="checkpoints/mdn_rnn_final.pt",
        controller_params_path="checkpoints/controller_best.npy")
    for agent in env.possible_agents
}

obs, _ = env.reset(seed=42)
for bot in bots.values():
    bot.reset()

while env.agents:
    actions = {agent: bots[agent].act(obs[agent]) for agent in env.agents}
    obs, rewards, dones, truncated, infos = env.step(actions)
```

---

## 12. Evaluation & Benchmarking

### 12.1 Metrics (konsisten dengan Plan 1 Phase 3 — Benchmarking)

| Metric | WHCAv* | AgentAStar | WM Controller |
|---|---|---|---|
| Throughput (orders/hr) | baseline | Plan 2 Phase 4 | target ≥ AgentAStar |
| Avg reward per step | — | Plan 2 Phase 4 | — |
| Collision rate (per hr) | 0 | — | — |
| Deadlock count | — | — | — |
| VAE recon loss | — | — | logged |
| MDN-RNN NLL | — | — | logged |
| CMA-ES best fitness | — | — | logged |

### 12.2 Ablation Study

| Config | Deskripsi | Pertanyaan penelitian |
|---|---|---|
| A | AgentAStar baseline (Plan 1) | Upper bound feasible |
| B | Random policy via Plan 2 env | Lower bound |
| C | WM Controller, accel/decel fixed (1,1) | Kontribusi routing saja |
| D | WM Controller, full action space | Kontribusi accel/decel tambahan |
| E | WM Controller, dream fine-tune saja (no real env) | Gap dream vs reality |

---

## 13. Implementation Phases

### Phase 1: VAE Training (3–4 hari)

- `rmfs_worldmodel/models/vae.py` — Encoder, Decoder, VAE class
- `rmfs_worldmodel/data/dataset_loader.py` — Load `.npz`, yield frame batches
- `rmfs_worldmodel/training/train_vae.py` — Training loop §8.2
- Validation: visual reconstruction check, latent space PCA plot

### Phase 2: MDN-RNN Training (3–4 hari)

- `rmfs_worldmodel/models/mdn_rnn.py` — LSTM + MDN head + Reward head + Done head
- `rmfs_worldmodel/data/encode_dataset.py` — Encode semua frames dengan frozen VAE
- `rmfs_worldmodel/data/sequence_loader.py` — Load z-sequences per episode
- `rmfs_worldmodel/training/train_mdn_rnn.py` — Training loop §9.2
- Validation: dream sanity check (decode z_dream → visual frame)

### Phase 3: Controller Training via CMA-ES (2–3 hari)

- `rmfs_worldmodel/models/controller.py` — Linear controller W + b
- `rmfs_worldmodel/training/train_controller.py` — CMA-ES loop §10.2
- `rmfs_worldmodel/training/fitness.py` — `fitness_function()` + parallel workers
- `rmfs_worldmodel/training/finetune_controller.py` — Real env fine-tune §10.5
- Validation: acceptance criteria §10.6

### Phase 4: Deployment & Evaluation (2 hari)

- `rmfs_worldmodel/inference/deploy.py` — `WorldModelBot` class §11.1
- `rmfs_worldmodel/evaluation/evaluate.py` — N=20 episode benchmark
- Ablation study: Config A, B, C, D, E
- Output: CSV + matplotlib plots throughput vs episode

---

## 14. Risk Matrix

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| VAE latent space tidak bermakna spatial (z dari area berbeda tidak terpisah) | Medium | High | Tambahkan spatial loss: pasangan obs yang diambil dari bot berbeda posisi harus memiliki jarak z lebih besar |
| MDN posterior collapse: semua mixture weight ke satu component | Medium | High | Weight init MDN head kecil (std=0.01); monitor entropy mixture weight selama training |
| Dream divergence: setelah beberapa step, z_dream jadi noise (tidak realistis) | Medium | High | Kurangi dream temperature τ ke 0.3–0.5; batasi horizon ≤ 16 step |
| CMA-ES tidak konvergen (fitness landscape terlalu noisy) | Medium | High | Naikkan n_rollouts ke 32; perbesar population ke 128; kurangi σ₀ |
| Gap besar antara dream reward dan real reward (world model tidak cukup akurat) | High | High | Fine-tune di real env §10.5; tambahkan diversity ke dataset (ε-greedy collection) |
| Phase A-B-C secara sequential memakan waktu total > 2 minggu | Medium | Medium | Optimalkan per-phase: VAE max 100 epoch, MDN-RNN max 50 epoch, CMA-ES max 200 gen — semua ada acceptance criteria untuk early stop |
| Action encoding mismatch Plan 2 vs Plan 3 | Low | High | `action_utils.py` ada di `rmfs_gym` (canonical); Plan 3 import dari sana — tidak ada duplikasi; unit test round-trip |
| Controller overfits ke starting states dari replay buffer | Medium | Medium | Diversifikasi starting states: 50% dari AgentAStar, 50% dari random atau early episodes |

---

## 15. File Index

### Files to Create (New — Python)

| File | Purpose |
|------|---------|
| `rmfs_worldmodel/__init__.py` | Package init |
| `rmfs_worldmodel/models/vae.py` | Encoder, Decoder, VAE; `vae_loss()` |
| `rmfs_worldmodel/models/mdn_rnn.py` | LSTM, MDN head, Reward head, Done head; `mdn_rnn_loss()`, `sample_next_z()` |
| `rmfs_worldmodel/models/controller.py` | Linear controller; `controller_act()` |
| `rmfs_worldmodel/data/dataset_loader.py` | Load `.npz` frames & sequences dari Plan 2 |
| `rmfs_worldmodel/data/encode_dataset.py` | Encode semua obs → z-sequences dengan frozen VAE |
| `rmfs_worldmodel/data/sequence_loader.py` | Load z-sequences untuk MDN-RNN training |
| `rmfs_worldmodel/training/train_vae.py` | Phase A training loop |
| `rmfs_worldmodel/training/train_mdn_rnn.py` | Phase B training loop |
| `rmfs_worldmodel/training/train_controller.py` | Phase C CMA-ES loop |
| `rmfs_worldmodel/training/fitness.py` | `fitness_function()`, parallel worker |
| `rmfs_worldmodel/training/finetune_controller.py` | Phase C real env fine-tune (opsional) |
| `rmfs_worldmodel/inference/deploy.py` | `WorldModelBot` class (V+M+C inference) |
| `rmfs_worldmodel/evaluation/evaluate.py` | N-episode benchmark, ablation runner |
| `rmfs_worldmodel/configs/default.yaml` | Semua hyperparameter (§8.3, §9.3, §10.3) |
| `rmfs_worldmodel/setup.py` | Package install config |

### Shared Contracts (antara Plan 2 dan Plan 3)

| Contract | Defined in | Used in |
|---|---|---|
| Observation shape `(64, 64, 3)` uint8 | `PLAN_GymEnvironment.md §4.1` | `vae.py` encoder input |
| Action `MultiDiscrete([5, 3, 3])` | `PLAN_GymEnvironment.md §5.1` | `controller.py` output heads |
| `encode_action_onehot()` / `decode_action_flat()` | `rmfs_gym/utils/action_utils.py` **(canonical)** | `mdn_rnn.py` + `deploy.py` + `gym_client.py` — Plan 3 imports from `rmfs_gym` |
| Reward constants ALPHA=1.0 / BETA=0.5 / GAMMA=0.1 | `PLAN_GymEnvironment.md §6.1` | `mdn_rnn.py` reward head training target; CMA-ES fitness |
| Dataset `.npz` schema | `PLAN_GymEnvironment.md §10.4.2` | `dataset_loader.py` |
| &nbsp;&nbsp;• `obs`: `(T,64,64,3)` `uint8` | — | VAE training input |
| &nbsp;&nbsp;• `actions`: `(T,3)` `int32` | — | MDN-RNN action input |
| &nbsp;&nbsp;• `rewards`: `(T,)` `float32` | — | MDN-RNN reward head target |
| &nbsp;&nbsp;• `dones`: `(T,)` `bool_` | — | MDN-RNN done head target |
| Step duration 1.0s | `PLAN_GymEnvironment.md §7.6` | Temporal unit sequence MDN-RNN |

### Files NOT Modified

| File | Reason |
|------|--------|
| Semua C# files (Plan 1 + Plan 2) | World model sepenuhnya Python |
| `rmfs_gym/` (Plan 2) | Digunakan as-is via TCP; tidak ada perubahan |
