#include "txbuf.h"

#define TXBUF_MASK  (TXBUF_SIZE - 1)

static uint8_t buf[TXBUF_SIZE];
static volatile uint16_t head;
static volatile uint16_t tail;

uint16_t txbuf_free(void)
{
    return (uint16_t)(TXBUF_SIZE - (uint16_t)(head - tail));
}

/* 呼叫前必須先用 txbuf_free() 確認空間足夠 */
void txbuf_write(const uint8_t *src, uint16_t len)
{
    uint16_t h = head;

    while (len--)
        buf[h++ & TXBUF_MASK] = *src++;
    head = h;                           /* 最後才更新:消費者不會看到半包 */
}

/* 取出 1 byte;buffer 為空時回傳 0 */
int txbuf_get(uint8_t *b)
{
    uint16_t t = tail;

    if (t == head)
        return 0;
    *b = buf[t & TXBUF_MASK];
    tail = t + 1;
    return 1;
}