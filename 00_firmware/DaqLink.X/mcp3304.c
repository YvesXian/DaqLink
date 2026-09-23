#include "p32xxxx.h"
#include "mcp3304.h"

/* ADC CS = RA7 (active low) */
#define ADC_CS_MASK     (1u << 7)
#define ADC_CS_HIGH()   (LATASET = ADC_CS_MASK)
#define ADC_CS_LOW()    (LATACLR = ADC_CS_MASK)

void mcp3304_init(void)
{
    ADC_CS_HIGH();
    TRISACLR = ADC_CS_MASK;
}