"""DaqLink 封包監看工具(開發除錯用)

依 docs/protocol.md 解析 MCU 送出的封包、驗證 CRC、檢查 seq 連續性，
每秒印出一行統計。

用法:
    pip install pyserial
    python daq_monitor.py COM5              # 監看(115200 8N1)
    python daq_monitor.py COM5 --raw        # 另外逐包印出內容
    python daq_monitor.py COM5 --send start # 送出命令後再開始監看
    python daq_monitor.py COM5 --send stop --send rate=50 --send start
                                            # 依序送出多個命令(間隔 100 ms)
    python daq_monitor.py --selftest        # 不需硬體，用協定第 8 節的測試向量驗證解析器

--send 可用的命令:
    start / stop / status / rate=N          正常命令
    wave=ch,type,freq_x10,amp,offset        SET_WAVE,例如 wave=0,1,10,1500,2048
                                            (ch 0=A 1=B;type 0=DC 1=sin 2=tri 3=square)
    unknown                                 TYPE=0x7F,預期 NAK err=0x01
    badlen                                  START 帶 1 byte payload,預期 NAK err=0x02
    badcrc                                  START 但 CRC 錯誤，預期無回應、rx_crc_err +1
"""

import argparse
import struct
import sys
import time

SOF = b"\xAA\x55"

TYPE_DATA, TYPE_ACK, TYPE_NAK, TYPE_STATUS = 0x01, 0x80, 0x81, 0x82
CMD = {
    "start": (0x10, b""),
    "stop": (0x11, b""),
    "status": (0x14, b""),
    "unknown": (0x7F, b""),
    "badlen": (0x10, b"\x00"),
}


def crc16_ccitt_false(data):
    crc = 0xFFFF
    for b in data:
        crc ^= b << 8
        for _ in range(8):
            crc = ((crc << 1) ^ 0x1021) & 0xFFFF if crc & 0x8000 else (crc << 1) & 0xFFFF
    return crc


def build_frame(ptype, payload=b""):
    body = bytes([len(payload), ptype]) + payload
    return SOF + body + struct.pack("<H", crc16_ccitt_false(body))


def parse_cmd(text):
    """把 --send 的字串轉成要送出的 bytes。"""
    if text == "badcrc":
        frame = bytearray(build_frame(0x10))
        frame[-1] ^= 0xFF
        return bytes(frame)
    if text.startswith("rate="):
        return build_frame(0x12, struct.pack("<H", int(text[5:])))
    if text.startswith("wave="):
        f = [int(x) for x in text[5:].split(",")]
        if len(f) != 5:
            raise argparse.ArgumentTypeError("wave=ch,type,freq_x10,amp,offset")
        return build_frame(0x13, struct.pack("<BBHHH", *f))
    if text in CMD:
        return build_frame(*CMD[text])
    raise argparse.ArgumentTypeError(f"unknown command: {text}")


class Parser:
    """位元組串流解析器;CRC 錯誤時從 SOF 的下一個 byte 重新同步(協定 2.2)。"""

    def __init__(self):
        self.buf = bytearray()
        self.crc_err = 0

    def feed(self, data):
        self.buf += data
        frames = []
        while True:
            i = self.buf.find(SOF)
            if i < 0:
                # 保留最後一個 byte,它可能是下一個 SOF 的 0xAA
                del self.buf[:max(0, len(self.buf) - 1)]
                return frames
            del self.buf[:i]
            if len(self.buf) < 4:
                return frames
            n = self.buf[2] + 6
            if len(self.buf) < n:
                return frames
            body = bytes(self.buf[2:n - 2])
            (crc,) = struct.unpack_from("<H", self.buf, n - 2)
            if crc16_ccitt_false(body) == crc:
                frames.append((body[1], body[2:]))
                del self.buf[:n]
            else:
                self.crc_err += 1
                del self.buf[:1]


def fmt_frame(ptype, p):
    if ptype == TYPE_DATA and len(p) == 14:
        seq, da, db, a0, a1, a2, a3 = struct.unpack("<7H", p)
        return (f"DATA seq={seq:5d} dacA={da:4d} dacB={db:4d} "
                f"adc={a0:4d} {a1:4d} {a2:4d} {a3:4d}")
    if ptype == TYPE_ACK and len(p) == 1:
        return f"ACK  cmd=0x{p[0]:02X}"
    if ptype == TYPE_NAK and len(p) == 2:
        return f"NAK  cmd=0x{p[0]:02X} err=0x{p[1]:02X}"
    if ptype == TYPE_STATUS and len(p) == 8:
        run, _, rate, crc_err, drop = struct.unpack("<BBHHH", p)
        return f"STATUS running={run} rate={rate} rx_crc_err={crc_err} tx_drop={drop}"
    return f"TYPE=0x{ptype:02X} payload={p.hex(' ').upper()}"


def selftest():
    vectors = [
        ("START", build_frame(0x10), "AA 55 00 10 3E 0F"),
        ("SET_RATE 100", build_frame(0x12, struct.pack("<H", 100)), "AA 55 02 12 64 00 45 83"),
        ("SET_WAVE", parse_cmd("wave=0,1,10,1500,2048"), "AA 55 08 13 00 01 0A 00 DC 05 00 08 62 66"),
        ("ACK(START)", build_frame(0x80, b"\x10"), "AA 55 01 80 10 05 F2"),
        ("DATA", build_frame(0x01, struct.pack("<7H", 1, 2048, 1024, 2047, 1025, 0, 4095)),
         "AA 55 0E 01 01 00 00 08 00 04 FF 07 01 04 00 00 FF 0F 75 E3"),
    ]
    ok = crc16_ccitt_false(b"123456789") == 0x29B1
    print(f"CRC check 0x29B1: {'OK' if ok else 'FAIL'}")
    for name, got, expect in vectors:
        good = got.hex(" ").upper() == expect
        ok &= good
        print(f"{name:14s}: {'OK' if good else 'FAIL ' + got.hex(' ').upper()}")

    # 雜訊 + 一包壞 CRC + 兩包正確 DATA,驗證重新同步
    p = Parser()
    good = build_frame(0x01, struct.pack("<7H", 7, 0, 0, 0, 0, 0, 4095))
    bad = bytearray(good)
    bad[-1] ^= 0xFF
    frames = p.feed(b"\x00\xAA\x13" + bytes(bad) + good + good[:5])
    frames += p.feed(good[5:])
    resync = len(frames) == 2 and p.crc_err == 1
    ok &= resync
    print(f"Resync        : {'OK' if resync else 'FAIL'} (frames={len(frames)}, crc_err={p.crc_err})")
    return 0 if ok else 1


def monitor(port, raw, send):
    import serial  # 延後匯入,--selftest 不需要 pyserial

    ser = serial.Serial(port, 115200, timeout=0.05)
    for text in send or []:
        frame = parse_cmd(text)
        ser.write(frame)
        print(f"sent {text:8s}: {frame.hex(' ').upper()}")
        time.sleep(0.1)

    p = Parser()
    last_seq = None
    total = lost = 0
    n_sec = 0
    last_data = None
    t0 = time.time()
    try:
        while True:
            for ptype, payload in p.feed(ser.read(256)):
                if raw or ptype != TYPE_DATA:
                    print(fmt_frame(ptype, payload))
                if ptype == TYPE_ACK and payload == b"\x10":
                    last_seq = None     # START 可能把 seq 歸零，視為新的時間軸，不算遺失
                if ptype == TYPE_DATA and len(payload) == 14:
                    seq = struct.unpack_from("<H", payload)[0]
                    if last_seq is not None:
                        diff = (seq - last_seq) & 0xFFFF
                        if diff != 1:
                            lost += (diff - 1) & 0xFFFF
                            print(f"!! seq gap: {last_seq} -> {seq}")
                    last_seq = seq
                    total += 1
                    n_sec += 1
                    last_data = payload
            if time.time() - t0 >= 1.0:
                t0 += 1.0
                line = f"[{n_sec:3d} pkt/s] total={total} lost={lost} crc_err={p.crc_err}"
                if last_data:
                    line += "  last: " + fmt_frame(TYPE_DATA, last_data)[5:]
                print(line)
                n_sec = 0
    except KeyboardInterrupt:
        pass
    finally:
        ser.close()


def main():
    ap = argparse.ArgumentParser(description="DaqLink packet monitor")
    ap.add_argument("port", nargs="?", help="COM port, e.g. COM5")
    ap.add_argument("--raw", action="store_true", help="print every DATA packet")
    ap.add_argument("--send", action="append", metavar="CMD",
                    help="send a command before monitoring (repeatable): "
                         "start/stop/status/rate=N/wave=ch,type,f10,amp,off/"
                         "unknown/badlen/badcrc")
    ap.add_argument("--selftest", action="store_true", help="verify parser with protocol test vectors")
    args = ap.parse_args()

    if args.selftest:
        sys.exit(selftest())
    if not args.port:
        ap.error("port is required (or use --selftest)")
    for text in args.send or []:
        try:
            parse_cmd(text)
        except (argparse.ArgumentTypeError, ValueError) as e:
            ap.error(str(e))
    monitor(args.port, args.raw, args.send)


if __name__ == "__main__":
    main()
