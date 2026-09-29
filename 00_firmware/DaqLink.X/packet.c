#include "packet.h"
#include "txbuf.h"
#include "crc16.h"
#include "uart1.h"

static volatile uint16_t tx_drop;       /* P-3 起會在 ISR 中遞增 */

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
