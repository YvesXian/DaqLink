#include "p32xxxx.h"
#include "mcp3304.h"
#include "spi2.h"

/* ADC CS = RA7 (active low) */
#define ADC_CS_MASK     (1u << 7)
#define ADC_CS_HIGH()   (LATASET = ADC_CS_MASK)
#define ADC_CS_LOW()    (LATACLR = ADC_CS_MASK)

void mcp3304_init(void)
{
    ADC_CS_HIGH();
    TRISACLR = ADC_CS_MASK;
}

uint16_t mcp3304_read(uint8_t ch)
{
    uint8_t b2, b3;
    
    ADC_CS_LOW();
    spi2_xfer(0x0C | ((ch >> 1) & 0x03));       /* 0000 1 SGL D2 D1 */
    b2 = spi2_xfer((uint8_t)((ch & 0x01) << 7)); /* D0 xxxxxxx → 收到 ? ? 0 SB B11..B8 */
    b3 = spi2_xfer(0x00);                        /* 產生 clock → 收到 B7..B0 */
    ADC_CS_HIGH();
    
    return (uint16_t)(((b2 & 0x0F) << 8) | b3);
}