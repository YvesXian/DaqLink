#include "p32xxxx.h"
#include <sys/attribs.h>
#include "daq.h"
#include "mcp4922.h"
#include "mcp3304.h"
#include "packet.h"

#define PBCLK_HZ    80000000UL
#define T2_TICK_HZ  (PBCLK_HZ / 256)        /* 1:256 → 312500 Hz */

static volatile uint16_t seq;
static uint16_t dac_a = 1024;               /* P-5 之後改成 DDS 查表 */
static uint16_t dac_b = 3072;

static void put_u16(uint8_t *p, uint16_t v)    /* little-endian */
{
    p[0] = (uint8_t)(v & 0xFF);
    p[1] = (uint8_t)(v >> 8);
}

void daq_init(void)
{
    T2CON = 0;                              /* 先停下 timer 再設定 */
    T2CONbits.TCKPS = 7;                    /* 1:256 */
    TMR2 = 0;
    PR2  = (uint16_t)(T2_TICK_HZ / DAQ_DEFAULT_RATE - 1);   /* 3124 */

    IPC2bits.T2IP = 3;                      /* 必須和 ISR 的 ipl3 一致 */
    IPC2bits.T2IS = 0;
    IFS0CLR = _IFS0_T2IF_MASK;
    IEC0CLR = _IEC0_T2IE_MASK;              /* 等 daq_start 才打開 */
}

void daq_start(void)
{
    seq  = 0;
    TMR2 = 0;
    IFS0CLR = _IFS0_T2IF_MASK;
    IEC0SET = _IEC0_T2IE_MASK;
    T2CONSET = _T2CON_ON_MASK;
}

void daq_stop(void)
{
    T2CONCLR = _T2CON_ON_MASK;
    IEC0CLR  = _IEC0_T2IE_MASK;
}

void __ISR(_TIMER_2_VECTOR, ipl3) daq_isr(void)
{
    uint8_t pl[14];
    uint8_t i;

    IFS0CLR = _IFS0_T2IF_MASK;

    mcp4922_write(MCP4922_CH_A, dac_a);
    mcp4922_write(MCP4922_CH_B, dac_b);

    put_u16(&pl[0], seq);
    put_u16(&pl[2], dac_a);
    put_u16(&pl[4], dac_b);
    for (i = 0; i < 4; i++)
        put_u16(&pl[6 + 2 * i], mcp3304_read(i));

    packet_send(PKT_TYPE_DATA, pl, sizeof(pl));  /* buffer 滿就丟包,tx_drop++ */
    seq++;                                       /* 丟包也要遞增,PC 端才看得出掉了幾包 */
}