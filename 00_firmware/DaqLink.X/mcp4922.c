#include "p32xxxx.h"
#include "mcp4922.h"
#include "spi2.h"

/* DAC CS = RE6 (active low) */
#define DAC_CS_MASK     (1u << 6)
#define DAC_CS_HIGH()   (LATESET = DAC_CS_MASK)
#define DAC_CS_LOW()    (LATECLR = DAC_CS_MASK)

/* Write Command: A/B | BUF=0 | GA=1 (1x) | SHDN=1 (active) | D11..D0 */
#define MCP4922_CMD_A   0x3000u
#define MCP4922_CMD_B   0xB000u

void mcp4922_init(void)
{
    DAC_CS_HIGH();
    TRISECLR = DAC_CS_MASK;
}

void mcp4922_write(uint8_t ch, uint16_t code)
{
    uint16_t cmd;
    
    cmd = (ch == MCP4922_CH_B) ? MCP4922_CMD_B : MCP4922_CMD_A;
    cmd |= (code & 0x0FFF);
    
    DAC_CS_LOW();
    spi2_xfer((uint8_t)(cmd >> 8)); /* MSB first */
    spi2_xfer((uint8_t)(cmd & 0xFF));
    DAC_CS_HIGH();
}