#include "wave.h"

typedef struct
{
    uint8_t  type;
    uint16_t freq_x10;
    uint16_t amp;
    uint16_t offset;
    uint32_t phase;
    uint32_t inc;
} wave_t;

static wave_t waves[2];

static const int16_t sine_tab[256] = {      /* Q15,一個週期 256 點 */
         0,    804,   1608,   2410,   3212,   4011,   4808,   5602,
      6393,   7179,   7962,   8739,   9512,  10278,  11039,  11793,
     12539,  13279,  14010,  14732,  15446,  16151,  16846,  17530,
     18204,  18868,  19519,  20159,  20787,  21403,  22005,  22594,
     23170,  23731,  24279,  24811,  25329,  25832,  26319,  26790,
     27245,  27683,  28105,  28510,  28898,  29268,  29621,  29956,
     30273,  30571,  30852,  31113,  31356,  31580,  31785,  31971,
     32137,  32285,  32412,  32521,  32609,  32678,  32728,  32757,
     32767,  32757,  32728,  32678,  32609,  32521,  32412,  32285,
     32137,  31971,  31785,  31580,  31356,  31113,  30852,  30571,
     30273,  29956,  29621,  29268,  28898,  28510,  28105,  27683,
     27245,  26790,  26319,  25832,  25329,  24811,  24279,  23731,
     23170,  22594,  22005,  21403,  20787,  20159,  19519,  18868,
     18204,  17530,  16846,  16151,  15446,  14732,  14010,  13279,
     12539,  11793,  11039,  10278,   9512,   8739,   7962,   7179,
      6393,   5602,   4808,   4011,   3212,   2410,   1608,    804,
         0,   -804,  -1608,  -2410,  -3212,  -4011,  -4808,  -5602,
     -6393,  -7179,  -7962,  -8739,  -9512, -10278, -11039, -11793,
    -12539, -13279, -14010, -14732, -15446, -16151, -16846, -17530,
    -18204, -18868, -19519, -20159, -20787, -21403, -22005, -22594,
    -23170, -23731, -24279, -24811, -25329, -25832, -26319, -26790,
    -27245, -27683, -28105, -28510, -28898, -29268, -29621, -29956,
    -30273, -30571, -30852, -31113, -31356, -31580, -31785, -31971,
    -32137, -32285, -32412, -32521, -32609, -32678, -32728, -32757,
    -32767, -32757, -32728, -32678, -32609, -32521, -32412, -32285,
    -32137, -31971, -31785, -31580, -31356, -31113, -30852, -30571,
    -30273, -29956, -29621, -29268, -28898, -28510, -28105, -27683,
    -27245, -26790, -26319, -25832, -25329, -24811, -24279, -23731,
    -23170, -22594, -22005, -21403, -20787, -20159, -19519, -18868,
    -18204, -17530, -16846, -16151, -15446, -14732, -14010, -13279,
    -12539, -11793, -11039, -10278,  -9512,  -8739,  -7962,  -7179,
     -6393,  -5602,  -4808,  -4011,  -3212,  -2410,  -1608,   -804
};

/* inc = freq_x10 × 2^32 / (10 × rate);乘上 2^32 會超過 32-bit,所以用 64-bit 計算 */
static uint32_t calc_inc(uint16_t freq_x10, uint16_t rate_hz)
{
    return (uint32_t)(((uint64_t)freq_x10 << 32) / (10u * rate_hz));
}

int wave_params_ok(uint8_t ch, uint8_t type, uint16_t freq_x10,
                   uint16_t amp, uint16_t offset, uint16_t rate_hz)
{
    if (ch > WAVE_CH_B || type > WAVE_SQUARE)
        return 0;
    if (type == WAVE_DC)                    /* DC 忽略 amp */
        amp = 0;
    if ((uint32_t)offset < (uint32_t)amp + WAVE_CODE_MIN)   /* offset - amp >= 100 */
        return 0;
    if ((uint32_t)offset + amp > WAVE_CODE_MAX)
        return 0;
    if (type != WAVE_DC && (freq_x10 == 0 || freq_x10 >= 5u * rate_hz))  /* Nyquist */
        return 0;
    return 1;
}

/* 呼叫前須先通過 wave_params_ok;採樣中呼叫要由呼叫端包 daq_lock */
void wave_set(uint8_t ch, uint8_t type, uint16_t freq_x10,
              uint16_t amp, uint16_t offset, uint16_t rate_hz)
{
    wave_t *w = &waves[ch];

    w->type     = type;
    w->freq_x10 = freq_x10;
    w->amp      = (type == WAVE_DC) ? 0 : amp;
    w->offset   = offset;
    w->inc      = (type == WAVE_DC) ? 0 : calc_inc(freq_x10, rate_hz);
    /* 不重設 phase:相位連續，切換時輸出不跳變 */
}

int wave_rate_ok(uint16_t rate_hz)
{
    uint8_t ch;

    for (ch = 0; ch < 2; ch++)
        if (waves[ch].type != WAVE_DC && waves[ch].freq_x10 >= 5u * rate_hz)
            return 0;
    return 1;
}

void wave_set_rate(uint16_t rate_hz)
{
    uint8_t ch;

    for (ch = 0; ch < 2; ch++)
        if (waves[ch].type != WAVE_DC)
            waves[ch].inc = calc_inc(waves[ch].freq_x10, rate_hz);
}

void wave_reset_phase(void)
{
    waves[WAVE_CH_A].phase = 0;
    waves[WAVE_CH_B].phase = 0;
}

void wave_init(uint16_t rate_hz)
{
    wave_set(WAVE_CH_A, WAVE_SINE, 10, 1500, 2048, rate_hz);   /* 協定 5.6 預設值 */
    wave_set(WAVE_CH_B, WAVE_TRI,  10, 1500, 2048, rate_hz);
    wave_reset_phase();
}

/* ISR 每個 tick 呼叫一次：回傳這個 tick 的 code,然後相位前進 */
uint16_t wave_next(uint8_t ch)
{
    wave_t  *w = &waves[ch];
    int32_t  s;
    uint16_t p;

    switch (w->type)
    {
    case WAVE_SINE:
        s = sine_tab[w->phase >> 24];                   /* 高 8 bit 當索引 */
        break;
    case WAVE_TRI:
        p = (uint16_t)(w->phase >> 16);
        s = (p < 0x8000) ? 2 * (int32_t)p - 32768       /* 前半週期：-1 → +1 */
                         : 32767 - 2 * ((int32_t)p - 32768);   /* 後半週期:+1 → -1 */
        break;
    case WAVE_SQUARE:
        s = (w->phase & 0x80000000u) ? -32767 : 32767;
        break;
    default:                                            /* DC */
        s = 0;
        break;
    }

    w->phase += w->inc;                                 /* 溢位自動繞回 = mod 2π */
    return (uint16_t)(w->offset + ((w->amp * s) >> 15));
}
