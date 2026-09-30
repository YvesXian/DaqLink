#include "packet.h"
#include "txbuf.h"
#include "crc16.h"
#include "uart1.h"

enum { RX_SOF1, RX_SOF2, RX_LEN, RX_TYPE, RX_PAYLOAD, RX_CRC_L, RX_CRC_H };

static volatile uint16_t tx_drop;       /* P-3 起會在 ISR 中遞增 */
static uint8_t  rx_state = RX_SOF1;
static uint8_t  rx_idx;
static uint8_t  rx_crc_l;
static uint16_t rx_crc;                 /* 邊收邊算，不用等整包收完 */
static uint16_t rx_crc_err;

/* 組成完整封包後一次寫入 ring buffer;空間不足則整包丟棄，回傳 -1 */
int packet_send(uint8_t type, const uint8_t *payload, uint8_t len)
{
    uint8_t  frame[PKT_MAX_PAYLOAD + PKT_OVERHEAD];
    uint16_t crc;
    uint8_t  i;

    if (len > PKT_MAX_PAYLOAD)
        return -1;

    frame[0] = 0xAA;
    frame[1] = 0x55;
    frame[2] = len;
    frame[3] = type;
    for (i = 0; i < len; i++)
        frame[4 + i] = payload[i];

    crc = crc16_calc(&frame[2], (uint16_t)len + 2);     /* LEN ~ payload */
    frame[4 + len] = (uint8_t)(crc & 0xFF);             /* little-endian */
    frame[5 + len] = (uint8_t)(crc >> 8);

    if (txbuf_free() < (uint16_t)len + PKT_OVERHEAD)
    {
        tx_drop++;
        return -1;
    }
    txbuf_write(frame, (uint16_t)len + PKT_OVERHEAD);
    return 0;
}

/* 主迴圈每一輪呼叫：把 ring buffer 的資料搬到 UART HW FIFO,不阻塞 */
void packet_tx_pump(void)
{
    uint8_t b;

    while (uart1_tx_ready() && txbuf_get(&b))
        uart1_putc((char)b);
}

uint16_t packet_tx_drop(void)
{
    return tx_drop;
}

int packet_rx_feed(uint8_t b, packet_t *pkt)
{
    switch (rx_state)
    {
    case RX_SOF1:
        if (b == 0xAA)
            rx_state = RX_SOF2;
        break;

    case RX_SOF2:
        if (b == 0x55)
            rx_state = RX_LEN;
        else if (b != 0xAA)             /* "AA AA 55" 時，第二個 AA 仍可能是 SOF */
            rx_state = RX_SOF1;
        break;

    case RX_LEN:
        if (b > PKT_MAX_PAYLOAD)        /* 不可能是合法命令，也放不進 payload[] */
        {
            rx_crc_err++;
            rx_state = RX_SOF1;
            break;
        }
        pkt->len = b;
        rx_crc   = crc16_update(CRC16_INIT, b);
        rx_state = RX_TYPE;
        break;
        
    case RX_TYPE:
        pkt->type = b;
        rx_crc    = crc16_update(rx_crc, b);
        rx_idx    = 0;
        rx_state  = (pkt->len == 0) ? RX_CRC_L : RX_PAYLOAD;
        break;
    case RX_PAYLOAD:
        pkt->payload[rx_idx++] = b;
        rx_crc = crc16_update(rx_crc, b);
        if (rx_idx == pkt->len)
            rx_state = RX_CRC_L;
        break;
    case RX_CRC_L:
        rx_crc_l = b;
        rx_state = RX_CRC_H;
        break;
    case RX_CRC_H:
        rx_state = RX_SOF1;
        if ((uint16_t)(rx_crc_l | ((uint16_t)b << 8)) == rx_crc)
            return 1;
        rx_crc_err++;                   /* 協定第 7 節:CRC 錯誤不回應，等 PC 重送 */
        break;
    }
    return 0;
}

uint16_t packet_rx_crc_err(void)
{
    return rx_crc_err;
}

void put_u16_le(uint8_t *p, uint16_t v)
{
    p[0] = (uint8_t)(v & 0xFF);
    p[1] = (uint8_t)(v >> 8);
}