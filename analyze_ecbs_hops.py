import math, sys

g, mu_r, I_coeff = 9.8, 0.01, 0.15
a, dec, vMax = 1.0, 1.0, 1.5
mTotal = 300.0
P_IDLE = 43.27
edge_len = 1.0

def move_energy(m, distance):
    if distance <= 0: return 0
    dA = vMax**2 / (2*a)
    dD = vMax**2 / (2*dec)
    if dA + dD <= distance:
        vPeak = vMax
        dCruise = distance - dA - dD
    else:
        vPeak = math.sqrt(2*a*dec*distance/(a+dec))
        dCruise = 0.0
    e1 = m * (g*mu_r + a*I_coeff) * vPeak**2 / (2*a)
    e3 = m * g * mu_r * dCruise
    return e1 + e3

def travel_time(distance):
    dA = vMax**2 / (2*a)
    dD = vMax**2 / (2*dec)
    if dA + dD <= distance:
        return vMax/a + vMax/dec + (distance - dA - dD)/vMax
    else:
        vPeak = math.sqrt(2*a*dec*distance/(a+dec))
        return vPeak/a + vPeak/dec

def h_lb(m, remaining):
    return m * g * mu_r * remaining + P_IDLE * remaining / vMax

D = 5.0
dA_full = vMax**2/(2*a)
dD_full = vMax**2/(2*dec)

lines = []
lines.append("=== f-value comparison: 1-cell hop vs k-cell hop (D=5m total) ===")
lines.append(f"accel+decel zone = {dA_full+dD_full:.2f}m  (need > this for cruise phase)")
lines.append("")
lines.append(f"{'hop':>6}  {'ME(J)':>8}  {'t(s)':>6}  {'P_IDLE*t':>9}  {'h_remain':>9}  {'f_total':>9}  {'cruise':>7}")
lines.append("-"*65)

for k in [1, 2, 3, 4, 5]:
    dist = k * edge_len
    ME = move_energy(mTotal, dist)
    t  = travel_time(dist)
    remaining = D - dist
    h  = h_lb(mTotal, remaining)
    f  = ME + P_IDLE*t + h
    cruise = "YES" if (dA_full+dD_full <= dist) else "NO"
    lines.append(f"{k:>5}m  {ME:>8.1f}  {t:>6.2f}  {P_IDLE*t:>9.1f}  {h:>9.1f}  {f:>9.1f}  {cruise:>7}")

lines.append("")
lines.append("=== multi-hop vs single-hop: cumulative mechanical energy ===")
lines.append(f"{'config':>12}  {'mech_E(J)':>10}  {'penalty vs single':>18}")
for n in range(1, 8):
    multi_E  = n * move_energy(mTotal, 1.0)
    single_E = move_energy(mTotal, n * 1.0)
    lines.append(f"{n}x 1-cell   {multi_E:>10.1f}  vs {single_E:>6.1f}  penalty={multi_E-single_E:>+7.1f}J")

lines.append("")
lines.append("=== Why h() is loose (underestimates future E1 accel costs) ===")
ME1 = move_energy(mTotal, 1.0)
friction1 = mTotal * g * mu_r * 1.0
lines.append(f"actual ME per 1-cell hop : {ME1:.2f} J")
lines.append(f"h() assumes per cell     : {friction1:.2f} J  (friction only)")
lines.append(f"h() misses E1 accel cost : {ME1 - friction1:.2f} J per hop")
lines.append(f"h() underestimate ratio  : {(ME1 - friction1)/ME1*100:.0f}%")
lines.append("")
lines.append("==> With loose h(), f(1-cell) < f(5-cell), so ECBS always picks 1-cell hops.")
lines.append("==> Each short hop pays E1 accel, but h() never predicted that cost.")
lines.append("==> Result: stop-and-go, E3_cruise=~0, E1_accel >> CBS.")

sys.stdout.buffer.write(("\n".join(lines) + "\n").encode("utf-8"))
