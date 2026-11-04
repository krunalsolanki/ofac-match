# Tuning the extra-part penalty (development set)

Development set: 4,255 original and 2,159 harder variations, 2,000 two-part and 1,000 three-part ordinary names, 1,000 near misses. Thresholds for a fixed alert rate are set on all ordinary names. Selection rule: see [the evaluation protocol](evaluation-protocol.md).

| Penalty | All cases, alerts ≤ 5% (threshold) | All cases, alerts ≤ 10% | Original cases, alerts ≤ 1% | Extra middle name, alerts ≤ 5% | Extra middle name ≥ 85 | Three-part alerts ≥ 85 | Near-miss alerts, alerts ≤ 5% |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0.5 | 91.8% (92.9) | 95.9% (90.2) | 95.3% (97.2) | 14.3% | 82.0% | 7.1% | 7.2% |
| 0.4 | 93.2% (92.9) | 97.1% (90.2) | 95.3% (97.2) | 35.0% | 93.3% | 7.2% | 7.2% |
| 0.3 | 94.9% (93.0) | 98.4% (90.2) | 95.3% (97.2) | 63.0% | 100.0% | 8.7% | 6.8% |
| 0.2 | 96.6% (93.0) | 99.4% (90.3) | 95.3% (97.2) | 89.0% | 100.0% | 12.2% | 6.8% |
| 0.1 | 97.3% (93.1) | 99.3% (90.5) | 95.4% (97.2) | 100.0% | 100.0% | 22.5% | 6.6% |
| 0.0 | 96.6% (93.6) | 99.1% (91.0) | 95.5% (97.3) | 100.0% | 100.0% | 41.7% | 5.7% |
