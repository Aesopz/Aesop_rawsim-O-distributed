import math, sys

g, mu_r, I = 9.8, 0.01, 0.15
a, dec, vMax = 1.0, 1.0, 1.5
m = 300.0
P_IDLE = 43.27
edgeLen = 1.0

def ME(k):
    d = k * edgeLen
    dA = vMax**2/(2*a); dD = vMax**2/(2*dec)
    if dA+dD <= d:
        vP = vMax; dC = d - dA - dD
    else:
        vP = math.sqrt(2*a*dec*d/(a+dec)); dC = 0.0
    e1 = m*(g*mu_r + a*I)*vP**2/(2*a)
    e3 = m*g*mu_r*dC
    return e1+e3

def T(k):
    d = k * edgeLen
    dA = vMax**2/(2*a); dD = vMax**2/(2*dec)
    if dA+dD <= d:
        return vMax/a + vMax/dec + (d-dA-dD)/vMax
    vP = math.sqrt(2*a*dec*d/(a+dec))
    return vP/a + vP/dec

lines = []
lines.append("=== Scenario: straight corridor A->B->C->D (3 cells) ===")
lines.append("")
lines.append("Step 1: A* expands A (lastStopId=A=n), direction=East")
lines.append("  Hop extends: cell1=B, cell2=C, cell3=D")
lines.append("  pathFree at C (2 cells), yield C")
lines.append("")

E_A = 100.0  # accumulated energy at A
lines.append(f"  NodeEnergy[A] = {E_A:.1f} J  (some path cost before A)")
lines.append(f"  ME[2 cells] = {ME(2):.1f} J  (A->C continuous)")
E_C = E_A + ME(2) + P_IDLE*T(2)
lines.append(f"  NodeEnergy[C] = {E_A:.1f} + {ME(2):.1f} + {P_IDLE*T(2):.1f} = {E_C:.1f} J")
lines.append(f"  NodeBackpointerLastStopId[C] = A")

lines.append("")
lines.append("Step 2: A* expands C (n=C, lastStopId=A), direction=East (same, no turn)")
lines.append("  AddCheckPointDistances(A, C) -> driveDistance = 2*edgeLen, hopCells=2")
lines.append("  Extend to D: hopCells=3")
lines.append(f"  ME[3 cells] = {ME(3):.1f} J  (A->D continuous)")
lines.append(f"  timeToMove for A->D = {T(3):.2f}s")
lines.append("")
lines.append("  CURRENT (BUGGY) formula:")
E_D_buggy = E_C + ME(3) + P_IDLE*T(3)
lines.append(f"    NodeEnergy[D] = NodeEnergy[C] + ME[3] + P_IDLE*T[3]")
lines.append(f"                  = {E_C:.1f} + {ME(3):.1f} + {P_IDLE*T(3):.1f} = {E_D_buggy:.1f} J")
lines.append(f"    = E_A + ME[2] + P_IDLE*T[2] + ME[3] + P_IDLE*T[3]  <- DOUBLE COUNTED")
lines.append(f"    ME[2] double-counted: {ME(2):.1f} J  (A->C counted TWICE!)")
lines.append("")
lines.append("  CORRECT formula (mirror SpaceTimeAStar: use NodeEnergy[lastStopId]):")
E_D_correct = E_A + ME(3) + P_IDLE*T(3)
lines.append(f"    NodeEnergy[D] = NodeEnergy[A] + ME[3] + P_IDLE*T[3]")
lines.append(f"                  = {E_A:.1f} + {ME(3):.1f} + {P_IDLE*T(3):.1f} = {E_D_correct:.1f} J")
lines.append(f"    Error from bug: {E_D_buggy - E_D_correct:+.1f} J  (+{(E_D_buggy-E_D_correct)/E_D_correct*100:.0f}%)")
lines.append("")
lines.append("=== Why this causes stop-and-go ===")
lines.append("")
lines.append("Compare: reach D via 1-hop (A->D) vs 3-hops (A->B->C->D):")
lines.append("")

# 1 hop: A->D directly (pathFree at cell 1=B skipped, assume cell1,2 not free, cell3=D free)
# This means expand A, pathFree at D (3 cells)
E_D_1hop = E_A + ME(3) + P_IDLE*T(3)
lines.append(f"  1 long hop (A->D, 3 cells): NodeEnergy[D] = {E_D_1hop:.1f} J")

# 3 short hops: A->B, B->C, C->D
E_B = E_A + ME(1) + P_IDLE*T(1)
E_C2 = E_B + ME(1) + P_IDLE*T(1)   # lastStopId=B, n=B => NodeEnergy[B]+ME[1]
E_D_3hop = E_C2 + ME(1) + P_IDLE*T(1)  # lastStopId=C, n=C
lines.append(f"  3 short hops (each 1 cell, no bug since each hop starts at n=lastStopId):")
lines.append(f"    A->B: {E_B:.1f}  B->C: {E_C2:.1f}  C->D: {E_D_3hop:.1f} J")
lines.append(f"  1 long hop NodeEnergy[D]  = {E_D_1hop:.1f} J")
lines.append(f"  3 short hops NodeEnergy[D]= {E_D_3hop:.1f} J")
lines.append(f"  (correct: long hop is cheaper by {E_D_3hop - E_D_1hop:.1f} J)")
lines.append("")
lines.append("  BUT with buggy intermediate: NodeEnergy[D via C] = {:.1f} J".format(E_D_buggy))
lines.append(f"  ECBS sees: long-hop-through-C ({E_D_buggy:.1f}) vs 3-short ({E_D_3hop:.1f})")
lines.append(f"  Bug makes long hop look MORE expensive -> ECBS picks stop-and-go!")
lines.append("")
lines.append("=== The fix ===")
lines.append("Change line 533:")
lines.append("  FROM: NodeEnergyTemp.Add(NodeEnergy[n]         + turnE + supportTurn + moveE)")
lines.append("  TO:   NodeEnergyTemp.Add(NodeEnergy[lastStopId] + turnE + supportTurn + moveE)")
lines.append("  (mirrors SpaceTimeAStar: NodeTimeTemp.Add(NodeTime[lastStopId] + ...))")

sys.stdout.buffer.write(("\n".join(lines)+"\n").encode("utf-8"))
