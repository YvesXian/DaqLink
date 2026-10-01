"""DaqLink 韌體回歸測試(需接上實機)

透過 COM Port 對 MCU 下命令，驗證 docs/protocol.md 規定的行為。
DDS 波形以 bit-exact 的 Python 參考模型逐點比對(與 wave.c 相同的整數運算)。

用法:
    pip install pyserial
    python test_firmware.py COM15                  # 全部測試(約 1 分鐘)
    python test_firmware.py COM15 --only cmd dds   # 只跑指定群組
    python test_firmware.py COM15 --skip stress    # 略過壓力測試

測試群組:
    cmd     命令與回應:START/STOP 冪等、SET_RATE 限制、NAK 錯誤碼、CRC 錯誤計數
    rate    取樣率 50/100/200 Hz 的封包數與連續性
    dds     DDS 波形逐點比對(正弦/三角/方波/DC、不同取樣率、邊界值)
    switch  採樣中 SET_WAVE:相位連續切換
    param   SET_WAVE / SET_RATE 參數檢查
    stress  並行壓力測試:ISR 與主迴圈同時寫 TX buffer、SET_WAVE 參數撕裂

結束時會把 MCU 恢復為：停止、100 Hz、開機預設波形。全部通過時 exit code 為 0。
"""

import argparse
import math
import struct
import sys
import time

import serial

from daq_monitor import (Parser, build_frame, fmt_frame, parse_cmd,
                         TYPE_ACK, TYPE_DATA, TYPE_NAK, TYPE_STATUS)

GROUPS = ["cmd", "rate", "dds", "switch", "param", "stress"]

T_START, T_STOP, T_SET_RATE, T_SET_WAVE = 0x10, 0x11, 0x12, 0x13
ERR_UNKNOWN, ERR_LEN, ERR_RANGE, ERR_BUSY = 0x01, 0x02, 0x03, 0x04

DC, SIN, TRI, SQ = 0, 1, 2, 3
DEFAULT_WAVES = [(SIN, 10, 1500, 2048), (TRI, 10, 1500, 2048)]   # 協定 5.6
M32 = 0xFFFFFFFF


# --------------------------------------------------------------------------
# DDS 參考模型(與 wave.c 相同的整數運算)
# --------------------------------------------------------------------------
SINE_TAB = [round(32767 * math.sin(2 * math.pi * i / 256)) for i in range(256)]


def calc_inc(freq_x10, rate):
    return ((freq_x10 << 32) // (10 * rate)) & M32


class WaveModel:
    def __init__(self, cfg, rate, phase=0):
        self.type, self.freq_x10, amp, self.offset = cfg
        self.amp = 0 if self.type == DC else amp
        self.inc = 0 if self.type == DC else calc_inc(self.freq_x10, rate)
        self.phase = phase

    def next(self):
        if self.type == SIN:
            s = SINE_TAB[self.phase >> 24]
        elif self.type == TRI:
            p = (self.phase >> 16) & 0xFFFF
            s = 2 * p - 32768 if p < 0x8000 else 32767 - 2 * (p - 32768)
        elif self.type == SQ:
            s = -32767 if self.phase & 0x80000000 else 32767
        else:
            s = 0
        self.phase = (self.phase + self.inc) & M32
        return (self.offset + ((self.amp * s) >> 15)) & 0xFFFF


def phase_at(state, seq, rate):
    """state = (seq_ref, phase_ref, cfg):推算該通道在 seq 時的相位。"""
    seq_ref, phase_ref, cfg = state
    inc = 0 if cfg[0] == DC else calc_inc(cfg[1], rate)
    return (phase_ref + inc * ((seq - seq_ref) & 0xFFFF)) & M32


# --------------------------------------------------------------------------
# 串列埠與結果紀錄
# --------------------------------------------------------------------------
class Link:
    def __init__(self, port):
        self.ser = serial.Serial(port, 115200, timeout=0)
        self.parser = Parser()

    def close(self):
        self.ser.close()

    def flush(self):
        time.sleep(0.05)
        self.ser.reset_input_buffer()
        self.parser.buf.clear()

    def collect(self, seconds):
        frames = []
        t_end = time.time() + seconds
        while time.time() < t_end:
            frames += self.parser.feed(self.ser.read(4096))
            time.sleep(0.002)
        return frames

    def send(self, cmd, wait=0.15):
        """送出命令(字串或 bytes),回傳 wait 秒內收到的所有封包。"""
        self.ser.write(parse_cmd(cmd) if isinstance(cmd, str) else cmd)
        return self.collect(wait)

    def status(self):
        for t, p in self.send("status"):
            if t == TYPE_STATUS and len(p) == 8:
                running, _, rate, rx_crc_err, tx_drop = struct.unpack("<BBHHH", p)
                return dict(running=running, rate=rate, rx_crc_err=rx_crc_err, tx_drop=tx_drop)
        return None

    def configure(self, rate, waves):
        """停止後設定取樣率與兩通道波形。"""
        self.send("stop")
        self.send(f"rate={rate}")
        for ch in (0, 1):
            self.send(wave_cmd(ch, waves[ch]))

    def start_capture(self, seconds):
        """從停止狀態 START,回傳收到的 DATA(seq 應從 0 開始)。"""
        self.flush()
        return data_of(self.send("start", wait=seconds))


class Report:
    def __init__(self):
        self.results = []

    def check(self, name, ok, detail=""):
        self.results.append(ok)
        print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f"  ({detail})" if detail else ""))
        return ok


def wave_cmd(ch, cfg):
    return "wave={},{},{},{},{}".format(ch, *cfg)


def data_of(frames):
    return [struct.unpack("<7H", p) for t, p in frames if t == TYPE_DATA and len(p) == 14]


def replies(frames):
    return [(t, p) for t, p in frames if t != TYPE_DATA]


def has(frames, ptype, payload):
    return any(t == ptype and p == payload for t, p in frames)


def describe(frames):
    return ", ".join(fmt_frame(t, p) for t, p in replies(frames)) or "no response"


def seq_gaps(pkts):
    seqs = [p[0] for p in pkts]
    return sum(1 for a, b in zip(seqs, seqs[1:]) if (b - a) & 0xFFFF != 1)


def expect(link, rep, cmd, ptype, payload, name):
    frames = link.send(cmd)
    return rep.check(name, has(frames, ptype, payload), describe(frames))


def nak(cmd_type, err):
    return TYPE_NAK, bytes([cmd_type, err])


def verify_stream(rep, pkts, rate, waves, name):
    """從 seq = 0、相位 0 起逐點比對兩通道。"""
    if not pkts or pkts[0][0] != 0:
        return rep.check(name, False, f"first seq = {pkts[0][0] if pkts else None}")
    model = [WaveModel(waves[0], rate), WaveModel(waves[1], rate)]
    for i, (seq, da, db, *_) in enumerate(pkts):
        ea, eb = model[0].next(), model[1].next()
        if seq != i or da != ea or db != eb:
            return rep.check(name, False, f"seq {seq}: A {da} (exp {ea}), B {db} (exp {eb})")
    a, b = [p[1] for p in pkts], [p[2] for p in pkts]
    return rep.check(name, True, f"{len(pkts)} pts bit-exact, A {min(a)}~{max(a)}, B {min(b)}~{max(b)}")


def find_switch(pkts, rate, state, new_cfg, ch):
    """找出新設定生效的 seq:之前符合舊設定，之後符合「相位延續」的新設定。"""
    seqs = [p[0] for p in pkts]
    vals = [p[1 + ch] for p in pkts]
    start_phase = phase_at(state, seqs[0], rate)
    old = WaveModel(state[2], rate, start_phase)
    old_vals = [old.next() for _ in vals]
    k = next((i for i, (v, e) in enumerate(zip(vals, old_vals)) if v != e), None)
    if k is None:
        return None, "switch not observed"
    for kk in range(max(0, k - 3), k + 1):          # 新值可能恰好等於舊值，往回找幾個 tick
        if vals[:kk] != old_vals[:kk]:
            continue
        w = WaveModel(state[2], rate, start_phase)
        for _ in range(kk):
            w.next()
        new = WaveModel(new_cfg, rate, w.phase)
        if all(v == new.next() for v in vals[kk:]):
            return seqs[kk], None
    return None, f"mismatch after seq {seqs[k]}"


# --------------------------------------------------------------------------
# 測試群組
# --------------------------------------------------------------------------
def test_cmd(link, rep):
    print("cmd: commands and responses")
    link.configure(100, DEFAULT_WAVES)
    st = link.status()
    rep.check("GET_STATUS when stopped", st is not None and st["running"] == 0 and st["rate"] == 100,
              str(st))

    link.flush()
    frames = link.send("start", wait=1.0)
    pkts = data_of(frames)
    rep.check("START -> ACK, seq from 0", has(frames, TYPE_ACK, bytes([T_START])) and bool(pkts)
              and pkts[0][0] == 0, f"first seq = {pkts[0][0] if pkts else None}")

    link.flush()
    frames = link.send("start", wait=0.5)
    pkts = data_of(frames)
    rep.check("START again -> ACK, seq not reset", has(frames, TYPE_ACK, bytes([T_START])) and bool(pkts)
              and pkts[0][0] > 50, f"first seq = {pkts[0][0] if pkts else None}")

    st = link.status()
    rep.check("GET_STATUS while running", st is not None and st["running"] == 1, str(st))
    expect(link, rep, "rate=50", *nak(T_SET_RATE, ERR_BUSY), "SET_RATE while running -> NAK 0x04")

    frames = link.send("stop", wait=0.3)
    rep.check("STOP -> ACK", has(frames, TYPE_ACK, bytes([T_STOP])), describe(frames))
    link.flush()
    rep.check("no DATA after STOP", not data_of(link.collect(0.3)))
    expect(link, rep, "stop", TYPE_ACK, bytes([T_STOP]), "STOP again -> ACK (idempotent)")

    expect(link, rep, "rate=9", *nak(T_SET_RATE, ERR_RANGE), "SET_RATE 9 -> NAK 0x03")
    expect(link, rep, "rate=201", *nak(T_SET_RATE, ERR_RANGE), "SET_RATE 201 -> NAK 0x03")
    expect(link, rep, "unknown", *nak(0x7F, ERR_UNKNOWN), "unknown TYPE -> NAK 0x01")
    expect(link, rep, "badlen", *nak(T_START, ERR_LEN), "START with payload -> NAK 0x02")
    rep.check("bad LEN does not start sampling", link.status()["running"] == 0)

    before = link.status()["rx_crc_err"]
    frames = link.send("badcrc")
    after = link.status()["rx_crc_err"]
    rep.check("bad CRC -> no response, rx_crc_err +1", not replies(frames) and after == before + 1,
              f"rx_crc_err {before} -> {after}")


def test_rate(link, rep):
    print("rate: sample rate and continuity")
    for rate, seconds in ((50, 3.0), (100, 3.0), (200, 5.0)):
        link.configure(rate, DEFAULT_WAVES)
        link.flush()
        link.send("start", wait=0.5)                 # 略過起始的批次
        pkts = data_of(link.collect(seconds))
        span = (pkts[-1][0] - pkts[0][0]) & 0xFFFF if pkts else 0
        measured = span / seconds
        rep.check(f"{rate} Hz for {seconds:.0f} s", seq_gaps(pkts) == 0 and abs(measured - rate) < rate * 0.05,
                  f"{len(pkts)} pkts, gaps={seq_gaps(pkts)}, ~{measured:.1f} pkt/s")
    link.send("stop")


def test_dds(link, rep):
    print("dds: bit-exact waveform check")
    for rate, seconds in ((100, 2.6), (50, 2.2), (200, 2.2)):
        link.configure(rate, DEFAULT_WAVES)
        verify_stream(rep, link.start_capture(seconds), rate, DEFAULT_WAVES,
                      f"default sine/tri 1 Hz @{rate} Hz")
    cases = [
        ([(SQ, 20, 1000, 2048), (DC, 0, 0, 3000)], "square 2 Hz / DC 3000"),
        ([(TRI, 499, 1947, 2048), (SIN, 499, 1947, 2048)], "boundary 49.9 Hz, amp 1947"),
    ]
    for waves, name in cases:
        link.configure(100, waves)
        verify_stream(rep, link.start_capture(1.5), 100, waves, f"{name} @100 Hz")
    link.send("stop")


def test_switch(link, rep):
    print("switch: SET_WAVE while running, phase-continuous")
    link.configure(100, DEFAULT_WAVES)
    link.start_capture(0.5)
    state = [(0, 0, DEFAULT_WAVES[0]), (0, 0, DEFAULT_WAVES[1])]     # START 時相位歸零
    for ch, new in ((0, (SQ, 20, 1000, 2048)), (1, (DC, 0, 0, 3000)), (0, (TRI, 5, 1947, 2048))):
        old = state[ch][2]
        link.flush()
        frames = link.collect(0.3)
        frames += link.send(wave_cmd(ch, new), wait=0.8)
        k, err = find_switch(data_of(frames), 100, state[ch], new, ch)
        if k is not None:
            state[ch] = (k, phase_at(state[ch], k, 100), new)
        rep.check(f"ch{ch} {old} -> {new}", has(frames, TYPE_ACK, bytes([T_SET_WAVE])) and k is not None,
                  err or f"switched at seq {k}, phase continuous, bit-exact")
    link.send("stop")


def test_param(link, rep):
    print("param: parameter validation")
    link.configure(100, DEFAULT_WAVES)
    ack = (TYPE_ACK, bytes([T_SET_WAVE]))
    cases = [
        ("wave=0,1,500,1500,2048", nak(T_SET_WAVE, ERR_RANGE), "Nyquist: 50.0 Hz @100 Hz"),
        ("wave=0,1,499,1500,2048", ack, "Nyquist boundary 49.9 Hz accepted"),
        ("wave=0,1,10,1949,2048", nak(T_SET_WAVE, ERR_RANGE), "offset - amp = 99"),
        ("wave=0,1,10,1948,2048", nak(T_SET_WAVE, ERR_RANGE), "offset + amp = 3996"),
        ("wave=0,1,10,1947,2048", ack, "amp 1947 accepted (101~3995)"),
        ("wave=0,1,10,1500,1000", nak(T_SET_WAVE, ERR_RANGE), "amp > offset (unsigned underflow)"),
        ("wave=2,1,10,1500,2048", nak(T_SET_WAVE, ERR_RANGE), "ch = 2"),
        ("wave=0,4,10,1500,2048", nak(T_SET_WAVE, ERR_RANGE), "type = 4"),
        ("wave=0,1,0,1500,2048", nak(T_SET_WAVE, ERR_RANGE), "freq_x10 = 0 (non-DC)"),
        ("wave=1,0,0,0,99", nak(T_SET_WAVE, ERR_RANGE), "DC offset 99"),
        ("wave=1,0,0,65535,3995", ack, "DC ignores amp"),
        (build_frame(T_SET_WAVE, b"\x00" * 7), nak(T_SET_WAVE, ERR_LEN), "LEN = 7"),
    ]
    for cmd, (ptype, payload), name in cases:
        expect(link, rep, cmd, ptype, payload, f"SET_WAVE {name}")

    link.configure(100, [(SIN, 400, 1500, 2048), DEFAULT_WAVES[1]])
    expect(link, rep, "rate=50", *nak(T_SET_RATE, ERR_RANGE), "SET_RATE 50 with 40 Hz wave -> NAK 0x03")
    st = link.status()
    rep.check("rate unchanged after NAK", st is not None and st["rate"] == 100, str(st))
    expect(link, rep, "rate=81", TYPE_ACK, bytes([T_SET_RATE]), "SET_RATE 81 accepted (40 < 40.5)")


def test_stress(link, rep):
    print("stress: concurrency stress @200 Hz")

    def hammer(make_cmd, seconds=5.0, interval=0.02):
        link.flush()
        crc0 = link.parser.crc_err
        frames = link.send("start", wait=0.1)
        n_sent = 0
        t0 = nxt = time.time()
        while time.time() - t0 < seconds:
            if time.time() >= nxt:
                link.ser.write(parse_cmd(make_cmd(n_sent)))
                n_sent += 1
                nxt += interval
            frames += link.parser.feed(link.ser.read(4096))
            time.sleep(0.001)
        frames += link.collect(0.3)
        link.send("stop")
        return frames, n_sent, link.parser.crc_err - crc0

    # 主迴圈的回應與 ISR 的 DATA 同時寫入 TX ring buffer(驗證 daq_lock)
    link.configure(200, DEFAULT_WAVES)
    frames, n_sent, crc = hammer(lambda i: "status")
    pkts = data_of(frames)
    n_status = sum(1 for t, _ in frames if t == TYPE_STATUS)
    rep.check("GET_STATUS every 20 ms: all answered", n_status == n_sent, f"{n_status}/{n_sent}")
    rep.check("no seq gap / CRC error", seq_gaps(pkts) == 0 and crc == 0,
              f"{len(pkts)} pkts, gaps={seq_gaps(pkts)}, crc_err+={crc}")

    # SET_WAVE 參數撕裂：若 ISR 讀到一半新一半舊的參數，會出現 3900 + 900 = 4800
    x, y = (DC, 0, 0, 3900), (SIN, 50, 900, 1000)
    link.configure(200, [x, DEFAULT_WAVES[1]])
    frames, n_sent, crc = hammer(lambda i: wave_cmd(0, y if i % 2 else x))
    pkts = data_of(frames)
    a = [p[1] for p in pkts]
    n_ack = sum(1 for t, p in frames if t == TYPE_ACK and p == bytes([T_SET_WAVE]))
    rep.check("SET_WAVE every 20 ms: all ACKed", n_ack == n_sent, f"{n_ack}/{n_sent}")
    rep.check("no seq gap / CRC error", seq_gaps(pkts) == 0 and crc == 0,
              f"{len(pkts)} pkts, gaps={seq_gaps(pkts)}, crc_err+={crc}")
    rep.check("no torn update (dacA within 100~3995)", bool(a) and min(a) >= 100 and max(a) <= 3995,
              f"range {min(a) if a else '-'}~{max(a) if a else '-'}")


TESTS = {"cmd": test_cmd, "rate": test_rate, "dds": test_dds,
         "switch": test_switch, "param": test_param, "stress": test_stress}


def main():
    ap = argparse.ArgumentParser(description="DaqLink firmware regression test (needs hardware)")
    ap.add_argument("port", help="COM port, e.g. COM15")
    ap.add_argument("--only", nargs="+", choices=GROUPS, help="run only these groups")
    ap.add_argument("--skip", nargs="+", choices=GROUPS, default=[], help="skip these groups")
    args = ap.parse_args()

    groups = [g for g in (args.only or GROUPS) if g not in args.skip]
    link = Link(args.port)
    rep = Report()
    t0 = time.time()
    try:
        for g in groups:
            TESTS[g](link, rep)
    finally:
        link.configure(100, DEFAULT_WAVES)          # 恢復開機預設狀態
        st = link.status()
        link.close()

    n_pass = sum(rep.results)
    print(f"\nfinal state: {st}")
    print(f"SUMMARY: {n_pass}/{len(rep.results)} passed ({time.time() - t0:.0f} s)")
    sys.exit(0 if n_pass == len(rep.results) else 1)


if __name__ == "__main__":
    main()
