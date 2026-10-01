"""DaqLink DC 掃描:量測 DAC → ADC 閉迴路的靜態誤差(需接上實機)

兩通道同時設為 DC,code 從 start 掃到 stop,每點收 N 筆樣本取平均。
比例式量測下理想 ADC 讀值 = DAC code,因此誤差直接以 LSB 表示。

對每個通道以最小平方法擬合 adc = gain × code + offset,拆出:
    offset      擬合直線在 code = 0 的截距(LSB)
    gain error  (gain − 1) × 100%
    INL         扣掉擬合直線後的最大殘差(LSB)
    max error   相對理想值(adc = code)的最大誤差(LSB)
    noise       每點樣本標準差的平均(LSB)

用法:
    pip install pyserial
    python dc_sweep.py COM15                           # 預設 100~3995、每 100 一點、每點 50 筆
    python dc_sweep.py COM15 --step 50 --samples 100   # 點更密、樣本更多
    python dc_sweep.py COM15 --out sweep.csv --plot    # 存 CSV 並畫圖(需 matplotlib)

結束時會把 MCU 恢復為：停止、100 Hz、開機預設波形。
"""

import argparse
import csv
import math
import sys
import time

from test_firmware import DC, DEFAULT_WAVES, Link, data_of, wave_cmd

CODE_MIN, CODE_MAX = 100, 3995          # 協定 5.4:避開 MCP4922 軌限區域


def sweep_codes(start, stop, step):
    codes = list(range(start, stop + 1, step))
    if codes[-1] != stop:
        codes.append(stop)
    return codes


def measure(link, code, samples, settle, timeout=3.0):
    """兩通道設為 DC code,丟掉前 settle 筆後收 samples 筆，回傳 [(adc0, adc1), ...]。"""
    link.send(wave_cmd(0, (DC, 0, 0, code)), wait=0)
    link.send(wave_cmd(1, (DC, 0, 0, code)), wait=0)

    got = []
    skipped = 0
    t_end = time.time() + timeout
    while len(got) < samples and time.time() < t_end:
        for seq, da, db, a0, a1, a2, a3 in data_of(link.collect(0.02)):
            if da != code or db != code:
                continue                    # 新的 code 還沒生效
            if skipped < settle:
                skipped += 1
                continue
            got.append((a0, a1))
    if len(got) < samples:
        raise RuntimeError(f"code {code}: only {len(got)}/{samples} samples in {timeout} s")
    return got[:samples]


def mean_std(xs):
    m = sum(xs) / len(xs)
    return m, math.sqrt(sum((x - m) ** 2 for x in xs) / len(xs))


def linear_fit(xs, ys):
    n = len(xs)
    mx, my = sum(xs) / n, sum(ys) / n
    sxx = sum((x - mx) ** 2 for x in xs)
    sxy = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    gain = sxy / sxx
    return gain, my - gain * mx


def analyze(codes, means, stds):
    gain, offset = linear_fit(codes, means)
    resid = [m - (gain * c + offset) for c, m in zip(codes, means)]
    err = [m - c for c, m in zip(codes, means)]
    return dict(offset=offset, gain_err=(gain - 1) * 100,
                inl=max(resid, key=abs), max_err=max(err, key=abs),
                noise=sum(stds) / len(stds))


def plot(rows, title):
    try:
        import matplotlib.pyplot as plt
    except ImportError:
        print("matplotlib not installed, skip plot (pip install matplotlib)")
        return
    codes = [r["code"] for r in rows]
    fig, ax = plt.subplots(figsize=(9, 5))
    for ch in (0, 1):
        ax.plot(codes, [r[f"err{ch}"] for r in rows], marker="o", ms=3, label=f"CH{ch} (DAC {'AB'[ch]})")
    ax.axhline(0, color="gray", lw=0.8)
    ax.set_xlabel("DAC code")
    ax.set_ylabel("ADC - DAC code (LSB)")
    ax.set_title(title)
    ax.grid(alpha=0.3)
    ax.legend()
    plt.tight_layout()
    plt.show()


def main():
    ap = argparse.ArgumentParser(description="DaqLink DC sweep: DAC -> ADC static error")
    ap.add_argument("port", help="COM port, e.g. COM15")
    ap.add_argument("--start", type=int, default=CODE_MIN)
    ap.add_argument("--stop", type=int, default=CODE_MAX)
    ap.add_argument("--step", type=int, default=100)
    ap.add_argument("--samples", type=int, default=50, help="samples per point")
    ap.add_argument("--settle", type=int, default=2, help="packets to drop after each code change")
    ap.add_argument("--rate", type=int, default=200, help="sample rate in Hz (10~200)")
    ap.add_argument("--out", help="save results to CSV")
    ap.add_argument("--plot", action="store_true", help="plot error vs code (needs matplotlib)")
    args = ap.parse_args()

    if not (CODE_MIN <= args.start < args.stop <= CODE_MAX) or args.step <= 0:
        ap.error(f"need {CODE_MIN} <= start < stop <= {CODE_MAX} and step > 0")

    codes = sweep_codes(args.start, args.stop, args.step)
    link = Link(args.port)
    rows = []
    t0 = time.time()
    try:
        link.configure(args.rate, [(DC, 0, 0, codes[0])] * 2)
        link.flush()
        link.send("start")

        print(f"{'code':>5} | {'CH0 mean':>8} {'err':>6} {'std':>5} | {'CH1 mean':>8} {'err':>6} {'std':>5}")
        for code in codes:
            got = measure(link, code, args.samples, args.settle)
            row = {"code": code}
            for ch in (0, 1):
                m, s = mean_std([g[ch] for g in got])
                row.update({f"mean{ch}": m, f"err{ch}": m - code, f"std{ch}": s,
                            f"min{ch}": min(g[ch] for g in got), f"max{ch}": max(g[ch] for g in got)})
            rows.append(row)
            print(f"{code:5d} | {row['mean0']:8.1f} {row['err0']:+6.1f} {row['std0']:5.2f} |"
                  f" {row['mean1']:8.1f} {row['err1']:+6.1f} {row['std1']:5.2f}")
    finally:
        link.configure(100, DEFAULT_WAVES)          # 恢復開機預設狀態
        link.close()

    print(f"\n{len(codes)} points x {args.samples} samples @ {args.rate} Hz ({time.time() - t0:.0f} s)")
    for ch in (0, 1):
        r = analyze(codes, [row[f"mean{ch}"] for row in rows], [row[f"std{ch}"] for row in rows])
        print(f"CH{ch} (DAC {'AB'[ch]}): offset {r['offset']:+.1f} LSB, gain error {r['gain_err']:+.3f} %, "
              f"INL {r['inl']:+.1f} LSB, max error {r['max_err']:+.1f} LSB, noise {r['noise']:.2f} LSB")

    if args.out:
        with open(args.out, "w", newline="") as f:
            w = csv.DictWriter(f, fieldnames=list(rows[0].keys()))
            w.writeheader()
            for row in rows:
                w.writerow({k: round(v, 3) if isinstance(v, float) else v for k, v in row.items()})
        print(f"saved {args.out}")

    if args.plot:
        plot(rows, f"DC sweep: {args.samples} samples/point @ {args.rate} Hz")


if __name__ == "__main__":
    main()
