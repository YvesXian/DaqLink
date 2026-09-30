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
#include "uart1.h"
#include "crc16.h"
#include "packet.h"
#include "daq.h"
#include "cmd.h"

#define SYS_FREQ        80000000UL
#define CT_TICKS_PER_MS (SYS_FREQ / 2 / 1000)   /* Core Timer = SYSCLK/2 */
#define LD1             LATEbits.LATE3
#define LD2             LATAbits.LATA10

static void delay_ms(uint32_t ms)
{
    uint32_t start = _CP0_GET_COUNT();
    while ((_CP0_GET_COUNT() - start) < ms * CT_TICKS_PER_MS);
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
    static const uint8_t crc_vec[] = {          /* 協定第 8 節 DATA 範例的 LEN~payload */
        0x0E, 0x01, 0x01, 0x00, 0x00, 0x08, 0x00, 0x04,
        0xFF, 0x07, 0x01, 0x04, 0x00, 0x00, 0xFF, 0x0F
    };
    uint32_t last;
    
    mcp4922_init();     /* 先把兩顆的 CS 拉高 */
    mcp3304_init();
    spi2_init();        /* 再啟動 SPI 匯流排 */
    led_init();
    uart1_init();
    
    led_initial_blinking(200);
    
    uart1_puts("\r\nDaqLink UART1 ready\r\n");

    uart1_puts("CRC check  = ");
    uart1_put_uint(crc16_calc((const uint8_t *)"123456789", 9));
    uart1_puts("  (expect 10673)\r\n");

    uart1_puts("CRC packet = ");
    uart1_put_uint(crc16_calc(crc_vec, sizeof(crc_vec)));
    uart1_puts("  (expect 58229)\r\n");
    
    /* ---- 啟用中斷，等待 PC 下 START:之後主迴圈不可碰 SPI,輸出只能走 packet_send ---- */
    daq_init();
    INTCONSET = _INTCON_MVEC_MASK;      /* multi-vector 模式 */
    asm volatile("ei");                 /* 開啟全域中斷 */
    
    last = _CP0_GET_COUNT();
    
    while (1)
    {
        if ((_CP0_GET_COUNT() - last) >= 1000 * CT_TICKS_PER_MS)
        {
            last += 1000 * CT_TICKS_PER_MS;
            LD1 = !LD1;
        }

        cmd_poll();
        packet_tx_pump();
    }
}
