#include "p32xxxx.h"
#include <sys/attribs.h>
#include "daq.h"
#include "mcp4922.h"
#include "mcp3304.h"
#include "packet.h"
#include "wave.h"

#define PBCLK_HZ    80000000UL
#define T2_TICK_HZ  (PBCLK_HZ / 256)        /* 1:256 → 312500 Hz */

static volatile uint16_t seq;
static volatile uint8_t running;
static uint16_t rate = DAQ_DEFAULT_RATE;

void daq_init(void)
{
    T2CON = 0;                              /* 先停下 timer 再設定 */
    T2CONbits.TCKPS = 7;                    /* 1:256 */
    TMR2 = 0;
    PR2  = (uint16_t)(T2_TICK_HZ / rate - 1);
    running = 0;

    IPC2bits.T2IP = 3;                      /* 必須和 ISR 的 ipl3 一致 */
    IPC2bits.T2IS = 0;
    IFS0CLR = _IFS0_T2IF_MASK;
    IEC0CLR = _IEC0_T2IE_MASK;              /* 等 daq_start 才打開 */
    
    wave_init(rate);
}

void daq_start(void)
{
    if (running)
        return;
    seq  = 0;
    wave_reset_phase();
    TMR2 = 0;
    IFS0CLR = _IFS0_T2IF_MASK;
    IEC0SET = _IEC0_T2IE_MASK;
    T2CONSET = _T2CON_ON_MASK;
    running = 1;
}

void daq_stop(void)
{
    T2CONCLR = _T2CON_ON_MASK;
    IEC0CLR  = _IEC0_T2IE_MASK;
    running = 0;
}

void daq_set_rate(uint16_t hz)
{
    wave_set_rate(hz);
    
    rate = hz;
    PR2  = (uint16_t)(T2_TICK_HZ / hz - 1);
}

uint16_t daq_get_rate(void)   { return rate; }
int      daq_is_running(void) { return running; }

/* 主迴圈寫 TX buffer 前後呼叫，避免和 ISR 同時當生產者 */
uint32_t daq_lock(void)
{
    uint32_t saved = IEC0 & _IEC0_T2IE_MASK;
    IEC0CLR = _IEC0_T2IE_MASK;
    return saved;
}

void daq_unlock(uint32_t saved)
{
    if (saved)
        IEC0SET = _IEC0_T2IE_MASK;
}

void __ISR(_TIMER_2_VECTOR, ipl3) daq_isr(void)
{
    uint8_t  pl[14];
    uint8_t  i;
    uint16_t dac_a, dac_b;

    IFS0CLR = _IFS0_T2IF_MASK;

    dac_a = wave_next(WAVE_CH_A);
    dac_b = wave_next(WAVE_CH_B);
    mcp4922_write(MCP4922_CH_A, dac_a);
    mcp4922_write(MCP4922_CH_B, dac_b);

    put_u16_le(&pl[0], seq);
    put_u16_le(&pl[2], dac_a);
    put_u16_le(&pl[4], dac_b);
    for (i = 0; i < 4; i++)
        put_u16_le(&pl[6 + 2 * i], mcp3304_read(i));

    packet_send(PKT_TYPE_DATA, pl, sizeof(pl));  /* buffer 滿就丟包,tx_drop++ */
    seq++;                                       /* 丟包也要遞增,PC 端才看得出掉了幾包 */
}