/* 
 * File:   mcp3304.h
 * Author: user
 *
 * Created on 2026年9月23日, 下午 9:37
 */

#ifndef MCP3304_H
#define	MCP3304_H

#include <stdint.h>

void mcp3304_init(void);
uint16_t mcp3304_read(uint8_t ch);

#ifdef	__cplusplus
extern "C" {
#endif




#ifdef	__cplusplus
}
#endif

#endif	/* MCP3304_H */

