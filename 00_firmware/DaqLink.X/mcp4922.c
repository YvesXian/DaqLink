#include "p32xxxx.h"
#include "mcp4922.h"

/* DAC CS = RE6 (active low) */
#define DAC_CS_MASK     (1u << 6)
#define DAC_CS_HIGH()   (LATESET = DAC_CS_MASK)
#define DAC_CS_LOW()    (LATECLR = DAC_CS_MASK)

void mcp4922_init(void)
{
    DAC_CS_HIGH();
    TRISECLR = DAC_CS_MASK;
}