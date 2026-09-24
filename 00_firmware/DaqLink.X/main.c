/*
 * File:   main.c
 * Author: user
 *
 * Created on 2026年9月23日, 下午 9:54
 */

#include "p32xxxx.h"
#include <sys/attribs.h>
#include "spi2.h"
#include "mcp4922.h"
#include "mcp3304.h"

#define SYS_FREQ        80000000UL
#define CT_TICKS_PER_MS (SYS_FREQ / 2 / 1000)   /* Core Timer = SYSCLK/2 */
#define PHASE_MS        8000            /* 每個階段 8 秒,電表才來得及穩定 */
#define LD1             LATEbits.LATE3
#define LD2             LATAbits.LATA10

static void delay_ms(uint32_t ms)
{
    uint32_t start = _CP0_GET_COUNT();
    while ((_CP0_GET_COUNT() - start) < ms * CT_TICKS_PER_MS);
}

/* 在指定時間內一直送同一個 byte */
static void spi_burst(uint8_t tx, uint32_t ms)
{
    uint32_t start = _CP0_GET_COUNT();
    while ((_CP0_GET_COUNT() - start) < ms * CT_TICKS_PER_MS)
    spi2_xfer(tx);
}

static void led_init(void)
{
    LD1 = 0;
    LD2 = 0;
    TRISEbits.TRISE3  = 0;
    TRISAbits.TRISA10 = 0;
}

static void led_initial_blinking(uint32_t ms)
{
    uint8_t i;
    
    for (i = 0; i < 3; i++)
    {
        LD1 = 1; LD2 = 1;
        delay_ms(ms);
        LD1 = 0; LD2 = 0;
        delay_ms(ms);
    }
}

int main(void)
{
    mcp4922_init();     /* 先把兩顆的 CS 拉高 */
    mcp3304_init();
    spi2_init();        /* 再啟動 SPI 匯流排 */
    led_init();
    
    led_initial_blinking(200);
    
    mcp4922_write(MCP4922_CH_A, 1024);
    mcp4922_write(MCP4922_CH_B, 3072);

    while (1)
    {
        LD1 = !LD1;
        delay_ms(500);
        
//         /* 階段 A:連續送 0xFF → SCK ≈ 1.3–1.6V、MOSI ≈ 3.3V */
//        LD1 = 1; LD2 = 0;
//        spi_burst(0xFF, PHASE_MS);
//
//        /* 階段 B:連續送 0x00 → SCK ≈ 1.3–1.6V、MOSI ≈ 0V */
//        LD1 = 0; LD2 = 1;
//        spi_burst(0x00, PHASE_MS);
//
//        /* 階段 C:停止傳送 → SCK = 0V */
//        LD1 = 0; LD2 = 0;
//        delay_ms(PHASE_MS);
    }
}
