#include "p32xxxx.h"
#include "uart1.h"

#define UART1_BRG_115200    173

void uart1_init(void)
{
    U1MODE = 0;                         /* 先關閉,並清成 8N1、無流量控制 */
    U1STA  = 0;
    U1MODEbits.BRGH = 1;                /* 4x clock,誤差較小 */
    U1BRG = UART1_BRG_115200;

    U1STAbits.URXEN = 1;
    U1MODEbits.ON   = 1;
    U1STAbits.UTXEN = 1;                /* 必須在 ON 之後才設 */
}

void uart1_putc(char c)
{
    while (U1STAbits.UTXBF);            /* TX FIFO(4 層)滿了就等 */
    U1TXREG = c;
}

void uart1_puts(const char *s)
{
    while (*s)
        uart1_putc(*s++);
}

int uart1_getc(void)
{
    if(U1STAbits.OERR)                  /* RX 溢位:不清掉的話 UART 會停止接收 */
        U1STAbits.OERR = 0;             /* 清除時 FIFO 內容也會一併丟棄 */
    
    if(!U1STAbits.URXDA)                /* 判斷 RX 沒有資料 */
        return -1;
    
    return (int)(U1RXREG & 0xFF);
}

void uart1_put_uint(uint32_t v)
{
    char buf[10];                           /* uint32 最大 4294967295,共 10 位 */
    int i = 0;                              /* C89:變數宣告放在區塊開頭 */
    
    do                                      /* do-while:v = 0 時也會輸出 "0" */
    {
        buf[i++] = (char)('0' + v % 10);    /* 從個位數開始，倒序存入 */
        v /= 10;
    }while(v);
    
    while(i > 0)                            /* 從尾端往回送，順序就正確了 */
    {
        uart1_putc(buf[--i]);
    }
}

int uart1_tx_ready(void)
{
    return !U1STAbits.UTXBF;
}