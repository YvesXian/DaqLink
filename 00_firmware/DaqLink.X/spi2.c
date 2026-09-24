#include "p32xxxx.h"
#include "spi2.h"

/* F_SCK = PBCLK / (2 * (BRG + 1)) = 80 MHz / 80 = 1 MHz (RM §23 Eq 23-1) */
#define SPI2_BRG_1MHZ   39

void spi2_init(void)
{
    IEC1CLR = _IEC1_SPI2EIE_MASK | _IEC1_SPI2TXIE_MASK | _IEC1_SPI2RXIE_MASK;  /* 輪詢模式,不用中斷 */

    SPI2CON = 0;                    /* 先關閉模組,CKP/CKE 只能在 OFF 時修改 */
    (void)SPI2BUF;                  /* 清空接收緩衝區 */
    SPI2BRG = SPI2_BRG_1MHZ;
    SPI2STATbits.SPIROV = 0;

    SPI2CONbits.MSTEN = 1;          /* 主機模式 */
    SPI2CONbits.CKP   = 0;          /* Mode 0:clock idle low */
    SPI2CONbits.CKE   = 1;          /* Mode 0:CKE 與 CPHA 相反,要設 1 */
    SPI2CONbits.SMP   = 0;          /* 在資料中段取樣 */
                                    /* MODE16 = MODE32 = 0 → 8-bit,SPI2CON = 0 時已清掉 */
    SPI2CONbits.ON = 1;
}

uint8_t spi2_xfer(uint8_t tx)
{
    SPI2BUF = tx;
    /* wait the reciever completed */
    while(!SPI2STATbits.SPIRBF);
    return (uint8_t)SPI2BUF;
}
