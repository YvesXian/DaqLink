/* 
 * File:   mcp4922.h
 * Author: user
 *
 * Created on 2026年9月23日, 下午 9:37
 */

#ifndef MCP4922_H
#define	MCP4922_H

#define MCP4922_CH_A    0
#define MCP4922_CH_B    1

#include <stdint.h>

void mcp4922_init(void);
void mcp4922_write(uint8_t ch, uint16_t code);

#ifdef	__cplusplus
extern "C" {
#endif




#ifdef	__cplusplus
}
#endif

#endif	/* MCP4922_H */

